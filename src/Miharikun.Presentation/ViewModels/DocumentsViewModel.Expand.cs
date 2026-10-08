using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miharikun.Core.Documents;

namespace Miharikun.ViewModels;

/// <summary>拡大モード（目次｜プレビュー。要件 12.7.1）。状態はここに持つ。</summary>
public sealed partial class DocumentsViewModel
{
    private string? _outlinePath;          // いまの目次を作った（待っている）ファイル

    /// <summary>拡大中か。上段・ツリー・一覧・概要カードを隠し、目次とプレビューを広げる。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOutlineVisible))]
    private bool _isExpanded;

    /// <summary>目次の列を出すか（拡大中で、開いているのが md のとき。html には目次を出さない）。</summary>
    public bool IsOutlineVisible => IsExpanded && CurrentTarget is { Kind: DocumentKind.Markdown };

    /// <summary>目次。入れ物は 1 つを使い回し、中身を合わせる。</summary>
    public ObservableCollection<OutlineItemViewModel> Outline { get; } = [];

    /// <summary>目次の上の進み具合「✅ 済み / 全部」。印つきの見出しが無ければ空。</summary>
    [ObservableProperty] private string _progressText = "";

    [ObservableProperty] private bool _hasProgress;

    /// <summary>描き直しの結果が来て、見出しが 0 件だったか（来る前は false）。</summary>
    [ObservableProperty] private bool _isOutlineEmpty;

    /// <summary>見出し（id）へ移る頼み（プレビューが受ける）。</summary>
    public event Action<string>? HeadingScrollRequested;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleExpanded()
    {
        if (HasSelection)                             // Execute は CanExecute を見ないので、ここでも確かめる
            IsExpanded = !IsExpanded;
    }

    /// <summary>拡大を戻すだけ（切り替えない）。戻したら true（View は true のときだけ Esc を使い切る）。</summary>
    public bool Collapse()
    {
        if (!IsExpanded)
            return false;
        IsExpanded = false;
        return true;
    }

    /// <summary>プレビューのページの中で Esc が押された。</summary>
    public void OnPageEscape() => Collapse();

    [RelayCommand]
    private void JumpToHeading(OutlineItemViewModel? item)
    {
        if (item?.Id is { Length: > 0 } id)
            HeadingScrollRequested?.Invoke(id);
    }

    public void OnOutline(PreviewSource? source, IReadOnlyList<OutlineHeading> headings)
    {
        if (_disposed || source is null || source != CurrentTarget)
            return;                                   // 古い結果（別のファイルを選び直した後）は捨てる

        var minLevel = headings.Count == 0 ? 0 : headings.Min(h => h.Level);
        for (var i = 0; i < headings.Count; i++)
        {
            if (i < Outline.Count)
                Outline[i].Update(headings[i], headings[i].Level - minLevel);
            else
                Outline.Add(new OutlineItemViewModel(headings[i], headings[i].Level - minLevel));
        }
        while (Outline.Count > headings.Count)
            Outline.RemoveAt(Outline.Count - 1);

        var marked = headings.Where(h => h.Done is not null).ToList();
        HasProgress = marked.Count > 0;
        ProgressText = marked.Count > 0 ? $"✅ {marked.Count(h => h.Done == true)} / {marked.Count}" : "";
        IsOutlineEmpty = headings.Count == 0;
    }

    /// <summary>別のファイルに替わった（または無くなった）。目次を空にして、新しい結果を待つ。</summary>
    private void ResetOutline(string? path)
    {
        _outlinePath = path;
        Outline.Clear();
        HasProgress = false;
        ProgressText = "";
        IsOutlineEmpty = false;
    }
}
