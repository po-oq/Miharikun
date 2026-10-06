using System.Text.Json.Nodes;
using Miharikun.Core.Install;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

/// <summary>Issue #17 Phase 34-1：Hook exe の置き場所（hookDir）、登録の書き方の違いの受け入れ、別の場所の登録の検出。</summary>
public sealed class HookInstallerPlacementTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-place-" + Guid.NewGuid().ToString("N"));
    private readonly string _hooksJson;
    private readonly string _installed;
    private readonly string _bundled;
    private readonly string _other;   // 別の場所（exe あり）

    public HookInstallerPlacementTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "cursor"));
        Directory.CreateDirectory(Path.Combine(_dir, "app"));
        Directory.CreateDirectory(Path.Combine(_dir, "dev"));
        _hooksJson = Path.Combine(_dir, "cursor", "hooks.json");
        _installed = Path.Combine(_dir, "data", "bin", HookInstaller.HookExeName);
        _bundled = Path.Combine(_dir, "app", HookInstaller.HookExeName);
        _other = Path.Combine(_dir, "dev", HookInstaller.HookExeName);
        File.WriteAllText(_bundled, "fake hook exe v1");
        File.WriteAllText(_other, "fake hook exe v1");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private HookInstaller Installer() => new(_hooksJson, _installed, _bundled);

    private static string Win(string path) => path.Replace('/', '\\');

    private JsonObject ReadJson() => JsonNode.Parse(File.ReadAllText(_hooksJson))!.AsObject();

    /// <summary>全イベントに同じコマンドを 1 つずつ登録した hooks.json を書く。</summary>
    private void WriteAll(Func<string, string> command)
    {
        var hooks = new JsonObject();
        foreach (var evt in HookInstaller.Events)
        {
            JsonNode entry = new JsonObject { ["command"] = command(evt), ["timeout"] = 5 };
            hooks[evt] = new JsonArray(entry);
        }
        File.WriteAllText(_hooksJson, new JsonObject { ["version"] = 1, ["hooks"] = hooks }.ToJsonString());
    }

    private void WriteAll(string command) => WriteAll(_ => command);

    private void Edit(Action<JsonObject> change)
    {
        var json = ReadJson();
        change(json["hooks"]!.AsObject());
        File.WriteAllText(_hooksJson, json.ToJsonString());
    }

    // ---------------------------------------------------------------- TryParseExePath

    [Theory]
    [InlineData("C:/dev/Miharikun.Hook.exe --agent cursor", @"C:\dev\Miharikun.Hook.exe")]
    [InlineData(@"C:\dev\Miharikun.Hook.exe --agent cursor", @"C:\dev\Miharikun.Hook.exe")]
    [InlineData("\"C:/my dev/Miharikun.Hook.exe\" --agent cursor", @"C:\my dev\Miharikun.Hook.exe")]
    [InlineData("C:/my dev/Miharikun.Hook.exe --agent cursor", @"C:\my dev\Miharikun.Hook.exe")]   // 引用符なしの手書き
    [InlineData("C:/dev/Miharikun.Hook.exe", @"C:\dev\Miharikun.Hook.exe")]                         // 引数なし
    [InlineData("  C:/dev/Miharikun.Hook.exe  --agent  cursor", @"C:\dev\Miharikun.Hook.exe")]
    [InlineData("C:/dev/miharikun.hook.EXE --agent cursor", @"C:\dev\miharikun.hook.EXE")]
    public void TryParseExePath_reads_every_way_the_command_can_be_written(string command, string expected) =>
        Assert.Equal(expected, HookInstaller.TryParseExePath(command));

    [Theory]
    [InlineData("C:/dev/other.exe --agent cursor")]               // Miharikun 以外
    [InlineData("dev/Miharikun.Hook.exe --agent cursor")]         // 相対パス
    [InlineData("Miharikun.Hook.exe --agent cursor")]
    [InlineData("\"C:/dev/Miharikun.Hook.exe --agent cursor")]    // 引用符が閉じていない
    [InlineData("C:/de\u0000v/Miharikun.Hook.exe --agent cursor")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseExePath_is_null_when_it_is_not_a_full_path_to_our_exe(string? command) =>
        Assert.Null(HookInstaller.TryParseExePath(command));

    // ---------------------------------------------------------------- SameRegistration

    [Theory]
    [InlineData("C:/dev/Miharikun.Hook.exe --agent cursor")]
    [InlineData(@"C:\dev\Miharikun.Hook.exe --agent cursor")]
    [InlineData("\"C:/dev/Miharikun.Hook.exe\" --agent cursor")]      // 空白なしの引用符
    [InlineData("c:/DEV/miharikun.hook.exe --agent cursor")]
    [InlineData("C:/dev/Miharikun.Hook.exe   --agent   cursor")]
    public void SameRegistration_accepts_writing_differences(string command) =>
        Assert.True(HookInstaller.SameRegistration(command, @"C:\dev\Miharikun.Hook.exe"));

    [Theory]
    [InlineData("C:/dev/Miharikun.Hook.exe")]                   // 引数なし
    [InlineData("C:/dev/Miharikun.Hook.exe --agent")]
    [InlineData("C:/dev/Miharikun.Hook.exe --agent claude")]
    [InlineData("C:/dev/Miharikun.Hook.exe cursor --agent")]
    [InlineData("C:/else/Miharikun.Hook.exe --agent cursor")]   // 別の場所
    [InlineData(null)]
    public void SameRegistration_rejects_a_missing_agent_argument_and_other_places(string? command) =>
        Assert.False(HookInstaller.SameRegistration(command, @"C:\dev\Miharikun.Hook.exe"));

    // ---------------------------------------------------------------- GetState（書き方の違い）

    [Fact]
    public void State_is_installed_when_only_the_way_of_writing_differs()
    {
        Installer().Install();
        Assert.Equal(HookInstallState.Installed, Installer().GetState());

        WriteAll(_ => Win(HookInstaller.BuildCommand(_installed)));
        Assert.Equal(HookInstallState.Installed, Installer().GetState());

        WriteAll(_ => HookInstaller.BuildCommand(_installed).ToUpperInvariant().Replace("--AGENT CURSOR", "--agent  cursor"));
        Assert.Equal(HookInstallState.Installed, Installer().GetState());
    }

    [Fact]
    public void State_is_partial_when_the_agent_argument_is_missing_in_all_13()
    {
        Installer().Install();
        WriteAll(_ => HookInstaller.BuildCommand(_installed).Replace(" --agent cursor", ""));

        Assert.Equal(HookInstallState.Partial, Installer().GetState());
    }

    [Fact]
    public void Install_still_rewrites_a_registration_that_is_written_differently()
    {
        Installer().Install();
        WriteAll(_ => Win(HookInstaller.BuildCommand(_installed)));

        var result = Installer().Install();

        Assert.True(result.Success, result.Message);
        Assert.Equal(HookInstaller.BuildCommand(_installed), (string)ReadJson()["hooks"]!["stop"]![0]!["command"]!);
    }

    // ---------------------------------------------------------------- FindRegisteredElsewhere（7.1 の表）

    private string OtherCommand => HookInstaller.BuildCommand(_other);

    [Fact]
    public void Elsewhere_is_null_when_everything_points_at_the_configured_place()
    {
        Installer().Install();
        Assert.Null(Installer().FindRegisteredElsewhere());

        WriteAll(_ => Win(HookInstaller.BuildCommand(_installed)));   // 書き方だけ違う
        Assert.Null(Installer().FindRegisteredElsewhere());
    }

    [Fact]
    public void Elsewhere_returns_the_exe_when_all_13_point_at_one_other_existing_place()
    {
        WriteAll(OtherCommand);

        Assert.Equal(Path.GetFullPath(_other), Installer().FindRegisteredElsewhere(), ignoreCase: true);
    }

    [Fact]
    public void Elsewhere_ignores_backslashes_quotes_and_case()
    {
        var plain = OtherCommand.Replace(" --agent cursor", "");
        WriteAll(evt => (evt.Length % 3) switch
        {
            0 => Win(OtherCommand),
            1 => $"\"{plain}\" --agent cursor",
            _ => OtherCommand.ToUpperInvariant().Replace("--AGENT CURSOR", "--agent cursor"),
        });

        Assert.NotNull(Installer().FindRegisteredElsewhere());
    }

    [Fact]
    public void Elsewhere_is_null_when_one_of_the_commands_has_no_agent_argument()
    {
        WriteAll(evt => evt == "stop" ? OtherCommand.Replace(" --agent cursor", "") : OtherCommand);

        Assert.Null(Installer().FindRegisteredElsewhere());
    }

    [Fact]
    public void Elsewhere_is_null_when_a_path_cannot_be_read()
    {
        WriteAll(evt => evt == "stop" ? "dev/Miharikun.Hook.exe --agent cursor" : OtherCommand);

        Assert.Null(Installer().FindRegisteredElsewhere());
    }

    [Fact]
    public void Elsewhere_is_null_when_the_exe_does_not_exist()
    {
        File.Delete(_other);
        WriteAll(OtherCommand);

        Assert.Null(Installer().FindRegisteredElsewhere());
    }

    [Fact]
    public void Elsewhere_is_null_when_some_events_have_no_registration()
    {
        WriteAll(OtherCommand);
        Edit(hooks => hooks.Remove("preCompact"));

        Assert.Null(Installer().FindRegisteredElsewhere());
    }

    [Fact]
    public void Elsewhere_is_null_when_the_places_are_scattered()
    {
        var second = Path.Combine(_dir, "dev2", HookInstaller.HookExeName);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllText(second, "x");
        WriteAll(evt => evt == "stop" ? HookInstaller.BuildCommand(second) : OtherCommand);

        Assert.Null(Installer().FindRegisteredElsewhere());
    }

    [Fact]
    public void Elsewhere_treats_two_entries_of_ours_at_the_same_place_as_that_place()
    {
        WriteAll(OtherCommand);
        Edit(hooks => hooks["stop"]!.AsArray().Add((JsonNode)new JsonObject { ["command"] = Win(OtherCommand) }));

        Assert.NotNull(Installer().FindRegisteredElsewhere());
    }

    [Fact]
    public void Elsewhere_is_null_when_one_event_has_two_entries_at_different_places()
    {
        WriteAll(OtherCommand);
        Edit(hooks => hooks["stop"]!.AsArray().Add((JsonNode)new JsonObject { ["command"] = HookInstaller.BuildCommand(_installed) }));

        Assert.Null(Installer().FindRegisteredElsewhere());
    }

    [Fact]
    public void Elsewhere_ignores_other_tools_entries_in_the_same_event()
    {
        WriteAll(OtherCommand);
        Edit(hooks => hooks["stop"]!.AsArray().Insert(0, (JsonNode)new JsonObject { ["command"] = "C:/tools/other.exe --x" }));

        Assert.NotNull(Installer().FindRegisteredElsewhere());
    }

    [Fact]
    public void Elsewhere_is_null_without_a_file_with_our_entries_or_with_a_broken_file()
    {
        Assert.Null(Installer().FindRegisteredElsewhere());

        File.WriteAllText(_hooksJson, """{"hooks":{"stop":[{"command":"C:/x/other.exe"}]}}""");
        Assert.Null(Installer().FindRegisteredElsewhere());

        File.WriteAllText(_hooksJson, "{ broken");
        Assert.Null(Installer().FindRegisteredElsewhere());
    }

    // ---------------------------------------------------------------- CreateDefault（hookDir）と置き場所

    private AppPaths Paths => new(Path.Combine(_dir, "data"));

    [Fact]
    public void DefaultHookDir_is_bin_under_the_data_root() =>
        Assert.Equal(Path.Combine(_dir, "data", "bin"), HookInstaller.DefaultHookDir(Paths));

    [Fact]
    public void ToSettingValue_is_null_for_the_default_in_any_spelling_and_normalised_otherwise()
    {
        var bin = HookInstaller.DefaultHookDir(Paths);

        Assert.Null(HookInstaller.ToSettingValue(bin, Paths));
        Assert.Null(HookInstaller.ToSettingValue(bin + @"\", Paths));
        Assert.Null(HookInstaller.ToSettingValue(bin.ToUpperInvariant(), Paths));
        Assert.Equal(Path.Combine(_dir, "dev"), HookInstaller.ToSettingValue(Path.Combine(_dir, "dev") + @"\", Paths));
    }

    [Fact]
    public void CreateDefault_with_a_hookDir_installs_into_that_folder()
    {
        var installer = HookInstaller.CreateDefault(Paths, Path.Combine(_dir, "app"), Path.Combine(_dir, "dev"));

        Assert.Equal(_other, installer.InstalledExePath);
    }

    [Fact]
    public void CreateDefault_without_a_hookDir_uses_the_default_bin()
    {
        var installer = HookInstaller.CreateDefault(Paths, Path.Combine(_dir, "app"), null);

        Assert.Equal(_installed, installer.InstalledExePath);
    }

    [Fact]
    public void Install_into_a_configured_folder_that_is_gone_fails_and_creates_nothing()
    {
        var missing = Path.Combine(_dir, "gone");
        var installer = new HookInstaller(_hooksJson, Path.Combine(missing, HookInstaller.HookExeName), _bundled,
            createExeDirectory: false);

        var result = installer.Install();

        Assert.False(result.Success);
        Assert.Contains(missing, result.Message);
        Assert.Contains("設定", result.Message);
        Assert.False(Directory.Exists(missing));
        Assert.False(File.Exists(_hooksJson));
    }

    [Fact]
    public void Install_into_a_configured_folder_that_exists_works_and_getstate_is_installed()
    {
        File.Delete(_other);
        var installer = new HookInstaller(_hooksJson, _other, _bundled, createExeDirectory: false);

        Assert.True(installer.Install().Success);
        Assert.Equal(HookInstallState.Installed, installer.GetState());
        Assert.Equal("fake hook exe v1", File.ReadAllText(_other));
    }

    [Fact]
    public void Install_into_the_default_folder_still_creates_it()
    {
        Assert.False(Directory.Exists(Path.GetDirectoryName(_installed)));

        Assert.True(Installer().Install().Success);

        Assert.True(File.Exists(_installed));
    }
}
