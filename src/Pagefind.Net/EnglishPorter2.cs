using System.Buffers;
using System.Collections.Frozen;

namespace Pagefind.Net;

/// <summary>
/// Hand-rolled Porter2 (Snowball English) stemmer.
/// Produces output identical to Pagefind's Rust snowball crate for English.
/// </summary>
internal static class EnglishPorter2
{
    // Words that bypass the full algorithm and return a fixed stem.
    private static readonly FrozenDictionary<string, string> Exceptions =
        new Dictionary<string, string>
        {
            ["skis"] = "ski", ["skies"] = "sky", ["dying"] = "die", ["lying"] = "lie",
            ["tying"] = "tie", ["idly"] = "idl", ["gently"] = "gentl", ["ugly"] = "ugli",
            ["early"] = "earli", ["only"] = "onli", ["singly"] = "singl", ["sky"] = "sky",
            ["news"] = "news", ["howe"] = "howe", ["atlas"] = "atlas", ["cosmos"] = "cosmos",
            ["bias"] = "bias", ["andes"] = "andes",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    // After step 1a these words stop (no further stemming).
    private static readonly FrozenSet<string> Exception2 =
        FrozenSet.ToFrozenSet(
        [
            "inning", "outing", "canning", "herring", "earring",
            "proceed", "exceed", "succeed",
        ], StringComparer.Ordinal);

    // Special-case prefixes that fix R1 = prefix.Length.
    private static readonly (string Prefix, int R1)[] R1Prefixes =
    [
        ("commun", 6), ("gener", 5), ("arsen", 5),
    ];

    // Chars that form a valid li-ending (char before "li").
    private static readonly SearchValues<char> LiEndingChars =
        SearchValues.Create("cdeghkmnrt");

    // Double consonants that trigger undoubling in step 1b (NOT l, s, z).
    private static readonly SearchValues<char> DoubleableChars =
        SearchValues.Create("bdfgmnprt");

    /// <summary>
    /// Stems <paramref name="word"/> into <paramref name="destination"/>.
    /// Returns the number of chars written. Destination must be at least
    /// <paramref name="word"/>.Length chars (stemming never grows a word).
    /// </summary>
    internal static int Stem(ReadOnlySpan<char> word, Span<char> destination)
    {
        if (word.Length <= 2)
        {
            word.CopyTo(destination);
            return word.Length;
        }

        var exceptionLookup = Exceptions.GetAlternateLookup<ReadOnlySpan<char>>();
        if (exceptionLookup.TryGetValue(word, out var exStem))
        {
            exStem.AsSpan().CopyTo(destination);
            return exStem.Length;
        }

        var buf = ArrayPool<char>.Shared.Rent(word.Length + 1);
        try
        {
            word.CopyTo(buf);
            var len = word.Length;

            MarkYs(buf, len);

            var r1 = ComputeR1(buf, len);
            var r2 = GetRegion(buf, len, r1);

            Step0(buf, ref len);
            Step1a(buf, ref len);

            var ex2Lookup = Exception2.GetAlternateLookup<ReadOnlySpan<char>>();
            if (ex2Lookup.Contains(new ReadOnlySpan<char>(buf, 0, len)))
                goto done;

            Step1b(buf, ref len, r1);
            Step1c(buf, ref len);
            Step2(buf, ref len, r1);
            Step3(buf, ref len, r1, r2);
            Step4(buf, ref len, r2);
            Step5(buf, ref len, r1, r2);

            done:
            for (var i = 0; i < len; i++)
                if (buf[i] == 'Y') buf[i] = 'y';

            new ReadOnlySpan<char>(buf, 0, len).CopyTo(destination);
            return len;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf);
        }
    }

    internal static string Stem(string word)
    {
        if (word.Length <= 2) return word;

        var exceptionLookup = Exceptions.GetAlternateLookup<ReadOnlySpan<char>>();
        if (exceptionLookup.TryGetValue(word.AsSpan(), out var exStem))
            return exStem;

        var buf = ArrayPool<char>.Shared.Rent(word.Length + 1);
        try
        {
            word.AsSpan().CopyTo(buf);
            var len = word.Length;

            MarkYs(buf, len);

            var r1 = ComputeR1(buf, len);
            var r2 = GetRegion(buf, len, r1);

            Step0(buf, ref len);
            Step1a(buf, ref len);

            var ex2Lookup = Exception2.GetAlternateLookup<ReadOnlySpan<char>>();
            if (ex2Lookup.Contains(new ReadOnlySpan<char>(buf, 0, len)))
                goto done;

            Step1b(buf, ref len, r1);
            Step1c(buf, ref len);
            Step2(buf, ref len, r1);
            Step3(buf, ref len, r1, r2);
            Step4(buf, ref len, r2);
            Step5(buf, ref len, r1, r2);

            done:
            for (var i = 0; i < len; i++)
                if (buf[i] == 'Y') buf[i] = 'y';

            return new string(buf, 0, len);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buf);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
    private static bool IsConsonant(char c) => !IsVowel(c);

    private static void MarkYs(char[] buf, int len)
    {
        if (len > 0 && buf[0] == 'y') buf[0] = 'Y';
        for (var i = 1; i < len; i++)
            if (buf[i] == 'y' && IsVowel(buf[i - 1]))
                buf[i] = 'Y';
    }

    // Region: position after the first non-vowel following a vowel, from 'begin'.
    private static int GetRegion(char[] buf, int len, int begin)
    {
        var i = begin;
        while (i < len && !IsVowel(buf[i])) i++;
        while (i < len) { if (IsConsonant(buf[i])) return i + 1; i++; }
        return len;
    }

    private static int ComputeR1(char[] buf, int len)
    {
        var word = new ReadOnlySpan<char>(buf, 0, len);
        foreach (var (prefix, r1) in R1Prefixes)
            if (word.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
                return r1;
        return GetRegion(buf, len, 0);
    }

    // Suffix is "in R1" when r1 <= len - suffixLen (suffix starts at or after r1).
    private static bool InR(int r, int len, int suffixLen) => r <= len - suffixLen;

    // True when the word (up to 'len') contains at least one vowel.
    private static bool HasVowel(char[] buf, int len)
    {
        for (var i = 0; i < len; i++)
            if (IsVowel(buf[i])) return true;
        return false;
    }

    // Short syllable: (A) word-end: C-V-C where last C is not w,x,Y;
    //                 (B) word of length 2: V-C.
    private static bool EndsShortSyllable(char[] buf, int len)
    {
        if (len < 2) return false;
        if (len == 2) return IsVowel(buf[0]) && IsConsonant(buf[1]);
        return IsVowel(buf[len - 2])
               && IsConsonant(buf[len - 1])
               && buf[len - 1] is not ('w' or 'x' or 'Y')
               && IsConsonant(buf[len - 3]);
    }

    private static bool IsShortWord(char[] buf, int len, int r1) =>
        EndsShortSyllable(buf, len) && r1 == len;

    // Replace last 'suffixLen' chars with 'replacement'; update len.
    private static void Replace(char[] buf, ref int len, int suffixLen, ReadOnlySpan<char> replacement)
    {
        var newLen = len - suffixLen + replacement.Length;
        replacement.CopyTo(buf.AsSpan(len - suffixLen));
        len = newLen;
    }

    private static void Delete(char[] buf, ref int len, int suffixLen) =>
        len -= suffixLen;

    private static bool EndsWith(char[] buf, int len, string suffix)
    {
        if (suffix.Length > len) return false;
        for (int i = 0, j = len - suffix.Length; i < suffix.Length; i++, j++)
            if (buf[j] != suffix[i]) return false;
        return true;
    }

    // ── Steps ──────────────────────────────────────────────────────────────────

    private static void Step0(char[] buf, ref int len)
    {
        if (EndsWith(buf, len, "'s'"))  { Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "'s"))   { Delete(buf, ref len, 2); return; }
        if (EndsWith(buf, len, "'"))    { Delete(buf, ref len, 1); }
    }

    private static void Step1a(char[] buf, ref int len)
    {
        if (EndsWith(buf, len, "sses")) { Delete(buf, ref len, 2); return; }

        if (EndsWith(buf, len, "ied") || EndsWith(buf, len, "ies"))
        {
            Delete(buf, ref len, len > 4 ? 2 : 1);
            return;
        }

        if (EndsWith(buf, len, "us") || EndsWith(buf, len, "ss")) return;

        if (len > 0 && buf[len - 1] == 's')
        {
            // Delete 's' if (a) char before s is not 's', and (b) stem has a vowel.
            var stemLen = len - 1;
            if (stemLen > 0 && buf[stemLen - 1] != 's' && HasVowel(buf, stemLen))
                Delete(buf, ref len, 1);
        }
    }

    private static void Step1b(char[] buf, ref int len, int r1)
    {
        if (EndsWith(buf, len, "eedly")) { if (InR(r1, len, 5)) Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "eed"))   { if (InR(r1, len, 3)) Delete(buf, ref len, 1); return; }

        int trimLen;
        if      (EndsWith(buf, len, "ingly")) trimLen = 5;
        else if (EndsWith(buf, len, "edly"))  trimLen = 4;
        else if (EndsWith(buf, len, "ing"))   trimLen = 3;
        else if (EndsWith(buf, len, "ed"))    trimLen = 2;
        else return;

        var stemLen2 = len - trimLen;
        if (!HasVowel(buf, stemLen2)) return;

        Delete(buf, ref len, trimLen);

        if (EndsWith(buf, len, "at") || EndsWith(buf, len, "bl") || EndsWith(buf, len, "iz"))
        {
            buf[len++] = 'e';
        }
        else if (len >= 2 && DoubleableChars.Contains(buf[len - 1]) && buf[len - 1] == buf[len - 2])
        {
            Delete(buf, ref len, 1);
        }
        else if (IsShortWord(buf, len, r1))
        {
            buf[len++] = 'e';
        }
    }

    private static void Step1c(char[] buf, ref int len)
    {
        if (len < 2) return;
        var last = buf[len - 1];
        if ((last == 'y' || last == 'Y') && IsConsonant(buf[len - 2]))
            buf[len - 1] = 'i';
    }

    private static void Step2(char[] buf, ref int len, int r1)
    {
        // Checked from longest to shortest; first match wins.
        if (EndsWith(buf, len, "ization")  && InR(r1, len, 7)) { Replace(buf, ref len, 7, "ize"); return; }
        if (EndsWith(buf, len, "ational")  && InR(r1, len, 7)) { Replace(buf, ref len, 7, "ate"); return; }
        if (EndsWith(buf, len, "ousness")  && InR(r1, len, 7)) { Replace(buf, ref len, 7, "ous"); return; }
        if (EndsWith(buf, len, "iveness")  && InR(r1, len, 7)) { Replace(buf, ref len, 7, "ive"); return; }
        if (EndsWith(buf, len, "fulness")  && InR(r1, len, 7)) { Replace(buf, ref len, 7, "ful"); return; }
        if (EndsWith(buf, len, "tional")   && InR(r1, len, 6)) { Replace(buf, ref len, 6, "tion"); return; }
        if (EndsWith(buf, len, "lessli")   && InR(r1, len, 6)) { Replace(buf, ref len, 6, "less"); return; }
        if (EndsWith(buf, len, "biliti")   && InR(r1, len, 6)) { Replace(buf, ref len, 6, "ble"); return; }
        if (EndsWith(buf, len, "entli")    && InR(r1, len, 5)) { Replace(buf, ref len, 5, "ent"); return; }
        if (EndsWith(buf, len, "ation")    && InR(r1, len, 5)) { Replace(buf, ref len, 5, "ate"); return; }
        if (EndsWith(buf, len, "alism")    && InR(r1, len, 5)) { Replace(buf, ref len, 5, "al"); return; }
        if (EndsWith(buf, len, "aliti")    && InR(r1, len, 5)) { Replace(buf, ref len, 5, "al"); return; }
        if (EndsWith(buf, len, "fulli")    && InR(r1, len, 5)) { Replace(buf, ref len, 5, "ful"); return; }
        if (EndsWith(buf, len, "ousli")    && InR(r1, len, 5)) { Replace(buf, ref len, 5, "ous"); return; }
        if (EndsWith(buf, len, "iviti")    && InR(r1, len, 5)) { Replace(buf, ref len, 5, "ive"); return; }
        if (EndsWith(buf, len, "enci")     && InR(r1, len, 4)) { Replace(buf, ref len, 4, "ence"); return; }
        if (EndsWith(buf, len, "anci")     && InR(r1, len, 4)) { Replace(buf, ref len, 4, "ance"); return; }
        if (EndsWith(buf, len, "abli")     && InR(r1, len, 4)) { Replace(buf, ref len, 4, "able"); return; }
        if (EndsWith(buf, len, "izer")     && InR(r1, len, 4)) { Replace(buf, ref len, 4, "ize"); return; }
        if (EndsWith(buf, len, "ator")     && InR(r1, len, 4)) { Replace(buf, ref len, 4, "ate"); return; }
        if (EndsWith(buf, len, "alli")     && InR(r1, len, 4)) { Replace(buf, ref len, 4, "al"); return; }
        if (EndsWith(buf, len, "bli")      && InR(r1, len, 3)) { Replace(buf, ref len, 3, "ble"); return; }
        if (EndsWith(buf, len, "ogi")      && InR(r1, len, 3) && len >= 4 && buf[len - 4] == 'l') { Replace(buf, ref len, 3, "og"); return; }
        if (EndsWith(buf, len, "li")       && InR(r1, len, 2) && len >= 3 && LiEndingChars.Contains(buf[len - 3])) { Delete(buf, ref len, 2); }
    }

    private static void Step3(char[] buf, ref int len, int r1, int r2)
    {
        if (EndsWith(buf, len, "ational") && InR(r1, len, 7)) { Replace(buf, ref len, 7, "ate"); return; }
        if (EndsWith(buf, len, "tional")  && InR(r1, len, 6)) { Replace(buf, ref len, 6, "tion"); return; }
        if (EndsWith(buf, len, "alize")   && InR(r1, len, 5)) { Replace(buf, ref len, 5, "al"); return; }
        if (EndsWith(buf, len, "icate")   && InR(r1, len, 5)) { Replace(buf, ref len, 5, "ic"); return; }
        if (EndsWith(buf, len, "iciti")   && InR(r1, len, 5)) { Replace(buf, ref len, 5, "ic"); return; }
        if (EndsWith(buf, len, "ical")    && InR(r1, len, 4)) { Replace(buf, ref len, 4, "ic"); return; }
        if (EndsWith(buf, len, "ness")    && InR(r1, len, 4)) { Delete(buf, ref len, 4); return; }
        if (EndsWith(buf, len, "ful")     && InR(r1, len, 3)) { Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "ative")   && InR(r1, len, 5) && InR(r2, len, 5)) { Delete(buf, ref len, 5); }
    }

    private static void Step4(char[] buf, ref int len, int r2)
    {
        // Per the Porter2 spec: find the LONGEST matching suffix. If found and in R2,
        // delete it. If found but NOT in R2, do nothing (do not try shorter suffixes).
        if (EndsWith(buf, len, "ement")) { if (InR(r2, len, 5)) Delete(buf, ref len, 5); return; }
        if (EndsWith(buf, len, "ment"))  { if (InR(r2, len, 4)) Delete(buf, ref len, 4); return; }
        if (EndsWith(buf, len, "ance"))  { if (InR(r2, len, 4)) Delete(buf, ref len, 4); return; }
        if (EndsWith(buf, len, "ence"))  { if (InR(r2, len, 4)) Delete(buf, ref len, 4); return; }
        if (EndsWith(buf, len, "able"))  { if (InR(r2, len, 4)) Delete(buf, ref len, 4); return; }
        if (EndsWith(buf, len, "ible"))  { if (InR(r2, len, 4)) Delete(buf, ref len, 4); return; }
        if (EndsWith(buf, len, "ant"))   { if (InR(r2, len, 3)) Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "ent"))   { if (InR(r2, len, 3)) Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "ism"))   { if (InR(r2, len, 3)) Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "ate"))   { if (InR(r2, len, 3)) Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "iti"))   { if (InR(r2, len, 3)) Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "ous"))   { if (InR(r2, len, 3)) Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "ive"))   { if (InR(r2, len, 3)) Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "ize"))   { if (InR(r2, len, 3)) Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "ion"))   { if (len >= 4 && (buf[len - 4] == 's' || buf[len - 4] == 't') && InR(r2, len, 3)) Delete(buf, ref len, 3); return; }
        if (EndsWith(buf, len, "al"))    { if (InR(r2, len, 2)) Delete(buf, ref len, 2); return; }
        if (EndsWith(buf, len, "er"))    { if (InR(r2, len, 2)) Delete(buf, ref len, 2); return; }
        if (EndsWith(buf, len, "ic"))    { if (InR(r2, len, 2)) Delete(buf, ref len, 2); }
    }

    private static void Step5(char[] buf, ref int len, int r1, int r2)
    {
        if (len == 0) return;
        if (buf[len - 1] == 'e')
        {
            if (InR(r2, len, 1) || (InR(r1, len, 1) && !EndsShortSyllable(buf, len - 1)))
                Delete(buf, ref len, 1);
        }
        else if (buf[len - 1] == 'l' && InR(r2, len, 1) && len >= 2 && buf[len - 2] == 'l')
        {
            Delete(buf, ref len, 1);
        }
    }
}
