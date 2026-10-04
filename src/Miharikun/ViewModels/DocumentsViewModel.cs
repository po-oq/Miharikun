using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miharikun.Core.Documents;
using Miharikun.Core.Settings;

namespace Miharikun.ViewModels;

/// <summary>
/// ドキュメントタブ（要件 12.7）。索引（DocumentIndex）を変えるのは、ここ（UI スレッド）だけ。
/// 走査のバッチも Watcher の差分も UI スレッドへ送ってから反映し、画面（ツリー・一覧）は 200ms ほどまとめて作り直す。
/// 状態（走査結果・選択・展開）はここに持つ（タブを切り替えると View は外れるため）。
/// </summary>
public sealed partial class DocumentsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(200);

    private readonly string _root;
    private readonly ProjectSettingsStore _settings;
    private readonly SynchronizationContext _ui;
    private readonly Action<string>? _log;
    private readonly DocumentIndex _index = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly RescanScheduler _scheduler;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FolderNodeViewModel> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly FolderNodeViewModel _allNode = new(null, "すべて");

    private GitIgnoreMatcher _matcher;
    private DocumentWatcher? _watcher;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _overviewCts;
    private string? _selectedFolder;          // null＝すべて
    private string? _selectedPath;            // 選んでいるファイルの相対パス。再走査で一覧が空になっても覚えておく
    private bool _syncing;                    // コードから選択・展開を変えている間は、画面の操作として扱わない
    private bool _started;
    private bool _restored;
    private bool _disposed;

    public DocumentsViewModel(string projectFolder, ProjectSettingsStore settings, SynchronizationContext ui, Action<string>? log = null)
    {
        _root = projectFolder;
        _settings = settings;
        _ui = ui;
        _log = log;
        _matcher = GitIgnoreMatcher.Parse(settings.IgnoreTextOrDefault(projectFolder));

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) => RefreshNow();

        // 全再走査の予約（フォルダの変更・バッファあふれ）。スレッドプールから呼ばれるので UI へ送る。
        _scheduler = new RescanScheduler(() => _ui.Post(_ => Rescan(), null));

        _allNode.IsExpanded = true;
        Hook(_allNode);
        _allNode.IsSelected = true;
        TreeRoots = [_allNode];
    }

    public string ProjectFolder => _root;

    /// <summary>ツリーの根（「すべて」の 1 件だけ）。</summary>
    public ObservableCollection<FolderNodeViewModel> TreeRoots { get; }

    [ObservableProperty] private IReadOnlyList<DocumentRowViewModel> _rows = [];

    [ObservableProperty] private DocumentRowViewModel? _selectedRow;

    [ObservableProperty] private DocumentOverviewViewModel? _overview;

    [ObservableProperty] private string _filterText = "";

    [ObservableProperty] private string _listHeader = "";

    [ObservableProperty] private string _statusText = "";

    [ObservableProperty] private bool _isScanning;

    /// <summary>索引が空（0 件）で、走査も終わっているとき。一覧の代わりに案内を出す。</summary>
    [ObservableProperty] private bool _isEmpty;

    /// <summary>設定ダイアログの初期値（保存済みの除外パターン。無ければ既定値）。</summary>
    public string CurrentIgnoreText => _settings.IgnoreTextOrDefault(_root);

    public string DefaultIgnoreText => DefaultIgnore.Text;

    /// <summary>最初にタブが表示されたときに呼ぶ。監視と走査を始める（アプリの起動を遅くしないため、それまでは何もしない）。</summary>
    public void Start()
    {
        if (_started || _disposed)
            return;
        _started = true;
        StartWatcher();
        Rescan();
    }

    /// <summary>「読み直し」：全再走査。</summary>
    [RelayCommand]
    public void Rescan()
    {
        if (_disposed)
            return;

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var generation = _index.Reset();
        IsScanning = true;
        _scheduler.MarkScanStarted();

        var matcher = _matcher;
        _ = DocumentIndexer.ScanAsync(_root, matcher, generation, batch => _ui.Post(_ => OnBatch(batch), null), _scanCts.Token);
        UpdateStatus();
    }

    /// <summary>設定ダイアログの「保存」：除外パターンを保存し、監視と走査をやり直す。</summary>
    public void ApplyIgnoreText(string text)
    {
        _settings.SaveIgnore(_root, text);
        _matcher = GitIgnoreMatcher.Parse(text);
        if (!_started)
            return;
        _watcher?.Dispose();
        StartWatcher();
        Rescan();
    }

    private void StartWatcher()
    {
        // 走査より先に始める（走査中に増えたファイルを拾うため）。
        _watcher = new DocumentWatcher(_root, _matcher, batch => _ui.Post(_ => OnWatcherChanges(batch), null), log: _log);
        _watcher.Start();
    }

    // ── 走査・監視の取り込み ───────────────────────────────────────────

    private void OnBatch(DocumentBatch batch)
    {
        if (_disposed || !_index.Apply(batch))
            return;                                   // 古い世代のバッチは捨てる

        MarkDirty();
        if (!batch.IsLast)
            return;

        IsScanning = false;
        _scheduler.ScanCompleted();
        RefreshNow();
        if (!_restored)
        {
            _restored = true;
            RestoreLastOpened();
        }
    }

    private void OnWatcherChanges(IReadOnlyList<FileChange> batch)
    {
        if (_disposed)
            return;

        var set = DocumentChangeClassifier.Classify(batch, _root, _matcher, _index.HasEntriesUnder);
        foreach (var path in set.Removes)
            _index.Remove(path);
        foreach (var entry in set.Upserts)
            _index.AddOrUpdate(entry);

        if (set.Rescan)
            _scheduler.Request();
        if (set.Removes.Count > 0 || set.Upserts.Count > 0)
            MarkDirty();

        // 選んでいるファイルが保存されたら、概要（更新日時・行数）も読み直す。
        if (_selectedPath is not null && set.Upserts.Any(e => e.RelativePath.Equals(_selectedPath, StringComparison.OrdinalIgnoreCase)))
            LoadOverview(_selectedPath);
    }

    private void MarkDirty()
    {
        if (!_refreshTimer.IsEnabled)
            _refreshTimer.Start();
    }

    // ── 画面（ツリー・一覧）の作り直し ─────────────────────────────────────

    partial void OnFilterTextChanged(string value) => RefreshNow();

    private void RefreshNow()
    {
        _refreshTimer.Stop();
        var query = FilterText;
        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var matched = hasQuery ? _index.Entries.Where(e => DocumentFilter.Matches(e, query)).ToList() : _index.Entries.ToList();

        var tree = DocumentTree.Build(matched);
        if (_selectedFolder is not null && tree.Find(_selectedFolder) is null)
            _selectedFolder = null;                   // 選んでいたフォルダが無くなった（絞り込み・削除）

        _syncing = true;
        try
        {
            _allNode.Count = tree.TotalCount;
            SyncChildren(_allNode, tree, hasQuery);
            foreach (var node in _nodes.Values)
                node.IsSelected = _selectedFolder is not null && string.Equals(node.RelativePath, _selectedFolder, StringComparison.OrdinalIgnoreCase);
            _allNode.IsSelected = _selectedFolder is null;

            var rows = DocumentFilter.Select(matched, _selectedFolder, "").Select(e => new DocumentRowViewModel(e)).ToList();
            Rows = rows;                              // ItemsSource を差し替えると ListBox が選択を外すので、あとで戻す
            SelectedRow = _selectedPath is null
                ? null
                : rows.FirstOrDefault(r => r.RelativePath.Equals(_selectedPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncing = false;
        }

        // 走査が終わっても選んでいたファイルが無ければ（消えた・除外された）、選択を解く。走査中は待つ。
        if (!IsScanning && _selectedPath is not null && _index.TryGet(_selectedPath) is null)
        {
            _selectedPath = null;
            _overviewCts?.Cancel();
            Overview = null;
        }

        ListHeader = _selectedFolder is null ? "すべて" : $"{_selectedFolder}/ の直下";
        IsEmpty = !IsScanning && _index.Count == 0;
        UpdateStatus(matched.Count, hasQuery);
    }

    private void UpdateStatus(int? matched = null, bool hasQuery = false)
    {
        var count = hasQuery && matched is { } m ? $"{m} / {_index.Count} 件" : $"{_index.Count} 件";
        StatusText = IsScanning ? $"走査中… {count}" : count;
    }

    /// <summary>VM を使い回しながら、子フォルダの並びを合わせる（展開状態を保つ）。</summary>
    private void SyncChildren(FolderNodeViewModel vm, FolderNode node, bool expandAll)
    {
        var desired = new List<FolderNodeViewModel>(node.Children.Count);
        foreach (var child in node.Children)
        {
            if (!_nodes.TryGetValue(child.RelativePath, out var childVm))
            {
                childVm = new FolderNodeViewModel(child.RelativePath, child.Name);
                Hook(childVm);
                _nodes[child.RelativePath] = childVm;
            }
            childVm.Count = child.TotalCount;
            childVm.IsExpanded = expandAll || _expanded.Contains(child.RelativePath);   // 絞り込み中は、ヒットを隠さないよう全部開く
            SyncChildren(childVm, child, expandAll);
            desired.Add(childVm);
        }

        if (!vm.Children.SequenceEqual(desired))
        {
            vm.Children.Clear();
            foreach (var child in desired)
                vm.Children.Add(child);
        }
    }

    private void Hook(FolderNodeViewModel node)
    {
        node.SelectionChanged += OnFolderSelected;
        node.ExpansionChanged += OnFolderExpansionChanged;
    }

    private void OnFolderSelected(FolderNodeViewModel node)
    {
        if (_syncing)
            return;
        _selectedFolder = node.RelativePath;
        _ui.Post(_ => RefreshNow(), null);            // ツリーの選択イベントの最中に、ツリーの項目を作り直さない
    }

    private void OnFolderExpansionChanged(FolderNodeViewModel node)
    {
        if (_syncing || node.RelativePath is null)
            return;
        if (node.IsExpanded)
            _expanded.Add(node.RelativePath);
        else
            _expanded.Remove(node.RelativePath);
    }

    // ── ファイルの選択・概要・復元 ─────────────────────────────────────────

    partial void OnSelectedRowChanged(DocumentRowViewModel? value)
    {
        if (_syncing || value is null)
            return;

        _selectedPath = value.RelativePath;
        _settings.SaveLastOpened(_root, value.RelativePath);
        LoadOverview(value.RelativePath);
    }

    /// <summary>概要は全文を読む（行数）ので背景で作る。選択が変わったら捨てる。</summary>
    private void LoadOverview(string relativePath)
    {
        _overviewCts?.Cancel();
        var cts = _overviewCts = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            DocumentOverview? overview = null;
            try
            {
                overview = DocumentOverview.Read(_root, relativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log?.Invoke($"{relativePath} を読めない: {ex.Message}");
            }

            _ui.Post(_ =>
            {
                if (!cts.IsCancellationRequested && !_disposed)
                    Overview = overview is null ? null : new DocumentOverviewViewModel(overview);
            }, null);
        });
    }

    /// <summary>前回開いていたファイルを選ぶ（走査が最初に終わったとき 1 回だけ。消えていたら何もしない）。</summary>
    private void RestoreLastOpened()
    {
        var path = _settings.LastOpenedExisting(_root);
        if (path is null || _index.TryGet(path) is not { } entry)
            return;

        _selectedFolder = entry.Folder.Length == 0 ? null : entry.Folder;
        for (var folder = entry.Folder; folder.Length > 0; folder = folder.Contains('/') ? folder[..folder.LastIndexOf('/')] : "")
            _expanded.Add(folder);
        _selectedPath = entry.RelativePath;
        RefreshNow();
        LoadOverview(entry.RelativePath);
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        _scanCts?.Cancel();
        _overviewCts?.Cancel();
        _watcher?.Dispose();
        _scheduler.Dispose();
    }
}
