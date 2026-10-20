using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Severity of render diagnostics (D23): a security clean-up (blocked URL, removed event handler) is a
/// warning — the message is still renderable and its HTML is complete — while a broken template or a
/// broken compile stays an error and fails <see cref="RenderResult.Success"/>.
/// </summary>
public class RenderErrorSeverityTests
{
    private static IEmailTemplateRenderer Renderer()
        => new EmailTemplateRenderer(
            new ScribanTemplateEngine(), new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance), new MjmlNetCompiler(), new TextVersionGenerator());

    private static EmailTemplateDocument Doc(params EmailBlockBase[] blocks)
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        foreach (var block in blocks) col.Blocks.Add(block);
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    [Fact]
    public async Task RenderAsync_BlockedUrl_IsWarning_SuccessStaysTrue_HtmlNotEmpty()
    {
        var doc = Doc(new EmailButtonBlock { Text = "Go", Href = "javascript:alert(1)" });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Success.Should().BeTrue("a refused URL is cleaned up, not a failure to render");
        result.HasWarnings.Should().BeTrue();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Severity.Should().Be(RenderErrorSeverity.Warning);
        result.Html.Should().NotBeNullOrWhiteSpace();
        result.Html.Should().Contain("href=\"#\"");
        result.Subject.Should().Be("S");
    }

    [Fact]
    public async Task RenderAsync_CompileError_SuccessFalse()
    {
        // A raw block that breaks the MJML structure: the compiler reports it, and that IS a failure.
        var doc = Doc(new EmailRawBlock { Content = "<mj-column>" });

        var result = await Renderer().RenderAsync(doc, new { });

        result.Success.Should().BeFalse();
        result.HasWarnings.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Severity == RenderErrorSeverity.Error);
    }

    [Fact]
    public void RenderResult_Success_IgnoresWarnings_And_CountsErrors()
    {
        var warning = new RenderError("w", Severity: RenderErrorSeverity.Warning);
        var error = new RenderError("e");

        new RenderResult("<html/>", "s", null, "t", [warning]).Success.Should().BeTrue();
        new RenderResult("<html/>", "s", null, "t", [warning]).HasWarnings.Should().BeTrue();
        new RenderResult("<html/>", "s", null, "t", [warning, error]).Success.Should().BeFalse();
        new RenderResult("<html/>", "s", null, "t", [error]).HasWarnings.Should().BeFalse();
        new RenderResult("<html/>", "s", null, "t", []).Success.Should().BeTrue();
    }

    [Fact]
    public void RenderErrorSeverity_NumericValues_ArePartOfTheContract()
    {
        ((int)RenderErrorSeverity.Error).Should().Be(0);
        ((int)RenderErrorSeverity.Warning).Should().Be(1);
        new RenderError("m").Severity.Should().Be(RenderErrorSeverity.Error);
    }
}
