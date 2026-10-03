using System.Text.Json;
using System.Text.Json.Nodes;
using Miharikun.Core.Documents;
using Miharikun.Core.Projects;
using Miharikun.Core.Storage;

namespace Miharikun.Core.Settings;

/// <summary>プロジェクトごとの、ドキュメントタブの設定。null は「未設定」。</summary>
public sealed record ProjectDocumentSettings(string? Ignore, string? LastOpened);

/// <summary>
/// projects\{slug}-{hash8}.json の読み書き（要件 6章）。AOT 互換のため JsonNode で扱う。
/// 保存は毎回「読む → 該当キーだけ変える → 全体を書く」（他のキー・知らないキーを消さない）。
/// </summary>
public sealed class ProjectSettingsStore(AppPaths paths, Action<string>? log = null)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public ProjectDocumentSettings Load(string projectPath)
    {
        var documents = ReadRoot(paths.ProjectSettingsFile(projectPath))["documents"] as JsonObject;
        return new ProjectDocumentSettings(String(documents, "ignore"), String(documents, "lastOpened"));
    }

    /// <summary>除外パターン。未設定（ファイルなし・キーなし）のときは既定値。空文字は「除外なし」として保つ。</summary>
    public string IgnoreTextOrDefault(string projectPath) => Load(projectPath).Ignore ?? DefaultIgnore.Text;

    /// <summary>最後に開いたファイルの相対パス。ファイルが消えていたら null（復元しない）。</summary>
    public string? LastOpenedExisting(string projectPath)
    {
        var rel = Load(projectPath).LastOpened;
        if (string.IsNullOrEmpty(rel))
            return null;
        var full = Path.Combine(projectPath, rel.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? rel : null;
    }

    public void SaveIgnore(string projectPath, string text) =>
        Update(projectPath, documents => documents["ignore"] = text);

    public void SaveLastOpened(string projectPath, string? relativePath) =>
        Update(projectPath, documents =>
        {
            if (relativePath is null)
                documents.Remove("lastOpened");
            else
                documents["lastOpened"] = relativePath;
        });

    private void Update(string projectPath, Action<JsonObject> change)
    {
        var file = paths.ProjectSettingsFile(projectPath);
        var root = ReadRoot(file);

        root["v"] ??= 1;
        root["path"] = ProjectPath.Normalize(projectPath) ?? projectPath;
        if (root["documents"] is not JsonObject documents)
        {
            documents = new JsonObject();
            root["documents"] = documents;
        }
        change(documents);

        try
        {
            AtomicFile.WriteAllText(file, root.ToJsonString(Indented));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"{file} に書けない: {ex.Message}");
        }
    }

    /// <summary>ファイルがない・壊れている・オブジェクトでないときは、空のオブジェクト。</summary>
    private JsonObject ReadRoot(string file)
    {
        try
        {
            if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonObject obj)
                return obj;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            log?.Invoke($"{file} を読めない（既定の設定を使う）: {ex.Message}");
        }
        return new JsonObject();
    }

    private static string? String(JsonObject? obj, string key) =>
        obj?[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
