namespace Pagefind.Net;

/// <summary>
/// Represents a pre-parsed HTML page for indexing. The caller is responsible
/// for extracting these components from their HTML (using any parser they
/// prefer). <see cref="PagefindIndex.AddHtmlRecord"/> converts this into a
/// <see cref="PagefindRecord"/> with weights matching the official Pagefind
/// binary.
/// </summary>
public sealed class HtmlPageData
{
	/// <summary>URL path of the page (e.g. <c>"/guide/"</c>).</summary>
	public required string Url { get; init; }

	/// <summary>
	/// Ordered content sections as they appear in the HTML document.
	/// Include headings and body text in document order. The heading levels
	/// determine search weight — matching the official Pagefind binary.
	/// </summary>
	public required IReadOnlyList<HtmlSection> Sections { get; init; }

	/// <summary>
	/// Arbitrary key-value metadata. The <c>title</c> key is populated
	/// automatically from the first H1 section if not provided.
	/// </summary>
	public IReadOnlyDictionary<string, string> Meta { get; init; } = new Dictionary<string, string>();

	/// <summary>
	/// Facet filter values for this page.
	/// </summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> Filters { get; init; } =
		new Dictionary<string, IReadOnlyList<string>>();
}

/// <summary>
/// A single section of an HTML page — either a heading or body text.
/// </summary>
/// <param name="Tag">
/// The HTML element tag: <c>"h1"</c> through <c>"h6"</c> for headings,
/// or any other value (e.g. <c>"p"</c>, <c>"div"</c>) for body text.
/// </param>
/// <param name="Text">Plain-text content of this section (markup-stripped).</param>
/// <param name="ElementId">
/// Optional HTML element <c>id</c> attribute. For headings, this generates
/// a sub-result anchor in the search UI.
/// </param>
public readonly record struct HtmlSection(string Tag, string Text, string? ElementId = null);

/// <summary>
/// Constants matching the official Pagefind binary's weight scheme.
/// </summary>
public static class PagefindWeights
{
	/// <summary>Multiplier applied to heading auto-weights.</summary>
	public const int Multiplier = 24;

	/// <summary>Maximum auto-weight value (before multiplication).</summary>
	public const int MaxAutoWeight = 10;

	/// <summary>Auto-weight for H1 headings.</summary>
	public const int H1AutoWeight = 7;

	/// <summary>Auto-weight for H2 headings.</summary>
	public const int H2AutoWeight = 6;

	/// <summary>Auto-weight for H3 headings.</summary>
	public const int H3AutoWeight = 5;

	/// <summary>Auto-weight for H4 headings.</summary>
	public const int H4AutoWeight = 4;

	/// <summary>Auto-weight for H5 headings.</summary>
	public const int H5AutoWeight = 3;

	/// <summary>Auto-weight for H6 headings.</summary>
	public const int H6AutoWeight = 2;

	/// <summary>Auto-weight for body text.</summary>
	public const int BodyAutoWeight = 1;

	/// <summary>Stored weight for H1 content: <c>7 * 24 = 168</c>.</summary>
	public const byte H1 = H1AutoWeight * Multiplier;

	/// <summary>Stored weight for H2 content: <c>6 * 24 = 144</c>.</summary>
	public const byte H2 = H2AutoWeight * Multiplier;

	/// <summary>Stored weight for H3 content: <c>5 * 24 = 120</c>.</summary>
	public const byte H3 = H3AutoWeight * Multiplier;

	/// <summary>Stored weight for H4 content: <c>4 * 24 = 96</c>.</summary>
	public const byte H4 = H4AutoWeight * Multiplier;

	/// <summary>Stored weight for H5 content: <c>3 * 24 = 72</c>.</summary>
	public const byte H5 = H5AutoWeight * Multiplier;

	/// <summary>Stored weight for H6 content: <c>2 * 24 = 48</c>.</summary>
	public const byte H6 = H6AutoWeight * Multiplier;

	/// <summary>Stored weight for body text: <c>1 * 24 = 24</c>.</summary>
	public const byte Body = BodyAutoWeight * Multiplier;

	/// <summary>
	/// Returns the stored weight for a heading tag, or <see cref="Body"/>
	/// for non-heading tags.
	/// </summary>
	public static byte ForTag(string tag) => tag.ToLowerInvariant() switch
	{
		"h1" => H1,
		"h2" => H2,
		"h3" => H3,
		"h4" => H4,
		"h5" => H5,
		"h6" => H6,
		_ => Body,
	};
}
