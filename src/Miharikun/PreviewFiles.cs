using System.IO;
using System.Security.Cryptography;
using System.Text;
using Miharikun.Core.Storage;

namespace Miharikun;

/// <summary>
/// md を HTML にした一時ファイル（要件 6章 preview\）。1 つの md につき 1 ファイル（名前はパスのハッシュ。同じ md は上書き）。
/// &lt;base&gt; を元のフォルダにして相対パスの画像・リンクを効かせるため、ファイルとして置く。
/// </summary>
internal static class PreviewFiles
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    /// <summary>HTML を書いて、そのパスを返す。複数起動の同時書き込みでも、前の内容が残るだけで壊れない（AtomicFile）。</summary>
    public static string Write(string previewDir, string markdownFullPath, string html)
    {
        var key = Path.GetFullPath(markdownFullPath).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        var path = Path.Combine(previewDir, hash + ".html");
        AtomicFile.WriteAllText(path, html);
        return path;
    }

    /// <summary>起動時の掃除：1 日より古いファイルだけ消す（複数起動中の別の Miharikun が使っているものを消さないため）。</summary>
    public static void CleanOld(string previewDir)
    {
        try
        {
            if (!Directory.Exists(previewDir))
                return;
            foreach (var file in Directory.EnumerateFiles(previewDir))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > MaxAge)
                        File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
