using Miharikun.Core.Documents;
using static Miharikun.Core.Documents.ChangeKind;

namespace Miharikun.Tests.Core;

public sealed class DocumentChangeClassifierTests
{
    private static readonly GitIgnoreMatcher Matcher = GitIgnoreMatcher.Parse("node_modules/\n*.draft.md");

    private static DocumentEntry Entry(string rel) => new(rel, DateTime.UtcNow, 1);

    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _indexed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _stats = [];

    private DocumentChangeSet Run(params FileChange[] changes) =>
        DocumentChangeClassifier.Classify(changes, Matcher,
            directoryExists: _dirs.Contains,
            statFile: p => { _stats.Add(p); return _missing.Contains(p) ? null : Entry(p); },
            indexHasEntriesUnder: _indexed.Contains);

    [Theory]
    [InlineData(Created)]
    [InlineData(Changed)]
    public void A_created_or_changed_document_is_upserted(ChangeKind kind)
    {
        var set = Run(new FileChange(kind, "docs/a.md"));
        Assert.Equal(["docs/a.md"], set.Upserts.Select(e => e.RelativePath));
        Assert.Empty(set.Removes);
        Assert.False(set.Rescan);
    }

    [Fact]
    public void Excluded_paths_are_ignored_without_touching_the_disk()
    {
        var set = Run(new FileChange(Created, "node_modules/p/a.md"), new FileChange(Changed, "docs/x.draft.md"));
        Assert.Empty(set.Upserts);
        Assert.False(set.Rescan);
        Assert.Empty(_stats);
    }

    [Fact]
    public void Other_files_are_ignored_without_touching_the_disk()
    {
        var set = Run(new FileChange(Created, "docs/a.txt"), new FileChange(Changed, "docs/b.cs"));
        Assert.Empty(set.Upserts);
        Assert.False(set.Rescan);
        Assert.Empty(_stats);
    }

    [Fact]
    public void A_changed_document_that_vanished_is_removed()
    {
        _missing.Add("a.md");
        var set = Run(new FileChange(Changed, "a.md"));
        Assert.Equal(["a.md"], set.Removes);
        Assert.Empty(set.Upserts);
    }

    [Fact]
    public void A_deleted_document_is_removed()
    {
        var set = Run(new FileChange(Deleted, "docs/a.md"));
        Assert.Equal(["docs/a.md"], set.Removes);
        Assert.False(set.Rescan);
    }

    [Fact]
    public void A_deleted_folder_that_held_documents_triggers_a_rescan()
    {
        _indexed.Add("docs");
        Assert.True(Run(new FileChange(Deleted, "docs")).Rescan);
    }

    [Fact]
    public void A_deleted_non_document_without_indexed_children_is_ignored()
    {
        var set = Run(new FileChange(Deleted, "src"), new FileChange(Deleted, "a.txt"));
        Assert.False(set.Rescan);
        Assert.Empty(set.Removes);
    }

    [Fact]
    public void A_created_folder_triggers_a_rescan_but_an_excluded_one_does_not()
    {
        _dirs.Add("docs");
        _dirs.Add("node_modules");
        Assert.True(Run(new FileChange(Created, "docs")).Rescan);
        Assert.False(Run(new FileChange(Created, "node_modules")).Rescan);
        Assert.False(Run(new FileChange(Created, "node_modules/pkg")).Rescan);
    }

    [Fact]
    public void A_changed_folder_is_ignored()
    {
        _dirs.Add("docs");
        Assert.False(Run(new FileChange(Changed, "docs")).Rescan);
    }

    [Fact]
    public void Rename_document_to_document_removes_the_old_and_upserts_the_new()
    {
        var set = Run(new FileChange(Renamed, "docs/new.md", "docs/old.md"));
        Assert.Equal(["docs/old.md"], set.Removes);
        Assert.Equal(["docs/new.md"], set.Upserts.Select(e => e.RelativePath));
    }

    [Fact]
    public void Editor_temp_file_renamed_over_a_document_counts_as_an_update()
    {
        var set = Run(new FileChange(Renamed, "a.md", "a.md.tmp123"));
        Assert.Equal(["a.md"], set.Upserts.Select(e => e.RelativePath));
        Assert.Empty(set.Removes);
    }

    [Fact]
    public void Rename_document_to_a_non_document_only_removes()
    {
        var set = Run(new FileChange(Renamed, "a.md.bak", "a.md"));
        Assert.Equal(["a.md"], set.Removes);
        Assert.Empty(set.Upserts);
    }

    [Fact]
    public void Rename_of_a_folder_triggers_a_rescan()
    {
        _indexed.Add("old");
        _dirs.Add("new");
        Assert.True(Run(new FileChange(Renamed, "new", "old")).Rescan);
    }

    [Fact]
    public void Error_triggers_a_rescan() =>
        Assert.True(Run(new FileChange(Error, "")).Rescan);

    [Fact]
    public void Too_many_changes_become_a_single_rescan()
    {
        var changes = Enumerable.Range(0, DocumentChangeClassifier.MaxChanges + 1)
            .Select(i => new FileChange(Changed, $"f{i}.md")).ToArray();
        var set = DocumentChangeClassifier.Classify(changes, Matcher, _dirs.Contains, p => Entry(p), _indexed.Contains);
        Assert.True(set.Rescan);
        Assert.Empty(set.Upserts);
        Assert.Empty(set.Removes);
    }

    [Fact]
    public void The_last_action_on_a_path_wins()
    {
        var a = Run(new FileChange(Changed, "a.md"), new FileChange(Deleted, "a.md"));
        Assert.Equal(["a.md"], a.Removes);
        Assert.Empty(a.Upserts);

        var b = Run(new FileChange(Deleted, "a.md"), new FileChange(Created, "a.md"));
        Assert.Empty(b.Removes);
        Assert.Equal(["a.md"], b.Upserts.Select(e => e.RelativePath));
    }
}

public sealed class RescanSchedulerTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(80);

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
    }

    [Fact]
    public async Task Many_requests_become_one_rescan()
    {
        var count = 0;
        using var s = new RescanScheduler(() => Interlocked.Increment(ref count), Debounce);
        for (var i = 0; i < 10; i++)
        {
            s.Request();
            await Task.Delay(5);
        }
        await WaitUntil(() => count >= 1);
        await Task.Delay(300);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task A_request_during_a_scan_runs_once_after_the_scan_completes()
    {
        var count = 0;
        using var s = new RescanScheduler(() => Interlocked.Increment(ref count), Debounce);
        s.Request();
        await WaitUntil(() => count == 1);

        s.Request();                                                  // 走査中
        s.Request();
        await Task.Delay(300);
        Assert.Equal(1, count);                                       // 走査が終わるまで待つ

        s.ScanCompleted();
        await WaitUntil(() => count == 2);
        await Task.Delay(300);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Without_pending_requests_completing_a_scan_does_nothing()
    {
        var count = 0;
        using var s = new RescanScheduler(() => Interlocked.Increment(ref count), Debounce);
        s.MarkScanStarted();
        s.ScanCompleted();
        await Task.Delay(300);
        Assert.Equal(0, count);
    }
}

public sealed class DocumentWatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "miharikun-watch-" + Guid.NewGuid().ToString("N"));

    public DocumentWatcherTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task Reports_a_created_document_with_a_relative_slash_path_after_the_debounce()
    {
        var seen = new List<FileChange>();
        using var w = new DocumentWatcher(_root, GitIgnoreMatcher.Parse("node_modules/"),
            batch => { lock (seen) seen.AddRange(batch); }, TimeSpan.FromMilliseconds(50));
        w.Start();

        Directory.CreateDirectory(Path.Combine(_root, "docs"));
        File.WriteAllText(Path.Combine(_root, "docs", "a.md"), "x");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            lock (seen)
                if (seen.Any(c => c.RelativePath == "docs/a.md"))
                    return;
            await Task.Delay(20);
        }
        Assert.Fail("docs/a.md の変更が届かなかった");
    }

    [Fact]
    public async Task Drops_events_under_excluded_folders()
    {
        var seen = new List<FileChange>();
        using var w = new DocumentWatcher(_root, GitIgnoreMatcher.Parse("node_modules/"),
            batch => { lock (seen) seen.AddRange(batch); }, TimeSpan.FromMilliseconds(50));
        w.Start();

        Directory.CreateDirectory(Path.Combine(_root, "node_modules", "p"));
        File.WriteAllText(Path.Combine(_root, "node_modules", "p", "a.md"), "x");
        File.WriteAllText(Path.Combine(_root, "ok.md"), "x");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            lock (seen)
                if (seen.Any(c => c.RelativePath == "ok.md"))
                    break;
            await Task.Delay(20);
        }
        await Task.Delay(200);
        lock (seen)
        {
            Assert.Contains(seen, c => c.RelativePath == "ok.md");
            Assert.DoesNotContain(seen, c => c.RelativePath.StartsWith("node_modules/p"));
        }
    }
}
