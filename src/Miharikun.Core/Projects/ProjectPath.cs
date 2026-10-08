namespace Miharikun.Core.Projects;

/// <summary>
/// プロジェクト一致判定（要件 9章）：GetFullPath → 末尾区切り除去 → NFC → 大文字小文字無視の完全一致。
/// 論理パスと実パス（シンボリックリンクをたどったもの。mac。<see cref="RealPath"/>）のどちらかが合えば一致とする。
/// </summary>
public static class ProjectPath
{
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return ToNfc(Path.GetFullPath(StripLeadingSlashBeforeDrive(path)).TrimEnd('\\', '/'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// 日本語の濁点が分かれた形（NFD。mac のファイル名に出ることがある）を NFC にそろえる（計画 7.14）。
    /// Hook は InvariantGlobalization で正規化できないことがあるので、そのときは元の文字列を返す（Hook は git の場所に使うだけで害は無い）。
    /// </summary>
    public static string ToNfc(string text)
    {
        try
        {
            return text.IsNormalized(System.Text.NormalizationForm.FormC) ? text : text.Normalize(System.Text.NormalizationForm.FormC);
        }
        catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException)
        {
            return text;
        }
    }

    /// <summary>
    /// Cursor は workspace_roots を「/c:/zDev/repo/Miharikun」の形で渡してくる（実機で確認）。
    /// Windows のパスとして解釈できるよう、ドライブ文字の前の / を取り除く。
    /// </summary>
    public static string StripLeadingSlashBeforeDrive(string path) =>
        path.Length >= 3 && path[0] is '/' or '\\' && char.IsAsciiLetter(path[1]) && path[2] == ':'
            ? path[1..]
            : path;

    public static bool Matches(string projectPath, IEnumerable<string> workspaceRoots)
    {
        var project = Normalize(projectPath);
        if (project is null)
            return false;

        string? projectReal = null;
        foreach (var root in workspaceRoots)
        {
            var normalized = Normalize(root);
            if (normalized is null)
                continue;
            if (string.Equals(project, normalized, StringComparison.OrdinalIgnoreCase))
                return true;

            // 論理パスと実パスのどちらかが合えば一致（リンクが無いときは同じ文字列なので、ここで増える比較は 2 回だけ）。
            projectReal ??= Real(project);
            var rootReal = Real(normalized);
            if (string.Equals(projectReal, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(project, rootReal, StringComparison.OrdinalIgnoreCase)
                || string.Equals(projectReal, rootReal, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string Real(string normalized) => ToNfc(RealPath.Resolve(normalized));
}
