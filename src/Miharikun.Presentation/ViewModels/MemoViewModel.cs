using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miharikun.Core.Memo;
using Miharikun.Core.Storage;
using Miharikun.Services;

namespace Miharikun.ViewModels;

/// <summary>
/// メモタブ（プロジェクトメモ。要件 12.10・実装計画 7.1〜7.6）。編集の状態は <see cref="MemoEditor"/>、読み書きは <see cref="ProjectMemoStore"/>。
/// 状態（入力中の文字・編集中か）はここに持つ（タブを切り替えると View は外れるため）。
/// </summary>
public sealed partial class MemoViewModel : ObservableObject, IPreviewHost, IDisposable
{
    private const string EmptyMemoText = "メモはまだありません。右上の『編集』で書けます";
    private static readonly TimeSpan WatchDelay = TimeSpan.FromMilliseconds(300);

    private readonly string _projectFolder;
    private readonly ProjectMemoStore _store;
    private readonly AppPaths _paths;
    private readonly Func<bool> _isDark;
    private readonly SynchronizationContext _ui;
    private readonly Func<string, string?> _resolveInApp;
    private readonly Action<string>? _log;
    private readonly MemoEditor _editor = new();
    private readonly IUiTimer _watchTimer;
    private readonly IUiServices _services;

    private FileSystemWatcher? _watcher;
    private DateTime? _lastWrite;
    private bool _started;
    private bool _loaded;                 // 一度でも読めたか（読めていなければ、編集・保存させない）
    private bool _disposed;
    private string? _loadError;
    private string? _saveError;

    /// <param name="resolveInApp">リンク先のフルパス → ドキュメントタブで開けるなら相対パス、開けなければ null。</param>
    public MemoViewModel(string projectFolder, ProjectMemoStore store, AppPaths paths, Func<bool> isDark,
        SynchronizationContext ui, Func<string, string?> resolveInApp, IUiServices services, Action<string>? log = null)
    {
        _projectFolder = projectFolder;
        _store = store;
        _paths = paths;
        _isDark = isDark;
        _ui = ui;
        _services = services;
        _resolveInApp = resolveInApp;
        _log = log;
        _watchTimer = services.CreateTimer(WatchDelay, () =>
        {
            _watchTimer!.Stop();
            OnFileChanged();
        });
    }

    // ── IPreviewHost ────────────────────────────────────────────────

    public string PreviewDir => _paths.PreviewDir;

    public bool IsDark => _isDark();

    public string EmptyText => "";

    public string RuntimeMissingNote =>
        "編集・保存は使えます。入れると、メモをここに表示できます（https://developer.microsoft.com/microsoft-edge/webview2/）。";

    public event Action<PreviewSource?, bool>? PreviewChanged;

    public void Log(string message) => _log?.Invoke(message);

    // メモには目次も拡大も無い（ドキュメントタブだけ）。
    public event Action<string>? HeadingScrollRequested
    {
        add { }
        remove { }
    }

    public void OnOutline(PreviewSource? source, IReadOnlyList<OutlineHeading> headings) { }

    public void OnPageEscape() { }

    /// <summary>プレビュー内のリンク：対象フォルダ内の md/html はドキュメントタブで開き、それ以外は規則（計画 9.3）で、開いてよい種類だけ既定のアプリ、ほかはファイラーで見せる。</summary>
    public void OpenLocalLink(string fullPath)
    {
        if (!LocalLinkRule.IsUsableLocalPath(fullPath, OperatingSystem.IsWindows()))
            return;
        if (File.Exists(fullPath) && _resolveInApp(fullPath) is { } rel)
        {
            OpenInDocumentsRequested?.Invoke(rel);
            return;
        }
        switch (LocalLinkRule.Decide(fullPath, OperatingSystem.IsWindows(), LinkProbe.Real))
        {
            case LocalLinkAction.OpenWithDefaultApp:
                _services.OpenWithDefaultApp(fullPath);
                break;
            case LocalLinkAction.Reveal:
                _services.RevealInFileManager(fullPath);
                break;
        }
    }

    // ── 画面とのやりとり ────────────────────────────────────────────

    /// <summary>ドキュメントタブで開いてほしい（引数は対象フォルダからの相対パス）。</summary>
    public event Action<string>? OpenInDocumentsRequested;

    /// <summary>入力欄にフォーカスしてほしい。</summary>
    public event Action? FocusEditorRequested;

    public bool IsEditing => _editor.IsEditing;

    public bool IsPreviewing => !_editor.IsEditing;

    /// <summary>未保存の入力があるか（閉じるときの確認用）。</summary>
    public bool IsDirty => _editor.IsDirty;

    /// <summary>入力欄の内容。</summary>
    public string Draft
    {
        get => _editor.Draft;
        set
        {
            if (_editor.Draft == value)
                return;
            _editor.Draft = value;
            OnPropertyChanged();
            RaiseStateChanged();
        }
    }

    public string StatusText =>
        _editor.IsEditing ? (_editor.IsDirty ? "編集中（未保存）" : "編集中")
        : _lastWrite is { } t ? $"最終更新 {t:yyyy/MM/dd HH:mm}" : "";

    /// <summary>読めない理由、または保存できなかった理由（赤で出す）。</summary>
    public string ErrorText => _saveError ?? _loadError ?? "";

    public bool HasError => ErrorText.Length > 0;

    /// <summary>読めていないメモは編集させない（空として保存すると、既存のメモを消すため）。</summary>
    public bool CanEdit => _loaded && !_disposed;

    // ── 始める・読む ────────────────────────────────────────────────

    /// <summary>タブが表示されたときに呼ぶ。最初の 1 回は読み込みと監視を始める（起動を遅くしない）。読めていなければ読み直す。</summary>
    public void Start()
    {
        if (_disposed)
            return;
        if (_started)
        {
            if (!_loaded)
                LoadInitial();
            return;
        }

        _started = true;
        LoadInitial();
        StartWatcher();
    }

    private void LoadInitial()
    {
        try
        {
            var memo = _store.Load(_projectFolder);
            _editor.OnExternalChange(memo.Text);
            _lastWrite = memo.LastWriteTime;
            _loaded = true;
            _loadError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _loadError = $"メモを読めません：{ex.Message}";
        }
        RaiseStateChanged();
        RaisePreview(false);
    }

    private void StartWatcher()
    {
        try
        {
            Directory.CreateDirectory(_paths.ProjectsDir);
            var file = _paths.ProjectMemoFile(_projectFolder);
            _watcher = new FileSystemWatcher(_paths.ProjectsDir, Path.GetFileName(file))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            FileSystemEventHandler changed = (_, _) => ScheduleReload();
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Renamed += (_, _) => ScheduleReload();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log?.Invoke($"メモの監視を始められない: {ex.Message}");
        }
    }

    // 300ms まとめてから、UI スレッドで読み直す。
    private void ScheduleReload() =>
        _ui.Post(_ =>
        {
            if (_disposed)
                return;
            _watchTimer.Stop();
            _watchTimer.Start();
        }, null);

    private void OnFileChanged()
    {
        if (_disposed)
            return;
        if (!_loaded)
        {
            LoadInitial();
            return;
        }

        MemoFile memo;
        try
        {
            memo = _store.Load(_projectFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"メモの読み直しに失敗（今の表示のまま）: {ex.Message}");
            return;
        }

        _lastWrite = memo.LastWriteTime;
        var rebuild = _editor.OnExternalChange(memo.Text);
        RaiseStateChanged();
        if (rebuild)
            RaisePreview(true);
    }

    // ── 編集・保存・キャンセル ──────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanBeginEdit))]
    private void BeginEdit()
    {
        _editor.BeginEdit();
        _saveError = null;
        RaiseStateChanged();
        OnPropertyChanged(nameof(Draft));
        FocusEditorRequested?.Invoke();
    }

    private bool CanBeginEdit() => CanEdit && !_editor.IsEditing;

    [RelayCommand(CanExecute = nameof(IsEditing))]
    private void Save()
    {
        if (!_editor.IsEditing)
            return;

        // 何も変えていないときは書かない（編集中に外で書き換わっていても、古い内容で戻さない）。
        if (!_editor.IsDirty)
        {
            LeaveEditing();
            return;
        }

        TrySave();
    }

    /// <summary>閉じるときの保存。失敗したら false（閉じない。理由はバーに出る）。</summary>
    public bool TrySaveForClose() => !_editor.IsDirty || TrySave();

    private bool TrySave()
    {
        var text = _editor.Draft;
        try
        {
            _lastWrite = _store.Save(_projectFolder, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _saveError = $"保存できません：{ex.Message}";
            RaiseStateChanged();
            return false;
        }

        _saveError = null;
        _editor.MarkSaved(text);
        RaiseStateChanged();
        OnPropertyChanged(nameof(Draft));
        RaisePreview(true);
        return true;
    }

    [RelayCommand(CanExecute = nameof(IsEditing))]
    private async Task CancelAsync()
    {
        if (!_editor.IsEditing)
            return;
        if (_editor.IsDirty)
        {
            if (!await _services.ConfirmAsync("Miharikun - メモ", "変更を破棄しますか？"))
                return;
            // 確認している間に、状態が変わっていたら何もしない
            if (!_editor.IsEditing || !_editor.IsDirty)
                return;
        }
        LeaveEditing();
    }

    private void LeaveEditing()
    {
        var rebuild = _editor.Discard();
        _saveError = null;
        RaiseStateChanged();
        OnPropertyChanged(nameof(Draft));
        if (rebuild)
            RaisePreview(true);
    }

    // ── 表示 ────────────────────────────────────────────────────────

    /// <summary>テーマが変わった。色を変えて作り直す。</summary>
    public void OnThemeChanged()
    {
        if (_started)
            RaisePreview(true);
    }

    private PreviewSource? BuildSource()
    {
        if (!_loaded)
            return _loadError is null ? null : new PreviewSource.Message(_loadError);
        if (string.IsNullOrWhiteSpace(_editor.Saved))
            return new PreviewSource.Message(EmptyMemoText);
        return new PreviewSource.Markdown(_editor.Saved, _projectFolder, _paths.ProjectMemoFile(_projectFolder), "プロジェクトのメモ");
    }

    private void RaisePreview(bool reload) => PreviewChanged?.Invoke(BuildSource(), reload);

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsPreviewing));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanEdit));
        BeginEditCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _disposed = true;
        _watchTimer.Stop();
        _watcher?.Dispose();
    }
}
