namespace Miharikun.Core.Sessions;

/// <summary>長い本文を、先頭の数行・数百文字だけに切り詰める（タイムラインの折りたたみ表示用）。</summary>
public static class TextPreview
{
    public const int DefaultLines = 8;
    public const int DefaultLength = 500;

    /// <returns>表示する本文と、切り詰めたか。切り詰めたときは末尾に「…」を付ける。</returns>
    public static (string Text, bool Truncated) Make(string text, int maxLines = DefaultLines, int maxLength = DefaultLength)
    {
        var shown = text;
        var cut = false;

        var lines = shown.Split('\n');
        if (lines.Length > maxLines)
        {
            shown = string.Join('\n', lines.Take(maxLines));
            cut = true;
        }
        if (shown.Length > maxLength)
        {
            // サロゲートペアを割らない
            var end = char.IsHighSurrogate(shown[maxLength - 1]) ? maxLength - 1 : maxLength;
            shown = shown[..end];
            cut = true;
        }
        return cut ? (shown.TrimEnd() + "…", true) : (text, false);
    }
}