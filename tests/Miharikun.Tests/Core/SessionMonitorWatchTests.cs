using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

/// <summary>Phase 18-3：監視先（WatchTargets）の取り直しと、フォルダを作る・作らないの規則。</summary>
public sealed class SessionMonitorWatchTests : IDisposable
{
    private sealed class DirSource(string agentId) : ISessionSource
    {
        private readonly object _lock = new();
        private List<WatchTarget> _targets = [];
        private int _targetReads;
        public string AgentId { get; } = agentId;

        public IReadOnlyList<WatchTarget> WatchTargets
        {
            get
            {
                lock (_lock)
                {
                    _targetReads++;
                    return [.. _targets];
                }
            }
        }

        public int TargetReads { get { lock (_lock) return _targetReads; } }

        public void SetTargets(params WatchTarget[] targets)
        {
            lock (_lock) _targets = [.. targets];
        }

        /// <summary>監視先のフォルダにある *.txt を、ファイル名のセッションとして出す（1 回だけ）。</summary>
        private readonly HashSet<string> _seen = [];

        public IReadOnlyList<SessionDelta> ReadNew()
        {
            var deltas = new List<SessionDelta>();
            foreach (var t in WatchTargets)
            {
                if (!Directory.Exists(t.Directory))
                    continue;
                foreach (var f in Directory.GetFiles(t.Directory, "*.txt"))
                {
                    if (_seen.Add(f))
                        deltas.Add(new SessionDelta(new SessionKey(AgentId, Path.GetFileNameWithoutExtension(f)),
                            SessionDeltaKind.Append, Events(E("beforeSubmitPrompt", 0, "\"prompt\":\"a\""))));
                }
            }
            return deltas;
        }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-watch-" + Guid.NewGuid().ToString("N"));
    private readonly DirSource _source = new("fake");
    private readonly List<SessionUpdate> _updates = [];
    private readonly object _lock = new();
    private readonly SessionMonitor _monitor;

    public SessionMonitorWatchTests()
    {
        Directory.CreateDirectory(_dir);
        _monitor = new SessionMonitor(new ProjectEventStore([_source]),
            debounce: TimeSpan.FromMilliseconds(50), pollInterval: TimeSpan.FromMilliseconds(100));
        _monitor.Updated += u => { lock (_lock) _updates.Add(u); };
    }

    public void Dispose()
    {
        _monitor.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private bool WaitFor(Func<bool> condition, int timeoutMs = 6000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    private bool Seen(string session)
    {
        lock (_lock)
            return _updates.SelectMany(u => u.Upserts).Any(s => s.Summary.Key.SessionId == session);
    }

    [Fact]
    public void Missing_folder_is_not_created_unless_CreateIfMissing_and_is_watched_once_it_appears()
    {
        var keep = Path.Combine(_dir, "not-created");
        var create = Path.Combine(_dir, "created");
        _source.SetTargets(new WatchTarget(keep, "*.txt", CreateIfMissing: false), new WatchTarget(create, "*.txt", CreateIfMissing: true));

        _monitor.Start();

        Assert.True(WaitFor(() => Directory.Exists(create)), "CreateIfMissing のフォルダは作る");
        Thread.Sleep(400);   // ポーリングを数回まわす
        Assert.False(Directory.Exists(keep), "CreateIfMissing=false のフォルダは作らない");

        Directory.CreateDirectory(keep);   // あとからできたフォルダも読まれる
        File.WriteAllText(Path.Combine(keep, "late.txt"), "x");
        Assert.True(WaitFor(() => Seen("late")));
    }

    [Fact]
    public void Targets_are_asked_again_on_every_poll_and_new_ones_are_picked_up()
    {
        _source.SetTargets();
        _monitor.Start();
        Assert.True(WaitFor(() => _source.TargetReads >= 3), "ポーリングのたびに取り直す");

        var added = Path.Combine(_dir, "added");
        _source.SetTargets(new WatchTarget(added, "*.txt", CreateIfMissing: true));

        Assert.True(WaitFor(() => Directory.Exists(added)), "増えた監視先が張られる");
        File.WriteAllText(Path.Combine(added, "new.txt"), "x");
        Assert.True(WaitFor(() => Seen("new")));
    }

    [Fact]
    public void Manual_refresh_before_start_does_not_create_folders()
    {
        var create = Path.Combine(_dir, "created");
        _source.SetTargets(new WatchTarget(create, "*.txt", CreateIfMissing: true));

        _monitor.RefreshNow();

        Assert.False(Directory.Exists(create));
    }

    [Fact]
    public void A_folder_that_disappears_and_comes_back_is_watched_again()
    {
        var target = Path.Combine(_dir, "t");
        _source.SetTargets(new WatchTarget(target, "*.txt", CreateIfMissing: false));
        Directory.CreateDirectory(target);
        _monitor.Start();
        File.WriteAllText(Path.Combine(target, "a.txt"), "x");
        Assert.True(WaitFor(() => Seen("a")));

        Directory.Delete(target, true);
        Thread.Sleep(400);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "b.txt"), "x");

        Assert.True(WaitFor(() => Seen("b")));
    }

    /// <summary>監視先の下を再帰的に探して *.txt をファイル名のセッションとして出す（1 回だけ）。</summary>
    private sealed class TreeSource(string dir, bool includeSubdirectories) : ISessionSource
    {
        private readonly HashSet<string> _seen = [];
        public string AgentId => "tree";
        public IReadOnlyList<WatchTarget> WatchTargets =>
            [new WatchTarget(dir, "*.txt", CreateIfMissing: false, IncludeSubdirectories: includeSubdirectories)];

        public IReadOnlyList<SessionDelta> ReadNew() =>
        [
            .. Directory.GetFiles(dir, "*.txt", SearchOption.AllDirectories)
                .Where(f => _seen.Add(f))
                .Select(f => new SessionDelta(new SessionKey(AgentId, Path.GetFileNameWithoutExtension(f)),
                    SessionDeltaKind.Append, Events(E("beforeSubmitPrompt", 0, "\"prompt\":\"a\"")))),
        ];
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Subfolder_changes_notify_only_when_IncludeSubdirectories_is_set(bool includeSubdirectories)
    {
        var root = Path.Combine(_dir, "tree");
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        var seen = new List<string>();
        var gate = new object();
        // ポーリングは 1 時間に 1 回（最初の 1 回だけ）なので、Watcher の通知だけで読み込みが起きる。
        using var monitor = new SessionMonitor(new ProjectEventStore([new TreeSource(root, includeSubdirectories)]),
            debounce: TimeSpan.FromMilliseconds(50), pollInterval: TimeSpan.FromHours(1));
        monitor.Updated += u =>
        {
            lock (gate)
                seen.AddRange(u.Upserts.Select(s => s.Summary.Key.SessionId));
        };
        monitor.Start();
        Thread.Sleep(500);   // 最初の読み込みと Watcher の準備

        File.WriteAllText(Path.Combine(sub, "deep.txt"), "x");

        var notified = WaitFor(() => { lock (gate) return seen.Contains("deep"); }, timeoutMs: 3000);
        Assert.Equal(includeSubdirectories, notified);
    }

    [Fact]
    public void Dispose_releases_the_watched_folder()
    {
        var target = Path.Combine(_dir, "t");
        _source.SetTargets(new WatchTarget(target, "*.txt", CreateIfMissing: true));
        _monitor.Start();
        Assert.True(WaitFor(() => Directory.Exists(target)));

        _monitor.Dispose();

        Directory.Delete(target, true);   // Watcher が残っていると、ここで失敗しうる
        Assert.False(Directory.Exists(target));
    }
}
