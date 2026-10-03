namespace Miharikun.Core.Documents;

/// <summary>
/// 全再走査の予約（要件 12.7）。<see cref="Request"/> が続いても、静かになってから 1 回だけ実行する（デバウンス）。
/// 走査中の要求は、<see cref="ScanCompleted"/> のあとに 1 回だけ実行する。
/// 手動の「読み直し」など、このクラス以外で走査を始めるときは <see cref="MarkScanStarted"/> を呼ぶ。
/// </summary>
public sealed class RescanScheduler : IDisposable
{
    private readonly Action _rescan;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private bool _requested;
    private bool _scanning;
    private bool _disposed;

    /// <param name="rescan">スレッドプールから呼ばれる。ここで走査を始め、終わったら <see cref="ScanCompleted"/> を呼ぶこと。</param>
    public RescanScheduler(Action rescan, TimeSpan? debounce = null)
    {
        _rescan = rescan;
        _debounce = debounce ?? TimeSpan.FromSeconds(1);
        _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Request()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _requested = true;
            if (!_scanning)
                _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>外で走査を始めた。待っていた要求は、その走査が拾うので取り消す。</summary>
    public void MarkScanStarted()
    {
        lock (_gate)
        {
            _scanning = true;
            _requested = false;
            if (!_disposed)
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public void ScanCompleted()
    {
        lock (_gate)
        {
            _scanning = false;
            if (_requested && !_disposed)
                _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed || !_requested || _scanning)
                return;
            _requested = false;
            _scanning = true;
        }
        _rescan();
    }

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
        _timer.Dispose();
    }
}
