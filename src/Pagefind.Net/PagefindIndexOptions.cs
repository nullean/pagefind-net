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
}
