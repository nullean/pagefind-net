namespace Pagefind.Net;

/// <summary>
/// Options for configuring a <see cref="PagefindIndex"/>.
/// </summary>
public sealed class PagefindIndexOptions
{
	/// <summary>
	/// BCP-47 language tag used for the index entry and wasm file reference.
	/// Defaults to <c>"en"</c>. Only whitespace/Snowball-segmented languages
	/// are supported in the first release; CJK/Thai fall back to "unknown".
	/// </summary>
	public string Language { get; init; } = "en";

	/// <summary>
	/// Additional Unicode characters to keep during tokenisation (appended to
	/// Pagefind's default include-character set). Empty string = default behaviour.
	/// </summary>
	public string IncludeCharacters { get; init; } = "";

	/// <summary>
	/// Number of tokenized records to accumulate before merging them into the
	/// inverted index. Higher values reduce lock contention under concurrent
	/// <see cref="PagefindIndex.AddRecord"/> calls at the cost of holding more
	/// pending results in memory. Set to <c>1</c> to merge on every call.
	/// Defaults to <c>500</c>.
	/// </summary>
	public int MergeBatchSize { get; init; } = 500;
}
