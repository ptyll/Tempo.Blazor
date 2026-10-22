using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Tests.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7A review fixes on the validation panel: the severity is spelled out for screen readers (the icon
/// is decorative), a finding that points at a node is a real <c>&lt;button&gt;</c> so a keyboard user can
/// jump to it, and the Info/Warning icon and strip use the STRONG colour tokens instead of the pale fill
/// colours that failed contrast.
/// </summary>
public class EmailValidationAccessibilityTests : BunitContext, IDisposable
{
    private readonly CultureScope _culture = CultureScope.Use("cs");

    public EmailValidationAccessibilityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddTempoEmailTemplateEditor();
    }

    void IDisposable.Dispose()
    {
        _culture.Dispose();
        Dispose();
    }

    private static EmailTemplateDocument Document(
        bool withBrokenButton = false, string? preheader = null, bool withImageWithoutAlt = false)
    {
        var document = new EmailTemplateDocument { Subject = "Ahoj", Preheader = preheader };
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = "Hello" });
        if (withBrokenButton) column.Blocks.Add(new EmailButtonBlock { Text = "Go", Href = null });
        if (withImageWithoutAlt) column.Blocks.Add(new EmailImageBlock { Src = "https://x/i.png", Alt = string.Empty });
        var section = new EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    private IRenderedComponent<TmEmailValidationPanel> Panel(EmailTemplateDocument document, Action<Guid?>? onNavigate = null)
        => Render<TmEmailValidationPanel>(p =>
        {
            p.Add(c => c.Document, document);
            if (onNavigate is not null) p.Add(c => c.OnNavigate, (Guid? id) => onNavigate(id));
        });

    [Theory]
    [InlineData("Error", "Chyba")]
    [InlineData("Warning", "Upozornění")]
    [InlineData("Info", "Doporučení")]
    public void The_severity_is_spelled_out_for_screen_readers(string severity, string expected)
    {
        var document = DocumentFor(severity);

        var cut = Panel(document);

        var item = cut.Find($"[data-tm-validation-severity=\"{severity}\"]");
        item.QuerySelector(".tm-sr-only")!.TextContent.Trim().Should().Be(expected,
            "the colour and the icon are not read aloud — the prefix names the severity");
    }

    /// <summary>One document per severity: a broken button (Error), an image without alt (Warning), a long preheader (Info).</summary>
    private static EmailTemplateDocument DocumentFor(string severity) => severity switch
    {
        "Error" => Document(withBrokenButton: true),
        "Warning" => Document(withImageWithoutAlt: true),
        _ => Document(preheader: new string('x', EmailDocumentValidator.MaxPreheaderLength + 1)),
    };

    [Fact]
    public void A_finding_that_points_at_a_node_is_a_keyboard_operable_button()
    {
        var document = Document(withBrokenButton: true);
        Guid? navigated = null;
        var cut = Panel(document, id => navigated = id);

        var link = cut.Find("button[data-tm-validation-severity=\"Error\"]");
        link.GetAttribute("type").Should().Be("button");

        link.Click();

        navigated.Should().Be(document.Sections[0].Columns[0].Blocks[1].Id);
    }

    [Fact]
    public void The_whole_row_still_navigates_when_clicked_outside_the_button()
    {
        // The row keeps its click handler (a pointer user aims at the row, not at six pixels of text); the
        // button is the keyboard/AT affordance and must not fire twice.
        var document = Document(withBrokenButton: true);
        Guid? navigated = null;
        var cut = Panel(document, id => navigated = id);

        cut.Find("[data-tm-validation-severity=\"Error\"]").Click();

        navigated.Should().Be(document.Sections[0].Columns[0].Blocks[1].Id);
    }

    [Fact]
    public void An_all_clear_renders_no_link_at_all()
    {
        var cut = Panel(Document());

        cut.Find("[data-tm-validation-ok]").Should().NotBeNull();
        cut.FindAll("[data-tm-validation-message]").Should().BeEmpty("a document-level sentence has nowhere to jump to");
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
