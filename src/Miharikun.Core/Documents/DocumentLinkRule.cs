namespace Miharikun.Core.Documents;

/// <summary>
/// プレビューのリンク（ローカルファイル）を、ドキュメントタブで開けるかの判定（要件 12.10・実装計画 7.3）。
/// 対象フォルダの中の .md/.html/.htm で、除外に当たらないものだけ。ファイルの有無は見ない（呼び手が見る）。
/// </summary>
public static class DocumentLinkRule
{
    public static bool TryGetInAppPath(string root, string fullPath, GitIgnoreMatcher matcher, out string relativePath)
    {
        relativePath = "";

        var rel = Projects.ProjectPath.ToNfc(Path.GetRelativePath(root, fullPath).Replace('\\', '/'));
        if (Path.IsPathRooted(rel) || rel == "." || IsOutside(rel))
            return false;
        if (!DocumentEntry.TryGetKind(rel, out _))
            return false;
        if (matcher.IsPathExcluded(rel, isDirectory: false))
            return false;

        relativePath = rel;
        return true;
    }

    /// <summary>最初の区切りまでが「..」か。「..memo.md」のような名前は外ではない。</summary>
    private static bool IsOutside(string rel) =>
        rel == ".." || rel.StartsWith("../", StringComparison.Ordinal);
}
