using System.Net;
using System.Text;

namespace Miharikun.Core.Documents;

/// <summary>「ドキュメント概要」カードの内容（要件 12.7）。タイトルは先頭 64KB だけ見る。</summary>
public sealed record DocumentOverview(
    string Title, string RelativePath, DateTime ModifiedUtc, DateTime CreatedUtc, int LineCount, long Size)
{
    private const int HeadBytes = 64 * 1024;

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>
    /// ファイルを読んで概要を作る（重いファイルでは時間がかかるので、呼び出し側は背景で実行する）。
    /// タイトル：md は最初の見出し（コードブロック内は除く）、html は &lt;title&gt;、無い・読めないときはファイル名。
    /// 行数は全文（バイトで数えるので文字コードに依存しない）。
    /// </summary>
    public static DocumentOverview Read(string root, string relativePath)
    {
        var info = new FileInfo(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var name = relativePath[(relativePath.LastIndexOf('/') + 1)..];

        string? title = null;
        var lines = 0;
        using (var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var buffer = new byte[HeadBytes];
            var read = stream.Read(buffer, 0, buffer.Length);
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0)
                    break;
                read += n;
            }

            title = ExtractTitle(name, Utf8.GetString(buffer, 0, read).TrimStart('﻿'));
            lines = CountLines(buffer, read, stream);
        }

        return new DocumentOverview(string.IsNullOrWhiteSpace(title) ? name : title, relativePath,
            info.LastWriteTimeUtc, info.CreationTimeUtc, lines, info.Length);
    }

    private static int CountLines(byte[] head, int headLength, Stream rest)
    {
        var count = 0;
        var last = (byte)0;
        var any = headLength > 0;

        for (var i = 0; i < headLength; i++)
            if (head[i] == (byte)'\n')
                count++;
        if (headLength > 0)
            last = head[headLength - 1];

        if (headLength == head.Length)
        {
            var chunk = new byte[HeadBytes];
            int n;
            while ((n = rest.Read(chunk, 0, chunk.Length)) > 0)
            {
                any = true;
                for (var i = 0; i < n; i++)
                    if (chunk[i] == (byte)'\n')
                        count++;
                last = chunk[n - 1];
            }
        }

        return any && last != (byte)'\n' ? count + 1 : count;
    }

    private static string? ExtractTitle(string fileName, string head)
    {
        var title = DocumentEntry.TryGetKind(fileName, out var kind) && kind == DocumentKind.Html
            ? HtmlTitle(head)
            : MarkdownTitle(head);
        return title is null || title.Contains('�') ? null : title;       // 読めない文字（UTF-8 でない）ならファイル名に任せる
    }

    private static string? MarkdownTitle(string text)
    {
        char fence = '\0';
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart(' ');
            var indent = line.Length - trimmed.Length;

            if (indent <= 3 && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
            {
                if (fence == '\0')
                    fence = trimmed[0];
                else if (fence == trimmed[0])
                    fence = '\0';
                continue;
            }
            if (fence != '\0' || indent > 3)
                continue;

            var hashes = 0;
            while (hashes < trimmed.Length && trimmed[hashes] == '#')
                hashes++;
            if (hashes is < 1 or > 6 || hashes >= trimmed.Length || (trimmed[hashes] != ' ' && trimmed[hashes] != '\t'))
                continue;

            var heading = trimmed[hashes..].Trim();
            var closing = heading.TrimEnd('#');
            if (closing.Length < heading.Length && (closing.Length == 0 || closing[^1] is ' ' or '\t'))
                heading = closing.TrimEnd();
            if (heading.Length > 0)
                return heading;
        }
        return null;
    }

    private static string? HtmlTitle(string text)
    {
        var open = text.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
        if (open < 0)
            return null;
        var start = text.IndexOf('>', open);
        if (start < 0)
            return null;
        var end = text.IndexOf("</title", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            return null;

        var decoded = WebUtility.HtmlDecode(text[(start + 1)..end]);
        var sb = new StringBuilder(decoded.Length);
        var space = false;
        foreach (var c in decoded)
        {
            if (char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }
            if (space)
                sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
