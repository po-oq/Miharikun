namespace Miharikun.Services;

/// <summary>
/// 重い処理（git）を背景で実行する口。製品はスレッドプール（<see cref="ThreadPoolRunner"/>）。続きは、呼んだ側の
/// 同期コンテキスト（UI スレッド）に戻る。テストは呼んだスレッドの上で同期的に実行する（背景の続きが、テストの最中に
/// 一覧を触って競合するのを避ける）。
/// </summary>
public interface IBackgroundRunner
{
    Task<T> Run<T>(Func<T> work);
}

public sealed class ThreadPoolRunner : IBackgroundRunner
{
    public Task<T> Run<T>(Func<T> work) => Task.Run(work);
}
