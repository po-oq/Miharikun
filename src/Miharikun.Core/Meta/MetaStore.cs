using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Miharikun.Core.Storage;

namespace Miharikun.Core.Meta;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(SessionMeta))]
internal sealed partial class MetaJsonContext : JsonSerializerContext;

/// <summary>meta\{agent}\{session}.json の読み書き。書き込みは一時ファイル → File.Replace でアトミックに行う（要件 6章）。</summary>
public sealed class MetaStore(AppPaths paths, string agentId, Action<string>? log = null)
{
    private const int ReplaceRetryCount = 20;
    private const int ReplaceRetryDelayMs = 10;

    private static readonly MetaJsonContext Context = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    });

    /// <summary>ファイルがなければ空のメタ。壊れていれば .bad に退避して空のメタ（次の保存で上書きされても元を失わないため）。</summary>
    public SessionMeta Load(string sessionId)
    {
        var path = paths.MetaFile(agentId, sessionId);
        string json;
        try
        {
            if (!File.Exists(path))
                return new SessionMeta();
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            log?.Invoke($"{path} を読めない: {ex.Message}");
            return new SessionMeta();
        }

        try
        {
            return JsonSerializer.Deserialize(json, Context.SessionMeta) ?? new SessionMeta();
        }
        catch (JsonException ex)
        {
            log?.Invoke($"{path} が壊れている（.bad に退避）: {ex.Message}");
            try { File.Copy(path, path + ".bad", overwrite: true); } catch (IOException) { }
            return new SessionMeta();
        }
    }

    public void Save(string sessionId, SessionMeta meta)
    {
        var path = paths.MetaFile(agentId, sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(meta, Context.SessionMeta));
            // 複数起動が同時に置き換えると Windows が拒否することがある。後勝ちでよいので、少し待ってやり直す。
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                        File.Replace(temp, path, null);
                    else
                        File.Move(temp, path);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < ReplaceRetryCount)
                {
                    Thread.Sleep(ReplaceRetryDelayMs);
                }
            }
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
        DeleteReplaceLeftovers(path);
    }

    /// <summary>
    /// 置き換えが競合で失敗すると Windows は「名前~RFxxxx.TMP」を残すことがある。本体は無事なので、気づいた時に消す。
    /// </summary>
    private static void DeleteReplaceLeftovers(string path)
    {
        try
        {
            foreach (var leftover in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "~RF*.TMP"))
            {
                try { File.Delete(leftover); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
    }
}