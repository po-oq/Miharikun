using Miharikun.Core.Settings;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mk-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private AppSettingsStore Store(Action<string>? log = null) => new(new AppPaths(_dir), log);

    [Fact]
    public void Missing_file_follows_the_system_theme()
    {
        Assert.Equal(AppTheme.System, Store().LoadTheme());
    }

    [Theory]
    [InlineData(AppTheme.System)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void Saved_theme_is_read_back(AppTheme mode)
    {
        Store().SaveTheme(mode);
        Assert.Equal(mode, Store().LoadTheme());
    }

    [Fact]
    public void Broken_or_unknown_value_falls_back_to_system_and_is_logged()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        var logs = new List<string>();
        Assert.Equal(AppTheme.System, Store(logs.Add).LoadTheme());
        Assert.NotEmpty(logs);

        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{"theme":"purple"}""");
        Assert.Equal(AppTheme.System, Store().LoadTheme());
    }

    // ---- runningTimeoutMinutes（Phase 19-4） ----

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, json);
    }

    [Fact]
    public void Running_timeout_defaults_to_10_minutes_when_missing()
    {
        Assert.Equal(10, AppSettingsStore.DefaultRunningTimeoutMinutes);
        Assert.Equal(10, Store().LoadRunningTimeoutMinutes());
    }

    [Theory]
    [InlineData("""{"theme":"dark"}""")]                       // 欠けている
    [InlineData("""{"runningTimeoutMinutes":"15"}""")]         // 文字
    [InlineData("""{"runningTimeoutMinutes":10.5}""")]         // 整数以外
    [InlineData("""{"runningTimeoutMinutes":null}""")]
    [InlineData("""{"runningTimeoutMinutes":[15]}""")]
    [InlineData("{ not json")]                                 // 壊れている
    [InlineData("[1,2]")]
    public void Running_timeout_falls_back_to_the_default_for_missing_or_bad_values(string json)
    {
        WriteRaw(json);

        Assert.Equal(10, Store().LoadRunningTimeoutMinutes());
    }

    [Theory]
    [InlineData(15)]
    [InlineData(1)]
    [InlineData(0)]     // 0 以下は「無効」の意味で、そのまま返す
    [InlineData(-5)]
    public void Running_timeout_is_read_back(int minutes)
    {
        Store().SaveRunningTimeoutMinutes(minutes);

        Assert.Equal(minutes, Store().LoadRunningTimeoutMinutes());
    }

    [Fact]
    public void Saving_the_timeout_keeps_the_theme_and_unknown_keys()
    {
        WriteRaw("""{"theme":"dark","somethingNew":{"a":1}}""");

        Store().SaveRunningTimeoutMinutes(20);

        Assert.Equal(AppTheme.Dark, Store().LoadTheme());
        Assert.Equal(20, Store().LoadRunningTimeoutMinutes());
        Assert.Contains("somethingNew", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Saving_the_theme_keeps_the_timeout_and_unknown_keys()
    {
        WriteRaw("""{"runningTimeoutMinutes":25,"somethingNew":"x"}""");

        Store().SaveTheme(AppTheme.Light);

        Assert.Equal(AppTheme.Light, Store().LoadTheme());
        Assert.Equal(25, Store().LoadRunningTimeoutMinutes());
        Assert.Contains("somethingNew", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Saving_over_a_broken_file_keeps_a_copy_and_writes_a_valid_file()
    {
        WriteRaw("{ not json");
        var logs = new List<string>();

        Store(logs.Add).SaveRunningTimeoutMinutes(30);

        Assert.Equal(30, Store().LoadRunningTimeoutMinutes());
        Assert.Equal("{ not json", File.ReadAllText(SettingsPath + ".bad"));
        Assert.NotEmpty(logs);
    }

    [Fact]
    public void Saving_into_a_new_folder_creates_the_file_with_defaults_for_the_rest()
    {
        Store().SaveRunningTimeoutMinutes(12);

        Assert.Equal(AppTheme.System, Store().LoadTheme());
        Assert.Equal(12, Store().LoadRunningTimeoutMinutes());
    }

    [Fact]
    public void A_file_that_cannot_be_read_right_now_is_not_overwritten()
    {
        WriteRaw("""{"theme":"dark","runningTimeoutMinutes":25}""");
        var logs = new List<string>();

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Store(logs.Add).SaveTheme(AppTheme.Light);

        Assert.NotEmpty(logs);
        Assert.Equal(AppTheme.Dark, Store().LoadTheme());          // 保存されなかった（他のキーも消えていない）
        Assert.Equal(25, Store().LoadRunningTimeoutMinutes());
        Assert.False(File.Exists(SettingsPath + ".bad"));
    }
}
