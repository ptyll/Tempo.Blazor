using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7A (kontrakt §3.10, D9/D19): the validation panel shows the new <c>Info</c> severity — the single
/// recommending rule is a preheader longer than 100 characters — with its own icon/colour modifier, and
/// findings are ordered by <c>LayoutSeverityRank</c> (Error → Warning → Info), never by the enum number.
/// </summary>
public class ValidationInfoSeverityTests : BunitContext
{
    public ValidationInfoSeverityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ITmEmailLocalizer>(new EchoEmailLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddTempoEmailTemplateEditor();
    }

    private static EmailTemplateDocument Document(string? preheader = null, bool withBrokenButton = false)
    {
        var document = new EmailTemplateDocument { Subject = "Ahoj", Preheader = preheader };
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = "Hello" });
        if (withBrokenButton) column.Blocks.Add(new EmailButtonBlock { Text = "Go", Href = null });
        var section = new EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    [Fact]
    public void A_long_preheader_is_shown_as_an_info_finding()
    {
        var document = Document(new string('x', EmailDocumentValidator.MaxPreheaderLength + 1));

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        var item = cut.Find("[data-tm-validation-severity=\"Info\"]");
        item.TextContent.Should().Contain(DocumentValidationKeys.PreheaderTooLong);
        item.ClassList.Should().Contain("tm-email-validation__item--info");
    }

    [Fact]
    public void Info_findings_carry_their_own_icon_so_the_severity_is_not_colour_only()
    {
        var document = Document(new string('x', EmailDocumentValidator.MaxPreheaderLength + 1));

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        cut.Find("[data-tm-validation-severity=\"Info\"] [data-tm-validation-icon]").Should().NotBeNull();
    }

    [Fact]
    public void Findings_are_ordered_error_then_warning_then_info()
    {
        var document = Document(new string('x', EmailDocumentValidator.MaxPreheaderLength + 1), withBrokenButton: true);

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        var severities = cut.FindAll("[data-tm-validation-message]")
            .Select(e => e.GetAttribute("data-tm-validation-severity"))
            .ToList();

        severities.Should().NotBeEmpty();
        severities.Should().BeInAscendingOrder(new LayoutSeverityComparer());
        severities[0].Should().Be("Error", "the broken button is the most serious finding");
        severities[^1].Should().Be("Info");
    }

    [Fact]
    public void A_preheader_at_the_limit_shows_no_info_finding()
    {
        var document = Document(new string('x', EmailDocumentValidator.MaxPreheaderLength));

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        cut.FindAll("[data-tm-validation-severity=\"Info\"]").Should().BeEmpty();
    }

    private sealed class LayoutSeverityComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
            => Rank(x).CompareTo(Rank(y));

        private static int Rank(string? value) => value is not null && Enum.TryParse<Abstractions.Layout.LayoutSeverity>(value, out var severity)
            ? Abstractions.Layout.LayoutSeverityRank.Of(severity)
            : int.MaxValue;
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }

    private sealed class EchoEmailLocalizer : ITmEmailLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "[" + string.Join(",", arguments) + "]";
    }
}
