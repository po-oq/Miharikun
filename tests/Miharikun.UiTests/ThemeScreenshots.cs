using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miharikun.Tests.Presentation;
using Miharikun.Views;

namespace Miharikun.UiTests;

/// <summary>
/// 30-2：見た目の確認用に、同じ画面（カード一覧・詳細のヘッダー・右ペインの見出し）をライトとダークで撮る（テーマは FluentTheme に決まった。
/// 見比べのとき撮った SukiUI の画像は <c>docs/macos-support/theme-compare/</c>）。
/// <c>MIHARIKUN_SHOTS=&lt;フォルダ&gt;</c> のときだけ動く（普段の <c>dotnet test</c> では飛ばす）。
/// </summary>
public sealed class ThemeScreenshots
{
    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Capture(string variant)
    {
        var dir = Environment.GetEnvironmentVariable("MIHARIKUN_SHOTS");
        if (string.IsNullOrEmpty(dir))
        {
            Assert.Skip("MIHARIKUN_SHOTS=<フォルダ> のときだけ動く");
            return;
        }

        Directory.CreateDirectory(dir);
        var app = Application.Current!;
        app.RequestedThemeVariant = variant == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        using var h = new MainVmHarness();
        Scene.Fill(h);
        var window = new Window { Width = 1280, Height = 1100, Content = new DashboardView { DataContext = h.Vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // 成果の「変更したファイル」を開いた状態も画に入れる（開閉の見た目の確認）
        var expander = window.GetVisualDescendants().OfType<Expander>()
            .First(x => Avalonia.Automation.AutomationProperties.GetAutomationId(x) == "ChangedFilesExpander");
        expander.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(dir, $"{variant}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        // 設定ダイアログ（⚙ → 設定…）も撮る。置き場所は存在するフォルダ
        var dialog = new AppSettingsDialog(10, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), Path.Combine(dir, "default-bin"));
        dialog.Show(window);
        Dispatcher.UIThread.RunJobs();
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        dialog.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"settings-{variant}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        dialog.Close();
        window.Close();
    }
}
