namespace Miharikun.Core.Documents;

/// <summary>gitignore の 1 行ぶんの規則（要件 12.7）。正規表現は使わず手書きで照合する（AOT 互換・高速）。</summary>
internal sealed class GitIgnorePattern
{
    private readonly string[] _segments;
    private readonly bool _anchored;

    private GitIgnorePattern(string[] segments, bool anchored, bool directoryOnly, bool negated)
    {
        _segments = segments;
        _anchored = anchored;
        DirectoryOnly = directoryOnly;
        Negated = negated;
    }

    public bool Negated { get; }

    public bool DirectoryOnly { get; }

    /// <summary>コメント・空行・意味のない行は null。</summary>
    public static GitIgnorePattern? Parse(string line)
    {
        var text = line.TrimEnd(' ', '\t', '\r', '\n');
        if (text.Length == 0 || text[0] == '#')
            return null;

        var negated = false;
        if (text[0] == '!')
        {
            negated = true;
            text = text[1..];
        }
        else if (text.Length > 1 && text[0] == '\\' && (text[1] == '#' || text[1] == '!'))
        {
            text = text[1..];                                    // \# \! は文字そのもの
        }

        var directoryOnly = false;
        if (text.EndsWith('/'))
        {
            directoryOnly = true;
            text = text.TrimEnd('/');
        }

        var anchored = text.StartsWith('/');
        text = text.TrimStart('/');
        if (text.Contains('/'))
            anchored = true;

        var segments = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        return new GitIgnorePattern(segments, anchored, directoryOnly, negated);
    }

    /// <summary>パス（区切りで分けた部品）が、この規則に当たるか。フォルダ限定の判定は呼び出し側。</summary>
    public bool Matches(string[] path)
    {
        if (path.Length == 0)
            return false;

        return _anchored
            ? MatchSegments(_segments, 0, path, 0)
            : MatchSegment(_segments[0], path[^1]);              // 「/」なし＝名前だけを見る（任意の深さ）
    }

    private static bool MatchSegments(string[] pattern, int pi, string[] path, int si)
    {
        while (pi < pattern.Length)
        {
            if (pattern[pi] == "**")
            {
                if (pi == pattern.Length - 1)
                    return si < path.Length;                     // 末尾の「/**」は中身すべて（本体は含まない）
                for (var k = si; k <= path.Length; k++)
                    if (MatchSegments(pattern, pi + 1, path, k))
                        return true;
                return false;
            }

            if (si >= path.Length || !MatchSegment(pattern[pi], path[si]))
                return false;
            pi++;
            si++;
        }
        return si == path.Length;
    }

    /// <summary>1 区間の照合：「*」は任意の文字列、「?」は 1 文字。大文字小文字は区別しない。</summary>
    private static bool MatchSegment(string pattern, string name)
    {
        int p = 0, n = 0, starP = -1, starN = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++;
                starN = n;
            }
            else if (p < pattern.Length && (pattern[p] == '?' || SameChar(pattern[p], name[n])))
            {
                p++;
                n++;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                n = ++starN;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] == '*')
            p++;
        return p == pattern.Length;
    }

    private static bool SameChar(char a, char b) =>
        a == b || char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
}
