using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using Miharikun.Core.Storage;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

public sealed class SessionMonitorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-mon-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly SessionMonitor _monitor;
    private readonly List<SessionUpdate> _updates = [];
    private readonly object _lock = new();

    public SessionMonitorTests()
    {
        _paths = new AppPaths(_dir);
        var store = new ProjectEventStore(new CursorAgent(), _paths, Root);
        _monitor = new SessionMonitor(store, _paths.EventsDir("cursor"));
        _monitor.Updated += u => { lock (_lock) _updates.Add(u); };
    }

    public void Dispose()
    {
        _monitor.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private void Append(string conv, string line)
    {
        var path = _paths.EventFile("cursor", conv);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, line + "\n");
    }

    private bool WaitFor(Func<bool> condition, int timeoutMs = 6000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(25);
        }
        return condition();
    }

    private SessionState? LastState(string conv)
    {
        lock (_lock)
            return _updates.SelectMany(u => u.Upserts).LastOrDefault(s => s.Summary.Key.SessionId == conv)?.Summary.State;
    }

    [Fact]
    public void RefreshNow_reports_changed_sessions_with_search_text()
    {
        Append("s1", Line("beforeSubmitPrompt", "\"prompt\":\"検索したい依頼\"", 0, "s1"));

        _monitor.RefreshNow();

        var snap = Assert.Single(Assert.Single(_updates).Upserts);
        Assert.Equal(SessionState.Running, snap.Summary.State);
        Assert.Contains("検索したい依頼", snap.SearchText);

        _monitor.RefreshNow();
        Assert.Single(_updates);   // 変化がなければ通知しない
    }

    [Fact]
    public void Existing_events_are_loaded_on_start_and_appended_ones_arrive_within_seconds()
    {
        Append("s1", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"));
        _monitor.Start();
        Assert.True(WaitFor(() => LastState("s1") == SessionState.Running), "起動時の読み込み");

        Append("s1", Line("stop", "\"status\":\"completed\"", 1, "s1"));
        Assert.True(WaitFor(() => LastState("s1") == SessionState.YourTurn), "追記の反映");

        Append("s2", Line("beforeSubmitPrompt", "\"prompt\":\"b\"", 0, "s2"));   // 新しいファイル
        Assert.True(WaitFor(() => LastState("s2") == SessionState.Running), "新規セッションの反映");
    }

    [Fact]
    public void Works_when_the_events_dir_does_not_exist_yet()
    {
        _monitor.Start();
        Thread.Sleep(200);

        Append("s1", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"));

        Assert.True(WaitFor(() => LastState("s1") == SessionState.Running));
    }

    [Fact]
    public void Dispose_stops_notifications()
    {
        _monitor.Start();
        _monitor.Dispose();
        Append("s1", Line("beforeSubmitPrompt", "\"prompt\":\"a\"", 0, "s1"));

        Thread.Sleep(500);
        _monitor.RefreshNow();

        lock (_lock) Assert.Empty(_updates);
    }
}