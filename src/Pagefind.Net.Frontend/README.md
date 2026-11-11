# Pagefind.Net.Frontend

![pagefind.net](https://raw.githubusercontent.com/nullean/pagefind-net/main/docs/images/png/pagefind-net-mark-180.png)

Ships the Pagefind 1.5.2 browser query runtime (`pagefind.js` and `wasm.en.pagefind`) for indexes written by Pagefind.Net. An MSBuild target extracts the files into your project on build: no npm install required.

[![NuGet](https://img.shields.io/nuget/v/Pagefind.Net.Frontend?style=flat-square)](https://www.nuget.org/packages/Pagefind.Net.Frontend)

## Install

```sh
dotnet add package Pagefind.Net.Frontend
```

## Quick start

Install the package and the frontend files appear in `wwwroot/pagefind/` after every build. The output path is
configurable in your `.csproj`, and extraction can be turned off:

```xml
<PropertyGroup>
  <PagefindFrontendOutputPath>wwwroot/pagefind</PagefindFrontendOutputPath>
  <!-- <PagefindFrontendDisableExtract>true</PagefindFrontendDisableExtract> -->
</PropertyGroup>
```

Or extract from code:

```csharp
using Pagefind.Net.Frontend;

// Writes pagefind.js and wasm.en.pagefind, unless the same version is already there (force: true overwrites).
string[] written = await PagefindFrontend.ExtractToAsync("wwwroot/pagefind");
```

An overload accepting `IFileSystem` is available for testing.

## ASP.NET

ASP.NET does not recognise Pagefind's file extensions. Register them in your static file middleware, or the Pagefind
runtime gets 404 responses when loading index data:

```csharp
using Microsoft.AspNetCore.StaticFiles;

var app = WebApplication.CreateBuilder(args).Build();

var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".pf_meta"] = "application/octet-stream";
contentTypeProvider.Mappings[".pf_index"] = "application/octet-stream";
contentTypeProvider.Mappings[".pf_fragment"] = "application/octet-stream";
contentTypeProvider.Mappings[".pagefind"] = "application/wasm";

app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypeProvider });
app.Run();
```

More: https://pagefind.nullean.net/getting-started/asp-net/ and https://pagefind.nullean.net/reference/frontend/

## Learn more

- Documentation: https://pagefind.nullean.net
- Source and issues: https://github.com/nullean/pagefind-net
