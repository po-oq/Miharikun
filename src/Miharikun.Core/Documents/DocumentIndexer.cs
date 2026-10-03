using System.IO.Enumeration;

namespace Miharikun.Core.Documents;

/// <summary>
/// 対象フォルダ配下の .md / .html / .htm を列挙する（要件 12.7）。除外パターンに当たるフォルダには入らない（枝刈り）。
/// ファイルの中身は読まない。ジャンクション等（ReparsePoint）のフォルダにも入らない（ループ・別ドライブへの迷い込み防止）。
/// </summary>
public static class DocumentIndexer
{
    public const int DefaultBatchSize = 200;

    public static IEnumerable<DocumentEntry> Enumerate(string root, GitIgnoreMatcher matcher, CancellationToken ct)
    {
        if (!Directory.Exists(root))
            return [];
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,                                 // .cursor などの隠しフォルダも対象（除外は設定で決める）
            ReturnSpecialDirectories = false,
        };

        var enumerable = new FileSystemEnumerable<DocumentEntry>(root,
            (ref FileSystemEntry e) => new DocumentEntry(RelativePath(root, ref e), e.LastWriteTimeUtc.UtcDateTime, e.Length), options)
        {
            ShouldRecursePredicate = (ref FileSystemEntry e) =>
                e.IsDirectory
                && (e.Attributes & FileAttributes.ReparsePoint) == 0
                && !matcher.IsIgnored(RelativePath(root, ref e), isDirectory: true),
            ShouldIncludePredicate = (ref FileSystemEntry e) =>
                !e.IsDirectory
                && DocumentEntry.TryGetKind(e.FileName, out _)
                && !matcher.IsIgnored(RelativePath(root, ref e), isDirectory: false),
        };

        return enumerable.TakeWhile(_ => !ct.IsCancellationRequested);
    }

    /// <summary>
    /// 背景で走査し、見つけた分から <paramref name="batchSize"/> 件ずつ <paramref name="onBatch"/> に渡す（呼び出しはスレッドプール上）。
    /// 最後に <c>IsLast = true</c> のバッチを送る（0 件でも）。キャンセルされたら最後のバッチは送らない。
    /// </summary>
    public static Task ScanAsync(string root, GitIgnoreMatcher matcher, int generation, Action<DocumentBatch> onBatch,
        CancellationToken ct, int batchSize = DefaultBatchSize) =>
        Task.Run(() =>
        {
            var buffer = new List<DocumentEntry>(batchSize);
            foreach (var entry in Enumerate(root, matcher, ct))
            {
                buffer.Add(entry);
                if (buffer.Count >= batchSize)
                {
                    onBatch(new DocumentBatch(generation, buffer, IsLast: false));
                    buffer = new List<DocumentEntry>(batchSize);
                }
            }

            if (ct.IsCancellationRequested)
                return;
            onBatch(new DocumentBatch(generation, buffer, IsLast: true));
        }, CancellationToken.None);

    /// <summary>対象フォルダからの相対パス（区切りは「/」）。</summary>
    private static string RelativePath(string root, ref FileSystemEntry e)
    {
        // root は末尾の区切りなしに揃えてある。e.Directory は root 配下のフォルダの絶対パス。
        var full = e.Directory;
        var relDir = full.Length > root.Length ? full[(root.Length + 1)..] : [];
        var name = e.FileName;
        var sb = new System.Text.StringBuilder(relDir.Length + name.Length + 1);
        foreach (var c in relDir)
            sb.Append(c == '\\' ? '/' : c);
        if (sb.Length > 0)
            sb.Append('/');
        sb.Append(name);
        return sb.ToString();
    }
}
