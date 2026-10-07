using Avalonia;

namespace Miharikun;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 古い macOS が Finder から渡すことがある -psn_ の引数は取り除く（計画 7.7）
        args = [.. args.Where(a => !a.StartsWith("-psn_", StringComparison.Ordinal))];
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
