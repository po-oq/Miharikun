using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Core.Install;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>⚙ → 設定…（要件 12.9・8.2）：停止とみなす時間と Hook の置き場所の検査・保存・既定に戻す・キャンセル。</summary>
public sealed class AppSettingsDialogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mk-settings-ui-" + Guid.NewGuid().ToString("N"));
    private string ExistingDir => Directory.CreateDirectory(Path.Combine(_dir, "hook")).FullName;
    private string DefaultDir => Path.Combine(_dir, "default-bin");   // 既定の場所：まだ無くてもよい（導入のときにアプリが作る）

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T Find<T>(Window w, string automationId) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == automationId);

    private static void Flush() => Dispatcher.UIThread.RunJobs();

    private (AppSettingsDialog Dialog, Window Owner) Open(int minutes = 10, string? hookDir = null)
    {
        var owner = new Window { Width = 400, Height = 300 };
        owner.Show();
        var dialog = new AppSettingsDialog(minutes, hookDir ?? ExistingDir, DefaultDir);
        dialog.Show(owner);
        Flush();
        return (dialog, owner);
    }

    [AvaloniaFact]
    public void The_current_values_are_shown_and_a_valid_state_can_be_saved()
    {
        var (dialog, _) = Open(25);

        Assert.Equal("25", Find<TextBox>(dialog, "RunningTimeoutBox").Text);
        Assert.Equal(ExistingDir, Find<TextBox>(dialog, "HookDirBox").Text);
        Assert.True(Find<Button>(dialog, "SaveButton").IsEnabled);
        Assert.Equal("", Find<TextBlock>(dialog, "RunningTimeoutError").Text);
        Assert.Equal("", Find<TextBlock>(dialog, "HookDirError").Text);
    }

    [AvaloniaTheory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData("999999")]
    public void An_invalid_number_of_minutes_cannot_be_saved_and_shows_the_reason(string text)
    {
        var (dialog, _) = Open();

        Find<TextBox>(dialog, "RunningTimeoutBox").Text = text;
        Flush();

        Assert.False(Find<Button>(dialog, "SaveButton").IsEnabled);
        Assert.Contains("整数で入力してください", Find<TextBlock>(dialog, "RunningTimeoutError").Text);
    }

    [AvaloniaFact]
    public void A_missing_folder_a_relative_path_and_an_empty_value_cannot_be_saved_and_the_other_field_does_not_rescue_it()
    {
        var (dialog, _) = Open();
        var box = Find<TextBox>(dialog, "HookDirBox");
        var save = Find<Button>(dialog, "SaveButton");

        box.Text = Path.Combine(_dir, "nothing");
        Flush();
        Assert.False(save.IsEnabled);
        Assert.Equal("フォルダが見つかりません", Find<TextBlock>(dialog, "HookDirError").Text);

        box.Text = "relative/dir";
        Flush();
        Assert.False(save.IsEnabled);
        Assert.Equal(HookWording.NotAbsoluteMessage, Find<TextBlock>(dialog, "HookDirError").Text);   // OS ごとの言い方（要件 12.12）

        box.Text = "";
        Flush();
        Assert.False(save.IsEnabled);
        Assert.Equal("フォルダを指定してください", Find<TextBlock>(dialog, "HookDirError").Text);

        box.Text = ExistingDir;
        Find<TextBox>(dialog, "RunningTimeoutBox").Text = "x";   // 置き場所を直しても、分が不正なら保存できない
        Flush();
        Assert.False(save.IsEnabled);
    }

    [AvaloniaFact]
    public void The_default_place_is_accepted_even_when_it_does_not_exist_yet_and_the_reset_button_puts_it_back()
    {
        var (dialog, _) = Open();
        var box = Find<TextBox>(dialog, "HookDirBox");

        Find<Button>(dialog, "HookDirResetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Flush();

        Assert.Equal(DefaultDir, box.Text);
        Assert.True(Find<Button>(dialog, "SaveButton").IsEnabled);
        Assert.False(Directory.Exists(DefaultDir));   // 作らない
    }

    [AvaloniaFact]
    public async Task Saving_returns_the_minutes_and_the_normalised_folder()
    {
        var owner = new Window { Width = 400, Height = 300 };
        owner.Show();
        var task = AppSettingsDialog.ShowAsync(owner, 10, ExistingDir, DefaultDir);
        Flush();
        var dialog = owner.OwnedWindows.OfType<AppSettingsDialog>().Single();

        Find<TextBox>(dialog, "RunningTimeoutBox").Text = " 30 ";
        Find<TextBox>(dialog, "HookDirBox").Text = ExistingDir + Path.DirectorySeparatorChar;   // 末尾の区切りは落とす
        Flush();
        Find<Button>(dialog, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Flush();

        var result = await task;
        Assert.NotNull(result);
        Assert.Equal(30, result!.RunningTimeoutMinutes);
        Assert.Equal(ExistingDir, result.HookDir);
    }

    [AvaloniaFact]
    public async Task Cancel_returns_nothing()
    {
        var owner = new Window { Width = 400, Height = 300 };
        owner.Show();
        var task = AppSettingsDialog.ShowAsync(owner, 10, ExistingDir, DefaultDir);
        Flush();
        var dialog = owner.OwnedWindows.OfType<AppSettingsDialog>().Single();

        Find<TextBox>(dialog, "RunningTimeoutBox").Text = "99";
        Find<Button>(dialog, "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Flush();

        Assert.Null(await task);
    }

    [AvaloniaFact]
    public void The_label_and_the_help_follow_the_os_wording()
    {
        var (dialog, _) = Open();
        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();

        Assert.Contains(HookWording.PlacementLabel, texts);
        Assert.Contains(HookWording.PlacementHelp, texts);
        Assert.Contains(OperatingSystem.IsWindows() ? "Cursor の Hook exe の置き場所" : "Cursor の Hook の置き場所", texts);
    }
}
