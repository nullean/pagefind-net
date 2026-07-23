using System.Net;
using Microsoft.Playwright;
using Pagefind.Net;
using TUnit.Core.Interfaces;

namespace Pagefind.Net.E2E.Tests;

/// <summary>
/// Shared fixture for Playwright E2E tests.
///
/// Setup sequence (run once before all tests in this class):
/// 1. Build the Pagefind data index from the in-memory corpus using pagefind-net.
/// 2. Overlay the npm Pagefind query runtime (pagefind.js + wasm.*.pagefind)
///    by running <c>node emit-runtime.mjs &lt;pagefindDir&gt;</c> from the example project.
/// 3. Start a local HTTP server serving the wwwroot directory.
/// 4. Playwright navigates to the served index.html.
///
/// Teardown: stop HTTP server, delete temp directory, close Playwright.
/// </summary>
public sealed class SearchFixture : IAsyncInitializer, IAsyncDisposable
{
	private string? _wwwroot;
	private HttpListener? _listener;
	private Thread? _listenerThread;
	private IPlaywright? _playwright;
	private IBrowser? _browser;

	public IBrowserContext? BrowserContext { get; private set; }
	public string? BaseUrl { get; private set; }

	public async Task InitializeAsync()
	{
		// 1. Write pagefind-net data files into a temp dir.
		_wwwroot = Path.Combine(Path.GetTempPath(), $"pagefind-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Combine(_wwwroot, "pagefind"));

		var index = BuildIndex();
		await index.WriteAsync(_wwwroot, CancellationToken.None);

		// 2. Emit pagefind.js + wasm from npm (requires npm install in example dir).
		//    Find the example directory relative to the test binary.
		var exampleDir = FindExampleDir();
		if (exampleDir is null)
			throw new InvalidOperationException(
				"Cannot find examples/Pagefind.Net.Example. Is npm install done?");

		var pagefindDir = Path.Combine(_wwwroot, "pagefind");
		await EmitNpmRuntimeAsync(exampleDir, pagefindDir);

		// Copy the index.html from the example's wwwroot.
		var srcHtml = Path.Combine(exampleDir, "wwwroot", "index.html");
		if (File.Exists(srcHtml))
			File.Copy(srcHtml, Path.Combine(_wwwroot, "index.html"), overwrite: true);

		// 3. Start a local HTTP server.
		var port = FindFreePort();
		BaseUrl = $"http://localhost:{port}";
		StartHttpServer(_wwwroot, port);

		// 4. Launch Playwright.
		_playwright = await Playwright.CreateAsync();
		_browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
		BrowserContext = await _browser.NewContextAsync(new() { BaseURL = BaseUrl });
	}

	public async ValueTask DisposeAsync()
	{
		if (BrowserContext is not null) await BrowserContext.DisposeAsync();
		if (_browser is not null) await _browser.DisposeAsync();
		_playwright?.Dispose();
		_listener?.Stop();
		if (_wwwroot is not null && Directory.Exists(_wwwroot))
			Directory.Delete(_wwwroot, recursive: true);
	}

	// ── Index builder ──────────────────────────────────────────────────────────

	public static PagefindIndex BuildIndex()
	{
		var index = new PagefindIndex(new PagefindIndexOptions { Language = "en" });

		index.AddRecord(new PagefindRecord
		{
			Url = "/getting-started/",
			Title = "Getting Started",
			Content = "Welcome to pagefind-net. Install the NuGet package and call WriteAsync.",
			WeightedSegments =
			[
				new WeightedSegment("Getting Started", Weight: 7),
				new WeightedSegment("Installation Quickstart Frontend Runtime", Weight: 4),
				new WeightedSegment("Welcome to pagefind-net. Install the NuGet package and call WriteAsync.", Weight: 1),
			],
			Meta = new Dictionary<string, string> { ["title"] = "Getting Started" },
		});

		index.AddRecord(new PagefindRecord
		{
			Url = "/configuration/",
			Title = "Configuration",
			Content = "Configure PagefindIndexOptions: Language, IncludeCharacters, WeightedSegments.",
			WeightedSegments =
			[
				new WeightedSegment("Configuration", Weight: 7),
				new WeightedSegment("Language IncludeCharacters WeightedSegments", Weight: 4),
				new WeightedSegment("Configure PagefindIndexOptions: Language, IncludeCharacters, WeightedSegments.", Weight: 1),
			],
			Meta = new Dictionary<string, string> { ["title"] = "Configuration" },
		});

		index.AddRecord(new PagefindRecord
		{
			Url = "/api-reference/",
			Title = "API Reference",
			Content = "Full reference for PagefindIndex, PagefindRecord, WeightedSegment, PagefindAnchor.",
			WeightedSegments =
			[
				new WeightedSegment("API Reference", Weight: 7),
				new WeightedSegment("PagefindIndex PagefindRecord WeightedSegment PagefindAnchor", Weight: 4),
				new WeightedSegment("Full reference for PagefindIndex, PagefindRecord, WeightedSegment, PagefindAnchor.", Weight: 1),
			],
			Meta = new Dictionary<string, string> { ["title"] = "API Reference" },
		});

		return index;
	}

	// ── Helpers ────────────────────────────────────────────────────────────────

	private static string? FindExampleDir()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !dir.GetFiles("*.slnx").Any())
			dir = dir.Parent;
		if (dir is null) return null;
		var candidate = Path.Combine(dir.FullName, "examples", "Pagefind.Net.Example");
		return Directory.Exists(candidate) ? candidate : null;
	}

	private static async Task EmitNpmRuntimeAsync(string exampleDir, string pagefindOutputDir)
	{
		// node_modules must exist (npm install must have been run).
		var nodeModules = Path.Combine(exampleDir, "node_modules");
		if (!Directory.Exists(nodeModules))
			throw new InvalidOperationException(
				$"node_modules not found at {nodeModules}. Run `npm install` in {exampleDir}.");

		var scriptPath = Path.Combine(exampleDir, "emit-runtime.mjs");
		var psi = new System.Diagnostics.ProcessStartInfo("node", $"\"{scriptPath}\" \"{pagefindOutputDir}\"")
		{
			WorkingDirectory = exampleDir,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		using var proc = System.Diagnostics.Process.Start(psi)
			?? throw new InvalidOperationException("Failed to start node process.");
		var stdout = await proc.StandardOutput.ReadToEndAsync();
		var stderr = await proc.StandardError.ReadToEndAsync();
		await proc.WaitForExitAsync();
		if (proc.ExitCode != 0)
			throw new InvalidOperationException(
				$"emit-runtime.mjs failed (exit {proc.ExitCode}):\n{stderr}");
	}

	private void StartHttpServer(string wwwroot, int port)
	{
		_listener = new HttpListener();
		_listener.Prefixes.Add($"http://localhost:{port}/");
		_listener.Start();
		_listenerThread = new Thread(() =>
		{
			while (_listener.IsListening)
			{
				HttpListenerContext? ctx = null;
				try { ctx = _listener.GetContext(); }
				catch { break; }
				ThreadPool.QueueUserWorkItem(_ => ServeRequest(ctx, wwwroot));
			}
		}) { IsBackground = true };
		_listenerThread.Start();
	}

	private static void ServeRequest(HttpListenerContext ctx, string wwwroot)
	{
		var urlPath = ctx.Request.Url?.LocalPath ?? "/";
		if (urlPath == "/") urlPath = "/index.html";
		var filePath = Path.Combine(wwwroot, urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(filePath))
		{
			ctx.Response.ContentType = GuessContentType(filePath);
			var bytes = File.ReadAllBytes(filePath);
			ctx.Response.ContentLength64 = bytes.Length;
			ctx.Response.OutputStream.Write(bytes);
		}
		else
		{
			ctx.Response.StatusCode = 404;
		}
		ctx.Response.Close();
	}

	private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
	{
		".html" => "text/html; charset=utf-8",
		".js" => "application/javascript",
		".json" => "application/json",
		".wasm" => "application/wasm",
		_ => "application/octet-stream",
	};

	private static int FindFreePort()
	{
		var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}
