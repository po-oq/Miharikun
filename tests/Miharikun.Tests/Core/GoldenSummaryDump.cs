using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

/// <summary>MIHARIKUN_GOLDEN_DIR があるときだけ動く A/B ダンプ用テスト（Phase 18 の品質ゲート 5。普段の dotnet test では飛ばす）。</summary>
public sealed class GoldenFactAttribute : FactAttribute
{
    public GoldenFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GoldenSummaryDump.DirEnvVar)))
            Skip = "A/B ダンプ用。MIHARIKUN_GOLDEN_DIR（と MIHARIKUN_GOLDEN_LABEL）を指定して実行する";
    }
}

/// <summary>
/// 本物のデータのコピーを読み込み、全セッションの要約と検索文字列を summaries-{label}.json に書く。
/// 共通化の前（before）と後（after）で書いたファイルを比べ、差分が 0 件であることを確かめる（計画 1 章・C2）。
/// <list type="bullet">
/// <item>MIHARIKUN_GOLDEN_DIR：コピーの置き場。&lt;dir&gt;\data を保存先のルート、&lt;dir&gt;\cursor を Cursor の設定フォルダとして読む。</item>
/// <item>MIHARIKUN_GOLDEN_LABEL：出力ファイル名の印（既定 "run"）。</item>
/// <item>MIHARIKUN_GOLDEN_PROJECT：対象プロジェクトのフォルダ（既定は、このリポジトリのルート）。</item>
/// </list>
/// </summary>
public sealed class GoldenSummaryDump
{
    public const string DirEnvVar = "MIHARIKUN_GOLDEN_DIR";
    public const string LabelEnvVar = "MIHARIKUN_GOLDEN_LABEL";
    public const string ProjectEnvVar = "MIHARIKUN_GOLDEN_PROJECT";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(), new IsoDateTimeOffsetConverter() },
    };

    /// <summary>時刻は ISO 形式（往復できる 7 桁の小数つき）で書く。</summary>
    private sealed class IsoDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("o"));
    }

    private sealed record Entry(SessionSummary Summary, string SearchText, int EventCount);

    [GoldenFact]
    public void Dump_all_session_summaries()
    {
        var dir = Environment.GetEnvironmentVariable(DirEnvVar)!;
        var label = Environment.GetEnvironmentVariable(LabelEnvVar);
        if (string.IsNullOrWhiteSpace(label))
            label = "run";
        var project = Environment.GetEnvironmentVariable(ProjectEnvVar);
        if (string.IsNullOrWhiteSpace(project))
            project = FindRepoRoot();

        var paths = new AppPaths(Path.Combine(dir, "data"));
        var store = new ProjectEventStore([new CursorSessionSource(new CursorAgent(), paths, project,
            importer: new CursorTranscriptImporter(Path.Combine(dir, "cursor")))]);
        store.Refresh();

        var entries = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var key in store.Sessions)
        {
            var summary = store.GetSummary(key);
            Assert.NotNull(summary);
            var events = store.GetEvents(key);
            entries[$"{key.AgentId}/{key.SessionId}"] = new Entry(summary, SessionSearch.BuildSearchText(summary, events), events.Count);
        }

        var path = Path.Combine(dir, $"summaries-{label}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(entries, Options));
        Console.WriteLine($"golden: {entries.Count} sessions -> {path}");
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Miharikun.slnx")))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException($"{ProjectEnvVar} を指定してください");
    }
}
