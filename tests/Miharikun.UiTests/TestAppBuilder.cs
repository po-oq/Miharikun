using Avalonia;
using Avalonia.Headless;
using Miharikun.UiTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Miharikun.UiTests;

/// <summary>
/// 画面を出さずに Avalonia の部品を動かす（Avalonia.Headless）。Skia で実際に描くので、画像（スクリーンショット）も取れる。
/// アプリ本体の <c>App</c>（<c>App.axaml</c> の色・スタイル）をそのまま使う。
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Miharikun.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
