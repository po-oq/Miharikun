using System.Text;

namespace Miharikun.Core.Storage;

/// <summary>一時ファイルに書いてから置き換える（途中で止まっても元のファイルが壊れない）。</summary>
public static class AtomicFile
{
    private const int ReplaceRetryCount = 20;
    private const int ReplaceRetryDelayMs = 10;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, text, Utf8NoBom);

            // 複数起動が同時に置き換えると Windows が拒否することがある。後勝ちでよいので、少し待ってやり直す。
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                        File.Replace(temp, path, null);
                    else
                        File.Move(temp, path);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < ReplaceRetryCount)
                {
                    Thread.Sleep(ReplaceRetryDelayMs);
                }
            }
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }

        DeleteReplaceLeftovers(path);
    }

    /// <summary>
    /// 置き換えが競合で失敗すると Windows は「名前~RFxxxx.TMP」を残すことがある。本体は無事なので、気づいた時に消す。
    /// </summary>
    private static void DeleteReplaceLeftovers(string path)
    {
        try
        {
            foreach (var leftover in Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileName(path) + "~RF*.TMP"))
            {
                try { File.Delete(leftover); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
    }
}