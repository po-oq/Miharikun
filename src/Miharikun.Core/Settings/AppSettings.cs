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

/// <summary>settings.json（保存先ルート直下）の読み書き。AOT 互換のため JsonNode で扱う。</summary>
public sealed class AppSettingsStore(AppPaths paths, Action<string>? log = null)
{
    private string FilePath => Path.Combine(paths.Root, "settings.json");

    /// <summary>ファイルがない・壊れている・知らない値のときは System。</summary>
    public AppTheme LoadTheme()
    {
        try
        {
            if (!File.Exists(FilePath))
                return AppTheme.System;
            var theme = (JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject)?["theme"]?.GetValue<string>();
            return theme switch
            {
                "light" => AppTheme.Light,
                "dark" => AppTheme.Dark,
                _ => AppTheme.System,
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException)
        {
            log?.Invoke($"{FilePath} を読めない（既定のテーマを使う）: {ex.Message}");
            return AppTheme.System;
        }
    }

    public void SaveTheme(AppTheme mode)
    {
        var value = mode switch { AppTheme.Light => "light", AppTheme.Dark => "dark", _ => "system" };
        var json = new JsonObject { ["theme"] = value }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        try
        {
            AtomicFile.WriteAllText(FilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"{FilePath} に書けない: {ex.Message}");
        }
    }
}
