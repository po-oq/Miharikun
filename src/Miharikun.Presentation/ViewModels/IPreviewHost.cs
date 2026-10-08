using Miharikun.Core.Documents;

namespace Miharikun.ViewModels;

/// <summary>プレビューに出すもの。</summary>
public abstract record PreviewSource
{
    /// <summary>ドキュメント。html はそのまま、md は読んで HTML にする（相対パスの基準は md のフォルダ）。</summary>
    public sealed record File(string FullPath, string RelativePath, DocumentKind Kind) : PreviewSource;

    /// <summary>メモ。文字をそのまま HTML にする。CacheKey は一時 HTML の名前の元（ほかの md と重ならないもの）。</summary>
    public sealed record Markdown(string Text, string BaseFolder, string CacheKey, string Title) : PreviewSource;

    /// <summary>WebView2 を出さずに、案内の文字だけ出す。</summary>
    public sealed record Message(string Text) : PreviewSource;
}

/// <summary>md プレビューの部品（画面の MarkdownPreview）を使う側（ドキュメントタブ・メモタブ）。</summary>
public interface IPreviewHost
{
    /// <summary>md の一時 HTML の置き場。</summary>
    string PreviewDir { get; }

    bool IsDark { get; }

    /// <summary>何も出すものが無いとき（null）の文。</summary>
    string EmptyText { get; }

    /// <summary>WebView2 Runtime が無いときの案内の 2 行目。</summary>
    string RuntimeMissingNote { get; }

    /// <summary>プレビューの表示が変わる。第 2 引数が true なら、同じものでも作り直す。UI スレッドで呼ばれる。</summary>
    event Action<PreviewSource?, bool>? PreviewChanged;

    void Log(string message);

    /// <summary>プレビュー内のリンク（ローカルのファイル・フォルダ）を開く。</summary>
    void OpenLocalLink(string fullPath);
}
