namespace Pagefind.Net;

/// <summary>
/// A heading anchor within a page, used to generate sub-results in the search UI.
/// </summary>
/// <param name="ElementId">The HTML element id of the heading (e.g. <c>"introduction"</c>).</param>
/// <param name="Text">The visible text of the heading.</param>
/// <param name="ByteLocation">
/// Byte offset of this anchor's text within the page's <see cref="PagefindRecord.Content"/>.
/// Used by the Pagefind JS runtime to highlight and scroll to the relevant section.
/// </param>
public readonly record struct PagefindAnchor(string ElementId, string Text, int ByteLocation);
