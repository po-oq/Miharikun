using System.Text;
using System.Text.Json;
using Miharikun.Core.Agents;
using Miharikun.Core.Install;
using Miharikun.Core.Sessions;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

/// <summary>Issue #17 Phase 34-2：transcript の「変わった分だけ」の取り込み（計画 7.3）。</summary>
public sealed class CursorTranscriptRealtimeTests : IDisposable
{
    /// <summary>読み終わる直前に差し込める importer（Cursor が読んでいる最中に追記する状況を作る）。</summary>
    private sealed class HookedImporter(string dir, HookRegistration? registration = null) : CursorTranscriptImporter(dir, null, registration)
    {
        public Action<string>? AfterRead { get; set; }
        public int Reads { get; private set; }

        protected override void OnTranscriptRead(string path)
        {
            Reads++;
            AfterRead?.Invoke(path);
        }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-realtime-" + Guid.NewGuid().ToString("N"));
    private static readonly string Project = TestPaths.Abs("zDev", "repo", "Miharikun");
    private static readonly string Slug = OperatingSystem.IsWindows() ? "c-zDev-repo-Miharikun" : "zDev-repo-Miharikun";
    private const string Id1 = "11111111-1111-1111-1111-111111111111";
    private const string Id2 = "22222222-2222-2222-2222-222222222222";

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string TranscriptPath(string id, string? slug = null) =>
        Path.Combine(_dir, "projects", slug ?? Slug, "agent-transcripts", id, id + ".jsonl");

    private string Write(string id, params string[] lines)
    {
        var path = TranscriptPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));
        return path;
    }

    private static void Append(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var bytes = new UTF8Encoding(false).GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string User(string query) =>
        "{\"role\":\"user\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":" +
        JsonSerializer.Serialize("<user_query>\n" + query + "\n</user_query>") + "}]}}";

    private static string Assistant(string text) =>
        "{\"role\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(text) + "}]}}";

    private static string[] Texts(ImportedSession s) => [.. s.Events.Select(e => e.Text!)];

    // ---------------------------------------------------------------- 変わった分だけ

    [Fact]
    public void First_scan_returns_everything_and_an_unchanged_second_scan_returns_nothing()
    {
        Write(Id1, User("a"), Assistant("b"));
        Write(Id2, User("c"));
        var importer = new CursorTranscriptImporter(_dir);

        Assert.Equal(2, importer.Scan(Project, null).Count);
        Assert.Empty(importer.Scan(Project, null));
    }

    [Fact]
    public void An_appended_transcript_is_returned_again_with_the_whole_session()
    {
        var path = Write(Id1, User("a"), Assistant("b"));
        var importer = new CursorTranscriptImporter(_dir);
        importer.Scan(Project, null);

        Append(path, Assistant("c") + "\n");

        var session = Assert.Single(importer.Scan(Project, null));
        Assert.Equal(new SessionKey("cursor", Id1), session.Key);
        Assert.Equal(["a", "b", "c"], Texts(session));
        Assert.Empty(importer.Scan(Project, null));
    }

    [Fact]
    public void A_change_of_only_the_modified_time_is_noticed_too()
    {
        var path = Write(Id1, User("a"));
        var importer = new CursorTranscriptImporter(_dir);
        importer.Scan(Project, null);

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));

        Assert.Single(importer.Scan(Project, null));
    }

    [Fact]
    public void Only_the_changed_or_new_transcripts_are_returned()
    {
        var p1 = Write(Id1, User("a"));
        var importer = new CursorTranscriptImporter(_dir);
        importer.Scan(Project, null);

        Write(Id2, User("new"));
        var second = Assert.Single(importer.Scan(Project, null));
        Assert.Equal(Id2, second.Key.SessionId);

        Append(p1, Assistant("more") + "\n");
        var third = Assert.Single(importer.Scan(Project, null));
        Assert.Equal(Id1, third.Key.SessionId);
    }

    [Fact]
    public void A_skipped_id_is_not_returned_and_comes_back_when_it_is_no_longer_skipped()
    {
        Write(Id1, User("a"));
        var importer = new CursorTranscriptImporter(_dir);

        Assert.Empty(importer.Scan(Project, id => id == Id1));   // hook の記録がある
        Assert.Empty(importer.Scan(Project, id => id == Id1));

        // hook のファイルを消した（skip が false に戻った）→ 取り込みに戻る
        Assert.Single(importer.Scan(Project, null));
    }

    [Fact]
    public void Skipping_an_id_after_it_was_imported_forgets_it_so_it_returns_later()
    {
        Write(Id1, User("a"));
        var importer = new CursorTranscriptImporter(_dir);
        Assert.Single(importer.Scan(Project, null));

        Assert.Empty(importer.Scan(Project, id => id == Id1));   // hook のファイルができて切り替え
        Assert.Single(importer.Scan(Project, null));              // hook のファイルを消した
    }

    // ---------------------------------------------------------------- 最後の行

    [Fact]
    public void A_half_written_last_line_is_skipped_and_used_once_it_is_finished()
    {
        var path = Write(Id1, User("a"));
        var half = Assistant("done");
        Append(path, half[..^6]);   // 改行なし・JSON としても途中
        var importer = new CursorTranscriptImporter(_dir);

        var first = Assert.Single(importer.Scan(Project, null));
        Assert.Equal(["a"], Texts(first));

        Append(path, half[^6..] + "\n");

        var second = Assert.Single(importer.Scan(Project, null));
        Assert.Equal(["a", "done"], Texts(second));
    }

    [Fact]
    public void A_complete_last_line_without_a_newline_is_used()
    {
        var path = Write(Id1, User("a"));
        Append(path, Assistant("last"));   // 末尾に改行なし

        var session = Assert.Single(new CursorTranscriptImporter(_dir).Scan(Project, null));

        Assert.Equal(["a", "last"], Texts(session));
    }

    [Fact]
    public void A_broken_line_in_the_middle_is_still_skipped()
    {
        Write(Id1, User("a"), "{ broken", Assistant("b"));

        var session = Assert.Single(new CursorTranscriptImporter(_dir).Scan(Project, null));

        Assert.Equal(["a", "b"], Texts(session));
    }

    // ---------------------------------------------------------------- 読んでいる途中・共有

    [Fact]
    public void A_line_appended_while_reading_is_returned_by_the_next_scan()
    {
        var path = Write(Id1, User("a"));
        var importer = new HookedImporter(_dir);
        var appended = false;
        importer.AfterRead = p =>
        {
            if (!appended)
            {
                appended = true;
                Append(p, Assistant("late") + "\n");
            }
        };

        var first = Assert.Single(importer.Scan(Project, null));
        Assert.Equal(["a"], Texts(first));   // 読んだ時点の分

        var second = Assert.Single(importer.Scan(Project, null));   // 読み終わりで値が変わっていたので覚えていない
        Assert.Equal(["a", "late"], Texts(second));
        Assert.Empty(importer.Scan(Project, null));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_transcript_that_another_process_has_open_for_writing_can_be_read()
    {
        var path = Write(Id1, User("a"));
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);

        var session = Assert.Single(new CursorTranscriptImporter(_dir).Scan(Project, null));

        Assert.Equal(["a"], Texts(session));
    }

    [Fact]
    public void A_transcript_that_cannot_be_opened_is_logged_and_tried_again_on_the_next_scan()
    {
        var path = Write(Id1, User("a"));
        var logs = new List<string>();
        var importer = new CursorTranscriptImporter(_dir, logs.Add);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Empty(importer.Scan(Project, null));
        Assert.NotEmpty(logs);

        Assert.Single(importer.Scan(Project, null));
    }

    // ---------------------------------------------------------------- 範囲

    [Fact]
    public void Subagent_transcripts_are_not_read()
    {
        var path = Write(Id1, User("a"));
        var sub = Path.Combine(Path.GetDirectoryName(path)!, "subagents");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "sub1.jsonl"), User("from a subagent") + "\n");

        var session = Assert.Single(new CursorTranscriptImporter(_dir).Scan(Project, null));

        Assert.Equal(["a"], Texts(session));
    }

    [Fact]
    public void TranscriptDirs_lists_only_the_existing_folders_of_the_project()
    {
        Write(Id1, User("a"));
        var other = TranscriptPath("33333333-3333-3333-3333-333333333333", "c-somewhere-else");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        var importer = new CursorTranscriptImporter(_dir);

        Assert.Equal([Path.Combine(_dir, "projects", Slug, "agent-transcripts")], importer.TranscriptDirs(Project));
        Assert.Empty(importer.TranscriptDirs(TestPaths.Abs("nothing", "here")));
    }

    [Fact]
    public void A_transcript_that_is_deleted_does_not_remove_the_session_from_the_store()
    {
        var path = Write(Id1, User("a"), Assistant("b"));
        var paths = new AppPaths(Path.Combine(_dir, "data"));
        var source = new CursorSessionSource(new CursorAgent(), paths, Project, importer: new CursorTranscriptImporter(_dir));
        var store = new ProjectEventStore([source]);
        Assert.Equal([new SessionKey("cursor", Id1)], store.Refresh());

        File.Delete(path);

        Assert.Empty(store.Refresh());
        Assert.NotNull(store.GetSummary(new SessionKey("cursor", Id1)));
    }

    // ---------------------------------------------------------------- Source / Store との組み合わせ

    [Fact]
    public void The_store_follows_a_transcript_that_keeps_growing()
    {
        var path = Write(Id1, User("a"));
        var paths = new AppPaths(Path.Combine(_dir, "data"));
        var source = new CursorSessionSource(new CursorAgent(), paths, Project, importer: new CursorTranscriptImporter(_dir));
        var store = new ProjectEventStore([source]);
        store.Refresh();
        var key = new SessionKey("cursor", Id1);
        Assert.Single(store.GetEvents(key));

        Append(path, Assistant("b") + "\n");

        Assert.Equal([key], store.Refresh());
        Assert.Equal(2, store.GetEvents(key).Count);
        Assert.Empty(store.Refresh());
    }

    [Fact]
    public void Watch_targets_include_the_transcript_folder_with_subfolders_and_never_create_it()
    {
        var paths = new AppPaths(Path.Combine(_dir, "data"));
        var source = new CursorSessionSource(new CursorAgent(), paths, Project, importer: new CursorTranscriptImporter(_dir));

        var before = source.WatchTargets;
        Assert.Single(before);   // events だけ（transcript のフォルダはまだ無い）
        Assert.False(Directory.Exists(Path.Combine(_dir, "projects")));

        Write(Id1, User("a"));
        var after = source.WatchTargets;

        var transcript = Assert.Single(after, t => t.IncludeSubdirectories);
        Assert.Equal(Path.Combine(_dir, "projects", Slug, "agent-transcripts"), transcript.Directory);
        Assert.Equal("*.jsonl", transcript.Filter);
        Assert.False(transcript.CreateIfMissing);
        Assert.DoesNotContain(after, t => t.CreateIfMissing && t.IncludeSubdirectories);
    }

    [Fact]
    public void Without_an_importer_the_watch_targets_are_only_the_events_folder()
    {
        var paths = new AppPaths(Path.Combine(_dir, "data"));
        var source = new CursorSessionSource(new CursorAgent(), paths, Project);

        var target = Assert.Single(source.WatchTargets);
        Assert.False(target.IncludeSubdirectories);
        Assert.True(target.CreateIfMissing);
    }

    // ---------------------------------------------------------------- Hook なし（34-3。計画 7.4）

    private static readonly DateTime Registered = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private const string Ours = """{"version":1,"hooks":{"stop":[{"command":"C:/dev/Miharikun.Hook.exe --agent cursor"}]}}""";

    private string HooksJson => Path.Combine(_dir, "hooks.json");
    private string EventsDir => Path.Combine(_dir, "data", "events", "cursor");

    private void Register(DateTime writtenUtc)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(HooksJson, Ours);
        File.SetLastWriteTimeUtc(HooksJson, writtenUtc);
    }

    private HookedImporter WithRegistration() => new(_dir, new HookRegistration(HooksJson, EventsDir));

    private string WriteAt(string id, DateTime writtenUtc, params string[] lines)
    {
        var path = Write(id, lines);
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    private static bool[] Missing(ImportedSession s) => [.. s.Events.Select(e => e.HookMissing)];

    [Fact]
    public void A_transcript_newer_than_the_registration_is_marked_hook_missing_on_every_event()
    {
        Register(Registered);
        WriteAt(Id1, Registered.AddMinutes(5), User("a"), Assistant("b"));

        var session = Assert.Single(WithRegistration().Scan(Project, null));

        Assert.Equal([true, true], Missing(session));
        Assert.All(session.Events, e => Assert.True(e.Imported));
    }

    [Fact]
    public void A_transcript_older_than_the_registration_is_not_marked()
    {
        Register(Registered);
        WriteAt(Id1, Registered.AddMinutes(-5), User("a"), Assistant("b"));

        var session = Assert.Single(WithRegistration().Scan(Project, null));

        Assert.Equal([false, false], Missing(session));
    }

    [Fact]
    public void Without_a_registration_object_or_a_registration_nothing_is_marked()
    {
        WriteAt(Id1, Registered, User("a"));

        Assert.Equal([false], Missing(Assert.Single(new CursorTranscriptImporter(_dir, null, null).Scan(Project, null))));

        // hooks.json が無い・Miharikun の登録が無い
        var importer = WithRegistration();
        Assert.Equal([false], Missing(Assert.Single(importer.Scan(Project, null))));
    }

    [Fact]
    public void When_the_registration_stamp_is_unchanged_unchanged_transcripts_are_not_read_again()
    {
        Register(Registered);
        WriteAt(Id1, Registered.AddMinutes(5), User("a"));
        var importer = WithRegistration();
        importer.Scan(Project, null);
        Assert.Equal(1, importer.Reads);

        Assert.Empty(importer.Scan(Project, null));
        Assert.Empty(importer.Scan(Project, null));

        Assert.Equal(1, importer.Reads);
    }

    [Fact]
    public void Removing_the_registration_makes_the_nohook_sessions_come_back_as_imported()
    {
        Register(Registered);
        WriteAt(Id1, Registered.AddMinutes(5), User("a"));
        var importer = WithRegistration();
        Assert.Equal([true], Missing(Assert.Single(importer.Scan(Project, null))));

        File.WriteAllText(HooksJson, """{"version":1,"hooks":{}}""");   // hook を削除した（更新日時も変わる）
        File.SetLastWriteTimeUtc(HooksJson, Registered.AddMinutes(10));

        var again = Assert.Single(importer.Scan(Project, null));
        Assert.Equal([false], Missing(again));
        Assert.Empty(importer.Scan(Project, null));
    }

    [Fact]
    public void Adding_a_registration_after_the_transcript_rereads_it_but_it_stays_imported()
    {
        WriteAt(Id1, Registered, User("a"));
        var importer = WithRegistration();
        Assert.Equal([false], Missing(Assert.Single(importer.Scan(Project, null))));   // 登録なし

        Register(Registered.AddMinutes(30));   // transcript より後に登録

        var again = Assert.Single(importer.Scan(Project, null));
        Assert.Equal([false], Missing(again));
    }

    [Fact]
    public void A_rewritten_hooks_json_with_an_older_time_for_the_transcript_marks_only_the_newer_ones()
    {
        Register(Registered);
        WriteAt(Id1, Registered.AddMinutes(-5), User("old"));
        WriteAt(Id2, Registered.AddMinutes(5), User("new"));

        var sessions = WithRegistration().Scan(Project, null).ToDictionary(s => s.Key.SessionId);

        Assert.Equal([false], Missing(sessions[Id1]));
        Assert.Equal([true], Missing(sessions[Id2]));
    }

    // ---------------------------------------------------------------- 起動中の流れ（34-4：App の組み立てと同じ構成）

    [Fact]
    public void While_running_a_new_transcript_shows_as_nohook_follows_appends_and_returns_to_imported_when_the_registration_goes()
    {
        Register(Registered);   // 登録あり・events なし
        var data = new AppPaths(Path.Combine(_dir, "data"));
        var registration = new HookRegistration(HooksJson, data.EventsDir("cursor"));
        var source = new CursorSessionSource(new CursorAgent(), data, Project,
            importer: new CursorTranscriptImporter(_dir, null, registration));
        var key = new SessionKey("cursor", Id1);
        var states = new List<SessionState>();
        var gate = new object();
        using var monitor = new SessionMonitor(new ProjectEventStore([source]),
            debounce: TimeSpan.FromMilliseconds(50), pollInterval: TimeSpan.FromMilliseconds(100));
        monitor.Updated += u =>
        {
            lock (gate)
                states.AddRange(u.Upserts.Where(s => s.Summary.Key == key).Select(s => s.Summary.State));
        };
        monitor.Start();
        Thread.Sleep(300);

        var path = WriteAt(Id1, DateTime.UtcNow, User("a"), Assistant("b"));   // 起動後に Cursor が書いた transcript（登録より後）
        Assert.True(WaitFor(() => { lock (gate) return states.Contains(SessionState.NoHook); }), "Hook なしで出る");

        Append(path, Assistant("c") + "\n");
        Assert.True(WaitFor(() => monitor.GetEvents(key).Count == 3), "追記が起動中に増える");

        File.WriteAllText(HooksJson, """{"version":1,"hooks":{}}""");   // hook を削除
        File.SetLastWriteTimeUtc(HooksJson, DateTime.UtcNow);
        Assert.True(WaitFor(() => { lock (gate) return states[^1] == SessionState.Imported; }), "導入前の扱いに戻る");
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }
}
