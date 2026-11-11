---
name: pagefind-net
description: >-
  Use when .NET code needs client-side search for a static or server-rendered site: build a Pagefind search index
  from pages or records, in managed code with no Rust binary, Node or npm. Pagefind.Net writes the index files that
  the Pagefind 1.5.2 JavaScript runtime reads.
---

# Pagefind.Net

`using Pagefind.Net;` Build a `PagefindIndex`, add one record per page, then write it. It writes only the index
*data*; the browser runtime (`pagefind.js`, `wasm.en.pagefind`) comes from the `Pagefind.Net.Frontend` package.

From HTML you have already parsed (the weights then match the official Pagefind binary):

```csharp
var index = new PagefindIndex(new PagefindIndexOptions { Language = "en" });

index.AddHtmlRecord(new HtmlPageData
{
    Url = "/guide/",
    Sections =
    [
        new HtmlSection("h1", "Getting started", ElementId: "getting-started"),
        new HtmlSection("p", "Install the package and build an index."),
        new HtmlSection("h2", "Configuration", ElementId: "configuration"),
        new HtmlSection("p", "Options live on PagefindIndexOptions."),
    ],
    Meta = new Dictionary<string, string> { ["section"] = "Guides" }, // title defaults to the first h1
});

await index.WriteAsync("wwwroot", ct); // writes wwwroot/pagefind/...
```

Or a record you assemble yourself:

```csharp
index.AddRecord(new PagefindRecord
{
    Url = "/api/",
    Title = "API reference",
    Content = "API reference. PagefindIndex accumulates records.",   // plain text, no markup
    WeightedSegments =
    [
        new WeightedSegment("API reference", PagefindWeights.H1),
        new WeightedSegment("PagefindIndex accumulates records.", PagefindWeights.Body),
    ],
    Anchors = [new PagefindAnchor("api", "API reference", Location: 0, Tag: "h1")],
});
```

## Gotchas

- `WriteAsync(dir)` writes into `dir/pagefind/`: pass the site root (`wwwroot`), not `wwwroot/pagefind`.
- `WeightedSegment.Weight` is the raw stored weight, the heading level's weight times 24. Use the `PagefindWeights`
  constants (`H1` is 168, `Body` is 24); a small number like `7` ranks below body text.
- `PagefindAnchor.Location` is a word offset into `Content`, not a byte or character offset.
- `AddRecord` and `AddHtmlRecord` are thread-safe, but `WriteAsync` must not run concurrently with them. A failure
  while indexing a record throws `PagefindIndexingException` carrying its `Url` and `Title`.
- `Filters` is copied into the page fragment, but no filter index is written, so the runtime cannot filter on them.
- Pass an `IFileSystem` (System.IO.Abstractions) as the second constructor argument to redirect or sandbox writes.

Docs: https://pagefind.nullean.net
