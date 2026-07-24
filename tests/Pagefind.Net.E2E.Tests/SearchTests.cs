using AwesomeAssertions;
using Microsoft.Playwright;
using TUnit;

namespace Pagefind.Net.E2E.Tests;

/// <summary>
/// End-to-end tests: the Pagefind.Net.Web app is hosted via WebApplicationFactory,
/// Playwright connects to it and verifies search results in headless Chromium.
///
/// Prerequisites:
/// - Playwright browsers installed: <c>pwsh playwright.ps1 install chromium</c>
/// </summary>
[ClassDataSource<SearchFixture>(Shared = SharedType.PerClass)]
public sealed class SearchTests(SearchFixture fixture)
{
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

	[Test]
	public async Task SearchForPerformance_ReturnsPerformancePage()
	{
		var page = await fixture.BrowserContext!.NewPageAsync();
		await page.GotoAsync("/");

		await page.WaitForFunctionAsync("() => typeof window.pagefind !== 'undefined'",
			null, new() { Timeout = 10_000 });

		var results = await page.EvaluateAsync<string[]>("""
			async () => {
				const { results } = await window.pagefind.search("performance");
				const data = await Promise.all(results.slice(0, 5).map(r => r.data()));
				return data.map(d => d.url);
			}
		""");

		results.Should().Contain("/performance/",
			"'performance' appears in the Performance page");
		await page.CloseAsync();
	}
}
