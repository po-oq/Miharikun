using System.Text.Json.Nodes;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using static Miharikun.Tests.Core.ClaudeLogBuilder;

namespace Miharikun.Tests.Core;

/// <summary>Phase 19-1：Claude Code の会話ログ 1 行 → 共通イベント（要件 5.1 の対応表・計画 8.2）。</summary>
public sealed class ClaudeNormalizerTests
{
    private readonly List<string> _logs = [];
    private readonly ClaudeFormatLog _formatLog;
    private readonly ClaudeTranscriptNormalizer _normalizer;
    private readonly ClaudeLogBuilder _b = new();

    public ClaudeNormalizerTests()
    {
        _formatLog = new ClaudeFormatLog(_logs.Add);
        _normalizer = new ClaudeTranscriptNormalizer("sess-1", "sess-1.jsonl", _formatLog);
    }

    /// <summary>version の初出のログ（毎回出る）を除いた、形式の変化のログ。</summary>
    private List<string> Logs => _logs.Where(l => !l.Contains("初めて見る version")).ToList();

    /// <summary>行番号は 1 始まりで自動採番。</summary>
    private List<AgentEvent> Run(params string[] lines)
    {
        var events = new List<AgentEvent>();
        for (var i = 0; i < lines.Length; i++)
            events.AddRange(_normalizer.NormalizeLine(i + 1, lines[i]));
        return events;
    }

    private static List<AgentEvent> Without(List<AgentEvent> events, AgentEventKind kind) => events.Where(e => e.Kind != kind).ToList();

    // ---- 最初の記録・ブランチ ----

    [Fact]
    public void First_record_with_a_timestamp_becomes_SessionStarted_with_the_branch()
    {
        var events = Run(_b.User("はじめ"));

        var started = events[0];
        Assert.Equal(AgentEventKind.SessionStarted, started.Kind);
        Assert.Equal(new SessionKey("claude", "sess-1"), started.Session);
        Assert.Equal(Start.AddSeconds(1), started.At);
        Assert.Equal(1, started.Seq);
        Assert.Equal(new GitSnapshot("main", null), started.Git);
        Assert.Equal(AgentEventKind.PromptSubmitted, events[1].Kind);
        Assert.Equal(1, events[1].Seq);   // 1 行から複数のイベントが出たら、同じ Seq
    }

    [Fact]
    public void SessionStarted_is_emitted_once_even_when_the_first_record_is_skipped()
    {
        var events = Run(_b.Other("queue-operation"), _b.User("a"), _b.User("b"));

        Assert.Equal(1, events.Count(e => e.Kind == AgentEventKind.SessionStarted));
        Assert.Equal(AgentEventKind.SessionStarted, events[0].Kind);
        Assert.Equal(1, events[0].Seq);
    }

    [Fact]
    public void Missing_branch_gives_a_snapshot_without_a_branch()
    {
        var events = Run(new ClaudeLogBuilder(branch: null).User("a"));

        Assert.Null(events[0].Git?.Branch);
    }

    [Fact]
    public void A_record_without_a_timestamp_does_not_start_the_session()
    {
        var events = Run("""{"type":"attachment","attachment":{"type":"x"}}""", _b.User("a"));

        Assert.Equal(AgentEventKind.SessionStarted, events[0].Kind);
        Assert.Equal(2, events[0].Seq);
    }

    // ---- ブランチ：最初の記録に gitBranch が無いとき（実ログ 2.1.286 の最初の記録は queue-operation で、cwd も gitBranch も無い） ----

    private const string QueueOperationWithoutBranch =
        """{"type":"queue-operation","operation":"enqueue","timestamp":"2026-10-03T02:00:00.000Z","sessionId":"sess-1"}""";

    [Fact]
    public void The_branch_comes_from_the_first_line_that_has_one_when_the_first_record_has_none()
    {
        var events = Run(QueueOperationWithoutBranch, _b.User("はじめ"));

        Assert.Equal(AgentEventKind.SessionStarted, events[0].Kind);
        Assert.Null(events[0].Git?.Branch);                     // 最初の記録には無い
        var prompt = events.Single(e => e.Kind == AgentEventKind.PromptSubmitted);
        Assert.Equal("main", prompt.Git?.Branch);               // 最初に branch のある行のイベントに載せる

        var summary = SessionAnalyzer.Analyze(new SessionKey("claude", "sess-1"), events);
        Assert.Equal(("main", "main"), (summary.Branch, summary.StartBranch));
    }

    [Fact]
    public void The_branch_is_sent_again_only_when_it_changes()
    {
        var other = new ClaudeLogBuilder("sess-1", @"C:\work\proj", "feature/x");
        var events = Run(_b.User("a"), _b.User("b"), other.User("c"), other.User("d"));

        var withGit = events.Where(e => e.Git is not null).ToList();
        Assert.Equal(["main", "feature/x"], withGit.Select(e => e.Git!.Branch));   // 変わった行に 1 回ずつ
        var summary = SessionAnalyzer.Analyze(new SessionKey("claude", "sess-1"), events);
        Assert.Equal(("feature/x", "main"), (summary.Branch, summary.StartBranch));
    }

    [Fact]
    public void Lines_that_produce_no_events_do_not_use_up_the_branch()
    {
        var events = Run(QueueOperationWithoutBranch, _b.Other("attachment"), _b.User("あとで"));

        Assert.Equal("main", events.Single(e => e.Kind == AgentEventKind.PromptSubmitted).Git?.Branch);
    }

    // ---- 人の入力 ----

    [Theory]
    [InlineData(Origin.Human)]
    [InlineData(Origin.None)]   // 古い版（origin 欄が無い）
    public void Human_input_becomes_PromptSubmitted(Origin origin)
    {
        var events = Without(Run(_b.User("依頼です", origin)), AgentEventKind.SessionStarted);

        var e = Assert.Single(events);
        Assert.Equal(AgentEventKind.PromptSubmitted, e.Kind);
        Assert.Equal("依頼です", e.Text);
    }

    [Fact]
    public void Task_notification_is_not_an_input()
    {
        var events = Without(Run(_b.User("<task-notification>…</task-notification>", Origin.TaskNotification)), AgentEventKind.SessionStarted);

        Assert.Empty(events);
    }

    [Fact]
    public void Meta_lines_are_not_an_input()
    {
        var events = Without(Run(_b.User("<system-reminder>…", isMeta: true)), AgentEventKind.SessionStarted);

        Assert.Empty(events);
    }

    [Fact]
    public void Interrupt_marker_is_not_an_input()
    {
        var events = Run(
            _b.UserBlocks("[Request interrupted by user]"),
            _b.User("[Request interrupted by user for tool use]", Origin.None));

        Assert.DoesNotContain(events, e => e.Kind == AgentEventKind.PromptSubmitted);   // ターンの中断になる（ClaudeTurnTests）
    }

    [Fact]
    public void Slash_commands_and_local_command_output_are_shown_as_they_are()
    {
        var events = Without(Run(
            _b.User("<command-name>/model</command-name>\n<command-args>x</command-args>", Origin.None),
            _b.User("<local-command-stdout>Set model</local-command-stdout>", Origin.None)), AgentEventKind.SessionStarted);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(AgentEventKind.PromptSubmitted, e.Kind));
        Assert.StartsWith("<command-name>", events[0].Text);
    }

    [Fact]
    public void Input_given_as_text_blocks_is_read()
    {
        var events = Without(Run(_b.UserBlocks("ブロックの入力", Origin.Human)), AgentEventKind.SessionStarted);

        Assert.Equal("ブロックの入力", Assert.Single(events).Text);
    }

    // ---- 返答・思考 ----

    [Fact]
    public void Assistant_text_and_thinking_are_kept_with_the_model()
    {
        var events = Without(Run(
            _b.AssistantThinking("考え中", model: "claude-opus-x"),
            _b.AssistantText("こたえ", model: "claude-opus-x")), AgentEventKind.SessionStarted);

        Assert.Equal([AgentEventKind.AssistantThought, AgentEventKind.AssistantMessage], events.Select(e => e.Kind));
        Assert.Equal("考え中", events[0].Text);
        Assert.Equal("こたえ", events[1].Text);
        Assert.Equal("claude-opus-x", events[1].Model);
    }

    [Fact]
    public void Synthetic_model_is_null()
    {
        var events = Run(_b.AssistantText("合成", model: "<synthetic>"));

        Assert.Null(events.Single(e => e.Kind == AgentEventKind.AssistantMessage).Model);
    }

    [Fact]
    public void One_line_with_several_blocks_gives_events_in_order_with_the_same_seq()
    {
        var line = ClaudeLogBuilder.S(new JsonObject
        {
            ["type"] = "assistant", ["uuid"] = "u", ["sessionId"] = "sess-1", ["cwd"] = @"C:\work\proj",
            ["timestamp"] = "2026-10-03T02:00:05.000Z",
            ["message"] = new JsonObject
            {
                ["id"] = "m1", ["role"] = "assistant", ["model"] = "claude-x",
                ["content"] = new JsonArray(
                    new JsonObject { ["type"] = "thinking", ["thinking"] = "t" },
                    new JsonObject { ["type"] = "text", ["text"] = "x" },
                    new JsonObject { ["type"] = "tool_use", ["id"] = "t1", ["name"] = "Read", ["input"] = new JsonObject() }),
            },
        });

        var events = Without(Run(line), AgentEventKind.SessionStarted);

        Assert.Equal([AgentEventKind.AssistantThought, AgentEventKind.AssistantMessage, AgentEventKind.ToolStarted], events.Select(e => e.Kind));
        Assert.All(events, e => Assert.Equal(1, e.Seq));
    }

    // ---- ツール ----

    [Theory]
    [InlineData("Bash")]
    [InlineData("PowerShell")]
    public void Shell_tools_get_the_common_name_and_command(string name)
    {
        var events = Run(_b.Bash("t1", "dotnet test", name: name));

        var e = events.Single(x => x.Kind == AgentEventKind.ToolStarted);
        Assert.Equal(CommonTools.Shell, e.ToolName);
        Assert.Equal("t1", e.ToolUseId);
        Assert.Equal("dotnet test", e.Command);
    }

    [Fact]
    public void Other_tools_keep_their_own_name()
    {
        var events = Run(_b.ToolUse("t1", "mcp__server__tool", new JsonObject { ["q"] = 1 }), _b.ToolUse("t2", "Read", new JsonObject { ["file_path"] = "a.cs" }));

        Assert.Equal(["mcp__server__tool", "Read"], events.Where(e => e.Kind == AgentEventKind.ToolStarted).Select(e => e.ToolName));
    }

    [Fact]
    public void Successful_shell_result_has_exit_code_0_duration_and_output()
    {
        var events = Run(_b.Bash("t1", "dotnet test"), _b.ToolResult("t1", "合格しました"));

        var e = events.Single(x => x.Kind == AgentEventKind.ToolSucceeded);
        Assert.Equal("t1", e.ToolUseId);
        Assert.Equal(CommonTools.Shell, e.ToolName);
        Assert.Equal("dotnet test", e.Command);
        Assert.Equal(0, e.ExitCode);
        Assert.Equal("合格しました", e.Output);
        Assert.Equal(TimeSpan.FromSeconds(1), e.Duration);   // 結果の時刻 − 呼び出しの時刻
        Assert.Equal(2, e.Seq);
    }

    [Theory]
    [InlineData("Bash")]
    [InlineData("PowerShell")]
    public void Exit_code_error_is_a_succeeded_tool_with_that_exit_code(string name)
    {
        var events = Run(_b.Bash("t1", "dotnet test", name: name), _b.ToolResult("t1", "Exit code 1\nFailed!  - 3 tests", isError: true));

        Assert.DoesNotContain(events, e => e.Kind == AgentEventKind.ToolFailed);
        var e = events.Single(x => x.Kind == AgentEventKind.ToolSucceeded);
        Assert.Equal(1, e.ExitCode);
        Assert.Contains("Failed!", e.Output);
    }

    [Fact]
    public void Other_errors_are_a_failed_tool()
    {
        var events = Run(_b.Bash("t1", "rm x"), _b.ToolResult("t1", "The user doesn't want to proceed", isError: true));

        var e = events.Single(x => x.Kind == AgentEventKind.ToolFailed);
        Assert.Equal("t1", e.ToolUseId);
        Assert.Equal(CommonTools.Shell, e.ToolName);
        Assert.Null(e.ExitCode);
        Assert.DoesNotContain(events, x => x.Kind == AgentEventKind.ToolSucceeded);
    }

    [Fact]
    public void Exit_code_text_without_the_error_flag_is_still_a_normal_success()
    {
        var events = Run(_b.Bash("t1", "echo"), _b.ToolResult("t1", "Exit code 9 というファイルの話"));

        Assert.Equal(0, events.Single(x => x.Kind == AgentEventKind.ToolSucceeded).ExitCode);
    }

    [Fact]
    public void Background_bash_has_no_exit_code_when_started_in_background()
    {
        var events = Run(_b.Bash("t1", "npm start", background: true), _b.ToolResult("t1", "Command running in background with ID: x"));

        var e = events.Single(x => x.Kind == AgentEventKind.ToolSucceeded);
        Assert.Null(e.ExitCode);
    }

    [Fact]
    public void Background_bash_has_no_exit_code_when_the_result_has_a_background_task_id()
    {
        var events = Run(
            _b.Bash("t1", "npm start"),
            _b.ToolResult("t1", "Command running in background", toolUseResult: new JsonObject { ["backgroundTaskId"] = "b1" }));

        Assert.Null(events.Single(x => x.Kind == AgentEventKind.ToolSucceeded).ExitCode);
    }

    [Fact]
    public void Non_shell_tools_have_no_exit_code_and_no_output()
    {
        var events = Run(_b.ToolUse("t1", "Read", new JsonObject { ["file_path"] = "a.cs" }), _b.ToolResult("t1", "ファイルの全文…"));

        var e = events.Single(x => x.Kind == AgentEventKind.ToolSucceeded);
        Assert.Equal("Read", e.ToolName);
        Assert.Null(e.ExitCode);
        Assert.Null(e.Output);
    }

    [Fact]
    public void Shell_output_keeps_only_the_last_2000_characters()
    {
        var text = new string('a', 3000) + "END";

        var events = Run(_b.Bash("t1", "cat big"), _b.ToolResult("t1", text));

        var output = events.Single(x => x.Kind == AgentEventKind.ToolSucceeded).Output!;
        Assert.Equal(2000, output.Length);
        Assert.EndsWith("END", output);
    }

    [Fact]
    public void Short_shell_output_is_kept_as_it_is_and_block_content_is_joined()
    {
        var events = Run(_b.Bash("t1", "ls"), _b.ToolResult("t1", "ブロックの結果", contentAsBlocks: true));

        Assert.Equal("ブロックの結果", events.Single(x => x.Kind == AgentEventKind.ToolSucceeded).Output);
    }

    [Fact]
    public void A_result_without_a_known_call_is_still_reported()
    {
        var events = Run(_b.ToolResult("unknown", "x"));

        var e = events.Single(x => x.Kind == AgentEventKind.ToolSucceeded);
        Assert.Equal("unknown", e.ToolUseId);
        Assert.Null(e.ToolName);
        Assert.Null(e.Duration);
    }

    // ---- ファイル編集 ----

    [Theory]
    [InlineData("Edit", "file_path")]
    [InlineData("Write", "file_path")]
    [InlineData("MultiEdit", "file_path")]
    [InlineData("NotebookEdit", "notebook_path")]
    public void Successful_edit_tools_also_give_FileEdited(string tool, string pathKey)
    {
        var events = Run(_b.ToolUse("t1", tool, new JsonObject { [pathKey] = @"C:\work\proj\a.cs" }), _b.ToolResult("t1", "ok"));

        var edited = events.Single(x => x.Kind == AgentEventKind.FileEdited);
        Assert.Equal(@"C:\work\proj\a.cs", edited.FilePath);
        Assert.Contains(events, x => x.Kind == AgentEventKind.ToolSucceeded);
    }

    [Fact]
    public void Failed_edit_is_not_FileEdited()
    {
        var events = Run(_b.ToolUse("t1", "Edit", new JsonObject { ["file_path"] = "a.cs" }), _b.ToolResult("t1", "File has not been read yet", isError: true));

        Assert.DoesNotContain(events, x => x.Kind == AgentEventKind.FileEdited);
        Assert.Contains(events, x => x.Kind == AgentEventKind.ToolFailed);
    }

    [Fact]
    public void Read_and_other_tools_are_not_FileEdited()
    {
        var events = Run(_b.ToolUse("t1", "Read", new JsonObject { ["file_path"] = "a.cs" }), _b.ToolResult("t1", "x"));

        Assert.DoesNotContain(events, x => x.Kind == AgentEventKind.FileEdited);
    }

    // ---- 読み飛ばす・壊れた行 ----

    [Theory]
    [InlineData("attachment")]
    [InlineData("queue-operation")]
    [InlineData("file-history-snapshot")]
    [InlineData("last-prompt")]
    [InlineData("agent-name")]
    [InlineData("mode")]
    [InlineData("permission-mode")]
    [InlineData("cost-state")]
    [InlineData("pr-link")]
    [InlineData("relocated")]
    [InlineData("worktree-state")]
    [InlineData("system")]
    [InlineData("custom-title")]
    [InlineData("ai-title")]
    [InlineData("atis-latch")]   // 実ログ（2.1.286）で見つけた
    public void Noise_records_are_skipped_without_logging(string type)
    {
        var events = Without(Run(_b.Other(type)), AgentEventKind.SessionStarted);
        _formatLog.Flush();

        Assert.Empty(events);
        Assert.Empty(Logs);
    }

    [Fact]
    public void Broken_and_empty_lines_are_skipped_and_numbering_continues()
    {
        var events = Run(_b.User("a"), "{ not json", "", "[1,2]", _b.User("b"));

        var prompts = events.Where(e => e.Kind == AgentEventKind.PromptSubmitted).ToList();
        Assert.Equal([1L, 5L], prompts.Select(e => e.Seq));
    }

    [Fact]
    public void Lines_with_wrong_field_types_do_not_stop_the_reading()
    {
        var events = Run(
            """{"type":"user","timestamp":"2026-10-03T02:00:01.000Z","message":{"content":42}}""",
            """{"type":"assistant","timestamp":"2026-10-03T02:00:02.000Z","message":"text"}""",
            """{"type":"assistant","timestamp":"2026-10-03T02:00:03.000Z","message":{"content":[{"type":"tool_use","id":7,"name":"Bash","input":"x"}]}}""",
            """{"type":"user","timestamp":"2026-10-03T02:00:04.000Z","message":{"content":[{"type":"tool_result","tool_use_id":null,"content":{"a":1},"is_error":"no"}]}}""",
            """{"type":123,"timestamp":"2026-10-03T02:00:05.000Z"}""",
            """{"type":"user","timestamp":["x"],"message":{"content":"a"}}""",
            _b.User("読める行"));

        Assert.Contains(events, e => e.Kind == AgentEventKind.PromptSubmitted && e.Text == "読める行");
    }

    // ---- 形式の変化のログ（要件 11.1・計画 8.5） ----

    [Fact]
    public void Unreadable_lines_are_logged_with_file_line_type_and_version_but_never_the_content()
    {
        Run(_b.User("はじまり"),
            """{"type":"user","version":"2.1.200","timestamp":"2026-10-03T02:00:02.000Z","message":{"content":42,"secret":"漏れてはいけない本文"}}""");

        var log = Assert.Single(_logs, l => l.Contains("sess-1.jsonl:2"));
        Assert.Contains("user", log);
        Assert.Contains("2.1.200", log);
        Assert.DoesNotContain("漏れてはいけない本文", log);
        Assert.DoesNotContain("はじまり", string.Join("\n", _logs));
    }

    [Fact]
    public void Broken_json_is_logged_without_the_content()
    {
        Run("{ 漏れてはいけない壊れた本文");

        var log = Assert.Single(Logs);
        Assert.Contains("sess-1.jsonl:1", log);
        Assert.DoesNotContain("漏れてはいけない", log);
    }

    [Fact]
    public void The_same_kind_of_error_is_logged_once_per_file_and_counted_at_the_end()
    {
        var bad = """{"type":"user","timestamp":"2026-10-03T02:00:02.000Z","message":{"content":42}}""";

        Run(bad, bad, bad, bad);
        Assert.Single(Logs);

        _normalizer.FlushLog();

        Assert.Equal(2, Logs.Count);
        Assert.Contains("3", Logs[1]);   // ほかに 3 件
        _normalizer.FlushLog();
        Assert.Equal(2, Logs.Count);     // 件数は 1 回だけ
    }

    [Fact]
    public void Unknown_record_types_are_logged_once_per_launch_with_a_count()
    {
        var other = new ClaudeTranscriptNormalizer("sess-2", "sess-2.jsonl", _formatLog);

        Run(_b.Other("brand-new-kind"), _b.Other("brand-new-kind"));
        other.NormalizeLine(1, new ClaudeLogBuilder("sess-2").Other("brand-new-kind"));
        Assert.Empty(Logs);   // 数えるだけ

        _formatLog.Flush();

        var log = Assert.Single(_logs, l => l.Contains("brand-new-kind"));
        Assert.Contains("3", log);
        _formatLog.Flush();
        Assert.Single(_logs, l => l.Contains("brand-new-kind"));   // 起動ごとに 1 回
    }

    [Fact]
    public void A_version_seen_for_the_first_time_is_logged_once()
    {
        Run(_b.User("a"), _b.User("b"));
        var other = new ClaudeTranscriptNormalizer("sess-2", "sess-2.jsonl", _formatLog);
        other.NormalizeLine(1, new ClaudeLogBuilder("sess-2").User("c"));
        other.NormalizeLine(2, new ClaudeLogBuilder("sess-2", version: "2.2.0").User("d"));

        Assert.Equal(1, _logs.Count(l => l.Contains("2.1.200")));
        Assert.Equal(1, _logs.Count(l => l.Contains("2.2.0")));
    }

    [Fact]
    public void Normalizer_works_without_a_log()
    {
        var plain = new ClaudeTranscriptNormalizer("sess-1", "sess-1.jsonl");

        var events = plain.NormalizeLine(1, "{ broken");
        events = [.. events, .. plain.NormalizeLine(2, _b.User("a"))];

        Assert.Contains(events, e => e.Kind == AgentEventKind.PromptSubmitted);
    }
}
