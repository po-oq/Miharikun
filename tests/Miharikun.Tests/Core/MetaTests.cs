using System.Text.Json.Nodes;
using Miharikun.Core.Agents;
using Miharikun.Core.Meta;
using Miharikun.Core.Sessions;
using Miharikun.Core.Storage;
using static Miharikun.Tests.Core.TestData;

namespace Miharikun.Tests.Core;

public sealed class SessionMetaTests
{
    private static readonly DateTimeOffset N1 = T0.AddMinutes(1);
    private static readonly DateTimeOffset N2 = T0.AddMinutes(2);
    private static readonly DateTimeOffset N3 = T0.AddMinutes(3);

    [Fact]
    public void Manual_title_wins_over_the_auto_title_and_empty_resets_it()
    {
        var m = new SessionMeta().WithManualTitle("  手動の名前  ", N1);
        Assert.Equal(("手動の名前", true), (m.Title, m.TitleIsManual));
        Assert.Equal("手動の名前", m.DisplayTitle("自動"));

        var reset = m.WithManualTitle("   ", N2);
        Assert.Equal((null, false), (reset.Title, reset.TitleIsManual));
        Assert.Equal("自動", reset.DisplayTitle("自動"));
        Assert.Null(reset.DisplayTitle(null));
    }

    [Fact]
    public void Import_keeps_only_one_previous_summary()
    {
        var m = new SessionMeta()
            .ImportSummary("一つ目", 1, N1)
            .ImportSummary("二つ目", 2, N2)
            .ImportSummary("三つ目", 4, N3);

        Assert.Equal(new SummaryEntry("三つ目", N3, 4), m.Summary);
        Assert.Equal(new SummaryEntry("二つ目", N2, 2), m.PreviousSummary);   // 一つ目は捨てる
    }

    [Fact]
    public void First_import_has_nothing_to_revert()
    {
        var m = new SessionMeta().ImportSummary("a", 1, N1);

        Assert.Null(m.PreviousSummary);
        Assert.False(m.CanRevertSummary);
        Assert.Same(m, m.RevertSummary(N2));   // 戻す先がなければ何もしない
    }

    [Fact]
    public void Revert_swaps_and_a_second_revert_undoes_it()
    {
        var m = new SessionMeta().ImportSummary("古い", 1, N1).ImportSummary("新しい", 2, N2);

        var back = m.RevertSummary(N3);
        Assert.Equal("古い", back.Summary!.Text);
        Assert.Equal("新しい", back.PreviousSummary!.Text);

        var forward = back.RevertSummary(N3);
        Assert.Equal("新しい", forward.Summary!.Text);
        Assert.Equal("古い", forward.PreviousSummary!.Text);
    }

    [Fact]
    public void Edit_replaces_the_summary_drops_the_source_turn_and_can_be_reverted()
    {
        var m = new SessionMeta().ImportSummary("取り込んだ文", 3, N1).EditSummary("  手で直した文  ", N2);

        Assert.Equal(new SummaryEntry("手で直した文", N2, null), m.Summary);
        Assert.Equal("取り込んだ文", m.RevertSummary(N3).Summary!.Text);
    }

    [Fact]
    public void Editing_to_empty_clears_the_summary_but_keeps_it_as_previous()
    {
        var m = new SessionMeta().ImportSummary("文", 1, N1).EditSummary("  ", N2);

        Assert.Null(m.Summary);
        Assert.Equal("文", m.PreviousSummary!.Text);
        Assert.Equal("文", m.RevertSummary(N3).Summary!.Text);
    }

    [Fact]
    public void Memo_and_search_text()
    {
        var m = new SessionMeta().WithMemo("6〜8まで。続きは明日", N1);
        Assert.True(m.HasMemo);
        Assert.False(m.WithMemo("  ", N2).HasMemo);
        Assert.False(m.WithMemo(null, N2).HasMemo);

        var all = m.WithManualTitle("名前", N1).ImportSummary("概要の文", 1, N1);
        Assert.Equal("名前\n概要の文\n6〜8まで。続きは明日", all.SearchText);
        Assert.Equal("", new SessionMeta().SearchText);
        Assert.DoesNotContain("自動", new SessionMeta().SearchText);   // 手動でないタイトルは対象外
    }

    [Fact]
    public void Import_source_is_the_last_response_and_the_turn_it_belongs_to()
    {
        var key = new SessionKey("cursor", "conv-1");
        var s = SessionAnalyzer.Analyze(key, Events(
            E("beforeSubmitPrompt", 0, "\"prompt\":\"1\""), E("afterAgentResponse", 1, "\"text\":\"一回目\""), E("stop", 2, "\"status\":\"completed\""),
            E("beforeSubmitPrompt", 3, "\"prompt\":\"2\""), E("afterAgentResponse", 4, "\"text\":\"二回目の返事\""), E("stop", 5, "\"status\":\"completed\""),
            E("beforeSubmitPrompt", 6, "\"prompt\":\"3\"")));

        var (text, turn) = SummaryImport.FromLastResponse(s)!.Value;

        Assert.Equal(("二回目の返事", 2), (text, turn));   // 実行中の3ターン目ではなく、返事があった2ターン目
    }

    [Fact]
    public void Import_source_is_null_without_a_usable_response()
    {
        var key = new SessionKey("cursor", "conv-1");

        Assert.Null(SummaryImport.FromLastResponse(SessionAnalyzer.Analyze(key, Events(E("sessionStart")))));
        Assert.Null(SummaryImport.FromLastResponse(SessionAnalyzer.Analyze(key, Events(E("afterAgentResponse", 0, "\"text\":\"  \"")))));
    }

    [Fact]
    public void Response_before_any_prompt_has_no_turn_number()
    {
        var key = new SessionKey("cursor", "conv-1");
        var s = SessionAnalyzer.Analyze(key, Events(E("afterAgentResponse", 0, "\"text\":\"途中から\"")));

        Assert.Equal(("途中から", (int?)null), SummaryImport.FromLastResponse(s)!.Value);
    }
}

public sealed class MetaStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-meta-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly List<string> _logs = [];
    private readonly MetaStore _store;

    public MetaStoreTests()
    {
        _paths = new AppPaths(_dir);
        _store = new MetaStore(_paths, "cursor", _logs.Add);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 5, 0, TimeSpan.FromHours(9));

    [Fact]
    public void Missing_file_loads_as_empty_meta()
    {
        var m = _store.Load("nope");

        Assert.Equal(new SessionMeta(), m);
        Assert.Empty(_logs);
    }

    [Fact]
    public void Round_trips_everything_including_japanese()
    {
        var meta = new SessionMeta()
            .WithManualTitle("テストタブの追加", Now)
            .ImportSummary("Playwright のテスト項目を出す「テスト」タブを追加した", 4, Now)
            .ImportSummary("表＋クリックでコード表示にした", 5, Now.AddMinutes(1))
            .WithMemo("6〜8まで。\n続きは明日", Now.AddMinutes(2));

        _store.Save("conv-1", meta);

        Assert.Equal(meta, _store.Load("conv-1"));
    }

    [Fact]
    public void File_matches_the_documented_schema()
    {
        _store.Save("conv-1", new SessionMeta().WithManualTitle("名前", Now).ImportSummary("文", 4, Now).WithMemo("メモ", Now));

        var text = File.ReadAllText(_paths.MetaFile("cursor", "conv-1"));
        var json = JsonNode.Parse(text)!;

        Assert.Equal(1, (int)json["v"]!);
        Assert.Equal("名前", (string)json["title"]!);
        Assert.True((bool)json["titleIsManual"]!);
        Assert.Equal(("文", 4), (((string)json["summary"]!["text"]!), (int)json["summary"]!["sourceTurn"]!));
        Assert.NotNull(json["summary"]!["importedAt"]);
        Assert.Equal("メモ", (string)json["memo"]!);
        Assert.NotNull(json["updatedAt"]);
        Assert.Null(json["previousSummary"]);          // 無いものは書かない
        Assert.Equal(                                  // 計算プロパティは保存しない
            ["v", "title", "titleIsManual", "summary", "memo", "updatedAt"],
            json.AsObject().Select(p => p.Key).ToArray());
        Assert.Contains("名前", text);                  // 日本語をエスケープしない
        Assert.False(text.StartsWith('\uFEFF'));       // BOM なし
    }

    [Fact]
    public void Save_overwrites_without_leaving_temp_files()
    {
        _store.Save("conv-1", new SessionMeta().WithMemo("一回目", Now));
        _store.Save("conv-1", new SessionMeta().WithMemo("二回目", Now));

        Assert.Equal("二回目", _store.Load("conv-1").Memo);
        Assert.Equal([_paths.MetaFile("cursor", "conv-1")], Directory.GetFiles(_paths.MetaDir("cursor")));
    }

    [Fact]
    public void Corrupt_file_loads_as_empty_is_logged_and_kept_as_bad()
    {
        Directory.CreateDirectory(_paths.MetaDir("cursor"));
        var path = _paths.MetaFile("cursor", "conv-1");
        File.WriteAllText(path, "{ これは壊れている");

        var m = _store.Load("conv-1");

        Assert.Equal(new SessionMeta(), m);
        Assert.Single(_logs);
        Assert.Equal("{ これは壊れている", File.ReadAllText(path + ".bad"));
    }

    [Fact]
    public void Unknown_fields_and_missing_fields_are_tolerated()
    {
        Directory.CreateDirectory(_paths.MetaDir("cursor"));
        File.WriteAllText(_paths.MetaFile("cursor", "conv-1"), """{"v":1,"memo":"だけ","future":{"x":1}}""");

        var m = _store.Load("conv-1");

        Assert.Equal("だけ", m.Memo);
        Assert.Null(m.Summary);
    }

    [Fact]
    public void Two_writers_do_not_corrupt_the_file_and_the_last_write_wins()
    {
        var other = new MetaStore(_paths, "cursor");
        Parallel.For(0, 50, i => (i % 2 == 0 ? _store : other).Save("conv-1", new SessionMeta().WithMemo("書き込み" + i, Now)));

        var m = _store.Load("conv-1");
        Assert.StartsWith("書き込み", m.Memo);
        // 50並列という極端な競合の最後の瞬間には、Windows が残す ~RF*.TMP が1つ残りうる（次の保存で片付く）。
        var strays = Directory.GetFiles(_paths.MetaDir("cursor"))
            .Select(Path.GetFileName).Where(n => n != "conv-1.json" && !n!.Contains("~RF")).ToList();
        Assert.Empty(strays);
    }

    [Fact]
    public void Session_id_with_path_characters_stays_inside_the_meta_dir()
    {
        _store.Save("..\\..\\evil", new SessionMeta().WithMemo("x", Now));

        Assert.Single(Directory.GetFiles(_paths.MetaDir("cursor")));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.json")));
    }
}

public sealed class SessionMetaServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-metasvc-" + Guid.NewGuid().ToString("N"));
    private static readonly SessionKey Key = new("cursor", "conv-1");
    private static readonly DateTimeOffset Fixed = new(2026, 10, 3, 12, 5, 0, TimeSpan.FromHours(9));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private SessionMetaService Create(List<string>? logs = null) =>
        new(agentId => new MetaStore(new AppPaths(_dir), agentId), () => Fixed, logs is null ? null : logs.Add);

    [Fact]
    public void Update_saves_notifies_and_survives_a_restart()
    {
        var svc = Create();
        var changed = new List<SessionKey>();
        svc.Changed += changed.Add;

        svc.Update(Key, (m, now) => m.WithManualTitle("名前", now).WithMemo("メモ", now));

        Assert.Equal([Key], changed);
        Assert.Equal("名前", svc.Get(Key).Title);
        Assert.Equal(Fixed, svc.Get(Key).UpdatedAt);

        var afterRestart = Create().Get(Key);
        Assert.Equal(("名前", "メモ"), (afterRestart.Title, afterRestart.Memo));
    }

    [Fact]
    public void Get_is_cached_and_defaults_to_empty()
    {
        var svc = Create();

        Assert.Same(svc.Get(Key), svc.Get(Key));
        Assert.Equal(new SessionMeta(), svc.Get(Key));
    }

    [Fact]
    public void Save_failure_is_logged_but_the_in_memory_value_still_updates()
    {
        var logs = new List<string>();
        var svc = Create(logs);
        Directory.CreateDirectory(Path.Combine(_dir, "meta"));
        File.WriteAllText(Path.Combine(_dir, "meta", "cursor"), "ファイルがディレクトリの場所にある");

        svc.Update(Key, (m, now) => m.WithMemo("残る", now));

        Assert.Equal("残る", svc.Get(Key).Memo);
        Assert.Single(logs);
    }
}