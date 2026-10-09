using System.Text;
using Miharikun.Core.Storage;

namespace Miharikun.Tests.Core;

/// <summary>44-1：<c>AtomicFile.WriteAllBytes</c>（同梱のファイルの書き出し用。置き換えの仕組みは <c>WriteAllText</c> と共用）。</summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "miharikun-atomic-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void WriteAllBytes_writes_the_bytes_as_they_are_and_creates_the_folder()
    {
        var path = Path.Combine(_dir, "sub", "a.bin");
        byte[] bytes = [0, 1, 2, 0xEF, 0xBB, 0xBF, 255];   // BOM に見える並びも、そのまま

        AtomicFile.WriteAllBytes(path, bytes);

        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteAllBytes_overwrites_an_existing_file()
    {
        var path = Path.Combine(_dir, "a.bin");
        AtomicFile.WriteAllBytes(path, [1, 2, 3, 4, 5]);

        AtomicFile.WriteAllBytes(path, [9]);

        Assert.Equal([(byte)9], File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteAllBytes_leaves_no_temporary_file_behind()
    {
        var path = Path.Combine(_dir, "a.bin");
        AtomicFile.WriteAllBytes(path, [1]);
        AtomicFile.WriteAllBytes(path, [2]);

        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Fact]
    public void WriteAllText_still_writes_utf8_without_a_bom()
    {
        var path = Path.Combine(_dir, "a.txt");

        AtomicFile.WriteAllText(path, "あ");

        Assert.Equal(Encoding.UTF8.GetBytes("あ"), File.ReadAllBytes(path));
    }
}
