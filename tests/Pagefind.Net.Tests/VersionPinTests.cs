using AwesomeAssertions;
using Pagefind.Net;
using TUnit;

namespace Pagefind.Net.Tests;

/// <summary>
/// Ensures that the library's declared target version constant is consistent
/// and that the example's npm package.json pins the same version.
/// </summary>
public sealed class VersionPinTests
{
	[Test]
	public void PagefindTargetVersionIsSet()
	{
		PagefindIndex.PagefindTargetVersion.Should().NotBeNullOrEmpty();
		// Basic semver shape: digits.digits.digits
		PagefindIndex.PagefindTargetVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+");
	}

	[Test]
	public async Task ExamplePackageJsonPinsCorrectVersion()
	{
		// Walk up from the test binary to find the example package.json.
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !dir.GetFiles("*.slnx").Any())
			dir = dir.Parent;

		if (dir is null)
		{
			// Can't find repo root in this environment; skip gracefully.
			return;
		}

		var packageJson = Path.Combine(
			dir.FullName, "examples", "Pagefind.Net.Example", "package.json");

		if (!File.Exists(packageJson))
		{
			// Can't find package.json; skip gracefully.
			return;
		}

		var json = await File.ReadAllTextAsync(packageJson);
		using var doc = System.Text.Json.JsonDocument.Parse(json);
		var deps = doc.RootElement.GetProperty("devDependencies");
		var pinned = deps.GetProperty("pagefind").GetString();

		pinned.Should().Be(PagefindIndex.PagefindTargetVersion,
			$"package.json should pin pagefind exactly to PagefindTargetVersion ({PagefindIndex.PagefindTargetVersion})");
	}
}
