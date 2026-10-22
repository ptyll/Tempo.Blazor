using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Tests.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 10 (§2.9.3, B13): the validation surface counts findings per severity, renders every finding as a
/// real button with an icon, a TITLE and a DESCRIPTION (the sentence alone was unreadable in a list), offers
/// the “Go to” action only where there is somewhere to go, and computes the document only when the dialog
/// opens instead of on every parent render.
/// </summary>
public class ValidationDialogPhase10Tests : BunitContext, IDisposable
{
    private readonly CultureScope _culture = CultureScope.Use("en");

    public ValidationDialogPhase10Tests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ITmEmailLocalizer>(new EchoEmailLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddTempoEmailTemplateEditor();
    }

    void IDisposable.Dispose()
    {
        _culture.Dispose();
        Dispose();
    }

    private static EmailTemplateDocument DocumentWith(EmailBlockBase block)
    {
        var document = new EmailTemplateDocument { Subject = "Hi" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(block);
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    [Fact]
    public void Summary_CountsFindingsPerSeverity()
    {
        // A preheader over 100 characters is the one Info rule; a button without a link is an Error and an
        // image without alt text is a Warning, so all three levels are present at once.
        var document = DocumentWith(new EmailButtonBlock { Text = "Go" });
        document.Preheader = new string('x', EmailDocumentValidator.MaxPreheaderLength + 1);
        document.Sections[0].Columns[0].Blocks.Add(new EmailImageBlock { Src = "https://example.com/a.png" });

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        var summary = cut.Find("[data-tm-validation-summary]");
        summary.GetAttribute("data-tm-validation-errors").Should().Be("1");
        summary.GetAttribute("data-tm-validation-warnings").Should().Be("1");
        summary.GetAttribute("data-tm-validation-infos").Should().Be("1");
        summary.TextContent.Should().Contain("EmailEditor_Validation_Count_Error|1");
        summary.TextContent.Should().Contain("EmailEditor_Validation_Count_Warning|1");
        summary.TextContent.Should().Contain("EmailEditor_Validation_Count_Info|1");
    }

    [Fact]
    public void EveryFinding_IsAButton_WithAnIconTitleDescriptionAndAGoToAction()
    {
        var button = new EmailButtonBlock { Text = "Go" };
        Guid? navigated = null;
        var cut = Render<TmEmailValidationPanel>(p => p
            .Add(c => c.Document, DocumentWith(button))
            .Add(c => c.OnNavigate, id => navigated = id));

        var finding = cut.Find("[data-tm-validation-message]");
        finding.TagName.Should().Be("BUTTON", "a list item with @onclick is not keyboard operable");
        finding.GetAttribute("type").Should().Be("button");
        finding.QuerySelector("[data-tm-validation-icon]").Should().NotBeNull();
        finding.QuerySelector("[data-tm-validation-title]").TextContent
            .Should().Be(DocumentValidationKeys.ButtonHrefMissing + ValidationKeyShape.TitleSuffix);
        finding.QuerySelector("[data-tm-validation-description]").TextContent
            .Should().Be(DocumentValidationKeys.ButtonHrefMissing + ValidationKeyShape.DescriptionSuffix);
        finding.QuerySelector("[data-tm-validation-action]").TextContent.Trim().Should().Be("EmailEditor_Validation_GoTo");

        finding.Click();
        navigated.Should().Be(button.Id);
    }

    [Fact]
    public void AFindingWithoutATarget_IsNotAButton_AndOffersNoAction()
    {
        var document = DocumentWith(new EmailTextBlock { Content = "ok" });
        document.Sections.Add(new EmailSection());
        var options = new EmailImportOptions { MaxSections = 1 };
        Guid? navigated = null;

        var cut = Render<TmEmailValidationPanel>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.ImportOptions, options)
            .Add(c => c.OnNavigate, id => navigated = id));

        var finding = cut.FindAll("[data-tm-validation-message]")
            .Single(element => element.TextContent.Contains(DocumentValidationKeys.TooManySections + "_Title", StringComparison.Ordinal));
        finding.TagName.Should().NotBe("BUTTON");
        finding.QuerySelector("[data-tm-validation-action]").Should().BeNull();
        navigated.Should().BeNull();
    }

    [Fact]
    public void Findings_AreOrderedBySeverityRank_NotByTheEnumValue()
    {
        var document = DocumentWith(new EmailButtonBlock { Text = "Go" });
        document.Preheader = new string('x', EmailDocumentValidator.MaxPreheaderLength + 1);

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        cut.FindAll("[data-tm-validation-message]")
            .Select(m => m.GetAttribute("data-tm-validation-severity"))
            .Should().ContainInOrder("Error", "Info");
    }

    [Fact]
    public void Validation_IsComputedOncePerDocument_NotOnEveryParentRender()
    {
        var validator = Substitute.For<ITemplateValidator>();
        validator.Validate(Arg.Any<string>()).Returns(TemplateValidationResult.Valid);
        Services.AddSingleton(validator);

        var document = DocumentWith(new EmailTextBlock { Content = "{{ name }}" });
        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        validator.Received().Validate(Arg.Any<string>());
        validator.ClearReceivedCalls();

        cut.Render(p => p.Add(c => c.Document, document));
        cut.Render(p => p.Add(c => c.Document, document));

        validator.DidNotReceive().Validate(Arg.Any<string>());
    }

    [Fact]
    public void ImportLimits_AreAppliedToTheFindings_WhenTheHostTightensThem()
    {
        var document = DocumentWith(new EmailTextBlock { Content = "ok" });
        document.Sections.Add(new EmailSection());

        var cut = Render<TmEmailValidationPanel>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.ImportOptions, new EmailImportOptions { MaxSections = 1 }));

        cut.FindAll("[data-tm-validation-message]")
            .Select(finding => finding.TextContent)
            .Should().Contain(text => text.Contains(DocumentValidationKeys.TooManySections + ValidationKeyShape.TitleSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public void NoFindings_ShowsASuccessCard_WithTheCheckIcon()
    {
        // Fáze 10 review (UX 15): the good outcome used to be a quiet grey sentence, which reads like "nothing
        // to report yet". It is a success card with the check icon and the success text token.
        var document = DocumentWith(new EmailTextBlock { Content = "ok" });

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        var ok = cut.Find("[data-tm-validation-ok]");
        ok.TextContent.Should().Contain("EmailEditor_Validation_Valid");
        ok.QuerySelector(".tm-email-validation__ok-icon").Should().NotBeNull();
        ok.QuerySelector("svg, .tm-icon").Should().NotBeNull("the card carries the check icon, not only text");
        cut.FindAll("[data-tm-validation-summary]").Should().BeEmpty("there is no count to show");
    }

    [Fact]
    public void ZeroCounts_LoseTheSeverityColour()
    {
        // Three coloured chips with "0" in two of them read as findings that are not there.
        var document = DocumentWith(new EmailButtonBlock { Text = "Go" });

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        cut.Find("[data-tm-validation-count='Error']").ClassList
            .Should().NotContain("tm-email-validation__count--zero");
        cut.Find("[data-tm-validation-count='Warning']").ClassList
            .Should().Contain("tm-email-validation__count--zero");
        cut.Find("[data-tm-validation-count='Info']").ClassList
            .Should().Contain("tm-email-validation__count--zero");
    }

    [Fact]
    public void SyntaxUnavailableNote_SitsBelowTheFindings()
    {
        var document = DocumentWith(new EmailButtonBlock { Text = "Go" });

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        var note = cut.Find("[data-tm-validation-unavailable]");
        note.GetAttribute("role").Should().Be("note");
        var html = cut.Markup;
        html.IndexOf("data-tm-validation-unavailable", StringComparison.Ordinal)
            .Should().BeGreaterThan(html.IndexOf("data-tm-validation-summary", StringComparison.Ordinal),
                "it qualifies the findings, so it belongs under them");
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments] => key;
    }

    private sealed class EchoEmailLocalizer : Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "|" + string.Join("|", arguments);
    }
}
