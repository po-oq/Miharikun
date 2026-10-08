namespace Miharikun.Tests;

/// <summary>Windows でだけ動くテスト（ドライブ文字・\ 区切り・CP932 など、Windows の意味を確かめるもの）。mac では飛ばす。</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows だけのテスト";
    }
}

public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows だけのテスト";
    }
}

/// <summary>mac でだけ動くテスト（/Users/x/… の形・open -R など）。</summary>
public sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "mac だけのテスト";
    }
}

public sealed class MacTheoryAttribute : TheoryAttribute
{
    public MacTheoryAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "mac だけのテスト";
    }
}

/// <summary>OS の絶対パスを作る（たまたま C:\ を使っているだけのテストを両 OS で動かすため）。</summary>
public static class TestPaths
{
    /// <summary>Windows は <c>C:\a\b</c>、mac は <c>/a/b</c>。</summary>
    public static string Abs(params string[] parts) =>
        OperatingSystem.IsWindows()
            ? @"C:\" + string.Join('\\', parts)
            : "/" + string.Join('/', parts);
}
