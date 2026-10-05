using Microsoft.Web.WebView2.Core;

namespace Miharikun;

/// <summary>
/// WebView2 の環境（作業フォルダ・Cookie 等）を 1 つだけ作って、ドキュメントタブとメモタブで共用する。
/// 同じ作業フォルダで環境を 2 つ作ると、設定が違ったときに失敗するため。UI スレッドから呼ぶ。
/// </summary>
public static class WebViewEnvironment
{
    private static Task<CoreWebView2Environment>? _task;

    /// <summary>作業フォルダ（App 起動時に <see cref="Configure"/> で決める）。</summary>
    public static string? DataDir { get; private set; }

    public static void Configure(string dataDir) => DataDir = dataDir;

    /// <summary>Runtime が入っているか。入っていなければ、案内の文は呼び手が出す。</summary>
    public static bool IsRuntimeInstalled()
    {
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
            return true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }

    /// <summary>最初の 1 回だけ作る。失敗・取り消しは覚えず、次の呼び出しで作り直す。</summary>
    public static Task<CoreWebView2Environment> GetAsync()
    {
        var task = _task;
        if (task is null || task.IsFaulted || task.IsCanceled)
            _task = task = CoreWebView2Environment.CreateAsync(null, DataDir);
        return task;
    }
}
