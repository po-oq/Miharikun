using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miharikun.Core.Agents;
using Miharikun.Core.Git;
using Miharikun.Core.Meta;
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
    private readonly SessionMetaService _meta;
    private readonly string _projectFolder;
    private SessionSummary _summary;
    private IReadOnlyList<string>? _uncommitted;
    private bool _memoDirty;
    private bool _loadingMemo;

    public SessionKey Key => _summary.Key;

    [ObservableProperty] private SessionState _state;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _headerMeta = "";

    // 概要・メモ（メタ）
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _summaryInfo = "";
    [ObservableProperty] private bool _isEditingSummary;
    [ObservableProperty] private string _summaryDraft = "";
    [ObservableProperty] private string _memoText = "";

    public RenameState Rename { get; }

    [ObservableProperty] private string _promptLine = "";
    [ObservableProperty] private string _toolLine = "";
    [ObservableProperty] private string _responseLine = "";

    [ObservableProperty] private IReadOnlyList<CheckItem> _checks = [];
    [ObservableProperty] private IReadOnlyList<StatRow> _stats = [];

    [ObservableProperty] private string _commitsText = "—";
    [ObservableProperty] private IReadOnlyList<string> _commits = [];
    [ObservableProperty] private string _changedFilesText = "";
    [ObservableProperty] private IReadOnlyList<string> _changedFiles = [];
    [ObservableProperty] private string _testRunsText = "";
    [ObservableProperty] private IReadOnlyList<TestRunRow> _testRuns = [];

    [ObservableProperty] private IReadOnlyList<TurnRow> _turns = [];

    public RelayCommand JumpPromptCommand { get; }
    public RelayCommand JumpToolCommand { get; }
    public RelayCommand JumpResponseCommand { get; }

    public RelayCommand ImportSummaryCommand { get; }
    public RelayCommand EditSummaryCommand { get; }
    public RelayCommand SaveSummaryCommand { get; }
    public RelayCommand CancelSummaryCommand { get; }
    public RelayCommand RevertSummaryCommand { get; }
    public RelayCommand SaveMemoCommand { get; }

    public SessionDetailViewModel(SessionSnapshot snapshot, DateTimeOffset now,
        Action<(long Seq, TimelineKind Kind)> jump, SessionMetaService meta, string projectFolder)
    {
        _jump = jump;
        _meta = meta;
        _projectFolder = projectFolder;
        _summary = snapshot.Summary;
        Rename = new RenameState(() => Title, text => _meta.Update(Key, (m, at) => m.WithManualTitle(text, at)));
        ImportSummaryCommand = new RelayCommand(ImportSummary, () => SummaryImport.FromLastResponse(_summary) is not null);
        EditSummaryCommand = new RelayCommand(() =>
        {
            SummaryDraft = _meta.Get(Key).Summary?.Text ?? "";
            IsEditingSummary = true;
        });
        SaveSummaryCommand = new RelayCommand(() =>
        {
            IsEditingSummary = false;
            _meta.Update(Key, (m, at) => m.EditSummary(SummaryDraft, at));
        });
        CancelSummaryCommand = new RelayCommand(() => IsEditingSummary = false);
        RevertSummaryCommand = new RelayCommand(
            () => _meta.Update(Key, (m, at) => m.RevertSummary(at)),
            () => _meta.Get(Key).CanRevertSummary);
        SaveMemoCommand = new RelayCommand(FlushMemo);
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

        // 導入前のセッション（transcript から取り込み）は、時刻・ターン・ツール・git の情報を持たない。0 や ✓ を出さずに「—」にする。
        var imported = s.State == SessionState.Imported;
        var meta = new List<string> { ShortId(s.Key.SessionId) };
        if (!imported) meta.Add("開始 " + SessionText.Clock(s.StartedAt, now));
        if (SessionText.BranchText(s) is { } branch) meta.Add("ブランチ " + branch);
        if (SessionText.ModelText(s) is { } model) meta.Add("モデル " + model);
        HeaderMeta = string.Join("・", meta);

        RebuildGitParts();

        var transcript = TranscriptSize(s.TranscriptPath);
        Stats = imported
        ?
        [
            new("依頼数", $"{s.PromptCount}件"),
            new("transcript サイズ", transcript),
        ]
        :
        [
            new("ターン / ツール呼び出し", $"{s.TurnCount} / {s.ToolCallCount}回"),
            new("圧縮", SessionText.CompactionText(s)),
            new("継続時間", SessionText.Duration(s.Duration)),
            new("サブエージェント", $"起動中 {s.SubagentsRunning} / 累計 {s.SubagentsTotal}"),
            new("transcript サイズ", transcript),
        ];

        ChangedFilesText = imported ? "—" : s.ChangedFiles.Count == 0 ? "なし" : $"{s.ChangedFiles.Count}件";
        TestRunsText = imported ? "—" : SessionText.TestRunsText(s);
        TestRuns = s.TestRuns.Select(t => new TestRunRow(
            t.Command,
            t.Succeeded switch { true => "成功", false => $"失敗（exit {t.ExitCode}）", null => "成否不明" },
            t.Duration is { } d ? SessionText.Duration(d) : "—",
            t.OutputTail ?? "",
            t.Succeeded == true)).ToList();

        Turns = s.Turns.Select(t => new TurnRow(
            imported ? $"{t.Number}." : $"{t.Number}. {SessionText.Clock(t.StartedAt, now)} {SessionText.TurnStatusLabel(t.Status)}",
            FirstLine(t.Prompt))).ToList();

        RefreshMeta(now);
        RefreshClock(now);
        JumpPromptCommand.NotifyCanExecuteChanged();
        JumpToolCommand.NotifyCanExecuteChanged();
        JumpResponseCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 未コミットのファイル（git status との突き合わせ結果）。git が使えないときは null。
    /// 完了チェックの「コミット済み」と、変更ファイル一覧の印に反映する。
    /// </summary>
    public void SetUncommitted(IReadOnlyList<string>? files)
    {
        _uncommitted = files;
        RebuildGitParts();
    }

    /// <summary>開始〜最後の stop のコミット一覧。範囲が解決できない／git が使えないときは null（不明）。</summary>
    public void SetCommits(IReadOnlyList<GitCommit>? commits)
    {
        CommitsText = commits is null ? "—" : $"{commits.Count}件";
        Commits = commits?.Select(c => c.Subject.Length == 0 ? c.Sha : $"{c.Sha} {c.Subject}").ToList() ?? [];
    }

    private void RebuildGitParts()
    {
        var s = _summary;
        var dirty = _uncommitted is null ? null : new HashSet<string>(_uncommitted, StringComparer.OrdinalIgnoreCase);

        if (s.State == SessionState.Imported)
        {
            Checks =
            [
                new("—", "ターン終了・裏の作業・コミット済みは、導入前のセッションなので不明", "na"),
            ];
            ChangedFiles = [];
            return;
        }

        Checks =
        [
            s.TurnInProgress ? new("✗", "ターン実行中", "ng") : new("✓", "ターン終了", "ok"),
            s.SubagentsRunning == 0
                ? new("✓", "裏の作業なし（サブエージェント 0）", "ok")
                : new("✗", $"裏の作業あり（サブエージェント {s.SubagentsRunning}）", "ng"),
            _uncommitted switch
            {
                null => new("—", "コミット済み（git が使えないため不明）", "na"),
                { Count: 0 } => new("✓", "コミット済み（未コミット 0）", "ok"),
                var files => new("✗", $"未コミットあり（{files.Count}ファイル）", "ng"),
            },
        ];

        ChangedFiles = s.ChangedFiles.Select(f => ShortPath(f) + (dirty?.Contains(f) == true ? "（未コミット）" : "")).ToList();
    }

    /// <summary>プロジェクトフォルダの下にあるファイルは相対パスで表示する（長い絶対パスだと印が隠れるため）。</summary>
    private string ShortPath(string path)
    {
        if (!Path.IsPathRooted(path))
            return path;
        try
        {
            var relative = Path.GetRelativePath(_projectFolder, path);
            return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    /// <summary>タイトル・概要・メモはメタから。メタが変わったとき（と更新時）に呼ぶ。</summary>
    public void RefreshMeta(DateTimeOffset? now = null)
    {
        var meta = _meta.Get(Key);
        Title = meta.DisplayTitle(_summary.AutoTitle) ?? "（依頼なし）";

        SummaryText = meta.Summary?.Text ?? "（概要はまだありません）";
        SummaryInfo = meta.Summary is { } e
            ? $"{SessionText.Clock(e.ImportedAt, now ?? DateTimeOffset.Now)} " +
              (e.SourceTurn is { } turn ? $"取り込み（ターン{turn}の返事）" : "編集")
            : "";

        // 入力途中のメモは、他の更新で上書きしない。
        if (!_memoDirty)
        {
            _loadingMemo = true;
            MemoText = meta.Memo;
            _loadingMemo = false;
        }

        ImportSummaryCommand.NotifyCanExecuteChanged();
        RevertSummaryCommand.NotifyCanExecuteChanged();
    }

    private void ImportSummary()
    {
        if (SummaryImport.FromLastResponse(_summary) is not { } source)
            return;
        _meta.Update(Key, (m, at) => m.ImportSummary(source.Text, source.SourceTurn, at));
    }

    partial void OnMemoTextChanged(string value)
    {
        if (!_loadingMemo)
            _memoDirty = true;
    }

    /// <summary>メモを保存する（フォーカスアウト・セッション切り替え・終了時）。変更がなければ何もしない。</summary>
    public void FlushMemo()
    {
        if (!_memoDirty)
            return;
        _memoDirty = false;
        _meta.Update(Key, (m, at) => m.WithMemo(MemoText, at));
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