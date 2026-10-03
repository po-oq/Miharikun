using CommunityToolkit.Mvvm.ComponentModel;
using Miharikun.Core.Agents;
using Miharikun.Core.Meta;
using Miharikun.Core.Sessions;

namespace Miharikun.ViewModels;

/// <summary>左ペインのカード1枚。表示用の文字列は SessionText で作る。</summary>
public sealed partial class SessionCardViewModel : ObservableObject
{
    private readonly SessionMetaService _meta;

    public SessionKey Key { get; }

    [ObservableProperty] private SessionSnapshot _snapshot;
    [ObservableProperty] private SessionState _state;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _metaLine = "";
    [ObservableProperty] private string _promptLine = "";
    [ObservableProperty] private string _toolLine = "";
    [ObservableProperty] private string _responseLines = "";
    [ObservableProperty] private string _summaryLine = "";
    [ObservableProperty] private string _memoLine = "";
    [ObservableProperty] private bool _hasMemo;

    /// <summary>ユーザーが設定したステータス。null は未設定（バッジを出さない）。</summary>
    [ObservableProperty] private SessionStatus? _status;
    [ObservableProperty] private string _statusText = "";

    /// <summary>未コミットのファイル数。git が使えないときは null（不明）。</summary>
    public int? UncommittedCount { get; private set; }
    [ObservableProperty] private DateTimeOffset _lastActivityAt;

    /// <summary>全文検索の対象（セッションの内容＋手動タイトル・概要・メモ）。</summary>
    public string SearchText { get; private set; } = "";

    public RenameState Rename { get; }

    public SessionCardViewModel(SessionSnapshot snapshot, DateTimeOffset now, SessionMetaService meta)
    {
        Key = snapshot.Summary.Key;
        _snapshot = snapshot;
        _meta = meta;
        Rename = new RenameState(() => Title, text => _meta.Update(Key, (m, at) => m.WithManualTitle(text, at)));
        Apply(snapshot, now);
    }

    public void Apply(SessionSnapshot snapshot, DateTimeOffset now)
    {
        Snapshot = snapshot;
        var s = snapshot.Summary;
        State = s.State;
        StateText = SessionText.StateName(s.State);
        LastActivityAt = s.LastActivityAt;
        RefreshMeta();
        RefreshClock(now);
    }

    /// <summary>タイトル・概要・メモはメタから。メタが変わったとき（と生成時）に呼ぶ。</summary>
    public void RefreshMeta()
    {
        var meta = _meta.Get(Key);
        Title = meta.DisplayTitle(Snapshot.Summary.AutoTitle) ?? "（依頼なし）";
        SummaryLine = meta.Summary is { } s ? "📄 概要：" + FirstLine(s.Text) : "";
        HasMemo = meta.HasMemo;
        Status = meta.Status;
        StatusText = meta.Status is null ? "" : SessionText.StatusName(meta.Status);
        MemoLine = meta.HasMemo ? "📌 " + string.Join("\n", Lines(meta.Memo).Take(2)) : "";
        SearchText = string.Join('\n', new[] { Snapshot.SearchText, meta.SearchText }.Where(t => t.Length > 0));
        OnPropertyChanged(nameof(SearchText));
    }

    public void SetUncommitted(int? count)
    {
        if (UncommittedCount == count)
            return;
        UncommittedCount = count;
        OnPropertyChanged(nameof(UncommittedCount));
        RefreshClock(DateTimeOffset.Now);
    }

    /// <summary>「◯分前」と実行中ツールの経過秒は時間とともに変わるので、定期的に呼ぶ。</summary>
    public void RefreshClock(DateTimeOffset now)
    {
        var s = Snapshot.Summary;
        var parts = new List<string> { $"依頼{s.PromptCount}", $"最後の動き {SessionText.RelativeTime(s.LastActivityAt, now)}" };
        if (s.Model is not null) parts.Add(s.Model);
        if (s.CompactionCount > 0) parts.Add($"圧縮{s.CompactionCount}回");
        if (s.Branch is not null) parts.Add(s.Branch);
        if (UncommittedCount > 0) parts.Add($"未コミット{UncommittedCount}");
        MetaLine = string.Join("・", parts);

        PromptLine = SessionText.PromptLine(s) is { } p ? "📝 " + p : "";
        ToolLine = SessionText.ToolLine(s, now) is { } t ? "🔧 " + t : "";
        ResponseLines = SessionText.ResponseLines(s) is { } r
            ? "💬 " + r
            : s.State == SessionState.Running ? "💬 （応答待ち）" : "";
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    private static string FirstLine(string text) => Lines(text).FirstOrDefault() ?? "";
}