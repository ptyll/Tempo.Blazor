using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;
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

    /// <summary>
    /// Phase 6: the countdown TICKS on the injected clock (no sleeps) — from the send it runs
    /// 30 → 0, then the text turns into a real "Resend" button, and pressing it re-raises the
    /// send and restarts the window.
    /// </summary>
    [Fact]
    public void PhoneStep_ResendCountdown_WithFakeTimeProvider_30To0_ThenResendButton()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var sends = 0;
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.PhoneNumber, "5550100")
            .Add(p => p.ResendSeconds, 30)
            .Add(p => p.OnSendCode, EventCallback.Factory.Create<string>(this, _ => sends++)));

        cut.Find(".tm-signing-phone-step__send").Click();
        sends.Should().Be(1);
        cut.Find(".tm-signing-phone-step__resend").TextContent.Should().Contain("30");
        cut.FindAll(".tm-signing-phone-step__resend-button").Should().BeEmpty("while the countdown runs there is no resend action");

        time.Advance(TimeSpan.FromSeconds(30));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-phone-step__resend-button").TextContent.Should().Contain("Resend",
                "at zero the countdown text becomes the real resend action");
            cut.FindAll(".tm-signing-phone-step__resend").Should().BeEmpty("the countdown text is gone at 0");
        });

        cut.Find(".tm-signing-phone-step__resend-button").Click();
        sends.Should().Be(2, "the resend re-raises the send");
        cut.Find(".tm-signing-phone-step__resend").TextContent.Should().Contain("30",
            "the resend restarts the window");
    }

    /// <summary>The Verify action raises the typed code — the RUNNER (or a host) decides whether
    /// the code is valid; the step only renders the outcome.</summary>
    [Fact]
    public void PhoneStep_VerifyButton_RaisesTheTypedCode()
    {
        var verifyRequests = 0;
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.OtpCode, "123456")
            .Add(p => p.OnVerifyCode, EventCallback.Factory.Create(this, () => verifyRequests++)));

        cut.Find(".tm-signing-phone-step__verify").Click();

        verifyRequests.Should().Be(1);
    }

    /// <summary>The host-confirmed state replaces the OTP row — the code input, the Verify action
    /// and the resend affordances are gone.</summary>
    [Fact]
    public void PhoneStep_VerifiedState_ReplacesTheOtpControls()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.Verified, true));

        cut.Find(".tm-signing-phone-step__verified").TextContent.Should().Contain("Verified");
        cut.FindAll("input.tm-signing-phone-step__otp").Should().BeEmpty();
        cut.FindAll(".tm-signing-phone-step__verify").Should().BeEmpty();
        cut.FindAll(".tm-signing-phone-step__resend").Should().BeEmpty();
        cut.FindAll(".tm-signing-phone-step__resend-button").Should().BeEmpty();
    }

    /// <summary>Phase 6 review item 1: the VERIFIED state LOCKS the number input and the country
    /// select (both disabled) and replaces "Send code" with a Secondary "Change number" action.</summary>
    [Fact]
    public void PhoneStep_VerifiedState_LocksNumberAndCountry_AndReplacesSendWithChangeNumber()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.PhoneNumber, "5550100")
            .Add(p => p.CodeSent, true)
            .Add(p => p.Verified, true));

        cut.Find("input.tm-signing-phone-step__input").HasAttribute("disabled").Should().BeTrue(
            "the verified number is locked");
        cut.Find(".tm-signing-phone-step__country select").HasAttribute("disabled").Should().BeTrue(
            "the country select is locked with the number");
        cut.FindAll(".tm-signing-phone-step__send").Should().BeEmpty("Send is replaced in the verified state");
        var change = cut.Find(".tm-signing-phone-step__change-number");
        change.TextContent.Should().Contain("Change number");
        change.ClassList.Should().Contain("tm-btn-secondary", "Change number is the Secondary action");
    }

    /// <summary>Phase 6 review item 1: Change number raises the callback (the runner clears its
    /// host confirmation) and unlocks the editing locally — no verified block, editable input.</summary>
    [Fact]
    public async Task PhoneStep_ChangeNumber_RaisesCallback_AndUnlocksControls()
    {
        var changeRequests = 0;
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.PhoneNumber, "5550100")
            .Add(p => p.CodeSent, true)
            .Add(p => p.Verified, true)
            .Add(p => p.OnChangeNumber, EventCallback.Factory.Create(this, () => changeRequests++)));

        cut.Find(".tm-signing-phone-step__change-number").Click();

        changeRequests.Should().Be(1, "the runner is asked to clear its confirmation");
        // The local unlock applies immediately (before the host's re-render).
        cut.Find("input.tm-signing-phone-step__input").HasAttribute("disabled").Should().BeFalse();
        cut.FindAll(".tm-signing-phone-step__verified").Should().BeEmpty();
        cut.Find(".tm-signing-phone-step__send").Should().NotBeNull("Send is back for the new number");
    }

    /// <summary>Phase 6 review item 9: once a code was sent the Send action is SECONDARY and
    /// disabled while the countdown runs; at zero it is enabled again (next to the Resend link).</summary>
    [Fact]
    public void PhoneStep_Send_IsSecondaryAndDisabledDuringCountdown_EnabledAtZero()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.PhoneNumber, "5550100")
            .Add(p => p.ResendSeconds, 30)
            .Add(p => p.OnSendCode, EventCallback.Factory.Create<string>(this, _ => { })));

        var send = cut.Find(".tm-signing-phone-step__send");
        send.ClassList.Should().Contain("tm-btn-primary", "the first send is the primary action");
        send.HasAttribute("disabled").Should().BeFalse();

        cut.Find(".tm-signing-phone-step__send").Click();

        send = cut.Find(".tm-signing-phone-step__send");
        send.ClassList.Should().Contain("tm-btn-secondary", "after a send the action is Secondary (resends go through Resend)");
        send.HasAttribute("disabled").Should().BeTrue("the countdown throttles the send");

        time.Advance(TimeSpan.FromSeconds(30));
        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-phone-step__send").HasAttribute("disabled").Should().BeFalse(
                "at zero the countdown no longer throttles the send");
        });
    }

    /// <summary>Phase 6 review item 5: a code cannot be sent to an EMPTY number — the callback is
    /// never raised (the required message explains the empty required field).</summary>
    [Fact]
    public void PhoneStep_SendCode_EmptyNumber_ReturnsEarlyWithoutRaisingTheCallback()
    {
        var sends = 0;
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.OnSendCode, EventCallback.Factory.Create<string>(this, _ => sends++)));

        cut.Find(".tm-signing-phone-step__send").Click();

        sends.Should().Be(0, "an empty number never reaches the host");
        cut.FindAll(".tm-signing-phone-step__otp").Should().BeEmpty("no OTP row without a send");
    }

    /// <summary>Phase 6 review item 5: a THROWING host shows the localized "verification
    /// unavailable" message, starts NO countdown and fakes NO progress.</summary>
    [Fact]
    public void PhoneStep_SendCallbackThrows_ShowsUnavailableMessage_AndStartsNoCountdown()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.PhoneNumber, "5550100")
            .Add(p => p.ResendSeconds, 30)
            .Add(p => p.OnSendCode, EventCallback.Factory.Create<string>(this, _ => throw new InvalidOperationException("host down"))));

        cut.Find(".tm-signing-phone-step__send").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("temporarily unavailable",
                "the localized unavailable message replaces any fake progress");
        });
        cut.FindAll(".tm-signing-phone-step__otp").Should().BeEmpty("a failed send shows no OTP row");

        time.Advance(TimeSpan.FromSeconds(60));
        cut.FindAll(".tm-signing-phone-step__resend-button").Should().BeEmpty(
            "no countdown was started for a failed send");
    }

    /// <summary>Phase 6 review item 7 (WCAG 4.1.3): ONE always-rendered polite live region carries
    /// the phase events ("Code sent" on send, "Resend available" at zero, "Verified" on the host
    /// confirmation); the ticking countdown text is aria-live="off" and the verified block carries
    /// NO role itself.</summary>
    [Fact]
    public void PhoneStep_Announcements_RunThroughOneAlwaysRenderedLiveRegion()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.PhoneNumber, "5550100")
            .Add(p => p.ResendSeconds, 2)
            .Add(p => p.OnSendCode, EventCallback.Factory.Create<string>(this, _ => { })));

        // Always rendered — even before any event.
        cut.FindAll("[role='status'][aria-live='polite']").Should().ContainSingle(
            "exactly one live region exists before any announcement");

        cut.Find(".tm-signing-phone-step__send").Click();
        cut.FindAll("[role='status'][aria-live='polite']").Should().ContainSingle("still exactly one region");
        cut.Find("[role='status'][aria-live='polite']").TextContent.Should().Contain("Code sent");

        cut.Find(".tm-signing-phone-step__resend").HasAttribute("aria-live").Should().BeTrue();
        cut.Find(".tm-signing-phone-step__resend").GetAttribute("aria-live").Should().Be("off",
            "the ticking countdown must not announce every second");

        time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() =>
        {
            cut.Find("[role='status'][aria-live='polite']").TextContent.Should().Contain("Resend available");
        });

        // The host confirms: the verified block carries no role; the live region announces.
        cut.Render(parameters => parameters.Add(p => p.Verified, true));
        cut.FindAll(".tm-signing-phone-step__verified[role='status']").Should().BeEmpty(
            "the verified block is presentation only — the live region owns announcements");
        cut.Find("[role='status'][aria-live='polite']").TextContent.Should().Contain("Verified");
    }

    /// <summary>Phase 6 review item 8: the OTP error is owned by the OTP INPUT — aria-invalid and
    /// the error state land on the OTP input, the phone input stays clean.</summary>
    [Fact]
    public void PhoneStep_OtpInvalid_ErrorOwnedByTheOtpInput_PhoneInputStaysClean()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.OtpCode, "000000")
            .Add(p => p.OtpInvalid, true));

        cut.Find("input.tm-signing-phone-step__otp").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find("input.tm-signing-phone-step__otp").GetAttribute("aria-describedby").Should().NotBeNullOrWhiteSpace(
            "the OTP input references the validation message");
        cut.Find("input.tm-signing-phone-step__input").HasAttribute("aria-invalid").Should().BeFalse(
            "the phone input is clean while the error belongs to the OTP");
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

    // ── Fáze 5 (TmOtpInput integration): CodeInputMode=Segmented swaps the single TmTextInput
    //    for the core TmOtpInput; CodeLength (default 6) drives the cell count. ──

    [Fact]
    public void PhoneStep_CodeInputModeSegmented_RendersTmOtpInput_WithCodeLengthCells()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented));

        cut.Find(".tm-signing-phone-step__otp-row .tm-otp-input").GetAttribute("role").Should().Be("group");
        cut.FindAll(".tm-otp-input__cell").Should().HaveCount(6, "CodeLength defaults to 6");
        cut.FindAll("input.tm-signing-phone-step__otp").Should().BeEmpty(
            "segmented mode replaces the single text input");
    }

    [Fact]
    public void PhoneStep_CodeInputModeSegmented_CodeLengthControlsTheCellCount()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.CodeLength, 4));

        cut.FindAll(".tm-otp-input__cell").Should().HaveCount(4);
    }

    [Fact]
    public void PhoneStep_DefaultMode_KeepsTheSingleTextInput()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true));

        cut.Find("input.tm-signing-phone-step__otp").Should().NotBeNull();
        cut.FindAll(".tm-otp-input").Should().BeEmpty("Single mode is today's TmTextInput");
    }

    /// <summary>Fáze 5: in Segmented mode completing the code (TmOtpInput.OnComplete) automatically
    /// runs the verification when a verifier is wired — no manual Verify button exists there.</summary>
    [Fact]
    public async Task PhoneStep_Segmented_CompletingTheCode_AutoVerifies_WhenVerifierWired()
    {
        var verifyRequests = 0;
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.OnVerifyCode, EventCallback.Factory.Create(this, () => verifyRequests++)));

        var otp = cut.FindComponent<TmOtpInput>();
        await cut.InvokeAsync(() => otp.Instance.DistributeFromAsync(0, "123456"));

        verifyRequests.Should().Be(1, "completion with a wired verifier starts the verification");
        cut.Find(".tm-signing-phone-step__verify").Should().NotBeNull(
            "fáze 5 review: the manual Verify action stays visible in segmented mode too");
    }

    [Fact]
    public async Task PhoneStep_Segmented_CompletingTheCode_WithoutVerifier_DoesNotVerify()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented));

        var otp = cut.FindComponent<TmOtpInput>();
        await cut.InvokeAsync(() => otp.Instance.DistributeFromAsync(0, "123456"));

        cut.FindAll(".tm-signing-phone-step__verify").Should().BeEmpty(
            "without a verifier the code stays cosmetic, exactly the documented legacy behaviour");
    }

    /// <summary>Fáze 5: the host-confirmed state drives the TmOtpInput Verified state (readonly
    /// cells + the localized verified status) instead of the single-input verified block.</summary>
    [Fact]
    public void PhoneStep_Segmented_Verified_RendersTheOtpVerifiedState()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.Verified, true));

        cut.Find(".tm-otp-input").ClassList.Should().Contain("tm-otp-input--verified");
        cut.FindAll(".tm-signing-phone-step__verify").Should().BeEmpty();
        cut.FindAll(".tm-signing-phone-step__resend").Should().BeEmpty();
        cut.FindAll(".tm-signing-phone-step__resend-button").Should().BeEmpty();
    }

    /// <summary>Fáze 5 review: the segmented verified status reuses the STEP's own
    /// <c>TmSigningStep_Verified</c> wording (the additive TmOtpInput VerifiedText) — and exactly
    /// ONE live text sounds, because the step skips its own "Verified" announcement in segmented
    /// mode (the TmOtpInput live region owns it).</summary>
    [Fact]
    public void PhoneStep_Segmented_Verified_ReusesTheStepWording_AndExactlyOneLiveText()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.Verified, true));

        cut.Find(".tm-otp-input__status").TextContent.Trim().Should().Be("Verified",
            "VerifiedText maps the input's verified status text to TmSigningStep_Verified");
        cut.FindAll("[role='status'][aria-live='polite']").Should().ContainSingle(region =>
            region.TextContent.Contains("Verified"),
            "exactly one live region announces the verified state — the step stays silent");
    }

    /// <summary>Fáze 5 review: while a verify runs the segmented cells turn READONLY (never
    /// disabled — the focus stays reachable) and the otp row carries aria-busy; both revert when
    /// the verify finishes.</summary>
    [Fact]
    public async Task PhoneStep_Segmented_WhileVerifying_CellsReadOnlyNotDisabled_AndRowAriaBusy()
    {
        var verifyGate = new TaskCompletionSource();
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.OnVerifyCode, EventCallback.Factory.Create(this, async () => await verifyGate.Task)));

        var otp = cut.FindComponent<TmOtpInput>();
        var distribution = cut.InvokeAsync(() => otp.Instance.DistributeFromAsync(0, "123456"));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-phone-step__otp-row").GetAttribute("aria-busy").Should().Be("true");
            cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(cell =>
                cell.HasAttribute("readonly") && !cell.HasAttribute("disabled"),
                "a verifying input is readonly, not disabled");
        });

        verifyGate.SetResult();
        await distribution;

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-phone-step__otp-row").HasAttribute("aria-busy").Should().BeFalse();
            cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(cell => !cell.HasAttribute("readonly"));
        });
    }

    /// <summary>Fáze 5 review: a completion raised while a verify is STILL in flight (the signer
    /// types fast) drives the same VerifyCodeAsync, which ignores the second call — exactly ONE
    /// verifier request fires.</summary>
    [Fact]
    public async Task PhoneStep_Segmented_CompletionDuringInFlightVerify_FiresOneVerifierCall()
    {
        var verifyCalls = 0;
        IRenderedComponent<TmOtpInput>? otp = null;
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.OnVerifyCode, EventCallback.Factory.Create(this, () =>
            {
                verifyCalls++;
                // The signer completes another code while this verify is still on the stack.
                return otp!.Instance.DistributeFromAsync(0, "654321");
            })));
        otp = cut.FindComponent<TmOtpInput>();

        await cut.InvokeAsync(() => otp.Instance.DistributeFromAsync(0, "123456"));

        verifyCalls.Should().Be(1, "a completion during an in-flight verify is ignored");
    }

    /// <summary>Fáze 5 review: a failed verification CLEARS the wrong draft and REFOCUSES the
    /// first cell, so the next full code fires OnComplete again — and a SECOND wrong code is a NEW
    /// rising edge that replays the shake (ShowErrorAsync → restartShake).</summary>
    [Fact]
    public async Task Runner_Segmented_FailedVerify_ClearsAndRefocuses_AndSecondWrongCodeReplaysTheShake()
    {
        var module = SetupOtpModule();
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreatePhoneField()])
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.Values, new Dictionary<string, object?> { ["runner-phone"] = "+420777123456" })
            .Add(p => p.OnVerifyPhoneCode, (Func<SigningPhoneVerificationRequest, Task<bool>>)(_ => Task.FromResult(false))));

        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-otp-input__cell").Should().HaveCount(6));
        var focusBefore = module.Invocations.Count(i => i.Identifier == "selectCell");

        var otp = cut.FindComponent<TmOtpInput>();
        await cut.InvokeAsync(() => otp.Instance.DistributeFromAsync(0, "111111"));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-otp-input").ClassList.Should().Contain("tm-otp-input--invalid");
            cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(cell =>
                string.IsNullOrEmpty(cell.GetAttribute("value")),
                "the failed verify clears the wrong draft");
            module.Invocations.Count(i => i.Identifier == "selectCell").Should().Be(focusBefore + 1,
                "the failure refocuses the first cell");
        });
        module.Invocations.Count(i => i.Identifier == "restartShake").Should().Be(1,
            "the first wrong code replays the shake once");

        // The runner cleared OtpInvalid on the new draft, so the next failure is a NEW rising edge.
        await cut.InvokeAsync(() => otp.Instance.DistributeFromAsync(0, "222222"));
        cut.WaitForAssertion(() =>
            module.Invocations.Count(i => i.Identifier == "restartShake").Should().Be(2,
                "every new wrong-code report replays the shake"));
    }

    /// <summary>Fáze 5 review: the segmented cells reference the step shell's DESCRIPTION id while
    /// no OTP error targets them.</summary>
    [Fact]
    public void PhoneStep_Segmented_CellsReferenceTheDescriptionId_WithoutAnOtpError()
    {
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Phone",
                Type = SigningFieldType.Phone,
                Description = "We will only use it for the signing code."
            })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented));

        var descriptionId = cut.Find(".tm-signing-step-shell__description").Id;
        cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(cell =>
            cell.GetAttribute("aria-describedby") == descriptionId);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    public void PhoneStep_Segmented_CodeLengthOutside4To10_Throws(int codeLength)
    {
        var act = () => Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.CodeLength, codeLength));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("CodeLength");
    }

    [Fact]
    public void PhoneStep_Single_CodeLength_IsIgnored()
    {
        var act = () => Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.CodeLength, 11));

        act.Should().NotThrow("Single mode never renders the TmOtpInput, so CodeLength is inert");
    }

    private BunitJSModuleInterop SetupOtpModule()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-otp-input.js");
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("setCellValue", _ => true).SetVoidResult();
        module.SetupVoid("setCellValues", _ => true).SetVoidResult();
        module.SetupVoid("restartShake", _ => true).SetVoidResult();
        module.SetupVoid("focusCell", _ => true).SetVoidResult();
        module.SetupVoid("selectCell", _ => true).SetVoidResult();
        return module;
    }

    /// <summary>Fáze 5 runner path: a failed verification maps OtpInvalid → TmOtpInput Invalid;
    /// the step shell keeps OWNING the error text (one alert in every mode) and the OTP input
    /// references it through aria-describedby.</summary>
    [Fact]
    public void Runner_Segmented_VerifyFailure_OtpInputOwnsTheInvalidState()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreatePhoneField()])
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.Values, new Dictionary<string, object?> { ["runner-phone"] = "+420777123456" })
            .Add(p => p.OnVerifyPhoneCode, (Func<SigningPhoneVerificationRequest, Task<bool>>)(_ => Task.FromResult(false))));

        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-otp-input__cell").Should().HaveCount(6));

        var otp = cut.FindComponent<TmOtpInput>();
        cut.InvokeAsync(() => otp.Instance.DistributeFromAsync(0, "000000")).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-otp-input").ClassList.Should().Contain("tm-otp-input--invalid");
            var validationId = cut.Find(".tm-signing-step-shell__validation").Id;
            validationId.Should().NotBeNullOrEmpty();
            cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("not valid");
            cut.Find(".tm-otp-input").GetAttribute("aria-describedby").Should().Contain(validationId,
                "the OTP input references the shell-owned error text");
            cut.FindAll(".tm-otp-input__cell").Should().OnlyContain(cell =>
                cell.GetAttribute("aria-describedby") == validationId,
                "every cell references the shell-owned validation id while the OTP error targets them");
        });
    }

    /// <summary>Fáze 5: the runner forwards the additive CodeInputMode / CodeLength parameters.</summary>
    [Fact]
    public void Runner_ForwardsCodeInputModeAndCodeLength_ToThePhoneStep()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreatePhoneField()])
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.CodeLength, 4)
            .Add(p => p.Values, new Dictionary<string, object?> { ["runner-phone"] = "+420777123456" }));

        cut.Find(".tm-signing-phone-step__send").Click();

        cut.WaitForAssertion(() => cut.FindAll(".tm-otp-input__cell").Should().HaveCount(4));
    }

    /// <summary>Fáze 5 resend contract: a new code invalidates the old draft — the segmented input is
    /// cleared (ClearAsync) and refocused (FocusAsync), and the live region still announces the
    /// "Code sent" event.</summary>
    [Fact]
    public void PhoneStep_Segmented_Resend_ClearsTheDraft_AndKeepsTheAnnouncement()
    {
        string? draft = "123456";
        var cut = Render<TmSigningPhoneStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Phone", Type = SigningFieldType.Phone })
            .Add(p => p.CodeSent, true)
            .Add(p => p.ResendSeconds, 0)
            .Add(p => p.PhoneNumber, "777123456")
            .Add(p => p.OtpCode, "123456")
            .Add(p => p.CodeInputMode, SigningCodeInputMode.Segmented)
            .Add(p => p.OtpCodeChanged, EventCallback.Factory.Create<string?>(this, value => draft = value))
            .Add(p => p.OnSendCode, EventCallback.Factory.Create<string>(this, _ => { })));

        cut.FindAll(".tm-otp-input__cell").Should().HaveCount(6);
        cut.Find(".tm-signing-phone-step__resend-button").Click();

        cut.WaitForAssertion(() =>
        {
            draft.Should().Be("", "the resend clears the previous draft through ClearAsync");
            cut.FindAll(".tm-otp-input__cell").Select(cell => cell.GetAttribute("value")).Should().OnlyContain(v => string.IsNullOrEmpty(v));
            cut.FindAll("[role='status'][aria-live='polite']").Select(region => region.TextContent)
                .Should().Contain(text => text.Contains("Code sent"));
        });
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
