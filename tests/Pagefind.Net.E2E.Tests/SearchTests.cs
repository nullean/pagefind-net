using System.Text.Json;
using AwesomeAssertions;
using Nullean.Cdp;
using Nullean.Snapshots.Browser;

namespace Pagefind.Net.E2E.Tests;

/// <summary>
/// End-to-end tests: the Pagefind.Net.Web app is hosted on Kestrel and searched in the installed Chrome, driven over
/// CDP by Nullean.Cdp.
///
/// Each test checks the search twice: the <c>window.pagefind</c> API must return the expected page (an explicit
/// assertion), and typing the term into the page's search box must render the results its verified accessibility
/// snapshot holds (<c>.snapshots/SearchTests/*.verified.aria</c>, compared by Nullean.Snapshots).
///
/// Prerequisites: Chrome installed. Nothing is downloaded; set <c>CDP_CHROME</c> to use a Chrome that is not found.
/// </summary>
[ClassDataSource<SearchFixture>(Shared = SharedType.PerClass)]
public sealed class SearchTests(SearchFixture fixture)
{
	[Test]
	public async Task SearchForInstall_ReturnsGettingStartedPage()
	{
		await using var context = await fixture.Browser!.NewContextAsync();
		var page = await OpenSearchPageAsync(context);

		var results = await SearchUrlsAsync(page, "install");
		results.Should().Contain("/getting-started/", "'install' appears in the Getting Started page");

		(await RenderedResultsAsync(page, "install")).Should().MatchAriaSnapshot();
	}

	[Test]
	public async Task SearchForLanguage_ReturnsConfigurationPage()
	{
		await using var context = await fixture.Browser!.NewContextAsync();
		var page = await OpenSearchPageAsync(context);

		var results = await SearchUrlsAsync(page, "language");
		results.Should().Contain("/configuration/", "'language' appears in the Configuration page");

		(await RenderedResultsAsync(page, "language")).Should().MatchAriaSnapshot();
	}

	[Test]
	public async Task SearchForApi_ReturnsApiReferencePage()
	{
		await using var context = await fixture.Browser!.NewContextAsync();
		var page = await OpenSearchPageAsync(context);

		var results = await SearchUrlsAsync(page, "api");
		results.Should().Contain("/api-reference/", "'api' appears in the API Reference page heading");

		(await RenderedResultsAsync(page, "api")).Should().MatchAriaSnapshot();
	}

	[Test]
	public async Task SearchForNonexistentTerm_ReturnsEmpty()
	{
		await using var context = await fixture.Browser!.NewContextAsync();
		var page = await OpenSearchPageAsync(context);

		var count = (await page.EvaluateAsync(
			"""
			(async () => {
				const { results } = await window.pagefind.search("xyzzyfrob");
				return results.length;
			})()
			"""
		)).GetInt32();

		count.Should().Be(0, "a nonsense term should not match any pages");

		(await RenderedResultsAsync(page, "xyzzyfrob", ".empty")).Should().MatchAriaSnapshot();
	}

	[Test]
	public async Task SearchForPerformance_ReturnsPerformancePage()
	{
		await using var context = await fixture.Browser!.NewContextAsync();
		var page = await OpenSearchPageAsync(context);

		var results = await SearchUrlsAsync(page, "performance");
		results.Should().Contain("/performance/", "'performance' appears in the Performance page");

		(await RenderedResultsAsync(page, "performance")).Should().MatchAriaSnapshot();
	}

	/// <summary>Opens the search page and waits until its module script has put <c>window.pagefind</c> in place.</summary>
	private async Task<Page> OpenSearchPageAsync(BrowserContext context)
	{
		var page = await context.NewPageAsync();
		await page.GotoAsync($"{fixture.BaseUrl}/");

		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!(await page.EvaluateAsync("typeof window.pagefind !== 'undefined'")).GetBoolean())
		{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("window.pagefind was not defined within 10 seconds");
			await Task.Delay(50);
		}
		return page;
	}

	/// <summary>The URLs of the top five results the pagefind API returns for <paramref name="term"/>.</summary>
	private static async Task<string[]> SearchUrlsAsync(Page page, string term)
	{
		var json = await page.EvaluateAsync(
			$$"""
			(async () => {
				const { results } = await window.pagefind.search({{JsonSerializer.Serialize(term)}});
				const data = await Promise.all(results.slice(0, 5).map(r => r.data()));
				return data.map(d => d.url);
			})()
			"""
		);
		return [.. json.EnumerateArray().Select(e => e.GetString()!)];
	}

	/// <summary>
	/// Types <paramref name="term"/> into the search box, waits for the page to render <paramref name="rendered"/>
	/// inside <c>#results</c>, and returns the accessibility tree of the results.
	/// </summary>
	private static async Task<string> RenderedResultsAsync(Page page, string term, string rendered = ".result")
	{
		await page.Css("#query").FillAsync(term);
		await page.Css($"#results {rendered}").WaitAsync();
		return await page.SnapshotAsync(new() { Compact = true, Selector = "#results" });
	}
}
