using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Miharikun.Core.Settings;
using Wpf.Ui.Controls;

namespace Miharikun.Views;

/// <summary>
/// アプリ全体の設定のダイアログ（要件 12.9）：「停止とみなす時間（分）」と「Cursor の Hook exe の置き場所」（Issue #17）。
/// 数字以外・負の数、存在しないフォルダ・完全なパスでない値は保存できない。「保存」の有効・無効は <see cref="Revalidate"/> でまとめて決める。
/// </summary>
public partial class AppSettingsDialog : FluentWindow
{
    private readonly string _defaultHookDir;

    public AppSettingsDialog(int runningTimeoutMinutes, string hookDir, string defaultHookDir)
    {
        InitializeComponent();
        _defaultHookDir = defaultHookDir;
        HookDirBox.Text = hookDir;
        MinutesBox.Text = runningTimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MinutesBox.SelectAll();
        MinutesBox.Focus();
    }

    /// <summary>「保存」で閉じたときの値（分）。</summary>
    public int RunningTimeoutMinutes { get; private set; }

    /// <summary>「保存」で閉じたときの置き場所（正規化済みのフォルダ）。</summary>
    public string HookDir { get; private set; } = "";

    private void OnInputChanged(object sender, TextChangedEventArgs e) => Revalidate();

    /// <summary>分と置き場所の両方を検査して、エラーの文と「保存」の有効・無効を決める（片方を直しても、もう片方が不正なら保存できない）。</summary>
    private void Revalidate()
    {
        // 初期化の途中（部品がまだ無い）でも呼ばれる。
        if (SaveButton is null || MinutesBox is null || HookDirBox is null)
            return;

        var minutesError = RunningTimeoutInput.Validate(MinutesBox.Text);
        var hookDirError = HookDirInput.Validate(HookDirBox.Text, _defaultHookDir);
        ErrorText.Text = minutesError ?? "";
        HookDirErrorText.Text = hookDirError ?? "";
        SaveButton.IsEnabled = minutesError is null && hookDirError is null;
    }

    private void OnBrowseHookDirClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Hook exe を置くフォルダ" };
        if (HookDirInput.TryNormalize(HookDirBox.Text, out var current) && Directory.Exists(current))
            dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) == true)
            HookDirBox.Text = dialog.FolderName;
    }

    private void OnResetHookDirClick(object sender, RoutedEventArgs e) => HookDirBox.Text = _defaultHookDir;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!RunningTimeoutInput.TryParse(MinutesBox.Text, out var minutes) || !HookDirInput.TryNormalize(HookDirBox.Text, out var hookDir))
            return;
        RunningTimeoutMinutes = minutes;
        HookDir = hookDir;
        DialogResult = true;
    }
}
