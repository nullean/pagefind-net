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

    private readonly SearchValues<char> _includeChars;

    internal Tokenizer(string includeCharacters) =>
        _includeChars = SearchValues.Create(includeCharacters.AsSpan());

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
            var dotIdx = word.IndexOf('.');
            if (dotIdx >= 0)
            {
                // Compound split on '.': emit joined form (dots removed) PLUS each sub-part.
                // This mirrors pagefind's behaviour: "foo.bar" → ["foobar", "foo", "bar"].
                var joinedLen = NormalizeNoDot(word, normBuf);
                if (joinedLen > 0) sink.OnToken(normBuf[..joinedLen]);

                var left = word[..dotIdx];
                if (left.Length > 0) ProcessWord(left, ref sink, normBuf);

                var right = word[(dotIdx + 1)..];
                if (right.Length > 0) ProcessWord(right, ref sink, normBuf);
                return;
            }

            var len = Normalize(word, normBuf);
            if (len > 0) sink.OnToken(normBuf[..len]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    // Normalize a span that is known to contain no dots. Writes result into output.
    private int Normalize(ReadOnlySpan<char> word, Span<char> output)
    {
        var buf = ArrayPool<char>.Shared.Rent(word.Length * 2);
        try
        {
            var len = CopyLoweredStripped(word, buf, stripDots: false);
            if (len == 0) return 0;

            var nfd = new string(buf, 0, len).Normalize(NormalizationForm.FormD);
            return StripNonSpacingMarks(nfd.AsSpan(), _includeChars, output);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf);
        }
    }

    // Normalize and also strip dots (used for the joined compound form).
    private int NormalizeNoDot(ReadOnlySpan<char> word, Span<char> output)
    {
        var buf = ArrayPool<char>.Shared.Rent(word.Length * 2);
        try
        {
            var len = CopyLoweredStripped(word, buf, stripDots: true);
            if (len == 0) return 0;

            var nfd = new string(buf, 0, len).Normalize(NormalizationForm.FormD);
            return StripNonSpacingMarks(nfd.AsSpan(), _includeChars, output);
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
