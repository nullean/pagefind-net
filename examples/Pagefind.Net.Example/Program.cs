using Pagefind.Net;

// ── Locate the output directory ───────────────────────────────────────────────

// When run via `dotnet run`, use wwwroot relative to the project.
// When run as a published binary, use a sibling directory.
var appDir = AppContext.BaseDirectory;
var wwwroot = Path.Combine(appDir, "wwwroot");
Directory.CreateDirectory(wwwroot);

var pagefindDir = Path.Combine(wwwroot, "pagefind");

// If a previous run left frontend runtime files (pagefind.js, wasm.*.pagefind)
// from the npm side, keep them — only the data files are ours to write.
// (PagefindIndex.WriteAsync creates the pagefind/ dir and writes data files.)

// ── Index the sample corpus ───────────────────────────────────────────────────

Console.WriteLine("Building Pagefind index…");

var index = new PagefindIndex(new PagefindIndexOptions { Language = "en" });

var corpusDir = Path.Combine(appDir, "Corpus");
if (!Directory.Exists(corpusDir))
{
    Console.Error.WriteLine($"Corpus directory not found: {corpusDir}");
    return 1;
}

foreach (var htmlFile in Directory.EnumerateFiles(corpusDir, "*.html"))
{
    var doc = CorpusParser.Parse(htmlFile);
    index.AddRecord(doc);
    Console.WriteLine($"  Indexed: {doc.Url} ({doc.Title})");
}

await index.WriteAsync(wwwroot, CancellationToken.None);

Console.WriteLine($"Index written to: {pagefindDir}");
Console.WriteLine("Frontend runtime (pagefind.js + wasm) was extracted by the build via Pagefind.Net.Frontend.");
Console.WriteLine("Serve wwwroot/ over HTTP to use the search UI.");
return 0;

// ── Corpus parser ─────────────────────────────────────────────────────────────

/// <summary>
/// Minimal HTML-to-PagefindRecord converter for the sample corpus.
/// Real integrations would use the in-memory document model instead.
/// </summary>
static class CorpusParser
{
    public static PagefindRecord Parse(string htmlPath)
    {
        var html = File.ReadAllText(htmlPath);
        var filename = Path.GetFileNameWithoutExtension(htmlPath);
        var url = $"/{filename}/";

        var title = ExtractTag(html, "h1") ?? filename;
        var body = StripTags(html);

        var segments = new List<WeightedSegment>();
        var h1 = ExtractTag(html, "h1");
        if (h1 is not null) segments.Add(new WeightedSegment(h1, Weight: 7));
        foreach (var h2 in ExtractAllTags(html, "h2"))
            segments.Add(new WeightedSegment(h2, Weight: 4));
        segments.Add(new WeightedSegment(body, Weight: 1));

        var anchors = new List<PagefindAnchor>();
        var byteOffset = 0;
        foreach (var h2 in ExtractAllTags(html, "h2"))
        {
            var id = Slugify(h2);
            anchors.Add(new PagefindAnchor(id, h2, byteOffset));
            byteOffset += System.Text.Encoding.UTF8.GetByteCount(h2);
        }

        return new PagefindRecord
        {
            Url = url,
            Title = title,
            Content = body,
            WeightedSegments = segments,
            Anchors = anchors,
            Meta = new Dictionary<string, string> { ["title"] = title },
        };
    }

    private static string? ExtractTag(string html, string tag)
    {
        var open = $"<{tag}>";
        var close = $"</{tag}>";
        var s = html.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (s < 0) return null;
        s += open.Length;
        var e = html.IndexOf(close, s, StringComparison.OrdinalIgnoreCase);
        return e < 0 ? null : StripTags(html[s..e]).Trim();
    }

    private static IEnumerable<string> ExtractAllTags(string html, string tag)
    {
        var open = $"<{tag}";
        var close = $"</{tag}>";
        var pos = 0;
        while (true)
        {
            var s = html.IndexOf(open, pos, StringComparison.OrdinalIgnoreCase);
            if (s < 0) yield break;
            var tagEnd = html.IndexOf('>', s);
            if (tagEnd < 0) yield break;
            var e = html.IndexOf(close, tagEnd, StringComparison.OrdinalIgnoreCase);
            if (e < 0) yield break;
            yield return StripTags(html[(tagEnd + 1)..e]).Trim();
            pos = e + close.Length;
        }
    }

    private static string StripTags(string html)
    {
        var sb = new System.Text.StringBuilder();
        var inTag = false;
        foreach (var c in html)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; sb.Append(' '); continue; }
            if (!inTag) sb.Append(c);
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static string Slugify(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
}
