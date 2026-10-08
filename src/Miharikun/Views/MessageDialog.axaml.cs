using Avalonia.Controls;
using Avalonia.Interactivity;
using Miharikun.Services;

namespace Miharikun.Views;

/// <summary>確認（はい／いいえ）とお知らせ（OK）の小さなダイアログ（計画 7.2）。</summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private MessageDialog(string title, string message, bool yesNo) : this()
    {
        Title = title;
        MessageText.Text = message;
        if (yesNo)
        {
            NoButton.Focus();
        }
        else
        {
            YesButton.Content = "OK";
            NoButton.IsVisible = false;
            YesButton.IsCancel = true;
        }
    }

    private void OnYes(object? sender, RoutedEventArgs e) => Close(true);

    private void OnNo(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>確認。はい → true、いいえ・閉じた → false。</summary>
    public static async Task<bool> ConfirmAsync(Window? owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message, yesNo: true);
        return owner is null ? await ShowWithoutOwner(dialog) : await dialog.ShowDialog<bool>(owner);
    }

    public static async Task ShowAsync(Window? owner, string title, string message, MessageKind kind)
    {
        var dialog = new MessageDialog(kind == MessageKind.Warning ? "⚠ " + title : title, message, yesNo: false);
        if (owner is null)
            await ShowWithoutOwner(dialog);
        else
            await dialog.ShowDialog<bool>(owner);
    }

    private static async Task<bool> ShowWithoutOwner(MessageDialog dialog)
    {
        var done = new TaskCompletionSource<bool>();
        dialog.Closed += (_, _) => done.TrySetResult(true);
        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.Show();
        await done.Task;
        return false;
    }
}
