using System.Text;
using System.Text.Json.Nodes;
using Miharikun.Core.Storage;
using Miharikun.Hook;

namespace Miharikun.Tests.Hook;

/// <summary>
/// 日本語環境の Windows で Cursor が hook に渡す入力は、PowerShell の変換（一時ファイルを CP932 で読み、UTF-8＋BOM で出力）で壊れる。
/// その壊れ方をそのまま再現して、元の一時ファイルから正しい内容を取り戻せることを確かめる。
/// </summary>
public sealed class PayloadRecoveryTests : IDisposable
{
    private static readonly Encoding Cp932;

    static PayloadRecoveryTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp932 = Encoding.GetEncoding(932);
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-recovery-" + Guid.NewGuid().ToString("N"));
    private readonly string _tmp;
    private readonly AppPaths _paths;

    public PayloadRecoveryTests()
    {
        _tmp = Path.Combine(_dir, "tmp");
        Directory.CreateDirectory(_tmp);
        _paths = new AppPaths(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>Cursor 側：BOM なし UTF-8 で一時ファイルに書く。</summary>
    private string WriteCursorTempFile(string json, string? name = null, DateTime? lastWrite = null)
    {
        var path = Path.Combine(_tmp, name ?? $"cursor-hook-payload-{Environment.ProcessId}-{Guid.NewGuid():N}.json");
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(json));
        if (lastWrite is { } t) File.SetLastWriteTimeUtc(path, t);
        return path;
    }

    /// <summary>PowerShell の変換：UTF-8 のバイト列を CP932 の文字列として読み、UTF-8＋BOM（＋改行）で出す。</summary>
    private static byte[] ThroughPowerShell(string json) =>
        new UTF8Encoding(true).GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes(Cp932.GetString(new UTF8Encoding(false).GetBytes(json)) + "\n"))
            .ToArray();

    private (int Exit, string Out) Run(byte[] input)
    {
        using var stdin = new MemoryStream(input);
        var stdout = new StringWriter();
        var exit = HookRunner.Run(["--agent", "cursor"], stdin, stdout, _paths, tempDir: _tmp);
        return (exit, stdout.ToString());
    }

    private JsonNode? LoadedEvent(string conv) =>
        File.Exists(_paths.EventFile("cursor", conv)) ? JsonNode.Parse(File.ReadAllLines(_paths.EventFile("cursor", conv))[0]) : null;

    private static string Prompt(string text, string conv = "c1") =>
        "{\"conversation_id\":\"" + conv + "\",\"generation_id\":\"g1\",\"model\":\"grok-4.7\",\"prompt\":\"" + text +
        "\",\"hook_event_name\":\"beforeSubmitPrompt\",\"workspace_roots\":[\"/c:/work/proj\"]}";

    [Fact]
    public void The_mangling_is_reproduced_exactly_as_seen_on_the_real_machine()
    {
        // 実機で記録された値：「@docs/ 内容教えて」→「@docs/ 蜀・ｮｹ謨吶∴縺ｦ」
        var mangled = Cp932.GetString(Encoding.UTF8.GetBytes("@docs/ 内容教えて"));

        Assert.Equal("@docs/ 蜀・ｮｹ謨吶∴縺ｦ", mangled);
    }

    [Fact]
    public void Japanese_prompt_is_restored_from_the_original_temp_file()
    {
        var json = Prompt("@docs/ 内容教えて");
        WriteCursorTempFile(json);

        var (exit, output) = Run(ThroughPowerShell(json));

        Assert.Equal(0, exit);
        Assert.Equal("{\"continue\":true}", output);
        var payload = LoadedEvent("c1")!["payload"]!;
        Assert.Equal("@docs/ 内容教えて", (string)payload["prompt"]!);
        Assert.Null(payload["_salvaged"]);
        Assert.False(Directory.Exists(Path.Combine(_dir, "logs", "bad-input")));
    }

    [Fact]
    public void Input_that_became_invalid_json_is_restored_in_full()
    {
        // 末尾の「。」(E3 80 82) の直後の「"」が失われて、JSON が壊れる（実機の afterAgentThought と同じ）
        var json = "{\"conversation_id\":\"c2\",\"generation_id\":\"g1\",\"text\":\"AgentEvent.cs の説明に必要な情報が揃った。\"," +
                   "\"duration_ms\":1,\"hook_event_name\":\"afterAgentThought\",\"workspace_roots\":[\"/c:/work/proj\"]}";
        var broken = ThroughPowerShell(json);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => JsonNode.Parse(Encoding.UTF8.GetString(broken).TrimStart('\uFEFF')));   // 前提：本当に壊れている
        WriteCursorTempFile(json);

        var (exit, _) = Run(broken);

        Assert.Equal(0, exit);
        var payload = LoadedEvent("c2")!["payload"]!;
        Assert.Equal("AgentEvent.cs の説明に必要な情報が揃った。", (string)payload["text"]!);
        Assert.Equal(1, (int)payload["duration_ms"]!);
        Assert.Null(payload["_salvaged"]);
    }

    [Fact]
    public void Long_response_with_newlines_quotes_and_code_is_restored()
    {
        var text = "`docs/` は設計用の資料です。\\n\\n| ファイル | 役割 |\\n|---|---|\\n| `a.md` | **仕様の正** |\\n\\\"引用\\\"もあります。";
        var json = "{\"conversation_id\":\"c3\",\"generation_id\":\"g1\",\"text\":\"" + text + "\",\"hook_event_name\":\"afterAgentResponse\"}";
        WriteCursorTempFile(json);

        Run(ThroughPowerShell(json));

        var restored = (string)LoadedEvent("c3")!["payload"]!["text"]!;
        Assert.StartsWith("`docs/` は設計用の資料です。\n\n| ファイル | 役割 |", restored);
        Assert.Contains("\"引用\"もあります。", restored);
    }

    [Fact]
    public void The_matching_file_is_chosen_among_several_concurrent_ones()
    {
        var a = Prompt("一つ目の依頼です", "ca");
        var b = Prompt("二つ目の依頼です", "cb");
        WriteCursorTempFile(a);
        WriteCursorTempFile(b);

        Run(ThroughPowerShell(b));

        Assert.Equal("二つ目の依頼です", (string)LoadedEvent("cb")!["payload"]!["prompt"]!);
    }

    [Fact]
    public void Identical_payloads_written_for_two_hooks_are_fine()
    {
        // 同じイベントに hook が2つあると、同じ内容の一時ファイルが2つできる
        var json = Prompt("同じ内容の依頼");
        WriteCursorTempFile(json);
        WriteCursorTempFile(json);

        Run(ThroughPowerShell(json));

        Assert.Equal("同じ内容の依頼", (string)LoadedEvent("c1")!["payload"]!["prompt"]!);
    }

    [Fact]
    public void Falls_back_to_the_unique_file_with_the_same_event_and_ids_when_the_text_does_not_match_exactly()
    {
        var json = Prompt("依頼の本文");
        WriteCursorTempFile(json);
        // CP932 の読み替え方が環境で違っても拾えるよう、受け取った文字列が少し違うものを作る
        var received = new UTF8Encoding(true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(Prompt("違う読み替えの結果") + "\n")).ToArray();

        Run(received);

        Assert.Equal("依頼の本文", (string)LoadedEvent("c1")!["payload"]!["prompt"]!);
    }

    [Fact]
    public void Ambiguous_candidates_are_not_guessed()
    {
        // 同じ会話・同じイベントで内容が違う2つ。受け取った文字列とも一致しないなら、取り違えを避けて採用しない
        WriteCursorTempFile(Prompt("依頼A"));
        WriteCursorTempFile(Prompt("依頼B"));
        var received = new UTF8Encoding(true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(Prompt("どちらでもない") + "\n")).ToArray();

        Run(received);

        Assert.Equal("どちらでもない", (string)LoadedEvent("c1")!["payload"]!["prompt"]!);   // 受け取った値のまま
    }

    [Fact]
    public void Stale_temp_files_are_ignored()
    {
        var json = Prompt("古い一時ファイル");
        WriteCursorTempFile(json, lastWrite: DateTime.UtcNow.AddMinutes(-10));

        Run(ThroughPowerShell(json));

        Assert.NotEqual("古い一時ファイル", (string)LoadedEvent("c1")!["payload"]!["prompt"]!);
    }

    [Fact]
    public void Ascii_only_input_is_never_replaced_by_a_temp_file()
    {
        var real = Prompt("plain ascii prompt");
        WriteCursorTempFile(Prompt("decoy that must not be used"));

        Run(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(real + "\n")).ToArray());

        Assert.Equal("plain ascii prompt", (string)LoadedEvent("c1")!["payload"]!["prompt"]!);
    }

    [Fact]
    public void Input_without_bom_is_trusted_as_is()
    {
        // PowerShell 7 などで変換が起きていないとき：日本語はそのまま、一時ファイルは見に行かない
        var json = Prompt("そのままの日本語");
        WriteCursorTempFile(Prompt("使われてはいけないおとり"));

        Run(Encoding.UTF8.GetBytes(json));

        Assert.Equal("そのままの日本語", (string)LoadedEvent("c1")!["payload"]!["prompt"]!);
    }

    [Fact]
    public void Without_any_temp_file_the_broken_event_is_still_salvaged()
    {
        var json = "{\"conversation_id\":\"c4\",\"text\":\"説明です。\",\"hook_event_name\":\"afterAgentResponse\"}";
        var broken = ThroughPowerShell(json);

        var (exit, _) = Run(broken);

        Assert.Equal(0, exit);
        var node = LoadedEvent("c4")!;
        Assert.Equal("afterAgentResponse", (string)node["event"]!);
        Assert.True((bool)node["payload"]!["_salvaged"]!);
    }
}