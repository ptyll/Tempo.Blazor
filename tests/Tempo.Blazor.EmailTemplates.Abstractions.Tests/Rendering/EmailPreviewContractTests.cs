using FluentAssertions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// The transport-free preview contract (D17/§3.4): a request with a document, optional variable JSON,
/// the substitution switch a host needs for an HTML export and the culture the server should render in;
/// a result whose strings are never null so a host view never has to nil-check, plus a status that keeps
/// "the network/server is gone" (<see cref="EmailPreviewStatus.Unavailable"/>) apart from "the template
/// failed" (<see cref="EmailPreviewStatus.RenderFailed"/>).
/// </summary>
public class EmailPreviewContractTests
{
    [Fact]
    public void Request_DefaultsToSubstitutingVariables()
    {
        var request = new EmailPreviewRequest { Document = new EmailTemplateDocument() };

        request.SubstituteVariables.Should().BeTrue();
        request.VariablesJson.Should().BeNull();
        request.CultureName.Should().BeNull();
    }

    [Fact]
    public void Request_KeepsTheExportModeAndTheCulture()
    {
        var request = new EmailPreviewRequest
        {
            Document = new EmailTemplateDocument { Subject = "Hi {{ name }}" },
            VariablesJson = """{"name":"World"}""",
            SubstituteVariables = false,
            CultureName = "cs-CZ",
        };

        request.SubstituteVariables.Should().BeFalse();
        request.CultureName.Should().Be("cs-CZ");
        request.VariablesJson.Should().Contain("World");
    }

    [Fact]
    public void Result_ExposesEveryRenderField()
    {
        var errors = new[] { new RenderError("boom", 1, 2, RenderKeys.BlockedUrlScheme, RenderErrorSeverity.Warning) };
        var result = new EmailPreviewResult("<html>", "text", "subject", "preheader", errors, EmailPreviewStatus.Rendered);

        result.Html.Should().Be("<html>");
        result.Text.Should().Be("text");
        result.Subject.Should().Be("subject");
        result.Preheader.Should().Be("preheader");
        result.Errors.Should().BeEquivalentTo(errors);
        result.Status.Should().Be(EmailPreviewStatus.Rendered);
    }

    [Fact]
    public void Status_HasTheThreeDistinctValues()
    {
        Enum.GetValues<EmailPreviewStatus>().Should().BeEquivalentTo(
            [EmailPreviewStatus.Rendered, EmailPreviewStatus.RenderFailed, EmailPreviewStatus.Unavailable]);
    }

    [Fact]
    public void Interface_HasOneReplacementPoint()
    {
        var method = typeof(IEmailPreviewRenderer).GetMethod(nameof(IEmailPreviewRenderer.RenderAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<EmailPreviewResult>));
        method.GetParameters().Select(p => p.ParameterType).Should().Equal(
            typeof(EmailPreviewRequest), typeof(CancellationToken));
    }
}
