using Porter2StemmerStandard;

namespace Pagefind.Net;

/// <summary>
/// Wraps the Porter2StemmerStandard English stemmer behind a simple interface.
/// For languages without a Snowball stemmer the word is returned unchanged.
/// </summary>
internal sealed class Stemmer
{
	private readonly EnglishPorter2Stemmer? _stemmer;

	internal Stemmer(string language)
	{
		// Porter2 (Snowball English) is the same algorithm used by Pagefind's
		// pagefind_stem crate. Other languages are either unsupported or use
		// the same algorithm under a different locale code.
		_stemmer = language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
			? new EnglishPorter2Stemmer()
			: null;
	}

	/// <summary>
	/// Returns the stemmed form of <paramref name="word"/>, or the original
	/// word unchanged if no stemmer is available for the configured language.
	/// </summary>
	internal string Stem(string word)
	{
		if (_stemmer is null || word.Length == 0)
			return word;

		return _stemmer.Stem(word).Value;
	}
}
