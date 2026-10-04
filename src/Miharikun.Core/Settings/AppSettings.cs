using System.Text.Json;
using System.Text.Json.Nodes;
using Miharikun.Core.Storage;

namespace Miharikun.Core.Settings;

/// <summary>画面のテーマ。System は OS のライト/ダーク設定に従う。</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// settings.json（保存先ルート直下）の読み書き。AOT 互換のため JsonNode で扱う（要件 6章）。
/// <b>書き込みは、ファイル全体を読み、変えるキーだけを書き換えて保存する</b>（知らないキーも残す）。
/// テーマ（⚙ メニュー）と設定画面が同じファイルに書くので、片方の保存で他方の値を消さない。
/// </summary>
public sealed class AppSettingsStore(AppPaths paths, Action<string>? log = null)
{
    public const int DefaultRunningTimeoutMinutes = Sessions.StalledRule.DefaultTimeoutMinutes;

    private enum ReadState { Missing, Ok, Broken, Unreadable }

    private string FilePath => Path.Combine(paths.Root, "settings.json");

    /// <summary>ファイルがない・壊れている・知らない値のときは System。</summary>
    public AppTheme LoadTheme()
    {
        var theme = ReadRoot() is { } root && root["theme"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        return theme switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.System,
        };
    }

    public void SaveTheme(AppTheme mode) =>
        Save("theme", mode switch { AppTheme.Light => "light", AppTheme.Dark => "dark", _ => "system" });

    /// <summary>
    /// 「実行中」のまま新しい記録が来ない時間（分）。既定 10。0 以下は無効の意味でそのまま返す。
    /// 欠けている・整数以外・壊れているときは既定値。
    /// </summary>
    public int LoadRunningTimeoutMinutes() =>
        ReadRoot() is { } root && root["runningTimeoutMinutes"] is JsonValue v && v.TryGetValue<int>(out var minutes)
            ? minutes
            : DefaultRunningTimeoutMinutes;

    public void SaveRunningTimeoutMinutes(int minutes) => Save("runningTimeoutMinutes", minutes);

    private JsonObject? ReadRoot() => ReadRoot(out _);

    /// <summary>ファイルがない・壊れている・オブジェクトでない・読めないときは null（壊れている・読めないときはログに残す）。</summary>
    private JsonObject? ReadRoot(out ReadState state)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                state = ReadState.Missing;
                return null;
            }
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject root)
            {
                state = ReadState.Ok;
                return root;
            }
            log?.Invoke($"{FilePath} が JSON のオブジェクトではない（既定の設定を使う）");
            state = ReadState.Broken;
        }
        catch (JsonException ex)
        {
            log?.Invoke($"{FilePath} が壊れている（既定の設定を使う）: {ex.Message}");
            state = ReadState.Broken;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"{FilePath} を読めない（既定の設定を使う）: {ex.Message}");
            state = ReadState.Unreadable;
        }
        return null;
    }

    /// <summary>全体を読んで、1 つのキーだけを書き換えて保存する。壊れているファイルは .bad に退避してから、新しく書く。</summary>
    private void Save(string key, JsonNode value)
    {
        try
        {
            var root = ReadRoot(out var state);
            if (state == ReadState.Unreadable)
                return;   // 一時的に読めないだけかもしれない。上書きして他のキーを消さない（読めない理由はログに出ている）
            if (state == ReadState.Broken)
            {
                try { File.Copy(FilePath, FilePath + ".bad", overwrite: true); } catch (IOException) { }
            }
            root ??= new JsonObject();
            root[key] = value;
            AtomicFile.WriteAllText(FilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"{FilePath} に書けない: {ex.Message}");
        }
    }
}
