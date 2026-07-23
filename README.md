# pagefind-net

A native .NET library that generates a [Pagefind](https://pagefind.app/)-compatible search index.

pagefind-net reimplements the Pagefind **indexer** in .NET, producing the `pagefind/` index data files that the stock Pagefind JS/WASM query runtime can consume. No native binary, no subprocess, no platform restriction.

## Usage

```csharp
using Pagefind.Net;

var index = new PagefindIndex(new PagefindIndexOptions { Language = "en" });

index.AddRecord(new PagefindRecord
{
    Url = "/guide/",
    Title = "Getting started",
    Content = plainTextBody,
    WeightedSegments =
    [
        new WeightedSegment(h1Text, weight: 7),
        new WeightedSegment(h2Text, weight: 4),
        new WeightedSegment(bodyText, weight: 1),
    ],
    Anchors =
    [
        new PagefindAnchor(ElementId: "intro", Text: "Introduction", ByteLocation: 0),
    ],
    Meta = new Dictionary<string, string>
    {
        ["breadcrumbs"] = breadcrumbsJson,
    },
});

await index.WriteAsync(outputDirectory, cancellationToken);
```

`WriteAsync` emits the index **data** files only (`pagefind-entry.json`, `*.pf_meta`, `index/*.pf_index`, `fragment/*.pf_fragment`). The Pagefind query runtime (`pagefind.js`, `wasm.en.pagefind`) must be obtained separately from the [pagefind npm package](https://www.npmjs.com/package/pagefind).

## Compatibility

Targets Pagefind version **1.5.2**. The index format is version-pinned; see `PagefindIndex.PagefindTargetVersion`.

## License

MIT — see [LICENSE.txt](LICENSE.txt). Third-party notices in [NOTICE.txt](NOTICE.txt).
