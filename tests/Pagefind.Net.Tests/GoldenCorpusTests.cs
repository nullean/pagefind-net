using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Pagefind.Net;
using TUnit;

namespace Pagefind.Net.Tests;

/// <summary>
/// Differential tests: run pagefind-net over the sample corpus and compare
/// the decoded structures to what the official Pagefind binary produces for
/// the same corpus.
///
/// The reference output is produced by the npm Pagefind Node API
/// (<c>index.writeFiles</c>) against the HTML corpus in <c>Corpus/</c>.
/// Run <c>node generate-fixtures.mjs</c> in the tests directory to generate
/// <c>Fixtures/reference-pagefind/</c> before running these tests.
///
/// The comparison is structural (decoded values), NOT byte-identical —
/// chunk boundaries and content-hash filenames may legitimately differ.
/// </summary>
public sealed class GoldenCorpusTests
{
	private static readonly string CorpusDir =
		Path.Combine(AppContext.BaseDirectory, "Corpus");
	private static readonly string ReferenceDir =
		Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference-pagefind");

	[Test]
	public async Task WritesExpectedOutputFiles()
	{
		var outputDir = Path.Combine(Path.GetTempPath(), $"pagefind-net-test-{Guid.NewGuid():N}");
		try
		{
			var index = BuildIndex();
			await index.WriteAsync(outputDir, CancellationToken.None);

			var pagefindDir = Path.Combine(outputDir, "pagefind");
			Directory.Exists(pagefindDir).Should().BeTrue();
			File.Exists(Path.Combine(pagefindDir, "pagefind-entry.json")).Should().BeTrue();

			var metaFiles = Directory.GetFiles(pagefindDir, "*.pf_meta");
			metaFiles.Should().HaveCount(1, "there should be exactly one meta file");

			Directory.GetFiles(Path.Combine(pagefindDir, "index"), "*.pf_index")
				.Should().NotBeEmpty("at least one index chunk is expected");

			Directory.GetFiles(Path.Combine(pagefindDir, "fragment"), "*.pf_fragment")
				.Should().HaveCount(3, "one fragment per corpus page");
		}
		finally
		{
			if (Directory.Exists(outputDir))
				Directory.Delete(outputDir, recursive: true);
		}
	}

	[Test]
	public async Task EntryJsonHasCorrectStructure()
	{
		var outputDir = Path.Combine(Path.GetTempPath(), $"pagefind-net-test-{Guid.NewGuid():N}");
		try
		{
			var index = BuildIndex();
			await index.WriteAsync(outputDir, CancellationToken.None);

			var entryPath = Path.Combine(outputDir, "pagefind", "pagefind-entry.json");
			var json = await File.ReadAllTextAsync(entryPath);
			using var doc = JsonDocument.Parse(json);

			doc.RootElement.GetProperty("version").GetString()
				.Should().Be(PagefindIndex.PagefindTargetVersion);
			doc.RootElement.TryGetProperty("languages", out var langs).Should().BeTrue();
			langs.TryGetProperty("en", out var en).Should().BeTrue();
			en.TryGetProperty("hash", out _).Should().BeTrue();
			en.TryGetProperty("wasm", out var wasm).Should().BeTrue();
			wasm.GetString().Should().Be("en"); // pagefind.js constructs wasm.${wasm}.pagefind
		}
		finally
		{
			if (Directory.Exists(outputDir))
				Directory.Delete(outputDir, recursive: true);
		}
	}

	[Test]
	public async Task FragmentsContainExpectedUrls()
	{
		var outputDir = Path.Combine(Path.GetTempPath(), $"pagefind-net-test-{Guid.NewGuid():N}");
		try
		{
			var index = BuildIndex();
			await index.WriteAsync(outputDir, CancellationToken.None);

			var fragmentDir = Path.Combine(outputDir, "pagefind", "fragment");
			var urls = new List<string>();
			foreach (var file in Directory.GetFiles(fragmentDir, "*.pf_fragment"))
			{
				var json = await ReadFramedJsonAsync(file);
				using var doc = JsonDocument.Parse(json);
				var url = doc.RootElement.GetProperty("url").GetString();
				if (url is not null) urls.Add(url);
			}

			urls.Should().Contain("/getting-started/");
			urls.Should().Contain("/configuration/");
			urls.Should().Contain("/api-reference/");
		}
		finally
		{
			if (Directory.Exists(outputDir))
				Directory.Delete(outputDir, recursive: true);
		}
	}

	[Test]
	public async Task StructuralParityWithOfficialBinary()
	{
		if (!Directory.Exists(ReferenceDir))
			return; // Fixture not yet generated; [Skip] attribute handles this.

		var outputDir = Path.Combine(Path.GetTempPath(), $"pagefind-net-test-{Guid.NewGuid():N}");
		try
		{
			var index = BuildIndex();
			await index.WriteAsync(outputDir, CancellationToken.None);

			// Compare fragment URLs.
			var ourUrls = await CollectFragmentUrlsAsync(Path.Combine(outputDir, "pagefind", "fragment"));
			var refUrls = await CollectFragmentUrlsAsync(Path.Combine(ReferenceDir, "fragment"));

			ourUrls.Should().BeEquivalentTo(refUrls,
				"both indexers should produce fragments for the same pages");
		}
		finally
		{
			if (Directory.Exists(outputDir))
				Directory.Delete(outputDir, recursive: true);
		}
	}

	// ── Helpers ────────────────────────────────────────────────────────────────

	private static PagefindIndex BuildIndex()
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
				new WeightedSegment("Installation", Weight: 4),
				new WeightedSegment("Quickstart", Weight: 4),
				new WeightedSegment("Frontend Runtime", Weight: 4),
				new WeightedSegment("Welcome to pagefind-net. Install the NuGet package and call WriteAsync.", Weight: 1),
			],
			Anchors =
			[
				new PagefindAnchor("installation", "Installation", 0),
				new PagefindAnchor("quickstart", "Quickstart", 20),
				new PagefindAnchor("frontend", "Frontend Runtime", 50),
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
				new WeightedSegment("Language", Weight: 4),
				new WeightedSegment("IncludeCharacters", Weight: 4),
				new WeightedSegment("WeightedSegments", Weight: 4),
				new WeightedSegment("Configure PagefindIndexOptions: Language, IncludeCharacters, WeightedSegments.", Weight: 1),
			],
			Meta = new Dictionary<string, string> { ["title"] = "Configuration" },
		});

		index.AddRecord(new PagefindRecord
		{
			Url = "/api-reference/",
			Title = "API Reference",
			Content = "PagefindIndex, PagefindRecord, WeightedSegment, PagefindAnchor — full API reference.",
			WeightedSegments =
			[
				new WeightedSegment("API Reference", Weight: 7),
				new WeightedSegment("PagefindIndex", Weight: 4),
				new WeightedSegment("PagefindRecord", Weight: 4),
				new WeightedSegment("WeightedSegment", Weight: 4),
				new WeightedSegment("PagefindAnchor", Weight: 4),
				new WeightedSegment("PagefindIndex, PagefindRecord, WeightedSegment, PagefindAnchor — full API reference.", Weight: 1),
			],
			Meta = new Dictionary<string, string> { ["title"] = "API Reference" },
		});

		return index;
	}

	private static async Task<string> ReadFramedJsonAsync(string path)
	{
		await using var fs = File.OpenRead(path);
		await using var gz = new GZipStream(fs, CompressionMode.Decompress);
		using var ms = new MemoryStream();
		await gz.CopyToAsync(ms);
		var bytes = ms.ToArray();
		// Skip "pagefind_dcd" magic (12 bytes).
		return Encoding.UTF8.GetString(bytes, 12, bytes.Length - 12);
	}

	private static async Task<List<string>> CollectFragmentUrlsAsync(string fragmentDir)
	{
		var urls = new List<string>();
		if (!Directory.Exists(fragmentDir)) return urls;
		foreach (var file in Directory.GetFiles(fragmentDir, "*.pf_fragment"))
		{
			var json = await ReadFramedJsonAsync(file);
			using var doc = JsonDocument.Parse(json);
			var url = doc.RootElement.GetProperty("url").GetString();
			if (url is not null) urls.Add(url);
		}
		return urls;
	}
}
