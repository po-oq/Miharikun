using System.Diagnostics;

namespace Miharikun.Core.Install;

/// <summary>導入した Hook の確認（要件 8.1・計画 7.12）：mac の「隔離」の印の解除と、試しの起動（<c>--probe</c>）。</summary>
public interface IHookCheck
{
    /// <summary>mac だけ：<c>xattr -d com.apple.quarantine</c>。印が無いときの失敗は成功とみなす。Windows は何もしない。</summary>
    Task ClearQuarantineAsync(string exePath);

    /// <summary>Hook を <c>--probe</c> で起動し、3 秒以内に終了コード 0 で <c>ok</c> を出せば true。</summary>
    Task<bool> ProbeAsync(string exePath);
}

public sealed class HookCheck(Action<string>? log = null) : IHookCheck
{
    private const int TimeoutMs = 3000;

    public async Task ClearQuarantineAsync(string exePath)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var (exit, _) = await RunAsync("/usr/bin/xattr", ["-d", "com.apple.quarantine", exePath], 3000);
        log?.Invoke($"隔離の印の解除: 終了コード {exit?.ToString() ?? "なし"}（印が無いときも失敗になる）");
    }

    public async Task<bool> ProbeAsync(string exePath)
    {
        var (exit, output) = await RunAsync(exePath, ["--probe"], TimeoutMs);
        var ok = exit == 0 && output.Trim() == "ok";
        log?.Invoke($"Hook の試しの起動: {(ok ? "成功" : $"失敗（終了コード {exit?.ToString() ?? "なし"}）")}");
        return ok;
    }

    private static async Task<(int? Exit, string Output)> RunAsync(string file, string[] args, int timeoutMs)
    {
        try
        {
            var info = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
                info.ArgumentList.Add(a);

            using var process = Process.Start(info);
            if (process is null)
                return (null, "");
            process.StandardInput.Close();

            var output = process.StandardOutput.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return (null, "");
            }
            return (process.ExitCode, await output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (null, "");
        }
    }
}

/// <summary>試しの起動が失敗したときの案内の文（要件 8.1・計画 7.12 の 4）。</summary>
public static class HookGuidance
{
    private const string Translocation = "/AppTranslocation/";

    public static string Build(bool isMac, string? processPath)
    {
        if (!isMac)
            return "Hook を起動できませんでした。セキュリティソフトなどで止められていないか確かめてください。\nそのあと、もう一度 ⚙ の「Hook を導入」を選んでください。";

        var app = AppBundlePath(processPath);
        var text = "Hook を起動できませんでした。ターミナルで次の 1 行を実行してから、もう一度 ⚙ の「Hook を導入」を選んでください。\n\n"
                   + $"xattr -dr com.apple.quarantine \"{app ?? "/Applications/Miharikun.app"}\"";
        if (processPath is not null && processPath.Contains(Translocation, StringComparison.Ordinal))
            text = "Miharikun.app を「アプリケーション」に移してから開き直してください（いまは一時的な場所から動いています）。\n\n" + text;
        return text;
    }

    /// <summary>実行ファイルのパスから、それを含む <c>.app</c> の場所を求める。<c>.app</c> の中でなければ null。</summary>
    public static string? AppBundlePath(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath))
            return null;
        var i = processPath.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? null : processPath[..(i + 4)];
    }
}
