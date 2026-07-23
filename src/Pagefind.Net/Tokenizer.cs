using System.Buffers;
using System.Globalization;
using System.Text;

namespace Pagefind.Net;

/// <summary>
/// Reproduces Pagefind's <c>get_indexable_words</c> tokenisation pipeline so
/// that .NET-generated tokens match those produced by the WASM query-time
/// tokeniser (which re-runs the same algorithm on search queries).
/// </summary>
internal sealed class Tokenizer
{
    // Zero-width characters stripped before normalisation (mirrors pagefind's zero_width_chars set).
    // U+200B ZERO WIDTH SPACE, U+200C ZWNJ, U+200D ZWJ, U+FEFF BOM, U+00AD SOFT HYPHEN
    private static readonly SearchValues<char> ZeroWidthChars =
        SearchValues.Create("​‌‍﻿­");

    private readonly SearchValues<char> _includeChars;

    internal Tokenizer(string includeCharacters) =>
        _includeChars = SearchValues.Create(includeCharacters.AsSpan());

    /// <summary>
    /// Tokenises <paramref name="text"/> exactly as Pagefind would, returning
    /// lower-cased, diacritic-normalised, stemmer-ready word tokens.
    /// </summary>
    internal IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var result = new List<string>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || char.IsWhiteSpace(text[i]))
            {
                if (i > start)
                    ProcessWord(text.AsSpan(start, i - start), result);
                start = i + 1;
            }
        }
        return result;
    }

    // Handles one whitespace-delimited token; emits into `result`.
    private void ProcessWord(ReadOnlySpan<char> word, List<string> result)
    {
        var dotIdx = word.IndexOf('.');
        if (dotIdx >= 0)
        {
            // Compound split on '.': emit joined form (dots removed) PLUS each sub-part.
            // This mirrors pagefind's behaviour: "foo.bar" → ["foobar", "foo", "bar"].
            var joined = NormalizeNoDot(word);
            if (joined.Length > 0) result.Add(joined);

            var left = word[..dotIdx];
            if (left.Length > 0) ProcessWord(left, result);

            var right = word[(dotIdx + 1)..];
            if (right.Length > 0) ProcessWord(right, result);
            return;
        }

        var normalized = Normalize(word);
        if (normalized.Length > 0) result.Add(normalized);
    }

    // Normalize a span that is known to contain no dots.
    private string Normalize(ReadOnlySpan<char> word)
    {
        var buf = ArrayPool<char>.Shared.Rent(word.Length * 2); // extra for NFD expansion
        try
        {
            var len = CopyLoweredStripped(word, buf, stripDots: false);
            if (len == 0) return string.Empty;

            var nfd = new string(buf, 0, len).Normalize(NormalizationForm.FormD);
            return StripNonSpacingMarks(nfd.AsSpan(), _includeChars);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf);
        }
    }

    // Normalize and also strip dots (used for the joined compound form).
    private string NormalizeNoDot(ReadOnlySpan<char> word)
    {
        var buf = ArrayPool<char>.Shared.Rent(word.Length * 2);
        try
        {
            var len = CopyLoweredStripped(word, buf, stripDots: true);
            if (len == 0) return string.Empty;

            var nfd = new string(buf, 0, len).Normalize(NormalizationForm.FormD);
            return StripNonSpacingMarks(nfd.AsSpan(), _includeChars);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf);
        }
    }

    // Copies word into buf: lowercased, zero-width chars removed, optionally dots removed.
    private static int CopyLoweredStripped(ReadOnlySpan<char> word, char[] buf, bool stripDots)
    {
        var len = 0;
        foreach (var c in word)
        {
            if (ZeroWidthChars.Contains(c)) continue;
            if (stripDots && c == '.') continue;
            buf[len++] = char.ToLowerInvariant(c);
        }
        return len;
    }

    // Remove NFD non-spacing marks (diacritics) and chars that are not letters/digits/includeChars.
    private static string StripNonSpacingMarks(ReadOnlySpan<char> nfd, SearchValues<char> includeChars)
    {
        var buf = ArrayPool<char>.Shared.Rent(nfd.Length);
        try
        {
            var len = 0;
            foreach (var c in nfd)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                if (!char.IsLetterOrDigit(c) && !includeChars.Contains(c))
                    continue;
                buf[len++] = c;
            }
            return len > 0 ? new string(buf, 0, len) : string.Empty;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf);
        }
    }
}
