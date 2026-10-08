using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Miharikun.ViewModels;

/// <summary>ドキュメントタブのツリーの 1 行。RelativePath が null のものは「すべて」。</summary>
public sealed partial class FolderNodeViewModel(string? relativePath, string name) : ObservableObject
{
    public string? RelativePath { get; } = relativePath;

    public string Name { get; } = name;

    /// <summary>子孫を含む件数。</summary>
    [ObservableProperty] private int _count;

    [ObservableProperty] private bool _isExpanded;

    [ObservableProperty] private bool _isSelected;

    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    /// <summary>画面の操作（クリック・展開）で変わったときに、親の ViewModel へ知らせる。</summary>
    public event Action<FolderNodeViewModel>? SelectionChanged;

    public event Action<FolderNodeViewModel>? ExpansionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            SelectionChanged?.Invoke(this);
    }

    partial void OnIsExpandedChanged(bool value) => ExpansionChanged?.Invoke(this);
}
