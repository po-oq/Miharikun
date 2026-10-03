using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Miharikun.Core.Storage;
using Miharikun.Hook;

namespace Miharikun.Tests.Hook;

public sealed class HookRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-test-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public HookRunnerTests() => _paths = new AppPaths(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (int Exit, string Out) Run(string json, params string[] args)
    {
        if (args.Length == 0) args = ["--agent", "cursor"];
        using var stdin = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var stdout = new StringWriter();
        var exit = HookRunner.Run(args, stdin, stdout, _paths);
        return (exit, stdout.ToString());
    }

    private static string Payload(string eventName, string conv = "conv-1", string extra = "") =>
        $$"""{"hook_event_name":"{{eventName}}","conversation_id":"{{conv}}","workspace_roots":["C:\\nonexistent-root"]{{extra}}}""";

    private string[] Lines(string conv = "conv-1") => File.ReadAllLines(_paths.EventFile("cursor", conv));

    [Theory]
    [InlineData("preToolUse", null, 1)]
    [InlineData("subagentStart", null, 1)]
    [InlineData("beforeSubmitPrompt", """{"continue":true}""", 0)]
    [InlineData("stop", "{}", 0)]
    [InlineData("postToolUse", "{}", 0)]
    [InlineData("sessionEnd", "{}", 0)]
    public void Output_and_exit_code_follow_the_table(string evt, string? expectedOut, int expectedExit)
    {
        var (exit, output) = Run(Payload(evt));

        Assert.Equal(expectedExit, exit);
        Assert.Equal(expectedOut ?? "", output);
        Assert.Single(Lines());   // 承認系でも記録はされる
    }

    [Fact]
    public void Appends_one_line_with_schema_and_untouched_payload()
    {
        var json = Payload("beforeSubmitPrompt", extra: ",\"prompt\":\"日本語のプロンプト\\n2行目\",\"n\":1.50");
        Run(json);
        Run(json);

        var lines = Lines();
        Assert.Equal(2, lines.Length);

        var node = JsonNode.Parse(lines[0])!;
        Assert.Equal(1, (int)node["v"]!);
        Assert.Equal("cursor", (string)node["agent"]!);
        Assert.Equal("beforeSubmitPrompt", (string)node["event"]!);
        Assert.NotNull(node["received_at"]);
        Assert.Null(node["git"]);
        Assert.Equal("日本語のプロンプト\n2行目", (string)node["payload"]!["prompt"]!);
        Assert.Contains("日本語のプロンプト", lines[0]);   // \uXXXX にエスケープしない
        Assert.Equal(JsonNode.Parse(json)!.ToJsonString(), node["payload"]!.ToJsonString());
    }

    [Fact]
    public void Events_are_split_by_conversation_id()
    {
        Run(Payload("stop", "a"));
        Run(Payload("stop", "b"));

        Assert.Single(Lines("a"));
        Assert.Single(Lines("b"));
    }

    [Fact]
    public void Missing_conversation_id_goes_to_app_file()
    {
        var (exit, _) = Run("""{"hook_event_name":"sessionStart"}""");

        Assert.Equal(0, exit);
        Assert.Single(Lines("_app"));
    }

    [Fact]
    public void Path_characters_in_conversation_id_cannot_escape_events_dir()
    {
        var (exit, _) = Run(Payload("stop", "..\\\\..\\\\evil/x"));   // JSON 上のエスケープ済み: ..\..\evil/x

        Assert.Equal(0, exit);
        Assert.False(File.Exists(Path.Combine(_dir, "evil.jsonl")));
        Assert.Single(Directory.GetFiles(_paths.EventsDir("cursor")));
    }

    [Fact]
    public void Utf8_bom_on_stdin_is_accepted()
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Payload("stop"))).ToArray();
        using var stdin = new MemoryStream(bytes);

        var exit = HookRunner.Run(["--agent", "cursor"], stdin, new StringWriter(), _paths);

        Assert.Equal(0, exit);
        Assert.Single(Lines());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{}")]
    public void Bad_input_is_fail_open_with_error_log(string input)
    {
        var (exit, output) = Run(input);

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.True(File.Exists(_paths.HookErrorLog));
        Assert.False(Directory.Exists(_paths.EventsDir("cursor")));
    }

    [Theory]
    [InlineData("--agent", "unknown")]
    [InlineData("--agent")]
    public void Unknown_or_missing_agent_exits_1(params string[] args)
    {
        var (exit, output) = Run(Payload("stop"), args);

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.False(Directory.Exists(_paths.EventsDir("cursor")));
    }

    [Fact]
    public void No_agent_argument_exits_1()
    {
        var (exit, _) = Run(Payload("stop"), "--other");

        Assert.Equal(1, exit);
    }

    [Fact]
    public void Write_failure_is_logged_and_exits_1()
    {
        Directory.CreateDirectory(_paths.EventsDir("cursor"));
        Directory.CreateDirectory(_paths.EventFile("cursor", "conv-1"));   // ファイルの場所にディレクトリがある

        var (exit, output) = Run(Payload("beforeSubmitPrompt"));

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.True(File.Exists(_paths.HookErrorLog));
    }

    [Fact]
    public void Concurrent_appends_keep_every_line_intact()
    {
        Parallel.For(0, 40, i => Run(Payload("postToolUse", extra: $",\"i\":{i}")));

        var lines = Lines();
        Assert.Equal(40, lines.Length);
        Assert.All(lines, l => Assert.NotNull(JsonNode.Parse(l)));
    }

    [Theory]
    [InlineData("sessionStart")]
    [InlineData("stop")]
    public void Git_info_is_attached_only_for_start_and_stop_in_a_repo(string evt)
    {
        var repo = Path.Combine(_dir, "repo");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-b", "feature/x");
        Git(repo, "-c", "user.name=t", "-c", "user.email=t@example.com", "commit", "--allow-empty", "-m", "init");
        var head = Git(repo, "rev-parse", "HEAD");
        var json = $$"""{"hook_event_name":"{{evt}}","conversation_id":"conv-1","workspace_roots":["{{repo.Replace("\\", "\\\\")}}"]}""";

        Run(json);
        Run(json.Replace(evt, "postToolUse"));

        var lines = Lines();
        var withGit = JsonNode.Parse(lines[0])!["git"]!;
        Assert.Equal("feature/x", (string)withGit["branch"]!);
        Assert.Equal(head, (string)withGit["head"]!);
        Assert.Null(JsonNode.Parse(lines[1])!["git"]);
    }

    [Fact]
    public void Git_info_is_omitted_when_not_a_repo()
    {
        var plain = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(plain);
        var json = $$"""{"hook_event_name":"stop","conversation_id":"conv-1","workspace_roots":["{{plain.Replace("\\", "\\\\")}}"]}""";

        var (exit, _) = Run(json);

        Assert.Equal(0, exit);
        Assert.Null(JsonNode.Parse(Lines()[0])!["git"]);
    }

    [Fact]
    public void Real_process_honours_stdout_and_exit_code()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Miharikun.Hook.dll");
        var (exitPrompt, outPrompt) = RunProcess(dll, Payload("beforeSubmitPrompt"));
        var (exitTool, outTool) = RunProcess(dll, Payload("preToolUse"));

        Assert.Equal(0, exitPrompt);
        Assert.Equal("""{"continue":true}""", outPrompt);
        Assert.Equal(1, exitTool);
        Assert.Equal("", outTool);
        Assert.Equal(2, Lines().Length);
    }

    private (int Exit, string Out) RunProcess(string dll, string stdinText)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add("--agent");
        psi.ArgumentList.Add("cursor");
        psi.Environment[AppPaths.DataDirEnvVar] = _dir;

        using var p = Process.Start(psi)!;
        p.StandardInput.Write(stdinText);
        p.StandardInput.Close();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    private static string Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(dir);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return o;
    }
}