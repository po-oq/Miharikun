using System.Text;
using Miharikun.Core.Storage;

namespace Miharikun.Core.Memo;

/// <summary>読み込んだメモ。ファイルが無いときは Text が空・LastWriteTime が null。</summary>
public sealed record MemoFile(string Text, DateTime? LastWriteTime);

/// <summary>
/// projects\{slug}-{hash8}.memo.md の読み書き（要件 12.10）。中身はただの Markdown（UTF-8・BOM なし）。
/// 読めないときは例外のまま返す（空として扱うと、保存で既存のメモを消すため）。
/// </summary>
public sealed class ProjectMemoStore(AppPaths paths, Action<string>? log = null)
{
    /// <summary>ファイルが無いときだけ空。あるのに読めないとき（IOException・UnauthorizedAccessException）は例外。</summary>
    public MemoFile Load(string projectPath)
    {
        var path = paths.ProjectMemoFile(projectPath);
        if (!File.Exists(path))
            return new MemoFile("", null);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            return new MemoFile(reader.ReadToEnd(), File.GetLastWriteTime(path));
        }
        catch (FileNotFoundException)
        {
            // 確認と読みの間に消えた
            return new MemoFile("", null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"メモを読めません: {ex.GetType().Name}");
            throw;
        }
    }

    /// <summary>文字をそのまま書く（改行コードは直さない）。書いた後の更新日時を返す。失敗は例外のまま。</summary>
    public DateTime Save(string projectPath, string text)
    {
        var path = paths.ProjectMemoFile(projectPath);
        try
        {
            AtomicFile.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"メモを書けません: {ex.GetType().Name}");
            throw;
        }
        return File.GetLastWriteTime(path);
    }
}
