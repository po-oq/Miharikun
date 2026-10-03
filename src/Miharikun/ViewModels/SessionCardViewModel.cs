using CommunityToolkit.Mvvm.ComponentModel;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;

namespace Miharikun.ViewModels;

/// <summary>左ペインのカード1枚。表示用の文字列は SessionText で作る。</summary>
public sealed partial class SessionCardViewModel : ObservableObject
{
    public SessionKey Key { get; }

    [ObservableProperty] private SessionSnapshot _snapshot;
    [ObservableProperty] private SessionState _state;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _metaLine = "";
    [ObservableProperty] private string _promptLine = "";
    [ObservableProperty] private string _toolLine = "";
    [ObservableProperty] private string _responseLines = "";
    [ObservableProperty] private DateTimeOffset _lastActivityAt;

    public string SearchText => Snapshot.SearchText;

    public SessionCardViewModel(SessionSnapshot snapshot, DateTimeOffset now)
    {
        Key = snapshot.Summary.Key;
        _snapshot = snapshot;
        Apply(snapshot, now);
    }

    public void Apply(SessionSnapshot snapshot, DateTimeOffset now)
    {
        Snapshot = snapshot;
        OnPropertyChanged(nameof(SearchText));
        var s = snapshot.Summary;
        State = s.State;
        StateText = SessionText.StateName(s.State);
        Title = s.AutoTitle ?? "（依頼なし）";
        LastActivityAt = s.LastActivityAt;
        RefreshClock(now);
    }

    /// <summary>「◯分前」と実行中ツールの経過秒は時間とともに変わるので、定期的に呼ぶ。</summary>
    public void RefreshClock(DateTimeOffset now)
    {
        var s = Snapshot.Summary;
        var parts = new List<string> { $"依頼{s.PromptCount}", $"最後の動き {SessionText.RelativeTime(s.LastActivityAt, now)}" };
        if (s.Model is not null) parts.Add(s.Model);
        if (s.CompactionCount > 0) parts.Add($"圧縮{s.CompactionCount}回");
        if (s.Branch is not null) parts.Add(s.Branch);
        MetaLine = string.Join("・", parts);

        PromptLine = SessionText.PromptLine(s) is { } p ? "📝 " + p : "";
        ToolLine = SessionText.ToolLine(s, now) is { } t ? "🔧 " + t : "";
        ResponseLines = SessionText.ResponseLines(s) is { } r
            ? "💬 " + r
            : s.State == SessionState.Running ? "💬 （応答待ち）" : "";
    }
}