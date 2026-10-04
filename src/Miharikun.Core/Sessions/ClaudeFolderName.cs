using System.Text;
using Miharikun.Core.Projects;

namespace Miharikun.Core.Sessions;

/// <summary>
/// Claude Code が <c>~\.claude\projects\</c> に作るフォルダの名前の規則（要件 9.1）。
/// パスの英数字以外を、1 文字ずつ「-」にする：<c>C:\zDev\repo\Miharikun</c> → <c>C--zDev-repo-Miharikun</c>。
/// Cursor の slug と違い、連なりを 1 つにまとめない。日本語やサロゲートペアも 1 文字（UTF-16 の 1 つ）ごとに「-」。
/// 規則は公開された仕様ではないので、照合は <c>cwd</c> で行い、この名前は読むフォルダを絞るためだけに使う。
/// </summary>
public static class ClaudeFolderName
{
    /// <summary>作業ツリーのフォルダは、本体の名前に <c>--claude-worktrees-&lt;名前&gt;</c> が続く。</summary>
    public const string WorktreeSuffix = "--claude-worktrees-";

    /// <summary>プロジェクトのフォルダの Claude 式の名前。パスとして使えなければ null。</summary>
    public static string? For(string projectPath)
    {
        var normalized = ProjectPath.Normalize(projectPath);
        if (normalized is null)
            return null;

        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        return sb.ToString();
    }

    /// <summary>フォルダ名が、同じ名前、または作業ツリーの形（<c>--claude-worktrees-</c> が続く）か。大文字小文字は無視する。</summary>
    public static bool IsCandidate(string folderName, string projectPath)
    {
        var wanted = For(projectPath);
        if (wanted is null)
            return false;

        return folderName.Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
               folderName.StartsWith(wanted + WorktreeSuffix, StringComparison.OrdinalIgnoreCase);
    }
}
