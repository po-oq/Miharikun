using System.Windows;
using Miharikun.Core.Install;

namespace Miharikun;

/// <summary>Cursor の hook の導入・削除をユーザーに確認しながら行う（要件 8章）。</summary>
public sealed class HookSetup(HookInstaller installer)
{
    private const string Title = "Miharikun - Hook の導入";

    /// <summary>
    /// 起動時の確認。未導入・一部未登録・Hook exe が古いときだけ導入を提案する。
    /// 同梱の Hook exe が無い（開発時など）ときは提案しない。
    /// </summary>
    public void CheckAtStartup(Window owner)
    {
        switch (installer.GetState())
        {
            case HookInstallState.Installed:
                return;

            case HookInstallState.Unreadable:
                Show(owner, $"{installer.HooksJsonPath} を読めないため、hook の導入状況を確認できません。\n" +
                            "ファイルは変更していません。内容を確認してください。", MessageBoxImage.Warning);
                return;

            case var state when !installer.CanInstall:
                AppLog.Write($"hook の状態は {state} だが、同梱の {HookInstaller.HookExeName} が無いので導入を提案しない");
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

    /// <summary>メニューからの導入／再導入。</summary>
    public void InstallFromMenu(Window owner)
    {
        if (!installer.CanInstall)
        {
            Show(owner, $"{HookInstaller.HookExeName} が見つかりません。\nMiharikun.exe と同じフォルダに置いてください。", MessageBoxImage.Warning);
            return;
        }
        RunInstall(owner);
    }

    public void UninstallFromMenu(Window owner)
    {
        var answer = MessageBox.Show(owner,
            $"{installer.HooksJsonPath} から Miharikun の hook を外します。\n" +
            "ほかのツールの設定は変えず、変更前にバックアップを作ります。\n" +
            "記録済みのデータと Hook exe は残ります。\n\n外しますか？",
            "Miharikun - Hook の削除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        var result = installer.Uninstall();
        AppLog.Write("hook の削除: " + result.Message);
        Show(owner, result.Message, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Offer(Window owner, string reason)
    {
        var answer = MessageBox.Show(owner,
            reason + "\n\n" +
            $"・Hook exe を {installer.InstalledExePath} にコピーします\n" +
            $"・{installer.HooksJsonPath} に Miharikun の登録を追加します\n" +
            "　（既存の設定はそのまま。変更前に hooks.json.bak-日時 を作ります）\n\n導入しますか？",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            RunInstall(owner);
    }

    private void RunInstall(Window owner)
    {
        var result = installer.Install();
        AppLog.Write("hook の導入: " + result.Message);
        Show(owner,
            result.Success ? result.Message + "\n\nCursor 側に反映されない場合は、Cursor を再起動してください。" : result.Message,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static void Show(Window owner, string text, MessageBoxImage icon) =>
        MessageBox.Show(owner, text, Title, MessageBoxButton.OK, icon);
}