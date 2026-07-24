# PagefindFrontend

Static class that extracts the embedded Pagefind browser runtime (`pagefind.js` and `wasm.en.pagefind`) to a directory on disk.

**Namespace:** `Pagefind.Net.Frontend`

## Constants

### Version

```csharp
public const string Version = "1.5.2";
```

The Pagefind version shipped in this package.

## Methods

### ExtractToAsync

```csharp
public static Task<string[]> ExtractToAsync(
    string outputDirectory,
    bool force = false,
    CancellationToken ct = default)
```

Extracts the frontend runtime files into `outputDirectory`. Returns the list of file paths that were written. Returns an empty array if all files are already up to date.

| Parameter | Description |
|---|---|
| `outputDirectory` | Target directory (e.g. `wwwroot/pagefind`). Created if it does not exist. |
| `force` | When `true`, always overwrite existing files. |
| `ct` | Cancellation token. |

:::{note}
A version marker file (`.pagefind-net-frontend-version`) is written alongside the assets. On subsequent calls, extraction is skipped when the marker matches the current package version and all expected files exist.
:::

### ExtractToAsync (IFileSystem overload)

```csharp
public static Task<string[]> ExtractToAsync(
    IFileSystem fs,
    string outputDirectory,
    bool force = false,
    CancellationToken ct = default)
```

Same behaviour as above, but uses the provided `IFileSystem` (from `System.IO.Abstractions`) for all file operations. Useful for unit testing.

## Example

```csharp
using Pagefind.Net.Frontend;

// Extract runtime files (skips if already current)
var files = await PagefindFrontend.ExtractToAsync("wwwroot/pagefind");
foreach (var file in files)
    Console.WriteLine($"Wrote: {file}");

// Force re-extraction
await PagefindFrontend.ExtractToAsync("wwwroot/pagefind", force: true);
```

## MSBuild integration

When installed as a NuGet package, `Pagefind.Net.Frontend` includes an MSBuild target that runs after `Build` and copies the runtime files automatically. See [Frontend package](../getting-started/frontend.md) for configuration options.
