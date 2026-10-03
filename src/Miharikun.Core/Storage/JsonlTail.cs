using System.Text;

namespace Miharikun.Core.Storage;

/// <summary>
/// JSONL ファイル1本分の読み取り位置を持ち、追記分の「完結した行」だけを返す（要件 9章）。
/// 末尾の改行で終わっていない行は次回に回す。行番号は1始まりで、壊れた行・空行も数える（AgentEvent.Seq の元）。
/// </summary>
public sealed class JsonlTail
{
    private long _offset;
    private long _lineNumber;

    public void Reset()
    {
        _offset = 0;
        _lineNumber = 0;
    }

    /// <param name="truncated">ファイルが小さくなっていたため先頭から読み直した場合 true。</param>
    public IReadOnlyList<(long LineNumber, string Text)> ReadNewLines(string path, out bool truncated)
    {
        truncated = false;

        // Hook が追記中でも、リネーム・削除されても読めるよう共有を最大にする。
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length < _offset)
        {
            Reset();
            truncated = true;
        }
        if (fs.Length == _offset)
            return [];

        fs.Seek(_offset, SeekOrigin.Begin);
        var buffer = new byte[fs.Length - _offset];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = fs.Read(buffer, read, buffer.Length - read);
            if (n == 0)
                break;
            read += n;
        }

        var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
        if (lastNewline < 0)
            return [];

        var lines = new List<(long, string)>();
        var start = 0;
        for (var i = 0; i <= lastNewline; i++)
        {
            if (buffer[i] != (byte)'\n')
                continue;

            var end = i > start && buffer[i - 1] == (byte)'\r' ? i - 1 : i;
            _lineNumber++;
            lines.Add((_lineNumber, Encoding.UTF8.GetString(buffer, start, end - start)));
            start = i + 1;
        }

        _offset += lastNewline + 1;
        return lines;
    }
}