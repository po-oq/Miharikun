using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Miharikun.Behaviors;

/// <summary>
/// インライン編集用の添付プロパティ（WPF 版と同じ。計画 7.9）。表示された（その入力欄自身の IsVisible が true になった）瞬間にフォーカスして全選択し、
/// フォーカスを失ったらコマンドを実行する（名前の編集：Enter・フォーカスアウトで確定、Esc で取り消し）。
/// </summary>
public static class TextBoxBehaviors
{
    public static readonly AttachedProperty<bool> FocusWhenVisibleProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, bool>("FocusWhenVisible");

    public static readonly AttachedProperty<ICommand?> LostFocusCommandProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, ICommand?>("LostFocusCommand");

    static TextBoxBehaviors()
    {
        FocusWhenVisibleProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            box.PropertyChanged -= OnBoxPropertyChanged;
            if (e.NewValue is true)
                box.PropertyChanged += OnBoxPropertyChanged;
        });
        LostFocusCommandProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            box.LostFocus -= OnLostFocus;
            if (e.NewValue is not null)
                box.LostFocus += OnLostFocus;
        });
    }

    public static bool GetFocusWhenVisible(TextBox box) => box.GetValue(FocusWhenVisibleProperty);
    public static void SetFocusWhenVisible(TextBox box, bool value) => box.SetValue(FocusWhenVisibleProperty, value);

    public static ICommand? GetLostFocusCommand(TextBox box) => box.GetValue(LostFocusCommandProperty);
    public static void SetLostFocusCommand(TextBox box, ICommand? value) => box.SetValue(LostFocusCommandProperty, value);

    private static void OnBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && e.Property == Visual.IsVisibleProperty && e.NewValue is true)
        {
            // 表示された直後は、まだ画面に載っていないことがあるので、少し遅らせる
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    private static void OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox box && GetLostFocusCommand(box) is { } command && command.CanExecute(null))
            command.Execute(null);
    }
}
