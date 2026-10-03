using Miharikun.Core.Documents;

namespace Miharikun.Tests.Core;

public sealed class GitIgnoreMatcherTests
{
    private static bool File(string patterns, string path) =>
        GitIgnoreMatcher.Parse(patterns).IsIgnored(path, isDirectory: false);

    private static bool Dir(string patterns, string path) =>
        GitIgnoreMatcher.Parse(patterns).IsIgnored(path, isDirectory: true);

    [Fact]
    public void Empty_and_comment_lines_match_nothing()
    {
        Assert.False(File("", "a.md"));
        Assert.False(File("# a.md\n\n   \n", "a.md"));
    }

    [Theory]
    [InlineData("*.draft.md", "a.draft.md", true)]
    [InlineData("*.draft.md", "docs/deep/a.draft.md", true)]      // 「/」なし＝任意の深さ
    [InlineData("*.draft.md", "a.md", false)]
    [InlineData("README.md", "docs/README.md", true)]
    [InlineData("README.md", "docs/README.md.bak", false)]
    [InlineData("a?.md", "ab.md", true)]
    [InlineData("a?.md", "abc.md", false)]
    [InlineData("a*c.md", "abbbc.md", true)]
    public void Name_patterns_match_at_any_depth(string pattern, string path, bool expected) =>
        Assert.Equal(expected, File(pattern, path));

    [Theory]
    [InlineData("docs/draft.md", "docs/draft.md", true)]           // 途中の「/」＝ルート基準
    [InlineData("docs/draft.md", "x/docs/draft.md", false)]
    [InlineData("/draft.md", "draft.md", true)]                    // 先頭の「/」＝ルート基準
    [InlineData("/draft.md", "docs/draft.md", false)]
    [InlineData("docs/*.md", "docs/a.md", true)]
    [InlineData("docs/*.md", "docs/sub/a.md", false)]              // 「*」は「/」をまたがない
    public void Slash_anchors_the_pattern_to_the_root(string pattern, string path, bool expected) =>
        Assert.Equal(expected, File(pattern, path));

    [Fact]
    public void Trailing_slash_matches_directories_only()
    {
        Assert.True(Dir("build/", "build"));
        Assert.True(Dir("build/", "src/build"));
        Assert.False(File("build/", "build"));                    // 同名のファイルは対象外
        Assert.False(File("build/", "src/build"));
    }

    [Theory]
    [InlineData("**/archive", "archive", true)]
    [InlineData("**/archive", "a/b/archive", true)]
    [InlineData("a/**/z.md", "a/z.md", true)]                      // 「**」は 0 個以上のフォルダ
    [InlineData("a/**/z.md", "a/b/c/z.md", true)]
    [InlineData("a/**/z.md", "b/a/z.md", false)]
    [InlineData("a/**", "a/x.md", true)]                           // 末尾の「/**」は中身すべて
    [InlineData("a/**", "a/b/x.md", true)]
    public void Double_star(string pattern, string path, bool expected) =>
        Assert.Equal(expected, File(pattern, path));

    [Fact]
    public void Trailing_double_star_does_not_match_the_folder_itself() =>
        Assert.False(Dir("a/**", "a"));

    [Fact]
    public void Matching_is_case_insensitive()
    {
        Assert.True(File("README.MD", "docs/readme.md"));
        Assert.True(Dir("Node_Modules/", "node_modules"));
    }

    [Fact]
    public void Backslash_in_the_path_is_treated_as_a_separator() =>
        Assert.True(File("docs/*.md", @"docs\a.md"));

    [Fact]
    public void Later_lines_win_and_negation_re_includes()
    {
        const string patterns = "*.md\n!keep.md";
        Assert.True(File(patterns, "a.md"));
        Assert.False(File(patterns, "keep.md"));
        Assert.False(File(patterns, "docs/keep.md"));
    }

    [Fact]
    public void A_negation_before_the_pattern_has_no_effect()
    {
        const string patterns = "!keep.md\n*.md";
        Assert.True(File(patterns, "keep.md"));
    }

    [Fact]
    public void Escaped_hash_and_bang_are_literal()
    {
        Assert.True(File(@"\#note.md", "#note.md"));
        Assert.True(File(@"\!note.md", "!note.md"));
    }

    [Fact]
    public void Line_endings_and_trailing_spaces_are_tolerated()
    {
        var m = GitIgnoreMatcher.Parse("a.md  \r\nb.md\r\n");
        Assert.True(m.IsIgnored("a.md", false));
        Assert.True(m.IsIgnored("b.md", false));
    }

    // 除外したフォルダの中身は、git と同じく「!」で戻せない（祖先が除外されたら終わり）。
    [Fact]
    public void IsPathExcluded_checks_every_ancestor()
    {
        var m = GitIgnoreMatcher.Parse("node_modules/\ndocs/archive/");
        Assert.True(m.IsPathExcluded("node_modules/pkg/README.md", isDirectory: false));
        Assert.True(m.IsPathExcluded("a/node_modules/x/y.md", isDirectory: false));
        Assert.True(m.IsPathExcluded("docs/archive/old.md", isDirectory: false));
        Assert.True(m.IsPathExcluded("docs/archive", isDirectory: true));
        Assert.False(m.IsPathExcluded("docs/guide.md", isDirectory: false));
        Assert.False(m.IsPathExcluded("docs", isDirectory: true));
    }

    [Fact]
    public void IsPathExcluded_cannot_re_include_inside_an_excluded_folder()
    {
        var m = GitIgnoreMatcher.Parse("docs/archive/\n!docs/archive/keep.md");
        Assert.True(m.IsPathExcluded("docs/archive/keep.md", isDirectory: false));
    }

    [Fact]
    public void IsPathExcluded_treats_a_file_with_a_folder_only_pattern_name_as_not_excluded()
    {
        var m = GitIgnoreMatcher.Parse("build/");
        Assert.False(m.IsPathExcluded("src/build", isDirectory: false));   // ファイルの build
        Assert.True(m.IsPathExcluded("src/build/x.md", isDirectory: false));
    }

    [Fact]
    public void Ignored_files_via_IsPathExcluded_agree_with_the_pruning_walk()
    {
        // 走査（フォルダに入る前に判定する枝刈り）と、Watcher の単発判定が同じ結果になること。
        var m = GitIgnoreMatcher.Parse(DefaultIgnore.Text + "\ndocs/archive/\n*.draft.md\n!keep.draft.md");
        string[] files =
        [
            "README.md", "docs/a.md", "docs/archive/b.md", "docs/c.draft.md", "docs/keep.draft.md",
            "node_modules/x/y.md", "src/bin/z.html", "sub/obj/q.md", ".git/info/x.md", ".cursor/rules/r.md",
        ];
        foreach (var f in files)
            Assert.Equal(WalkExcluded(m, f), m.IsPathExcluded(f, isDirectory: false));
    }

    private static bool WalkExcluded(GitIgnoreMatcher m, string file)
    {
        var parts = file.Split('/');
        var current = "";
        for (var i = 0; i < parts.Length - 1; i++)
        {
            current = current.Length == 0 ? parts[i] : current + "/" + parts[i];
            if (m.IsIgnored(current, isDirectory: true))
                return true;                                          // 枝刈り：中に入らない
        }
        return m.IsIgnored(file, isDirectory: false);
    }

    [Fact]
    public void Default_ignore_lists_the_agreed_folders_and_keeps_dot_cursor()
    {
        var m = GitIgnoreMatcher.Parse(DefaultIgnore.Text);
        foreach (var name in new[]
                 {
                     ".git", "node_modules", "bin", "obj", ".vs", ".idea", ".venv", "venv", "__pycache__",
                     "dist", "build", "out", "target", "packages", ".gradle", ".next", "coverage",
                 })
            Assert.True(m.IsPathExcluded($"x/{name}/a.md", isDirectory: false), name);

        Assert.False(m.IsPathExcluded(".cursor/rules/a.md", isDirectory: false));
        Assert.False(m.IsPathExcluded("docs/a.md", isDirectory: false));
    }
}
