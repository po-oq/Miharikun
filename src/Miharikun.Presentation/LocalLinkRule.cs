namespace Miharikun;

/// <summary>プレビューのローカルのリンクを押したときの扱い。</summary>
public enum LocalLinkAction
{
    /// <summary>何もしない（使えない形のパス・ネットワーク・無いファイル）。</summary>
    Ignore,

    /// <summary>既定のアプリで開く（開いてよい種類だけ）。</summary>
    OpenWithDefaultApp,

    /// <summary>開かず、エクスプローラー／Finder で選ばれた状態にする（フォルダ・開いてよい種類でないもの）。</summary>
    Reveal,
}

/// <summary>
/// 規則が触るファイルシステムの 3 つの操作。<see cref="ReadLinkOnce"/> はリンクの中身を 1 段だけ読む（リンクでなければ null。返すのはフルパス）。
/// 先を開かないので、先がネットワークでも接続は起きない。
/// </summary>
public sealed record LinkProbe(Func<string, bool> FileExists, Func<string, bool> DirectoryExists, Func<string, string?> ReadLinkOnce)
{
    public static LinkProbe Real { get; } = new(File.Exists, Directory.Exists, ReadLinkOnceReal);

    private static string? ReadLinkOnceReal(string path)
    {
        var target = new FileInfo(path).LinkTarget ?? new DirectoryInfo(path).LinkTarget;
        if (target is null)
            return null;
        return Path.GetFullPath(target, Path.GetDirectoryName(path) ?? "/");
    }
}

/// <summary>
/// プレビューのリンクの規則（要件 12.7・実装計画 9.3）。md/html は利用者のプロジェクトの外から来ることがあるので、
/// 押しただけでプログラムが起動したり、ネットワークに接続したりしないように決める。文字列で判定してからファイルシステムに触る。
/// </summary>
public static class LocalLinkRule
{
    private const int MaxLinkDepth = 8;

    // 開いてよい種類（小文字）。Office の文書・.csv・.json などは入れない（マクロや別のアプリの動きを持ち込まないため）。
    // Windows の短い名前（X~1.HTM）は拡張子の先頭 3 文字で始まる危ない拡張子が無いので抜け道にならない。足すときはその点を確かめる。
    private static readonly HashSet<string> OpenableExtensions = new(StringComparer.Ordinal)
    {
        ".md", ".html", ".htm", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".pdf", ".txt",
    };

    /// <summary>使ってよい形のパスだけを通す（文字列だけで判定。許す形を決める）。Windows はドライブ文字で始まるもの、mac は <c>/</c> で始まり <c>//</c>・<c>/net/</c> でないもの。</summary>
    public static bool IsUsableLocalPath(string path, bool isWindows)
    {
        if (isWindows)
            return path.Length >= 3 && path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' && path[1] == ':' && path[2] is '\\' or '/';
        return path.Length >= 1 && path[0] == '/'
            && !path.StartsWith("//", StringComparison.Ordinal)
            && !path.StartsWith("/net/", StringComparison.Ordinal) && path != "/net";
    }

    public static LocalLinkAction Decide(string fullPath, bool isWindows, LinkProbe probe)
    {
        try
        {
            return DecideCore(fullPath, isWindows, probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LocalLinkAction.Reveal;   // 確かめられないものは、起動しない側へ
        }
    }

    private static LocalLinkAction DecideCore(string fullPath, bool isWindows, LinkProbe probe)
    {
        if (!IsAcceptable(fullPath, isWindows))
            return LocalLinkAction.Ignore;

        // リンクを 1 段ずつ読む。1 段進むごとに、先のパスを文字列で判定する（先を開くのは、ネットワークでないと分かった後だけ）。
        var current = fullPath;
        string? target = null;
        for (var depth = 0; probe.ReadLinkOnce(current) is { } next; depth++)
        {
            if (depth >= MaxLinkDepth)
                return LocalLinkAction.Reveal;   // 輪になったリンク・長すぎる連なり
            if (!IsAcceptable(next, isWindows))
                return LocalLinkAction.Ignore;
            current = target = next;
        }

        if (probe.DirectoryExists(current))
            return LocalLinkAction.Reveal;       // mac の .app もフォルダ
        if (!probe.FileExists(current))
            return LocalLinkAction.Ignore;

        return OpenableExtensions.Contains(ExtensionOf(fullPath)) && (target is null || OpenableExtensions.Contains(ExtensionOf(target)))
            ? LocalLinkAction.OpenWithDefaultApp
            : LocalLinkAction.Reveal;
    }

    // 使える形で、Windows ではドライブの ':' のほかに ':' を含まない（代替データストリーム a.txt:x.exe）。
    private static bool IsAcceptable(string path, bool isWindows) =>
        IsUsableLocalPath(path, isWindows) && !(isWindows && path.IndexOf(':', 2) >= 0);

    // 最後の区切りより後ろの、最後の '.' から（小文字）。'.' で終わる名前（a.txt.）は空 → 開いてよい種類でない。
    private static string ExtensionOf(string path)
    {
        var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        var dot = name.LastIndexOf('.');
        return dot < 0 || dot == name.Length - 1 ? "" : name[dot..].ToLowerInvariant();
    }
}
