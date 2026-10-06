using System.Text.Json.Nodes;

namespace Miharikun.Core.Install;

/// <summary>
/// Cursor の hooks.json に Miharikun の Hook が登録された時刻（要件 11章・計画 7.4）。
/// transcript の最後の更新がこれより後なのに Hook の記録が無ければ、Hook が動いていない（<c>NoHook</c>）と判断する。
/// 読むだけ。hooks.json も events も変更しない。
/// </summary>
public sealed class HookRegistration(string hooksJsonPath, string eventsDir)
{
    /// <summary>hooks.json の更新日時。無い・読めないときは null。stat だけで、中身は読まない（ポーリングのたびに呼べる）。</summary>
    public DateTimeOffset? Stamp()
    {
        try
        {
            var info = new FileInfo(hooksJsonPath);
            return info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 登録の時刻：hooks.json に Miharikun の登録があるとき、「hooks.json の更新日時」と「events の一番古いファイルの作成日時」の早いほう。
    /// 登録が無い・hooks.json が無い・壊れているときは null（すべて「導入前」と同じ扱い）。
    /// </summary>
    public DateTimeOffset? Since()
    {
        if (Stamp() is not { } stamp || !HasOurEntry())
            return null;

        var since = stamp;
        try
        {
            if (Directory.Exists(eventsDir))
            {
                foreach (var file in Directory.GetFiles(eventsDir, "*.jsonl"))
                {
                    var created = new DateTimeOffset(File.GetCreationTimeUtc(file), TimeSpan.Zero);
                    if (created < since)
                        since = created;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // events を読めなくても、hooks.json の更新日時は使える
        }
        return since;
    }

    private bool HasOurEntry()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(hooksJsonPath)) is not JsonObject { } root || root["hooks"] is not JsonObject hooks)
                return false;

            foreach (var (_, value) in hooks)
            {
                if (value is not JsonArray array)
                    continue;
                foreach (var entry in array)
                {
                    if (entry is JsonObject o && o["command"] is JsonValue v && v.TryGetValue<string>(out var command)
                        && HookInstaller.IsOurs(command))
                        return true;
                }
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
