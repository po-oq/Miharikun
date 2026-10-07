using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Miharikun.Views;

namespace Miharikun.Services;

/// <summary>Avalonia での <see cref="IUiServices"/>（計画 7.2）。確認・お知らせはメインウィンドウの上に出す。</summary>
public sealed class AvaloniaUiServices(Func<Window?> owner) : IUiServices
{
    public IUiTimer CreateTimer(TimeSpan interval, Action tick) => new AvaloniaTimer(interval, tick);

    public async Task<bool> SetClipboardTextAsync(string text)
    {
        try
        {
            if (owner() is not { } window || TopLevel.GetTopLevel(window)?.Clipboard is not { } clipboard)
                return false;
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("クリップボードにコピーできなかった: " + ex.Message);
            return false;
        }
    }

    public void OpenWithDefaultApp(string target) => ShellOpen.Open(target);

    public void RevealInFileManager(string file) => ShellOpen.Reveal(file);

    public string RevealButtonText => ShellOpen.RevealButtonText;

    /// <summary>確認・お知らせを出している数。出している間に、もう一度ウィンドウを閉じようとしたときの判断に使う（計画 7.6 の 5）。</summary>
    public int OpenDialogs { get; private set; }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        OpenDialogs++;
        try { return await MessageDialog.ConfirmAsync(owner(), title, message); }
        finally { OpenDialogs--; }
    }

    public async Task ShowMessageAsync(string title, string message, MessageKind kind)
    {
        OpenDialogs++;
        try { await MessageDialog.ShowAsync(owner(), title, message, kind); }
        finally { OpenDialogs--; }
    }

    /// <summary>Background の優先度（WPF の <c>DispatcherTimer</c> の既定と同じ。計画 7.4）。</summary>
    private sealed class AvaloniaTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public AvaloniaTimer(TimeSpan interval, Action tick)
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
