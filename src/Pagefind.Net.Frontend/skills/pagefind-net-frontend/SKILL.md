---
name: pagefind-net-frontend
description: >-
  Use when a .NET web project serves a Pagefind search index and needs the browser runtime (pagefind.js and its
  wasm) without npm. Pagefind.Net.Frontend copies the Pagefind 1.5.2 runtime into wwwroot/pagefind on every build,
  or extracts it from code.
---

# Pagefind.Net.Frontend

Referencing the package is enough: after each build, `pagefind.js` and `wasm.en.pagefind` are in the project's
`wwwroot/pagefind/`, next to the index that `Pagefind.Net` writes there. Set MSBuild properties to change that:

```xml
<PropertyGroup>
  <PagefindFrontendOutputPath>public/pagefind</PagefindFrontendOutputPath> <!-- relative to the project -->
  <PagefindFrontendDisableExtract>true</PagefindFrontendDisableExtract>    <!-- no copy on build -->
</PropertyGroup>
```

To extract at run time instead (for example into a generated site's output folder):

```csharp
using Pagefind.Net.Frontend;

string[] written = await PagefindFrontend.ExtractToAsync("out/site/pagefind");          // [] when up to date
await PagefindFrontend.ExtractToAsync("out/site/pagefind", force: true, ct: ct);        // always overwrite
```

ASP.NET Core does not know Pagefind's file extensions, so map them or the runtime gets 404s:

```csharp
var types = new FileExtensionContentTypeProvider();
types.Mappings[".pf_meta"] = "application/octet-stream";
types.Mappings[".pf_index"] = "application/octet-stream";
types.Mappings[".pf_fragment"] = "application/octet-stream";
types.Mappings[".pagefind"] = "application/wasm";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = types });
```

## Gotchas

- Only the English wasm (`wasm.en.pagefind`) ships. An index built with another `Language` makes the runtime load
  `wasm.<language>.pagefind`, which this package does not provide.
- `ExtractToAsync` skips the copy when its `.pagefind-net-frontend-version` marker matches `PagefindFrontend.Version`
  and both files exist; delete the marker or pass `force: true` after replacing them by hand.
- Keep the runtime version (`PagefindFrontend.Version`) and the index version (`PagefindIndex.PagefindTargetVersion`)
  the same; the runtime checks the version the index declares.

Docs: https://pagefind.nullean.net
