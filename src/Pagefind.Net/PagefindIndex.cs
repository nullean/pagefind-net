using System.Collections.Concurrent;
using System.IO.Abstractions;

namespace Pagefind.Net;

/// <summary>
/// Accumulates <see cref="PagefindRecord"/> documents and writes a
/// Pagefind-compatible index directory when <see cref="WriteAsync"/> is called.
/// </summary>
/// <remarks>
/// <para>
/// Each call to <see cref="AddRecord"/> eagerly tokenizes and stems the record
/// and builds its fragment. Tokenized results are queued and merged into the
/// inverted index in batches (see <see cref="PagefindIndexOptions.MergeBatchSize"/>),
/// keeping <see cref="AddRecord"/> lock-free most of the time while bounding
/// memory to at most one batch of pending results. The record's <c>Content</c>
/// and <c>WeightedSegments</c> are <b>not retained</b> after the call returns.
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
	private readonly ConcurrentQueue<PendingPage> _pending = new();
	private readonly Lock _mergeLock = new();
	private readonly int _batchSize;

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
		_batchSize = Math.Max(1, _options.MergeBatchSize);
		var tokenizer = new Tokenizer(_options.IncludeCharacters);
		var stemmer = new Stemmer(_options.Language);
		_builder = new InvertedIndexBuilder(tokenizer, stemmer);
	}

	/// <summary>
	/// Tokenizes, stems, and queues the record for indexing, then builds its
	/// search fragment. The record's <see cref="PagefindRecord.Content"/> and
	/// <see cref="PagefindRecord.WeightedSegments"/> are not retained after
	/// this call returns.
	/// </summary>
	/// <remarks>
	/// Thread-safe: the expensive tokenization and fragment building run
	/// concurrently without contention. Tokenized results are enqueued
	/// lock-free and merged into the inverted index in batches of
	/// <see cref="PagefindIndexOptions.MergeBatchSize"/>.
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
			var tokenized = _builder.Tokenize(record);
			var (hash, bytes) = _fragmentBuilder.BuildFragment(record);
			var wordCount = CountWords(record.Content);

			_pending.Enqueue(new PendingPage(tokenized, record, hash, bytes, wordCount));

			if (_pending.Count >= _batchSize)
				FlushPending();
		}
		catch (Exception ex) when (ex is not PagefindIndexingException)
		{
			throw new PagefindIndexingException(record.Url, record.Title, ex);
		}
	}

	private void FlushPending()
	{
		lock (_mergeLock)
		{
			while (_pending.TryDequeue(out var page))
			{
				var pageIndex = _pages.Count;
				_builder.Merge(pageIndex, page.Tokenized, page.Record);
				_pages.Add(new IndexedPage(page.FragmentHash, page.FragmentBytes, page.WordCount));
			}
		}
	}

	/// <summary>
	/// Converts an <see cref="HtmlPageData"/> into a <see cref="PagefindRecord"/>
	/// with per-position weights matching the official Pagefind binary, then indexes it.
	/// </summary>
	public void AddHtmlRecord(HtmlPageData page)
	{
		var content = BuildContent(page.Sections);
		var title = FindTitle(page);
		var anchors = BuildAnchors(page.Sections, content);
		var positionWeights = BuildPositionWeights(page.Sections);

		var meta = new Dictionary<string, string>(page.Meta);
		if (!meta.ContainsKey("title"))
			meta["title"] = title;

		var record = new PagefindRecord
		{
			Url = page.Url,
			Title = title,
			Content = content,
			WeightedSegments = [],
			Anchors = anchors,
			Meta = meta,
			Filters = page.Filters,
			PositionWeights = positionWeights,
		};

		AddRecord(record);
	}

	private static string BuildContent(IReadOnlyList<HtmlSection> sections)
	{
		var sb = new System.Text.StringBuilder();
		foreach (var section in sections)
		{
			if (string.IsNullOrWhiteSpace(section.Text)) continue;
			if (sb.Length > 0)
			{
				var lastChar = sb[sb.Length - 1];
				if (lastChar is '.' or '!' or '?') sb.Append(' ');
				else sb.Append(". ");
			}
			sb.Append(section.Text);
		}
		return sb.ToString();
	}

	private static string FindTitle(HtmlPageData page)
	{
		if (page.Meta.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
			return title;
		foreach (var section in page.Sections)
			if (section.Tag.Equals("h1", StringComparison.OrdinalIgnoreCase))
				return section.Text;
		return "";
	}

	private static IReadOnlyList<PagefindAnchor> BuildAnchors(IReadOnlyList<HtmlSection> sections, string content)
	{
		var anchors = new List<PagefindAnchor>();
		var wordOffset = 0;
		foreach (var section in sections)
		{
			if (string.IsNullOrWhiteSpace(section.Text)) continue;
			var isHeading = section.Tag.Length == 2 && section.Tag[0] is 'h' or 'H' && section.Tag[1] is >= '1' and <= '6';
			if (isHeading && section.ElementId is not null)
				anchors.Add(new PagefindAnchor(section.ElementId, section.Text, wordOffset, section.Tag));
			wordOffset += CountWords(section.Text);
		}
		return anchors;
	}

	private static byte[] BuildPositionWeights(IReadOnlyList<HtmlSection> sections)
	{
		var weights = new List<byte>();
		foreach (var section in sections)
		{
			if (string.IsNullOrWhiteSpace(section.Text)) continue;
			var weight = PagefindWeights.ForTag(section.Tag);
			var wordCount = CountWords(section.Text);
			for (var i = 0; i < wordCount; i++)
				weights.Add(weight);
		}
		return [.. weights];
	}

	/// <summary>
	/// Flushes any remaining pending records, finalizes the inverted index,
	/// and writes all data files to <paramref name="outputDirectory"/>/pagefind/.
	/// </summary>
	/// <param name="outputDirectory">
	/// Root of the site output (e.g. <c>wwwroot</c>). The index is written to
	/// <c>&lt;outputDirectory&gt;/pagefind/</c>.
	/// </param>
	/// <param name="ct">Cancellation token.</param>
	public async Task WriteAsync(string outputDirectory, CancellationToken ct = default)
	{
		FlushPending();

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

	private readonly record struct PendingPage(
		TokenizedRecord Tokenized, PagefindRecord Record, string FragmentHash, byte[] FragmentBytes, int WordCount);

	/// <summary>
	/// Lightweight struct holding the pre-computed output for a single page.
	/// The original <see cref="PagefindRecord"/> is not retained.
	/// </summary>
	private readonly record struct IndexedPage(string FragmentHash, byte[] FragmentBytes, int WordCount);
}
