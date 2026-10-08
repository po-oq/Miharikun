namespace Miharikun.Services;

public enum MessageKind
{
    Information,
    Warning,
}

/// <summary>ViewModel が使う、画面の仕組み（WPF・Avalonia）への口。ViewModel はウィンドウを知らない（どのウィンドウの上に出すかは実装が持つ）。</summary>
public interface IUiServices
{
    /// <summary>UI スレッドで定期的に <paramref name="tick"/> を呼ぶタイマー（優先度は Background）。作っただけでは動かない（Start で動く）。</summary>
    IUiTimer CreateTimer(TimeSpan interval, Action tick);

    /// <summary>クリップボードへコピーする。できなければ false。</summary>
    Task<bool> SetClipboardTextAsync(string text);

    /// <summary>URL・ファイル・フォルダを、既定のアプリで開く。</summary>
    void OpenWithDefaultApp(string target);

    /// <summary>ファイルを選んだ状態で、ファイラー（エクスプローラー）で開く。</summary>
    void RevealInFileManager(string file);

    /// <summary>「フォルダで開く」ボタンの文字（要件 12.12）。</summary>
    string RevealButtonText { get; }

    /// <summary>はい／いいえの確認。はい＝true。</summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>お知らせ（OK だけ）。</summary>
    Task ShowMessageAsync(string title, string message, MessageKind kind);
}
