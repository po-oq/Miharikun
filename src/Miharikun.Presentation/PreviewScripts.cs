using System.Text.Json;

namespace Miharikun;

/// <summary>プレビュー（WebView）のページへ送るスクリプトと、ページからの知らせの判定（Issue #28。リフレクションを使わない）。</summary>
public static class PreviewScripts
{
    /// <summary>
    /// 見出し（id）へスクロールするスクリプト。id は JSON の文字列として逃がす（<c>"</c>・<c>\</c>・<c>&lt;</c>・改行・U+2028 など）。
    /// 見つからなければ何もしない。<c>location.hash</c> は <c>&lt;base&gt;</c> のため使わない。
    /// </summary>
    public static string ScrollToHeading(string id) =>
        "(function(){var el=document.getElementById(\"" + JsonEncodedText.Encode(id) + "\");if(el)el.scrollIntoView();})()";

    /// <summary>ページからの知らせ（<c>invokeCSharpAction</c> の文字）が Esc か。Windows で JSON の文字列の形（前後が <c>"</c>）で来ても受ける。</summary>
    public static bool IsEscapeMessage(string? body) => body is "key:Escape" or "\"key:Escape\"";
}
