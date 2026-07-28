using System.Formats.Cbor;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pagefind.Net.Tests;

/// <summary>
/// Decodes Pagefind binary files (.pf_index, .pf_meta, .pf_fragment) into
/// comparable C# structures. Used by parity tests to compare .NET output
/// against official Pagefind binary output.
/// </summary>
internal static class IndexDecoder
{
	private const int MagicLength = 12; // "pagefind_dcd"

	/// <summary>
	/// Reads a framed pagefind file: gunzip → skip 12-byte magic → return raw bytes.
	/// </summary>
	internal static byte[] ReadFramedBytes(string path)
	{
		using var fs = File.OpenRead(path);
		using var gz = new GZipStream(fs, CompressionMode.Decompress);
		using var ms = new MemoryStream();
		gz.CopyTo(ms);
		return ms.ToArray()[MagicLength..];
	}

	/// <summary>
	/// Decodes a .pf_fragment file into its JSON string.
	/// </summary>
	internal static string ReadFragmentJson(string path)
	{
		var bytes = ReadFramedBytes(path);
		return Encoding.UTF8.GetString(bytes);
	}

	/// <summary>
	/// Decodes a .pf_fragment file into a <see cref="DecodedFragment"/>.
	/// </summary>
	internal static DecodedFragment ReadFragment(string path)
	{
		var json = ReadFragmentJson(path);
		return JsonSerializer.Deserialize(json, DecoderSerializerContext.Default.DecodedFragment)!;
	}

	/// <summary>
	/// Decodes a .pf_index file into a dictionary of word → postings.
	/// </summary>
	internal static Dictionary<string, DecodedWordEntry> ReadIndex(string path)
	{
		var bytes = ReadFramedBytes(path);
		var reader = new CborReader(bytes, CborConformanceMode.Lax);

		var result = new Dictionary<string, DecodedWordEntry>(StringComparer.Ordinal);

		reader.ReadStartArray(); // outer array
		var wordCount = reader.ReadStartArray(); // word list array

		for (var i = 0; i < wordCount; i++)
		{
			reader.ReadStartArray(); // word entry: [word, postings, variants]

			var word = reader.ReadTextString();

			// Postings array
			var postingCount = reader.ReadStartArray();
			var postings = new List<DecodedPosting>();
			var prevPage = 0;

			for (var j = 0; j < postingCount; j++)
			{
				reader.ReadStartArray(); // [page_delta, locs, meta_locs]

				var pageDelta = reader.ReadInt32();
				prevPage += pageDelta;

				var locCount = reader.ReadStartArray();
				var locs = new int[locCount!.Value];
				for (var k = 0; k < locCount; k++)
					locs[k] = reader.ReadInt32();
				reader.ReadEndArray();

				var metaLocCount = reader.ReadStartArray();
				var metaLocs = new int[metaLocCount!.Value];
				for (var k = 0; k < metaLocCount; k++)
					metaLocs[k] = reader.ReadInt32();
				reader.ReadEndArray();

				reader.ReadEndArray(); // posting

				postings.Add(new DecodedPosting(prevPage, locs, metaLocs));
			}
			reader.ReadEndArray(); // postings array

			// Variants array
			var variantCount = reader.ReadStartArray();
			var variants = new string[variantCount!.Value];
			for (var k = 0; k < variantCount; k++)
				variants[k] = reader.ReadTextString();
			reader.ReadEndArray();

			reader.ReadEndArray(); // word entry

			result[word] = new DecodedWordEntry(postings, variants);
		}

		reader.ReadEndArray(); // word list
		reader.ReadEndArray(); // outer

		return result;
	}

	/// <summary>
	/// Reads all .pf_index files from a directory and merges them into a single dictionary.
	/// </summary>
	internal static Dictionary<string, DecodedWordEntry> ReadAllIndexChunks(string indexDir)
	{
		var result = new Dictionary<string, DecodedWordEntry>(StringComparer.Ordinal);
		if (!Directory.Exists(indexDir))
			return result;

		foreach (var file in Directory.GetFiles(indexDir, "*.pf_index"))
		{
			var chunk = ReadIndex(file);
			foreach (var (word, entry) in chunk)
				result[word] = entry;
		}
		return result;
	}

	/// <summary>
	/// Decodes a .pf_meta file into a <see cref="DecodedMeta"/>.
	/// </summary>
	internal static DecodedMeta ReadMeta(string path)
	{
		var bytes = ReadFramedBytes(path);
		var reader = new CborReader(bytes, CborConformanceMode.Lax);

		reader.ReadStartArray(); // outer 6-element array

		var version = reader.ReadTextString();

		// Pages: [[hash, word_count], ...]
		var pageCount = reader.ReadStartArray();
		var pages = new List<DecodedMetaPage>();
		for (var i = 0; i < pageCount; i++)
		{
			reader.ReadStartArray();
			var hash = reader.ReadTextString();
			var wordCount = reader.ReadInt32();
			reader.ReadEndArray();
			pages.Add(new DecodedMetaPage(hash, wordCount));
		}
		reader.ReadEndArray();

		// Index chunks: [[from, to, hash], ...]
		var chunkCount = reader.ReadStartArray();
		var chunks = new List<DecodedMetaChunk>();
		for (var i = 0; i < chunkCount; i++)
		{
			reader.ReadStartArray();
			var from = reader.ReadTextString();
			var to = reader.ReadTextString();
			var hash = reader.ReadTextString();
			reader.ReadEndArray();
			chunks.Add(new DecodedMetaChunk(from, to, hash));
		}
		reader.ReadEndArray();

		// Skip filter_chunks, sorts, meta_fields for now
		SkipCborValue(reader); // filter_chunks
		SkipCborValue(reader); // sorts

		// Meta fields
		var fieldCount = reader.ReadStartArray();
		var metaFields = new List<string>();
		for (var i = 0; i < fieldCount; i++)
			metaFields.Add(reader.ReadTextString());
		reader.ReadEndArray();

		reader.ReadEndArray(); // outer

		return new DecodedMeta(version, pages, chunks, metaFields);
	}

	/// <summary>
	/// Finds the single .pf_meta file in a pagefind output directory.
	/// </summary>
	internal static string? FindMetaFile(string pagefindDir) =>
		Directory.GetFiles(pagefindDir, "*.pf_meta").FirstOrDefault();

	/// <summary>
	/// Reads all .pf_fragment files from a directory into decoded fragments, sorted by URL.
	/// </summary>
	internal static List<DecodedFragment> ReadAllFragments(string fragmentDir)
	{
		var fragments = new List<DecodedFragment>();
		if (!Directory.Exists(fragmentDir))
			return fragments;

		foreach (var file in Directory.GetFiles(fragmentDir, "*.pf_fragment"))
			fragments.Add(ReadFragment(file));

		fragments.Sort((a, b) => string.Compare(a.Url, b.Url, StringComparison.Ordinal));
		return fragments;
	}

	private static void SkipCborValue(CborReader reader)
	{
		reader.SkipValue();
	}
}

// ── Decoded DTOs ───────────────────────────────────────────────────────────────

internal sealed record DecodedPosting(int PageIndex, int[] Locs, int[] MetaLocs);

internal sealed record DecodedWordEntry(List<DecodedPosting> Postings, string[] Variants);

internal sealed record DecodedMetaPage(string Hash, int WordCount);

internal sealed record DecodedMetaChunk(string From, string To, string Hash);

internal sealed record DecodedMeta(
	string Version,
	List<DecodedMetaPage> Pages,
	List<DecodedMetaChunk> Chunks,
	List<string> MetaFields);

internal sealed class DecodedFragment
{
	[JsonPropertyName("url")]
	public string Url { get; set; } = "";

	[JsonPropertyName("content")]
	public string Content { get; set; } = "";

	[JsonPropertyName("word_count")]
	public int WordCount { get; set; }

	[JsonPropertyName("filters")]
	public Dictionary<string, List<string>> Filters { get; set; } = new();

	[JsonPropertyName("meta")]
	public Dictionary<string, string> Meta { get; set; } = new();

	[JsonPropertyName("anchors")]
	public List<DecodedFragmentAnchor> Anchors { get; set; } = [];
}

internal sealed class DecodedFragmentAnchor
{
	[JsonPropertyName("element")]
	public string Element { get; set; } = "";

	[JsonPropertyName("id")]
	public string? Id { get; set; }

	[JsonPropertyName("text")]
	public string Text { get; set; } = "";

	[JsonPropertyName("location")]
	public int Location { get; set; }
}

[JsonSerializable(typeof(DecodedFragment))]
[JsonSerializable(typeof(DecodedFragmentAnchor))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(List<DecodedFragmentAnchor>))]
[JsonSerializable(typeof(List<DecodedFragment>))]
internal partial class DecoderSerializerContext : JsonSerializerContext;
