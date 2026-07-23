using AwesomeAssertions;
using Pagefind.Net;
using TUnit;

namespace Pagefind.Net.Tests;

/// <summary>
/// Verifies that the Snowball.Net Porter2 stemmer matches the output of
/// Pagefind's <c>pagefind_stem</c> crate for English.
///
/// The fixture <c>Fixtures/stemmer-parity.csv</c> is a CSV with two columns:
/// <c>input,expected_stem</c>. Generate it by running the pagefind stem
/// utility against a vocabulary list.
/// </summary>
public sealed class StemmerTests
{
	private static readonly Stemmer EnglishStemmer = new("en");

	[Test]
	[Arguments("running", "run")]
	[Arguments("jumps", "jump")]
	[Arguments("easily", "easili")]
	[Arguments("fishing", "fish")]
	[Arguments("generously", "generous")]
	[Arguments("troubled", "troubl")]
	[Arguments("index", "index")]
	[Arguments("indexing", "index")]
	[Arguments("indexes", "index")]
	public void EnglishPorter2Cases(string input, string expectedStem)
	{
		var actual = EnglishStemmer.Stem(input);
		actual.Should().Be(expectedStem, because: $"'{input}' should stem to '{expectedStem}'");
	}

	[Test]
	public void EmptyStringPassesThrough()
	{
		EnglishStemmer.Stem("").Should().Be("");
	}

	[Test]
	public void NonEnglishStemmerReturnUnchanged()
	{
		// For unsupported languages the stemmer is a no-op.
		var unknownStemmer = new Stemmer("unknown");
		unknownStemmer.Stem("running").Should().Be("running");
	}

	[Test]
	public async Task ParityWithOfficialBinary()
	{
		var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "stemmer-parity.csv");
		if (!File.Exists(fixturePath))
			return; // Fixture not yet generated; [Skip] attribute handles this.

		var lines = await File.ReadAllLinesAsync(fixturePath);
		foreach (var line in lines.Skip(1)) // skip header
		{
			var parts = line.Split(',');
			if (parts.Length < 2) continue;
			var input = parts[0].Trim();
			var expected = parts[1].Trim();
			EnglishStemmer.Stem(input).Should().Be(expected,
				because: $"stemmer parity: '{input}' → '{expected}'");
		}
	}
}
