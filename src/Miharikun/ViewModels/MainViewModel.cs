using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Miharikun.Core.Agents;
using Miharikun.Core.Sessions;

namespace Miharikun.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SessionMonitor _monitor;
    private readonly SynchronizationContext _ui;
    private readonly Dictionary<SessionKey, SessionCardViewModel> _byKey = [];

    public string ProjectFolder { get; }

    public ObservableCollection<SessionCardViewModel> Cards { get; } = [];

    /// <summary>検索・フィルタ済みで、最後の動きの新しい順に並んだカード。</summary>
    public ICollectionView CardsView { get; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _runningOnly;
    [ObservableProperty] private bool _uncommittedOnly;
    [ObservableProperty] private bool _memoOnly;
    [ObservableProperty] private SessionCardViewModel? _selected;
    [ObservableProperty] private IReadOnlyList<StateCount> _counts = [];

    /// <summary>「未コミットあり」は git 連携（Phase 7）、「メモあり」はメタ（Phase 6）で有効になる。</summary>
    public bool UncommittedFilterAvailable => false;
    public bool MemoFilterAvailable => false;

    public MainViewModel(string projectFolder, SessionMonitor monitor, SynchronizationContext ui)
    {
        ProjectFolder = projectFolder;
        _monitor = monitor;
        _ui = ui;

        CardsView = CollectionViewSource.GetDefaultView(Cards);
        CardsView.SortDescriptions.Add(new SortDescription(nameof(SessionCardViewModel.LastActivityAt), ListSortDirection.Descending));
        CardsView.Filter = o => o is SessionCardViewModel c && Visible(c);

        UpdateCounts();
        _monitor.Updated += OnUpdated;
    }

    // 監視スレッドから呼ばれる。UI スレッドへ渡すだけにする。
    private void OnUpdated(SessionUpdate update) => _ui.Post(_ => Apply(update), null);

    public void Apply(SessionUpdate update)
    {
        var now = DateTimeOffset.Now;
        foreach (var snap in update.Upserts)
        {
            if (_byKey.TryGetValue(snap.Summary.Key, out var card))
            {
                card.Apply(snap, now);
            }
            else
            {
                card = new SessionCardViewModel(snap, now);
                _byKey[card.Key] = card;
                Cards.Add(card);
            }
        }
        foreach (var key in update.Removed)
        {
            if (_byKey.Remove(key, out var card))
            {
                Cards.Remove(card);
                if (ReferenceEquals(Selected, card))
                    Selected = null;
            }
        }
        CardsView.Refresh();
        UpdateCounts();
    }

    /// <summary>時刻表示の更新用。1秒ごとに呼ぶ。</summary>
    public void Tick()
    {
        var now = DateTimeOffset.Now;
        foreach (var card in Cards)
            card.RefreshClock(now);
    }

    partial void OnSearchTextChanged(string value) => CardsView.Refresh();
    partial void OnRunningOnlyChanged(bool value) => CardsView.Refresh();
    partial void OnUncommittedOnlyChanged(bool value) => CardsView.Refresh();
    partial void OnMemoOnlyChanged(bool value) => CardsView.Refresh();

    private bool Visible(SessionCardViewModel c)
    {
        if (RunningOnly && c.State != SessionState.Running) return false;
        // UncommittedOnly / MemoOnly は、対応するデータ（Phase 7 / Phase 6）が入ってから判定を足す。
        return SessionSearch.Matches(c.SearchText, SearchText);
    }

    private void UpdateCounts()
    {
        int Count(params SessionState[] states) => Cards.Count(c => states.Contains(c.State));
        Counts =
        [
            new(SessionState.Running, SessionText.StateName(SessionState.Running), Count(SessionState.Running)),
            new(SessionState.YourTurn, SessionText.StateName(SessionState.YourTurn), Count(SessionState.YourTurn)),
            new(SessionState.Aborted, SessionText.StateName(SessionState.Aborted), Count(SessionState.Aborted)),
            new(SessionState.Error, SessionText.StateName(SessionState.Error), Count(SessionState.Error)),
            new(SessionState.Closed, SessionText.StateName(SessionState.Closed), Count(SessionState.Closed, SessionState.Imported)),
        ];
    }

    public void Dispose() => _monitor.Updated -= OnUpdated;
}
public sealed record StateCount(SessionState State, string Name, int Count);
