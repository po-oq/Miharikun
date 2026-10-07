using Miharikun.Core.Agents;
using Miharikun.Core.Git;
using Miharikun.Core.Install;
using Miharikun.Core.Memo;
using Miharikun.Core.Meta;
using Miharikun.Core.Sessions;
using Miharikun.Core.Settings;
using Miharikun.Core.Storage;
using Miharikun.Services;
using Miharikun.ViewModels;

namespace Miharikun;

/// <summary>
/// 対象フォルダが決まってからの組み立て（計画 7.7 の 4）。Source のリスト → Store → Monitor → メタ → <see cref="MainViewModel"/> →
/// <see cref="HookSetup"/> → 一時 HTML の掃除 → <see cref="DocumentsViewModel"/> → <see cref="MemoViewModel"/>。順は WPF 版と同じ。
/// フォルダに依らないもの（ログ・設定・テーマ）は、ウィンドウを出す前に <see cref="App"/> が済ませる。
/// </summary>
public sealed class AppComposition : IDisposable
{
    public SessionMonitor Monitor { get; }
    public MainViewModel ViewModel { get; }
    public DocumentsViewModel Documents { get; }
    public MemoViewModel Memo { get; }
    public HookSetup HookSetup { get; }
    public AppSettingsStore Settings { get; }
    public ThemeService Theme { get; }

    public AppComposition(string folder, AppPaths paths, AppSettingsStore settings, ThemeService theme, IUiServices services)
    {
        Settings = settings;
        Theme = theme;
        var ui = SynchronizationContext.Current ?? throw new InvalidOperationException("UI スレッドの同期コンテキストが無い");

        // 読み込みの元（Source）のリスト。エージェントが増えたら、ここに足す。
        var cursorDir = HookInstaller.ResolveCursorDir();
        var hookRegistration = new HookRegistration(Path.Combine(cursorDir, "hooks.json"), paths.EventsDir("cursor"));
        List<ISessionSource> sources =
        [
            new CursorSessionSource(new CursorAgent(), paths, folder, log: AppLog.Write,
                importer: new CursorTranscriptImporter(cursorDir, AppLog.Write, hookRegistration)),
            // Claude Code は会話ログを読むだけ（Hook は使わない）。~/.claude/projects が無くても入れてよい（読むだけで、無ければ何も出ない）
            new ClaudeSessionSource(folder, ClaudeLocations.ResolveClaudeDir(), AppLog.Write),
        ];
        var store = new ProjectEventStore(sources, log: AppLog.Write);
        Monitor = new SessionMonitor(store);
        var meta = new SessionMetaService(agentId => new MetaStore(paths, agentId, AppLog.Write), log: AppLog.Write);

        var git = new GitClient(folder);
        ViewModel = new MainViewModel(folder, Monitor, ui, Monitor.GetEvents, meta, services,
            git.GetStatus, s => SessionCommits.Load(s, git),
            runningTimeoutMinutes: settings.LoadRunningTimeoutMinutes(), hookErrorLogPath: paths.HookErrorLog, log: AppLog.Write);

        // 同梱の Hook は App と同じフォルダ（Windows は Miharikun.exe、mac は Miharikun.app/Contents/MacOS/）
        var appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        HookSetup = new HookSetup(dir => HookInstaller.CreateDefault(paths, appDir, dir), settings, paths, services);

        _ = Task.Run(() => PreviewFiles.CleanOld(paths.PreviewDir));   // 1 日より古い md の一時 HTML を消す
        Documents = new DocumentsViewModel(folder, new ProjectSettingsStore(paths, AppLog.Write), paths, () => theme.IsDark,
            ui, services, AppLog.Write);
        Memo = new MemoViewModel(folder, new ProjectMemoStore(paths, AppLog.Write), paths, () => theme.IsDark,
            ui, path => Documents.IsInAppDocument(path, out var rel) ? rel : null, services, AppLog.Write);

        theme.Changed += _ =>
        {
            Documents.OnThemeChanged();
            Memo.OnThemeChanged();
        };
    }

    /// <summary>画面に渡したあとの開始（WPF 版の <c>OnStartup</c> の最後と同じ順）。</summary>
    public void Start()
    {
        Monitor.Start();
        ViewModel.RefreshGit();
        ViewModel.StartClock();
    }

    /// <summary>アプリの終了のとき 1 回（計画 7.6 の 8）。</summary>
    public void Dispose()
    {
        ViewModel.Dispose();
        Documents.Dispose();
        Memo.Dispose();
        Monitor.Dispose();
    }
}
