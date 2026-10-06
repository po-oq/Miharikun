using System.IO;
using System.Windows;
using Miharikun.Core.Install;
using Miharikun.Core.Settings;
using Miharikun.Core.Storage;

namespace Miharikun;

/// <summary>
/// Cursor の hook の導入・削除をユーザーに確認しながら行う（要件 8章）。
/// Cursor 専用（Claude Code は会話ログだけで読むので hook を使わない。別のエージェントが hook を使うようになったら、抽象化する）。
/// Hook exe の置き場所（settings.json の hookDir。8.2）を持ち、起動時に hooks.json の登録から受け入れる・設定画面で変えたら導入し直す。
/// </summary>
public sealed class HookSetup
{
    private const string Title = "Miharikun - Hook の導入";

    private readonly Func<string?, HookInstaller> _create;
    private readonly AppSettingsStore _settings;
    private readonly AppPaths _paths;
    private HookInstaller _installer;

    /// <param name="create">置き場所（null なら既定）から installer を作る。</param>
    public HookSetup(Func<string?, HookInstaller> create, AppSettingsStore settings, AppPaths paths)
    {
        _create = create;
        _settings = settings;
        _paths = paths;
        _installer = create(settings.LoadHookDir());
    }

    /// <summary>いまの置き場所（フォルダ）。設定画面の初期値。</summary>
    public string CurrentHookDir => Path.GetDirectoryName(_installer.InstalledExePath)!;

    /// <summary>既定の置き場所。設定画面の「既定に戻す」。</summary>
    public string DefaultHookDir => HookInstaller.DefaultHookDir(_paths);

    /// <summary>
    /// 起動時の確認（計画 7.1 の順）。hooks.json を読めないときは警告して終わり。導入済みなら終わり。
    /// hooks.json の登録が「全イベントで同じ別の場所」を指し、その exe があるなら、ダイアログなしでその場所を採用する。
    /// そのうえで、未導入・一部未登録・Hook exe が古いときだけ導入を提案する。同梱の Hook exe が無い（開発時など）ときは提案しない。
    /// </summary>
    public void CheckAtStartup(Window owner)
    {
        var state = _installer.GetState();
        if (state is not (HookInstallState.Installed or HookInstallState.Unreadable)
            && _installer.FindRegisteredElsewhere() is { } registeredExe)
        {
            AcceptRegisteredPlace(Path.GetDirectoryName(registeredExe)!);
            state = _installer.GetState();   // 1 回だけ。受け入れは繰り返さない
        }

        switch (state)
        {
            case HookInstallState.Installed:
                return;

            case HookInstallState.Unreadable:
                Show(owner, $"{_installer.HooksJsonPath} を読めないため、hook の導入状況を確認できません。\n" +
                            "ファイルは変更していません。内容を確認してください。", MessageBoxImage.Warning);
                return;

            case var other when !_installer.CanInstall:
                AppLog.Write($"hook の状態は {other} だが、同梱の {HookInstaller.HookExeName} が無いので導入を提案しない");
                return;

            case HookInstallState.NotInstalled:
                Offer(owner,
                    "Cursor の hook が未導入です。導入すると、チャットの状況が記録されてダッシュボードに表示されます。");
                return;

            case HookInstallState.Partial:
                Offer(owner, "一部のイベントが未登録、または登録先が現在の場所と違います。登録し直します。");
                return;

            case HookInstallState.ExeOutdated:
                Offer(owner, "導入済みの Hook exe が、同梱のものと違います（更新）。");
                return;
        }
    }

    /// <summary>
    /// 受け入れ：置き場所の設定に保存して（既定と同じならキーを消す）、<b>受け入れたフォルダを直接渡して</b> installer を作り直す。
    /// 設定を読み直さない：保存に失敗したときに既定の場所で作り直すと、動いている登録を止められている場所に書き換えてしまうため。
    /// </summary>
    private void AcceptRegisteredPlace(string folder)
    {
        var saved = _settings.SaveHookDir(HookInstaller.ToSettingValue(folder, _paths));
        AppLog.Write(saved
            ? $"hook の置き場所を hooks.json の登録から採用: {folder}"
            : $"hook の置き場所を hooks.json の登録から採用: {folder}（保存できなかったので今回だけ使う）");
        _installer = _create(folder);
    }

    /// <summary>
    /// 設定画面で置き場所が変わったとき：保存して、その場所に導入し直すかを聞く（8.2・7.5）。
    /// いいえなら hooks.json は変えない（次の起動で hooks.json の登録の場所に戻る）。
    /// </summary>
    public void ChangePlacement(Window owner, string newFolder)
    {
        var value = HookInstaller.ToSettingValue(newFolder, _paths);
        if (!_settings.SaveHookDir(value))
        {
            Show(owner, "設定を保存できませんでした。置き場所は変更していません。", MessageBoxImage.Warning);
            return;
        }
        AppLog.Write($"hook の置き場所を設定: {newFolder}");
        _installer = _create(value);

        var answer = MessageBox.Show(owner,
            "この場所に Hook を導入し直しますか？\n" +
            $"（{_installer.HooksJsonPath} の Miharikun の登録をこの場所に書き換え、Hook exe をコピーします）\n\n" +
            newFolder,
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            InstallFromMenu(owner);
    }

    /// <summary>メニューからの導入／再導入。</summary>
    public void InstallFromMenu(Window owner)
    {
        if (!_installer.CanInstall)
        {
            Show(owner, $"{HookInstaller.HookExeName} が見つかりません。\nMiharikun.exe と同じフォルダに置いてください。", MessageBoxImage.Warning);
            return;
        }
        RunInstall(owner);
    }

    public void UninstallFromMenu(Window owner)
    {
        var answer = MessageBox.Show(owner,
            $"{_installer.HooksJsonPath} から Miharikun の hook を外します。\n" +
            "ほかのツールの設定は変えず、変更前にバックアップを作ります。\n" +
            "記録済みのデータと Hook exe は残ります。\n\n外しますか？",
            "Miharikun - Hook の削除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        var result = _installer.Uninstall();
        AppLog.Write("hook の削除: " + result.Message);
        Show(owner, result.Message, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Offer(Window owner, string reason)
    {
        var answer = MessageBox.Show(owner,
            reason + "\n\n" +
            $"・Hook exe を {_installer.InstalledExePath} にコピーします\n" +
            $"・{_installer.HooksJsonPath} に Miharikun の登録を追加します\n" +
            "　（既存の設定はそのまま。変更前に hooks.json.bak-日時 を作ります）\n\n導入しますか？",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            RunInstall(owner);
    }

    private void RunInstall(Window owner)
    {
        var result = _installer.Install();
        AppLog.Write("hook の導入: " + result.Message);
        Show(owner,
            result.Success ? result.Message + "\n\nCursor 側に反映されない場合は、Cursor を再起動してください。" : result.Message,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static void Show(Window owner, string text, MessageBoxImage icon) =>
        MessageBox.Show(owner, text, Title, MessageBoxButton.OK, icon);
}
