using AwesomeAssertions;
using Pagefind.Net;
using TUnit;

namespace Pagefind.Net.Tests;

/// <summary>
/// Unit tests for the inverted index builder, delta-encoding, and weight markers.
/// </summary>
public sealed class InvertedIndexTests
{
	private static readonly Tokenizer Tokenizer = new(includeCharacters: "");
	private static readonly Stemmer Stemmer = new("en");

	[Test]
	public void SingleWordSinglePage()
	{
		var builder = new InvertedIndexBuilder(Tokenizer, Stemmer);
		var record = new PagefindRecord
		{
			Url = "/a/",
			Title = "A",
			Content = "hello world",
			WeightedSegments = [new WeightedSegment("hello world", Weight: 1)],
		};
		var tokenized = builder.Tokenize(record);
		builder.Merge(0, tokenized, record);

		var index = builder.Build();
		// "hello" and "world" should appear; stemmed forms may differ.
		index.Should().ContainKey("hello").WhoseValue.Should().HaveCount(1);
		index["hello"][0].PageIndex.Should().Be(0);
	}

	[Test]
	public void MultiPageDeltaEncoding()
	{
		var builder = new InvertedIndexBuilder(Tokenizer, Stemmer);
		AddRecord(builder, 0, new PagefindRecord
		{
			Url = "/a/", Title = "A", Content = "search engine",
			WeightedSegments = [new WeightedSegment("search engine", Weight: 1)],
		});
		AddRecord(builder, 1, new PagefindRecord
		{
			Url = "/b/", Title = "B", Content = "search results",
			WeightedSegments = [new WeightedSegment("search results", Weight: 1)],
		});
		AddRecord(builder, 2, new PagefindRecord
		{
			Url = "/c/", Title = "C", Content = "search query",
			WeightedSegments = [new WeightedSegment("search query", Weight: 1)],
		});

		var index = builder.Build();
		// "search" should appear in all three pages.
		// The builder returns absolute page indices; PagefindWriter does the delta.
		var postings = index["search"];
		postings.Should().HaveCount(3);
		postings[0].PageIndex.Should().Be(0);
		postings[1].PageIndex.Should().Be(1);
		postings[2].PageIndex.Should().Be(2);
	}

	[Test]
	public void WeightBoostFromSegments()
	{
		var builder = new InvertedIndexBuilder(Tokenizer, Stemmer);
		AddRecord(builder, 0, new PagefindRecord
		{
			Url = "/a/", Title = "A", Content = "search engine",
			WeightedSegments =
			[
				new WeightedSegment("search", Weight: 7),  // H1 weight boosts "search"
				new WeightedSegment("search engine", Weight: 1),  // body (not used for positions)
			],
		});

		var index = builder.Build();
		var postings = index["search"];
		postings.Should().HaveCount(1);
		// One run: positions from Content, weight boosted to 7 by the heading segment.
		postings[0].Runs.Should().HaveCount(1);
		postings[0].Runs[0].Weight.Should().Be(7);
		postings[0].Runs[0].Positions.Should().BeEquivalentTo([0]);

		// "engine" only appears in Content at weight 1 (no boost segment).
		var enginePostings = index["engin"]; // stemmed form
		enginePostings[0].Runs[0].Weight.Should().Be(1);
		enginePostings[0].Runs[0].Positions.Should().BeEquivalentTo([1]);
	}

	[Test]
	public void WordsAreSortedAlphabetically()
	{
		var builder = new InvertedIndexBuilder(Tokenizer, Stemmer);
		AddRecord(builder, 0, new PagefindRecord
		{
			Url = "/a/", Title = "A", Content = "zebra apple mango",
			WeightedSegments = [new WeightedSegment("zebra apple mango", Weight: 1)],
		});

		var index = builder.Build();
		var keys = index.Keys.ToList();
		keys.Should().BeInAscendingOrder();
	}

	[Test]
	public async Task ConcurrentAddRecordDoesNotCorruptIndex()
	{
		const int recordCount = 10_000;

		var fs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
		var index = new PagefindIndex(
			new PagefindIndexOptions { Language = "en" },
			fs);

		var records = new PagefindRecord[recordCount];
		for (var i = 0; i < recordCount; i++)
		{
			records[i] = new PagefindRecord
			{
				Url = $"/page-{i}/",
				Title = $"Page {i}",
				Content = $"search result number {i} with some extra words to index properly",
				WeightedSegments =
				[
					new WeightedSegment($"Page {i}", Weight: 7),
					new WeightedSegment($"search result number {i} with some extra words to index properly", Weight: 1),
				],
			};
		}

		Parallel.ForEach(records, record => index.AddRecord(record));

		await index.WriteAsync("/output", CancellationToken.None);

		var pagefindDir = "/output/pagefind";
		fs.File.Exists($"{pagefindDir}/pagefind-entry.json").Should().BeTrue();
		fs.Directory.GetFiles($"{pagefindDir}", "*.pf_meta").Should().HaveCount(1);
		fs.Directory.GetFiles($"{pagefindDir}/fragment", "*.pf_fragment")
			.Should().HaveCount(recordCount, $"one fragment per record ({recordCount})");
		fs.Directory.GetFiles($"{pagefindDir}/index", "*.pf_index")
			.Should().NotBeEmpty("at least one index chunk is expected");

		var entryJson = fs.File.ReadAllText($"{pagefindDir}/pagefind-entry.json");
		using var doc = System.Text.Json.JsonDocument.Parse(entryJson);
		var pageCount = doc.RootElement
			.GetProperty("languages")
			.GetProperty("en")
			.GetProperty("page_count")
			.GetInt32();
		pageCount.Should().Be(recordCount);
	}

	private static void AddRecord(InvertedIndexBuilder builder, int pageIndex, PagefindRecord record)
	{
		var tokenized = builder.Tokenize(record);
		builder.Merge(pageIndex, tokenized, record);
	}
}
