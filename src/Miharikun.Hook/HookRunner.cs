using System.Text;
using System.Text.Json.Nodes;
using Miharikun.Core.Agents;
using Miharikun.Core.Storage;

namespace Miharikun.Hook;

public static class HookRunner
{
    private const int AppendRetryCount = 10;
    private const int AppendRetryDelayMs = 20;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// stdin の JSON を events\{agent}\{session}.jsonl に追記し、stdout と終了コードを返す（要件 7章）。
    /// 例外時は何も出力せず exit 1（fail-open）にして hook-error.log に記録する。
    /// </summary>
    public static int Run(string[] args, Stream stdin, TextWriter stdout, AppPaths paths, Func<DateTimeOffset>? clock = null)
    {
        var eventName = "unknown";
        try
        {
            var agent = AgentRegistry.Find(ParseAgentArg(args));
            if (agent is null)
                return 1;

            var raw = ReadAll(stdin);
            var payload = JsonNode.Parse(raw) ?? throw new InvalidDataException("payload is null");

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
                var root = CursorAgent.GetWorkspaceRoots(payload).FirstOrDefault();
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

    private static string ReadAll(Stream stdin)
    {
        using var reader = new StreamReader(stdin, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
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