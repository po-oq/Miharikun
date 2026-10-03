namespace Miharikun.Core.Documents;

public enum DocumentKind
{
    Markdown,
    Html,
}

/// <summary>索引の 1 ファイル。パスは対象フォルダからの相対で、区切りは常に「/」。中身は読まない。</summary>
public sealed record DocumentEntry(string RelativePath, DateTime ModifiedUtc, long Size)
{
    public string Name => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];

    /// <summary>所属フォルダの相対パス。対象フォルダ直下のファイルは空文字。</summary>
    public string Folder
    {
        get
        {
            var i = RelativePath.LastIndexOf('/');
            return i < 0 ? "" : RelativePath[..i];
        }
    }

    public DocumentKind Kind => TryGetKind(Name, out var kind) ? kind : DocumentKind.Markdown;

    /// <summary>対象の拡張子（.md / .html / .htm。大文字小文字は区別しない）か。</summary>
    public static bool TryGetKind(ReadOnlySpan<char> fileName, out DocumentKind kind)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocumentKind.Markdown;
            return true;
        }
        if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocumentKind.Html;
            return true;
        }
        kind = default;
        return false;
    }
}

/// <summary>走査が送る 1 回ぶん。Generation は再走査ごとの世代番号で、古い世代のバッチは捨てる。</summary>
public sealed record DocumentBatch(int Generation, IReadOnlyList<DocumentEntry> Entries, bool IsLast);
