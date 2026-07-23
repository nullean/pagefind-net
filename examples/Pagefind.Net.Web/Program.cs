using Microsoft.AspNetCore.StaticFiles;
using Pagefind.Net;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Build the pagefind index into wwwroot/pagefind/ on startup.
var wwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
await BuildSearchIndex(wwwroot);

// Pagefind uses custom file extensions that ASP.NET doesn't know about.
var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".pf_meta"] = "application/octet-stream";
contentTypeProvider.Mappings[".pf_index"] = "application/octet-stream";
contentTypeProvider.Mappings[".pf_fragment"] = "application/octet-stream";
contentTypeProvider.Mappings[".pagefind"] = "application/wasm";

app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypeProvider });
app.MapFallbackToFile("index.html");

Console.WriteLine("Search UI available at: http://localhost:5200");
app.Run("http://localhost:5200");

static async Task BuildSearchIndex(string wwwroot)
{
    Console.WriteLine("Building Pagefind index...");

    var index = new PagefindIndex(new PagefindIndexOptions { Language = "en" });

    foreach (var doc in SampleCorpus.Documents)
        index.AddRecord(doc);

    await index.WriteAsync(wwwroot, CancellationToken.None);

    Console.WriteLine($"  Indexed {SampleCorpus.Documents.Length} documents into {wwwroot}/pagefind/");
}

/// <summary>
/// A larger sample corpus representing documentation for a fictional .NET search library.
/// </summary>
static class SampleCorpus
{
    public static readonly PagefindRecord[] Documents =
    [
        new()
        {
            Url = "/getting-started/",
            Title = "Getting Started",
            Content = "Welcome to pagefind-net, a pure .NET implementation of the Pagefind indexer. "
                + "Install the NuGet package and build a search index for your static site in seconds. "
                + "No external binaries required — the entire pipeline runs in managed code.",
            WeightedSegments =
            [
                new("Getting Started", Weight: 7),
                new("Installation Quickstart Setup", Weight: 4),
                new("Welcome to pagefind-net, a pure .NET implementation of the Pagefind indexer. "
                    + "Install the NuGet package and build a search index for your static site in seconds. "
                    + "No external binaries required — the entire pipeline runs in managed code.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "Getting Started" },
        },
        new()
        {
            Url = "/configuration/",
            Title = "Configuration",
            Content = "Configure PagefindIndexOptions to control language, stemming, and tokenisation. "
                + "Set IncludeCharacters to preserve special characters like # or @ in tokens. "
                + "Choose your language for locale-aware stemming and stop-word handling.",
            WeightedSegments =
            [
                new("Configuration", Weight: 7),
                new("Language IncludeCharacters Options Stemming", Weight: 4),
                new("Configure PagefindIndexOptions to control language, stemming, and tokenisation. "
                    + "Set IncludeCharacters to preserve special characters like # or @ in tokens. "
                    + "Choose your language for locale-aware stemming and stop-word handling.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "Configuration" },
        },
        new()
        {
            Url = "/api-reference/",
            Title = "API Reference",
            Content = "Complete reference for the public API: PagefindIndex, PagefindRecord, "
                + "WeightedSegment, PagefindAnchor, and PagefindIndexOptions. "
                + "All types are AOT-compatible and trimming-safe.",
            WeightedSegments =
            [
                new("API Reference", Weight: 7),
                new("PagefindIndex PagefindRecord WeightedSegment PagefindAnchor PagefindIndexOptions", Weight: 4),
                new("Complete reference for the public API: PagefindIndex, PagefindRecord, "
                    + "WeightedSegment, PagefindAnchor, and PagefindIndexOptions. "
                    + "All types are AOT-compatible and trimming-safe.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "API Reference" },
        },
        new()
        {
            Url = "/indexing/",
            Title = "Indexing Documents",
            Content = "Add documents to the index using AddRecord. Each record represents one searchable page "
                + "with a URL, title, content body, and optional weighted segments for boosting headings. "
                + "Call WriteAsync to emit the complete index as .pf_index, .pf_fragment, and .pf_meta files.",
            WeightedSegments =
            [
                new("Indexing Documents", Weight: 7),
                new("AddRecord WriteAsync PagefindRecord Segments", Weight: 4),
                new("Add documents to the index using AddRecord. Each record represents one searchable page "
                    + "with a URL, title, content body, and optional weighted segments for boosting headings. "
                    + "Call WriteAsync to emit the complete index as .pf_index, .pf_fragment, and .pf_meta files.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "Indexing Documents" },
        },
        new()
        {
            Url = "/weighted-segments/",
            Title = "Weighted Segments",
            Content = "Weighted segments allow you to boost certain parts of a document — headings, "
                + "summaries, or keywords — so they rank higher in search results. "
                + "Assign a weight from 1 (normal) to 10 (highest priority). "
                + "The default body text weight is 1.",
            WeightedSegments =
            [
                new("Weighted Segments", Weight: 7),
                new("Boosting Ranking Priority Weight", Weight: 4),
                new("Weighted segments allow you to boost certain parts of a document — headings, "
                    + "summaries, or keywords — so they rank higher in search results. "
                    + "Assign a weight from 1 (normal) to 10 (highest priority). "
                    + "The default body text weight is 1.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "Weighted Segments" },
        },
        new()
        {
            Url = "/anchors/",
            Title = "Sub-page Anchors",
            Content = "PagefindAnchor lets search results link directly to a section within a page. "
                + "Provide an element ID, a human-readable label, and the byte offset into the content. "
                + "Pagefind's frontend will highlight the matching section when the user navigates.",
            WeightedSegments =
            [
                new("Sub-page Anchors", Weight: 7),
                new("PagefindAnchor Sections Deep Links Fragments", Weight: 4),
                new("PagefindAnchor lets search results link directly to a section within a page. "
                    + "Provide an element ID, a human-readable label, and the byte offset into the content. "
                    + "Pagefind's frontend will highlight the matching section when the user navigates.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "Sub-page Anchors" },
        },
        new()
        {
            Url = "/performance/",
            Title = "Performance",
            Content = "pagefind-net is designed for zero-allocation hot paths. The tokeniser uses a "
                + "push-based span API, the stemmer operates entirely on stack buffers, and dictionary "
                + "lookups use GetAlternateLookup to avoid allocating strings for repeated words. "
                + "Indexing 10,000 documents typically completes in under one second.",
            WeightedSegments =
            [
                new("Performance", Weight: 7),
                new("Zero Allocation Span Benchmarks Speed", Weight: 4),
                new("pagefind-net is designed for zero-allocation hot paths. The tokeniser uses a "
                    + "push-based span API, the stemmer operates entirely on stack buffers, and dictionary "
                    + "lookups use GetAlternateLookup to avoid allocating strings for repeated words. "
                    + "Indexing 10,000 documents typically completes in under one second.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "Performance" },
        },
        new()
        {
            Url = "/asp-net-integration/",
            Title = "ASP.NET Integration",
            Content = "Integrate pagefind-net into an ASP.NET application by building the index at startup "
                + "or as a background hosted service. Serve the pagefind directory as static files. "
                + "The frontend JavaScript handles fetching index chunks on demand from the browser.",
            WeightedSegments =
            [
                new("ASP.NET Integration", Weight: 7),
                new("Middleware StaticFiles Startup HostedService WebApplication", Weight: 4),
                new("Integrate pagefind-net into an ASP.NET application by building the index at startup "
                    + "or as a background hosted service. Serve the pagefind directory as static files. "
                    + "The frontend JavaScript handles fetching index chunks on demand from the browser.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "ASP.NET Integration" },
        },
        new()
        {
            Url = "/static-site-generators/",
            Title = "Static Site Generators",
            Content = "Use pagefind-net as a post-build step in your static site generator pipeline. "
                + "Read the generated HTML files, extract content with your preferred parser, "
                + "create PagefindRecord instances, and write the index alongside your site output. "
                + "Works with Jekyll, Hugo, Docusaurus, Astro, or any tool that emits HTML.",
            WeightedSegments =
            [
                new("Static Site Generators", Weight: 7),
                new("Jekyll Hugo Docusaurus Astro Post-Build Pipeline", Weight: 4),
                new("Use pagefind-net as a post-build step in your static site generator pipeline. "
                    + "Read the generated HTML files, extract content with your preferred parser, "
                    + "create PagefindRecord instances, and write the index alongside your site output. "
                    + "Works with Jekyll, Hugo, Docusaurus, Astro, or any tool that emits HTML.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "Static Site Generators" },
        },
        new()
        {
            Url = "/filtering/",
            Title = "Filtering and Metadata",
            Content = "Attach arbitrary metadata to each record via the Meta dictionary. "
                + "Common uses include category tags, author names, and publication dates. "
                + "The pagefind frontend can filter results by metadata values before performing "
                + "the full-text search, reducing the result set efficiently.",
            WeightedSegments =
            [
                new("Filtering and Metadata", Weight: 7),
                new("Meta Tags Categories Facets Filter", Weight: 4),
                new("Attach arbitrary metadata to each record via the Meta dictionary. "
                    + "Common uses include category tags, author names, and publication dates. "
                    + "The pagefind frontend can filter results by metadata values before performing "
                    + "the full-text search, reducing the result set efficiently.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "Filtering and Metadata" },
        },
        new()
        {
            Url = "/troubleshooting/",
            Title = "Troubleshooting",
            Content = "Common issues: missing pagefind.js (ensure npm install and emit-runtime ran), "
                + "empty search results (check that WriteAsync completed without errors), "
                + "incorrect stemming (verify the Language option matches your content). "
                + "Enable verbose logging to diagnose index-time problems.",
            WeightedSegments =
            [
                new("Troubleshooting", Weight: 7),
                new("Errors Debugging Logging Issues FAQ", Weight: 4),
                new("Common issues: missing pagefind.js (ensure npm install and emit-runtime ran), "
                    + "empty search results (check that WriteAsync completed without errors), "
                    + "incorrect stemming (verify the Language option matches your content). "
                    + "Enable verbose logging to diagnose index-time problems.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "Troubleshooting" },
        },
        new()
        {
            Url = "/deployment/",
            Title = "Deployment",
            Content = "Deploy the pagefind directory alongside your site. The index files are static — "
                + "no server-side runtime needed for search. Host on any CDN, GitHub Pages, Azure Static "
                + "Web Apps, Netlify, or Cloudflare Pages. The total index size for a typical docs site "
                + "is under 500KB compressed.",
            WeightedSegments =
            [
                new("Deployment", Weight: 7),
                new("CDN GitHub Pages Azure Netlify Cloudflare Hosting", Weight: 4),
                new("Deploy the pagefind directory alongside your site. The index files are static — "
                    + "no server-side runtime needed for search. Host on any CDN, GitHub Pages, Azure Static "
                    + "Web Apps, Netlify, or Cloudflare Pages. The total index size for a typical docs site "
                    + "is under 500KB compressed.", Weight: 1),
            ],
            Meta = new Dictionary<string, string> { ["title"] = "Deployment" },
        },
    ];
}
