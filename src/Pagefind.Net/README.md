# Pagefind.Net

A pure .NET Pagefind indexer that generates a [Pagefind](https://pagefind.app/)-compatible search index. No native Rust binary or subprocess required -- the entire indexing pipeline runs in managed code and is AOT-compatible.

## Usage

```csharp
using Pagefind.Net;

var index = new PagefindIndex(new PagefindIndexOptions { Language = "en" });

index.AddRecord(new PagefindRecord
{
    Url = "/guide/",
    Title = "Getting started",
    Content = "Full plain-text body of the page...",
    WeightedSegments =
    [
        new WeightedSegment("Getting started", Weight: 7),
        new WeightedSegment("Full plain-text body of the page...", Weight: 1),
    ],
    Anchors =
    [
        new PagefindAnchor(ElementId: "setup", Text: "Setup", ByteLocation: 0),
    ],
    Meta = new Dictionary<string, string> { ["title"] = "Getting started" },
});

await index.WriteAsync("wwwroot", CancellationToken.None);
```

`WriteAsync` writes all index data files into `wwwroot/pagefind/`:
- `pagefind-entry.json`
- `pagefind.{hash}.pf_meta`
- `index/{hash}.pf_index`
- `fragment/{hash}.pf_fragment`

## Key types

| Type | Description |
|------|-------------|
| `PagefindIndex` | Accumulates records and writes the index via `WriteAsync`. |
| `PagefindRecord` | A single searchable document with URL, title, content, segments, anchors, and metadata. |
| `WeightedSegment` | A text segment with a search weight (body = 1, H2 = 4, H1 = 7). |
| `PagefindAnchor` | A heading anchor for sub-page deep links in search results. |
| `PagefindIndexOptions` | Configuration for language and tokenisation behaviour. |

## Frontend runtime

This package writes only the index **data** files. The Pagefind query runtime (`pagefind.js` and `wasm.en.pagefind`) must be provided separately. You have two options:

- Install the [Pagefind.Net.Frontend](https://www.nuget.org/packages/Pagefind.Net.Frontend) NuGet package, which ships the runtime and auto-extracts it on build.
- Install the [`pagefind`](https://www.npmjs.com/package/pagefind) npm package and copy the runtime files yourself.

## Documentation

Full documentation: [nullean.github.io/pagefind-net](https://nullean.github.io/pagefind-net/)
