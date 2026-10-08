using Miharikun.Core.Projects;

namespace Miharikun.Core.Documents;

/// <summary>
/// 相対パスの比較：NFC にそろえて、大文字小文字を無視する（計画 7.14）。mac の Finder などが作った日本語の名前は
/// 濁点が分かれた形（NFD）のことがあり、md に打ったリンクは NFC。形が違うと、リンクがアプリ内で開けず既定のアプリで開いてしまう。
/// </summary>
public sealed class NfcPathComparer : IEqualityComparer<string>
{
    public static readonly NfcPathComparer Instance = new();

    public bool Equals(string? x, string? y) =>
        x is null || y is null ? x is null && y is null : string.Equals(ProjectPath.ToNfc(x), ProjectPath.ToNfc(y), StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(string obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(ProjectPath.ToNfc(obj));
}
