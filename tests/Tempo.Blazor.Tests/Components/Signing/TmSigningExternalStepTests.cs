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
}
