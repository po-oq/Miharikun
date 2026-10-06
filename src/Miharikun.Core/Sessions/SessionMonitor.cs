using Miharikun.Core.Agents;

namespace Miharikun.Core.Sessions;

/// <summary>
/// Store の各 Source が指す監視先（WatchTargets）を FileSystemWatcher（300ms デバウンス）と 3秒ごとのポーリングで監視し、
/// 変わったセッションを通知する（要件 9章）。監視先は Start とポーリングのたびに取り直し、増えた分だけ Watcher を張る
/// （まだ無いフォルダは、できるまで次回また試す。CreateIfMissing の監視先だけ、無ければ作る）。
/// ProjectEventStore へのアクセスはすべてここのロック内で行う。
/// </summary>
public sealed class SessionMonitor : IDisposable
{
    private readonly ProjectEventStore _store;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _pollInterval;
    private readonly object _gate = new();
    private readonly Dictionary<(string Directory, string Filter), FileSystemWatcher> _watchers = [];
    /// <summary>Error（バッファあふれ・フォルダの削除など）を出した Watcher。次の取り直しで捨てて張り直す。ロックなしで書くので ConcurrentDictionary。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Directory, string Filter), byte> _broken = [];
    private Timer? _debounceTimer;
    private Timer? _pollTimer;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// 監視スレッド（スレッドプール）から、ロックを持ったまま呼ばれる。
    /// 通知順を保つためで、ハンドラーは UI スレッドへ Post するだけにして、ブロックしないこと。
    /// </summary>
    public event Action<SessionUpdate>? Updated;

    public SessionMonitor(ProjectEventStore store, TimeSpan? debounce = null, TimeSpan? pollInterval = null)
    {
        _store = store;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>監視を始める。最初の読み込みもバックグラウンドで行うので、すぐ戻る。</summary>
    public void Start()
    {
        _debounceTimer = new Timer(_ => RefreshNow(), null, Timeout.Infinite, Timeout.Infinite);
        lock (_gate)
        {
            _started = true;
            EnsureWatchers();
        }
        _pollTimer = new Timer(_ => RefreshNow(), null, TimeSpan.Zero, _pollInterval);
    }

    /// <summary>監視先を取り直し、まだ張っていないものに Watcher を張る。消えたフォルダの Watcher は捨てる。_gate の中で呼ぶこと。</summary>
    private void EnsureWatchers()
    {
        foreach (var (key, watcher) in _watchers.ToList())
        {
            // 消えて同じ名前で作り直されたフォルダは、Exists が true でも古い Watcher は通知が来ない。Error の印で張り直す。
            if (_broken.TryRemove(key, out _) || !Directory.Exists(key.Directory))
            {
                watcher.Dispose();
                _watchers.Remove(key);
            }
        }

        foreach (var target in _store.WatchTargets)
        {
            var key = (target.Directory, target.Filter);
            if (_watchers.ContainsKey(key))
                continue;

            try
            {
                if (!Directory.Exists(target.Directory))
                {
                    if (!target.CreateIfMissing)
                        continue;   // できたら、次回のポーリングで張る
                    Directory.CreateDirectory(target.Directory);
                }

                var w = new FileSystemWatcher(target.Directory, target.Filter)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                    IncludeSubdirectories = target.IncludeSubdirectories,
                };
                w.Created += (_, _) => Schedule();
                w.Changed += (_, _) => Schedule();
                w.Renamed += (_, _) => Schedule();
                w.Deleted += (_, _) => Schedule();
                w.Error += (_, _) =>   // バッファあふれ・フォルダの削除。取りこぼしはこの再読み込みとポーリングで拾い、Watcher は次回張り直す。
                {
                    _broken[key] = 0;
                    Schedule();
                };
                w.EnableRaisingEvents = true;
                _watchers[key] = w;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // 監視できなくてもポーリングで動く。次回また試す。
            }
        }
    }

    private void Schedule()
    {
        try { _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>今すぐ読み込む（テスト・手動更新用）。</summary>
    public void RefreshNow()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            if (_started)
                EnsureWatchers();   // Start 前の手動更新では Watcher を張らない

            var changed = _store.Refresh();
            if (changed.Count == 0)
                return;

            var upserts = new List<SessionSnapshot>();
            var removed = new List<SessionKey>();
            foreach (var key in changed)
            {
                var summary = _store.GetSummary(key);
                if (summary is null)
                    removed.Add(key);
                else
                    upserts.Add(new SessionSnapshot(summary, SessionSearch.BuildSearchText(summary, _store.GetEvents(key))));
            }
            Updated?.Invoke(new SessionUpdate(upserts, removed));
        }
    }

    /// <summary>セッション1つ分のイベントのコピー（タイムライン用）。呼び出し元のスレッドで自由に読める。</summary>
    public IReadOnlyList<AgentEvent> GetEvents(SessionKey key)
    {
        lock (_gate)
            return _store.GetEvents(key).ToArray();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var w in _watchers.Values)
                w.Dispose();
            _watchers.Clear();
        }
        _debounceTimer?.Dispose();
        _pollTimer?.Dispose();
    }
}
