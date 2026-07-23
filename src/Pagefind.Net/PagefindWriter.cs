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
				var chunk = await FlushChunkAsync(fs, pagefindDir, wordBuffer, ct);
				chunks.Add(chunk);
				wordBuffer.Clear();
				bufferSize = 0;
			}
		}

		if (wordBuffer.Count > 0)
		{
			var chunk = await FlushChunkAsync(fs, pagefindDir, wordBuffer, ct);
			chunks.Add(chunk);
		}

		return [.. chunks];
	}

	private static async Task<IndexChunkInfo> FlushChunkAsync(
		IFileSystem fs,
		string pagefindDir,
		List<KeyValuePair<string, List<PagePosting>>> words,
		CancellationToken ct)
	{
		var cbor = BuildIndexCbor(words);
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
	private static byte[] BuildIndexCbor(List<KeyValuePair<string, List<PagePosting>>> words)
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

				// Meta-locs: empty for this implementation.
				w.WriteStartArray(0);
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
	/// For each run: -weight (marker), then delta-encoded positions.
	/// </summary>
	private static int[] BuildLocs(WeightRun[] runs)
	{
		var result = new List<int>();
		foreach (var run in runs)
		{
			// Negative weight marker (abs value = weight, clamped to 0..255).
			var weight = (int)Math.Clamp(run.Weight, (byte)1, (byte)255);
			result.Add(-weight);

			// Delta-encode positions within this run.
			var prevPos = 0;
			foreach (var pos in run.Positions)
			{
				result.Add(pos - prevPos);
				prevPos = pos;
			}
		}
		return [.. result];
	}

	private static int EstimateWordSize(KeyValuePair<string, List<PagePosting>> entry)
	{
		// Rough estimate: word length + (runs * avg positions) * 4 bytes per int.
		var size = entry.Key.Length;
		foreach (var p in entry.Value)
			size += p.Runs.Sum(r => r.Positions.Length + 1) * 4 + 8;
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
		IndexChunkInfo[] indexChunks)
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

		// 6. meta_fields: include "title" (matches pagefind binary output)
		w.WriteStartArray(1);
		w.WriteTextString("title");
		w.WriteEndArray();

		w.WriteEndArray(); // outer

		var cbor = w.Encode();
		var hashBytes = SHA256.HashData(cbor);
		var hash = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
		return (hash, cbor);
	}

	// ── pagefind-entry.json ────────────────────────────────────────────────────

	internal static async Task WriteEntryJsonAsync(
		IFileSystem fs,
		string pagefindDir,
		string language,
		string metaHash,
		int pageCount,
		string includeCharacters,
		CancellationToken ct)
	{
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
			IncludeCharacters = includeCharacters,
		};

		var json = JsonSerializer.SerializeToUtf8Bytes(entry, EntrySerializerContext.Default.EntryJson);
		var path = Path.Combine(pagefindDir, "pagefind-entry.json");
		await fs.File.WriteAllBytesAsync(path, json, ct);
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
	public required string IncludeCharacters { get; init; }
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
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class EntrySerializerContext : JsonSerializerContext;
