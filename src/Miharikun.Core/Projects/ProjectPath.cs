namespace Miharikun.Core.Projects;

/// <summary>プロジェクト一致判定（要件 9章）：GetFullPath → 末尾区切り除去 → 大文字小文字無視の完全一致。</summary>
public static class ProjectPath
{
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(StripLeadingSlashBeforeDrive(path)).TrimEnd('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
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

        foreach (var root in workspaceRoots)
        {
            if (string.Equals(project, Normalize(root), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}