using Miharikun.Core.Projects;

namespace Miharikun.Tests.Core;

/// <summary>29-4：シンボリックリンクをたどった実パス（計画 7.14）。リンクを作れる mac で確かめる。Windows は何もしない。</summary>
public sealed class RealPathTests : IDisposable
{
    // 一時フォルダ自体が /var → /private/var のリンクの下にあるので、基準は実パスで取る
    private readonly string _dir = RealPath.Resolve(Path.Combine(Path.GetTempPath(), "mk-realpath-" + Guid.NewGuid().ToString("N")));

    public RealPathTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
        RealPath.ClearCache();
    }

    private string Dir(string name)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private void Link(string link, string target) => Directory.CreateSymbolicLink(link, target);

    [WindowsFact]
    public void Windows_returns_the_path_as_it_is()
    {
        Assert.Equal(@"C:\work\proj", RealPath.Resolve(@"C:\work\proj"));
    }

    [MacFact]
    public void A_path_without_links_is_returned_as_it_is()
    {
        var real = Dir("real");

        Assert.Equal(real, RealPath.Resolve(real));
        Assert.Equal("/", RealPath.Resolve("/"));
    }

    [MacFact]
    public void The_system_temp_folder_resolves_to_private_var()
    {
        // /var → /private/var、/tmp → /private/tmp は macOS の標準
        Assert.Equal("/private/tmp", RealPath.Resolve("/tmp"));
        Assert.Equal("/private/tmp/a/b", RealPath.Resolve("/tmp/a/b"));   // 無い部品はそのまま付ける
    }

    [MacFact]
    public void A_link_in_the_middle_of_the_path_is_followed()
    {
        var real = Dir("real");
        Dir("real/sub");
        Link(Path.Combine(_dir, "lnk"), real);

        Assert.Equal(real, RealPath.Resolve(Path.Combine(_dir, "lnk")));
        Assert.Equal(Path.Combine(real, "sub"), RealPath.Resolve(Path.Combine(_dir, "lnk", "sub")));
    }

    [MacFact]
    public void A_relative_link_and_a_chain_of_links_are_followed()
    {
        var real = Dir("real");
        Link(Path.Combine(_dir, "l1"), "real");             // 相対パスのリンク
        Link(Path.Combine(_dir, "l2"), Path.Combine(_dir, "l1"));   // リンクへのリンク

        Assert.Equal(real, RealPath.Resolve(Path.Combine(_dir, "l1")));
        Assert.Equal(real, RealPath.Resolve(Path.Combine(_dir, "l2")));
    }

    [MacFact]
    public void A_link_with_dotdot_in_its_target_is_followed()
    {
        var real = Dir("a/real");
        Dir("b");
        Link(Path.Combine(_dir, "b", "lnk"), "../a/real");

        Assert.Equal(real, RealPath.Resolve(Path.Combine(_dir, "b", "lnk")));
    }

    [MacFact]
    public void A_broken_link_and_a_loop_return_something_without_hanging()
    {
        Link(Path.Combine(_dir, "broken"), Path.Combine(_dir, "nothing"));
        Link(Path.Combine(_dir, "loopA"), Path.Combine(_dir, "loopB"));
        Link(Path.Combine(_dir, "loopB"), Path.Combine(_dir, "loopA"));

        Assert.Equal(Path.Combine(_dir, "nothing"), RealPath.Resolve(Path.Combine(_dir, "broken")));
        Assert.Equal(Path.Combine(_dir, "loopA"), RealPath.Resolve(Path.Combine(_dir, "loopA")));   // 多すぎるリンクは元のまま
    }

    [MacFact]
    public void Japanese_and_spaces_in_names_survive()
    {
        var real = Dir("日本語 フォルダ");
        Link(Path.Combine(_dir, "リンク"), real);

        Assert.Equal(real, RealPath.Resolve(Path.Combine(_dir, "リンク")));
    }
}
