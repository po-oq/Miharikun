using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Miharikun.Docs;

/// <summary>
/// md のページが読む、同梱の mermaid・highlight.js（要件 12.7・実装計画 9.5）。CDN をやめ、埋め込みのファイルを
/// <c>&lt;libRoot&gt;/&lt;Version&gt;/</c> に書き出して、md のページから file URL で読む（ネットが無くても図と色付けが出る）。
/// 同梱の版：mermaid 11.17.2（MIT）、highlight.js 11.12.0（BSD-3-Clause）。ライセンスは <c>scripts/dist/THIRD-PARTY-NOTICES.txt</c>。
/// </summary>
public static class MarkdownAssets
{
    public const string MermaidFile = "mermaid.min.js";
    public const string HighlightFile = "highlight.min.js";
    public const string HighlightLightCss = "hljs-github.min.css";
    public const string HighlightDarkCss = "hljs-github-dark.min.css";

    /// <summary>書き出すファイルの名前。</summary>
    public static IReadOnlyList<string> Names { get; } = [MermaidFile, HighlightFile, HighlightLightCss, HighlightDarkCss];

    private static readonly Lazy<string> VersionValue = new(ComputeVersion);

    /// <summary>
    /// 埋め込みの中身から作る版の名前（<c>v-</c> ＋ SHA-256 の先頭 12 文字）。手で変える決まりは置かない
    /// （替え忘れで、長さが同じ差し替えが書き直されない・新旧の起動が書き直し合う、を防ぐ）。
    /// <c>mermaid</c>・<c>highlight</c> の文字は入れない（そのページが読まないときに、その文字が HTML に出ないことをテストで確かめているため）。
    /// </summary>
    public static string Version => VersionValue.Value;

    /// <summary>埋め込みのファイルの中身。</summary>
    public static byte[] Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Miharikun.Docs.vendor.{name}")
            ?? throw new InvalidOperationException($"埋め込みリソース {name} が無い");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// 全ファイルが <c>&lt;libRoot&gt;/&lt;Version&gt;/</c> にあるようにして、そのフォルダを返す。無い、または長さが違うものだけ <paramref name="write"/> で書く
    /// （中身は版で決まるので、長さは途中で壊れたファイルの保険）。書けなくても投げない（図と色付けが出ないだけ）。毎回の描画の前に呼んでよい。
    /// </summary>
    public static string Ensure(string libRoot, Action<string, byte[]> write, Action<string>? log = null)
    {
        var dir = Path.Combine(libRoot, Version);
        foreach (var name in Names)
        {
            var path = Path.Combine(dir, name);
            try
            {
                var bytes = Read(name);
                if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
                    write(path, bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"{name} を書き出せない（mermaid・色付けが出ない）: {ex.Message}");
            }
        }
        return dir;
    }

    private static string ComputeVersion()
    {
        using var sha = SHA256.Create();
        foreach (var name in Names.OrderBy(n => n, StringComparer.Ordinal))
        {
            var bytes = Read(name);
            var head = Encoding.UTF8.GetBytes(name + "\n" + bytes.Length + "\n");
            sha.TransformBlock(head, 0, head.Length, null, 0);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return "v-" + Convert.ToHexString(sha.Hash!).ToLowerInvariant()[..12];
    }
}
