namespace Pagefind.Net;

/// <summary>
/// Wraps the Porter2 (Snowball English) stemmer behind a simple interface.
/// For languages without a Snowball stemmer the word is returned unchanged.
/// </summary>
internal sealed class Stemmer
{
    private readonly bool _useEnglish;

    internal Stemmer(string language) =>
        _useEnglish = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the stemmed form of <paramref name="word"/>, or the original
    /// word unchanged if no stemmer is available for the configured language.
    /// </summary>
    internal string Stem(string word)
    {
        if (!_useEnglish || word.Length == 0)
            return word;

        return EnglishPorter2.Stem(word);
    }
}
