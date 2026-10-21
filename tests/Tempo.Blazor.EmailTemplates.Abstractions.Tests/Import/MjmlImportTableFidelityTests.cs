using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Import;

/// <summary>
/// Fáze 9 review — the fidelity and the diagnostics of the table-cell import. Two rules are guarded here:
/// <list type="bullet">
/// <item>a cell's <c>&lt;a href&gt;</c> is read back as the cell link only when the anchor is the WHOLE
/// content of the cell; a cell with mixed content (<c>&lt;a&gt;Docs&lt;/a&gt; since 2024</c>, or an anchor
/// next to markup) keeps its full text and no link, so the import never silently drops the rest of the
/// cell;</item>
/// <item>a value the pipeline cannot emit (a blocked URL scheme, a colour outside
/// <see cref="EmailColorPolicy"/>) is reported as an <see cref="ImportMessage"/> warning instead of
/// vanishing without a trace.</item>
/// </list>
/// </summary>
public class MjmlImportTableFidelityTests
{
    private static EmailTableBlock Table(int rows, int columns)
    {
        var table = new EmailTableBlock();
        for (var r = 0; r < rows; r++)
        {
            var row = new EmailTableRow();
            for (var c = 0; c < columns; c++) row.Cells.Add(new EmailTableCell { Text = $"r{r}c{c}" });
            table.Rows.Add(row);
        }

        return table;
    }

    private static EmailTableBlock ImportFirstTable(string mjml, out ImportResult result)
    {
        result = new MjmlImporter().Import(mjml);
        result.Document.Should().NotBeNull();
        return result.Document!.Sections[0].Columns[0].Blocks.OfType<EmailTableBlock>().Single();
    }

    private static string MjmlTable(string rows)
        => $"""
            <mjml><mj-body><mj-section><mj-column><mj-table>
            {rows}
            </mj-table></mj-column></mj-section></mj-body></mjml>
            """;

    // ── Mixed cell content ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MixedContent_KeepsTheWholeTextAndNoLink()
    {
        var table = ImportFirstTable(
            MjmlTable("""<tr><td><a href="https://example.com/">Docs</a> since 2024</td></tr>"""),
            out _);

        var cell = table.Rows[0].Cells[0];
        cell.Text.Should().Contain("Docs");
        cell.Text.Should().Contain("since 2024",
            "the text after the anchor is part of the cell and must survive the import");
        cell.Href.Should().BeNull("the anchor is not the whole cell content, so it is not the cell link");
    }

    [Fact]
    public void AnchorNextToMarkup_KeepsTheWholeTextAndNoLink()
    {
        var table = ImportFirstTable(
            MjmlTable("""<tr><td><strong>New</strong> <a href="https://example.com/">Docs</a></td></tr>"""),
            out _);

        var cell = table.Rows[0].Cells[0];
        cell.Text.Should().Contain("New");
        cell.Text.Should().Contain("Docs");
        cell.Href.Should().BeNull();
    }

    [Fact]
    public void AnchorAsTheOnlyContent_IsStillTheCellLink()
    {
        var table = ImportFirstTable(
            MjmlTable("""<tr><td><a href="https://example.com/docs">Docs</a></td></tr>"""),
            out _);

        var cell = table.Rows[0].Cells[0];
        cell.Text.Should().Be("Docs");
        cell.Href.Should().Be("https://example.com/docs");
    }

    [Fact]
    public void RoundTrip_KeepsEveryCellOfAMixedTable()
    {
        var table = Table(2, 2);
        table.Rows[0].Cells[0].Href = "https://example.com/a";
        table.Rows[0].Cells[0].BackgroundColor = "#eee";
        table.Rows[1].Cells[1].Text = string.Empty;

        var mjml = new MjmlGenerator().Generate(Document(table));
        var result = new MjmlImporter().Import(mjml);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty("the generated document only carries valid values");
        var back = result.Document!.Sections[0].Columns[0].Blocks.OfType<EmailTableBlock>().Single();
        back.Rows.Count.Should().Be(2);
        back.Rows[0].Cells[0].Text.Should().Be("r0c0");
        back.Rows[0].Cells[0].Href.Should().Be("https://example.com/a");
        back.Rows[0].Cells[0].BackgroundColor.Should().Be("#eee");
        back.Rows[1].Cells[1].Text.Should().BeEmpty();
        back.Rows[1].Cells[1].Href.Should().BeNull();
    }

    // ── Diagnostics: blocked link ───────────────────────────────────────────────────────────

    [Fact]
    public void BlockedLink_IsReportedAsAWarningAndKeptSoTheEditorCanFixIt()
    {
        var table = ImportFirstTable(
            MjmlTable("""<tr><td><a href="javascript:alert(1)">Docs</a></td></tr>"""),
            out var result);

        var warning = result.Warnings.Should().ContainSingle(w => w.Key == ImportKeys.BlockedTableLink).Subject;
        warning.Detail.Should().NotBeNullOrEmpty("the warning names the cell, never the rejected value");

        var cell = table.Rows[0].Cells[0];
        cell.Text.Should().Be("Docs");
        cell.Href.Should().Be("javascript:alert(1)",
            "the imported value is kept so the editor can show and fix it; every emission path "
            + "(generator, text version, renderer) fail-closes it");
    }

    [Theory]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("//evil.example.com/x")]
    public void BlockedLinkSchemes_AreAllReported(string href)
    {
        ImportFirstTable(MjmlTable($"""<tr><td><a href="{href}">Docs</a></td></tr>"""), out var result);

        result.Warnings.Should().ContainSingle(w => w.Key == ImportKeys.BlockedTableLink);
    }

    [Theory]
    [InlineData("https://example.com/x")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("/relative")]
    [InlineData("#fragment")]
    public void AllowedLinkSchemes_AreNotReported(string href)
    {
        ImportFirstTable(MjmlTable($"""<tr><td><a href="{href}">Docs</a></td></tr>"""), out var result);

        result.Warnings.Should().NotContain(w => w.Key == ImportKeys.BlockedTableLink);
    }

    // ── Diagnostics: invalid colour ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("rgb(1,2,3)")]
    [InlineData("expression(alert(1))")]
    [InlineData("url(https://evil.example.com/x)")]
    [InlineData("red;background-image:url(x)")]
    public void InvalidBackgroundColor_IsDroppedAndReported(string color)
    {
        var table = ImportFirstTable(
            MjmlTable($"""<tr><td bgcolor="{color}">x</td></tr>"""),
            out var result);

        result.Warnings.Should().ContainSingle(w => w.Key == ImportKeys.InvalidTableColor);
        table.Rows[0].Cells[0].BackgroundColor.Should().BeNull(
            "a colour the message cannot carry is not added to the model");
    }

    [Theory]
    [InlineData("#fff")]
    [InlineData("#ff0000")]
    [InlineData("red")]
    public void ValidBackgroundColor_IsKeptAndNotReported(string color)
    {
        var table = ImportFirstTable(
            MjmlTable($"""<tr><td bgcolor="{color}">x</td></tr>"""),
            out var result);

        result.Warnings.Should().BeEmpty();
        table.Rows[0].Cells[0].BackgroundColor.Should().Be(color);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static EmailTemplateDocument Document(EmailTableBlock block)
    {
        var document = new EmailTemplateDocument();
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(block);
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }
}
