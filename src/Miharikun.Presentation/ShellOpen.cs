using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Miharikun;

/// <summary>既定のアプリ・ブラウザ・エクスプローラーで開く。失敗してもアプリは止めない（ログに残す）。</summary>
public static class ShellOpen
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

    /// <summary>「フォルダで開く」ボタンの文字（要件 12.12）。Windows は「フォルダで開く」、mac は「Finder で表示」。</summary>
    public static string RevealButtonText => OperatingSystem.IsMacOS() ? "Finder で表示" : "フォルダで開く";

    /// <summary>ファイラーでファイルを選んだ状態で開くコマンド（Windows は <c>explorer.exe /select,</c>、mac は <c>open -R</c>）。</summary>
    public static (string FileName, string[] Arguments) BuildRevealCommand(bool isMac, string file) =>
        isMac ? ("open", ["-R", file]) : ("explorer.exe", [$"/select,{file}"]);

    /// <summary>ファイラーでファイルを選んだ状態で開く（Windows はエクスプローラー、mac は Finder）。</summary>
    public static void Reveal(string file)
    {
        try
        {
            var (fileName, arguments) = BuildRevealCommand(OperatingSystem.IsMacOS(), file);
            var info = new ProcessStartInfo(fileName) { UseShellExecute = false };
            foreach (var a in arguments)
                info.ArgumentList.Add(a);
            Process.Start(info);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.Write($"{file} をファイラーで開けない: {ex.Message}");
        }
    }
}
