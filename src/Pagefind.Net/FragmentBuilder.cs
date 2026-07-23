using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pagefind.Net;

/// <summary>
/// Serialises a <see cref="PagefindRecord"/> to a Pagefind
/// <c>PageFragmentData</c> JSON blob and returns the content hash.
/// </summary>
internal sealed class FragmentBuilder
{
	/// <summary>
	/// Builds the JSON fragment for <paramref name="record"/>.
	/// </summary>
	/// <returns>
	/// A tuple of (content-hash-hex-prefix, raw JSON bytes).
	/// The hash is SHA-256 of the JSON bytes, first 8 hex chars.
	/// </returns>
	internal (string Hash, byte[] JsonBytes) BuildFragment(PagefindRecord record)
	{
		var fragment = new PageFragmentData
		{
			Url = record.Url,
			Content = record.Content,
			WordCount = CountWords(record.Content),
			Filters = new Dictionary<string, IReadOnlyList<string>>(record.Filters),
			Meta = BuildMeta(record),
			Anchors = record.Anchors
				.Select(a => new FragmentAnchor
				{
					Element = a.ElementId,
					Text = a.Text,
					Location = a.ByteLocation,
				})
				.ToArray(),
		};

		// Use source-gen overload for AOT/trim safety.
		var json = JsonSerializer.SerializeToUtf8Bytes(
			fragment,
			FragmentSerializerContext.Default.PageFragmentData);

		var hashBytes = SHA256.HashData(json);
		var hash = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
		return (hash, json);
	}

	private static Dictionary<string, string> BuildMeta(PagefindRecord record)
	{
		var meta = new Dictionary<string, string>(record.Meta) { ["title"] = record.Title };
		return meta;
	}

	private static int CountWords(string content)
	{
		if (string.IsNullOrWhiteSpace(content))
			return 0;
		var count = 0;
		var inWord = false;
		foreach (var c in content)
		{
			if (char.IsWhiteSpace(c)) inWord = false;
			else if (!inWord) { inWord = true; count++; }
		}
		return count;
	}
}

// ── JSON DTOs ─────────────────────────────────────────────────────────────────

internal sealed class PageFragmentData
{
	[JsonPropertyName("url")]
	public required string Url { get; init; }

	[JsonPropertyName("content")]
	public required string Content { get; init; }

	[JsonPropertyName("word_count")]
	public required int WordCount { get; init; }

	[JsonPropertyName("filters")]
	public required Dictionary<string, IReadOnlyList<string>> Filters { get; init; }

	[JsonPropertyName("meta")]
	public required Dictionary<string, string> Meta { get; init; }

	[JsonPropertyName("anchors")]
	public required FragmentAnchor[] Anchors { get; init; }
}

internal sealed class FragmentAnchor
{
	[JsonPropertyName("element")]
	public required string Element { get; init; }

	[JsonPropertyName("text")]
	public required string Text { get; init; }

	[JsonPropertyName("location")]
	public required int Location { get; init; }
}

[JsonSerializable(typeof(PageFragmentData))]
[JsonSerializable(typeof(FragmentAnchor[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, IReadOnlyList<string>>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class FragmentSerializerContext : JsonSerializerContext;
