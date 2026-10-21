using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Fáze 9 — the two new <see cref="EmailTableCell"/> fields (<c>Href</c>, <c>BackgroundColor</c>) and the
/// security rules they inherit from §3.7a: the URL allow-list and the colour allow-list. The values reach
/// the markup as an <c>&lt;a href&gt;</c> and a <c>bgcolor</c> attribute, so every test here is an injection
/// test: a blocked scheme becomes <c>#</c>, an invalid colour is never written anywhere, and neither can
/// escape its attribute.
/// </summary>
public class TableCellsSecurityTests
{
    private static readonly MjmlGenerator Generator = new();

    private static EmailTableBlock Table(params EmailTableRow[] rows)
    {
        var table = new EmailTableBlock();
        foreach (var row in rows) table.Rows.Add(row);
        return table;
    }

    private static EmailTableRow Row(params EmailTableCell[] cells)
    {
        var row = new EmailTableRow();
        foreach (var cell in cells) row.Cells.Add(cell);
        return row;
    }

    private static EmailTemplateDocument DocWith(EmailBlockBase block)
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(block);
        section.Columns.Add(column);
        doc.Sections.Add(section);
        return doc;
    }

    private static string Generate(EmailTableBlock table, ICollection<RenderError>? diagnostics = null)
        => Generator.Generate(DocWith(table), MjmlGeneratorOptions.Default, diagnostics);

    // ── Link ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cell_Link_IsRenderedAsAnchorAroundTheText()
    {
        var mjml = Generate(Table(Row(new EmailTableCell { Text = "Docs", Href = "https://example.com/docs" })));

        mjml.Should().Contain("<a href=\"https://example.com/docs\">Docs</a>");
    }

    [Fact]
    public void Cell_Link_IsAttributeEscaped_SoItCannotLeaveTheAttribute()
    {
        var mjml = Generate(Table(Row(new EmailTableCell
        {
            Text = "Docs",
            Href = "https://example.com/?a=1&b=\"onmouseover=alert(1)",
        })));

        // The quote stayed inside the value: no attribute was opened, so no event-handler attribute exists.
        mjml.Should().Contain("&quot;onmouseover=alert(1)");
        mjml.Should().NotContain(" onmouseover=");
        mjml.Should().NotContain("onmouseover=\"");
    }

    [Fact]
    public void Cell_Text_IsEscaped_SoItCannotOpenMarkup()
    {
        var mjml = Generate(Table(Row(new EmailTableCell { Text = "<script>alert(1)</script>" })));

        mjml.Should().NotContain("<script>");
        mjml.Should().Contain("&lt;script&gt;");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("&#106;avascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("blob:https://example.com/1")]
    [InlineData("//evil.example.com/x")]
    [InlineData("\\\\evil.example.com\\share")]
    public void Cell_Link_WithABlockedScheme_BecomesHashInTheMarkup(string href)
    {
        var mjml = Generate(Table(Row(new EmailTableCell { Text = "Docs", Href = href })));

        mjml.Should().Contain("href=\"#\"");
        mjml.Should().NotContain("javascript:");
        mjml.Should().NotContain("vbscript:");
        mjml.Should().NotContain("evil.example.com");
    }

    [Theory]
    [InlineData("https://example.com/x")]
    [InlineData("http://example.com/x")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("tel:+420123456789")]
    [InlineData("/relative/path")]
    [InlineData("./relative")]
    [InlineData("#fragment")]
    public void Cell_Link_OnTheAllowList_IsKeptVerbatim(string href)
    {
        var mjml = Generate(Table(Row(new EmailTableCell { Text = "Docs", Href = href })));

        mjml.Should().Contain($"href=\"{href}\"");
    }

    [Fact]
    public void Cell_WithoutHref_RendersPlainText()
    {
        var mjml = Generate(Table(Row(new EmailTableCell { Text = "Docs" })));

        mjml.Should().NotContain("<a ");
        mjml.Should().Contain("<td>Docs</td>");
    }

    // ── Background colour ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("#fff")]
    [InlineData("#ffffff")]
    [InlineData("#AbC123")]
    [InlineData("white")]
    [InlineData("lightgray")]
    public void Cell_ValidBackgroundColor_IsEmittedAsBgcolor(string color)
    {
        var diagnostics = new List<RenderError>();

        var mjml = Generate(Table(Row(new EmailTableCell { Text = "x", BackgroundColor = color })), diagnostics);

        mjml.Should().Contain($"bgcolor=\"{color}\"");
        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("expression(alert(1))")]
    [InlineData("url(https://evil.example.com/x)")]
    [InlineData("red;background-image:url(https://evil.example.com/x)")]
    [InlineData("#fff\" onmouseover=\"alert(1)")]
    [InlineData("#fff\" style=\"background-image:url(x)")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("var(--tm-color-primary)")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not-a-color")]
    public void Cell_InvalidBackgroundColor_IsNeverWrittenAndIsReported(string color)
    {
        var diagnostics = new List<RenderError>();

        var mjml = Generate(Table(Row(new EmailTableCell { Text = "x", BackgroundColor = color })), diagnostics);

        mjml.Should().NotContain("bgcolor=");
        mjml.Should().NotContain("style=");
        mjml.Should().NotContain("onmouseover");
        mjml.Should().NotContain("expression(");
        mjml.Should().NotContain("url(");

        diagnostics.Should().ContainSingle();
        diagnostics[0].Key.Should().Be(RenderKeys.InvalidTableCellColor);
        diagnostics[0].Severity.Should().Be(RenderErrorSeverity.Warning);
        // The warning names the cell, never the rejected value (a crafted value is not echoed).
        diagnostics[0].Message.Should().NotContain("expression(");
    }

    [Fact]
    public void Cell_WithoutBackgroundColor_EmitsNoBgcolor()
    {
        var mjml = Generate(Table(Row(new EmailTableCell { Text = "x" })));

        mjml.Should().NotContain("bgcolor=");
    }

    // ── Both new fields together, edge cases ─────────────────────────────────────────────────

    [Fact]
    public void Cell_WithLinkAndBackground_KeepsTheOtherCellAttributes()
    {
        var mjml = Generate(Table(Row(new EmailTableCell
        {
            Text = "Docs",
            Href = "https://example.com",
            BackgroundColor = "#eee",
            Align = "right",
            ColSpan = 2,
            RowSpan = 3,
        })));

        mjml.Should().Contain("align=\"right\"");
        mjml.Should().Contain("colspan=\"2\"");
        mjml.Should().Contain("rowspan=\"3\"");
        mjml.Should().Contain("bgcolor=\"#eee\"");
        mjml.Should().Contain("<a href=\"https://example.com\">Docs</a>");
    }

    [Fact]
    public void EmptyTable_GeneratesAnEmptyMjTable()
    {
        var mjml = Generate(Table());

        mjml.Should().Contain("<mj-table");
        mjml.Should().Contain("</mj-table>");
        mjml.Should().NotContain("<tr>");
    }

    [Fact]
    public void SixColumnTable_GeneratesSixCells()
    {
        var cells = Enumerable.Range(1, 6).Select(i => new EmailTableCell { Text = $"c{i}" }).ToArray();

        var mjml = Generate(Table(Row(cells)));

        mjml.Split("<td").Length.Should().Be(7); // 6 cells + the leading chunk
        mjml.Should().Contain("c6");
    }

    // ── Import round-trip ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Importer_ReadsBackTheLinkAndTheBackground()
    {
        var mjml = Generate(Table(Row(new EmailTableCell
        {
            Text = "Docs",
            Href = "https://example.com/docs",
            BackgroundColor = "#eee",
        })));

        var result = new MjmlImporter().Import(mjml);

        var table = result.Document.Sections[0].Columns[0].Blocks.OfType<EmailTableBlock>().Single();
        var cell = table.Rows[0].Cells[0];
        cell.Text.Should().Be("Docs");
        cell.Href.Should().Be("https://example.com/docs");
        cell.BackgroundColor.Should().Be("#eee");
    }

    [Fact]
    public void Importer_ReadsAnAnchorAndBgcolorWrittenByHand()
    {
        const string mjml = """
            <mjml><mj-body><mj-section><mj-column><mj-table>
            <tr><td bgcolor="#ff0000"><a href="mailto:sales@example.com">Sales</a></td></tr>
            </mj-table></mj-column></mj-section></mj-body></mjml>
            """;

        var result = new MjmlImporter().Import(mjml);

        var cell = result.Document.Sections[0].Columns[0].Blocks.OfType<EmailTableBlock>().Single()
            .Rows[0].Cells[0];
        cell.Text.Should().Be("Sales");
        cell.Href.Should().Be("mailto:sales@example.com");
        cell.BackgroundColor.Should().Be("#ff0000");
    }

    [Fact]
    public void Importer_KeepsTheBlockedPlaceholderInsteadOfGuessingTheOriginalScheme()
    {
        var mjml = Generate(Table(Row(new EmailTableCell { Text = "Docs", Href = "javascript:alert(1)" })));

        var result = new MjmlImporter().Import(mjml);

        result.Document.Sections[0].Columns[0].Blocks.OfType<EmailTableBlock>().Single()
            .Rows[0].Cells[0].Href.Should().Be("#");
    }

    // ── Plain-text version ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TextVersion_WritesTheCellTextWithItsUrlInParentheses()
    {
        var doc = DocWith(Table(Row(
            new EmailTableCell { Text = "Docs", Href = "https://example.com/docs" },
            new EmailTableCell { Text = "Plain" })));

        var text = new TextVersionGenerator().Generate(doc);

        text.Should().Contain("Docs (https://example.com/docs)");
        text.Should().Contain("Plain");
    }

    // ── Serialization is additive: no schema bump (§3.1/D18) ────────────────────────────────

    [Fact]
    public void Serializer_RoundTripsTheNewFieldsWithoutASchemaBump()
    {
        var doc = DocWith(Table(Row(new EmailTableCell
        {
            Text = "Docs",
            Href = "https://example.com",
            BackgroundColor = "#eee",
        })));

        var json = EmailTemplateSerializer.Serialize(doc);
        var back = EmailTemplateSerializer.Deserialize(json);

        EmailTemplateSerializer.ReadSchemaVersion(json).Should().Be(1);
        EmailTemplateDocument.CurrentSchemaVersion.Should().Be(1);
        var cell = back.Sections[0].Columns[0].Blocks.OfType<EmailTableBlock>().Single().Rows[0].Cells[0];
        cell.Href.Should().Be("https://example.com");
        cell.BackgroundColor.Should().Be("#eee");
    }

    [Fact]
    public void Deserialize_LoadsAJsonWithoutTheNewFields()
    {
        const string json = """
            {"schemaVersion":1,"sections":[{"columns":[{"blocks":[
            {"$type":"table","rows":[{"isHeader":false,"cells":[{"text":"Old"}]}]}]}]}]}
            """;

        var document = EmailTemplateSerializer.Deserialize(json);

        var cell = document.Sections[0].Columns[0].Blocks.OfType<EmailTableBlock>().Single().Rows[0].Cells[0];
        cell.Text.Should().Be("Old");
        cell.Href.Should().BeNull();
        cell.BackgroundColor.Should().BeNull();
    }
}
