using System.Globalization;

namespace Miharikun.Core.Settings;

/// <summary>設定画面の「停止とみなす時間（分）」の入力の検査（要件 12.9）。数字以外・負の数は保存できない。</summary>
public static class RunningTimeoutInput
{
    /// <summary>1 週間。これより大きい値は、入力の間違いとみなす。</summary>
    public const int MaxMinutes = 7 * 24 * 60;

    /// <summary>半角の数字だけ（前後の空白は無視）。0 は「無効」の意味で許す。空・負の数・小数・数字以外・大きすぎる値は false。</summary>
    public static bool TryParse(string? text, out int minutes)
    {
        minutes = 0;
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t) || !t.All(char.IsAsciiDigit))
            return false;
        if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value <= MaxMinutes)
        {
            minutes = value;
            return true;
        }
        return false;
    }

    /// <summary>入力欄の下に出す説明。値が正しいなら null。</summary>
    public static string? Validate(string? text) =>
        TryParse(text, out _) ? null : $"0 から {MaxMinutes} までの整数で入力してください（0 は無効）。";
}
