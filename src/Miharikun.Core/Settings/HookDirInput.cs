using Miharikun.Core.Install;

namespace Miharikun.Core.Settings;

/// <summary>
/// 設定画面の「Hook exe の置き場所」の入力の検査（要件 12.9・計画 7.5）。<c>AppSettingsStore.LoadHookDir</c> と同じ規則
/// （完全なパス）に、「フォルダが実在する」を足したもの。アプリはフォルダを作らない。
/// </summary>
public static class HookDirInput
{
    /// <summary>
    /// 保存できるなら null。できないときは、画面に出す理由。既定の置き場所（<paramref name="defaultDir"/>）は、まだ無くてもよい
    /// （導入のときにアプリが作る。設定した場所は作らない）。
    /// </summary>
    public static string? Validate(string? text, string? defaultDir = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "フォルダを指定してください";
        return TryNormalize(text, out var dir)
            ? (Directory.Exists(dir) || IsDefault(dir, defaultDir) ? null : "フォルダが見つかりません")
            : HookWording.NotAbsoluteMessage;
    }

    private static bool IsDefault(string dir, string? defaultDir) =>
        defaultDir is not null && TryNormalize(defaultDir, out var d) && d.Equals(dir, StringComparison.OrdinalIgnoreCase);

    /// <summary>完全なパスなら、正規化（<c>GetFullPath</c> ＋末尾の区切りを落とす）して true。</summary>
    public static bool TryNormalize(string? text, out string dir)
    {
        dir = "";
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var raw = text.Trim();
        try
        {
            if (raw.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(raw))
                return false;
            dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
