using Miharikun.Core.Documents;

namespace Miharikun.Tests.Core;

public sealed class DocumentIndexTests
{
    private static DocumentEntry E(string rel, int minutesAgo = 0, long size = 1) =>
        new(rel, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo), size);

    [Fact]
    public void Entries_are_keyed_by_relative_path_ignoring_case()
    {
        var index = new DocumentIndex();
        index.AddOrUpdate(E("Docs/A.md", size: 1));
        index.AddOrUpdate(E("docs/a.MD", size: 2));                 // 走査と Watcher から同じファイルが来ても重複しない
        Assert.Equal(1, index.Count);
        Assert.Equal(2, index.TryGet("DOCS/a.md")!.Size);
    }

    [Fact]
    public void Remove_returns_whether_it_existed()
    {
        var index = new DocumentIndex();
        index.AddOrUpdate(E("a.md"));
        Assert.True(index.Remove("A.MD"));
        Assert.False(index.Remove("a.md"));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void HasEntriesUnder_looks_for_files_below_a_folder_ignoring_case()
    {
        var index = new DocumentIndex();
        index.AddOrUpdate(E("Docs/adr/x.md"));
        index.AddOrUpdate(E("docsx/y.md"));
        Assert.True(index.HasEntriesUnder("docs"));
        Assert.True(index.HasEntriesUnder("docs/ADR"));
        Assert.False(index.HasEntriesUnder("doc"));                  // 前方一致だけでは当たらない（区切りまで見る）
        Assert.False(index.HasEntriesUnder("Docs/adr/x.md"));        // ファイル自身は「下」ではない
    }

    [Fact]
    public void Reset_bumps_the_generation_and_clears()
    {
        var index = new DocumentIndex();
        index.AddOrUpdate(E("a.md"));
        var before = index.Generation;
        var next = index.Reset();
        Assert.Equal(before + 1, next);
        Assert.Equal(next, index.Generation);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void Apply_rejects_batches_from_an_older_generation()
    {
        var index = new DocumentIndex();
        var oldGen = index.Reset();
        var newGen = index.Reset();

        Assert.False(index.Apply(new DocumentBatch(oldGen, [E("stale.md")], IsLast: false)));
        Assert.True(index.Apply(new DocumentBatch(newGen, [E("fresh.md")], IsLast: true)));

        Assert.Null(index.TryGet("stale.md"));
        Assert.NotNull(index.TryGet("fresh.md"));
    }
}

public sealed class DocumentTreeTests
{
    private static DocumentEntry E(string rel) => new(rel, DateTime.UtcNow, 1);

    [Fact]
    public void Counts_include_descendants_and_the_root_is_the_total()
    {
        var root = DocumentTree.Build([
            E("README.md"), E("docs/a.md"), E("docs/b.md"), E("docs/adr/x.md"), E("docs/specs/y.md"), E("src/z.html"),
        ]);

        Assert.Equal(6, root.TotalCount);
        Assert.Equal(1, root.DirectCount);
        var docs = root.Children.Single(c => c.Name == "docs");
        Assert.Equal(4, docs.TotalCount);
        Assert.Equal(2, docs.DirectCount);
        Assert.Equal("docs", docs.RelativePath);
        Assert.Equal(["adr", "specs"], docs.Children.Select(c => c.Name));
        Assert.Equal("docs/adr", docs.Children[0].RelativePath);
    }

    [Fact]
    public void Folders_without_documents_do_not_appear()
    {
        var root = DocumentTree.Build([E("a/b/c/deep.md")]);
        var a = Assert.Single(root.Children);
        var b = Assert.Single(a.Children);
        Assert.Equal(1, b.Children.Single().TotalCount);
        Assert.Equal(0, a.DirectCount);                              // 中間のフォルダ自身は直下 0 件でも出る
        Assert.Empty(DocumentTree.Build([]).Children);
    }

    [Fact]
    public void Children_are_sorted_by_name_ignoring_case()
    {
        var root = DocumentTree.Build([E("b/x.md"), E("A/x.md"), E("c/x.md")]);
        Assert.Equal(["A", "b", "c"], root.Children.Select(c => c.Name));
    }

    [Fact]
    public void Find_returns_the_node_for_a_relative_path()
    {
        var root = DocumentTree.Build([E("docs/adr/x.md")]);
        Assert.Equal("adr", root.Find("DOCS/adr")!.Name);
        Assert.Same(root, root.Find(""));
        Assert.Null(root.Find("nope"));
    }
}

public sealed class DocumentFilterTests
{
    private static DocumentEntry E(string rel, int minutesAgo = 0) =>
        new(rel, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo), 1);

    private static readonly DocumentEntry[] Sample =
    [
        E("README.md", 50), E("docs/a.md", 10), E("docs/B.html", 5), E("docs/adr/x.md", 1), E("src/z.md", 30),
    ];

    private static string[] Names(IEnumerable<DocumentEntry> entries) => entries.Select(e => e.RelativePath).ToArray();

    [Fact]
    public void Null_folder_means_all_and_sorts_by_modified_descending() =>
        Assert.Equal(["docs/adr/x.md", "docs/B.html", "docs/a.md", "src/z.md", "README.md"],
            Names(DocumentFilter.Select(Sample, folder: null, query: "")));

    [Fact]
    public void A_folder_shows_its_direct_files_only() =>
        Assert.Equal(["docs/B.html", "docs/a.md"], Names(DocumentFilter.Select(Sample, "docs", "")));

    [Fact]
    public void The_empty_folder_is_the_project_root() =>
        Assert.Equal(["README.md"], Names(DocumentFilter.Select(Sample, "", "")));

    [Fact]
    public void Folder_names_compare_ignoring_case() =>
        Assert.Equal(2, DocumentFilter.Select(Sample, "DOCS", "").Count);

    [Fact]
    public void Query_matches_file_or_folder_names_ignoring_case_and_terms_are_anded()
    {
        Assert.Equal(["docs/adr/x.md"], Names(DocumentFilter.Select(Sample, null, "ADR")));          // フォルダ名
        Assert.Equal(["docs/B.html"], Names(DocumentFilter.Select(Sample, null, "b.html")));        // ファイル名
        Assert.Equal(["docs/a.md"], Names(DocumentFilter.Select(Sample, null, "docs  a.md")));      // 複数語は AND
        Assert.Empty(DocumentFilter.Select(Sample, null, "zzz"));
    }

    [Fact]
    public void Query_and_folder_are_combined()
    {
        Assert.Equal(["docs/a.md"], Names(DocumentFilter.Select(Sample, "docs", ".md")));
    }

    [Fact]
    public void Matches_with_a_blank_query_accepts_everything()
    {
        Assert.True(DocumentFilter.Matches(Sample[0], ""));
        Assert.True(DocumentFilter.Matches(Sample[0], "   "));
    }
}
