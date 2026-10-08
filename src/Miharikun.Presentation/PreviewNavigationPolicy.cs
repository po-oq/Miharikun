namespace Miharikun;

/// <summary>プレビューの中で起きた移動を、どう扱うか。</summary>
public enum PreviewNavigationAction
{
    /// <summary>そのまま WebView に任せる（自分で開いたページ・ページ内の # 移動・読み込み中の部品）。</summary>
    Allow,

    /// <summary>WebView の移動は取り消し、リンクとして振り分ける（http は既定のブラウザ、ローカルのファイルはホストへ）。</summary>
    Route,
}

/// <summary>
/// プレビュー内のリンクの振り分け（要件 12.7）。WebView の種類に依らない判断だけをここに置く。
/// mac の WKWebView は iframe の読み込みにも NavigationStarted が来るため、ページを読み込んでいる間は、
/// 自分で開いたページ以外の移動（iframe・部品）も通す。読み込みが終わってからの移動は、リンクのクリックとして振り分ける。
/// </summary>
public static class PreviewNavigationPolicy
{
    public static PreviewNavigationAction Decide(Uri target, string? pagePath, bool isLoadingPage)
    {
        if (target.Scheme is "about" or "data" or "blob")
            return PreviewNavigationAction.Allow;

        // 自分で開いたページ（および同じページ内の #リンク）は通す。
        if (target.IsFile && pagePath is not null
            && string.Equals(target.LocalPath, pagePath, StringComparison.OrdinalIgnoreCase))
            return PreviewNavigationAction.Allow;

        // 自分で開いたページを読み込んでいる間は、その中の iframe などの読み込みなので通す。
        if (isLoadingPage)
            return PreviewNavigationAction.Allow;

        return PreviewNavigationAction.Route;
    }

    /// <summary>振り分けた先が「既定のアプリで開くもの」か（http・https・mailto）。</summary>
    public static bool IsExternal(Uri uri) => uri.Scheme is "http" or "https" or "mailto";

    /// <summary>JavaScript の <c>window.scrollY</c> の結果（"123.5" など）を、戻す位置（整数のピクセル）にする。読めなければ 0。</summary>
    public static int ParseScrollY(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return 0;
        return double.TryParse(script.Trim('"'), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var y) && y > 0 && y < 100_000_000
            ? (int)Math.Round(y)
            : 0;
    }
}
