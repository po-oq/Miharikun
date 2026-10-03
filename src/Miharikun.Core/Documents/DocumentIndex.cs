namespace Miharikun.Core.Documents;

/// <summary>
/// メモリ上の索引（相対パス・大文字小文字無視をキーにした辞書）。スレッド非安全：更新は 1 か所（UI スレッド）からだけ行う。
/// 再走査ごとに <see cref="Generation"/> を進め、古い世代のバッチは捨てる。
/// </summary>
public sealed class DocumentIndex
{
    private readonly Dictionary<string, DocumentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public int Generation { get; private set; }

    public int Count => _entries.Count;

    public IEnumerable<DocumentEntry> Entries => _entries.Values;

    public DocumentEntry? TryGet(string relativePath) =>
        _entries.TryGetValue(relativePath, out var entry) ? entry : null;

    /// <summary>そのフォルダの下に、索引のファイルが 1 つでもあるか（消えたのがフォルダかの判定に使う）。</summary>
    public bool HasEntriesUnder(string folder)
    {
        var prefix = folder.TrimEnd('/') + "/";
        foreach (var path in _entries.Keys)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public void AddOrUpdate(DocumentEntry entry) => _entries[entry.RelativePath] = entry;

    public bool Remove(string relativePath) => _entries.Remove(relativePath);

    /// <summary>再走査の開始：世代を進めて空にする。戻り値は新しい世代（走査に渡す）。</summary>
    public int Reset()
    {
        _entries.Clear();
        return ++Generation;
    }

    /// <summary>走査のバッチを取り込む。古い世代なら捨てて false。</summary>
    public bool Apply(DocumentBatch batch)
    {
        if (batch.Generation != Generation)
            return false;
        foreach (var entry in batch.Entries)
            AddOrUpdate(entry);
        return true;
    }
}
