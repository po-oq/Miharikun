using System.Windows;
using System.Windows.Controls;
using Miharikun.Core.Settings;
using Wpf.Ui.Controls;

namespace Miharikun.Views;

/// <summary>アプリ全体の設定のダイアログ（要件 12.9）。今は「停止とみなす時間（分）」だけ。数字以外・負の数は保存できない。</summary>
public partial class AppSettingsDialog : FluentWindow
{
    public AppSettingsDialog(int runningTimeoutMinutes)
    {
        InitializeComponent();
        MinutesBox.Text = runningTimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MinutesBox.SelectAll();
        MinutesBox.Focus();
    }

    /// <summary>「保存」で閉じたときの値（分）。</summary>
    public int RunningTimeoutMinutes { get; private set; }

    private void OnMinutesChanged(object sender, TextChangedEventArgs e)
    {
        // 初期化の途中（SaveButton がまだ無い）でも呼ばれる。
        if (SaveButton is null)
            return;
        var error = RunningTimeoutInput.Validate(MinutesBox.Text);
        ErrorText.Text = error ?? "";
        SaveButton.IsEnabled = error is null;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!RunningTimeoutInput.TryParse(MinutesBox.Text, out var minutes))
            return;
        RunningTimeoutMinutes = minutes;
        DialogResult = true;
    }
}
