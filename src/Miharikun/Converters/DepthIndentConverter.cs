using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Miharikun.Converters;

/// <summary>目次の段の深さ → 左の余白（1 段 14px）。</summary>
public sealed class DepthIndentConverter : IValueConverter
{
    public const double StepPixels = 14;

    public static readonly DepthIndentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is int depth ? Math.Max(0, depth) * StepPixels : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
