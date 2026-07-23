namespace Pagefind.Net;

/// <summary>
/// Builds an in-memory inverted index from <see cref="PagefindRecord"/> documents.
/// </summary>
internal sealed class InvertedIndexBuilder
{
	private readonly Tokenizer _tokenizer;
	private readonly Stemmer _stemmer;

	// word → sorted list of (pageIndex, positions[], weight)
	private readonly Dictionary<string, List<PagePosting>> _index = new(StringComparer.Ordinal);

	internal InvertedIndexBuilder(Tokenizer tokenizer, Stemmer stemmer)
	{
		_tokenizer = tokenizer;
		_stemmer = stemmer;
	}

	internal void AddRecord(int pageIndex, PagefindRecord record)
	{
		// Track position across all segments (position = word offset in the concatenated stream).
		var globalPosition = 0;

		foreach (var segment in record.WeightedSegments)
		{
			if (string.IsNullOrWhiteSpace(segment.Text))
				continue;

			// Collect (token, position) for this segment.
			var segmentPositions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
			foreach (var raw in _tokenizer.Tokenize(segment.Text))
			{
				var stemmed = _stemmer.Stem(raw);
				if (string.IsNullOrEmpty(stemmed))
				{
					globalPosition++;
					continue;
				}

				if (!segmentPositions.TryGetValue(stemmed, out var positions))
					segmentPositions[stemmed] = positions = [];
				positions.Add(globalPosition);
				globalPosition++;
			}

			// Merge segment postings into the global index.
			foreach (var (word, positions) in segmentPositions)
			{
				if (!_index.TryGetValue(word, out var postings))
					_index[word] = postings = [];

				// Find or create the posting for this page.
				var found = false;
				for (var i = 0; i < postings.Count; i++)
				{
					if (postings[i].PageIndex == pageIndex)
					{
						// Append a new weight run to the existing posting.
						postings[i] = postings[i] with
						{
							Runs = [.. postings[i].Runs, new WeightRun(segment.Weight, [.. positions])]
						};
						found = true;
						break;
					}
				}

				if (!found)
					postings.Add(new PagePosting(pageIndex, [new WeightRun(segment.Weight, [.. positions])]));
			}
		}
	}

	/// <summary>
	/// Returns all accumulated postings, sorted alphabetically by word.
	/// </summary>
	internal SortedDictionary<string, List<PagePosting>> Build()
	{
		var sorted = new SortedDictionary<string, List<PagePosting>>(StringComparer.Ordinal);
		foreach (var (word, postings) in _index)
		{
			postings.Sort((a, b) => a.PageIndex.CompareTo(b.PageIndex));
			sorted[word] = postings;
		}
		return sorted;
	}
}

/// <summary>One page's postings for a given word.</summary>
internal record PagePosting(int PageIndex, WeightRun[] Runs);

/// <summary>
/// A run of positions for a single weight within a page.
/// Encoded as a negative-int weight marker followed by delta-encoded positions.
/// </summary>
internal record WeightRun(byte Weight, int[] Positions);
