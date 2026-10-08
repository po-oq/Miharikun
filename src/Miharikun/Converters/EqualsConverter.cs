using System.Globalization;
using Avalonia.Data.Converters;

namespace Miharikun.Converters;

/// <summary>
/// 値の文字列表現が <c>ConverterParameter</c> と同じなら true（null は空文字として比べる）。状態・ステータス・エージェントの出し分けを、
/// スタイルのクラス（<c>Classes.running="{Binding State, Converter={x:Static conv:Equals.To}, ConverterParameter=Running}"</c>）で行うために使う
/// （WPF の <c>DataTrigger</c> の代わり。色は <c>DynamicResource</c> なので、テーマの切り替えにも追従する。計画 7.9）。
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter To = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString() ?? "", parameter?.ToString() ?? "", StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
