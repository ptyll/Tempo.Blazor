using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Fáze 9 security run for the two new <see cref="EmailTableCell"/> fields through the FULL render
/// pipeline (the same one a server-side preview uses): the blocked cell link is warned, audited as
/// <see cref="TemplateSecurityEventKind.BlockedUrlScheme"/> and neutralized in the compiled HTML, and an
/// invalid cell colour is dropped with <see cref="RenderKeys.InvalidTableCellColor"/> — nothing of either
/// value survives into the message.
/// </summary>
public class RenderTableCellSecurityTests
{
    private sealed class SpySink : ITemplateSecurityAuditSink
    {
        public List<TemplateSecurityEvent> Events { get; } = new();

        public void OnEvent(TemplateSecurityEvent securityEvent) => Events.Add(securityEvent);
    }

    private static IEmailTemplateRenderer Renderer(ITemplateSecurityAuditSink? sink = null)
        => new EmailTemplateRenderer(
            new ScribanTemplateEngine(),
            new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance),
            new MjmlNetCompiler(),
            new TextVersionGenerator(),
            sink is null ? null : new[] { sink });

    private static EmailTemplateDocument DocWithTable(EmailTableCell cell)
    {
        var table = new EmailTableBlock();
        var row = new EmailTableRow();
        row.Cells.Add(cell);
        table.Rows.Add(row);

        var document = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(table);
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("//evil.example.com/x")]
    public async Task RenderAsync_TableCellLink_BlockedScheme_IsNeutralizedWarnedAndAudited(string href)
    {
        var sink = new SpySink();
        var document = DocWithTable(new EmailTableCell { Text = "Docs", Href = href });

        var result = await Renderer(sink).RenderAsync(document);

        result.Html.Should().Contain("href=\"#\"");
        result.Html.Should().NotContain("javascript:");
        result.Html.Should().NotContain("evil.example.com");
        result.Html.Should().NotContain("<script>");

        result.Errors.Should().Contain(error => error.Key == RenderKeys.BlockedUrlScheme
                                                && error.Severity == RenderErrorSeverity.Warning);
        sink.Events.Should().Contain(securityEvent =>
            securityEvent.Kind == TemplateSecurityEventKind.BlockedUrlScheme);
        // Only the scheme is audited — never the URL, which may carry data.
        sink.Events.Should().NotContain(securityEvent => securityEvent.Detail.Contains("alert(1)"));
    }

    [Fact]
    public async Task RenderAsync_TableCellLink_AllowedScheme_IsKeptWithoutAWarning()
    {
        var document = DocWithTable(new EmailTableCell { Text = "Docs", Href = "https://example.com/docs" });

        var result = await Renderer().RenderAsync(document);

        result.Html.Should().Contain("href=\"https://example.com/docs\"");
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("expression(alert(1))")]
    [InlineData("url(https://evil.example.com/x.png)")]
    [InlineData("#fff\" onmouseover=\"alert(1)")]
    public async Task RenderAsync_TableCellColor_Invalid_IsDroppedAndReported(string color)
    {
        var document = DocWithTable(new EmailTableCell { Text = "x", BackgroundColor = color });

        var result = await Renderer().RenderAsync(document);

        result.Html.Should().NotContain("expression(");
        result.Html.Should().NotContain("onmouseover");
        result.Html.Should().NotContain("evil.example.com");
        result.Errors.Should().Contain(error => error.Key == RenderKeys.InvalidTableCellColor
                                                && error.Severity == RenderErrorSeverity.Warning);
        // The render still succeeds: a dropped colour is a warning, not a broken message.
        result.Html.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RenderAsync_TableCellColor_Valid_IsRenderedAsBgcolor()
    {
        var document = DocWithTable(new EmailTableCell { Text = "x", BackgroundColor = "#eeeeee" });

        var result = await Renderer().RenderAsync(document);

        result.Html.Should().Contain("bgcolor=\"#eeeeee\"");
        result.Errors.Should().BeEmpty();
    }
}
