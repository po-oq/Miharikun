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
            return Path.GetFullPath(path).TrimEnd('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

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