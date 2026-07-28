using System.Buffers;
using System.Globalization;
using System.Text;

namespace Pagefind.Net;

/// <summary>
/// Callback interface for the push-based tokeniser. Implementors receive
/// each normalised token as a span — no string is allocated by the tokeniser.
/// </summary>
internal interface ITokenSink
{
    void OnToken(scoped ReadOnlySpan<char> token);

    /// <summary>
    /// Called for each sub-word produced by compound splitting (PascalCase, dot, punctuation).
    /// These share the same position as the primary token. Default forwards to OnToken.
    /// </summary>
    void OnCompoundPart(scoped ReadOnlySpan<char> token) => OnToken(token);

    /// <summary>
    /// Called once per whitespace-delimited word in the source text, BEFORE any
    /// tokens from that word are emitted. This lets consumers track positions
    /// by source word (as pagefind's frontend does for excerpt highlighting)
    /// rather than by emitted token count.
    /// </summary>
    void OnWordBoundary() { }
}

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

    /// <summary>
    /// Pagefind's default connector characters (Unicode Pc category).
    /// Always included during tokenisation, matching the Rust binary's behaviour.
    /// </summary>
    private const string DefaultConnectorChars = "_\u203F\u2040\u2054\uFE33\uFE34\uFE4D\uFE4E\uFE4F\uFF3F";

    private readonly SearchValues<char> _includeChars;

    internal Tokenizer(string includeCharacters) =>
        _includeChars = SearchValues.Create(string.Concat(DefaultConnectorChars, includeCharacters).AsSpan());

    /// <summary>
    /// Push-based tokenisation: each normalised token is delivered to
    /// <paramref name="sink"/> as a <see cref="ReadOnlySpan{T}"/> with zero
    /// per-token string allocations.
    /// </summary>
    internal void Tokenize<TSink>(ReadOnlySpan<char> text, ref TSink sink)
        where TSink : ITokenSink, allows ref struct
    {
        if (text.IsEmpty)
            return;

        Span<char> normBuf = stackalloc char[256];

        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || char.IsWhiteSpace(text[i]))
            {
                if (i > start)
                {
                    sink.OnWordBoundary();
                    ProcessWord(text[start..i], ref sink, normBuf);
                }
                start = i + 1;
            }
        }
    }


    private void ProcessWord<TSink>(ReadOnlySpan<char> word, ref TSink sink, scoped Span<char> normBuf)
        where TSink : ITokenSink, allows ref struct
    {
        // Ensure the output buffer can hold the normalized result.
        // After NFD + StripNonSpacingMarks the output is typically ≤ word.Length,
        // but we need headroom for intermediate NFD expansion of certain ligatures.
        char[]? rented = null;
        if (word.Length > normBuf.Length)
        {
            rented = ArrayPool<char>.Shared.Rent(word.Length * 2);
            normBuf = rented.AsSpan();
        }
        try
        {
            // Emit the primary (joined) normalized form. Normalize strips dots,
            // punctuation, and diacritics producing the joined compound form.
            var len = Normalize(word, normBuf);
            if (len > 0) sink.OnToken(normBuf[..len]);

            // If the word is possibly compound (contains punctuation or PascalCase),
            // split into discrete parts and emit each as a compound part.
            if (IsPossiblyCompound(word))
                EmitCompoundParts(word, ref sink, normBuf);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Returns true if the word contains punctuation (non-letter/digit, excluding
    /// zero-width chars) or has uppercase letters after the first character,
    /// indicating it may be a compound word.
    /// </summary>
    private static bool IsPossiblyCompound(ReadOnlySpan<char> word)
    {
        for (var i = 0; i < word.Length; i++)
        {
            if (ZeroWidthChars.Contains(word[i])) continue;
            if (!char.IsLetterOrDigit(word[i])) return true;
            if (i > 0 && char.IsUpper(word[i])) return true;
        }
        return false;
    }

    /// <summary>
    /// Splits a compound word into discrete parts (handling punctuation boundaries and
    /// PascalCase) and emits each via OnCompoundPart. Matches pagefind's get_discrete_words
    /// which replaces ASCII punctuation with spaces then applies convert_case Case::Lower.
    /// </summary>
    private void EmitCompoundParts<TSink>(ReadOnlySpan<char> word, ref TSink sink, scoped Span<char> normBuf)
        where TSink : ITokenSink, allows ref struct
    {
        // Collect split points. We identify "segments" (contiguous runs of letters/digits)
        // separated by punctuation, then within each segment apply PascalCase splitting.
        Span<int> splitPoints = stackalloc int[word.Length + 1];
        var splitCount = 0;

        var segStart = -1;
        for (var i = 0; i <= word.Length; i++)
        {
            var isWordChar = i < word.Length && char.IsLetterOrDigit(word[i]);
            if (isWordChar && segStart < 0)
                segStart = i;
            else if (!isWordChar && segStart >= 0)
            {
                // End of a segment — apply PascalCase splitting within it
                SplitPascalCase(word, segStart, i, splitPoints, ref splitCount);
                segStart = -1;
            }
        }

        if (splitCount <= 1) return; // Only 1 part means nothing to split

        // Emit each part via OnCompoundPart
        for (var p = 0; p < splitCount; p++)
        {
            var start = splitPoints[p];
            var end = (p + 1 < splitCount) ? splitPoints[p + 1] : word.Length;

            // Find the actual end: part extends to next split or next non-wordchar
            var partEnd = end;
            for (var k = start; k < end; k++)
            {
                if (!char.IsLetterOrDigit(word[k]))
                {
                    partEnd = k;
                    break;
                }
            }

            var part = word[start..partEnd];
            if (part.Length <= 1) continue;

            var partLen = Normalize(part, normBuf);
            if (partLen > 1)
                sink.OnCompoundPart(normBuf[..partLen]);
        }
    }

    /// <summary>
    /// Applies PascalCase splitting rules (matching convert_case Case::Lower) to a segment
    /// of the word between segStart and segEnd. Appends split start positions to splitPoints.
    /// Rules:
    ///   - lowercase/digit → uppercase: split before uppercase
    ///   - uppercase run + lowercase: split before last uppercase in the run
    ///   - letter → digit: split before digit
    ///   - digit → letter: split before letter
    /// </summary>
    private static void SplitPascalCase(ReadOnlySpan<char> word, int segStart, int segEnd, Span<int> splitPoints, ref int splitCount)
    {
        splitPoints[splitCount++] = segStart;

        for (var i = segStart + 1; i < segEnd; i++)
        {
            var prev = word[i - 1];
            var curr = word[i];

            var prevUpper = char.IsUpper(prev);
            var prevLower = char.IsLower(prev);
            var prevDigit = char.IsDigit(prev);
            var currUpper = char.IsUpper(curr);
            var currLower = char.IsLower(curr);
            var currDigit = char.IsDigit(curr);

            // lowercase/digit → uppercase: "pageFind" → split before 'F'
            if ((prevLower || prevDigit) && currUpper)
            {
                splitPoints[splitCount++] = i;
            }
            // letter → digit: "page2" → split before '2'
            else if ((prevUpper || prevLower) && currDigit)
            {
                splitPoints[splitCount++] = i;
            }
            // digit → letter: "2page" → split before 'p'
            else if (prevDigit && (currUpper || currLower))
            {
                splitPoints[splitCount++] = i;
            }
            // uppercase run + lowercase: "WKWeb" → split before last uppercase ('W')
            else if (prevUpper && currLower && i >= segStart + 2 && char.IsUpper(word[i - 2]))
            {
                splitPoints[splitCount++] = i - 1;
            }
        }
    }

    // Normalize a span that is known to contain no dots. Writes result into output.
    private int Normalize(ReadOnlySpan<char> word, Span<char> output)
    {
        var buf = ArrayPool<char>.Shared.Rent(word.Length * 2);
        try
        {
            var len = CopyLoweredStripped(word, buf);
            if (len == 0) return 0;

            var nfd = new string(buf, 0, len).Normalize(NormalizationForm.FormD);
            return StripNonSpacingMarks(nfd.AsSpan(), _includeChars, output);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf);
        }
    }


    // Copies word into buf: lowercased, zero-width chars removed.
    private static int CopyLoweredStripped(ReadOnlySpan<char> word, char[] buf)
    {
        var len = 0;
        foreach (var c in word)
        {
            if (ZeroWidthChars.Contains(c)) continue;
            buf[len++] = char.ToLowerInvariant(c);
        }
        return len;
    }

    // Remove NFD non-spacing marks (diacritics) and chars that are not letters/digits/includeChars.
    // Writes into output span, returns length written.
    private static int StripNonSpacingMarks(ReadOnlySpan<char> nfd, SearchValues<char> includeChars, Span<char> output)
    {
        var len = 0;
        foreach (var c in nfd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (!char.IsLetterOrDigit(c) && !includeChars.Contains(c))
                continue;
            if (len >= output.Length)
                break;
            output[len++] = c;
        }
        return len;
    }

}
