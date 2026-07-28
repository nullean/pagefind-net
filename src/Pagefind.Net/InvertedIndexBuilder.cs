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
		var compoundPositions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
		var compoundCountAtPosition = new Dictionary<int, int>();
		var contentSink = new IndexTokenSink(contentWords, compoundPositions, compoundCountAtPosition, _stemmer, 0);
		_tokenizer.Tokenize(record.Content.AsSpan(), ref contentSink);

		// Phase 2: Determine max weight for each word across all WeightedSegments.
		// All segments are processed (including weight 0 for addCustomRecord parity).
		var wordWeights = new Dictionary<string, byte>(StringComparer.Ordinal);
		foreach (var segment in record.WeightedSegments)
		{
			if (string.IsNullOrWhiteSpace(segment.Text))
				continue;

			var boostSink = new CollectWordsSink(_stemmer);
			_tokenizer.Tokenize(segment.Text.AsSpan(), ref boostSink);
			foreach (var word in boostSink.Words)
			{
				if (!wordWeights.TryGetValue(word, out var existing) || segment.Weight > existing)
					wordWeights[word] = segment.Weight;
			}
		}

		// Phase 3: Merge primary tokens into the global index.
		// When PositionWeights is provided (AddHtmlRecord), use per-position weights.
		// Otherwise, use the max-weight-per-word from WeightedSegments.
		foreach (var (word, positions) in contentWords)
		{
			if (record.PositionWeights is not null)
			{
				// Per-position weights: group positions by their weight, create a run per group.
				var grouped = new Dictionary<byte, List<int>>();
				foreach (var pos in positions)
				{
					var posWeight = pos < record.PositionWeights.Length ? record.PositionWeights[pos] : (byte)0;
					if (!grouped.TryGetValue(posWeight, out var list))
						grouped[posWeight] = list = [];
					list.Add(pos);
				}
				AddGroupedRuns(pageIndex, word, grouped);
			}
			else
			{
				var weight = wordWeights.TryGetValue(word, out var w) ? w : (byte)0;
				AddRun(pageIndex, word, weight, positions);
			}
		}

		// Phase 3b: Merge compound parts with reduced weight.
		// partial_weight = max(1, base_weight / compound_count) per pagefind behaviour.
		foreach (var (word, positions) in compoundPositions)
		{
			var grouped = new Dictionary<byte, List<int>>();
			foreach (var pos in positions)
			{
				var baseWeight = record.PositionWeights is not null && pos < record.PositionWeights.Length
					? record.PositionWeights[pos]
					: wordWeights.TryGetValue(word, out var bw) ? bw : (byte)0;

				var compoundCount = compoundCountAtPosition.GetValueOrDefault(pos, 1);
				var partialWeight = baseWeight > 0 && compoundCount > 0
					? (byte)Math.Max(1, baseWeight / compoundCount)
					: (byte)0;

				if (!grouped.TryGetValue(partialWeight, out var posList))
					grouped[partialWeight] = posList = [];
				posList.Add(pos);
			}
			AddGroupedRuns(pageIndex, word, grouped);
		}

		// Phase 4: Index meta field values (e.g. meta.title) into metaLocs.
		// Pagefind indexes these separately so titles can be searched/boosted
		// even if their words don't appear in the body content.
		var metaFieldOrder = record.Meta.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
		for (var fieldIdx = 0; fieldIdx < metaFieldOrder.Count; fieldIdx++)
		{
			var fieldName = metaFieldOrder[fieldIdx];
			var fieldValue = record.Meta[fieldName];
			if (string.IsNullOrWhiteSpace(fieldValue))
				continue;

			var metaWords = new Dictionary<string, List<int>>(StringComparer.Ordinal);
			var metaCompound = new Dictionary<string, List<int>>(StringComparer.Ordinal);
			var metaCompoundCounts = new Dictionary<int, int>();
			var metaSink = new IndexTokenSink(metaWords, metaCompound, metaCompoundCounts, _stemmer, 0);
			_tokenizer.Tokenize(fieldValue.AsSpan(), ref metaSink);

			// Merge meta words (including compound parts) into the global index's metaRuns.
			var allMetaWords = new Dictionary<string, List<int>>(metaWords, StringComparer.Ordinal);
			foreach (var (word, positions) in metaCompound)
			{
				if (allMetaWords.TryGetValue(word, out var existing))
					existing.AddRange(positions);
				else
					allMetaWords[word] = [.. positions];
			}

			foreach (var (word, positions) in allMetaWords)
			{
				if (!_index.TryGetValue(word, out var postings))
					_index[word] = postings = [];

				var existing = postings.FirstOrDefault(p => p.PageIndex == pageIndex);
				if (existing is null)
				{
					existing = new PagePosting(pageIndex, []);
					postings.Add(existing);
				}
				existing.MetaRuns.Add(new MetaFieldRun(fieldIdx, [.. positions]));
			}
		}
	}

	private void AddRun(int pageIndex, string word, byte weight, List<int> positions)
	{
		if (!_index.TryGetValue(word, out var postings))
			_index[word] = postings = [];

		var existing = postings.FirstOrDefault(p => p.PageIndex == pageIndex);
		if (existing is not null)
		{
			existing.Runs.Add(new WeightRun(weight, [.. positions]));
		}
		else
		{
			postings.Add(new PagePosting(pageIndex, [new WeightRun(weight, [.. positions])]));
		}
	}

	private void AddGroupedRuns(int pageIndex, string word, Dictionary<byte, List<int>> grouped)
	{
		foreach (var (weight, posList) in grouped)
			AddRun(pageIndex, word, weight, posList);
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
		private readonly Dictionary<string, List<int>> _compoundPositions;
		private readonly Dictionary<string, List<int>>.AlternateLookup<ReadOnlySpan<char>> _compoundLookup;
		private readonly Dictionary<int, int> _compoundCountAtPosition;
		private readonly Stemmer _stemmer;
		private int _globalPosition;
		private int _currentCompoundCount;

		internal int GlobalPosition => _globalPosition;

		internal IndexTokenSink(
			Dictionary<string, List<int>> positions,
			Dictionary<string, List<int>> compoundPositions,
			Dictionary<int, int> compoundCountAtPosition,
			Stemmer stemmer,
			int globalPosition)
		{
			_positions = positions;
			_lookup = positions.GetAlternateLookup<ReadOnlySpan<char>>();
			_compoundPositions = compoundPositions;
			_compoundLookup = compoundPositions.GetAlternateLookup<ReadOnlySpan<char>>();
			_compoundCountAtPosition = compoundCountAtPosition;
			_stemmer = stemmer;
			_globalPosition = globalPosition - 1; // pre-decrement; first OnWordBoundary brings it to startPos
			_currentCompoundCount = 0;
		}

		public void OnWordBoundary()
		{
			_globalPosition++;
			_currentCompoundCount = 0;
		}

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

		public void OnCompoundPart(scoped ReadOnlySpan<char> token)
		{
			Span<char> stemBuf = stackalloc char[token.Length];
			var stemLen = _stemmer.Stem(token, stemBuf);
			if (stemLen == 0) return;
			var stemmed = stemBuf[..stemLen];

			_currentCompoundCount++;
			_compoundCountAtPosition[_globalPosition] = _currentCompoundCount;

			if (!_compoundLookup.TryGetValue(stemmed, out var positions))
			{
				positions = [];
				_compoundPositions[new string(stemmed)] = positions;
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

		public void OnCompoundPart(scoped ReadOnlySpan<char> token) => OnToken(token);
	}
}

/// <summary>One page's postings for a given word.</summary>
internal sealed class PagePosting(int pageIndex, List<WeightRun> runs)
{
	internal int PageIndex { get; } = pageIndex;
	internal List<WeightRun> Runs { get; } = runs;

	/// <summary>
	/// Meta-field positions (e.g. title words). Encoded as field-ID markers
	/// followed by delta-encoded positions: <c>[-(fieldId+1), pos, ...]</c>.
	/// </summary>
	internal List<MetaFieldRun> MetaRuns { get; } = [];
}

/// <summary>
/// A run of positions for a single weight within a page.
/// Encoded as a negative-int weight marker followed by delta-encoded positions.
/// </summary>
internal record WeightRun(byte Weight, int[] Positions);

/// <summary>
/// Positions of a word within a single meta field.
/// Encoded as <c>[-(fieldId+1), delta_positions...]</c> in the CBOR meta_locs array.
/// </summary>
internal record MetaFieldRun(int FieldId, int[] Positions);
