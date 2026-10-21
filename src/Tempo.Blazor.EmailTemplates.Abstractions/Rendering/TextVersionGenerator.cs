using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Produces the plain-text alternative of a document: HTML stripped, links rendered as
/// <c>text (url)</c>, sections separated by blank lines, tables row-by-row.
/// </summary>
public sealed partial class TextVersionGenerator
{
    /// <summary>Generates the plain-text version of the document.</summary>
    public string Generate(EmailTemplateDocument document)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(document.Preheader))
            sb.AppendLine(document.Preheader).AppendLine();

        for (int i = 0; i < document.Sections.Count; i++)
        {
            if (i > 0) sb.AppendLine();
            foreach (var column in document.Sections[i].Columns)
                foreach (var block in column.Blocks)
                    WriteBlock(sb, block);
        }

        return Collapse(sb.ToString());
    }

    private static void WriteBlock(StringBuilder sb, EmailBlockBase block)
    {
        switch (block)
        {
            case EmailTextBlock t:
                sb.AppendLine(StripHtml(t.Content));
                break;
            case EmailButtonBlock b:
                sb.AppendLine(Linked(b.Text, b.Href));
                break;
            case EmailImageBlock i when !string.IsNullOrEmpty(i.Alt):
                sb.AppendLine(i.Alt);
                break;
            case EmailDividerBlock:
                sb.AppendLine("----------------------------------------");
                break;
            case EmailSpacerBlock:
                sb.AppendLine();
                break;
            case EmailRawBlock r:
                sb.AppendLine(StripHtml(r.Content));
                break;
            case EmailTableBlock table:
                WriteTable(sb, table);
                break;
            case EmailSocialBlock social:
                WriteSocial(sb, social);
                break;
            case EmailNavbarBlock navbar:
                WriteNavbar(sb, navbar);
                break;
            case EmailCarouselBlock carousel:
                WriteCarousel(sb, carousel);
                break;
            case EmailAccordionBlock accordion:
                WriteAccordion(sb, accordion);
                break;
            case EmailHeroBlock hero:
                WriteBlocks(sb, hero.Blocks);
                break;
            case EmailGroupBlock group:
                WriteColumns(sb, group.Columns);
                break;
            case EmailWrapperBlock wrapper:
                WriteSections(sb, wrapper.Sections);
                break;
        }
    }

    private static void WriteTable(StringBuilder sb, EmailTableBlock table)
    {
        foreach (var row in table.Rows)
            sb.AppendLine(string.Join("\t", row.Cells.Select(CellText)));
    }

    /// <summary>
    /// One cell of the plain-text table: a linked cell reads <c>text (url)</c> like a linked button, so the
    /// link is not lost in the text alternative (Fáze 9).
    /// </summary>
    private static string CellText(EmailTableCell cell) => Linked(cell.Text, cell.Href);

    /// <summary>
    /// A label with its target in parentheses. A target outside the URL allow-list is NOT printed: the
    /// markup replaces such a link with <c>#</c> (§3.7a), so the text alternative must not advertise a
    /// destination the message itself refuses (review F9).
    /// </summary>
    private static string Linked(string? text, string? href)
        => string.IsNullOrEmpty(href) || !EmailUrlPolicy.IsAllowed(href)
            ? text ?? string.Empty
            : $"{text} ({href})";

    private static void WriteSocial(StringBuilder sb, EmailSocialBlock social)
    {
        foreach (var e in social.Elements)
            sb.AppendLine(Linked(e.Label ?? e.Name ?? "", e.Href));
    }

    private static void WriteNavbar(StringBuilder sb, EmailNavbarBlock navbar)
    {
        foreach (var link in navbar.Links)
            sb.AppendLine(Linked(link.Text, link.Href));
    }

    private static void WriteCarousel(StringBuilder sb, EmailCarouselBlock carousel)
    {
        foreach (var img in carousel.Images.Where(im => !string.IsNullOrEmpty(im.Alt)))
            sb.AppendLine(img.Alt);
    }

    private static void WriteAccordion(StringBuilder sb, EmailAccordionBlock accordion)
    {
        foreach (var item in accordion.Items)
        {
            sb.AppendLine(item.Title);
            sb.AppendLine(StripHtml(item.Content));
        }
    }

    private static void WriteSections(StringBuilder sb, IEnumerable<EmailSection> sections)
    {
        foreach (var section in sections)
            WriteColumns(sb, section.Columns);
    }

    private static void WriteColumns(StringBuilder sb, IEnumerable<EmailColumn> columns)
    {
        foreach (var column in columns)
            WriteBlocks(sb, column.Blocks);
    }

    private static void WriteBlocks(StringBuilder sb, IEnumerable<EmailBlockBase> blocks)
    {
        foreach (var block in blocks)
            WriteBlock(sb, block);
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var withoutTags = TagRegex.Replace(html, " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string Collapse(string text)
    {
        // Collapse 3+ newlines to a single blank line and trim trailing spaces on each line.
        var normalized = SpacesRegex.Replace(text, " ");
        normalized = BlankLinesRegex.Replace(normalized, "\n\n");
        return normalized.Trim();
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.None, 1000)]
    private static partial Regex TagRegex { get; }

    [GeneratedRegex("[ \\t]+", RegexOptions.None, 1000)]
    private static partial Regex SpacesRegex { get; }

    [GeneratedRegex("\\n{3,}", RegexOptions.None, 1000)]
    private static partial Regex BlankLinesRegex { get; }
}
