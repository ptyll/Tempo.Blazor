using System.Globalization;
using System.Text;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Tempo.Blazor.AI.Markdown;

/// <summary>
/// The plain-text projection of an assistant answer — what the message's Copy action and its
/// read-aloud action send to the outside world. The markdown markers are dropped while the block
/// structure survives (one line per paragraph, list item, code line and table row, blank lines between
/// blocks), so a copied answer reads like the answer instead of like its source.
/// </summary>
/// <remarks>
/// <para>
/// The walk runs over the SAME Markdig pipeline as <see cref="AiMarkdownRenderer"/>, so the extension set
/// and the security posture are shared: raw HTML is disabled, therefore <c>&lt;script&gt;</c> arrives as
/// literal text and stays literal text here too.
/// </para>
/// <para>
/// Link and image targets are never emitted, only their label (or alt text): a recipient of the copied
/// text gets readable prose, not a list of URLs — and a refused/citation URL cannot travel as text either.
/// </para>
/// </remarks>
public static class AiMessageText
{
    /// <summary>
    /// Projects untrusted markdown to readable plain text.
    /// </summary>
    /// <param name="markdown">The markdown to flatten; <see langword="null"/> or whitespace projects to an empty string.</param>
    /// <returns>The plain text, with <c>\n</c> line endings and no leading/trailing blank lines.</returns>
    public static string ToPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var document = Markdig.Markdown.Parse(markdown, AiMarkdownRenderer.Pipeline);

        var blocks = new List<string>();
        Collect(document, blocks, indent: 0);

        return Normalize(string.Join("\n\n", blocks.Where(block => !string.IsNullOrWhiteSpace(block))));
    }

    /// <summary>Walks one block, appending each rendering block as its own text entry.</summary>
    private static void Collect(Block block, List<string> blocks, int indent)
    {
        switch (block)
        {
            case ListBlock list:
                var lines = new List<string>();
                WriteList(list, lines, indent);
                blocks.Add(string.Join("\n", lines));
                break;

            case FencedCodeBlock fenced:
                blocks.Add(string.Join("\n", CodeLines(fenced.Lines)));
                break;

            case CodeBlock code:
                blocks.Add(string.Join("\n", CodeLines(code.Lines)));
                break;

            case Table table:
                WriteTable(table, blocks);
                break;

            case LeafBlock leaf:
                var text = InlineText(leaf.Inline);
                if (text.Length > 0)
                {
                    blocks.Add(new string(' ', indent) + text);
                }

                break;

            case ContainerBlock container:
                foreach (var child in container)
                {
                    Collect(child, blocks, indent);
                }

                break;
        }
    }

    /// <summary>Writes a list: one line per item, a plain bullet or the item's number, nested items indented.</summary>
    private static void WriteList(ListBlock list, List<string> lines, int indent)
    {
        var number = 1;
        if (list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start))
        {
            number = start;
        }

        var pad = new string(' ', indent);
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = list.IsOrdered ? $"{number++}. " : "\u2022 ";
            string? first = null;
            var nested = new List<string>();

            foreach (var child in item)
            {
                switch (child)
                {
                    case ListBlock sublist:
                        var inner = new List<string>();
                        WriteList(sublist, inner, indent + 2);
                        nested.AddRange(inner);
                        break;

                    case LeafBlock leaf:
                        var text = InlineText(leaf.Inline);
                        if (first is null)
                        {
                            first = text;
                        }
                        else if (text.Length > 0)
                        {
                            nested.Add(new string(' ', indent + 2) + text);
                        }

                        break;

                    default:
                        var blocks = new List<string>();
                        Collect(child, blocks, indent + 2);
                        nested.AddRange(blocks.SelectMany(block => block.Split('\n')));
                        break;
                }
            }

            lines.Add(pad + marker + (first ?? string.Empty));
            lines.AddRange(nested);
        }
    }

    /// <summary>Writes a table as one line per row with the cells separated by a pipe, without the agreement row.</summary>
    private static void WriteTable(Table table, List<string> blocks)
    {
        var rows = new List<string>();
        foreach (var row in table.OfType<TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.OfType<TableCell>())
            {
                var text = string.Join(
                    " ",
                    cell.OfType<LeafBlock>().Select(leaf => InlineText(leaf.Inline)).Where(value => value.Length > 0));
                cells.Add(text);
            }

            rows.Add(string.Join(" | ", cells));
        }

        if (rows.Count > 0)
        {
            blocks.Add(string.Join("\n", rows));
        }
    }

    /// <summary>The source lines of a code block, verbatim.</summary>
    private static IEnumerable<string> CodeLines(StringLineGroup group)
    {
        var lines = new List<string>(group.Count);
        for (var index = 0; index < group.Count; index++)
        {
            lines.Add(group.Lines[index].Slice.ToString());
        }

        return lines;
    }

    /// <summary>Flattens one inline container to text: literals and code keep their content, markup disappears.</summary>
    private static string InlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        WriteInlines(container, builder);
        return builder.ToString().Trim();
    }

    private static void WriteInlines(ContainerInline container, StringBuilder builder)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;

                case CodeInline code:
                    builder.Append(code.Content);
                    break;

                case LineBreakInline lineBreak:
                    // A HARD break is the author's line break; a soft break reflows into a space, exactly
                    // as the rendered paragraph does.
                    builder.Append(lineBreak.IsHard ? "\n" : " ");
                    break;

                case AutolinkInline autolink:
                    builder.Append(autolink.Url);
                    break;

                case LinkInline link:
                    // Label only (an image contributes its alt text): targets never travel as plain text.
                    WriteInlines(link, builder);
                    break;

                case EmphasisInline emphasis:
                    WriteInlines(emphasis, builder);
                    break;

                case ContainerInline nested:
                    WriteInlines(nested, builder);
                    break;
            }
        }
    }

    /// <summary>Normalizes line endings, trims trailing spaces and collapses runs of blank lines to one.</summary>
    private static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var output = new List<string>(lines.Length);
        var blank = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
            {
                if (!blank && output.Count > 0)
                {
                    output.Add(string.Empty);
                }

                blank = true;
                continue;
            }

            blank = false;
            output.Add(trimmed);
        }

        while (output.Count > 0 && output[^1].Length == 0)
        {
            output.RemoveAt(output.Count - 1);
        }

        return string.Join("\n", output).Trim();
    }
}
