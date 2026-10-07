using Miharikun.Core.Install;
using Miharikun.Core.Settings;

namespace Miharikun.Tests.Core;

/// <summary>Issue #17 Phase 35-1：設定画面の置き場所の入力の検査。</summary>
public sealed class HookDirInputTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mk-hookdirinput-" + Guid.NewGuid().ToString("N"));

    public HookDirInputTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void An_existing_full_path_is_accepted_with_or_without_a_trailing_separator()
    {
        Assert.Null(HookDirInput.Validate(_dir));
        Assert.Null(HookDirInput.Validate(_dir + Path.DirectorySeparatorChar));
        Assert.Null(HookDirInput.Validate("  " + _dir + "  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_value_cannot_be_saved(string? text) =>
        Assert.Equal("フォルダを指定してください", HookDirInput.Validate(text));

    [Theory]
    [InlineData(@"relative\dir")]
    [InlineData(@"..\dir")]
    [InlineData("C:\\de\u0000v")]
    public void A_relative_path_or_unusable_characters_cannot_be_saved(string text) =>
        Assert.Equal(HookWording.NotAbsoluteMessage, HookDirInput.Validate(text));

    [WindowsFact]
    public void The_message_for_a_relative_path_names_the_drive_on_windows() =>
        Assert.Equal("C:\\ から始まるフォルダを指定してください", HookDirInput.Validate("dir"));

    [MacFact]
    public void The_message_for_a_relative_path_names_the_root_on_mac()
    {
        Assert.Equal("/ から始まるフォルダを指定してください", HookDirInput.Validate("dir"));
        Assert.Equal("/ から始まるフォルダを指定してください", HookDirInput.Validate("~/dir"));   // ~ は展開しない
    }

    [Fact]
    public void A_missing_folder_cannot_be_saved_and_is_not_created()
    {
        var missing = Path.Combine(_dir, "nothing");

        Assert.Equal("フォルダが見つかりません", HookDirInput.Validate(missing));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void A_file_is_not_a_folder()
    {
        var file = Path.Combine(_dir, "a.txt");
        File.WriteAllText(file, "x");

        Assert.Equal("フォルダが見つかりません", HookDirInput.Validate(file));
    }

    [Fact]
    public void TryNormalize_drops_the_trailing_separator()
    {
        Assert.True(HookDirInput.TryNormalize(_dir + Path.DirectorySeparatorChar, out var dir));
        Assert.Equal(_dir, dir);
    }

    [Fact]
    public void The_default_place_may_be_missing_because_the_app_creates_it_but_other_missing_folders_may_not()
    {
        var defaultDir = Path.Combine(_dir, "default-bin");

        Assert.Null(HookDirInput.Validate(defaultDir, defaultDir));
        Assert.Null(HookDirInput.Validate(defaultDir.ToUpperInvariant() + Path.DirectorySeparatorChar, defaultDir));
        Assert.Equal("フォルダが見つかりません", HookDirInput.Validate(Path.Combine(_dir, "other"), defaultDir));
        Assert.False(Directory.Exists(defaultDir));
    }
}
