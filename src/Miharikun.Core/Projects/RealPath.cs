using System.Collections.Concurrent;

namespace Miharikun.Core.Projects;

/// <summary>
/// シンボリックリンクをたどった実パス（計画 7.14）。mac は <c>/tmp</c> → <c>/private/tmp</c>、<c>/var</c> → <c>/private/var</c> など
/// リンクが多く、Claude Code の <c>cwd</c> や git のルートは実パスで来る一方、ターミナルの <c>$PWD</c> は論理パスになりうる。
/// 途中の部品も 1 つずつたどる（<c>Directory.ResolveLinkTarget</c> は最後の部品しか見ないため）。Windows は今のまま（何もしない）。
/// </summary>
public static class RealPath
{
    private const int MaxLinks = 40;

    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>実パス。リンクが無い・Windows・解決できない（壊れたリンクなど）ときは、渡したパスのまま。</summary>
    public static string Resolve(string fullPath)
    {
        if (OperatingSystem.IsWindows())
            return fullPath;
        return Cache.GetOrAdd(fullPath, static p => ResolveUncached(p));
    }

    /// <summary>キャッシュを捨てる（テスト用。リンクを作り直すとき）。</summary>
    public static void ClearCache() => Cache.Clear();

    private static string ResolveUncached(string fullPath)
    {
        try
        {
            var links = 0;
            return Walk(fullPath, ref links);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return fullPath;
        }
    }

    private static string Walk(string fullPath, ref int links)
    {
        var current = Path.GetPathRoot(fullPath) ?? "/";
        var rest = fullPath[current.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        var queue = new Queue<string>(rest);

        while (queue.Count > 0)
        {
            var part = queue.Dequeue();
            if (part == ".")
                continue;
            if (part == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            var next = Path.Combine(current, part);
            var target = new FileInfo(next).LinkTarget;
            if (target is null)
            {
                current = next;
                continue;
            }

            if (++links > MaxLinks)
                throw new IOException("リンクが多すぎる");

            // リンク先（相対ならリンクのあるフォルダから）を、残りの部品の前に差し込んで、改めて 1 つずつたどる。
            var resolved = Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(current, target));
            var root = Path.GetPathRoot(resolved) ?? "/";
            var pending = resolved[root.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).Concat(queue).ToList();
            current = root;
            queue = new Queue<string>(pending);
        }
        return current.Length > 1 ? current.TrimEnd('/') : current;
    }
}
