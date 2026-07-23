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
	// Zero-width characters to strip (mirrors pagefind's zero_width_chars set).
	private static readonly HashSet<int> ZeroWidthCodePoints =
	[
		0x200B, // ZERO WIDTH SPACE
		0x200C, // ZERO WIDTH NON-JOINER
		0x200D, // ZERO WIDTH JOINER
		0xFEFF, // ZERO WIDTH NO-BREAK SPACE (BOM)
		0x00AD, // SOFT HYPHEN
	];

	private readonly HashSet<char> _includeChars;

	internal Tokenizer(string includeCharacters)
	{
		_includeChars = new HashSet<char>(includeCharacters);
	}

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
					ProcessWord(text, start, i - start, result);
				start = i + 1;
			}
		}
		return result;
	}

	// Not an iterator: accumulates into `result` to avoid ReadOnlySpan-across-yield issues.
	private void ProcessWord(string text, int offset, int length, List<string> result)
	{
		// Compound split on '.': emit the joined form (dots removed) PLUS each sub-part.
		// This mirrors pagefind's behaviour: "foo.bar" → ["foobar", "foo", "bar"].
		var dotIdx = text.IndexOf('.', offset, length);
		if (dotIdx >= 0)
		{
			var raw = text.Substring(offset, length).Replace(".", "");
			var joined = Normalize(raw, 0, raw.Length);
			if (joined.Length > 0)
				result.Add(joined);

			var leftLen = dotIdx - offset;
			if (leftLen > 0)
				ProcessWord(text, offset, leftLen, result);
			var rightStart = dotIdx + 1;
			var rightLen = offset + length - rightStart;
			if (rightLen > 0)
				ProcessWord(text, rightStart, rightLen, result);
			return;
		}

		var normalized = Normalize(text, offset, length);
		if (normalized.Length > 0)
			result.Add(normalized);
	}

	/// <summary>
	/// Strips zero-width chars, applies NFD diacritic removal, filters
	/// non-letter/digit/include chars, and lower-cases the result.
	/// </summary>
	private string Normalize(string text, int offset, int length)
	{
		var sb = new StringBuilder(length);
		for (var i = offset; i < offset + length; i++)
		{
			var c = text[i];
			var cp = (int)c;
			if (ZeroWidthCodePoints.Contains(cp))
				continue;
			sb.Append(char.ToLowerInvariant(c));
		}

		if (sb.Length == 0)
			return string.Empty;

		// NFD normalise to decompose diacritics, then strip Mn (non-spacing mark) chars.
		var nfd = sb.ToString().Normalize(NormalizationForm.FormD);
		sb.Clear();
		foreach (var c in nfd)
		{
			var cat = CharUnicodeInfo.GetUnicodeCategory(c);
			if (cat == UnicodeCategory.NonSpacingMark)
				continue;
			if (!char.IsLetterOrDigit(c) && !_includeChars.Contains(c))
				continue;
			sb.Append(c);
		}

		return sb.Length > 0 ? sb.ToString() : string.Empty;
	}
}
