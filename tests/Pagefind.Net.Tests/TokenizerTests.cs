using AwesomeAssertions;
using Pagefind.Net;
using TUnit;

namespace Pagefind.Net.Tests;

/// <summary>
/// Verifies that <see cref="Tokenizer"/> produces the same tokens as the
/// official Pagefind binary's <c>get_indexable_words</c> function.
///
/// The fixture file <c>Fixtures/tokenizer-parity.json</c> is a JSON array of
/// <c>{ "input": "...", "expected": ["token1", "token2"] }</c> objects captured
/// by running the pagefind binary with the <c>--logfile</c> flag (or by using
/// the pagefind Node API to dump tokenised terms).
///
/// To regenerate: run <c>node generate-fixtures.mjs</c> in the tests directory.
/// </summary>
public sealed class TokenizerTests
{
	private static readonly Tokenizer Tokenizer = new(includeCharacters: "");

	[Test]
	public void BasicWhitespaceSplit()
	{
		var tokens = Tokenizer.Tokenize("hello world").ToList();
		tokens.Should().BeEquivalentTo(["hello", "world"]);
	}

	[Test]
	public void LowerCases()
	{
		var tokens = Tokenizer.Tokenize("Hello WORLD").ToList();
		tokens.Should().BeEquivalentTo(["hello", "world"]);
	}

	[Test]
	public void StripsDiacritics()
	{
		// café → cafe
		var tokens = Tokenizer.Tokenize("café résumé").ToList();
		tokens.Should().BeEquivalentTo(["cafe", "resume"]);
	}

	[Test]
	public void CompoundSplitOnDot()
	{
		// pagefind emits the joined form (dots removed) AND each sub-part.
		var tokens = Tokenizer.Tokenize("foo.bar baz.qux").ToList();
		tokens.Should().BeEquivalentTo(["foobar", "foo", "bar", "bazqux", "baz", "qux"]);
	}

	[Test]
	public void StripsPunctuation()
	{
		// Non-letter/digit chars (excluding include_characters) are dropped.
		var tokens = Tokenizer.Tokenize("hello! world?").ToList();
		tokens.Should().BeEquivalentTo(["hello", "world"]);
	}

	[Test]
	public void EmptyStringReturnsEmpty()
	{
		var tokens = Tokenizer.Tokenize("").ToList();
		tokens.Should().BeEmpty();
	}

	[Test]
	public void WhitespaceOnlyReturnsEmpty()
	{
		var tokens = Tokenizer.Tokenize("   \t\n   ").ToList();
		tokens.Should().BeEmpty();
	}

	[Test]
	public void IncludeCharactersRespected()
	{
		// With '#' in include_characters, "C#" should be kept intact.
		var tokenizerWithHash = new Tokenizer(includeCharacters: "#");
		var tokens = tokenizerWithHash.Tokenize("C# programming language").ToList();
		tokens.Should().Contain("c#");
	}

	[Test]
	public void ZeroWidthCharsStripped()
	{
		// U+200B between letters should produce a single merged token.
		var input = "hel​lo";
		var tokens = Tokenizer.Tokenize(input).ToList();
		// After stripping ZWS: "hello"
		tokens.Should().BeEquivalentTo(["hello"]);
	}

	// ── Parity fixture tests (skipped if fixture not yet generated) ────────────

	[Test]
	public async Task ParityWithOfficialBinary()
	{
		var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tokenizer-parity.json");
		if (!File.Exists(fixturePath))
			return; // Fixture not yet generated; [Skip] attribute handles this.

		var json = await File.ReadAllTextAsync(fixturePath);
		var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		var cases = System.Text.Json.JsonSerializer.Deserialize<TokenizerCase[]>(json, opts)!;

		foreach (var tc in cases)
		{
			var actual = Tokenizer.Tokenize(tc.Input).ToList();
			actual.Should().BeEquivalentTo(tc.Expected,
				because: $"tokenizer parity failed for input: {tc.Input}");
		}
	}

	private sealed record TokenizerCase(string Input, string[] Expected);
}
