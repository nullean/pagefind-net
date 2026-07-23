using AwesomeAssertions;
using Microsoft.Playwright;
using TUnit;

namespace Pagefind.Net.E2E.Tests;

/// <summary>
/// End-to-end tests: our CBOR+gzip data files + npm pagefind.js/wasm runtime
/// → correct search results in a headless Chromium browser.
///
/// Prerequisites (handled by CI / SearchFixture):
/// - <c>npm install</c> run in <c>examples/Pagefind.Net.Example/</c>
/// - Playwright browsers installed: <c>dotnet tool install --global playwright install chromium</c>
///   or the equivalent <c>pwsh playwright.ps1 install chromium</c> post-build step.
/// </summary>
[ClassDataSource<SearchFixture>(Shared = SharedType.PerClass)]
public sealed class SearchTests(SearchFixture fixture)
{
	/// <summary>
	/// Searching for "install" should return the Getting Started page (contains
	/// "Install the NuGet package") ranked in the top results.
	/// </summary>
	[Test]
	public async Task SearchForInstall_ReturnsGettingStartedPage()
	{
		var page = await fixture.BrowserContext!.NewPageAsync();
		await page.GotoAsync("/");

		await page.WaitForFunctionAsync("() => typeof window.pagefind !== 'undefined'",
			null, new() { Timeout = 10_000 });

		var results = await page.EvaluateAsync<string[]>("""
			async () => {
				const { results } = await window.pagefind.search("install");
				const data = await Promise.all(results.slice(0, 5).map(r => r.data()));
				return data.map(d => d.url);
			}
		""");

		results.Should().Contain("/getting-started/",
			"'install' appears in the Getting Started page");
		await page.CloseAsync();
	}

	/// <summary>
	/// Searching for "language" should return the Configuration page which
	/// has a section specifically about the Language option.
	/// </summary>
	[Test]
	public async Task SearchForLanguage_ReturnsConfigurationPage()
	{
		var page = await fixture.BrowserContext!.NewPageAsync();
		await page.GotoAsync("/");

		await page.WaitForFunctionAsync("() => typeof window.pagefind !== 'undefined'",
			null, new() { Timeout = 10_000 });

		var results = await page.EvaluateAsync<string[]>("""
			async () => {
				const { results } = await window.pagefind.search("language");
				const data = await Promise.all(results.slice(0, 5).map(r => r.data()));
				return data.map(d => d.url);
			}
		""");

		results.Should().Contain("/configuration/",
			"'language' appears in the Configuration page");
		await page.CloseAsync();
	}

	/// <summary>
	/// Searching for "API" should return the API Reference page.
	/// </summary>
	[Test]
	public async Task SearchForApi_ReturnsApiReferencePage()
	{
		var page = await fixture.BrowserContext!.NewPageAsync();
		await page.GotoAsync("/");

		await page.WaitForFunctionAsync("() => typeof window.pagefind !== 'undefined'",
			null, new() { Timeout = 10_000 });

		var results = await page.EvaluateAsync<string[]>("""
			async () => {
				const { results } = await window.pagefind.search("api");
				const data = await Promise.all(results.slice(0, 5).map(r => r.data()));
				return data.map(d => d.url);
			}
		""");

		results.Should().Contain("/api-reference/",
			"'api' appears in the API Reference page heading");
		await page.CloseAsync();
	}

	/// <summary>
	/// A query for a word that doesn't appear in any page should return
	/// an empty result set (not an error).
	/// </summary>
	[Test]
	public async Task SearchForNonexistentTerm_ReturnsEmpty()
	{
		var page = await fixture.BrowserContext!.NewPageAsync();
		await page.GotoAsync("/");

		await page.WaitForFunctionAsync("() => typeof window.pagefind !== 'undefined'",
			null, new() { Timeout = 10_000 });

		var count = await page.EvaluateAsync<int>("""
			async () => {
				const { results } = await window.pagefind.search("xyzzyfrob");
				return results.length;
			}
		""");

		count.Should().Be(0, "a nonsense term should not match any pages");
		await page.CloseAsync();
	}
}
