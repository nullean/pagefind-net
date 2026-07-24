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
		builder.AddRecord(0, new PagefindRecord
		{
			Url = "/a/",
			Title = "A",
			Content = "hello world",
			WeightedSegments = [new WeightedSegment("hello world", Weight: 1)],
		});

		var index = builder.Build();
		// "hello" and "world" should appear; stemmed forms may differ.
		index.Should().ContainKey("hello").WhoseValue.Should().HaveCount(1);
		index["hello"][0].PageIndex.Should().Be(0);
	}

	[Test]
	public void MultiPageDeltaEncoding()
	{
		var builder = new InvertedIndexBuilder(Tokenizer, Stemmer);
		builder.AddRecord(0, new PagefindRecord
		{
			Url = "/a/", Title = "A", Content = "search engine",
			WeightedSegments = [new WeightedSegment("search engine", Weight: 1)],
		});
		builder.AddRecord(1, new PagefindRecord
		{
			Url = "/b/", Title = "B", Content = "search results",
			WeightedSegments = [new WeightedSegment("search results", Weight: 1)],
		});
		builder.AddRecord(2, new PagefindRecord
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
		builder.AddRecord(0, new PagefindRecord
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
		builder.AddRecord(0, new PagefindRecord
		{
			Url = "/a/", Title = "A", Content = "zebra apple mango",
			WeightedSegments = [new WeightedSegment("zebra apple mango", Weight: 1)],
		});

		var index = builder.Build();
		var keys = index.Keys.ToList();
		keys.Should().BeInAscendingOrder();
	}
}
