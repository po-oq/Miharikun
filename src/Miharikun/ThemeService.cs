using Avalonia;
using Avalonia.Styling;
using Miharikun.Core.Settings;

namespace Miharikun;

/// <summary>
/// テーマの適用（計画 7.8）。System のときは OS のライト/ダークの変更に、実行中も追従する（<c>ThemeVariant.Default</c>）。
/// 色は <c>App.axaml</c> の <c>ThemeDictionaries</c>（<c>Themes/Colors.*.axaml</c>）から読まれるので、辞書の差し替えは要らない。
/// </summary>
public sealed class ThemeService(AppSettingsStore settings)
{
    public AppTheme Mode { get; private set; } = AppTheme.System;

    /// <summary>いま、ダークで表示しているか。</summary>
    public bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>見た目が変わった（⚙ からの変更・OS の変更の両方）。引数は変更後の IsDark。UI スレッドで呼ばれる。</summary>
    public event Action<bool>? Changed;

    /// <summary>保存したテーマを適用する。ウィンドウを出す前に呼ぶ（空のウィンドウも、保存したテーマで出る）。</summary>
    public void Start()
    {
        var app = Application.Current!;
        app.ActualThemeVariantChanged += (_, _) => Changed?.Invoke(IsDark);
        Apply(settings.LoadTheme());
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
        Application.Current!.RequestedThemeVariant = mode switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
