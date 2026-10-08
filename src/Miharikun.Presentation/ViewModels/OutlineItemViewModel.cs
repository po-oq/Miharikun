using CommunityToolkit.Mvvm.ComponentModel;

namespace Miharikun.ViewModels;

/// <summary>目次の 1 行。再読み込みでは入れ物ごと作り直さず <see cref="Update"/> で中身だけ合わせる（一覧のスクロール位置を保つため）。</summary>
public sealed class OutlineItemViewModel : ObservableObject
{
    private int _depth;
    private string? _id;
    private string _displayText = "";
    private string _taskText = "";

    public OutlineItemViewModel(OutlineHeading heading, int depth) => Update(heading, depth);

    /// <summary>その md のいちばん浅い段を 0 とした深さ。</summary>
    public int Depth
    {
        get => _depth;
        private set => SetProperty(ref _depth, value);
    }

    /// <summary>移る先の id。無ければ押しても移らない。</summary>
    public string? Id
    {
        get => _id;
        private set => SetProperty(ref _id, value);
    }

    /// <summary>印（✅／⬜）つきの文字。</summary>
    public string DisplayText
    {
        get => _displayText;
        private set => SetProperty(ref _displayText, value);
    }

    /// <summary>節のタスク数「済み/全部」。0 件なら空。</summary>
    public string TaskText
    {
        get => _taskText;
        private set => SetProperty(ref _taskText, value);
    }

    public void Update(OutlineHeading heading, int depth)
    {
        Depth = depth;
        Id = heading.Id;
        DisplayText = heading.Done switch
        {
            true => "✅ " + heading.Text,
            false => "⬜ " + heading.Text,
            _ => heading.Text,
        };
        TaskText = heading.TasksTotal > 0 ? $"{heading.TasksDone}/{heading.TasksTotal}" : "";
    }
}
