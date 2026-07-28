using System.IO.Abstractions;

namespace Pagefind.Net;

/// <summary>
/// Accumulates <see cref="PagefindRecord"/> documents and writes a
/// Pagefind-compatible index directory when <see cref="WriteAsync"/> is called.
/// </summary>
/// <remarks>
/// <para>
/// Each call to <see cref="AddRecord"/> eagerly tokenizes, stems, and indexes
/// the record, then builds its fragment. The record's <c>Content</c> and
/// <c>WeightedSegments</c> are <b>not retained</b> after the call returns,
/// keeping memory usage constant regardless of corpus size.
/// </para>
/// <para>
/// The output directory will contain the index <em>data</em> files only:
/// <c>pagefind-entry.json</c>, <c>pagefind.*.pf_meta</c>,
/// <c>index/*.pf_index</c>, and <c>fragment/*.pf_fragment</c>.
/// </para>
/// <para>
/// The Pagefind query runtime (<c>pagefind.js</c> and <c>wasm.*.pagefind</c>)
/// must be obtained separately from the <c>pagefind</c> npm package.
/// </para>
/// </remarks>
public sealed class PagefindIndex
{
	/// <summary>
	/// The Pagefind release version this library targets. The emitted
	/// <c>pagefind-entry.json</c> declares this version so the JS runtime
	/// can verify compatibility.
	/// </summary>
	public const string PagefindTargetVersion = "1.5.2";

	private readonly PagefindIndexOptions _options;
	private readonly IFileSystem _fs;
	private readonly InvertedIndexBuilder _builder;
	private readonly FragmentBuilder _fragmentBuilder = new();
	private readonly List<IndexedPage> _pages = [];

	/// <summary>Initialises a new index with the given options.</summary>
	/// <param name="options">Index configuration.</param>
	/// <param name="fileSystem">
	/// File-system abstraction to use when writing output files.
	/// Pass <see langword="null"/> (the default) to use the real file system.
	/// Inject a custom implementation such as <c>Nullean.ScopedFileSystem</c>
	/// to sandbox or redirect all writes.
	/// </param>
	public PagefindIndex(PagefindIndexOptions? options = null, IFileSystem? fileSystem = null)
	{
		_options = options ?? new PagefindIndexOptions();
		_fs = fileSystem ?? new FileSystem();
		var tokenizer = new Tokenizer(_options.IncludeCharacters);
		var stemmer = new Stemmer(_options.Language);
		_builder = new InvertedIndexBuilder(tokenizer, stemmer);
	}

	/// <summary>
	/// Tokenizes, stems, and indexes the record immediately, then builds its
	/// search fragment. The record's <see cref="PagefindRecord.Content"/> and
	/// <see cref="PagefindRecord.WeightedSegments"/> are not retained after
	/// this call returns.
	/// </summary>
	/// <remarks>
	/// Thread-safe when called from a single producer thread;
	/// <see cref="WriteAsync"/> must not be called concurrently with
	/// <see cref="AddRecord"/>.
	/// </remarks>
	/// <exception cref="PagefindIndexingException">
	/// Wraps any exception thrown during tokenization or fragment building,
	/// attaching the record's <see cref="PagefindRecord.Url"/> and
	/// <see cref="PagefindRecord.Title"/> for error attribution.
	/// </exception>
	public void AddRecord(PagefindRecord record)
	{
		try
		{
			var pageIndex = _pages.Count;
			_builder.AddRecord(pageIndex, record);
			var (hash, bytes) = _fragmentBuilder.BuildFragment(record);
			var wordCount = CountWords(record.Content);
			_pages.Add(new IndexedPage(hash, bytes, wordCount));
		}
		catch (Exception ex) when (ex is not PagefindIndexingException)
		{
			throw new PagefindIndexingException(record.Url, record.Title, ex);
		}
	}

	/// <summary>
	/// Converts pre-parsed HTML page components into a <see cref="PagefindRecord"/>
	/// with weights matching the official Pagefind binary, then indexes it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The caller provides the parsed HTML components — headings with levels,
	/// body text sections, element IDs — and this method applies the official
	/// Pagefind weight scheme: h1 = 168, h2 = 144, h3 = 120, h4 = 96,
	/// h5 = 72, h6 = 48, body = 24.
	/// </para>
	/// <para>
	/// Content is constructed by joining section texts with <c>". "</c>
	/// separators (matching the official Pagefind binary's text extraction).
	/// </para>
	/// </remarks>
	public void AddHtmlRecord(HtmlPageData page)
	{
		var content = BuildContent(page.Sections);
		var title = FindTitle(page);
		var anchors = BuildAnchors(page.Sections, content);
		var segments = BuildSegments(page.Sections, content);

		var meta = new Dictionary<string, string>(page.Meta);
		if (!meta.ContainsKey("title"))
			meta["title"] = title;

		var record = new PagefindRecord
		{
			Url = page.Url,
			Title = title,
			Content = content,
			WeightedSegments = segments,
			Anchors = anchors,
			Meta = meta,
			Filters = page.Filters,
		};

		AddRecord(record);
	}

	private static string BuildContent(IReadOnlyList<HtmlSection> sections)
	{
		var sb = new System.Text.StringBuilder();
		foreach (var section in sections)
		{
			if (string.IsNullOrWhiteSpace(section.Text))
				continue;
			if (sb.Length > 0)
				sb.Append(". ");
			sb.Append(section.Text);
		}
		// Ensure trailing period if content doesn't end with one (matching pagefind).
		var result = sb.ToString();
		if (result.Length > 0 && !result.EndsWith('.'))
			result += '.';
		return result;
	}

	private static string FindTitle(HtmlPageData page)
	{
		if (page.Meta.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
			return title;

		// Use first H1 text as title.
		foreach (var section in page.Sections)
		{
			if (section.Tag.Equals("h1", StringComparison.OrdinalIgnoreCase))
				return section.Text;
		}

		return "";
	}

	private static IReadOnlyList<PagefindAnchor> BuildAnchors(
		IReadOnlyList<HtmlSection> sections, string content)
	{
		var anchors = new List<PagefindAnchor>();
		var wordOffset = 0;

		foreach (var section in sections)
		{
			if (string.IsNullOrWhiteSpace(section.Text))
				continue;

			var isHeading = section.Tag.Length == 2
				&& section.Tag[0] is 'h' or 'H'
				&& section.Tag[1] is >= '1' and <= '6';

			if (isHeading && section.ElementId is not null)
			{
				anchors.Add(new PagefindAnchor(section.ElementId, section.Text, wordOffset));
			}

			wordOffset += CountWords(section.Text);
		}

		return anchors;
	}

	private static IReadOnlyList<WeightedSegment> BuildSegments(
		IReadOnlyList<HtmlSection> sections, string content)
	{
		var segments = new List<WeightedSegment>();

		foreach (var section in sections)
		{
			if (string.IsNullOrWhiteSpace(section.Text))
				continue;

			var weight = PagefindWeights.ForTag(section.Tag);
			segments.Add(new WeightedSegment(section.Text, weight));
		}

		// Add the full content as a body-weight segment.
		segments.Add(new WeightedSegment(content, PagefindWeights.Body));

		return segments;
	}

	/// <summary>
	/// Finalizes the inverted index and writes all data files to
	/// <paramref name="outputDirectory"/>/pagefind/.
	/// No tokenization is performed — all CPU-intensive work was done during
	/// <see cref="AddRecord"/> calls. This method is a pure I/O flush.
	/// </summary>
	/// <param name="outputDirectory">
	/// Root of the site output (e.g. <c>wwwroot</c>). The index is written to
	/// <c>&lt;outputDirectory&gt;/pagefind/</c>.
	/// </param>
	/// <param name="ct">Cancellation token.</param>
	public async Task WriteAsync(string outputDirectory, CancellationToken ct = default)
	{
		var pagefindDir = Path.Combine(outputDirectory, "pagefind");
		_fs.Directory.CreateDirectory(pagefindDir);
		_fs.Directory.CreateDirectory(Path.Combine(pagefindDir, "index"));
		_fs.Directory.CreateDirectory(Path.Combine(pagefindDir, "fragment"));

		// 1. Finalize the inverted index (sort postings — no tokenization).
		var invertedIndex = _builder.Build();

		// 2. Write pre-built fragment files.
		var pageHashes = new string[_pages.Count];
		for (var i = 0; i < _pages.Count; i++)
		{
			ct.ThrowIfCancellationRequested();
			pageHashes[i] = _pages[i].FragmentHash;
			var path = Path.Combine(pagefindDir, "fragment", $"{pageHashes[i]}.pf_fragment");
			await PagefindWriter.WriteFramedAsync(_fs, path, _pages[i].FragmentBytes, ct);
		}

		// 3. Write index chunk files.
		var indexChunks = await PagefindWriter.WriteIndexChunksAsync(
			_fs, pagefindDir, invertedIndex, ct);

		// 4. Write the meta file.
		var wordCounts = new int[_pages.Count];
		for (var i = 0; i < _pages.Count; i++)
			wordCounts[i] = _pages[i].WordCount;

		var (metaHash, metaBytes) = PagefindWriter.BuildMetaCbor(
			PagefindTargetVersion,
			pageHashes,
			wordCounts,
			indexChunks);

		await PagefindWriter.WriteFramedAsync(
			_fs,
			Path.Combine(pagefindDir, $"pagefind.{metaHash}.pf_meta"),
			metaBytes, ct);

		// 5. Write pagefind-entry.json.
		await PagefindWriter.WriteEntryJsonAsync(
			_fs,
			pagefindDir,
			_options.Language,
			metaHash,
			_pages.Count,
			_options.IncludeCharacters,
			ct);
	}

	private static int CountWords(string content)
	{
		if (string.IsNullOrWhiteSpace(content))
			return 0;
		var count = 0;
		var inWord = false;
		foreach (var c in content)
		{
			if (char.IsWhiteSpace(c))
				inWord = false;
			else if (!inWord)
			{
				inWord = true;
				count++;
			}
		}
		return count;
	}

	/// <summary>
	/// Lightweight struct holding the pre-computed output for a single page.
	/// The original <see cref="PagefindRecord"/> is not retained.
	/// </summary>
	private readonly record struct IndexedPage(string FragmentHash, byte[] FragmentBytes, int WordCount);
}
