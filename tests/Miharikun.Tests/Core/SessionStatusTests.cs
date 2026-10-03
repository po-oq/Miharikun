using System.Text.Json.Nodes;
using Miharikun.Core.Meta;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

public sealed class SessionStatusTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(9));

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-status-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly MetaStore _store;

    public SessionStatusTests()
    {
        _paths = new AppPaths(_dir);
        _store = new MetaStore(_paths, "cursor");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_paths.MetaDir("cursor"));
        File.WriteAllText(_paths.MetaFile("cursor", "c1"), json);
    }

    [Fact]
    public void New_meta_has_no_status()
    {
        Assert.Null(new SessionMeta().Status);
    }

    [Fact]
    public void WithStatus_sets_it_and_touches_updated_at()
    {
        var m = new SessionMeta().WithStatus(SessionStatus.Working, Now);
        Assert.Equal(SessionStatus.Working, m.Status);
        Assert.Equal(Now, m.UpdatedAt);
    }

    [Fact]
    public void Toggle_selects_a_status_and_pressing_the_same_one_again_clears_it()
    {
        var m = new SessionMeta().ToggleStatus(SessionStatus.Paused, Now);
        Assert.Equal(SessionStatus.Paused, m.Status);

        Assert.Equal(SessionStatus.Done, m.ToggleStatus(SessionStatus.Done, Now).Status);   // 別の値へは直接切り替わる
        Assert.Null(m.ToggleStatus(SessionStatus.Paused, Now).Status);
    }

    [Fact]
    public void Status_does_not_touch_other_fields()
    {
        var before = new SessionMeta().WithManualTitle("名前", Now).WithMemo("メモ", Now);
        var after = before.WithStatus(SessionStatus.Done, Now);
        Assert.Equal(before with { Status = SessionStatus.Done }, after);
    }

    [Theory]
    [InlineData(SessionStatus.Working, "working")]
    [InlineData(SessionStatus.Paused, "paused")]
    [InlineData(SessionStatus.Done, "done")]
    public void Saved_as_a_lowercase_string_and_read_back(SessionStatus status, string text)
    {
        var meta = new SessionMeta().WithStatus(status, Now);
        _store.Save("c1", meta);

        var json = JsonNode.Parse(File.ReadAllText(_paths.MetaFile("cursor", "c1")))!;
        Assert.Equal(text, (string)json["status"]!);
        Assert.Equal(meta, _store.Load("c1"));
    }

    [Fact]
    public void Unset_status_is_not_written()
    {
        _store.Save("c1", new SessionMeta().WithMemo("だけ", Now));
        var json = JsonNode.Parse(File.ReadAllText(_paths.MetaFile("cursor", "c1")))!;
        Assert.Null(json["status"]);
    }

    [Fact]
    public void Existing_files_without_status_load_as_unset()
    {
        WriteRaw("""{"v":1,"memo":"古いファイル"}""");
        var m = _store.Load("c1");
        Assert.Null(m.Status);
        Assert.Equal("古いファイル", m.Memo);
    }

    [Theory]
    [InlineData("\"purple\"")]
    [InlineData("\"\"")]
    [InlineData("3")]
    [InlineData("null")]
    public void Unknown_status_values_load_as_unset_and_keep_the_rest(string value)
    {
        WriteRaw("{\"v\":1,\"memo\":\"残る\",\"status\":" + value + "}");
        var m = _store.Load("c1");
        Assert.Null(m.Status);
        Assert.Equal("残る", m.Memo);
    }
}

public sealed class SessionStatusTextTests
{
    [Theory]
    [InlineData(SessionStatus.Working, "作業中")]
    [InlineData(SessionStatus.Paused, "中断")]
    [InlineData(SessionStatus.Done, "完了")]
    public void Names(SessionStatus status, string expected) =>
        Assert.Equal(expected, Miharikun.Core.Sessions.SessionText.StatusName(status));

    [Fact]
    public void Unset_name_is_used_for_the_filter_tab()
    {
        Assert.Equal("未設定", Miharikun.Core.Sessions.SessionText.StatusName(null));
    }
}
