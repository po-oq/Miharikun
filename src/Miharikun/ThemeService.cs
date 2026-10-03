using System.Windows;
using Miharikun.Core.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Miharikun;

/// <summary>
/// テーマの適用。System のときは OS のライト/ダーク変更に実行中も追従する。
/// WPF-UI のコントロール色は ApplicationThemeManager が、アプリ独自の色（Themes\Colors.*.xaml）はここで差し替える。
/// </summary>
public sealed class ThemeService(AppSettingsStore settings)
{
    private const string ColorsDictionaryPrefix = "Themes/Colors.";

    private Window? _window;

    public AppTheme Mode { get; private set; } = AppTheme.System;

    /// <summary>ウィンドウが出来てから呼ぶ（OS の変更の監視にウィンドウが要る）。</summary>
    public void Start(Window window)
    {
        _window = window;
        Apply(settings.LoadTheme());
        // 監視の開始・解除は、ウィンドウが読み込まれてからでないとできない。
        window.Loaded += (_, _) => UpdateWatcher();
    }

    public void Set(AppTheme mode)
    {
        if (mode == Mode)
            return;
        settings.SaveTheme(mode);
        Apply(mode);
    }

    private void Apply(AppTheme mode)
    {
        Mode = mode;
        UpdateWatcher();

        var dark = mode switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => ApplicationThemeManager.GetSystemTheme() != SystemTheme.Light,
        };
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica);
        SetColors(dark);
    }

    private void UpdateWatcher()
    {
        if (_window is not { IsLoaded: true })
            return;
        if (Mode == AppTheme.System)
            SystemThemeWatcher.Watch(_window, WindowBackdropType.Mica);
        else
            SystemThemeWatcher.UnWatch(_window);
    }

    /// <summary>独自の色の辞書を、ライト用／ダーク用に差し替える。</summary>
    private static void SetColors(bool dark)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri($"{ColorsDictionaryPrefix}{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative);
        var existing = merged.FirstOrDefault(d => d.Source?.OriginalString.StartsWith(ColorsDictionaryPrefix, StringComparison.Ordinal) == true);
        if (existing is not null)
            merged.Remove(existing);
        merged.Add(new ResourceDictionary { Source = uri });
    }
}
