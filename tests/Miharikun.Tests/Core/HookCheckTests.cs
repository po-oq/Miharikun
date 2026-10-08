using Miharikun.Core.Install;

namespace Miharikun.Tests.Core;

/// <summary>29-3：導入のあとの確認（隔離の印の解除・試しの起動）の案内の文と、試しの起動の判定。</summary>
public sealed class HookCheckTests
{
    [Fact]
    public void Guidance_on_windows_points_at_security_software_and_has_no_command()
    {
        var text = HookGuidance.Build(isMac: false, @"C:\apps\Miharikun.exe");

        Assert.Contains("セキュリティソフト", text);
        Assert.DoesNotContain("xattr", text);
    }

    [Fact]
    public void Guidance_on_mac_gives_a_one_line_xattr_for_the_app_that_is_running()
    {
        var text = HookGuidance.Build(isMac: true, "/Users/x/Apps/My Tools/Miharikun.app/Contents/MacOS/Miharikun");

        Assert.Contains("ターミナル", text);
        Assert.Contains("xattr -dr com.apple.quarantine \"/Users/x/Apps/My Tools/Miharikun.app\"", text);
        Assert.DoesNotContain("移してから", text);
    }

    [Fact]
    public void Guidance_on_mac_tells_to_move_the_app_first_when_it_runs_from_an_app_translocation_folder()
    {
        var path = "/private/var/folders/ab/xyz/AppTranslocation/0A1B/d/Miharikun.app/Contents/MacOS/Miharikun";

        var text = HookGuidance.Build(isMac: true, path);

        Assert.StartsWith("Miharikun.app を「アプリケーション」に移してから開き直してください", text);
        Assert.Contains("xattr -dr com.apple.quarantine", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/Users/x/dev/Miharikun/bin/Debug/net10.0/Miharikun")]   // dotnet run など。.app の中ではない
    public void Guidance_on_mac_falls_back_to_the_applications_folder_when_the_app_is_not_in_a_bundle(string? path)
    {
        Assert.Null(HookGuidance.AppBundlePath(path));
        Assert.Contains("\"/Applications/Miharikun.app\"", HookGuidance.Build(isMac: true, path));
    }

    [WindowsFact]
    public void Wording_on_windows_keeps_the_existing_texts()
    {
        Assert.Equal("Hook exe", HookWording.Noun);
        Assert.Equal("Miharikun.exe と同じフォルダ", HookWording.BundledPlace);
        Assert.Equal("Cursor の Hook exe の置き場所", HookWording.PlacementLabel);
        Assert.Equal("⚠ Cursor の Hook が記録していません（Hook なし 3 件）。この PC で Hook exe の実行が止められている可能性があります。⚙ →「設定…」で置き場所を変えてください。",
            HookWording.NoHookWarning(3));
    }

    [MacFact]
    public void Wording_on_mac_says_hook_and_the_app_bundle()
    {
        Assert.Equal("Hook", HookWording.Noun);
        Assert.Equal("Miharikun.app の中", HookWording.BundledPlace);
        Assert.Equal("Cursor の Hook の置き場所", HookWording.PlacementLabel);
        Assert.Contains("Hook なし 3 件", HookWording.NoHookWarning(3));
        Assert.Contains("この Mac で", HookWording.NoHookWarning(3));
        Assert.Contains("「隔離」の印", HookWording.NoHookWarning(3));
    }

    // ---------------------------------------------------------------- 試しの起動

    [MacFact]
    public async Task Probe_is_true_when_the_program_prints_ok_and_exits_with_0()
    {
        Assert.False(await new HookCheck().ProbeAsync("/usr/bin/true"));   // 終了コードは 0 だが ok を出さない
        var script = WriteScript("#!/bin/sh\nprintf ok\n");
        try { Assert.True(await new HookCheck().ProbeAsync(script)); }
        finally { File.Delete(script); }
    }

    [MacFact]
    public async Task Probe_is_false_for_a_missing_file_a_failing_exit_code_and_a_hang()
    {
        Assert.False(await new HookCheck().ProbeAsync("/no/such/Miharikun.Hook"));

        var fail = WriteScript("#!/bin/sh\nprintf ok\nexit 3\n");
        var hang = WriteScript("#!/bin/sh\nsleep 30\n");
        try
        {
            Assert.False(await new HookCheck().ProbeAsync(fail));
            var started = DateTime.UtcNow;
            Assert.False(await new HookCheck().ProbeAsync(hang));
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));   // 3 秒で打ち切る
        }
        finally
        {
            File.Delete(fail);
            File.Delete(hang);
        }
    }

    [MacFact]
    public async Task Clearing_the_quarantine_removes_the_mark_and_a_missing_mark_is_not_an_error()
    {
        var file = Path.Combine(Path.GetTempPath(), "mk-quarantine-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "x");
        try
        {
            await new HookCheck().ClearQuarantineAsync(file);   // 印が無い：何も起きない
            await RunXattr("-w", "com.apple.quarantine", "0081;00000000;Safari;", file);
            Assert.Contains("com.apple.quarantine", await RunXattr(file));

            await new HookCheck().ClearQuarantineAsync(file);

            Assert.DoesNotContain("com.apple.quarantine", await RunXattr(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string WriteScript(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), "mk-probe-" + Guid.NewGuid().ToString("N") + ".sh");
        File.WriteAllText(path, body);
        if (!OperatingSystem.IsWindows())   // mac 専用のテストからだけ呼ぶ。Windows には実行権限が無い
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static async Task<string> RunXattr(params string[] args)
    {
        var info = new System.Diagnostics.ProcessStartInfo("/usr/bin/xattr") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(info)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return output;
    }
}
