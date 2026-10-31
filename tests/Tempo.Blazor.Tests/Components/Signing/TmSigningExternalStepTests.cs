using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// TmSigningExternalStep (Signing redesign phase 4): a hero icon in a primary-subtle circle per
/// type, a per-type default description (overridable by the field description), the payment amount
/// formatted with the FIELD CULTURE (the old InvariantCulture "EUR 19.50" was the bug), Start as the
/// primary action with the open link as an outline sibling, and the verified / paid state once the
/// value is <c>true</c>.
/// </summary>
public class TmSigningExternalStepTests : LocalizationTestBase
{
    [Fact]
    public void ExternalStep_RendersHeroIconInCircle()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification }));

        cut.Find(".tm-signing-external-step__hero .tm-icon").Should().NotBeNull();
    }

    [Theory]
    [InlineData(SigningFieldType.Verification, "Complete the identity verification")]
    [InlineData(SigningFieldType.Kba, "Answer the questions")]
    [InlineData(SigningFieldType.Payment, "Pay the amount")]
    public void ExternalStep_WithoutFieldDescription_ShowsTheTypeDefault(SigningFieldType type, string expectedFragment)
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "External", Type = type }));

        cut.Find(".tm-signing-step-shell__description").TextContent.Should().Contain(expectedFragment);
    }

    [Fact]
    public void ExternalStep_FieldDescription_WinsOverTheTypeDefault()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "ID check",
                Type = SigningFieldType.Verification,
                Description = "Provider-specific instructions."
            }));

        cut.Find(".tm-signing-step-shell__description").TextContent.Should().Contain("Provider-specific instructions.");
    }

    [Fact]
    public void ExternalStep_PaymentAmount_FormattedByCulture()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Uuid = "payment",
                Name = "Payment",
                Type = SigningFieldType.Payment,
                Preferences = new SigningFieldPreferences { Currency = "Kč", Price = 1500m }
            })
            .Add(p => p.Culture, "cs-CZ"));

        cut.Find(".tm-signing-external-step__amount").TextContent.Should().Be("1\u00A0500,00 Kč");
    }

    [Fact]
    public void ExternalStep_ShowsVerifiedState_WhenValueTrue()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification })
            .Add(p => p.Value, true));

        cut.Find(".tm-signing-external-step__verified").TextContent.Should().Contain("Verified");
        cut.FindAll(".tm-signing-external-step__start-button").Should().BeEmpty(
            "the verified state replaces the call to action");
    }

    [Fact]
    public void ExternalStep_ShowsPaidState_WhenPaymentValueTrue()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Payment", Type = SigningFieldType.Payment })
            .Add(p => p.Value, "true"));

        cut.Find(".tm-signing-external-step__verified").TextContent.Should().Contain("Paid");
    }

    [Fact]
    public void Verification_Loading_RendersLoadingState()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification })
            .Add(p => p.IsLoading, true));

        cut.Find(".tm-signing-external-step__loading").TextContent.Should().Contain("Loading");
    }

    [Fact]
    public void Verification_Error_ShowsValidation()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification })
            .Add(p => p.Error, "Provider failed"));

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Be("Provider failed");
    }

    [Fact]
    public void Verification_ExternalUrl_RendersOutlineLink()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification })
            .Add(p => p.ExternalUrl, "https://example.test/verify"));

        var link = cut.Find(".tm-signing-external-step__link");
        link.GetAttribute("href").Should().Be("https://example.test/verify");
        link.ClassList.Should().Contain("tm-btn-outline", "Open renders as the outline action");
    }

    /// <summary>Phase 6 (security): the provider link opens a NEW TAB and must never hand the
    /// signing page's window context over — rel carries both noopener and noreferrer.</summary>
    [Fact]
    public void ExternalStep_ExternalUrl_RendersRelNoopenerNoreferrer()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification })
            .Add(p => p.ExternalUrl, "https://example.test/verify"));

        var link = cut.Find(".tm-signing-external-step__link");
        link.GetAttribute("target").Should().Be("_blank");
        link.GetAttribute("rel").Should().Contain("noopener").And.Contain("noreferrer");
        cut.Find(".tm-signing-external-step__redirect-note").Should().NotBeNull(
            "the signer is told the provider opens outside the signing page");
    }

    /// <summary>Phase 6: the host's provider context names the provider above the call to
    /// action.</summary>
    [Fact]
    public void ExternalStep_ProviderName_ShowsTheProviderLine()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification })
            .Add(p => p.ProviderName, "Acme Check"));

        cut.Find(".tm-signing-external-step__provider").TextContent.Should().Contain("Acme Check");
    }

    [Fact]
    public void Kba_WithoutQuestions_RendersPrimaryStartButton()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "KBA", Type = SigningFieldType.Kba }));

        var start = cut.Find(".tm-signing-external-step__start-button");
        start.TextContent.Should().Contain("Start KBA");
        start.ClassList.Should().Contain("tm-btn-primary", "Start is the primary action");
    }

    [Fact]
    public void Kba_WithQuestions_RendersQuestionInputs()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "KBA", Type = SigningFieldType.Kba })
            .Add(p => p.Questions, ["What city?"]));

        // Each question labels its own TmTextInput answer (the question IS the name).
        var questions = cut.Find(".tm-signing-external-step__questions");
        questions.TextContent.Should().Contain("What city?");
        questions.QuerySelectorAll("input.tm-input").Should().HaveCount(1);
    }

    [Fact]
    public void Payment_RendersAmountAndCheckoutCallback()
    {
        SigningField? checkedOut = null;
        var field = new SigningField
        {
            Uuid = "payment",
            Name = "Payment",
            Type = SigningFieldType.Payment,
            Preferences = new SigningFieldPreferences { Currency = "EUR", Price = 19.5m }
        };
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, field)
            .Add(p => p.OnCheckout, EventCallback.Factory.Create<SigningField>(this, value => checkedOut = value)));

        cut.Find(".tm-signing-external-step__amount").TextContent.Should().Contain("19.50");
        cut.Find(".tm-signing-external-step__checkout").Click();

        checkedOut.Should().BeSameAs(field);
    }

    /// <summary>Phase 6 review item 14: with a named provider the verified state reads the
    /// COMBINED "Verified by {provider}" text (payment keeps "Paid").</summary>
    [Fact]
    public void ExternalStep_VerifiedWithProvider_ShowsTheCombinedVerifiedByText()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification })
            .Add(p => p.Value, true)
            .Add(p => p.ProviderName, "Acme Check"));

        cut.Find(".tm-signing-external-step__verified").TextContent.Should().Contain("Verified by Acme Check");
    }

    /// <summary>Phase 6 review item 4: the KBA questions branch renders a SUBMIT action that hands
    /// the typed answers to the host through <see cref="SigningExternalStepRequest"/> — the field
    /// AND the answers, nothing else.</summary>
    [Fact]
    public async Task ExternalStep_KbaSubmit_RaisesRequestWithTheFieldAndTheTypedAnswers()
    {
        SigningExternalStepRequest? submitted = null;
        var field = new SigningField { Uuid = "kba", Name = "KBA", Type = SigningFieldType.Kba };
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, field)
            .Add(p => p.Questions, ["What city?", "What street?"])
            .Add(p => p.OnSubmitAnswers, EventCallback.Factory
                .Create<SigningExternalStepRequest>(this, request => submitted = request)));

        var answers = cut.FindAll("input.tm-signing-external-step__answer");
        answers[0].Input("Brno");
        answers[1].Input("Kounicova");
        cut.Find(".tm-signing-external-step__submit").Click();

        submitted.Should().NotBeNull("the submit hands the request to the host");
        submitted!.Field.Uuid.Should().Be("kba");
        submitted.Answers.Should().HaveCount(2);
        submitted.Answers["What city?"].Should().Be("Brno");
        submitted.Answers["What street?"].Should().Be("Kounicova");
    }

    /// <summary>Phase 6 review item 7: the provider's LOADING state keeps the actions IN PLACE —
    /// disabled and aria-busy — next to the indicator row, instead of swapping them out.</summary>
    [Fact]
    public void ExternalStep_Loading_KeepsStartInPlace_DisabledAndAriaBusy()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification })
            .Add(p => p.IsLoading, true));

        cut.Find(".tm-signing-external-step__loading").Should().NotBeNull("the indicator row renders");
        var start = cut.Find(".tm-signing-external-step__start-button");
        start.Should().NotBeNull("the Start action stays in place while loading");
        start.HasAttribute("disabled").Should().BeTrue();
        start.GetAttribute("aria-busy").Should().Be("true");
    }

    /// <summary>Phase 6 review item 13: the outbound link references the redirect note through
    /// <c>aria-describedby</c> (the note carries the id), and its icon is aria-hidden.</summary>
    [Fact]
    public void ExternalStep_Link_ReferencesTheRedirectNote_AndHidesTheIcon()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification })
            .Add(p => p.ExternalUrl, "https://example.test/verify"));

        var noteId = cut.Find(".tm-signing-external-step__redirect-note").GetAttribute("id");
        noteId.Should().NotBeNullOrWhiteSpace("the note carries a stable id");
        var link = cut.Find(".tm-signing-external-step__link");
        link.GetAttribute("aria-describedby").Should().Be(noteId);
        link.QuerySelector("svg.tm-icon").Should().NotBeNull("the external-link icon renders");
    }

    /// <summary>Phase 6 review item 7 (WCAG 4.1.3): ONE always-rendered polite live region carries
    /// the events ("Loading…", "Verified"/"Paid"); the verified block carries NO role itself.</summary>
    [Fact]
    public void ExternalStep_Announcements_RunThroughOneAlwaysRenderedLiveRegion()
    {
        var cut = Render<TmSigningExternalStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "ID check", Type = SigningFieldType.Verification }));

        cut.FindAll("[role='status'][aria-live='polite']").Should().ContainSingle(
            "exactly one live region exists before any announcement");

        cut.Render(parameters => parameters.Add(p => p.IsLoading, true));
        cut.Find("[role='status'][aria-live='polite']").TextContent.Should().Contain("Loading");

        cut.Render(parameters => parameters.Add(p => p.IsLoading, false).Add(p => p.Value, true));
        cut.FindAll(".tm-signing-external-step__verified[role='status']").Should().BeEmpty(
            "the verified block is presentation only — the live region owns announcements");
        cut.Find("[role='status'][aria-live='polite']").TextContent.Should().Contain("Verified");
    }
}
