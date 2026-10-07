using Miharikun.Core.Agents;

namespace Miharikun.Tests.Core;

public sealed class TranscriptImporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-transcripts-" + Guid.NewGuid().ToString("N"));
    private static readonly string Project = TestPaths.Abs("zDev", "repo", "Miharikun");
    private static readonly string Slug = OperatingSystem.IsWindows() ? "c-zDev-repo-Miharikun" : "zDev-repo-Miharikun";
    private const string Id1 = "11111111-1111-1111-1111-111111111111";
    private const string Id2 = "22222222-2222-2222-2222-222222222222";

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string TranscriptsDir(string slug) => Path.Combine(_dir, "projects", slug, "agent-transcripts");

    private string WriteNew(string slug, string id, params string[] lines)
    {
        var dir = Path.Combine(TranscriptsDir(slug), id);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id + ".jsonl");
        File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));
        return path;
    }

    private static string User(string text) =>
        "{\"role\":\"user\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}]}}";

    private static string UserQuery(string query) =>
        User("<timestamp>Saturday, Oct 3, 2026, 6:01 PM (UTC+9)</timestamp>\n<user_query>\n" + query + "\n</user_query>");

    private static string Assistant(params string[] blocks) =>
        "{\"role\":\"assistant\",\"message\":{\"content\":[" + string.Join(",", blocks) + "]}}";

    private static string Text(string t) => "{\"type\":\"text\",\"text\":" + System.Text.Json.JsonSerializer.Serialize(t) + "}";
    private const string ToolUse = "{\"type\":\"tool_use\",\"name\":\"Read\",\"input\":{\"path\":\"a\"}}";

    private IReadOnlyList<ImportedSession> Scan(Func<string, bool>? skip = null, string? project = null) =>
        new CursorTranscriptImporter(_dir).Scan(project ?? Project, skip);

    [MacTheory]
    [InlineData("/Users/x/repo", "Users-x-repo")]                          // 27-1 で実機の形を確認（先頭の / は落ちる）
    [InlineData("/Users/x/repo/", "Users-x-repo")]
    [InlineData("/Users/x/my_proj.v2", "Users-x-my-proj-v2")]
    [InlineData("/Users/x/Documents/repo/github.com/po-oq/ebata", "Users-x-Documents-repo-github-com-po-oq-ebata")]
    public void Mac_slug_is_separators_turned_into_hyphens(string path, string expected) =>
        Assert.Equal(expected, CursorTranscriptImporter.SlugFor(path), ignoreCase: true);

    [WindowsTheory]
    [InlineData(@"C:\zDev\repo\Miharikun", "c-zDev-repo-Miharikun")]    // 実機で確認した形
    [InlineData(@"c:\zDev\repo\Miharikun\", "c-zDev-repo-Miharikun")]
    [InlineData("/c:/zDev/repo/Miharikun", "c-zDev-repo-Miharikun")]
    [InlineData(@"C:\Users\x\my_proj.v2", "c-Users-x-my-proj-v2")]
    public void Slug_is_drive_and_separators_turned_into_hyphens(string path, string expected) =>
        Assert.Equal(expected, CursorTranscriptImporter.SlugFor(path), ignoreCase: true);

    [Fact]
    public void Imports_prompts_and_replies_of_the_matching_project_in_order_without_tool_calls()
    {
        var path = WriteNew(Slug, Id1,
            UserQuery("内容教えて"),
            Assistant(Text("確認します。"), ToolUse),
            Assistant(ToolUse),
            Assistant(Text("読みました。"), Text("以上です。")),
            UserQuery("ありがとう"));
        File.SetLastWriteTime(path, new DateTime(2026, 10, 3, 18, 3, 24));

        var session = Assert.Single(Scan());

        Assert.Equal(new SessionKey("cursor", Id1), session.Key);
        Assert.Equal(path, session.TranscriptPath);
        Assert.All(session.Events, e =>
        {
            Assert.True(e.Imported);
            Assert.Equal(new DateTimeOffset(new DateTime(2026, 10, 3, 18, 3, 24)), e.At);
            Assert.Equal(path, e.TranscriptPath);
        });
        Assert.Equal(
            [(AgentEventKind.PromptSubmitted, "内容教えて"),
             (AgentEventKind.AssistantMessage, "確認します。"),
             (AgentEventKind.AssistantMessage, "読みました。\n以上です。"),
             (AgentEventKind.PromptSubmitted, "ありがとう")],
            session.Events.Select(e => (e.Kind, e.Text!)));
        Assert.Equal([1L, 2L, 3L, 4L], session.Events.Select(e => e.Seq));
    }

    [Fact]
    public void User_text_without_the_wrapper_tags_is_kept_minus_the_timestamp()
    {
        WriteNew(Slug, Id1, User("<timestamp>now</timestamp>\nただの文章"));

        var e = Assert.Single(Assert.Single(Scan()).Events);

        Assert.Equal("ただの文章", e.Text);
    }

    [Fact]
    public void Slug_match_ignores_case_and_old_flat_format_is_read_too_but_subagents_are_not()
    {
        WriteNew(Slug.ToUpperInvariant(), Id1, UserQuery("new format"));
        var flat = TranscriptsDir(Slug.ToUpperInvariant());
        File.WriteAllText(Path.Combine(flat, Id2 + ".jsonl"), UserQuery("old format") + "\n");
        var sub = Path.Combine(flat, Id1, "subagents");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "33333333-3333-3333-3333-333333333333.jsonl"), UserQuery("sub") + "\n");

        var sessions = Scan();

        Assert.Equal([Id1, Id2], sessions.Select(s => s.Key.SessionId).Order());
    }

    [Fact]
    public void Other_projects_skipped_ids_and_empty_transcripts_are_not_imported()
    {
        WriteNew("c-other", Id1, UserQuery("other project"));
        WriteNew(Slug, Id2, UserQuery("has hook events"));
        WriteNew(Slug, "44444444-4444-4444-4444-444444444444", "{\"role\":\"assistant\",\"message\":{\"content\":[" + ToolUse + "]}}");

        Assert.Empty(Scan(skip: id => id == Id2));
    }

    [Fact]
    public void Broken_lines_are_skipped_and_a_missing_cursor_dir_is_fine()
    {
        WriteNew(Slug, Id1, "{ not json", UserQuery("ok"), "{\"role\":\"user\"}");

        var e = Assert.Single(Assert.Single(Scan()).Events);
        Assert.Equal("ok", e.Text);

        Assert.Empty(new CursorTranscriptImporter(Path.Combine(_dir, "nowhere")).Scan(Project, null));
    }
}
