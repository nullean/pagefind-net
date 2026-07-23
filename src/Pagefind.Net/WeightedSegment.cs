namespace Pagefind.Net;

/// <summary>
/// A segment of plain text with an associated search weight.
/// </summary>
/// <param name="Text">Plain-text content of this segment (markup-stripped).</param>
/// <param name="Weight">
/// Relative search weight. Pagefind convention: body = 1, H3 = 3, H2 = 4, H1 = 7.
/// Encoded as a negative-int weight marker in the CBOR index.
/// </param>
public readonly record struct WeightedSegment(string Text, byte Weight);
