using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using Miharikun.Core.Agents;
using Miharikun.Core.Projects;
using Miharikun.Core.Storage;

namespace Miharikun.Hook;

public static class HookRunner
{
    private const int AppendRetryCount = 10;
    private const int AppendRetryDelayMs = 20;
    private const int MaxBadInputFiles = 20;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// stdin の JSON を events\{agent}\{session}.jsonl に追記し、stdout と終了コードを返す（要件 7章）。
    /// 例外時は何も出力せず exit 1（fail-open）にして hook-error.log に記録する。
    /// </summary>
    public static int Run(string[] args, Stream stdin, TextWriter stdout, AppPaths paths, Func<DateTimeOffset>? clock = null,
        string? tempDir = null)
    {
        var eventName = "unknown";
        try
        {
            var agent = AgentRegistry.Find(ParseAgentArg(args));
            if (agent is null)
                return 1;

            var bytes = ReadAllBytes(stdin);
            var raw = Utf8NoBom.GetString(bytes).TrimStart('\uFEFF');

            JsonNode? payload;
            string? parseError = null;
            try
            {
                payload = JsonNode.Parse(raw);
            }
            catch (JsonException ex)
            {
                payload = null;
                parseError = ex.Message;
            }

            // 日本語環境の Windows では、PowerShell の変換で入力が化ける。元の一時ファイルが見つかれば、正しい内容に差し替える。
            if (payload is null || PayloadRecovery.MaybeMangled(bytes, raw))
            {
                var hint = RecoveryHint.From(payload ?? PayloadSalvage.TryRecover(raw, parseError ?? ""));
                var original = PayloadRecovery.TryFindOriginal(tempDir ?? Path.GetTempPath(), raw, hint);
                if (original is not null)
                {
                    raw = original;
                    payload = JsonNode.Parse(original);
                    parseError = null;
                }
            }

            if (payload is null)
            {
                // 読めない入力は、あとで原因を調べられるよう元のバイト列を残し、救えるだけ救って記録する。
                SaveBadInput(paths, bytes);
                payload = PayloadSalvage.TryRecover(raw, parseError ?? "payload is null")
                          ?? throw new InvalidDataException($"JSON を読めず、イベント名も拾えない: {parseError}");
                LogError(paths, (string?)payload["hook_event_name"] ?? "unknown",
                    new InvalidDataException($"JSON を読めなかったため、イベント名などだけ救出して記録した: {parseError}"));
            }

            eventName = agent.GetEventName(payload) ?? throw new InvalidDataException("hook_event_name がない");
            var sessionId = agent.GetSessionId(payload) ?? AppPaths.AppSessionId;

            var line = new EventLine
            {
                Agent = agent.Id,
                ReceivedAt = (clock ?? (() => DateTimeOffset.Now))(),
                Event = eventName,
                Payload = payload,
            };

            if (agent.NeedsGitSnapshot(eventName))
            {
                // Cursor は「/c:/dir」の形で渡してくるので、git に渡せるパスに直す
                var root = ProjectPath.Normalize(CursorAgent.GetWorkspaceRoots(payload).FirstOrDefault());
                if (root is not null)
                    line.Git = GitProbe.TryGet(root);
            }

            Append(paths.EventFile(agent.Id, sessionId), EventLineJson.Serialize(line) + "\n");

            var response = agent.RespondToHook(eventName, payload);
            if (response.Stdout is not null)
                stdout.Write(response.Stdout);
            return response.ExitCode;
        }
        catch (Exception ex)
        {
            LogError(paths, eventName, ex);
            return 1;
        }
    }

    private static string? ParseAgentArg(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--agent")
                return args[i + 1];
        }
        return null;
    }

    private static byte[] ReadAllBytes(Stream stdin)
    {
        using var buffer = new MemoryStream();
        stdin.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>読めなかった入力のバイト列を logs\bad-input\ に残す（新しい20件まで）。</summary>
    private static void SaveBadInput(AppPaths paths, byte[] bytes)
    {
        try
        {
            var dir = Path.Combine(paths.Root, "logs", "bad-input");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.bin"), bytes);

            foreach (var old in new DirectoryInfo(dir).GetFiles("*.bin").OrderByDescending(f => f.Name).Skip(MaxBadInputFiles))
                old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 調査用なので、残せなくても止めない
        }
    }

    private static void Append(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Utf8NoBom.GetBytes(text);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                fs.Write(bytes);
                return;
            }
            catch (IOException) when (attempt < AppendRetryCount)
            {
                Thread.Sleep(AppendRetryDelayMs);
            }
        }
    }

    private static void LogError(AppPaths paths, string eventName, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.HookErrorLog)!);
            File.AppendAllText(paths.HookErrorLog, $"{DateTimeOffset.Now:o} {eventName} {ex}\n", Utf8NoBom);
        }
        catch
        {
            // ログも書けないときは諦める。Cursor を止めない。
        }
    }
}