using Miharikun.Core.Agents;

namespace Miharikun.Core.Sessions;

/// <summary>
/// events\ を FileSystemWatcher（300ms デバウンス）と 3秒ごとのポーリングで監視し、変わったセッションを通知する（要件 9章）。
/// ProjectEventStore へのアクセスはすべてここのロック内で行う。
/// </summary>
public sealed class SessionMonitor : IDisposable
{
    private readonly ProjectEventStore _store;
    private readonly string _eventsDir;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _pollInterval;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private Timer? _pollTimer;
    private bool _disposed;

    /// <summary>
    /// 監視スレッド（スレッドプール）から、ロックを持ったまま呼ばれる。
    /// 通知順を保つためで、ハンドラーは UI スレッドへ Post するだけにして、ブロックしないこと。
    /// </summary>
    public event Action<SessionUpdate>? Updated;

    public SessionMonitor(ProjectEventStore store, string eventsDir, TimeSpan? debounce = null, TimeSpan? pollInterval = null)
    {
        _store = store;
        _eventsDir = eventsDir;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>監視を始める。最初の読み込みもバックグラウンドで行うので、すぐ戻る。</summary>
    public void Start()
    {
        _debounceTimer = new Timer(_ => RefreshNow(), null, Timeout.Infinite, Timeout.Infinite);
        StartWatcher();
        _pollTimer = new Timer(_ => RefreshNow(), null, TimeSpan.Zero, _pollInterval);
    }

    private void StartWatcher()
    {
        try
        {
            Directory.CreateDirectory(_eventsDir);
            var w = new FileSystemWatcher(_eventsDir, "*.jsonl")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            w.Created += (_, _) => Schedule();
            w.Changed += (_, _) => Schedule();
            w.Renamed += (_, _) => Schedule();
            w.Deleted += (_, _) => Schedule();
            w.Error += (_, _) => Schedule();   // バッファあふれ。取りこぼしはこの再読み込みとポーリングで拾う。
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // 監視できなくてもポーリングで動く。
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
        }
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
        _pollTimer?.Dispose();
    }
}