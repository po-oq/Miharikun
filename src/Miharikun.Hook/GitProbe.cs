using System.Diagnostics;
using Miharikun.Core.Agents;
using Miharikun.Core.Git;

namespace Miharikun.Hook;

/// <summary>git rev-parse でブランチと HEAD を取る。リポジトリでない／失敗／タイムアウト時は null。</summary>
internal static class GitProbe
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(1.5);

    public static GitSnapshot? TryGet(string root)
    {
        // 2つのコマンドを並行して走らせ、全体でも 1.5 秒に収める。
        var branchTask = Task.Run(() => Run(root, "rev-parse", "--abbrev-ref", "HEAD"));
        var headTask = Task.Run(() => Run(root, "rev-parse", "HEAD"));

        var branch = branchTask.GetAwaiter().GetResult();
        var head = headTask.GetAwaiter().GetResult();
        return branch is null && head is null ? null : new GitSnapshot(branch, head);
    }

    private static string? Run(string root, params string[] args)
    {
        try
        {
            if (GitLocator.Find() is not { } git)
                return null;   // mac で git が使えない（Command Line Tools が無い）ときは、git を省略する

            var psi = new ProcessStartInfo(git)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(root);
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var stdout = process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(CommandTimeout))
            {
                try { process.Kill(true); } catch { }
                return null;
            }
            if (process.ExitCode != 0)
                return null;

            var text = stdout.GetAwaiter().GetResult().Trim();
            return text.Length == 0 ? null : text;
        }
        catch
        {
            return null;
        }
    }
}