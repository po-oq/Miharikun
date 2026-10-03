using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Miharikun.Behaviors;

/// <summary>インライン編集用の添付プロパティ。表示された瞬間にフォーカスして全選択し、フォーカスを失ったらコマンドを実行する。</summary>
public static class TextBoxBehaviors
{
    public static readonly DependencyProperty FocusWhenVisibleProperty = DependencyProperty.RegisterAttached(
        "FocusWhenVisible", typeof(bool), typeof(TextBoxBehaviors), new PropertyMetadata(false, OnFocusWhenVisibleChanged));

    public static readonly DependencyProperty LostFocusCommandProperty = DependencyProperty.RegisterAttached(
        "LostFocusCommand", typeof(ICommand), typeof(TextBoxBehaviors), new PropertyMetadata(null, OnLostFocusCommandChanged));

    public static bool GetFocusWhenVisible(DependencyObject o) => (bool)o.GetValue(FocusWhenVisibleProperty);
    public static void SetFocusWhenVisible(DependencyObject o, bool v) => o.SetValue(FocusWhenVisibleProperty, v);

    public static ICommand? GetLostFocusCommand(DependencyObject o) => (ICommand?)o.GetValue(LostFocusCommandProperty);
    public static void SetLostFocusCommand(DependencyObject o, ICommand? v) => o.SetValue(LostFocusCommandProperty, v);

    private static void OnFocusWhenVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        element.IsVisibleChanged -= OnIsVisibleChanged;
        if ((bool)e.NewValue)
            element.IsVisibleChanged += OnIsVisibleChanged;
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && (bool)e.NewValue)
        {
            box.Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private static void OnLostFocusCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        element.LostFocus -= OnLostFocus;
        if (e.NewValue is not null)
            element.LostFocus += OnLostFocus;
    }

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject d && GetLostFocusCommand(d) is { } command && command.CanExecute(null))
            command.Execute(null);
    }
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}