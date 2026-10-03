using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;

namespace Miharikun.ViewModels;

/// <summary>Mark は ✓ / ✗ / —、Level は ok / ng / na（色分け用）。</summary>
public sealed record CheckItem(string Mark, string Text, string Level);

public sealed record StatRow(string Label, string Value);

public sealed record TestRunRow(string Command, string Result, string Duration, string OutputTail, bool Succeeded);

public sealed record TurnRow(string Header, string Prompt);

/// <summary>中央ペイン：選択中セッションの詳細（要件 12.3）。</summary>
public sealed partial class SessionDetailViewModel : ObservableObject
{
    private readonly Action<(long Seq, TimelineKind Kind)> _jump;
    private SessionSummary _summary;

    public SessionKey Key => _summary.Key;

    [ObservableProperty] private SessionState _state;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _headerMeta = "";

    [ObservableProperty] private string _promptLine = "";
    [ObservableProperty] private string _toolLine = "";
    [ObservableProperty] private string _responseLine = "";

    [ObservableProperty] private IReadOnlyList<CheckItem> _checks = [];
    [ObservableProperty] private IReadOnlyList<StatRow> _stats = [];

    [ObservableProperty] private string _changedFilesText = "";
    [ObservableProperty] private IReadOnlyList<string> _changedFiles = [];
    [ObservableProperty] private string _testRunsText = "";
    [ObservableProperty] private IReadOnlyList<TestRunRow> _testRuns = [];

    [ObservableProperty] private IReadOnlyList<TurnRow> _turns = [];

    public RelayCommand JumpPromptCommand { get; }
    public RelayCommand JumpToolCommand { get; }
    public RelayCommand JumpResponseCommand { get; }

    public SessionDetailViewModel(SessionSnapshot snapshot, DateTimeOffset now, Action<(long Seq, TimelineKind Kind)> jump)
    {
        _jump = jump;
        _summary = snapshot.Summary;
        JumpPromptCommand = new RelayCommand(() => Jump(SummaryJump.Prompt(_summary)), () => SummaryJump.Prompt(_summary) is not null);
        JumpToolCommand = new RelayCommand(() => Jump(SummaryJump.Tool(_summary)), () => SummaryJump.Tool(_summary) is not null);
        JumpResponseCommand = new RelayCommand(() => Jump(SummaryJump.Response(_summary)), () => SummaryJump.Response(_summary) is not null);
        Update(snapshot, now);
    }

    private void Jump((long Seq, TimelineKind Kind)? target)
    {
        if (target is { } t)
            _jump(t);
    }

    public void Update(SessionSnapshot snapshot, DateTimeOffset now)
    {
        var s = _summary = snapshot.Summary;

        State = s.State;
        StateText = SessionText.StateName(s.State);
        Title = s.AutoTitle ?? "（依頼なし）";

        var meta = new List<string> { ShortId(s.Key.SessionId), "開始 " + SessionText.Clock(s.StartedAt, now) };
        if (s.Branch is not null) meta.Add("ブランチ " + s.Branch);
        if (SessionText.ModelText(s) is { } model) meta.Add("モデル " + model);
        HeaderMeta = string.Join("・", meta);

        Checks =
        [
            s.TurnInProgress ? new("✗", "ターン実行中", "ng") : new("✓", "ターン終了", "ok"),
            s.SubagentsRunning == 0
                ? new("✓", "裏の作業なし（サブエージェント 0）", "ok")
                : new("✗", $"裏の作業あり（サブエージェント {s.SubagentsRunning}）", "ng"),
            new("—", "コミット済み（git 連携で判定）", "na"),
        ];

        var transcript = TranscriptSize(s.TranscriptPath);
        Stats =
        [
            new("ターン / ツール呼び出し", $"{s.TurnCount} / {s.ToolCallCount}回"),
            new("圧縮", SessionText.CompactionText(s)),
            new("継続時間", SessionText.Duration(s.Duration)),
            new("サブエージェント", $"起動中 {s.SubagentsRunning} / 累計 {s.SubagentsTotal}"),
            new("transcript サイズ", transcript),
        ];

        ChangedFilesText = s.ChangedFiles.Count == 0 ? "なし" : $"{s.ChangedFiles.Count}件";
        ChangedFiles = s.ChangedFiles;
        TestRunsText = SessionText.TestRunsText(s);
        TestRuns = s.TestRuns.Select(t => new TestRunRow(
            t.Command,
            t.Succeeded switch { true => "成功", false => $"失敗（exit {t.ExitCode}）", null => "成否不明" },
            t.Duration is { } d ? SessionText.Duration(d) : "—",
            t.OutputTail ?? "",
            t.Succeeded == true)).ToList();

        Turns = s.Turns.Select(t => new TurnRow(
            $"{t.Number}. {SessionText.Clock(t.StartedAt, now)} {SessionText.TurnStatusLabel(t.Status)}",
            FirstLine(t.Prompt))).ToList();

        RefreshClock(now);
        JumpPromptCommand.NotifyCanExecuteChanged();
        JumpToolCommand.NotifyCanExecuteChanged();
        JumpResponseCommand.NotifyCanExecuteChanged();
    }

    /// <summary>実行中ツールの経過秒は時間とともに変わるので、定期的に呼ぶ。</summary>
    public void RefreshClock(DateTimeOffset now)
    {
        PromptLine = SessionText.PromptLine(_summary) ?? "—";
        ToolLine = SessionText.ToolLine(_summary, now) ?? "—";
        ResponseLine = SessionText.ResponseLines(_summary)?.Replace('\n', ' ')
                       ?? (_summary.State == SessionState.Running ? "（応答待ち）" : "—");
    }

    private static string ShortId(string id) => id.Length <= 8 ? id : id[..8];

    private static string FirstLine(string? text) =>
        text?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

    private static string TranscriptSize(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "—";
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "—";
            return info.Length switch
            {
                >= 1024 * 1024 => $"{info.Length / 1024.0 / 1024.0:0.0} MB",
                >= 1024 => $"{info.Length / 1024.0:0} KB",
                _ => $"{info.Length} B",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "—";
        }
    }
}