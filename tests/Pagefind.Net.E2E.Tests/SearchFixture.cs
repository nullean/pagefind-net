using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Playwright;
using TUnit.Core.Interfaces;

namespace Pagefind.Net.E2E.Tests;

/// <summary>
/// Shared fixture for Playwright E2E tests.
///
/// Starts the <c>Pagefind.Net.Web</c> app on a random port using Kestrel,
/// then connects Playwright to it for headless browser testing.
/// No npm, no Node.js required — the frontend runtime comes from Pagefind.Net.Frontend.
/// </summary>
public sealed class SearchFixture : IAsyncInitializer, IAsyncDisposable
{
	private WebApplication? _app;
	private IPlaywright? _playwright;
	private IBrowser? _browser;

	public IBrowserContext? BrowserContext { get; private set; }
	public string? BaseUrl { get; private set; }

	public async Task InitializeAsync()
	{
		var port = FindFreePort();
		BaseUrl = $"http://localhost:{port}";

		// Start the web app on a real port.
		// We reuse the exact same setup as Program.cs by passing --urls.
		_app = BuildApp(port);
		await _app.StartAsync();

		// Launch Playwright.
		_playwright = await Playwright.CreateAsync();
		_browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
		BrowserContext = await _browser.NewContextAsync(new() { BaseURL = BaseUrl });
	}

	public async ValueTask DisposeAsync()
	{
		if (BrowserContext is not null) await BrowserContext.DisposeAsync();
		if (_browser is not null) await _browser.DisposeAsync();
		_playwright?.Dispose();
		if (_app is not null) await _app.StopAsync();
		if (_app is not null) await _app.DisposeAsync();
	}

	/// <summary>
	/// Builds the web app identically to the Pagefind.Net.Web example but on the given port.
	/// This directly invokes the same pipeline: index on startup, static files, pagefind MIME types.
	/// </summary>
	private static WebApplication BuildApp(int port)
	{
		// Point the content root at the Pagefind.Net.Web project directory so it finds wwwroot/.
		var webProjectDir = FindWebProjectDir()
			?? throw new InvalidOperationException(
				"Cannot find examples/Pagefind.Net.Web. Ensure the solution structure is intact.");

		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			ContentRootPath = webProjectDir,
			Args = [$"--urls=http://localhost:{port}"]
		});
		builder.WebHost.UseUrls($"http://localhost:{port}");

		var app = builder.Build();

		// Build the pagefind index (same as Program.cs).
		var wwwroot = Path.Combine(webProjectDir, "wwwroot");
		var index = new PagefindIndex(new PagefindIndexOptions { Language = "en" });
		foreach (var doc in GetCorpus())
			index.AddRecord(doc);
		index.WriteAsync(wwwroot, CancellationToken.None).GetAwaiter().GetResult();

		// Configure middleware (same as Program.cs).
		var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
		contentTypeProvider.Mappings[".pf_meta"] = "application/octet-stream";
		contentTypeProvider.Mappings[".pf_index"] = "application/octet-stream";
		contentTypeProvider.Mappings[".pf_fragment"] = "application/octet-stream";
		contentTypeProvider.Mappings[".pagefind"] = "application/wasm";

		app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
		{
			ContentTypeProvider = contentTypeProvider
		});
		app.MapFallbackToFile("index.html");

		return app;
	}

	/// <summary>
	/// Uses the same corpus as the web example. Import a subset so the tests have known documents.
	/// </summary>
	private static PagefindRecord[] GetCorpus() =>
	[
		new()
		{
			Url = "/getting-started/",
			Title = "Getting Started",
			Content = "Welcome to pagefind-net, a pure .NET implementation of the Pagefind indexer. "
				+ "Install the NuGet package and build a search index for your static site in seconds.",
			WeightedSegments =
			[
				new("Getting Started", Weight: 7),
				new("Installation Quickstart", Weight: 4),
			],
			Meta = new Dictionary<string, string> { ["title"] = "Getting Started" },
		},
		new()
		{
			Url = "/configuration/",
			Title = "Configuration",
			Content = "Configure PagefindIndexOptions to control language, stemming, and tokenisation. "
				+ "Set IncludeCharacters to preserve special characters like # or @ in tokens.",
			WeightedSegments =
			[
				new("Configuration", Weight: 7),
				new("Language IncludeCharacters Options", Weight: 4),
			],
			Meta = new Dictionary<string, string> { ["title"] = "Configuration" },
		},
		new()
		{
			Url = "/api-reference/",
			Title = "API Reference",
			Content = "Complete reference for the public API: PagefindIndex, PagefindRecord, "
				+ "WeightedSegment, PagefindAnchor, and PagefindIndexOptions.",
			WeightedSegments =
			[
				new("API Reference", Weight: 7),
				new("PagefindIndex PagefindRecord WeightedSegment", Weight: 4),
			],
			Meta = new Dictionary<string, string> { ["title"] = "API Reference" },
		},
		new()
		{
			Url = "/performance/",
			Title = "Performance",
			Content = "Performance: pagefind-net is designed for zero-allocation hot paths. The tokeniser uses a "
				+ "push-based span API, the stemmer operates entirely on stack buffers.",
			WeightedSegments =
			[
				new("Performance", Weight: 7),
				new("Zero Allocation Span Benchmarks", Weight: 4),
			],
			Meta = new Dictionary<string, string> { ["title"] = "Performance" },
		},
	];

	private static string? FindWebProjectDir()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !dir.GetFiles("*.slnx").Any())
			dir = dir.Parent;
		if (dir is null) return null;
		var candidate = Path.Combine(dir.FullName, "examples", "Pagefind.Net.Web");
		return Directory.Exists(candidate) ? candidate : null;
	}

	private static int FindFreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}
