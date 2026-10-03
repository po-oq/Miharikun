using Miharikun.Core.Documents;

namespace Miharikun.Tests.Core;

/// <summary>MIHARIKUN_PERF=1 のときだけ動く計測用テスト（Defender 等で時間が揺れるため、通常の dotnet test では飛ばす）。</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public PerfFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MIHARIKUN_PERF") != "1")
            Skip = "計測用。MIHARIKUN_PERF=1 で実行する";
    }
}

public sealed class DocumentIndexerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "miharikun-docs-" + Guid.NewGuid().ToString("N"));

    public DocumentIndexerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private void Touch(string rel, string content = "x")
    {
        var path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private List<string> Scan(string ignore = "") =>
        DocumentIndexer.Enumerate(_root, GitIgnoreMatcher.Parse(ignore), CancellationToken.None)
            .Select(e => e.RelativePath).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void Picks_only_md_html_htm_case_insensitively()
    {
        Touch("a.md"); Touch("b.HTML"); Touch("c.htm"); Touch("d.txt"); Touch("e.cs"); Touch("sub/F.MD"); Touch("noext");
        Assert.Equal(["a.md", "b.HTML", "c.htm", "sub/F.MD"], Scan());
    }

    [Fact]
    public void Relative_paths_use_forward_slashes_and_carry_size_and_time()
    {
        Touch("docs/guide/a.md", "hello");
        var entry = Assert.Single(DocumentIndexer.Enumerate(_root, GitIgnoreMatcher.Parse(""), CancellationToken.None));
        Assert.Equal("docs/guide/a.md", entry.RelativePath);
        Assert.Equal("a.md", entry.Name);
        Assert.Equal("docs/guide", entry.Folder);
        Assert.Equal(DocumentKind.Markdown, entry.Kind);
        Assert.Equal(5, entry.Size);
        Assert.True(DateTime.UtcNow - entry.ModifiedUtc < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Excluded_folders_are_pruned()
    {
        Touch("keep/a.md"); Touch("node_modules/p/b.md"); Touch("src/bin/c.md"); Touch("docs/archive/d.md");
        Assert.Equal(["keep/a.md"], Scan("node_modules/\nbin/\ndocs/archive/"));
    }

    [Fact]
    public void Excluded_file_patterns_and_negation()
    {
        Touch("a.md"); Touch("b.draft.md"); Touch("keep.draft.md");
        Assert.Equal(["a.md", "keep.draft.md"], Scan("*.draft.md\n!keep.draft.md"));
    }

    [Fact]
    public void Hidden_folders_such_as_dot_cursor_are_included_unless_excluded()
    {
        Touch(".cursor/rules/r.md");
        Assert.Equal([".cursor/rules/r.md"], Scan(DefaultIgnore.Text));
    }

    [Fact]
    public void Missing_root_yields_nothing() =>
        Assert.Empty(DocumentIndexer.Enumerate(Path.Combine(_root, "nope"), GitIgnoreMatcher.Parse(""), CancellationToken.None));

    [Fact]
    public async Task ScanAsync_sends_batches_with_generation_and_a_final_marker()
    {
        for (var i = 0; i < 5; i++)
            Touch($"f{i}.md");
        var batches = new List<DocumentBatch>();

        await DocumentIndexer.ScanAsync(_root, GitIgnoreMatcher.Parse(""), generation: 7,
            onBatch: batches.Add, CancellationToken.None, batchSize: 2);

        Assert.All(batches, b => Assert.Equal(7, b.Generation));
        Assert.True(batches.Count >= 3);
        Assert.Equal(5, batches.SelectMany(b => b.Entries).Select(e => e.RelativePath).Distinct().Count());
        Assert.True(batches[^1].IsLast);
        Assert.All(batches.Take(batches.Count - 1), b => Assert.False(b.IsLast));
    }

    [Fact]
    public async Task ScanAsync_with_no_files_still_sends_the_final_marker()
    {
        var batches = new List<DocumentBatch>();
        await DocumentIndexer.ScanAsync(_root, GitIgnoreMatcher.Parse(""), 1, batches.Add, CancellationToken.None);
        var only = Assert.Single(batches);
        Assert.True(only.IsLast);
        Assert.Empty(only.Entries);
    }

    [Fact]
    public async Task ScanAsync_stops_when_cancelled_and_sends_no_final_marker()
    {
        for (var i = 0; i < 50; i++)
            Touch($"f{i}.md");
        using var cts = new CancellationTokenSource();
        var batches = new List<DocumentBatch>();

        await DocumentIndexer.ScanAsync(_root, GitIgnoreMatcher.Parse(""), 1,
            b => { batches.Add(b); cts.Cancel(); }, cts.Token, batchSize: 5);

        Assert.DoesNotContain(batches, b => b.IsLast);
        Assert.True(batches.Sum(b => b.Entries.Count) < 50);
    }

    [PerfFact]
    public void Ten_thousand_files_are_listed_quickly()
    {
        for (var d = 0; d < 100; d++)
            for (var f = 0; f < 100; f++)
                Touch($"d{d}/f{f}.md");
        for (var i = 0; i < 2000; i++)
            Touch($"node_modules/p{i % 50}/f{i}.md");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var count = DocumentIndexer.Enumerate(_root, GitIgnoreMatcher.Parse(DefaultIgnore.Text), CancellationToken.None).Count();
        sw.Stop();

        Assert.Equal(10_000, count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"{sw.Elapsed}");
    }
}
