using System.Text.Json.Nodes;
using Miharikun.Core.Install;
using Miharikun.Core.Settings;
using Miharikun.Core.Storage;
using Miharikun.Services;

namespace Miharikun.Tests.Presentation;

/// <summary>28-3：Hook の導入の流れ（確認で「いいえ」なら何もしない）と、置き場所（Issue #17。起動時の受け入れ・ChangePlacement）。</summary>
public sealed class HookSetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-hooksetup-" + Guid.NewGuid().ToString("N"));
    private readonly string _hooksJson;
    private readonly string _bundled;
    private readonly string _other;
    private readonly AppPaths _paths;
    private readonly AppSettingsStore _settings;
    private readonly FakeUiServices _ui = new();
    private readonly FakeHookCheck _check = new();

    /// <summary>導入のあとの確認（隔離の印の解除・試しの起動）の代わり。呼ばれた順と、試しの結果を決められる。</summary>
    private sealed class FakeHookCheck : IHookCheck
    {
        public bool ProbeResult { get; set; } = true;
        public List<string> Calls { get; } = [];

        public Task ClearQuarantineAsync(string exePath)
        {
            Calls.Add("clear:" + exePath);
            return Task.CompletedTask;
        }

        public Task<bool> ProbeAsync(string exePath)
        {
            Calls.Add("probe:" + exePath);
            return Task.FromResult(ProbeResult);
        }
    }

    public HookSetupTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "cursor"));
        Directory.CreateDirectory(Path.Combine(_dir, "app"));
        Directory.CreateDirectory(Path.Combine(_dir, "dev"));
        _hooksJson = Path.Combine(_dir, "cursor", "hooks.json");
        _bundled = Path.Combine(_dir, "app", HookInstaller.HookExeName);
        _other = Path.Combine(_dir, "dev", HookInstaller.HookExeName);
        File.WriteAllText(_bundled, "fake hook exe v1");
        File.WriteAllText(_other, "fake hook exe v1");
        _paths = new AppPaths(Path.Combine(_dir, "data"));
        Directory.CreateDirectory(_paths.Root);
        _settings = new AppSettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private HookSetup Setup(bool withBundled = true) =>
        new(dir => new HookInstaller(_hooksJson, Path.Combine(dir ?? HookInstaller.DefaultHookDir(_paths), HookInstaller.HookExeName),
            withBundled ? _bundled : null), _settings, _paths, _ui, _check);

    private string DefaultExe => Path.Combine(HookInstaller.DefaultHookDir(_paths), HookInstaller.HookExeName);

    private void WriteAllEvents(string exe)
    {
        var hooks = new JsonObject();
        foreach (var evt in HookInstaller.Events)
        {
            JsonNode entry = new JsonObject { ["command"] = HookInstaller.BuildCommand(exe), ["timeout"] = 5 };
            hooks[evt] = new JsonArray(entry);
        }
        File.WriteAllText(_hooksJson, new JsonObject { ["version"] = 1, ["hooks"] = hooks }.ToJsonString());
    }

    // ---------------------------------------------------------------- 起動時の確認

    [Fact]
    public async Task Startup_offers_when_not_installed_and_No_changes_nothing()
    {
        _ui.ConfirmAnswer = false;

        await Setup().CheckAtStartupAsync();

        var (title, message) = Assert.Single(_ui.Confirms);
        Assert.Equal("Miharikun - Hook の導入", title);
        Assert.Contains("未導入", message);
        Assert.False(File.Exists(_hooksJson));
        Assert.False(File.Exists(DefaultExe));
        Assert.Empty(_ui.Messages);
    }

    [Fact]
    public async Task Startup_offer_Yes_installs_and_tells_the_result()
    {
        _ui.ConfirmAnswer = true;

        await Setup().CheckAtStartupAsync();

        Assert.True(File.Exists(_hooksJson));
        Assert.True(File.Exists(DefaultExe));
        var (_, message, kind) = Assert.Single(_ui.Messages);
        Assert.Equal(MessageKind.Information, kind);
        Assert.Contains("Cursor を再起動", message);
    }

    [Fact]
    public async Task Startup_does_nothing_when_already_installed()
    {
        _ui.ConfirmAnswer = true;
        var setup = Setup();
        await setup.CheckAtStartupAsync();   // 導入する
        _ui.Confirms.Clear();
        _ui.Messages.Clear();

        await Setup().CheckAtStartupAsync();

        Assert.Empty(_ui.Confirms);
        Assert.Empty(_ui.Messages);
    }

    [Fact]
    public async Task Startup_warns_and_does_not_touch_an_unreadable_hooks_json()
    {
        File.WriteAllText(_hooksJson, "{ これは JSON ではない");

        await Setup().CheckAtStartupAsync();

        var (_, message, kind) = Assert.Single(_ui.Messages);
        Assert.Equal(MessageKind.Warning, kind);
        Assert.Contains("読めない", message);
        Assert.Empty(_ui.Confirms);
        Assert.Equal("{ これは JSON ではない", File.ReadAllText(_hooksJson));
    }

    [Fact]
    public async Task Startup_does_not_offer_when_there_is_no_bundled_hook_exe()
    {
        await Setup(withBundled: false).CheckAtStartupAsync();

        Assert.Empty(_ui.Confirms);
        Assert.Empty(_ui.Messages);
    }

    [Fact]
    public async Task Startup_accepts_a_registration_in_another_place_without_a_dialog_and_saves_the_setting()
    {
        WriteAllEvents(_other);   // 全イベントが同じ別の場所（exe あり）

        var setup = Setup();
        await setup.CheckAtStartupAsync();

        Assert.Empty(_ui.Confirms);   // ダイアログなし
        Assert.Empty(_ui.Messages);
        Assert.Equal(Path.GetDirectoryName(_other), _settings.LoadHookDir());
        Assert.Equal(Path.GetDirectoryName(_other), setup.CurrentHookDir);
    }

    [Fact]
    public async Task Startup_still_uses_the_accepted_place_this_time_when_the_setting_cannot_be_saved()
    {
        WriteAllEvents(_other);
        File.WriteAllText(Path.Combine(_paths.Root, "settings.json"), """{"theme":"dark"}""");

        var setup = Setup();
        using (new FileStream(Path.Combine(_paths.Root, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await setup.CheckAtStartupAsync();

        Assert.Equal(Path.GetDirectoryName(_other), setup.CurrentHookDir);   // 保存できなくても今回はその場所
        Assert.Empty(_ui.Confirms);
    }

    // ---------------------------------------------------------------- 置き場所の変更

    [Fact]
    public async Task ChangePlacement_saves_asks_and_No_leaves_hooks_json_alone()
    {
        var newDir = Path.Combine(_dir, "newplace");
        Directory.CreateDirectory(newDir);
        _ui.ConfirmAnswer = false;
        var setup = Setup();

        await setup.ChangePlacementAsync(newDir);

        Assert.Equal(newDir, _settings.LoadHookDir());
        var (_, message) = Assert.Single(_ui.Confirms);
        Assert.Contains("導入し直しますか", message);
        Assert.Contains(newDir, message);
        Assert.False(File.Exists(_hooksJson));
        Assert.False(File.Exists(Path.Combine(newDir, HookInstaller.HookExeName)));
    }

    [Fact]
    public async Task ChangePlacement_Yes_installs_into_the_new_place()
    {
        var newDir = Path.Combine(_dir, "newplace");
        Directory.CreateDirectory(newDir);
        _ui.ConfirmAnswer = true;

        await Setup().ChangePlacementAsync(newDir);

        Assert.True(File.Exists(Path.Combine(newDir, HookInstaller.HookExeName)));
        Assert.Contains(newDir.Replace('\\', '/'), File.ReadAllText(_hooksJson).Replace("\\\\", "/").Replace('\\', '/'));
        Assert.Single(_ui.Messages, m => m.Kind == MessageKind.Information);
    }

    [Fact]
    public async Task ChangePlacement_tells_the_failure_to_save_and_does_not_ask()
    {
        File.WriteAllText(Path.Combine(_paths.Root, "settings.json"), """{"theme":"dark"}""");
        var setup = Setup();
        var before = setup.CurrentHookDir;

        using (new FileStream(Path.Combine(_paths.Root, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await setup.ChangePlacementAsync(Path.Combine(_dir, "newplace"));

        var (_, message, kind) = Assert.Single(_ui.Messages);
        Assert.Equal(MessageKind.Warning, kind);
        Assert.Contains("保存できませんでした", message);
        Assert.Empty(_ui.Confirms);
        Assert.Equal(before, setup.CurrentHookDir);   // 置き場所は変えない
    }

    // ---------------------------------------------------------------- メニュー

    [Fact]
    public async Task Install_from_the_menu_without_the_bundled_exe_tells_it_is_missing()
    {
        await Setup(withBundled: false).InstallFromMenuAsync();

        var (_, message, kind) = Assert.Single(_ui.Messages);
        Assert.Equal(MessageKind.Warning, kind);
        Assert.Contains("見つかりません", message);
        Assert.False(File.Exists(_hooksJson));
    }

    [Fact]
    public async Task Install_from_the_menu_installs_without_asking()
    {
        await Setup().InstallFromMenuAsync();

        Assert.Empty(_ui.Confirms);
        Assert.True(File.Exists(_hooksJson));
        Assert.Single(_ui.Messages, m => m.Kind == MessageKind.Information);
    }

    // ---------------------------------------------------------------- 導入のあとの確認（要件 8.1・計画 7.12）

    [Fact]
    public async Task After_an_install_the_quarantine_is_cleared_and_the_hook_is_probed_in_that_order()
    {
        await Setup().InstallFromMenuAsync();

        Assert.Equal(["clear:" + DefaultExe, "probe:" + DefaultExe], _check.Calls);
        Assert.Single(_ui.Messages, m => m.Kind == MessageKind.Information && m.Message.Contains("Cursor を再起動"));
    }

    [Fact]
    public async Task A_failed_probe_shows_the_guidance_as_a_warning_and_keeps_the_registration()
    {
        _check.ProbeResult = false;

        await Setup().InstallFromMenuAsync();

        var (_, message, kind) = Assert.Single(_ui.Messages);
        Assert.Equal(MessageKind.Warning, kind);
        Assert.Contains("Hook を起動できませんでした", message);
        Assert.DoesNotContain("Cursor を再起動", message);
        Assert.True(File.Exists(_hooksJson));   // 登録は戻さない（次の導入で直る）
    }

    [Fact]
    public async Task ChangePlacement_Yes_also_clears_the_quarantine_and_probes_the_new_place()
    {
        var newDir = Path.Combine(_dir, "newplace");
        Directory.CreateDirectory(newDir);
        _ui.ConfirmAnswer = true;

        await Setup().ChangePlacementAsync(newDir);

        var exe = Path.Combine(newDir, HookInstaller.HookExeName);
        Assert.Equal(["clear:" + exe, "probe:" + exe], _check.Calls);
    }

    [Fact]
    public async Task Nothing_is_probed_when_the_install_fails_or_when_the_startup_only_accepts_a_registered_place()
    {
        await Setup(withBundled: false).InstallFromMenuAsync();   // 同梱が無いので導入しない
        Assert.Empty(_check.Calls);

        var elsewhere = Path.Combine(_dir, "dev");
        WriteAllEvents(_other);
        await Setup().CheckAtStartupAsync();   // 登録の場所を受け入れるだけ（ダイアログも確認もしない）
        Assert.Empty(_check.Calls);
        Assert.Equal(elsewhere, Setup().CurrentHookDir);
    }

    [Fact]
    public async Task Uninstall_asks_first_and_No_changes_nothing()
    {
        await Setup().InstallFromMenuAsync();
        var installed = File.ReadAllText(_hooksJson);
        _ui.Messages.Clear();
        _ui.ConfirmAnswer = false;

        await Setup().UninstallFromMenuAsync();

        var (title, _) = Assert.Single(_ui.Confirms);
        Assert.Equal("Miharikun - Hook の削除", title);
        Assert.Equal(installed, File.ReadAllText(_hooksJson));
        Assert.Empty(_ui.Messages);
    }

    [Fact]
    public async Task Uninstall_Yes_removes_the_registration_and_tells_the_result()
    {
        await Setup().InstallFromMenuAsync();
        _ui.Messages.Clear();
        _ui.ConfirmAnswer = true;

        await Setup().UninstallFromMenuAsync();

        Assert.DoesNotContain("Miharikun.Hook", File.ReadAllText(_hooksJson));
        Assert.Single(_ui.Messages, m => m.Kind == MessageKind.Information);
    }
}
