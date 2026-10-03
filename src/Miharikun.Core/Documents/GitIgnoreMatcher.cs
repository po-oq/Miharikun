namespace Miharikun.Core.Documents;

/// <summary>
/// 除外パターン（gitignore 形式。要件 12.7）の判定。対応する書式：# コメント・空行・! 再包含・末尾 / ＝フォルダのみ・
/// 先頭/途中の / ＝ルート基準・* ? **。大文字小文字は区別しない。後ろの行が優先。
/// </summary>
public sealed class GitIgnoreMatcher
{
    private static readonly char[] Separators = ['/', '\\'];

    private readonly GitIgnorePattern[] _patterns;

    private GitIgnoreMatcher(GitIgnorePattern[] patterns) => _patterns = patterns;

    public static GitIgnoreMatcher Parse(string text)
    {
        var patterns = new List<GitIgnorePattern>();
        foreach (var line in text.Split('\n'))
        {
            var pattern = GitIgnorePattern.Parse(line);
            if (pattern is not null)
                patterns.Add(pattern);
        }
        return new GitIgnoreMatcher([.. patterns]);
    }

    /// <summary>このパスそのものが除外に当たるか（親は見ない）。走査の枝刈り（フォルダに入る前の判定）に使う。</summary>
    public bool IsIgnored(string relativePath, bool isDirectory) =>
        IsIgnored(Split(relativePath), isDirectory);

    /// <summary>
    /// 親フォルダを順にさかのぼって判定する。1 つでも除外されていれば除外。
    /// git と同じく、除外したフォルダの中身を ! で戻すことはできない。Watcher の単発判定に使う。
    /// </summary>
    public bool IsPathExcluded(string relativePath, bool isDirectory)
    {
        var parts = Split(relativePath);
        for (var i = 1; i < parts.Length; i++)
            if (IsIgnored(parts[..i], isDirectory: true))
                return true;
        return IsIgnored(parts, isDirectory);
    }

    private bool IsIgnored(string[] parts, bool isDirectory)
    {
        var ignored = false;
        foreach (var pattern in _patterns)
        {
            if (pattern.DirectoryOnly && !isDirectory)
                continue;
            if (pattern.Matches(parts))
                ignored = !pattern.Negated;
        }
        return ignored;
    }

    private static string[] Split(string path) =>
        path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
}
