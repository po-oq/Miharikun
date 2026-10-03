using System.IO;
using System.Windows;
using Miharikun.Core.Agents;
using Miharikun.Core.Git;
using Miharikun.Core.Meta;
using Miharikun.Core.Sessions;
using Miharikun.Core.Storage;
using Miharikun.ViewModels;

namespace Miharikun;

public partial class App : Application
{
    private SessionMonitor? _monitor;
    private MainViewModel? _viewModel;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // 起動：Miharikun.exe [フォルダ]。省略時はカレントディレクトリ（要件 9章）。
        var folder = Path.GetFullPath(e.Args.Length > 0 ? e.Args[0] : Environment.CurrentDirectory);

        var paths = AppPaths.Default();
        AppLog.Init(paths);

        var agent = new CursorAgent();
        var store = new ProjectEventStore(agent, paths, folder, log: AppLog.Write);
        _monitor = new SessionMonitor(store, paths.EventsDir(agent.Id));
        var meta = new SessionMetaService(new MetaStore(paths, agent.Id, AppLog.Write), log: AppLog.Write);
        _viewModel = new MainViewModel(folder, _monitor, SynchronizationContext.Current!, _monitor.GetEvents, meta, new GitClient(folder));

        new MainWindow(_viewModel).Show();
        _monitor.Start();
        _viewModel.RefreshGit();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _monitor?.Dispose();
    }
}