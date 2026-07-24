---
title: pagefind-net
---

# pagefind-net

A pure .NET library that generates [Pagefind](https://pagefind.app/)-compatible search indexes. No native binaries, no subprocess, no platform restrictions.

pagefind-net reimplements the Pagefind indexer in managed .NET code, producing the same `pagefind/` directory structure that the stock Pagefind JS/WASM query runtime consumes.

## Packages

**Pagefind.Net**
:   The indexer. Accepts documents as `PagefindRecord` instances and writes `pagefind-entry.json`, `.pf_meta`, `.pf_index`, and `.pf_fragment` files.

**Pagefind.Net.Frontend**
:   Ships the Pagefind browser runtime (`pagefind.js` + `wasm.en.pagefind`). An MSBuild target auto-extracts the files into your project on build.

## Features

- Pure managed .NET -- no native Rust binary, no Node.js dependency
- AOT-compatible and trimming-safe
- Zero-allocation tokeniser and stemmer hot paths
- Produces indexes byte-compatible with Pagefind 1.5.2
- MSBuild integration for automatic frontend extraction
- Programmatic `ExtractToAsync` API for the JS/WASM runtime
- Supports weighted segments for heading boosting and sub-page anchors

## Getting started

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
        new WeightedSegment(bodyText, Weight: 1),
    ],
});

await index.WriteAsync("wwwroot");
```

See [Getting started](getting-started/index.md) for installation and integration guides, or jump to the [API reference](api/index.md).
