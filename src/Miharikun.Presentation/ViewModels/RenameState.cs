using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Miharikun.ViewModels;

/// <summary>タイトルのインライン編集。Enter / フォーカスアウトで確定、Esc で取り消し。</summary>
public sealed partial class RenameState : ObservableObject
{
    private readonly Func<string> _current;
    private readonly Action<string> _save;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _text = "";

    public RelayCommand BeginCommand { get; }
    public RelayCommand CommitCommand { get; }
    public RelayCommand CancelCommand { get; }

    public RenameState(Func<string> current, Action<string> save)
    {
        _current = current;
        _save = save;
        BeginCommand = new RelayCommand(Begin);
        CommitCommand = new RelayCommand(Commit);
        CancelCommand = new RelayCommand(() => IsEditing = false);
    }

    private void Begin()
    {
        Text = _current();
        IsEditing = true;
    }

    // Enter で確定したあとにフォーカスアウトでも呼ばれるので、編集中でなければ何もしない。
    private void Commit()
    {
        if (!IsEditing)
            return;

        IsEditing = false;
        if (Text.Trim() != _current())
            _save(Text);   // 空にすると自動タイトルに戻る
    }
}