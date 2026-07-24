---
title: PagefindIndex
---

# PagefindIndex

`PagefindIndex` accumulates `PagefindRecord` documents and writes a Pagefind-compatible index directory.

**Namespace:** `Pagefind.Net`

## Constructor

```csharp
public PagefindIndex(PagefindIndexOptions? options = null, IFileSystem? fileSystem = null)
```

| Parameter | Description |
|---|---|
| `options` | Index configuration. Defaults to English language with no extra include characters. |
| `fileSystem` | File system abstraction from `System.IO.Abstractions`. Pass `null` to use the real file system. |

## Properties

### PagefindTargetVersion

```csharp
public const string PagefindTargetVersion = "1.5.2";
```

The Pagefind release version this library targets. The emitted `pagefind-entry.json` declares this version so the JS runtime can verify compatibility.

## Methods

### AddRecord

```csharp
public void AddRecord(PagefindRecord record)
```

Adds a document to the index. Call before `WriteAsync`.

### WriteAsync

```csharp
public async Task WriteAsync(string outputDirectory, CancellationToken ct = default)
```

Builds the full index and writes all data files to `{outputDirectory}/pagefind/`.

Output files:
- `pagefind-entry.json` -- entry point for the JS runtime
- `pagefind.{hash}.pf_meta` -- page metadata (CBOR, framed)
- `index/{hash}.pf_index` -- inverted index chunks (CBOR, framed)
- `fragment/{hash}.pf_fragment` -- content fragments for excerpts (CBOR, framed)

The `pagefind/` subdirectory is created if it does not exist.

## PagefindIndexOptions

```csharp
public sealed class PagefindIndexOptions
{
    public string Language { get; init; } = "en";
    public string IncludeCharacters { get; init; } = "";
}
```

| Property | Default | Description |
|---|---|---|
| `Language` | `"en"` | BCP-47 language tag for stemming and the wasm file reference. |
| `IncludeCharacters` | `""` | Additional Unicode characters to preserve during tokenisation. |

## Example

```csharp
using Pagefind.Net;

var index = new PagefindIndex(new PagefindIndexOptions
{
    Language = "en",
    IncludeCharacters = "#",
});

index.AddRecord(new PagefindRecord
{
    Url = "/docs/csharp/",
    Title = "C# Guide",
    Content = "Learn about C# features including async/await and pattern matching.",
    WeightedSegments =
    [
        new WeightedSegment("C# Guide", Weight: 7),
        new WeightedSegment("Learn about C# features including async/await and pattern matching.", Weight: 1),
    ],
    Meta = new Dictionary<string, string> { ["title"] = "C# Guide" },
});

await index.WriteAsync("output");
```
