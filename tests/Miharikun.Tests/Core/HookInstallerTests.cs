using System.Text.Json.Nodes;
using Miharikun.Core.Install;

namespace Miharikun.Tests.Core;

public sealed class HookInstallerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 13, 45, 6, TimeSpan.FromHours(9));

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-install-" + Guid.NewGuid().ToString("N"));
    private readonly string _hooksJson;
    private readonly string _installed;
    private readonly string _bundled;

    public HookInstallerTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "cursor"));
        Directory.CreateDirectory(Path.Combine(_dir, "app"));
        _hooksJson = Path.Combine(_dir, "cursor", "hooks.json");
        _installed = Path.Combine(_dir, "data", "bin", HookInstaller.HookExeName);
        _bundled = Path.Combine(_dir, "app", HookInstaller.HookExeName);
        File.WriteAllText(_bundled, "fake hook exe v1");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private HookInstaller Installer(bool withBundled = true) =>
        new(_hooksJson, _installed, withBundled ? _bundled : null, () => Now);

    private JsonObject Read() => JsonNode.Parse(File.ReadAllText(_hooksJson))!.AsObject();

    private string[] Backups() => Directory.GetFiles(Path.GetDirectoryName(_hooksJson)!, "hooks.json.bak-*");

    private static string Expected(string exe) => HookInstaller.BuildCommand(exe);

    // ---------------------------------------------------------------- 新規導入

    [Fact]
    public void Fresh_install_creates_hooks_json_with_every_event_and_copies_the_exe()
    {
        var result = Installer().Install();

        Assert.True(result.Success, result.Message);
        Assert.Null(result.BackupPath);   // 元のファイルが無いのでバックアップなし
        Assert.Empty(Backups());
        Assert.Equal("fake hook exe v1", File.ReadAllText(_installed));

        var json = Read();
        Assert.Equal(1, (int)json["version"]!);
        var hooks = json["hooks"]!.AsObject();
        Assert.Equal(HookInstaller.Events, hooks.Select(p => p.Key));
        foreach (var evt in HookInstaller.Events)
        {
            var entry = Assert.Single(hooks[evt]!.AsArray())!.AsObject();
            Assert.Equal(Expected(_installed), (string)entry["command"]!);
            Assert.Equal(5, (int)entry["timeout"]!);
        }
        Assert.Equal(HookInstallState.Installed, Installer().GetState());
    }

    [Fact]
    public void Command_uses_forward_slashes_and_the_agent_argument()
    {
        var command = HookInstaller.BuildCommand(@"C:\Users\boss\AppData\Local\Miharikun\bin\Miharikun.Hook.exe");

        Assert.Equal("C:/Users/boss/AppData/Local/Miharikun/bin/Miharikun.Hook.exe --agent cursor", command);
    }

    [Fact]
    public void Command_quotes_a_path_with_spaces()
    {
        var command = HookInstaller.BuildCommand(@"C:\Users\Boss Name\AppData\Local\Miharikun\bin\Miharikun.Hook.exe");

        Assert.Equal("\"C:/Users/Boss Name/AppData/Local/Miharikun/bin/Miharikun.Hook.exe\" --agent cursor", command);
        Assert.True(HookInstaller.IsOurs(command));
    }

    [Theory]
    [InlineData("C:/x/Miharikun.Hook.exe --agent cursor", true)]
    [InlineData("\"C:/a b/miharikun.hook.EXE\" --agent cursor", true)]
    [InlineData("C:/x/MiharikunDump.exe", false)]            // Step 0 のダンプ用は別物
    [InlineData("node ./other-hook.js", false)]
    [InlineData(null, false)]
    public void Recognises_only_its_own_entries(string? command, bool expected) =>
        Assert.Equal(expected, HookInstaller.IsOurs(command));

    // ---------------------------------------------------------------- 既存設定を壊さない

    [Fact]
    public void Merge_keeps_every_existing_setting_and_adds_ours_after_theirs()
    {
        var original = """
        {
          "version": 1,
          "note": "日本語のメモ",
          "hooks": {
            "stop": [ { "command": "C:/tools/notify.exe", "timeout": 30, "extra": { "a": [1, 2] } } ],
            "beforeShellExecution": [ { "command": "C:/tools/guard.exe" } ],
            "afterFileEdit": []
          }
        }
        """;
        File.WriteAllText(_hooksJson, original);

        var result = Installer().Install();

        Assert.True(result.Success, result.Message);
        var json = Read();
        Assert.Equal("日本語のメモ", (string)json["note"]!);
        var hooks = json["hooks"]!.AsObject();

        var stop = hooks["stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("C:/tools/notify.exe", (string)stop[0]!["command"]!);          // 既存が先
        Assert.Equal(30, (int)stop[0]!["timeout"]!);
        Assert.Equal("[1,2]", stop[0]!["extra"]!["a"]!.ToJsonString());             // 未知のフィールドも保持
        Assert.Equal(Expected(_installed), (string)stop[1]!["command"]!);

        Assert.Equal("C:/tools/guard.exe", (string)Assert.Single(hooks["beforeShellExecution"]!.AsArray())!["command"]!);
        Assert.Equal(Expected(_installed), (string)Assert.Single(hooks["afterFileEdit"]!.AsArray())!["command"]!);
        Assert.Contains("日本語のメモ", File.ReadAllText(_hooksJson));               // \uXXXX にしない
    }

    [Fact]
    public void Existing_file_is_backed_up_byte_for_byte_before_it_changes()
    {
        var original = "{ \"hooks\": { \"stop\": [ { \"command\": \"C:/tools/notify.exe\" } ] } }";
        File.WriteAllText(_hooksJson, original);

        var result = Installer().Install();

        var backup = Assert.Single(Backups());
        Assert.Equal(backup, result.BackupPath);
        Assert.EndsWith("hooks.json.bak-20261003-134506", backup);
        Assert.Equal(original, File.ReadAllText(backup));
    }

    [Fact]
    public void Missing_version_is_set_to_1_and_an_existing_one_is_kept()
    {
        File.WriteAllText(_hooksJson, "{ \"hooks\": {} }");
        Installer().Install();
        Assert.Equal(1, (int)Read()["version"]!);

        File.WriteAllText(_hooksJson, "{ \"version\": 2, \"hooks\": {} }");
        Installer().Install();
        Assert.Equal(2, (int)Read()["version"]!);
    }

    [Fact]
    public void Other_tools_entries_that_look_similar_are_not_touched()
    {
        File.WriteAllText(_hooksJson, """{"hooks":{"stop":[{"command":"C:/x/MiharikunDump.exe"}]}}""");

        Installer().Install();

        var stop = Read()["hooks"]!["stop"]!.AsArray();
        Assert.Equal(["C:/x/MiharikunDump.exe", Expected(_installed)], stop.Select(e => (string)e!["command"]!));
    }

    [Fact]
    public void Output_is_utf8_without_bom_and_indented()
    {
        Installer().Install();

        var bytes = File.ReadAllBytes(_hooksJson);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Contains("\n", File.ReadAllText(_hooksJson).TrimEnd());
    }

    // ---------------------------------------------------------------- 再導入

    [Fact]
    public void Installing_twice_changes_nothing_the_second_time()
    {
        Installer().Install();
        var before = File.ReadAllBytes(_hooksJson);

        var again = Installer().Install();

        Assert.True(again.Success);
        Assert.Contains("導入済み", again.Message);
        Assert.Equal(before, File.ReadAllBytes(_hooksJson));
        Assert.Empty(Backups());   // 変更がないのでバックアップも作らない
        Assert.All(Read()["hooks"]!.AsObject(), p => Assert.Single(p.Value!.AsArray()));
    }

    [Fact]
    public void A_stale_path_is_corrected_in_place_without_adding_a_second_entry()
    {
        File.WriteAllText(_hooksJson, """
        {"version":1,"hooks":{"stop":[
          {"command":"C:/old/place/Miharikun.Hook.exe --agent cursor","timeout":9},
          {"command":"C:/other/tool.exe"}
        ]}}
        """);

        Installer().Install();

        var stop = Read()["hooks"]!["stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal(Expected(_installed), (string)stop[0]!["command"]!);
        Assert.Equal(9, (int)stop[0]!["timeout"]!);   // 利用者が変えた timeout は尊重する
        Assert.Equal("C:/other/tool.exe", (string)stop[1]!["command"]!);
    }

    [Fact]
    public void Duplicate_entries_of_ours_are_collapsed_to_one()
    {
        var c = Expected(_installed);
        File.WriteAllText(_hooksJson, "{\"hooks\":{\"stop\":[{\"command\":\"" + c + "\"},{\"command\":\"" + c + "\"},{\"command\":\"x.exe\"}]}}");

        Installer().Install();

        Assert.Equal([c, "x.exe"], Read()["hooks"]!["stop"]!.AsArray().Select(e => (string)e!["command"]!));
    }

    [Fact]
    public void A_newer_bundled_exe_replaces_the_installed_one()
    {
        Installer().Install();
        File.WriteAllText(_bundled, "fake hook exe v2 - longer");
        Assert.Equal(HookInstallState.ExeOutdated, Installer().GetState());

        var result = Installer().Install();

        Assert.True(result.Success);
        Assert.Contains("更新", result.Message);
        Assert.Equal("fake hook exe v2 - longer", File.ReadAllText(_installed));
        Assert.Equal(HookInstallState.Installed, Installer().GetState());
    }

    // ---------------------------------------------------------------- 状態

    [Fact]
    public void State_is_not_installed_without_a_file_or_without_our_entries()
    {
        Assert.Equal(HookInstallState.NotInstalled, Installer().GetState());

        File.WriteAllText(_hooksJson, """{"hooks":{"stop":[{"command":"x.exe"}]}}""");
        Assert.Equal(HookInstallState.NotInstalled, Installer().GetState());
    }

    [Fact]
    public void State_is_partial_when_some_events_are_missing_or_the_path_is_old()
    {
        Installer().Install();
        var json = Read();
        json["hooks"]!.AsObject().Remove("preCompact");
        File.WriteAllText(_hooksJson, json.ToJsonString());
        Assert.Equal(HookInstallState.Partial, Installer().GetState());

        Installer().Install();
        Assert.Equal(HookInstallState.Installed, Installer().GetState());

        var moved = new HookInstaller(_hooksJson, Path.Combine(_dir, "elsewhere", HookInstaller.HookExeName), _bundled);
        Assert.Equal(HookInstallState.Partial, moved.GetState());   // 導入先が変わった
    }

    [Fact]
    public void State_is_exe_outdated_when_the_installed_exe_is_gone()
    {
        Installer().Install();
        File.Delete(_installed);

        Assert.Equal(HookInstallState.ExeOutdated, Installer().GetState());
        Installer().Install();
        Assert.Equal("fake hook exe v1", File.ReadAllText(_installed));
    }

    [Fact]
    public void Without_a_bundled_exe_an_installed_one_is_accepted_but_install_is_refused()
    {
        Installer().Install();

        Assert.Equal(HookInstallState.Installed, Installer(withBundled: false).GetState());
        Assert.False(Installer(withBundled: false).CanInstall);

        var result = new HookInstaller(_hooksJson, Path.Combine(_dir, "x", "y.exe"), null).Install();
        Assert.False(result.Success);
        Assert.Contains(HookInstaller.HookExeName, result.Message);
    }

    // ---------------------------------------------------------------- 読めないファイルは触らない

    [Theory]
    [InlineData("{ これは JSON ではない")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"hooks\": []}")]
    [InlineData("{\"hooks\": {\"stop\": {\"command\": \"x\"}}}")]
    [InlineData("{ \"version\": 1, // コメントは書き戻すと消えるので触らない\n \"hooks\": {} }")]
    public void Unusable_files_are_never_modified_or_backed_up(string content)
    {
        File.WriteAllText(_hooksJson, content);

        var result = Installer().Install();

        Assert.False(result.Success);
        Assert.Equal(content, File.ReadAllText(_hooksJson));
        Assert.Empty(Backups());
        Assert.False(File.Exists(_installed));   // exe も配置しない
    }

    [Fact]
    public void State_of_a_broken_file_is_unreadable_and_uninstall_leaves_it_alone()
    {
        File.WriteAllText(_hooksJson, "{ broken");

        Assert.Equal(HookInstallState.Unreadable, Installer().GetState());
        var result = Installer().Uninstall();

        Assert.False(result.Success);
        Assert.Equal("{ broken", File.ReadAllText(_hooksJson));
        Assert.Empty(Backups());
    }

    [Fact]
    public void An_empty_file_is_treated_as_a_new_one()
    {
        File.WriteAllText(_hooksJson, "  \n");

        Assert.True(Installer().Install().Success);
        Assert.Equal(HookInstallState.Installed, Installer().GetState());
    }

    // ---------------------------------------------------------------- 削除

    [Fact]
    public void Uninstall_removes_only_our_entries_and_backs_up_first()
    {
        File.WriteAllText(_hooksJson, """
        {"version":1,"hooks":{
          "stop":[{"command":"C:/tools/notify.exe"}],
          "beforeReadFile":[],
          "beforeShellExecution":[{"command":"C:/tools/guard.exe"}]
        }}
        """);
        Installer().Install();
        var afterInstall = File.ReadAllText(_hooksJson);

        var result = Installer().Uninstall();

        Assert.True(result.Success, result.Message);
        var hooks = Read()["hooks"]!.AsObject();
        Assert.Equal(["C:/tools/notify.exe"], hooks["stop"]!.AsArray().Select(e => (string)e!["command"]!));
        Assert.Equal("C:/tools/guard.exe", (string)hooks["beforeShellExecution"]![0]!["command"]!);
        Assert.NotNull(hooks["beforeReadFile"]);                   // 登録対象外のイベントの空配列には触れない
        Assert.Empty(hooks["beforeReadFile"]!.AsArray());
        Assert.Null(hooks["sessionStart"]);                        // 自分だけだった配列はキーごと消す
        Assert.DoesNotContain(HookInstaller.HookExeName, File.ReadAllText(_hooksJson));
        Assert.Equal(afterInstall, File.ReadAllText(Assert.Single(Backups())));   // 削除前の状態をバックアップ
        Assert.Equal(HookInstallState.NotInstalled, Installer().GetState());
    }

    [Fact]
    public void Install_then_uninstall_restores_the_original_settings()
    {
        var original = """{"version":1,"hooks":{"stop":[{"command":"C:/tools/notify.exe","timeout":30}]}}""";
        File.WriteAllText(_hooksJson, original);

        Installer().Install();
        Installer().Uninstall();

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), Read()));
    }

    [Fact]
    public void Uninstall_keeps_the_exe_and_is_a_noop_when_nothing_is_registered()
    {
        Installer().Install();
        Installer().Uninstall();
        Assert.True(File.Exists(_installed));

        var before = File.ReadAllBytes(_hooksJson);
        var backupsBefore = Backups().Length;

        var again = Installer().Uninstall();

        Assert.True(again.Success);
        Assert.Equal(before, File.ReadAllBytes(_hooksJson));
        Assert.Equal(backupsBefore, Backups().Length);
    }

    [Fact]
    public void Uninstall_without_a_file_succeeds_quietly()
    {
        var result = Installer().Uninstall();

        Assert.True(result.Success);
        Assert.False(File.Exists(_hooksJson));
    }

    [Fact]
    public void Default_locations_honour_the_cursor_dir_override()
    {
        var old = Environment.GetEnvironmentVariable(HookInstaller.CursorDirEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(HookInstaller.CursorDirEnvVar, Path.Combine(_dir, "fake-cursor"));
            var installer = HookInstaller.CreateDefault(new Miharikun.Core.Storage.AppPaths(Path.Combine(_dir, "data")), Path.Combine(_dir, "app"));

            Assert.Equal(Path.Combine(_dir, "fake-cursor", "hooks.json"), installer.HooksJsonPath);
            Assert.Equal(Path.Combine(_dir, "data", "bin", HookInstaller.HookExeName), installer.InstalledExePath);
            Assert.True(installer.CanInstall);   // app フォルダに同梱の exe がある
        }
        finally
        {
            Environment.SetEnvironmentVariable(HookInstaller.CursorDirEnvVar, old);
        }
    }
}