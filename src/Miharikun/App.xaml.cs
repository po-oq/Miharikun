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

        var theme = new ThemeService(new AppSettingsStore(paths, AppLog.Write));

        var agent = new CursorAgent();
        var store = new ProjectEventStore(agent, paths, folder, log: AppLog.Write,
            importer: new CursorTranscriptImporter(HookInstaller.ResolveCursorDir(), AppLog.Write));
        _monitor = new SessionMonitor(store, paths.EventsDir(agent.Id));
        var meta = new SessionMetaService(new MetaStore(paths, agent.Id, AppLog.Write), log: AppLog.Write);
        _viewModel = new MainViewModel(folder, _monitor, SynchronizationContext.Current!, _monitor.GetEvents, meta, new GitClient(folder));

        // 同梱の Hook exe は Miharikun.exe と同じフォルダ（単一ファイル発行でも実行ファイルの場所を使う）
        var appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var hookSetup = new HookSetup(HookInstaller.CreateDefault(paths, appDir));

        _ = Task.Run(() => PreviewFiles.CleanOld(paths.PreviewDir));   // 1 日より古い md の一時 HTML を消す
        _documents = new DocumentsViewModel(folder, new ProjectSettingsStore(paths, AppLog.Write), paths, () => theme.IsDark,
            SynchronizationContext.Current!, AppLog.Write);
        var window = new MainWindow(_viewModel, _documents, hookSetup, theme);
        theme.Start(window);
        window.Show();
        _monitor.Start();
        _viewModel.RefreshGit();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _documents?.Dispose();
        _monitor?.Dispose();
    }
}