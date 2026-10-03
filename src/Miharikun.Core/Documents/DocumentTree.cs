namespace Miharikun.Core.Documents;

/// <summary>ツリーの 1 フォルダ。件数は子孫を含む（DirectCount は直下だけ）。</summary>
public sealed class FolderNode
{
    private readonly List<FolderNode> _children = [];

    internal FolderNode(string name, string relativePath)
    {
        Name = name;
        RelativePath = relativePath;
    }

    public string Name { get; }

    /// <summary>対象フォルダからの相対パス（区切りは「/」）。ルートは空文字。</summary>
    public string RelativePath { get; }

    public int TotalCount { get; internal set; }

    public int DirectCount { get; internal set; }

    public IReadOnlyList<FolderNode> Children => _children;

    internal void Add(FolderNode child) => _children.Add(child);

    internal void SortChildren()
    {
        _children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var child in _children)
            child.SortChildren();
    }

    /// <summary>相対パス（大文字小文字無視）のフォルダを探す。空文字はこのノード自身。</summary>
    public FolderNode? Find(string relativePath)
    {
        if (relativePath.Length == 0)
            return this;
        var node = this;
        foreach (var part in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node._children.FirstOrDefault(c => c.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (node is null)
                return null;
        }
        return node;
    }
}

/// <summary>ファイルの一覧からフォルダの階層と件数を作る。md/html を 1 件も含まないフォルダは出ない。</summary>
public static class DocumentTree
{
    public static FolderNode Build(IEnumerable<DocumentEntry> entries)
    {
        var root = new FolderNode("", "");
        var nodes = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase) { [""] = root };

        foreach (var entry in entries)
        {
            var node = root;
            node.TotalCount++;
            var path = "";
            var parts = entry.Folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                path = path.Length == 0 ? part : path + "/" + part;
                if (!nodes.TryGetValue(path, out var child))
                {
                    child = new FolderNode(part, path);
                    nodes[path] = child;
                    node.Add(child);
                }
                node = child;
                node.TotalCount++;
            }
            node.DirectCount++;
        }

        root.SortChildren();
        return root;
    }
}
