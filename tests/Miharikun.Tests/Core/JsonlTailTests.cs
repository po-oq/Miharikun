using System.Text;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

public sealed class JsonlTailTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "miharikun-tail-" + Guid.NewGuid().ToString("N") + ".jsonl");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    private void Append(string text) => File.AppendAllText(_path, text, new UTF8Encoding(false));

    [Fact]
    public void Reads_only_appended_lines_with_stable_line_numbers()
    {
        var tail = new JsonlTail();
        Append("a\nb\n");
        Assert.Equal([(1L, "a"), (2L, "b")], tail.ReadNewLines(_path, out _));

        Assert.Empty(tail.ReadNewLines(_path, out _));

        Append("c\n");
        Assert.Equal([(3L, "c")], tail.ReadNewLines(_path, out _));
    }

    [Fact]
    public void Incomplete_last_line_waits_until_it_is_finished()
    {
        var tail = new JsonlTail();
        Append("a\nhalf");
        Assert.Equal([(1L, "a")], tail.ReadNewLines(_path, out _));
        Assert.Empty(tail.ReadNewLines(_path, out _));

        Append("-done\n");
        Assert.Equal([(2L, "half-done")], tail.ReadNewLines(_path, out _));
    }

    [Fact]
    public void Multibyte_line_split_across_reads_is_decoded_correctly()
    {
        var bytes = Encoding.UTF8.GetBytes("日本語\n");
        using (var fs = new FileStream(_path, FileMode.Create, FileAccess.Write))
            fs.Write(bytes, 0, 4);   // 「日」+「本」の途中で切る
        var tail = new JsonlTail();
        Assert.Empty(tail.ReadNewLines(_path, out _));

        using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write))
            fs.Write(bytes, 4, bytes.Length - 4);
        Assert.Equal([(1L, "日本語")], tail.ReadNewLines(_path, out _));
    }

    [Fact]
    public void Crlf_and_blank_lines_are_counted()
    {
        var tail = new JsonlTail();
        Append("a\r\n\nb\r\n");

        Assert.Equal([(1L, "a"), (2L, ""), (3L, "b")], tail.ReadNewLines(_path, out _));
    }

    [Fact]
    public void Truncated_file_is_read_again_from_the_start()
    {
        var tail = new JsonlTail();
        Append("aaaa\nbbbb\n");
        tail.ReadNewLines(_path, out _);

        File.WriteAllText(_path, "x\n");
        var lines = tail.ReadNewLines(_path, out var truncated);

        Assert.True(truncated);
        Assert.Equal([(1L, "x")], lines);
    }

    [Fact]
    public void Can_read_while_another_writer_keeps_the_file_open()
    {
        using var writer = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer.Write(Encoding.UTF8.GetBytes("a\n"));
        writer.Flush();

        Assert.Equal([(1L, "a")], new JsonlTail().ReadNewLines(_path, out _));
    }
}