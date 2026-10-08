using System.Text;
using System.Text.RegularExpressions;
using Markdig.Renderers.Html;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Miharikun.Docs;

/// <summary>目次の 1 項目。<c>Done</c> は見出しの印（true＝[x]、false＝[ ]、null＝印なし）。タスク数は節の中（下の段の分を含む）。</summary>
public sealed record MarkdownHeading(int Level, string Text, string? Id, bool? Done, int TasksDone, int TasksTotal);

/// <summary>1 回の解析で作った HTML と見出しの一覧。</summary>
public sealed record MarkdownRendering(string Html, IReadOnlyList<MarkdownHeading> Headings);

/// <summary>md の構文木から目次（見出し・印・節のタスク数）を取り出す（要件 12.7.1）。id は Markdig が付けたものをそのまま使う。</summary>
public static partial class MarkdownOutline
{
    [GeneratedRegex(@"^\[([ xX])\](\s+|$)")]
    private static partial Regex MarkPattern();

    private sealed class Builder
    {
        public int Level;
        public string Text = "";
        public string? Id;
        public bool? Done;
        public int TasksDone;
        public int TasksTotal;
    }

    public static IReadOnlyList<MarkdownHeading> Extract(MarkdownDocument doc)
    {
        var all = new List<Builder>();
        var open = new List<Builder>();   // 開いている見出し（浅い順）

        foreach (var node in doc.Descendants())
        {
            switch (node)
            {
                case HeadingBlock heading:
                {
                    while (open.Count > 0 && open[^1].Level >= heading.Level)
                        open.RemoveAt(open.Count - 1);

                    var (text, done) = ReadText(heading);
                    if (text.Length == 0)
                        break;              // 文字が空の見出しは出さない（節の境目にもしない）
                    var b = new Builder
                    {
                        Level = heading.Level,
                        Text = text,
                        Done = done,
                        Id = heading.GetAttributes().Id,
                    };
                    all.Add(b);
                    open.Add(b);
                    break;
                }
                case TaskList task:
                    foreach (var b in open)
                    {
                        b.TasksTotal++;
                        if (task.Checked)
                            b.TasksDone++;
                    }
                    break;
            }
        }

        return all.Select(b => new MarkdownHeading(b.Level, b.Text, b.Id, b.Done, b.TasksDone, b.TasksTotal)).ToList();
    }

    private static (string Text, bool? Done) ReadText(HeadingBlock heading)
    {
        var sb = new StringBuilder();
        if (heading.Inline is { } inline)
            Append(inline, sb);

        var text = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        bool? done = null;
        var m = MarkPattern().Match(text);
        if (m.Success)
        {
            done = m.Groups[1].Value != " ";
            text = text[m.Length..].Trim();
        }
        return (text, done);
    }

    private static void Append(Inline inline, StringBuilder sb)
    {
        switch (inline)
        {
            case LiteralInline lit:
                sb.Append(lit.Content.ToString());
                break;
            case CodeInline code:
                sb.Append(code.Content);
                break;
            case LineBreakInline:
                sb.Append(' ');
                break;
            case HtmlEntityInline entity:
                sb.Append(entity.Transcoded.ToString());
                break;
            case HtmlInline:
                break;
            case AutolinkInline auto:
                sb.Append(auto.Url);
                break;
            case ContainerInline container:
                for (var child = container.FirstChild; child != null; child = child.NextSibling)
                    Append(child, sb);
                break;
        }
    }
}
