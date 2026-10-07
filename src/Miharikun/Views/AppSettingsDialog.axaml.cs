using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Miharikun.Core.Install;
using Miharikun.Core.Settings;

namespace Miharikun.Views;

/// <summary>「保存」で閉じたときの値。</summary>
public sealed record AppSettingsResult(int RunningTimeoutMinutes, string HookDir);

/// <summary>
/// アプリ全体の設定のダイアログ（要件 12.9）：「停止とみなす時間（分）」と「Cursor の Hook の置き場所」（Issue #17）。
/// 数字以外・負の数、存在しないフォルダ・完全なパスでない値は保存できない。「保存」の有効・無効は <see cref="Revalidate"/> でまとめて決める
/// （片方を直しても、もう片方が不正なら保存できない）。欄の名前・説明・誤りの文は OS で言い方が違う（要件 12.12）。
/// </summary>
public partial class AppSettingsDialog : Window
{
    private readonly string _defaultHookDir = "";

    public AppSettingsDialog()
    {
        InitializeComponent();
    }

    public AppSettingsDialog(int runningTimeoutMinutes, string hookDir, string defaultHookDir) : this()
    {
        _defaultHookDir = defaultHookDir;
        HookDirLabel.Text = HookWording.PlacementLabel;
        HookDirHelp.Text = HookWording.PlacementHelp;
        HookDirBox.Text = hookDir;
        MinutesBox.Text = runningTimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MinutesBox.TextChanged += (_, _) => Revalidate();
        HookDirBox.TextChanged += (_, _) => Revalidate();
        Revalidate();
        Opened += (_, _) =>
        {
            MinutesBox.Focus();
            MinutesBox.SelectAll();
        };
    }

    /// <summary>設定を開く。「保存」なら値、キャンセル・閉じたなら null。</summary>
    public static Task<AppSettingsResult?> ShowAsync(Window owner, int runningTimeoutMinutes, string hookDir, string defaultHookDir) =>
        new AppSettingsDialog(runningTimeoutMinutes, hookDir, defaultHookDir).ShowDialog<AppSettingsResult?>(owner);

    /// <summary>分と置き場所の両方を検査して、エラーの文と「保存」の有効・無効を決める。</summary>
    private void Revalidate()
    {
        var minutesError = RunningTimeoutInput.Validate(MinutesBox.Text);
        var hookDirError = HookDirInput.Validate(HookDirBox.Text, _defaultHookDir);
        ErrorText.Text = minutesError ?? "";
        HookDirErrorText.Text = hookDirError ?? "";
        SaveButton.IsEnabled = minutesError is null && hookDirError is null;
    }

    private async void OnBrowseHookDirClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var options = new FolderPickerOpenOptions { Title = HookWording.BrowseTitle, AllowMultiple = false };
            if (HookDirInput.TryNormalize(HookDirBox.Text, out var current) && Directory.Exists(current))
                options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(current);   // 今の置き場所があれば、そこから開く
            var folders = await StorageProvider.OpenFolderPickerAsync(options);
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
                HookDirBox.Text = path;
        }
        catch (Exception ex)
        {
            AppLog.Write("Hook の置き場所の参照で例外: " + ex);
        }
    }

    private void OnResetHookDirClick(object? sender, RoutedEventArgs e) => HookDirBox.Text = _defaultHookDir;

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (!RunningTimeoutInput.TryParse(MinutesBox.Text, out var minutes) || !HookDirInput.TryNormalize(HookDirBox.Text, out var hookDir))
            return;
        Close(new AppSettingsResult(minutes, hookDir));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
