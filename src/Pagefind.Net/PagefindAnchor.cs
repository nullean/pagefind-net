namespace Pagefind.Net;

/// <summary>
/// A heading anchor within a page, used to generate sub-results in the search UI.
/// </summary>
/// <param name="ElementId">The HTML element id of the heading (e.g. <c>"introduction"</c>).</param>
/// <param name="Text">The visible text of the heading.</param>
/// <param name="Location">
/// Word offset of this anchor within the page's <see cref="PagefindRecord.Content"/>.
/// Used by the Pagefind JS runtime to highlight and scroll to the relevant section.
/// </param>
/// <param name="Tag">
/// The HTML element tag name (e.g. <c>"h2"</c>). Used in the fragment JSON
/// <c>element</c> field. Defaults to <c>"a"</c>.
/// </param>
public readonly record struct PagefindAnchor(string ElementId, string Text, int Location, string Tag = "a");
