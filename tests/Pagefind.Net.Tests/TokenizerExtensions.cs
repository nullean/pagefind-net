using Pagefind.Net;

namespace Pagefind.Net.Tests;

/// <summary>
/// Test-only extension that collects tokeniser output into a <see cref="List{T}"/>
/// of strings for easy assertion. The production path uses the zero-alloc
/// <see cref="ITokenSink"/> push API directly.
/// </summary>
internal static class TokenizerExtensions
{
	internal static List<string> Tokenize(this Tokenizer tokenizer, string text)
	{
		if (string.IsNullOrEmpty(text))
			return [];

		var result = new List<string>();
		var sink = new ListSink(result);
		tokenizer.Tokenize(text.AsSpan(), ref sink);
		return result;
	}

	private struct ListSink(List<string> list) : ITokenSink
	{
		public void OnWordBoundary() { }
		public void OnToken(scoped ReadOnlySpan<char> token) => list.Add(new string(token));
	}
}
