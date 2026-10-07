using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Miharikun.Core.Settings;
using Miharikun.Core.Storage;
using Miharikun.Services;

namespace Miharikun;

public partial class App : Application
{
    private AppComposition? _composition;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            Start(desktop);
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 起動（計画 7.7）：フォルダに依らないもの（ログ・未処理の例外・設定・テーマ）をウィンドウより先に済ませる。
    /// 引数があればそのフォルダ、無ければ Windows はカレントディレクトリ、mac はフォルダを選ぶ画面（Finder から開くと、カレントはルートなどになるため）。
    /// </summary>
    private void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var paths = AppPaths.Default();
        AppLog.Init(paths);
        LogUnhandledExceptions();

        var settings = new AppSettingsStore(paths, AppLog.Write);
        var theme = new ThemeService(settings);
        theme.Start();

        var window = new MainWindow();
        desktop.MainWindow = window;
        desktop.Exit += (_, _) => _composition?.Dispose();

        void Compose(string folder)
        {
            var services = new AvaloniaUiServices(() => window);
            _composition = new AppComposition(folder, paths, settings, theme, services);
            window.Attach(_composition);
        }

        if (desktop.Args is { Length: > 0 } args)
            Compose(Path.GetFullPath(args[0]));
        else if (!OperatingSystem.IsMacOS())
            Compose(Path.GetFullPath(Environment.CurrentDirectory));
        else
            window.Opened += async (_, _) =>
            {
                try
                {
                    var folder = await PickFolderAsync(window);
                    if (folder is null)
                        desktop.Shutdown();   // キャンセル・空なら終了（守るものが無い）
                    else
                        Compose(folder);
                }
                catch (Exception ex)
                {
                    AppLog.Write("フォルダの選択で例外: " + ex);
                    desktop.Shutdown();
                }
            };
    }

    private static async Task<string?> PickFolderAsync(Window window)
    {
        var folders = await window.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Miharikun で開くフォルダ", AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    /// <summary>
    /// 捕まえられなかった例外（背景スレッド・Task）を app.log にスタックつきで残す。ログだけで、握りつぶさない。
    /// （UI スレッドの例外は Avalonia が取り扱い、アプリを落とす。これまでと同じ。）
    /// </summary>
    private static void LogUnhandledExceptions()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Write($"未処理の例外（背景スレッド。終了中={args.IsTerminating}）: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
            AppLog.Write($"未処理の例外（Task）: {args.Exception}");
    }
}
