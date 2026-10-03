namespace Miharikun.Core.Documents;

/// <summary>一覧の絞り込みと並び（要件 12.7）。</summary>
public static class DocumentFilter
{
    /// <summary>
    /// <paramref name="folder"/>：null＝「すべて」、空文字＝対象フォルダ直下、それ以外＝そのフォルダの直下だけ。
    /// <paramref name="query"/>：ファイル名・フォルダ名の部分一致（空白区切りは AND、大文字小文字無視）。並びは更新日の降順。
    /// </summary>
    public static IReadOnlyList<DocumentEntry> Select(IEnumerable<DocumentEntry> entries, string? folder, string query)
    {
        var terms = Terms(query);
        return entries
            .Where(e => folder is null || e.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))
            .Where(e => Matches(e, terms))
            .OrderByDescending(e => e.ModifiedUtc)
            .ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>絞り込み語に当たるか。ツリーの件数をヒット分に変えるときにも使う。</summary>
    public static bool Matches(DocumentEntry entry, string query) => Matches(entry, Terms(query));

    private static bool Matches(DocumentEntry entry, string[] terms) =>
        terms.All(t => entry.RelativePath.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static string[] Terms(string query) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
