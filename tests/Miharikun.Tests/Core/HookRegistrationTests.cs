using Miharikun.Core.Install;

namespace Miharikun.Tests.Core;

/// <summary>Issue #17 Phase 34-3：Hook の登録の時刻（計画 7.4）。</summary>
public sealed class HookRegistrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-hookreg-" + Guid.NewGuid().ToString("N"));
    private readonly string _hooksJson;
    private readonly string _events;

    public HookRegistrationTests()
    {
        Directory.CreateDirectory(_dir);
        _hooksJson = Path.Combine(_dir, "hooks.json");
        _events = Path.Combine(_dir, "events", "cursor");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private HookRegistration Registration() => new(_hooksJson, _events);

    private static readonly DateTime T = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private void WriteHooks(string json, DateTime writtenUtc)
    {
        File.WriteAllText(_hooksJson, json);
        File.SetLastWriteTimeUtc(_hooksJson, writtenUtc);
    }

    private const string Ours = """{"version":1,"hooks":{"stop":[{"command":"C:/dev/Miharikun.Hook.exe --agent cursor"}]}}""";

    private void WriteEvents(string name, DateTime createdUtc)
    {
        Directory.CreateDirectory(_events);
        var path = Path.Combine(_events, name);
        File.WriteAllText(path, "{}\n");
        File.SetCreationTimeUtc(path, createdUtc);
    }

    [Fact]
    public void Without_a_registration_there_is_no_since()
    {
        Assert.Null(Registration().Since());   // hooks.json が無い

        WriteHooks("""{"version":1,"hooks":{"stop":[{"command":"C:/tools/other.exe"}]}}""", T);
        Assert.Null(Registration().Since());   // 別アプリの登録だけ

        WriteHooks("""{"version":1,"hooks":{}}""", T);
        Assert.Null(Registration().Since());
    }

    [Fact]
    public void With_a_registration_and_no_events_since_is_the_hooks_json_time()
    {
        WriteHooks(Ours, T);

        Assert.Equal(new DateTimeOffset(T), Registration().Since());
    }

    [Fact]
    public void With_events_since_is_the_earlier_of_hooks_json_and_the_oldest_events_file()
    {
        WriteHooks(Ours, T);
        WriteEvents("b.jsonl", T.AddHours(-1));
        WriteEvents("a.jsonl", T.AddHours(-3));
        Assert.Equal(new DateTimeOffset(T.AddHours(-3)), Registration().Since());

        // hooks.json のほうが早ければそちら
        WriteHooks(Ours, T.AddHours(-10));
        Assert.Equal(new DateTimeOffset(T.AddHours(-10)), Registration().Since());
    }

    [Fact]
    public void The_app_events_file_counts_as_a_hook_record_too()
    {
        WriteHooks(Ours, T);
        WriteEvents("_app.jsonl", T.AddHours(-2));

        Assert.Equal(new DateTimeOffset(T.AddHours(-2)), Registration().Since());
    }

    [Fact]
    public void A_broken_hooks_json_has_no_since()
    {
        WriteHooks("{ broken", T);

        Assert.Null(Registration().Since());
    }

    [Fact]
    public void Stamp_is_null_without_the_file_and_changes_when_it_is_rewritten()
    {
        Assert.Null(Registration().Stamp());

        WriteHooks(Ours, T);
        var first = Registration().Stamp();
        Assert.Equal(new DateTimeOffset(T), first);

        WriteHooks(Ours, T.AddMinutes(1));
        Assert.NotEqual(first, Registration().Stamp());
    }
}
