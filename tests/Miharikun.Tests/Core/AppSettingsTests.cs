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
}
