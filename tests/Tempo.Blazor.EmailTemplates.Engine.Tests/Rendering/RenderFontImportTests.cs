using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Fáze 10 security re-review (item 4): the <c>@import</c> rule of <see cref="CssSafety"/> reaches the compiled
/// HTML through the markup guard, so the pipeline has to be honest about WHICH imports are its own. The MJML
/// compiler writes an <c>@import</c> for every <c>mj-font</c> (next to the <c>&lt;link&gt;</c>) from the href the
/// generator already allow-listed — that one has to survive — while the compiler's implicit default font table
/// (an <c>@import</c> of Google's Ubuntu that no template asked for) is switched off, and an author's
/// <c>@import</c> in a raw block is refused and warned about.
/// </summary>
public class RenderFontImportTests
{
    private static IEmailTemplateRenderer Renderer()
        => new EmailTemplateRenderer(
            new ScribanTemplateEngine(),
            new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance),
            new MjmlNetCompiler(),
            new TextVersionGenerator());

    private static EmailTemplateDocument Document(EmailBlockBase block)
    {
        var document = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(block);
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    [Fact]
    public async Task RenderAsync_DocumentWithAFont_KeepsTheCompilersImportWithoutAWarning()
    {
        var document = Document(new EmailTextBlock { Content = "hi" });
        document.Styles.Fonts.Add(new EmailFont { Name = "Roboto", Href = "https://f/r.css" });

        var result = await Renderer().RenderAsync(document);

        result.Html.Should().Contain("@import url(https://f/r.css)");
        result.Html.Should().Contain("href=\"https://f/r.css\"", "the <link> stays next to the @import");
        result.Errors.Should().BeEmpty("the pipeline's own font import is not a security finding");
    }

    [Fact]
    public async Task RenderAsync_DocumentWithoutAFont_ImportsNothing()
    {
        var result = await Renderer().RenderAsync(Document(new EmailTextBlock { Content = "hi" }));

        result.Html.Should().NotContain("@import",
            "the compiler's implicit Ubuntu font table is disabled: no message fetches a font it did not ask for");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_RawBlockStylesheetImport_IsRemovedAndWarned()
    {
        const string raw = "<style>@IMPORT url(https://evil.example/x.css);</style>";
        var result = await Renderer().RenderAsync(Document(new EmailRawBlock { Content = raw }));

        result.Html.Should().NotContain("@import");
        result.Html.Should().NotContain("evil.example");
        result.Errors.Should().Contain(error => error.Key == RenderKeys.UnsafeElementRemoved
                                                && error.Severity == RenderErrorSeverity.Warning);
    }
}
