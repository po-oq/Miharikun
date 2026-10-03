namespace Miharikun.Core.Documents;

public enum ChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed,

    /// <summary>FileSystemWatcher のバッファあふれ・監視の失敗。取りこぼしがあり得る。</summary>
    Error,
}

/// <summary>FileSystemWatcher の 1 イベント。パスは対象フォルダからの相対（区切りは「/」）。Renamed では RelativePath が新しい名前。</summary>
public sealed record FileChange(ChangeKind Kind, string RelativePath, string? OldRelativePath = null);

/// <summary>Upserts＝索引へ追加/更新するファイル、Removes＝索引から消す相対パス、Rescan＝全再走査が必要。</summary>
public sealed record DocumentChangeSet(IReadOnlyList<DocumentEntry> Upserts, IReadOnlyList<string> Removes, bool Rescan)
{
    public static readonly DocumentChangeSet Empty = new([], [], false);

    public bool IsEmpty => Upserts.Count == 0 && Removes.Count == 0 && !Rescan;
}

/// <summary>
/// 「イベント → ファイルの差分 or 全再走査」の振り分け（要件 12.7）。
/// ファイルの差分だけ自分で反映し、フォルダの作成・削除・名前変更とバッファあふれは全再走査にする
/// （フォルダ単位の差分は作らない。FileSystemWatcher はフォルダの名前変更・削除でフォルダ 1 件しか知らせないため）。
/// </summary>
public static class DocumentChangeClassifier
{
    /// <summary>これを超える数のイベントは、個別に見ずに全再走査にする（git checkout 等）。</summary>
    public const int MaxChanges = 500;

    /// <summary>実際のディスクを見て振り分ける。索引に触るので、呼び出しは索引の所有スレッド（UI）で行う。</summary>
    public static DocumentChangeSet Classify(IReadOnlyList<FileChange> changes, string root, GitIgnoreMatcher matcher,
        Func<string, bool> indexHasEntriesUnder) =>
        Classify(changes, matcher,
            directoryExists: rel => Directory.Exists(Full(root, rel)),
            statFile: rel => Stat(root, rel),
            indexHasEntriesUnder);

    public static DocumentChangeSet Classify(IReadOnlyList<FileChange> changes, GitIgnoreMatcher matcher,
        Func<string, bool> directoryExists, Func<string, DocumentEntry?> statFile, Func<string, bool> indexHasEntriesUnder)
    {
        if (changes.Count > MaxChanges)
            return new DocumentChangeSet([], [], true);

        var state = new Dictionary<string, DocumentEntry?>(StringComparer.OrdinalIgnoreCase);   // null＝消す
        var rescan = false;

        void Present(string path, bool created)
        {
            if (matcher.IsPathExcluded(path, isDirectory: false))
                return;
            if (DocumentEntry.TryGetKind(FileName(path), out _))
                state[path] = statFile(path);                                    // 消えていたら null（＝消す）
            else if (created && directoryExists(path) && !matcher.IsPathExcluded(path, isDirectory: true))
                rescan = true;
        }

        void Gone(string path)
        {
            if (DocumentEntry.TryGetKind(FileName(path), out _))
                state[path] = null;
            else if (indexHasEntriesUnder(path))
                rescan = true;                                                   // 消えたのはフォルダ（もう Directory.Exists では判定できない）
        }

        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case ChangeKind.Error:
                    rescan = true;
                    break;
                case ChangeKind.Created:
                    Present(change.RelativePath, created: true);
                    break;
                case ChangeKind.Changed:
                    Present(change.RelativePath, created: false);
                    break;
                case ChangeKind.Deleted:
                    Gone(change.RelativePath);
                    break;
                case ChangeKind.Renamed:
                    if (change.OldRelativePath is not null)
                        Gone(change.OldRelativePath);
                    Present(change.RelativePath, created: true);
                    break;
            }
        }

        var upserts = state.Values.OfType<DocumentEntry>().ToList();
        var removes = state.Where(p => p.Value is null).Select(p => p.Key).ToList();
        return new DocumentChangeSet(upserts, removes, rescan);
    }

    private static string FileName(string relativePath) => relativePath[(relativePath.LastIndexOf('/') + 1)..];

    private static string Full(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static DocumentEntry? Stat(string root, string relativePath)
    {
        try
        {
            var info = new FileInfo(Full(root, relativePath));
            return info.Exists ? new DocumentEntry(relativePath, info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
