namespace Pagefind.Net;

/// <summary>
/// A single document to be added to the Pagefind index.
/// </summary>
public sealed class PagefindRecord
{
	/// <summary>URL path of the page (e.g. <c>"/guide/"</c>).</summary>
	public required string Url { get; init; }

	/// <summary>Page title, included in fragment metadata.</summary>
	public required string Title { get; init; }

	/// <summary>
	/// Full markup-stripped plain-text body of the page.
	/// Used for the fragment's <c>content</c> and <c>word_count</c>.
	/// </summary>
	public required string Content { get; init; }

	/// <summary>
	/// Text segments with associated weights. Terms in higher-weight segments
	/// receive larger weight markers in the CBOR index, boosting their BM25 score.
	/// At minimum provide one segment covering the full body (weight = 1).
	/// Heading segments should be listed before body so their positions are recorded first.
	/// </summary>
	public IReadOnlyList<WeightedSegment> WeightedSegments { get; init; } = [];

	/// <summary>
	/// Heading anchors within this page. Each anchor generates a sub-result entry
	/// in the Pagefind JS runtime (url + "#elementid").
	/// </summary>
	public IReadOnlyList<PagefindAnchor> Anchors { get; init; } = [];

	/// <summary>
	/// Arbitrary key-value metadata included verbatim in the page fragment.
	/// The Pagefind JS frontend reads <c>meta.title</c> and <c>meta.breadcrumbs</c>
	/// (among others) from search results.
	/// </summary>
	public IReadOnlyDictionary<string, string> Meta { get; init; } = new Dictionary<string, string>();

	/// <summary>
	/// Facet filter values for this page. Deferred in the first release; pass an
	/// empty dictionary (the default). The filter data is omitted from the index.
	/// </summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> Filters { get; init; } =
		new Dictionary<string, IReadOnlyList<string>>();

	/// <summary>
	/// Optional per-position weight map. When provided, each word position in
	/// <see cref="Content"/> gets the weight from this array instead of the
	/// max-weight-per-word approach from <see cref="WeightedSegments"/>.
	/// Populated automatically by <see cref="PagefindIndex.AddHtmlRecord"/>.
	/// Index = word position in Content, value = weight for that position.
	/// </summary>
	internal byte[]? PositionWeights { get; init; }
}
