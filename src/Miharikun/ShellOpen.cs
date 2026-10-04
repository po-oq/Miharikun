using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Miharikun;

/// <summary>既定のアプリ・ブラウザ・エクスプローラーで開く。失敗してもアプリは止めない（ログに残す）。</summary>
internal static class ShellOpen
{
    /// <summary>URL・ファイル・フォルダを、関連付けられたアプリで開く。</summary>
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            AppLog.Write($"{target} を開けない: {ex.Message}");
        }
    }

    /// <summary>エクスプローラーでファイルを選んだ状態で開く。</summary>
    public static void RevealInExplorer(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { $"/select,{file}" } });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.Write($"{file} をエクスプローラーで開けない: {ex.Message}");
        }
    }
}
