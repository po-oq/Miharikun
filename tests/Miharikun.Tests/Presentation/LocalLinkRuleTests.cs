namespace Miharikun.Tests.Presentation;

/// <summary>43-1：プレビューのローカルのリンクの規則（計画 9.3 の表の行ごと）。ファイルの有無・リンクの中身は差し替えて試す。</summary>
public sealed class LocalLinkRuleTests
{
    /// <summary>ファイル・フォルダ・リンク（パス → 1 段の先）を持つ作り物。呼ばれたパスを覚える。</summary>
    private sealed class FakeFs
    {
        public HashSet<string> Files { get; } = [];
        public HashSet<string> Dirs { get; } = [];
        public Dictionary<string, string> Links { get; } = [];
        public List<string> Touched { get; } = [];

        public LinkProbe Probe => new(
            p => { Touched.Add(p); return Files.Contains(p); },
            p => { Touched.Add(p); return Dirs.Contains(p); },
            p => Links.TryGetValue(p, out var t) ? t : null);
    }

    private static LocalLinkAction Win(FakeFs fs, string path) => LocalLinkRule.Decide(path, isWindows: true, fs.Probe);
    private static LocalLinkAction Mac(FakeFs fs, string path) => LocalLinkRule.Decide(path, isWindows: false, fs.Probe);

    // ---- 判定 1：使える形のパスだけ。ファイルシステムには触らない ----

    [Theory]
    [InlineData(@"\\server\share\a.txt")]
    [InlineData("//server/share/a.txt")]
    [InlineData(@"\/server\share\a.txt")]
    [InlineData(@"/\server\share\a.txt")]
    [InlineData(@"\??\UNC\server\share\a.txt")]
    [InlineData(@"\\?\C:\p\a.txt")]
    [InlineData(@"\\.\C:\p\a.txt")]
    [InlineData(@"\a.txt")]
    [InlineData(@"a.txt")]
    [InlineData("")]
    public void Windows_ignores_network_device_rootless_and_relative_forms_without_touching_the_file_system(string path)
    {
        var fs = new FakeFs();
        fs.Files.Add(path);
        fs.Dirs.Add(path);

        Assert.Equal(LocalLinkAction.Ignore, Win(fs, path));
        Assert.False(LocalLinkRule.IsUsableLocalPath(path, isWindows: true));
        Assert.Empty(fs.Touched);
    }

    [Theory]
    [InlineData("//server/a.txt")]
    [InlineData("/net/host/a.txt")]
    [InlineData("/net/")]
    [InlineData("a.txt")]
    [InlineData("")]
    public void Mac_ignores_double_slash_net_automount_and_relative_forms_without_touching_the_file_system(string path)
    {
        var fs = new FakeFs();
        fs.Files.Add(path);

        Assert.Equal(LocalLinkAction.Ignore, Mac(fs, path));
        Assert.False(LocalLinkRule.IsUsableLocalPath(path, isWindows: false));
        Assert.Empty(fs.Touched);
    }

    [Fact]
    public void Usable_forms_are_drive_paths_on_windows_and_single_slash_paths_on_mac()
    {
        Assert.True(LocalLinkRule.IsUsableLocalPath(@"C:\p\a.txt", true));
        Assert.True(LocalLinkRule.IsUsableLocalPath("c:/p/a.txt", true));
        Assert.True(LocalLinkRule.IsUsableLocalPath("/Users/x/a.txt", false));
        Assert.True(LocalLinkRule.IsUsableLocalPath("/network/a.txt", false));   // /net/ ではない
    }

    // ---- 判定 2：Windows の代替データストリーム ----

    [Theory]
    [InlineData(@"C:\p\a.txt:x.exe")]
    [InlineData(@"C:\p\a.exe:x.txt")]
    public void Windows_ignores_a_path_with_a_colon_besides_the_drive(string path)
    {
        var fs = new FakeFs();
        fs.Files.Add(path);

        Assert.Equal(LocalLinkAction.Ignore, Win(fs, path));
    }

    [Fact]
    public void Windows_drive_colon_alone_goes_on_to_the_next_steps()
    {
        var fs = new FakeFs();
        fs.Files.Add(@"C:\p\a.txt");

        Assert.Equal(LocalLinkAction.OpenWithDefaultApp, Win(fs, @"C:\p\a.txt"));
    }

    [Fact]
    public void Mac_does_not_apply_the_colon_rule()
    {
        var fs = new FakeFs();
        fs.Files.Add("/p/a:b.txt");

        Assert.Equal(LocalLinkAction.OpenWithDefaultApp, Mac(fs, "/p/a:b.txt"));
    }

    // ---- 判定 4〜6：フォルダ・無いファイル・種類 ----

    [Fact]
    public void A_folder_a_link_to_a_folder_and_an_app_bundle_are_revealed_not_opened()
    {
        var fs = new FakeFs();
        fs.Dirs.Add("/p/dir");
        fs.Dirs.Add("/p/x.app");
        fs.Dirs.Add("/p/real");
        fs.Links["/p/a.txt"] = "/p/real";

        Assert.Equal(LocalLinkAction.Reveal, Mac(fs, "/p/dir"));
        Assert.Equal(LocalLinkAction.Reveal, Mac(fs, "/p/x.app"));
        Assert.Equal(LocalLinkAction.Reveal, Mac(fs, "/p/a.txt"));
    }

    [Fact]
    public void A_missing_file_is_ignored()
    {
        Assert.Equal(LocalLinkAction.Ignore, Mac(new FakeFs(), "/p/ない.txt"));
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("A.PDF")]
    [InlineData("b.png")]
    [InlineData("c.MD")]
    [InlineData("d.html")]
    [InlineData("e.htm")]
    [InlineData("f.jpg")]
    [InlineData("f.jpeg")]
    [InlineData("g.gif")]
    [InlineData("h.webp")]
    [InlineData("i.bmp")]
    [InlineData("j.svg")]
    public void Allowed_kinds_are_opened_with_the_default_app_whatever_the_case(string name)
    {
        var fs = new FakeFs();
        fs.Files.Add("/p/" + name);

        Assert.Equal(LocalLinkAction.OpenWithDefaultApp, Mac(fs, "/p/" + name));
    }

    [Theory]
    [InlineData("a.bat")]
    [InlineData("a.exe")]
    [InlineData("a.command")]
    [InlineData("a.sh")]
    [InlineData("a.lnk")]
    [InlineData("noext")]
    [InlineData("a.txt.")]
    [InlineData("a.bat ")]
    [InlineData("a.csv")]
    [InlineData("a.json")]
    [InlineData("a.docx")]
    public void Anything_else_that_exists_is_revealed_not_started(string name)
    {
        var fs = new FakeFs();
        fs.Files.Add("/p/" + name);

        Assert.Equal(LocalLinkAction.Reveal, Mac(fs, "/p/" + name));
    }

    [Fact]
    public void A_dot_in_a_folder_name_is_not_taken_as_the_extension()
    {
        var fs = new FakeFs();
        fs.Files.Add("/p.txt/run");
        fs.Files.Add(@"C:\p.txt\run");

        Assert.Equal(LocalLinkAction.Reveal, Mac(fs, "/p.txt/run"));
        Assert.Equal(LocalLinkAction.Reveal, Win(fs, @"C:\p.txt\run"));
    }

    // ---- 判定 3：シンボリックリンクを 1 段ずつ ----

    [Fact]
    public void A_txt_link_to_a_command_file_is_revealed()
    {
        var fs = new FakeFs();
        fs.Files.Add("/p/run.command");
        fs.Links["/p/a.txt"] = "/p/run.command";

        Assert.Equal(LocalLinkAction.Reveal, Mac(fs, "/p/a.txt"));
    }

    [Fact]
    public void A_txt_link_to_a_txt_file_is_opened()
    {
        var fs = new FakeFs();
        fs.Files.Add("/p/b.txt");
        fs.Links["/p/a.txt"] = "/p/b.txt";

        Assert.Equal(LocalLinkAction.OpenWithDefaultApp, Mac(fs, "/p/a.txt"));
    }

    [Fact]
    public void Two_step_links_use_the_last_target_for_the_kind()
    {
        var fs = new FakeFs();
        fs.Files.Add("/p/run.command");
        fs.Links["/p/a.txt"] = "/p/b.txt";
        fs.Links["/p/b.txt"] = "/p/run.command";

        Assert.Equal(LocalLinkAction.Reveal, Mac(fs, "/p/a.txt"));
    }

    [Fact]
    public void A_link_to_a_network_path_is_ignored_without_touching_the_file_system()
    {
        var fs = new FakeFs();
        fs.Links["/p/a.txt"] = "//server/x.txt";
        fs.Links[@"C:\p\a.txt"] = @"\\server\x.txt";

        Assert.Equal(LocalLinkAction.Ignore, Mac(fs, "/p/a.txt"));
        Assert.Equal(LocalLinkAction.Ignore, Win(fs, @"C:\p\a.txt"));
        Assert.Empty(fs.Touched);
    }

    [Fact]
    public void A_second_step_to_a_network_path_is_ignored_without_touching_the_file_system()
    {
        var fs = new FakeFs();
        fs.Links[@"C:\p\a.txt"] = @"C:\p\b.txt";
        fs.Links[@"C:\p\b.txt"] = @"\\server\x.txt";

        Assert.Equal(LocalLinkAction.Ignore, Win(fs, @"C:\p\a.txt"));
        Assert.Empty(fs.Touched);
    }

    [Fact]
    public void A_link_chain_of_eight_is_followed_and_nine_or_a_loop_is_revealed()
    {
        var fs = new FakeFs();
        for (var i = 1; i <= 8; i++)
            fs.Links[$"/p/l{i}.txt"] = i == 8 ? "/p/end.txt" : $"/p/l{i + 1}.txt";
        fs.Files.Add("/p/end.txt");
        Assert.Equal(LocalLinkAction.OpenWithDefaultApp, Mac(fs, "/p/l1.txt"));   // リンクが 8 つ

        fs.Links["/p/l8.txt"] = "/p/l9.txt";
        fs.Links["/p/l9.txt"] = "/p/end.txt";
        Assert.Equal(LocalLinkAction.Reveal, Mac(fs, "/p/l1.txt"));               // リンクが 9 つ

        var loop = new FakeFs();
        loop.Links["/p/a.txt"] = "/p/b.txt";
        loop.Links["/p/b.txt"] = "/p/a.txt";
        Assert.Equal(LocalLinkAction.Reveal, Mac(loop, "/p/a.txt"));
    }

    [Fact]
    public void An_exception_from_the_probe_falls_to_the_side_that_does_not_start_anything()
    {
        var probe = new LinkProbe(_ => throw new IOException("x"), _ => false, _ => null);

        Assert.Equal(LocalLinkAction.Reveal, LocalLinkRule.Decide("/p/a.txt", false, probe));
    }

    // ---- 本物のファイルシステム ----

    [MacFact]
    public void Real_symlinks_on_this_mac_are_judged_by_name_and_by_target()
    {
        var dir = Path.Combine(Path.GetTempPath(), "miharikun-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var command = Path.Combine(dir, "run.command");
            var text = Path.Combine(dir, "b.txt");
            File.WriteAllText(command, "echo hi");
            File.WriteAllText(text, "x");
            File.CreateSymbolicLink(Path.Combine(dir, "a.txt"), command);
            File.CreateSymbolicLink(Path.Combine(dir, "c.txt"), text);
            File.CreateSymbolicLink(Path.Combine(dir, "d.txt"), "c.txt");        // 相対のリンク
            File.CreateSymbolicLink(Path.Combine(dir, "e.txt"), "a.txt");        // 2 段
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.CreateSymbolicLink(Path.Combine(dir, "f.txt"), "sub");

            LocalLinkAction Real(string name) => LocalLinkRule.Decide(Path.Combine(dir, name), false, LinkProbe.Real);

            Assert.Equal(LocalLinkAction.Reveal, Real("a.txt"));
            Assert.Equal(LocalLinkAction.OpenWithDefaultApp, Real("c.txt"));
            Assert.Equal(LocalLinkAction.OpenWithDefaultApp, Real("d.txt"));
            Assert.Equal(LocalLinkAction.Reveal, Real("e.txt"));
            Assert.Equal(LocalLinkAction.Reveal, Real("f.txt"));
            Assert.Equal(LocalLinkAction.Reveal, Real("sub"));
            Assert.Equal(LocalLinkAction.OpenWithDefaultApp, Real("b.txt"));
            Assert.Equal(LocalLinkAction.Reveal, Real("run.command"));
            Assert.Equal(LocalLinkAction.Ignore, Real("ない.txt"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
