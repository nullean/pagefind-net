# pagefind-net

A pure .NET library that generates [Pagefind](https://pagefind.app/)-compatible search indexes. No native binaries, no subprocess, no platform restrictions -- the entire indexing pipeline runs in managed code.

| Package | NuGet |
|---------|-------|
| **Pagefind.Net** | [![NuGet](https://img.shields.io/nuget/v/Pagefind.Net.svg)](https://www.nuget.org/packages/Pagefind.Net) |
| **Pagefind.Net.Frontend** | [![NuGet](https://img.shields.io/nuget/v/Pagefind.Net.Frontend.svg)](https://www.nuget.org/packages/Pagefind.Net.Frontend) |

## Quick start

Install both packages:

```shell
dotnet add package Pagefind.Net
dotnet add package Pagefind.Net.Frontend
```

Build an index and let the frontend runtime extract automatically:

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
        new WeightedSegment(h1Text, Weight: 7),
        new WeightedSegment(h2Text, Weight: 4),
        new WeightedSegment(bodyText, Weight: 1),
    ],
    Anchors =
    [
        new PagefindAnchor(ElementId: "intro", Text: "Introduction", ByteLocation: 0),
    ],
    Meta = new Dictionary<string, string>
    {
        ["title"] = "Getting started",
    },
});

await index.WriteAsync("wwwroot", CancellationToken.None);
```

`WriteAsync` emits the index data files (`pagefind-entry.json`, `*.pf_meta`, `index/*.pf_index`, `fragment/*.pf_fragment`) into `wwwroot/pagefind/`.

The **Pagefind.Net.Frontend** package ships `pagefind.js` and `wasm.en.pagefind` and automatically extracts them into `wwwroot/pagefind/` on build via an MSBuild target. No npm install required.

## Compatibility

Targets Pagefind version **1.5.2**. The index format is version-pinned; see `PagefindIndex.PagefindTargetVersion`.

## Documentation

Full documentation is available at [nullean.github.io/pagefind-net](https://nullean.github.io/pagefind-net/).

## License

MIT -- see [LICENSE.txt](LICENSE.txt). Third-party notices in [NOTICE.txt](NOTICE.txt).
