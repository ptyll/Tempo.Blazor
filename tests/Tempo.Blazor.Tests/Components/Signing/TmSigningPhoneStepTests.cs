using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// TmSigningPhoneStep on the Tm components (Signing redesign phase 4, SI1): the country select is a
/// <c>TmSelect</c> whose default comes from the FIELD CULTURE (a Czech template no longer opens on
/// "+1"), the number input is a <c>TmTextInput Type="tel"</c> and the OTP input carries
/// <c>inputmode="numeric"</c> + <c>autocomplete="one-time-code"</c>. Every control has a visible
/// label (placeholders are not names).
/// </summary>
public class TmSigningPhoneStepTests : LocalizationTestBase
{
    [Fact]
    public void PhoneStep_UsesTmSelectAndTmTextInput_WithLabels_AndOneTimeCodeAutocomplete()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true));

        cut.Find(".tm-signing-phone-step__country select.tm-select").Should().NotBeNull();
        var phone = cut.Find("input.tm-input.tm-signing-phone-step__input");
        phone.GetAttribute("type").Should().Be("tel");

        var otp = cut.Find("input.tm-signing-phone-step__otp");
        otp.GetAttribute("inputmode").Should().Be("numeric");
        otp.GetAttribute("autocomplete").Should().Be("one-time-code");

        // Every control has a visible label (the localized keys), not just a placeholder.
        cut.FindAll("label").Select(node => node.TextContent.Trim())
            .Should().Contain(new[] { "Country", "Phone number", "Verification code" });
    }

    [Fact]
    public void PhoneStep_DefaultCountry_FromCulture()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.Culture, "cs-CZ"));

        cut.Find(".tm-signing-phone-step__country select").GetAttribute("value").Should().Be("+420");
    }

    /// <summary>
    /// The country pick is LOCAL state (Signing redesign phase 4 review): the select used to snap
    /// back to the culture default on the next render when the host did not bind
    /// <c>CountryCodeChanged</c>, and the normalized value silently went back to +420….
    /// </summary>
    [Fact]
    public void CountryPick_PersistsWithoutABoundCountryCodeChanged_AndFeedsTheNormalizedValue()
    {
        string? captured = null;
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.Culture, "cs")
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        cut.Find(".tm-signing-phone-step__country select").Change("+44");
        cut.Find("input[type='tel']").Input("777 123 456");

        // The select keeps the pick after the render the callbacks triggered…
        cut.Find(".tm-signing-phone-step__country select").GetAttribute("value").Should().Be("+44");
        // …and the normalized value carries it.
        captured.Should().Be("+44777123456");
    }

    /// <summary>The same pick path through the runner (the host re-renders between the events).</summary>
    [Fact]
    public void Runner_CountryPick_PersistsAndFeedsTheNormalizedValue()
    {
        IReadOnlyDictionary<string, object?>? published = null;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreatePhoneField()])
            .Add(p => p.ValuesChanged, EventCallback.Factory
                .Create<IReadOnlyDictionary<string, object?>>(this, values => published = values)));

        cut.Find(".tm-signing-phone-step__country select").Change("+44");
        cut.Find("input.tm-signing-phone-step__input").Input("777123456");

        cut.Find(".tm-signing-phone-step__country select").GetAttribute("value").Should().Be("+44");
        published.Should().NotBeNull();
        published!.Should().ContainKey("runner-phone");
        published["runner-phone"].Should().Be("+44777123456");
    }

    [Fact]
    public void CountryPick_SyncsBackWhenTheHostChangesTheParameter()
    {
        var countryCode = "+420";
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.Culture, "cs")
            .Add(p => p.CountryCode, countryCode)
            .Add(p => p.CountryCodeChanged, EventCallback.Factory.Create<string>(this, value => countryCode = value)));

        cut.Find(".tm-signing-phone-step__country select").Change("+44");
        cut.Find(".tm-signing-phone-step__country select").GetAttribute("value").Should().Be("+44");

        // The host pushes a NEW parameter value: the parameter wins again.
        cut.Render(parameters => parameters.Add(p => p.CountryCode, "+1"));
        cut.Find(".tm-signing-phone-step__country select").GetAttribute("value").Should().Be("+1");
    }

    /// <summary>
    /// A culture default the host's options do not offer falls back to the FIRST option — the
    /// select must never carry a value its option list cannot show.
    /// </summary>
    [Fact]
    public void CultureDefault_NotAmongCustomOptions_FallsBackToTheFirstOption()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.Culture, "cs")
            .Add(p => p.CountryOptions, [new TmSigningPhoneStep.PhoneCountryOption("+49", "DE +49")]));

        cut.Find(".tm-signing-phone-step__country select").GetAttribute("value").Should().Be("+49");
    }

    [Fact]
    public void PhoneStep_ExplicitCountry_WinsOverTheCultureDefault()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.Culture, "cs-CZ")
            .Add(p => p.CountryCode, "+421"));

        cut.Find(".tm-signing-phone-step__country select").GetAttribute("value").Should().Be("+421");
    }

    [Fact]
    public void Change_Phone_NormalizesValue()
    {
        string? captured = null;
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CountryCode, "+420")
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        cut.Find("input[type='tel']").Input("777 123 456");

        captured.Should().Be("+420777123456");
    }

    [Fact]
    public void SendCode_InvokesCallbackAndShowsOtp()
    {
        string? sent = null;
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.PhoneNumber, "5550100")
            .Add(p => p.OnSendCode, EventCallback.Factory.Create<string>(this, value => sent = value)));

        cut.Find(".tm-signing-phone-step__send").Click();

        sent.Should().Be("+15550100");
        cut.Find(".tm-signing-phone-step__otp").Should().NotBeNull();
    }

    [Fact]
    public void SentState_ShowsResendCountdown()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.ResendSeconds, 12));

        cut.Find(".tm-signing-phone-step__resend").TextContent.Should().Contain("12");
    }

    /// <summary>The example-format placeholder (phase 4 review): the country's dialling example, not a repeated label.</summary>
    [Fact]
    public void PhoneStep_ExampleFormatPlaceholder_FollowsTheSelectedCountry()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.Culture, "cs"));

        cut.Find("input[type='tel']").GetAttribute("placeholder").Should().Be("777 123 456");

        cut.Find(".tm-signing-phone-step__country select").Change("+44");
        cut.Find("input[type='tel']").GetAttribute("placeholder").Should().Be("7911 123456");
    }

    private static SigningField CreatePhoneField()
    {
        return new SigningField
        {
            Uuid = "runner-phone",
            Name = "Phone",
            Type = SigningFieldType.Phone,
            Areas =
            [
                new SigningFieldArea { AttachmentUuid = "doc", Page = 0, X = 0.1, Y = 0.1, Width = 0.3, Height = 0.05 }
            ]
        };
    }
}
