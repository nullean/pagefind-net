namespace Pagefind.Net;

/// <summary>
/// A segment of plain text with an associated search weight.
/// </summary>
/// <param name="Text">Plain-text content of this segment (markup-stripped).</param>
/// <param name="Weight">
/// Raw Pagefind weight value. The official binary uses a 24x multiplier:
/// body text = <c>0</c> (via <c>addCustomRecord</c>) or <c>24</c> (via HTML),
/// headings = <c>auto_weight * 24</c> (h1=168, h2=144, h3=120, h4=96, h5=72, h6=48).
/// Use <see cref="PagefindIndex.AddHtmlRecord"/> for automatic weight assignment
/// that matches the official binary's behaviour.
/// Encoded as <c>-(weight + 1)</c> marker in the CBOR index.
/// </param>
public readonly record struct WeightedSegment(string Text, byte Weight);
