# ASP.NET integration

pagefind-net works well with ASP.NET applications. Build the index at startup (or as a background task) and serve the `pagefind/` directory as static files.

## Complete example

```csharp
using Microsoft.AspNetCore.StaticFiles;
using Pagefind.Net;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Build the search index into wwwroot/pagefind/
var wwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var index = new PagefindIndex(new PagefindIndexOptions { Language = "en" });

foreach (var doc in LoadDocuments())
    index.AddRecord(doc);

await index.WriteAsync(wwwroot);

// Register custom MIME types for Pagefind file extensions
var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".pf_meta"] = "application/octet-stream";
contentTypeProvider.Mappings[".pf_index"] = "application/octet-stream";
contentTypeProvider.Mappings[".pf_fragment"] = "application/octet-stream";
contentTypeProvider.Mappings[".pagefind"] = "application/wasm";

app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypeProvider });
app.MapFallbackToFile("index.html");

app.Run();
```

## Custom MIME types

ASP.NET's static file middleware does not recognize Pagefind's file extensions by default. Without registering them, the browser receives 404 responses when `pagefind.js` tries to load index chunks.

The required mappings:

| Extension | Content type |
|---|---|
| `.pf_meta` | `application/octet-stream` |
| `.pf_index` | `application/octet-stream` |
| `.pf_fragment` | `application/octet-stream` |
| `.pagefind` | `application/wasm` |

## Frontend runtime

With `Pagefind.Net.Frontend` installed, the MSBuild target automatically copies `pagefind.js` and `wasm.en.pagefind` into `wwwroot/pagefind/` on every build. The project file just needs a package reference:

```xml
<ItemGroup>
  <PackageReference Include="Pagefind.Net" />
  <PackageReference Include="Pagefind.Net.Frontend" />
</ItemGroup>
```

The `.targets` file shipped in the `build/` and `buildTransitive/` folders of the NuGet package is imported automatically by MSBuild — no manual `<Import>` element is needed.

To customise the output directory, set the `PagefindFrontendOutputPath` property:

```xml
<PropertyGroup>
  <PagefindFrontendOutputPath>wwwroot/pagefind</PagefindFrontendOutputPath>
</PropertyGroup>
```

To disable automatic extraction entirely (e.g. in test projects):

```xml
<PropertyGroup>
  <PagefindFrontendDisableExtract>true</PagefindFrontendDisableExtract>
</PropertyGroup>
```

## HTML search page

Include a minimal search UI in your `wwwroot/index.html`. Note that `pagefind.js` is an ES module and must be loaded with `import()`:

```html
<!DOCTYPE html>
<html>
<head>
  <title>Search</title>
</head>
<body>
  <h1>Search</h1>
  <input type="text" id="search" placeholder="Search..." />
  <div id="results"></div>

  <script type="module">
    const pagefind = await import('/pagefind/pagefind.js');
    await pagefind.init();

    const input = document.getElementById('search');
    const results = document.getElementById('results');
    input.addEventListener('input', async () => {
      const { results: hits } = await pagefind.search(input.value);
      const data = await Promise.all(hits.slice(0, 10).map(r => r.data()));
      results.innerHTML = data.map(d =>
        `<a href="${d.url}">${d.meta.title}</a><p>${d.excerpt}</p>`
      ).join('');
    });
  </script>
</body>
</html>
```
