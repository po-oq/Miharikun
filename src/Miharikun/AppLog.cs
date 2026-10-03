using System.IO;
using Miharikun.Core.Storage;

namespace Miharikun;

/// <summary>%LOCALAPPDATA%\Miharikun\logs\app.log への簡易ログ。書けなくてもアプリは止めない。</summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(AppPaths paths) => _path = Path.Combine(paths.Root, "logs", "app.log");

    public static void Write(string message)
    {
        if (_path is null)
            return;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, $"{DateTimeOffset.Now:o} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}