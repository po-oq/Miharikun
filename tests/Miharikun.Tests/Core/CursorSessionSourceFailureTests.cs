using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using Miharikun.Core.Storage;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

/// <summary>Phase 18 のレビュー A1：Source の中の例外で、すでに読んだ分の差分を捨てない・取り込みを二度と走らせなくしない。</summary>
public sealed class CursorSessionSourceFailureTests : IDisposable
{
    /// <summary>最初の何回かの Scan で例外を投げ、そのあとは何も取り込まない。</summary>
    private sealed class ThrowingImporter(int failTimes, Exception error) : CursorTranscriptImporter("unused")
    {
        public int Calls { get; private set; }

        public override IReadOnlyList<ImportedSession> Scan(string projectFolder, Func<string, bool>? skip)
        {
            Calls++;
            if (Calls <= failTimes)
                throw error;
            return [];
        }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-srcfail-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly List<string> _logs = [];

    public CursorSessionSourceFailureTests() => _paths = new AppPaths(Path.Combine(_dir, "data"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private void WriteHook(string conv, params string[] lines)
    {
        var path = _paths.EventFile("cursor", conv);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, string.Concat(lines.Select(l => l + "\n")));
    }

    [Fact]
    public void A_failing_import_scan_does_not_drop_the_hook_deltas_of_the_same_read()
    {
        WriteHook("s1", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"));
        var importer = new ThrowingImporter(1, new InvalidOperationException("取り込みの失敗"));
        var source = new CursorSessionSource(new CursorAgent(), _paths, Root, log: _logs.Add, importer: importer);

        var deltas = source.ReadNew();

        var delta = Assert.Single(deltas);
        Assert.Equal(new SessionKey("cursor", "s1"), delta.Key);
        Assert.Contains(_logs, l => l.Contains("取り込みの失敗") && l.Contains("InvalidOperationException"));
    }

    [Fact]
    public void A_failing_import_scan_is_tried_again_on_the_next_read()
    {
        var importer = new ThrowingImporter(1, new InvalidOperationException("取り込みの失敗"));
        var source = new CursorSessionSource(new CursorAgent(), _paths, Root, log: _logs.Add, importer: importer);

        source.ReadNew();
        Assert.Equal(1, importer.Calls);

        source.ReadNew();
        Assert.Equal(2, importer.Calls);   // 失敗したので、また試す

        source.ReadNew();
        Assert.Equal(2, importer.Calls);   // 成功したので、もう試さない
    }

    [Fact]
    public void The_store_still_gets_the_hook_events_when_the_import_scan_fails()
    {
        WriteHook("s1", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"));
        var source = new CursorSessionSource(new CursorAgent(), _paths, Root, log: _logs.Add,
            importer: new ThrowingImporter(1, new UnauthorizedAccessException("拒否")));
        var store = new ProjectEventStore([source], log: _logs.Add);

        Assert.Equal([new SessionKey("cursor", "s1")], store.Refresh());
        Assert.Equal(SessionState.Running, store.GetSummary(new SessionKey("cursor", "s1"))!.State);
    }
}
