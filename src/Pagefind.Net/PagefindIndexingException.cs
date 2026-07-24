namespace Pagefind.Net;

/// <summary>
/// Thrown when tokenization or indexing of a <see cref="PagefindRecord"/> fails.
/// Wraps the original exception and carries the record's <see cref="Url"/> and
/// <see cref="Title"/> so the caller can identify the problematic page immediately.
/// </summary>
public class PagefindIndexingException : Exception
{
	/// <summary>URL of the record that failed indexing.</summary>
	public string Url { get; }

	/// <summary>Title of the record that failed indexing.</summary>
	public string Title { get; }

	public PagefindIndexingException(string url, string title, Exception inner)
		: base($"Failed to index '{url}' ({title})", inner)
	{
		Url = url;
		Title = title;
	}
}
