using Miharikun.Core.Memo;
using Miharikun.Core.Storage;
using Miharikun.ViewModels;

namespace Miharikun.Tests.Presentation;

/// <summary>28-3：メモタブ（キャンセルの確認：はい／いいえ・変更なしならすぐ戻る・確認の間に状態が変わったら何もしない・リンク）。</summary>
public sealed class MemoViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-memovm-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly FakeUiServices _ui = new();
    private readonly AppPaths _paths;
    private readonly ProjectMemoStore _store;
    private readonly MemoViewModel _vm;
    private readonly List<string> _openedInDocuments = [];
    private Func<string, string?> _resolve = _ => null;

    public MemoViewModelTests()
    {
        _project = Path.Combine(_dir, "proj");
        Directory.CreateDirectory(_project);
        _paths = new AppPaths(_dir);
        _store = new ProjectMemoStore(_paths);
        _store.Save(_project, "保存済みのメモ");
        _vm = new MemoViewModel(_project, _store, _paths, () => false, new ImmediateSynchronizationContext(),
            path => _resolve(path), _ui);
        _vm.OpenInDocumentsRequested += _openedInDocuments.Add;
        _vm.Start();
    }

    public void Dispose()
    {
        _vm.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------------------------------------------------------------- キャンセル

    [Fact]
    public async Task Cancel_without_changes_leaves_editing_at_once_without_asking()
    {
        _vm.BeginEditCommand.Execute(null);
        Assert.True(_vm.IsEditing);

        await _vm.CancelCommand.ExecuteAsync(null);

        Assert.False(_vm.IsEditing);
        Assert.Empty(_ui.Confirms);
    }

    [Fact]
    public async Task Cancel_with_changes_asks_and_No_keeps_editing_with_the_input()
    {
        _vm.BeginEditCommand.Execute(null);
        _vm.Draft = "書きかけ";
        _ui.ConfirmAnswer = false;

        await _vm.CancelCommand.ExecuteAsync(null);

        Assert.True(_vm.IsEditing);
        Assert.Equal("書きかけ", _vm.Draft);
        var (title, message) = Assert.Single(_ui.Confirms);
        Assert.Equal("Miharikun - メモ", title);
        Assert.Equal("変更を破棄しますか？", message);
    }

    [Fact]
    public async Task Cancel_with_changes_and_Yes_discards_the_input()
    {
        _vm.BeginEditCommand.Execute(null);
        _vm.Draft = "書きかけ";
        _ui.ConfirmAnswer = true;

        await _vm.CancelCommand.ExecuteAsync(null);

        Assert.False(_vm.IsEditing);
        Assert.Equal("保存済みのメモ", _store.Load(_project).Text);   // 保存済みの内容は変わらない
    }

    [Fact]
    public async Task Cancel_does_nothing_when_the_state_changed_while_asking()
    {
        _vm.BeginEditCommand.Execute(null);
        _vm.Draft = "書きかけ";
        _ui.OnConfirm = () =>
        {
            _vm.Draft = "保存済みのメモ";   // 確認している間に、入力が元に戻って変更なしになった
            return true;
        };

        await _vm.CancelCommand.ExecuteAsync(null);

        Assert.True(_vm.IsEditing);   // 何もしない（編集のまま）
    }

    [Fact]
    public async Task Cancel_does_nothing_when_editing_ended_while_asking()
    {
        _vm.BeginEditCommand.Execute(null);
        _vm.Draft = "書きかけ";
        _ui.OnConfirm = () =>
        {
            Assert.True(_vm.TrySaveForClose());   // 確認している間に保存された（編集は続いているが変更は無くなる）
            return true;
        };

        await _vm.CancelCommand.ExecuteAsync(null);

        Assert.Equal("書きかけ", _store.Load(_project).Text);   // 保存した内容を、破棄で戻さない
    }

    [Fact]
    public void Cancel_is_available_only_while_editing()
    {
        Assert.False(_vm.CancelCommand.CanExecute(null));
        _vm.BeginEditCommand.Execute(null);
        Assert.True(_vm.CancelCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- リンク

    [Fact]
    public void A_link_to_a_document_inside_the_project_is_opened_in_the_documents_tab()
    {
        var file = Path.Combine(_project, "docs", "a.md");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "# a");
        _resolve = path => path == file ? "docs/a.md" : null;

        _vm.OpenLocalLink(file);

        Assert.Equal(["docs/a.md"], _openedInDocuments);
        Assert.Empty(_ui.Opened);
    }

    [Fact]
    public void A_file_is_opened_with_the_default_app_a_folder_is_only_revealed_and_a_missing_one_is_ignored()
    {
        var other = Path.Combine(_dir, "other.txt");
        File.WriteAllText(other, "x");

        _vm.OpenLocalLink(other);
        _vm.OpenLocalLink(_dir);
        _vm.OpenLocalLink(Path.Combine(_dir, "ない.txt"));

        Assert.Equal([other], _ui.Opened);
        Assert.Equal([_dir], _ui.Revealed);
        Assert.Empty(_openedInDocuments);
    }

    [Fact]
    public void Files_that_could_start_a_program_are_only_revealed_never_opened()
    {
        foreach (var name in new[] { "run.bat", "run.command", "run.exe" })   // 中身はただの文字
        {
            var f = Path.Combine(_dir, name);
            File.WriteAllText(f, "echo hi");
            _vm.OpenLocalLink(f);
        }

        Assert.Empty(_ui.Opened);
        Assert.Equal(3, _ui.Revealed.Count);
    }

    [Theory]
    [InlineData(@"\\server\share\x.txt")]
    [InlineData("//server/share/x.txt")]
    public void A_network_form_of_path_is_dropped_before_anything_is_looked_at(string path)
    {
        var asked = new List<string>();
        _resolve = p => { asked.Add(p); return null; };

        _vm.OpenLocalLink(path);

        Assert.Empty(_ui.Opened);
        Assert.Empty(_ui.Revealed);
        Assert.Empty(asked);
    }

    // ---------------------------------------------------------------- タイマー

    [Fact]
    public void The_reload_timer_is_a_300ms_ui_timer_that_is_stopped_on_Dispose()
    {
        var timer = _ui.TimerOf(TimeSpan.FromMilliseconds(300));
        timer.Start();

        _vm.Dispose();

        Assert.False(timer.IsEnabled);
    }
}
