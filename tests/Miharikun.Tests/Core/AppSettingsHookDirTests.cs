using Miharikun.Core.Settings;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

/// <summary>Issue #17 Phase 34-1：settings.json の hookDir（Cursor の Hook exe の置き場所）。</summary>
public sealed class AppSettingsHookDirTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mk-hookdir-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private AppSettingsStore Store(Action<string>? log = null) => new(new AppPaths(_dir), log);

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, json);
    }

    [Fact]
    public void Missing_file_or_key_or_empty_value_is_null_without_a_log()
    {
        var logs = new List<string>();
        Assert.Null(Store(logs.Add).LoadHookDir());

        WriteRaw("""{"theme":"dark"}""");
        Assert.Null(Store(logs.Add).LoadHookDir());

        WriteRaw("""{"hookDir":""}""");
        Assert.Null(Store(logs.Add).LoadHookDir());

        Assert.Empty(logs);
    }

    [Theory]
    [InlineData("""{"hookDir":123}""")]
    [InlineData("""{"hookDir":true}""")]
    [InlineData("""{"hookDir":"relative\\dir"}""")]
    [InlineData("""{"hookDir":"C:\\dev\u0000x"}""")]
    public void An_unusable_value_is_null_and_logged_once(string json)
    {
        WriteRaw(json);
        var logs = new List<string>();

        Assert.Null(Store(logs.Add).LoadHookDir());
        Assert.Single(logs, l => l.Contains("hookDir"));
    }

    [Fact]
    public void A_full_path_is_returned_normalised_without_a_trailing_separator()
    {
        WriteRaw("""{"hookDir":"C:\\dev\\hook\\"}""");
        var withSlash = Store().LoadHookDir();
        WriteRaw("""{"hookDir":"C:\\dev\\hook"}""");

        Assert.Equal(@"C:\dev\hook", Store().LoadHookDir());
        Assert.Equal(Store().LoadHookDir(), withSlash);
    }

    [Fact]
    public void Saved_value_is_read_back_and_other_keys_survive()
    {
        WriteRaw("""{"theme":"dark","runningTimeoutMinutes":25,"unknown":{"a":1}}""");

        Assert.True(Store().SaveHookDir(@"C:\dev\hook"));

        Assert.Equal(@"C:\dev\hook", Store().LoadHookDir());
        Assert.Equal(AppTheme.Dark, Store().LoadTheme());
        Assert.Equal(25, Store().LoadRunningTimeoutMinutes());
        Assert.Contains("\"unknown\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Saving_null_removes_the_key_and_keeps_the_others()
    {
        WriteRaw("""{"theme":"dark","hookDir":"C:\\dev\\hook"}""");

        Assert.True(Store().SaveHookDir(null));

        Assert.Null(Store().LoadHookDir());
        Assert.Equal(AppTheme.Dark, Store().LoadTheme());
        Assert.DoesNotContain("hookDir", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Saving_null_without_a_file_or_a_key_writes_nothing()
    {
        Assert.True(Store().SaveHookDir(null));
        Assert.False(Directory.Exists(_dir));

        WriteRaw("""{"theme":"dark"}""");
        var text = File.ReadAllText(SettingsPath);
        var before = File.GetLastWriteTimeUtc(SettingsPath);

        Assert.True(Store().SaveHookDir(null));

        Assert.Equal(text, File.ReadAllText(SettingsPath));
        Assert.Equal(before, File.GetLastWriteTimeUtc(SettingsPath));
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_not_written_and_reports_false()
    {
        WriteRaw("""{"theme":"dark"}""");
        var logs = new List<string>();

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(Store(logs.Add).SaveHookDir(@"C:\dev\hook"));
            Assert.False(Store(logs.Add).SaveHookDir(null));
        }

        Assert.Equal("""{"theme":"dark"}""", File.ReadAllText(SettingsPath));
    }
}
