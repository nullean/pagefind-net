using System.Formats.Cbor;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pagefind.Net;

/// <summary>
/// Encodes and writes Pagefind index data files.
/// Every file follows the framing contract: <c>gzip("pagefind_dcd" + body)</c>.
/// </summary>
internal static class PagefindWriter
{
	private static readonly byte[] Magic = "pagefind_dcd"u8.ToArray();

	// Target size before splitting into a new index chunk (uncompressed CBOR).
	private const int ChunkTargetBytes = 200_000;

	// ── Public helpers ─────────────────────────────────────────────────────────

	/// <summary>
	/// Writes a pagefind-framed gzip file: <c>gzip("pagefind_dcd" + body)</c>.
	/// </summary>
	internal static async Task WriteFramedAsync(IFileSystem fs, string path, byte[] body, CancellationToken ct)
	{
		await using var fileStream = fs.FileStream.New(path, FileMode.Create, FileAccess.Write,
			FileShare.None, 65536, FileOptions.Asynchronous);
		await using var gz = new GZipStream(fileStream, CompressionLevel.Optimal);
		await gz.WriteAsync(Magic, ct);
		await gz.WriteAsync(body, ct);
	}

	// ── Index chunks ───────────────────────────────────────────────────────────

	internal static async Task<IndexChunkInfo[]> WriteIndexChunksAsync(
		IFileSystem fs,
		string pagefindDir,
		SortedDictionary<string, List<PagePosting>> invertedIndex,
		Dictionary<string, int> fieldNameToId,
		CancellationToken ct)
	{
		if (invertedIndex.Count == 0)
			return [];

		var chunks = new List<IndexChunkInfo>();
		var wordBuffer = new List<KeyValuePair<string, List<PagePosting>>>();
		var bufferSize = 0;

		foreach (var entry in invertedIndex)
		{
			wordBuffer.Add(entry);
			bufferSize += EstimateWordSize(entry);

			if (bufferSize >= ChunkTargetBytes)
			{
				var chunk = await FlushChunkAsync(fs, pagefindDir, wordBuffer, fieldNameToId, ct);
				chunks.Add(chunk);
				wordBuffer.Clear();
				bufferSize = 0;
			}
		}

		if (wordBuffer.Count > 0)
		{
			var chunk = await FlushChunkAsync(fs, pagefindDir, wordBuffer, fieldNameToId, ct);
			chunks.Add(chunk);
		}

		return [.. chunks];
	}

	private static async Task<IndexChunkInfo> FlushChunkAsync(
		IFileSystem fs,
		string pagefindDir,
		List<KeyValuePair<string, List<PagePosting>>> words,
		Dictionary<string, int> fieldNameToId,
		CancellationToken ct)
	{
		var cbor = BuildIndexCbor(words, fieldNameToId);
		var hashBytes = SHA256.HashData(cbor);
		var hash = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();

		var path = Path.Combine(pagefindDir, "index", $"{hash}.pf_index");
		await WriteFramedAsync(fs, path, cbor, ct);

		return new IndexChunkInfo(
			From: words[0].Key,
			To: words[^1].Key,
			Hash: hash);
	}

	// ── CBOR: SearchIndex (.pf_index) ──────────────────────────────────────────

	/// <summary>
	/// Encodes one index shard into CBOR.
	/// Structure: outer array → words array (word, postings_flat, variants?).
	/// </summary>
	private static byte[] BuildIndexCbor(List<KeyValuePair<string, List<PagePosting>>> words, Dictionary<string, int> fieldNameToId)
	{
		var w = new CborWriter(CborConformanceMode.Lax);

		// Outer: array of 1 (the word list).
		w.WriteStartArray(1);
		// Word list: array of words.Count
		w.WriteStartArray(words.Count);

		foreach (var (word, postings) in words)
		{
			// Each word entry: [word, postings_array, variants]
			w.WriteStartArray(3);
			w.WriteTextString(word);

			// Postings: array of posting arrays, each [page_delta, locs_array, meta_locs_array].
			w.WriteStartArray(postings.Count);
			var prevPageId = 0;
			foreach (var posting in postings)
			{
				w.WriteStartArray(3);

				// Page id: delta from previous (first is absolute).
				var delta = posting.PageIndex - prevPageId;
				prevPageId = posting.PageIndex;
				w.WriteInt32(delta);

				// Locs: interleave weight markers and delta-encoded positions.
				var locs = BuildLocs(posting.Runs);
				w.WriteStartArray(locs.Length);
				foreach (var loc in locs)
					w.WriteInt32(loc);
				w.WriteEndArray();

				// Meta-locs: field-ID markers + delta-encoded positions.
				var metaLocs = BuildMetaLocs(posting.MetaRuns, fieldNameToId);
				w.WriteStartArray(metaLocs.Length);
				foreach (var ml in metaLocs)
					w.WriteInt32(ml);
				w.WriteEndArray();

				w.WriteEndArray(); // posting
			}
			w.WriteEndArray(); // postings_array

			// Variants: empty array.
			w.WriteStartArray(0);
			w.WriteEndArray();

			w.WriteEndArray(); // word entry
		}

		w.WriteEndArray(); // word list
		w.WriteEndArray(); // outer

		return w.Encode();
	}

	/// <summary>
	/// Encodes weight runs into the Pagefind locs int array.
	/// Flattens all runs into (weight, position) pairs, sorts by weight
	/// (ascending, matching pagefind's sort with weight 25 as sort key 0)
	/// then by position, and emits weight change markers + delta positions.
	/// </summary>
	private static int[] BuildLocs(List<WeightRun> runs)
	{
		// Flatten all runs into individual (weight, position) pairs and sort.
		var pairs = new List<(byte Weight, int Position)>();
		foreach (var run in runs)
			foreach (var pos in run.Positions)
				pairs.Add((run.Weight, pos));

		// Sort by weight (ascending, with 25 mapped to 0 for first position)
		// then by position — matching pagefind's positions_to_packed_page sort.
		pairs.Sort((a, b) =>
		{
			var wa = a.Weight == 25 ? 0 : a.Weight;
			var wb = b.Weight == 25 ? 0 : b.Weight;
			var cmp = wa.CompareTo(wb);
			return cmp != 0 ? cmp : a.Position.CompareTo(b.Position);
		});

		// Encode: current_weight starts at 25 (sentinel, matching pagefind).
		// Every weight change emits -(weight+1) marker + absolute position.
		// Same-weight positions emit delta from previous.
		var result = new List<int>(pairs.Count * 2);
		var currentWeight = (byte)25; // pagefind sentinel
		var lastPosition = 0;

		foreach (var (weight, position) in pairs)
		{
			if (weight != currentWeight)
			{
				result.Add(-(int)weight - 1);
				result.Add(position);
				lastPosition = position;
				currentWeight = weight;
			}
			else
			{
				result.Add(position - lastPosition);
				lastPosition = position;
			}
		}

		return [.. result];
	}

	/// <summary>
	/// Encodes meta-field runs into the meta_locs int array.
	/// Format: <c>[-(fieldId+1), pos_delta, ...]</c> for each field.
	/// Matches the official Pagefind binary's <c>meta_positions_to_packed</c>.
	/// </summary>
	private static int[] BuildMetaLocs(List<MetaFieldRun> runs, Dictionary<string, int> fieldNameToId)
	{
		if (runs.Count == 0)
			return [];

		// Sort by resolved global field ID, then positions within each field
		var sorted = runs.OrderBy(r => fieldNameToId[r.FieldName]).ToList();

		var totalLen = 0;
		foreach (var run in sorted)
			totalLen += 1 + run.Positions.Length;

		var result = new int[totalLen];
		var idx = 0;

		foreach (var run in sorted)
		{
			result[idx++] = -(fieldNameToId[run.FieldName] + 1);

			var prevPos = 0;
			foreach (var pos in run.Positions)
			{
				result[idx++] = pos - prevPos;
				prevPos = pos;
			}
		}
		return result;
	}

	private static int EstimateWordSize(KeyValuePair<string, List<PagePosting>> entry)
	{
		var size = entry.Key.Length;
		foreach (var p in entry.Value)
		{
			foreach (var r in p.Runs)
				size += (r.Positions.Length + 1) * 4;
			size += 8;
		}
		return size;
	}

	// ── CBOR: MetaIndex (.pf_meta) ─────────────────────────────────────────────

	/// <summary>
	/// Builds the MetaIndex CBOR blob.
	/// Array order: [version, pages[], index_chunks[], filter_chunks{}, sorts{}, meta_fields[]].
	/// </summary>
	internal static (string Hash, byte[] Cbor) BuildMetaCbor(
		string version,
		string[] pageHashes,
		int[] wordCounts,
		IndexChunkInfo[] indexChunks,
		string[] metaFields)
	{
		var w = new CborWriter(CborConformanceMode.Lax);

		// Outer 6-element array.
		w.WriteStartArray(6);

		// 1. generator_version
		w.WriteTextString(version);

		// 2. pages: [[hash, word_count], ...]
		w.WriteStartArray(pageHashes.Length);
		for (var i = 0; i < pageHashes.Length; i++)
		{
			w.WriteStartArray(2);
			w.WriteTextString(pageHashes[i]);
			w.WriteUInt32((uint)wordCounts[i]);
			w.WriteEndArray();
		}
		w.WriteEndArray();

		// 3. index_chunks: [[from, to, hash], ...]
		w.WriteStartArray(indexChunks.Length);
		foreach (var chunk in indexChunks)
		{
			w.WriteStartArray(3);
			w.WriteTextString(chunk.From);
			w.WriteTextString(chunk.To);
			w.WriteTextString(chunk.Hash);
			w.WriteEndArray();
		}
		w.WriteEndArray();

		// 4. filter_chunks: empty array (matches pagefind binary output when no filters)
		w.WriteStartArray(0);
		w.WriteEndArray();

		// 5. sorts: empty array (matches pagefind binary output when no sort keys)
		w.WriteStartArray(0);
		w.WriteEndArray();

		// 6. meta_fields: all indexed meta field names (sorted alphabetically)
		w.WriteStartArray(metaFields.Length);
		foreach (var field in metaFields)
			w.WriteTextString(field);
		w.WriteEndArray();

		w.WriteEndArray(); // outer

		var cbor = w.Encode();
		var hashBytes = SHA256.HashData(cbor);
		var hash = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
		return (hash, cbor);
	}

	// ── pagefind-entry.json ────────────────────────────────────────────────────

	/// <summary>
	/// Pagefind's default connector characters (Unicode "Pc" category).
	/// These are always included in the <c>include_characters</c> array in
	/// <c>pagefind-entry.json</c>, matching the official binary's behaviour.
	/// </summary>
	internal static readonly string[] DefaultConnectorCharacters =
		["_", "\u203F", "\u2040", "\u2054", "\uFE33", "\uFE34", "\uFE4D", "\uFE4E", "\uFE4F", "\uFF3F"];

	internal static async Task WriteEntryJsonAsync(
		IFileSystem fs,
		string pagefindDir,
		string language,
		string metaHash,
		int pageCount,
		string includeCharacters,
		CancellationToken ct)
	{
		var chars = BuildIncludeCharactersArray(includeCharacters);

		var entry = new EntryJson
		{
			Version = PagefindIndex.PagefindTargetVersion,
			Languages = new Dictionary<string, LanguageEntry>
			{
				[language] = new LanguageEntry
				{
					Hash = metaHash,
					Wasm = language,   // pagefind.js builds: wasm.${wasm}.pagefind
					PageCount = pageCount,
				},
			},
			IncludeCharacters = chars,
		};

		var json = JsonSerializer.SerializeToUtf8Bytes(entry, EntrySerializerContext.Default.EntryJson);
		var path = Path.Combine(pagefindDir, "pagefind-entry.json");
		await fs.File.WriteAllBytesAsync(path, json, ct);
	}

	/// <summary>
	/// Builds the <c>include_characters</c> array for the entry JSON.
	/// Merges the user-supplied characters with the default connector set,
	/// emitting each character as a separate single-char string — matching
	/// the official Pagefind binary output format.
	/// </summary>
	private static string[] BuildIncludeCharactersArray(string includeCharacters)
	{
		if (string.IsNullOrEmpty(includeCharacters))
			return DefaultConnectorCharacters;

		var set = new HashSet<string>(DefaultConnectorCharacters);
		foreach (var c in includeCharacters)
			set.Add(c.ToString());
		return [.. set];
	}
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

internal record IndexChunkInfo(string From, string To, string Hash);

internal sealed class EntryJson
{
	[JsonPropertyName("version")]
	public required string Version { get; init; }

	[JsonPropertyName("languages")]
	public required Dictionary<string, LanguageEntry> Languages { get; init; }

	[JsonPropertyName("include_characters")]
	public required string[] IncludeCharacters { get; init; }
}

internal sealed class LanguageEntry
{
	[JsonPropertyName("hash")]
	public required string Hash { get; init; }

	[JsonPropertyName("wasm")]
	public required string Wasm { get; init; }

	[JsonPropertyName("page_count")]
	public required int PageCount { get; init; }
}

[JsonSerializable(typeof(EntryJson))]
[JsonSerializable(typeof(LanguageEntry))]
[JsonSerializable(typeof(Dictionary<string, LanguageEntry>))]
[JsonSerializable(typeof(string[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class EntrySerializerContext : JsonSerializerContext;
