using FluentAssertions;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Import;

/// <summary>
/// Fáze 10 (§3.7a, carry-forward from F9): an import warns about EVERY value the pipeline will refuse or drop —
/// a blocked link scheme on any block kind and a colour that is not a CSS colour — not only about a table cell.
/// The value itself stays in the model so the editor can show and fix it, and only the scheme/property is ever
/// reported (a value may carry crafted markup).
/// </summary>
public class ImportValueAuditTests
{
    private static ImportResult Import(string mjml) => new MjmlImporter().Import(mjml, null, new EmailImportOptions());

    private static string Page(string body) => $"<mjml><mj-body><mj-section><mj-column>{body}</mj-column></mj-section></mj-body></mjml>";

    [Fact]
    public void A_blocked_button_link_is_reported_and_kept_for_editing()
    {
        var result = Import(Page("<mj-button href=\"javascript:alert(1)\">Go</mj-button>"));

        result.Errors.Should().BeEmpty();
        var button = result.Document!.Sections[0].Columns[0].Blocks.OfType<EmailButtonBlock>().Single();
        button.Href.Should().Be("javascript:alert(1)", "the model keeps the value so the editor can show it");

        var warning = result.Warnings.Should().ContainSingle(w => w.Key == ImportKeys.BlockedLink).Subject;
        warning.Detail.Should().Be("javascript", "only the scheme is reported, never the URL");
    }

    [Fact]
    public void A_blocked_image_source_is_reported()
    {
        var result = Import(Page("<mj-image src=\"data:image/png;base64,AAAA\" alt=\"x\" />"));

        result.Warnings.Should().Contain(w => w.Key == ImportKeys.BlockedLink && w.Detail == "data");
    }

    [Fact]
    public void A_blocked_background_url_on_a_section_is_reported()
    {
        var result = Import(
            "<mjml><mj-body><mj-section background-url=\"vbscript:evil\"><mj-column>"
            + "<mj-text>Hi</mj-text></mj-column></mj-section></mj-body></mjml>");

        result.Warnings.Should().Contain(w => w.Key == ImportKeys.BlockedLink && w.Detail == "vbscript");
    }

    [Fact]
    public void A_colour_that_is_not_a_css_colour_is_reported_by_property()
    {
        var result = Import(Page("<mj-text color=\"url(javascript:alert(1))\">Hi</mj-text>"));

        result.Warnings.Should().Contain(w => w.Key == ImportKeys.InvalidColor && w.Detail == "mj-text color");
    }

    [Fact]
    public void A_plain_document_produces_no_audit_warning()
    {
        var result = Import(Page(
            "<mj-text color=\"#1a1a1a\">Hi</mj-text>"
            + "<mj-button href=\"https://example.com/a\" background-color=\"rgb(20, 30, 40)\">Go</mj-button>"));

        result.Warnings.Should().NotContain(w => w.Key == ImportKeys.BlockedLink || w.Key == ImportKeys.InvalidColor);
    }

    [Fact]
    public void A_table_cell_link_is_not_reported_twice()
    {
        var result = Import(Page(
            "<mj-table><tr><td><a href=\"javascript:alert(1)\">Cell</a></td></tr></mj-table>"));

        result.Warnings.Should().Contain(w => w.Key == ImportKeys.BlockedTableLink);
        result.Warnings.Should().NotContain(w => w.Key == ImportKeys.BlockedLink,
            "the cell link has its own, more precise report");
    }

    [Fact]
    public void A_blocked_scheme_is_reported_once_per_scheme()
    {
        var result = Import(Page(
            "<mj-button href=\"javascript:alert(1)\">One</mj-button>"
            + "<mj-button href=\"javascript:alert(2)\">Two</mj-button>"
            + "<mj-image src=\"javascript:alert(3)\" alt=\"x\" />"));

        result.Warnings.Where(w => w.Key == ImportKeys.BlockedLink).Should().ContainSingle();
    }

    [Fact]
    public void The_generator_refuses_to_write_a_value_that_is_not_a_colour()
    {
        var document = new EmailTemplateDocument();
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = "Hi", Color = "red;background:url(https://evil.test/x)" });
        var section = new EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);

        var mjml = new MjmlGenerator().Generate(document);

        mjml.Should().NotContain("evil.test", "a colour is copied into a style, so a non-colour value is dropped");
        mjml.Should().NotContain("url(", "a CSS function is not a colour value");
    }
}
