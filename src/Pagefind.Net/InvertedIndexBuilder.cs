namespace Pagefind.Net;

/// <summary>
/// Builds an in-memory inverted index from <see cref="PagefindRecord"/> documents.
/// </summary>
internal sealed class InvertedIndexBuilder
{
	private readonly Tokenizer _tokenizer;
	private readonly Stemmer _stemmer;

	// word → sorted list of (pageIndex, runs)
	// SortedDictionary so Build() returns it directly without re-sorting.
	private readonly SortedDictionary<string, List<PagePosting>> _index = new(StringComparer.Ordinal);

	internal InvertedIndexBuilder(Tokenizer tokenizer, Stemmer stemmer)
	{
		_tokenizer = tokenizer;
		_stemmer = stemmer;
	}

	internal void AddRecord(int pageIndex, PagefindRecord record)
	{
		// Phase 1: Tokenize Content to establish canonical positions.
		// Positions in the index must correspond to word offsets in Content
		// because the fragment stores Content and the frontend uses positions for excerpts.
		var contentWords = new Dictionary<string, List<int>>(StringComparer.Ordinal);
		var contentSink = new IndexTokenSink(contentWords, _stemmer, 0);
		_tokenizer.Tokenize(record.Content.AsSpan(), ref contentSink);

		// Phase 2: Determine which words appear in higher-weight segments.
		// These get boosted weights but don't introduce new positions.
		var boostedWords = new Dictionary<string, byte>(StringComparer.Ordinal);
		foreach (var segment in record.WeightedSegments)
		{
			if (segment.Weight <= 1 || string.IsNullOrWhiteSpace(segment.Text))
				continue;

			var boostSink = new CollectWordsSink(_stemmer);
			_tokenizer.Tokenize(segment.Text.AsSpan(), ref boostSink);
			foreach (var word in boostSink.Words)
			{
				if (!boostedWords.TryGetValue(word, out var existing) || segment.Weight > existing)
					boostedWords[word] = segment.Weight;
			}
		}

		// Phase 3: Merge into the global index with appropriate weights.
		foreach (var (word, positions) in contentWords)
		{
			var weight = boostedWords.TryGetValue(word, out var boost) ? boost : (byte)1;

			if (!_index.TryGetValue(word, out var postings))
				_index[word] = postings = [];

			var found = false;
			for (var i = 0; i < postings.Count; i++)
			{
				if (postings[i].PageIndex == pageIndex)
				{
					postings[i].Runs.Add(new WeightRun(weight, [.. positions]));
					found = true;
					break;
				}
			}

			if (!found)
				postings.Add(new PagePosting(pageIndex, [new WeightRun(weight, [.. positions])]));
		}
	}

	/// <summary>
	/// Returns all accumulated postings, sorted alphabetically by word.
	/// The returned dictionary is the builder's own instance — do not mutate after calling.
	/// </summary>
	internal SortedDictionary<string, List<PagePosting>> Build()
	{
		foreach (var (_, postings) in _index)
			postings.Sort((a, b) => a.PageIndex.CompareTo(b.PageIndex));
		return _index;
	}

	/// <summary>
	/// Ref struct token sink that stems each token via span APIs and inserts
	/// into a positions dictionary, allocating a string only for first-seen stemmed words.
	/// Position increments once per whitespace word (via OnWordBoundary), not per token,
	/// matching pagefind's frontend excerpt/highlight word counting.
	/// </summary>
	private ref struct IndexTokenSink : ITokenSink
	{
		private readonly Dictionary<string, List<int>> _positions;
		private readonly Dictionary<string, List<int>>.AlternateLookup<ReadOnlySpan<char>> _lookup;
		private readonly Stemmer _stemmer;
		private int _globalPosition;

		internal int GlobalPosition => _globalPosition;

		internal IndexTokenSink(Dictionary<string, List<int>> positions, Stemmer stemmer, int globalPosition)
		{
			_positions = positions;
			_lookup = positions.GetAlternateLookup<ReadOnlySpan<char>>();
			_stemmer = stemmer;
			_globalPosition = globalPosition - 1; // pre-decrement; first OnWordBoundary brings it to startPos
		}

		public void OnWordBoundary() => _globalPosition++;

		public void OnToken(scoped ReadOnlySpan<char> token)
		{
			Span<char> stemBuf = stackalloc char[token.Length];
			var stemLen = _stemmer.Stem(token, stemBuf);
			var stemmed = stemBuf[..stemLen];

			if (stemLen == 0)
				return;

			if (!_lookup.TryGetValue(stemmed, out var positions))
			{
				positions = [];
				_positions[new string(stemmed)] = positions;
			}
			positions.Add(_globalPosition);
		}
	}

	/// <summary>
	/// Sink that just collects unique stemmed words (no positions needed).
	/// Used for extracting boosted words from high-weight segments.
	/// </summary>
	private ref struct CollectWordsSink : ITokenSink
	{
		private readonly HashSet<string> _words;
		private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
		private readonly Stemmer _stemmer;

		internal HashSet<string> Words => _words;

		internal CollectWordsSink(Stemmer stemmer)
		{
			_words = new(StringComparer.Ordinal);
			_lookup = _words.GetAlternateLookup<ReadOnlySpan<char>>();
			_stemmer = stemmer;
		}

		public void OnWordBoundary() { }

		public void OnToken(scoped ReadOnlySpan<char> token)
		{
			Span<char> stemBuf = stackalloc char[token.Length];
			var stemLen = _stemmer.Stem(token, stemBuf);
			if (stemLen == 0) return;

			var stemmed = stemBuf[..stemLen];
			if (!_lookup.Contains(stemmed))
				_words.Add(new string(stemmed));
		}
	}
}

/// <summary>One page's postings for a given word.</summary>
internal sealed class PagePosting(int pageIndex, List<WeightRun> runs)
{
	internal int PageIndex { get; } = pageIndex;
	internal List<WeightRun> Runs { get; } = runs;
}

/// <summary>
/// A run of positions for a single weight within a page.
/// Encoded as a negative-int weight marker followed by delta-encoded positions.
/// </summary>
internal record WeightRun(byte Weight, int[] Positions);
