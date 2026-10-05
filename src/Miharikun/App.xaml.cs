using System.IO;
using System.Windows;
using Miharikun.Core.Agents;
using Miharikun.Core.Git;
using Miharikun.Core.Install;
using Miharikun.Core.Meta;
using Miharikun.Core.Sessions;
using Miharikun.Core.Settings;
using Miharikun.Core.Storage;
using Miharikun.ViewModels;

namespace Miharikun;

public partial class App : Application
{
    private SessionMonitor? _monitor;
    private MainViewModel? _viewModel;
    private DocumentsViewModel? _documents;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // 起動：Miharikun.exe [フォルダ]。省略時はカレントディレクトリ（要件 9章）。
        var folder = Path.GetFullPath(e.Args.Length > 0 ? e.Args[0] : Environment.CurrentDirectory);

        var paths = AppPaths.Default();
        AppLog.Init(paths);
        LogUnhandledExceptions();

        var appSettings = new AppSettingsStore(paths, AppLog.Write);
        var theme = new ThemeService(appSettings);

        // 読み込みの元（Source）のリスト。エージェントが増えたら、ここに足す。
        List<ISessionSource> sources =
        [
            new CursorSessionSource(new CursorAgent(), paths, folder, log: AppLog.Write,
                importer: new CursorTranscriptImporter(HookInstaller.ResolveCursorDir(), AppLog.Write)),
            // Claude Code は会話ログを読むだけ（Hook は使わない）。.claude\projects が無くても入れてよい（読むだけで、無ければ何も出ない）
            new ClaudeSessionSource(folder, ClaudeLocations.ResolveClaudeDir(), AppLog.Write),
        ];
        var store = new ProjectEventStore(sources, log: AppLog.Write);
        _monitor = new SessionMonitor(store);
        var meta = new SessionMetaService(agentId => new MetaStore(paths, agentId, AppLog.Write), log: AppLog.Write);
        _viewModel = new MainViewModel(folder, _monitor, SynchronizationContext.Current!, _monitor.GetEvents, meta, new GitClient(folder),
            runningTimeoutMinutes: appSettings.LoadRunningTimeoutMinutes());

        // 同梱の Hook exe は Miharikun.exe と同じフォルダ（単一ファイル発行でも実行ファイルの場所を使う）
        var appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var hookSetup = new HookSetup(HookInstaller.CreateDefault(paths, appDir));

        WebViewEnvironment.Configure(paths.WebView2Dir);
        _ = Task.Run(() => PreviewFiles.CleanOld(paths.PreviewDir));   // 1 日より古い md の一時 HTML を消す
        _documents = new DocumentsViewModel(folder, new ProjectSettingsStore(paths, AppLog.Write), paths, () => theme.IsDark,
            SynchronizationContext.Current!, AppLog.Write);
        var window = new MainWindow(_viewModel, _documents, hookSetup, theme, appSettings);
        theme.Start(window);
        window.Show();
        _monitor.Start();
        _viewModel.RefreshGit();
    }

    /// <summary>
    /// 捕まえられなかった例外（UI スレッド・背景スレッド・Task）を app.log にスタックつきで残す。
    /// ログだけで、握りつぶさない（これまでと同じく、UI スレッドと背景スレッドの例外はアプリを落とす）。
    /// </summary>
    private void LogUnhandledExceptions()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Write($"未処理の例外（背景スレッド。終了中={args.IsTerminating}）: {args.ExceptionObject}");
        DispatcherUnhandledException += (_, args) =>
            AppLog.Write($"未処理の例外（UI スレッド）: {args.Exception}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
            AppLog.Write($"未処理の例外（Task）: {args.Exception}");
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _documents?.Dispose();
        _monitor?.Dispose();
    }
}