// Step 0: Cursor hook の入力をそのまま保存するだけのダンプ用 hook
// 保存先: %LOCALAPPDATA%\Miharikun\dump\{conversation_id}.jsonl
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// 承認系 hook: exit 0 で JSON を返すと許可/拒否の判定に参加してしまう。
// 記録だけしたいので「何も出力せず exit 1（失敗扱い＝fail-open）」にする。※Step0で挙動を確認する
var permissionHooks = new HashSet<string> {
    "preToolUse", "beforeShellExecution", "beforeMCPExecution",
    "beforeReadFile", "beforeTabFileRead", "subagentStart"
};

string raw = "";
string evt = "unknown";
try
{
    Console.InputEncoding = Encoding.UTF8;
    raw = await Console.In.ReadToEndAsync();
    var node = JsonNode.Parse(raw);
    evt = node?["hook_event_name"]?.GetValue<string>() ?? "unknown";
    var conv = node?["conversation_id"]?.GetValue<string>() ?? "_app";

    var dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Miharikun", "dump");
    Directory.CreateDirectory(dir);

    var line = new JsonObject
    {
        ["received_at"] = DateTimeOffset.Now.ToString("o"),
        ["event"] = evt,
        ["pid"] = Environment.ProcessId,
        ["cwd"] = Environment.CurrentDirectory,
        ["env"] = new JsonObject
        {
            ["CURSOR_PROJECT_DIR"] = Environment.GetEnvironmentVariable("CURSOR_PROJECT_DIR"),
            ["CURSOR_VERSION"] = Environment.GetEnvironmentVariable("CURSOR_VERSION"),
            ["CURSOR_TRANSCRIPT_PATH"] = Environment.GetEnvironmentVariable("CURSOR_TRANSCRIPT_PATH"),
        },
        ["payload"] = node?.DeepClone()
    };

    var path = Path.Combine(dir, $"{conv}.jsonl");
    // 複数 hook が同時に走っても壊れにくいよう、共有ロック付きでリトライ追記
    for (var i = 0; i < 10; i++)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var bytes = Encoding.UTF8.GetBytes(line.ToJsonString() + "\n");
            fs.Write(bytes);
            break;
        }
        catch (IOException) { Thread.Sleep(20); }
    }
}
catch (Exception ex)
{
    try
    {
        var err = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "Miharikun", "dump-error.log");
        File.AppendAllText(err, $"{DateTimeOffset.Now:o} {evt} {ex}\n{raw}\n");
    }
    catch { }
    return 1; // 失敗時は fail-open
}

if (permissionHooks.Contains(evt)) return 1; // 判定に参加しない

// beforeSubmitPrompt は送信を止めないよう明示的に continue:true
Console.Out.Write(evt == "beforeSubmitPrompt" ? "{\"continue\":true}" : "{}");
return 0;
