# Frontend package

The `Pagefind.Net.Frontend` NuGet package ships the Pagefind browser query runtime: `pagefind.js` and `wasm.en.pagefind`. These files are required for the search UI to work in the browser.

## MSBuild auto-extraction

When you install the package, an MSBuild target runs after every build and copies the runtime files into your project:

```
wwwroot/pagefind/
  pagefind.js
  wasm.en.pagefind
```

:::{tip}
No configuration is needed for the default setup. The index data files written by `PagefindIndex.WriteAsync` go into the same directory.
:::

### Changing the output path

Set `PagefindFrontendOutputPath` in your `.csproj`:

```xml
<PropertyGroup>
  <PagefindFrontendOutputPath>wwwroot/_search</PagefindFrontendOutputPath>
</PropertyGroup>
```

### Disabling auto-extraction

If you prefer to manage runtime files yourself:

```xml
<PropertyGroup>
  <PagefindFrontendDisableExtract>true</PagefindFrontendDisableExtract>
</PropertyGroup>
```

## Programmatic API

The `PagefindFrontend` static class provides an `ExtractToAsync` method for extracting files from code:

```csharp
using Pagefind.Net.Frontend;

// Extract to a directory (skips if already up to date)
string[] written = await PagefindFrontend.ExtractToAsync("wwwroot/pagefind");

// Force overwrite
string[] written = await PagefindFrontend.ExtractToAsync("wwwroot/pagefind", force: true);
```

:::{note}
A version marker file (`.pagefind-net-frontend-version`) is written alongside the assets. On subsequent calls, extraction is skipped if the marker matches the current package version and all expected files exist.
:::

An overload accepting `IFileSystem` (from `System.IO.Abstractions`) is available for unit testing.

## Version

This package ships the runtime from Pagefind **1.5.2**. The version is available programmatically:

```csharp
Console.WriteLine(PagefindFrontend.Version); // "1.5.2"
```
