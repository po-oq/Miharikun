namespace Miharikun.Core.Documents;

/// <summary>
/// 対象フォルダの FileSystemWatcher（要件 12.7）。イベントを 300ms ためて、まとめて通知する。
/// 拡張子では絞らない（フォルダのイベントも要るため）。除外に当たるパスのイベントは、ここで捨てる。
/// 振り分け（差分 or 再走査）は <see cref="DocumentChangeClassifier"/>。索引に触るので、通知を受けたら索引の所有スレッドへ渡すこと。
/// </summary>
public sealed class DocumentWatcher : IDisposable
{
    private const int MaxQueued = 2000;

    private readonly string _root;
    private readonly GitIgnoreMatcher _matcher;
    private readonly Action<IReadOnlyList<FileChange>> _onChanges;
    private readonly TimeSpan _debounce;
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private List<FileChange> _queue = [];
    private bool _overflowed;
    private bool _disposed;
    private FileSystemWatcher? _watcher;
    private readonly Timer _timer;

    /// <param name="onChanges">スレッドプールから呼ばれる。UI スレッドへ Post するだけにして、ブロックしないこと。</param>
    public DocumentWatcher(string root, GitIgnoreMatcher matcher, Action<IReadOnlyList<FileChange>> onChanges,
        TimeSpan? debounce = null, Action<string>? log = null)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _matcher = matcher;
        _onChanges = onChanges;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        _log = log;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>監視を始める。始められなくても例外にはしない（「読み直し」で使える）。</summary>
    public void Start()
    {
        try
        {
            var w = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,                          // あふれにくくする（あふれても再走査で直る）
            };
            w.Created += (_, e) => Enqueue(ChangeKind.Created, e.FullPath);
            w.Changed += (_, e) => Enqueue(ChangeKind.Changed, e.FullPath);
            w.Deleted += (_, e) => Enqueue(ChangeKind.Deleted, e.FullPath);
            w.Renamed += (_, e) => Enqueue(ChangeKind.Renamed, e.FullPath, e.OldFullPath);
            w.Error += (_, _) => EnqueueError();
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _log?.Invoke($"{_root} を監視できない: {ex.Message}");
        }
    }

    private void Enqueue(ChangeKind kind, string fullPath, string? oldFullPath = null)
    {
        var rel = ToRelative(fullPath);
        var old = oldFullPath is null ? null : ToRelative(oldFullPath);
        if (rel is null && old is null)
            return;

        // 名前変更は、新旧どちらかが対象なら残す。それ以外は、除外に当たるパス（親が除外のものも含む）を捨てる。
        var relExcluded = rel is null || _matcher.IsPathExcluded(rel, isDirectory: false);
        var oldExcluded = old is null || _matcher.IsPathExcluded(old, isDirectory: false);
        if (relExcluded && oldExcluded)
            return;

        Add(new FileChange(kind, rel ?? old!, kind == ChangeKind.Renamed ? old : null));
    }

    private void EnqueueError() => Add(new FileChange(ChangeKind.Error, ""));

    private void Add(FileChange change)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_overflowed)
                return;                                                  // あふれたら 1 件の Error にまとめて、これ以上ためない
            if (_queue.Count >= MaxQueued)
            {
                _queue.Clear();
                _queue.Add(new FileChange(ChangeKind.Error, ""));
                _overflowed = true;
            }
            else
            {
                _queue.Add(change);
            }
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        List<FileChange> batch;
        lock (_gate)
        {
            if (_disposed || _queue.Count == 0)
                return;
            batch = _queue;
            _queue = [];
            _overflowed = false;
        }

        try
        {
            _onChanges(batch);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"ドキュメントの変更通知で例外: {ex.Message}");   // タイマースレッドの例外でプロセスを落とさない
        }
    }

    private string? ToRelative(string fullPath)
    {
        var rel = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
        return rel == "." || rel.StartsWith("../", StringComparison.Ordinal) || rel == ".." ? null : rel;
    }

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
        _watcher?.Dispose();
        _timer.Dispose();
    }
}
