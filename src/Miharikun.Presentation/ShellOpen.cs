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

    /// <summary>
    /// ファイラーでファイルを選んだ状態で開くコマンド。実行ファイルは<b>フルパス</b>（Windows は <c>&lt;windowsDir&gt;\explorer.exe</c>、mac は <c>/usr/bin/open</c>）。
    /// 名前だけだと、.NET がカレントフォルダのプログラムを先に起動しうる（診断 #2）。
    /// Windows の引数は <c>/select,"&lt;file&gt;"</c>（<paramref name="Raw"/> のとき、<c>Arguments[0]</c> を <c>ProcessStartInfo.Arguments</c> にそのまま入れる）。
    /// エクスプローラーは <c>/select,</c> の後ろを <c>,</c> で区切って読むので、名前に <c>,</c> を含むファイルのために引用符で囲む。
    /// </summary>
    public static (string FileName, string[] Arguments, bool Raw) BuildRevealCommand(bool isMac, string file, string windowsDir) =>
        isMac
            ? ("/usr/bin/open", ["-R", file], false)
            : (windowsDir.TrimEnd('\\') + "\\explorer.exe", [$"/select,\"{file}\""], true);

    /// <summary>ファイラーでファイルを選んだ状態で開く（Windows はエクスプローラー、mac は Finder）。</summary>
    public static void Reveal(string file)
    {
        try
        {
            var isMac = OperatingSystem.IsMacOS();
            var windowsDir = isMac ? "" : Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!isMac && string.IsNullOrEmpty(windowsDir))
            {
                AppLog.Write($"{file} をファイラーで開けない: Windows のフォルダが取れない");
                return;
            }
            var (fileName, arguments, raw) = BuildRevealCommand(isMac, file, windowsDir);
            var info = new ProcessStartInfo(fileName) { UseShellExecute = false };
            if (raw)
                info.Arguments = arguments[0];
            else
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
