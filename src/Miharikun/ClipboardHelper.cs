using System.Runtime.InteropServices;
using System.Windows;

namespace Miharikun;

/// <summary>クリップボードへのコピー。他のアプリが開いている間は失敗することがあるので、少し待ってやり直す。</summary>
public static class ClipboardHelper
{
    public static bool TrySetText(string text)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
        }
        AppLog.Write("クリップボードにコピーできなかった");
        return false;
    }
}
