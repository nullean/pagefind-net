# Getting started

## Installation

Add both packages to your project:

```shell
dotnet add package Pagefind.Net
dotnet add package Pagefind.Net.Frontend
```

`Pagefind.Net` provides the indexer. `Pagefind.Net.Frontend` ships the browser query runtime and extracts it automatically on build.

## Building your first index

Create a `PagefindIndex`, add records, and write the output:

```csharp
using Pagefind.Net;

var index = new PagefindIndex(new PagefindIndexOptions { Language = "en" });

index.AddRecord(new PagefindRecord
{
    Url = "/hello/",
    Title = "Hello World",
    Content = "This is a searchable page about greeting the world.",
    WeightedSegments =
    [
        new WeightedSegment("Hello World", Weight: 7),
        new WeightedSegment("This is a searchable page about greeting the world.", Weight: 1),
    ],
    Meta = new Dictionary<string, string> { ["title"] = "Hello World" },
});

await index.WriteAsync("wwwroot");
```

This writes the following files into `wwwroot/pagefind/`:

| File pattern | Purpose |
|---|---|
| `pagefind-entry.json` | Entry point loaded by `pagefind.js` |
| `pagefind.{hash}.pf_meta` | Page metadata (titles, word counts) |
| `index/{hash}.pf_index` | Inverted index chunks |
| `fragment/{hash}.pf_fragment` | Content fragments for result excerpts |

## Weighted segments

Weighted segments control how different parts of a document affect search ranking. Higher weights boost terms in headings or keywords:

```csharp
WeightedSegments =
[
    new WeightedSegment(h1Text, Weight: 7),   // page title
    new WeightedSegment(h2Text, Weight: 4),   // section headings
    new WeightedSegment(bodyText, Weight: 1), // body content
],
```

Always include a weight-1 segment covering the full body text.

## Sub-page anchors

Anchors let search results link directly to a section within a page:

```csharp
Anchors =
[
    new PagefindAnchor(ElementId: "setup", Text: "Setup", ByteLocation: 0),
    new PagefindAnchor(ElementId: "config", Text: "Configuration", ByteLocation: 142),
],
```

The `ByteLocation` is the byte offset of the anchor text within `Content` (UTF-8 encoded).

## Next steps

- [Frontend package](frontend.md) -- how the JS/WASM runtime is delivered
- [ASP.NET integration](asp-net.md) -- serving a Pagefind index from ASP.NET
- [API reference](../reference/index.md) -- full type documentation
