using Pagefind.Net;

// Minimal smoke test: create an index, add one record, write to a temp dir.
// This binary is published with PublishAot=true in CI to verify there are no
// reflection or trim warnings at runtime.

var outputDir = Path.Combine(Path.GetTempPath(), "pagefind-net-smoketest");
Directory.CreateDirectory(outputDir);

var index = new PagefindIndex(new PagefindIndexOptions { Language = "en" });

index.AddRecord(new PagefindRecord
{
    Url = "/smoke-test/",
    Title = "Smoke Test",
    Content = "This is a smoke test to verify native AOT compatibility.",
    WeightedSegments =
    [
        new WeightedSegment("Smoke Test", Weight: 7),
        new WeightedSegment("This is a smoke test to verify native AOT compatibility.", Weight: 1),
    ],
    Anchors =
    [
        new PagefindAnchor("smoke-test", "Smoke Test", 0),
    ],
    Meta = new Dictionary<string, string> { ["title"] = "Smoke Test" },
});

await index.WriteAsync(outputDir, CancellationToken.None);

// Verify expected output files exist.
var pagefindDir = Path.Combine(outputDir, "pagefind");
var entry = Path.Combine(pagefindDir, "pagefind-entry.json");
if (!File.Exists(entry))
{
    Console.Error.WriteLine($"FAIL: {entry} was not written.");
    Environment.Exit(1);
}

Console.WriteLine("AOT smoke test passed. Index written to: " + pagefindDir);
