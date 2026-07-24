# Pagefind.Net.Frontend

Ships the Pagefind browser query runtime (`pagefind.js` and `wasm.en.pagefind`) as embedded resources. An MSBuild target automatically extracts the files into your project on build -- no npm install required.

**Ships Pagefind 1.5.2 runtime.**

## Automatic extraction

Install the package and the frontend files appear in `wwwroot/pagefind/` after every build:

```shell
dotnet add package Pagefind.Net.Frontend
```

The output path is configurable in your `.csproj`:

```xml
<PropertyGroup>
  <PagefindFrontendOutputPath>wwwroot/pagefind</PagefindFrontendOutputPath>
</PropertyGroup>
```

To disable automatic extraction entirely:

```xml
<PropertyGroup>
  <PagefindFrontendDisableExtract>true</PagefindFrontendDisableExtract>
</PropertyGroup>
```

## Programmatic API

Extract the runtime files from code using `PagefindFrontend.ExtractToAsync`:

```csharp
using Pagefind.Net.Frontend;

string[] written = await PagefindFrontend.ExtractToAsync("wwwroot/pagefind");
```

The method writes files only when the embedded version is newer than what is already on disk (tracked via a version marker file). Pass `force: true` to always overwrite.

An overload accepting `IFileSystem` is available for testing.

## ASP.NET integration

ASP.NET does not recognize Pagefind's custom file extensions by default. Register them in your static file middleware:

```csharp
using Microsoft.AspNetCore.StaticFiles;

var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".pf_meta"] = "application/octet-stream";
contentTypeProvider.Mappings[".pf_index"] = "application/octet-stream";
contentTypeProvider.Mappings[".pf_fragment"] = "application/octet-stream";
contentTypeProvider.Mappings[".pagefind"] = "application/wasm";

app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypeProvider });
```

Without these mappings, the Pagefind JS runtime will receive 404 responses when loading index data.

## Documentation

Full documentation: [nullean.github.io/pagefind-net](https://nullean.github.io/pagefind-net/)
