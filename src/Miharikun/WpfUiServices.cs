using System.Windows;
using System.Windows.Threading;
using Miharikun.Services;

namespace Miharikun;

/// <summary>WPF での <see cref="IUiServices"/>（計画 7.2）。Phase 30 で Avalonia の実装に置き換わる。</summary>
public sealed class WpfUiServices(Func<Window?> owner) : IUiServices
{
    public IUiTimer CreateTimer(TimeSpan interval, Action tick) => new WpfTimer(interval, tick);

    // 今のやり直し（ClipboardHelper）のまま、その場で結果を返す
    public Task<bool> SetClipboardTextAsync(string text) => Task.FromResult(ClipboardHelper.TrySetText(text));

    public void OpenWithDefaultApp(string target) => ShellOpen.Open(target);

    public void RevealInFileManager(string file) => ShellOpen.Reveal(file);

    public string RevealButtonText => "フォルダで開く";

    public Task<bool> ConfirmAsync(string title, string message) =>
        Task.FromResult(Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

    public Task ShowMessageAsync(string title, string message, MessageKind kind)
    {
        Show(message, title, MessageBoxButton.OK, kind == MessageKind.Warning ? MessageBoxImage.Warning : MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    private MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage icon) =>
        owner() is { } window
            ? MessageBox.Show(window, message, title, buttons, icon)
            : MessageBox.Show(message, title, buttons, icon);

    /// <summary>今の <c>DispatcherTimer</c>（Background）と同じ。</summary>
    private sealed class WpfTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public WpfTimer(TimeSpan interval, Action tick)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
            _timer.Tick += (_, _) => tick();
        }

        public bool IsEnabled => _timer.IsEnabled;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public void Dispose() => _timer.Stop();
    }
}
