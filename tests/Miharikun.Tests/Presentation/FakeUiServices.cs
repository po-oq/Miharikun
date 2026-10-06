using Miharikun.Services;

namespace Miharikun.Tests.Presentation;

/// <summary>テスト用の <see cref="IUiTimer"/>。<see cref="Fire"/> で手で 1 回進める（動いているときだけ）。</summary>
public sealed class FakeTimer(TimeSpan interval, Action tick) : IUiTimer
{
    public TimeSpan Interval { get; } = interval;

    public bool IsEnabled { get; private set; }

    public bool Disposed { get; private set; }

    public void Start() => IsEnabled = true;

    public void Stop() => IsEnabled = false;

    public void Dispose()
    {
        Disposed = true;
        IsEnabled = false;
    }

    /// <summary>1 回進める。止まっていれば何も起きない（本物のタイマーと同じ）。</summary>
    public void Fire()
    {
        if (IsEnabled)
            tick();
    }
}

/// <summary>テスト用の <see cref="IUiServices"/>。タイマーは手で進め、確認の答えとクリップボードの成否は決めておける。呼ばれた値は記録する。</summary>
public sealed class FakeUiServices : IUiServices
{
    public List<FakeTimer> Timers { get; } = [];

    public bool ClipboardSucceeds { get; set; } = true;

    public List<string> Clipboard { get; } = [];

    public List<string> Opened { get; } = [];

    public List<string> Revealed { get; } = [];

    /// <summary>確認（はい／いいえ）の答え。<see cref="OnConfirm"/> があればそちらが先。</summary>
    public bool ConfirmAnswer { get; set; } = true;

    /// <summary>確認の最中に起きることを仕込む（確認している間に状態が変わる場合など）。</summary>
    public Func<bool>? OnConfirm { get; set; }

    public List<(string Title, string Message)> Confirms { get; } = [];

    public List<(string Title, string Message, MessageKind Kind)> Messages { get; } = [];

    public string RevealButtonText => "フォルダで開く";

    public IUiTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var timer = new FakeTimer(interval, tick);
        Timers.Add(timer);
        return timer;
    }

    /// <summary>指定の間隔で作られたタイマー（1 つだけ）。</summary>
    public FakeTimer TimerOf(TimeSpan interval) => Timers.Single(t => t.Interval == interval);

    public Task<bool> SetClipboardTextAsync(string text)
    {
        if (ClipboardSucceeds)
            Clipboard.Add(text);
        return Task.FromResult(ClipboardSucceeds);
    }

    public void OpenWithDefaultApp(string target) => Opened.Add(target);

    public void RevealInFileManager(string file) => Revealed.Add(file);

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Confirms.Add((title, message));
        return Task.FromResult(OnConfirm?.Invoke() ?? ConfirmAnswer);
    }

    public Task ShowMessageAsync(string title, string message, MessageKind kind)
    {
        Messages.Add((title, message, kind));
        return Task.CompletedTask;
    }
}

/// <summary>Post をその場で実行する <see cref="SynchronizationContext"/>（UI スレッドへの受け渡しの代わり）。</summary>
public sealed class ImmediateSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => d(state);

    public override void Send(SendOrPostCallback d, object? state) => d(state);
}
