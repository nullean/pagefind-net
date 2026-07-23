namespace Pagefind.Net;

/// <summary>
/// Accumulates <see cref="PagefindRecord"/> documents and writes a
/// Pagefind-compatible index directory when <see cref="WriteAsync"/> is called.
/// </summary>
/// <remarks>
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
	private readonly List<PagefindRecord> _records = [];

	/// <summary>Initialises a new index with the given options.</summary>
	public PagefindIndex(PagefindIndexOptions? options = null) =>
		_options = options ?? new PagefindIndexOptions();

	/// <summary>
	/// Adds a document to the index. Thread-safe when called from a single
	/// producer thread; <see cref="WriteAsync"/> must not be called concurrently
	/// with <see cref="AddRecord"/>.
	/// </summary>
	public void AddRecord(PagefindRecord record) => _records.Add(record);

	/// <summary>
	/// Builds the full Pagefind index and writes all data files to
	/// <paramref name="outputDirectory"/>/pagefind/.
	/// </summary>
	/// <param name="outputDirectory">
	/// Root of the site output (e.g. <c>wwwroot</c>). The index is written to
	/// <c>&lt;outputDirectory&gt;/pagefind/</c>.
	/// </param>
	/// <param name="ct">Cancellation token.</param>
	public async Task WriteAsync(string outputDirectory, CancellationToken ct = default)
	{
		var pagefindDir = Path.Combine(outputDirectory, "pagefind");
		Directory.CreateDirectory(pagefindDir);
		Directory.CreateDirectory(Path.Combine(pagefindDir, "index"));
		Directory.CreateDirectory(Path.Combine(pagefindDir, "fragment"));

		// 1. Tokenise and stem every record, building the inverted index.
		var tokenizer = new Tokenizer(_options.IncludeCharacters);
		var stemmer = new Stemmer(_options.Language);
		var builder = new InvertedIndexBuilder(tokenizer, stemmer);

		for (var i = 0; i < _records.Count; i++)
			builder.AddRecord(i, _records[i]);

		var invertedIndex = builder.Build();

		// 2. Write fragment files.
		var fragmentWriter = new FragmentBuilder();
		var pageHashes = new string[_records.Count];
		for (var i = 0; i < _records.Count; i++)
		{
			ct.ThrowIfCancellationRequested();
			var (hash, bytes) = fragmentWriter.BuildFragment(_records[i]);
			pageHashes[i] = hash;
			var path = Path.Combine(pagefindDir, "fragment", $"{hash}.pf_fragment");
			await PagefindWriter.WriteFramedAsync(path, bytes, ct);
		}

		// 3. Write index chunk files.
		var indexChunks = await PagefindWriter.WriteIndexChunksAsync(
			pagefindDir, invertedIndex, ct);

		// 4. Write the meta file.
		var wordCounts = new int[_records.Count];
		for (var i = 0; i < _records.Count; i++)
			wordCounts[i] = CountWords(_records[i].Content);

		var (metaHash, metaBytes) = PagefindWriter.BuildMetaCbor(
			PagefindTargetVersion,
			pageHashes,
			wordCounts,
			indexChunks);

		await PagefindWriter.WriteFramedAsync(
			Path.Combine(pagefindDir, $"pagefind.{metaHash}.pf_meta"),
			metaBytes, ct);

		// 5. Write pagefind-entry.json.
		await PagefindWriter.WriteEntryJsonAsync(
			pagefindDir,
			_options.Language,
			metaHash,
			_records.Count,
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
}
