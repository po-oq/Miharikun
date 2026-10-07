using System.Globalization;
using Avalonia.Data.Converters;

namespace Miharikun.Converters;

/// <summary>真偽で文字を替える。<c>ConverterParameter</c> は「偽のとき|真のとき」（例：<c>⧉ 全部コピー|✓ コピーしました</c>）。</summary>
public sealed class BoolTextConverter : IValueConverter
{
    public static readonly BoolTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter?.ToString() ?? "|").Split('|', 2);
        return value is true ? parts[^1] : parts[0];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
