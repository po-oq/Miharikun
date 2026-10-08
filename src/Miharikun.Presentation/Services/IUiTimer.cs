namespace Miharikun.Services;

/// <summary>UI スレッドで定期的に呼ばれるタイマー（<see cref="IUiServices.CreateTimer"/> で作る）。</summary>
public interface IUiTimer : IDisposable
{
    bool IsEnabled { get; }

    void Start();

    void Stop();
}
