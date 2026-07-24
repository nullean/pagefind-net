# PagefindRecord

A single document to be added to the Pagefind index.

**Namespace:** `Pagefind.Net`

## Properties

```csharp
public sealed class PagefindRecord
{
    public required string Url { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public IReadOnlyList<WeightedSegment> WeightedSegments { get; init; } = [];
    public IReadOnlyList<PagefindAnchor> Anchors { get; init; } = [];
    public IReadOnlyDictionary<string, string> Meta { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Filters { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
}
```

| Property | Required | Description |
|---|---|---|
| `Url` | Yes | URL path of the page (e.g. `"/guide/"`). |
| `Title` | Yes | Page title, included in fragment metadata. |
| `Content` | Yes | Full markup-stripped plain-text body. Used for fragments and word count. |
| `WeightedSegments` | No | Text segments with search weights for heading boosting. |
| `Anchors` | No | Heading anchors for sub-page deep links. |
| `Meta` | No | Arbitrary key-value metadata (`title`, `breadcrumbs`, etc.). |
| `Filters` | No | Facet filter values. Reserved for future use. |

## WeightedSegment

A text segment with an associated search weight.

```csharp
public readonly record struct WeightedSegment(string Text, byte Weight);
```

| Parameter | Description |
|---|---|
| `Text` | Plain-text content of this segment. |
| `Weight` | Relative search weight. Convention: body = 1, H3 = 3, H2 = 4, H1 = 7. |

Terms appearing in higher-weight segments receive larger weight markers in the CBOR index, boosting their BM25 score at query time.

Always include at least one segment covering the full body text with weight 1. List heading segments before body segments so their positions are recorded first.

## PagefindAnchor

A heading anchor within a page, used to generate sub-results in the search UI.

```csharp
public readonly record struct PagefindAnchor(string ElementId, string Text, int ByteLocation);
```

| Parameter | Description |
|---|---|
| `ElementId` | The HTML element id of the heading (e.g. `"introduction"`). |
| `Text` | The visible text of the heading. |
| `ByteLocation` | Byte offset of this anchor's text within `Content` (UTF-8). |

The Pagefind JS runtime uses anchors to construct URLs with fragment identifiers (e.g. `/guide/#introduction`) and to highlight the relevant section in search results.

## Example

```csharp
var record = new PagefindRecord
{
    Url = "/reference/api/",
    Title = "API Reference",
    Content = "Complete reference for the public API. PagefindIndex is the main entry point.",
    WeightedSegments =
    [
        new WeightedSegment("API Reference", Weight: 7),
        new WeightedSegment("PagefindIndex Entry Point", Weight: 4),
        new WeightedSegment("Complete reference for the public API. PagefindIndex is the main entry point.", Weight: 1),
    ],
    Anchors =
    [
        new PagefindAnchor("overview", "Overview", 0),
        new PagefindAnchor("pagefind-index", "PagefindIndex", 42),
    ],
    Meta = new Dictionary<string, string>
    {
        ["title"] = "API Reference",
        ["breadcrumbs"] = "Docs > API",
    },
};
```
