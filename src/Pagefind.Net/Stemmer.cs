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
    /// Stems <paramref name="word"/> into <paramref name="destination"/>.
    /// Returns the number of chars written. Destination must be at least
    /// word.Length chars long.
    /// </summary>
    internal int Stem(ReadOnlySpan<char> word, Span<char> destination)
    {
        if (!_useEnglish || word.Length == 0)
        {
            word.CopyTo(destination);
            return word.Length;
        }
        return EnglishPorter2.Stem(word, destination);
    }

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
