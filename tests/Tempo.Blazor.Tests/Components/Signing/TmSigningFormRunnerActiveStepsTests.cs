using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// The active-steps contract (Signing redesign phase 6, S2-5/SI2): the phone OTP, the attachment
/// upload and the external verification become real, HOST-CONFIRMED steps — but only when the
/// host wires the callbacks (opt-in). Every contract test here has a legacy twin: without the
/// callbacks today's documented "click = done" behaviour must keep working unchanged.
/// </summary>
public class TmSigningFormRunnerActiveStepsTests : LocalizationTestBase
{
    /// <summary>
    /// With a host verifier wired, the runner asks the HOST about every typed code. A rejected
    /// code shows the localized validation and never completes the step; an accepted code shows
    /// the verified state and completes it. The countdown runs on a fake clock, so no timer tick
    /// can interleave with the assertions — and every post-click assertion is reached through
    /// <c>WaitForElement</c> / <c>WaitForAssertion</c> so the assertions cannot race the
    /// dispatcher under a parallel run (phase 6 review item 6).
    /// </summary>
    [Fact]
    public async Task Runner_OnVerifyPhoneCode_False_ShowsValidation_True_CompletesStep()
    {
        Services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
        SigningPhoneVerificationRequest? verifiedRequest = null;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("phone", "Phone", SigningFieldType.Phone, required: true)])
            .Add(p => p.OnVerifyPhoneCode, request =>
            {
                verifiedRequest = request;
                return Task.FromResult(request.Code == "123456");
            }));

        cut.Find("input.tm-signing-phone-step__input").Input("5550100");
        cut.Find(".tm-signing-phone-step__send").Click();

        cut.WaitForElement("input.tm-signing-phone-step__otp").Should().NotBeNull(
            "the send raised the OTP row");

        cut.Find("input.tm-signing-phone-step__otp").Input("000000");
        cut.Find(".tm-signing-phone-step__verify").Click();

        cut.WaitForAssertion(() =>
        {
            verifiedRequest.Should().NotBeNull("the typed code is verified against the HOST, never locally");
            verifiedRequest!.Code.Should().Be("000000");
            verifiedRequest.Phone.Should().StartWith("+").And.EndWith("5550100",
                "the normalized phone the code was sent to (the calling code follows the runner's culture)");
            cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("not valid",
                "a rejected code shows the localized invalid-code message");
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue(
                "a rejected code must not complete the phone step");
            // Phase 6 review item 8: the error is owned by the OTP input.
            cut.Find("input.tm-signing-phone-step__otp").GetAttribute("aria-invalid").Should().Be("true");
            cut.Find("input.tm-signing-phone-step__input").HasAttribute("aria-invalid").Should().BeFalse(
                "the phone input stays clean while the error belongs to the OTP");
        });

        cut.Find("input.tm-signing-phone-step__otp").Input("123456");
        cut.Find(".tm-signing-phone-step__verify").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-phone-step__verified").Should().NotBeNull(
                "an accepted code flips the step into the verified state");
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeFalse(
                "the host-confirmed phone completes the required step");
            // Phase 6 review item 8: the accepted code cleared the old OTP error message.
            cut.FindAll(".tm-signing-step-shell__validation").Should().BeEmpty(
                "the accepted code cleared the OTP error");
        });
    }

    /// <summary>Phase 6 review item 5: a THROWING verifier never records a confirmation — the
    /// localized "verification unavailable" message shows and the step stays incomplete.</summary>
    [Fact]
    public async Task Runner_OnVerifyPhoneCode_Throws_ShowsUnavailableMessage_NeverVerifies()
    {
        Services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("phone", "Phone", SigningFieldType.Phone, required: true)])
            .Add(p => p.OnVerifyPhoneCode, _ => throw new InvalidOperationException("idp down")));

        cut.Find("input.tm-signing-phone-step__input").Input("5550100");
        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForElement("input.tm-signing-phone-step__otp");

        cut.Find("input.tm-signing-phone-step__otp").Input("123456");
        cut.Find(".tm-signing-phone-step__verify").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("temporarily unavailable");
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue(
                "a failing host never completes the step");
        });
        await Task.CompletedTask;
    }

    /// <summary>Phase 6 review item 1: the confirmation is bound to the VERIFIED NUMBER. After a
    /// verified step, a different phone value makes the step INCOMPLETE again; "Change number"
    /// clears the host confirmation (unlocking the editing) and only a fresh verification
    /// completes the step again.</summary>
    [Fact]
    public async Task Runner_PhoneVerified_ThenNumberEdited_StepIncompleteAgain()
    {
        Services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("phone", "Phone", SigningFieldType.Phone, required: true)])
            .Add(p => p.OnVerifyPhoneCode, request => Task.FromResult(request.Code == "123456")));

        cut.Find("input.tm-signing-phone-step__input").Input("5550100");
        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForElement("input.tm-signing-phone-step__otp");
        cut.Find("input.tm-signing-phone-step__otp").Input("123456");
        cut.Find(".tm-signing-phone-step__verify").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-phone-step__verified").Should().NotBeNull();
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeFalse();
        });

        // The verified state LOCKS the editing; "Change number" is the way out.
        cut.Find(".tm-signing-phone-step__change-number").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-signing-phone-step__verified").Should().BeEmpty(
                "the host confirmation is cleared");
            cut.Find("input.tm-signing-phone-step__input").HasAttribute("disabled").Should().BeFalse(
                "the number input unlocks for the new number");
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue(
                "after clearing the confirmation the step is incomplete again");
        });

        // A DIFFERENT number does not accidentally resurrect the cleared confirmation: type it,
        // send and verify again — only the fresh confirmation completes the step.
        cut.Find("input.tm-signing-phone-step__input").Input("5550200");
        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForElement("input.tm-signing-phone-step__otp");
        cut.Find("input.tm-signing-phone-step__otp").Input("123456");
        cut.Find(".tm-signing-phone-step__verify").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-phone-step__verified").Should().NotBeNull();
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeFalse();
        });

        // A host-pushed value change (a DIFFERENT number) voids the binding: the step is
        // incomplete even though a confirmation was recorded — the number does not match it.
        cut.Render(parameters => parameters
            .Add(p => p.Values, new Dictionary<string, object?> { ["phone"] = "+1555099999" }));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-signing-phone-step__verified").Should().BeEmpty(
                "the confirmation is bound to the verified number — a different number voids it");
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue();
        });
    }

    /// <summary>Phase 6 review item 12: the code-sent state and the typed OTP draft are kept PER
    /// FIELD by the runner — navigating away and back restores the OTP row and the draft.</summary>
    [Fact]
    public async Task Runner_PhoneCodeSent_AndOtpDraft_SurviveNavigation()
    {
        Services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("phone", "Phone", SigningFieldType.Phone, y: 0.1, required: true),
                CreateField("name", "Name", SigningFieldType.Text, y: 0.5)
            ])
            .Add(p => p.OnVerifyPhoneCode, _ => Task.FromResult(true)));

        cut.Find("input.tm-signing-phone-step__input").Input("5550100");
        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForElement("input.tm-signing-phone-step__otp");
        cut.Find("input.tm-signing-phone-step__otp").Input("998877");

        // Navigate away and back through the permanent field list.
        await cut.Find(".tm-signing-form-runner__field-button[data-field-uuid='name']").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => cut.FindAll("input.tm-signing-phone-step__otp").Should().BeEmpty("we are on the other step"));

        await cut.Find(".tm-signing-form-runner__field-button[data-field-uuid='phone']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            cut.Find("input.tm-signing-phone-step__otp").GetAttribute("value").Should().Be("998877",
                "the runner kept the OTP draft");
            cut.Find(".tm-signing-phone-step__send").HasAttribute("disabled").Should().BeTrue(
                "the countdown state survived too (the send stays throttled)");
        });
        await Task.CompletedTask;
    }

    /// <summary>Phase 6 review item 12 (hardening): the countdown TICKS in the runner — ordinary
    /// re-renders while unverified (each keystroke re-renders the runner) must not stop it, and at
    /// zero the real Resend action appears.</summary>
    [Fact]
    public async Task Runner_PhoneCountdown_TicksAcrossOrdinaryRerenders()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("phone", "Phone", SigningFieldType.Phone, required: true)])
            .Add(p => p.OnVerifyPhoneCode, _ => Task.FromResult(true)));

        cut.Find("input.tm-signing-phone-step__input").Input("5550100");
        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForElement("input.tm-signing-phone-step__otp");

        // Ordinary re-renders while the step is unverified must not stop the countdown.
        cut.Find("input.tm-signing-phone-step__input").Input("5550100");

        time.Advance(TimeSpan.FromSeconds(30));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-phone-step__resend-button").Should().NotBeNull(
                "the countdown survived the re-renders and reached zero");
            cut.FindAll(".tm-signing-phone-step__resend").Should().BeEmpty("the ticking text is gone at zero");
        });
    }

    /// <summary>Phase 6 review item 10: the raised host requests carry a RUNNER-OWNED
    /// cancellation token — disposed runner cancels it, so a host op in flight can stop.</summary>
    [Fact]
    public async Task Runner_ActiveStepRequests_CarryCancellableRunnerToken()
    {
        var phoneToken = default(CancellationToken);
        var uploadToken = default(CancellationToken);
        var externalToken = default(CancellationToken);
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("phone", "Phone", SigningFieldType.Phone, required: true),
                CreateField("photo", "Photo", SigningFieldType.Image, required: true),
                CreateField("verify", "ID check", SigningFieldType.Verification, required: true)
            ])
            .Add(p => p.OnVerifyPhoneCode, request => { phoneToken = request.CancellationToken; return Task.FromResult(true); })
            .Add(p => p.OnAttachmentSelected, request => { uploadToken = request.CancellationToken; return Task.FromResult<string?>("https://host.test/f.png"); })
            .Add(p => p.OnExternalStepRequested, EventCallback.Factory
                .Create<SigningExternalStepRequest>(this, request => externalToken = request.CancellationToken)));

        cut.Find("input.tm-signing-phone-step__input").Input("5550100");
        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForElement("input.tm-signing-phone-step__otp");
        cut.Find("input.tm-signing-phone-step__otp").Input("123456");
        cut.Find(".tm-signing-phone-step__verify").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-signing-phone-step__verified").Should().NotBeNull());

        // The attachment step renders only while CURRENT: navigate to it first.
        await cut.Find(".tm-signing-form-runner__field-button[data-field-uuid='photo']").ClickAsync(new MouseEventArgs());
        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("f.png", "image/png", TestBrowserFile.PngBytes)])));

        await cut.Find(".tm-signing-form-runner__field-button[data-field-uuid='verify']").ClickAsync(new MouseEventArgs());
        await cut.InvokeAsync(() => cut.Find(".tm-signing-external-step__start-button").Click());

        phoneToken.Should().NotBe(default(CancellationToken), "the verify request carries the runner token");
        uploadToken.Should().NotBe(default(CancellationToken), "the upload request carries the runner token");
        externalToken.Should().NotBe(default(CancellationToken), "the external request carries the runner token");

        // The runner-owned source is cancelled in DisposeAsync.
        await ((IAsyncDisposable)cut).DisposeAsync();
        phoneToken.IsCancellationRequested.Should().BeTrue("the runner cancels the token on dispose");
        uploadToken.IsCancellationRequested.Should().BeTrue("the runner cancels the token on dispose");
        externalToken.IsCancellationRequested.Should().BeTrue("the runner cancels the token on dispose");
    }

    /// <summary>Phase 6 review item 10 (hardened): the cancellation is ONE source PER
    /// FIELD+OPERATION — a new request cancels only its own slot's predecessor, never the
    /// previous request globally (the old single-source design cancelled a send on field A just
    /// because a verify was raised on field B). DisposeAsync cancels ALL live slots.</summary>
    [Fact]
    public async Task Runner_ActiveStepCancellation_IsPerFieldAndOperation()
    {
        Services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
        var sendPhoneAToken = default(CancellationToken);
        var sendPhoneBToken = default(CancellationToken);
        var verifyToken = default(CancellationToken);
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("phone-a", "Phone A", SigningFieldType.Phone, y: 0.1, required: true),
                CreateField("phone-b", "Phone B", SigningFieldType.Phone, y: 0.5, required: true)
            ])
            .Add(p => p.OnSendPhoneCode, EventCallback.Factory.Create<SigningPhoneCodeRequest>(this, request =>
            {
                if (request.FieldUuid == "phone-a")
                {
                    sendPhoneAToken = request.CancellationToken;
                }
                else
                {
                    sendPhoneBToken = request.CancellationToken;
                }
            }))
            .Add(p => p.OnVerifyPhoneCode, request =>
            {
                verifyToken = request.CancellationToken;
                return Task.FromResult(true);
            }));

        // A completed send on phone-a…
        cut.Find("input.tm-signing-phone-step__input").Input("5550100");
        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForElement("input.tm-signing-phone-step__otp");
        sendPhoneAToken.CanBeCanceled.Should().BeTrue("the send request carries its own token");

        // …then a send on phone-b: ANOTHER field. The old global source cancelled phone-a's
        // token here; per field+operation it must stay alive.
        await cut.Find(".tm-signing-form-runner__field-button[data-field-uuid='phone-b']").ClickAsync(new MouseEventArgs());
        cut.Find("input.tm-signing-phone-step__input").Input("5550200");
        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForAssertion(() => sendPhoneBToken.Should().NotBe(default(CancellationToken)));
        sendPhoneAToken.IsCancellationRequested.Should().BeFalse(
            "a request on ANOTHER field never cancels the previous request");

        // …and a verify on phone-a: SAME field, DIFFERENT operation. Only the same slot's newer
        // request supersedes its predecessor — the send token still stays alive.
        await cut.Find(".tm-signing-form-runner__field-button[data-field-uuid='phone-a']").ClickAsync(new MouseEventArgs());
        cut.Find("input.tm-signing-phone-step__otp").Input("123456");
        cut.Find(".tm-signing-phone-step__verify").Click();
        cut.WaitForAssertion(() =>
        {
            verifyToken.Should().NotBe(default(CancellationToken));
            cut.Find(".tm-signing-phone-step__verified").Should().NotBeNull();
        });
        sendPhoneAToken.IsCancellationRequested.Should().BeFalse(
            "a newer request for a DIFFERENT operation on the same field never cancels the send token");

        // DisposeAsync cancels EVERY live slot.
        await ((IAsyncDisposable)cut).DisposeAsync();
        sendPhoneAToken.IsCancellationRequested.Should().BeTrue("the runner cancels all slots on dispose");
        sendPhoneBToken.IsCancellationRequested.Should().BeTrue("the runner cancels all slots on dispose");
        verifyToken.IsCancellationRequested.Should().BeTrue("the runner cancels all slots on dispose");
    }

    /// <summary>Phase 6 review fix: the wired <c>OnSendPhoneCode</c> receives the
    /// <see cref="SigningPhoneCodeRequest"/> with the field's UUID and the NORMALIZED number
    /// (calling code + digits-only — what an SMS provider dials), carrying a cancellable
    /// runner-owned token.</summary>
    [Fact]
    public void Runner_OnSendPhoneCode_ReceivesFieldUuidAndNormalizedPhone()
    {
        Services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
        SigningPhoneCodeRequest? sent = null;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("runner-phone", "Phone", SigningFieldType.Phone, required: true)])
            .Add(p => p.OnSendPhoneCode, EventCallback.Factory
                .Create<SigningPhoneCodeRequest>(this, request => sent = request)));

        cut.Find(".tm-signing-phone-step__country select").Change("+420");
        cut.Find("input.tm-signing-phone-step__input").Input("777 123 456");
        cut.Find(".tm-signing-phone-step__send").Click();

        cut.WaitForAssertion(() => sent.Should().NotBeNull("the send reaches the wired host"));
        sent!.FieldUuid.Should().Be("runner-phone");
        sent.Phone.Should().Be("+420777123456",
            "the normalized number (calling code + digits only) is what an SMS provider dials");
        sent.CancellationToken.CanBeCanceled.Should().BeTrue("the request carries the runner-owned token");

        cut.WaitForElement("input.tm-signing-phone-step__otp");
    }

    /// <summary>Phase 6 review fix: when the host's <c>OnSendPhoneCode</c> THROWS, the runner
    /// never marks the send — and the runner-hosted step keys its OTP row, its countdown and its
    /// "Code sent" announcement ONLY off the host-reported sent state, so none of them appear
    /// (standalone steps catch the same throw locally — unchanged). A successful retry recovers.</summary>
    [Fact]
    public void Runner_OnSendPhoneCode_Throws_NoOtpRowNoCountdown()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var attempts = 0;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("phone", "Phone", SigningFieldType.Phone, required: true)])
            .Add(p => p.OnSendPhoneCode, EventCallback.Factory.Create<SigningPhoneCodeRequest>(this, _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("sms gateway down");
                }
            }))
            .Add(p => p.OnVerifyPhoneCode, _ => Task.FromResult(true)));

        cut.Find("input.tm-signing-phone-step__input").Input("5550100");
        cut.Find(".tm-signing-phone-step__send").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("temporarily unavailable",
                "the failing host send shows the localized unavailable message");
        });
        cut.FindAll("input.tm-signing-phone-step__otp").Should().BeEmpty("a failed host send shows NO OTP row");
        cut.FindAll(".tm-signing-phone-step__resend").Should().BeEmpty("no countdown text for a failed send");
        cut.Find(".tm-signing-phone-step [role='status'][aria-live='polite']").TextContent.Should().NotContain("Code sent",
            "a failed host send is never announced");
        cut.Find(".tm-signing-phone-step__send").HasAttribute("disabled").Should().BeFalse(
            "no countdown throttles the send after a failure — the signer can retry");
        cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue(
            "a failed send never completes the step");

        // No countdown was started: advancing past the whole window must not conjure a resend.
        time.Advance(TimeSpan.FromSeconds(60));
        cut.FindAll(".tm-signing-phone-step__resend-button").Should().BeEmpty(
            "no countdown was running for a failed send");

        // The retry succeeds: only THEN does the host-reported sent state raise the OTP row.
        cut.Find(".tm-signing-phone-step__send").Click();
        cut.WaitForElement("input.tm-signing-phone-step__otp");
        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-phone-step__resend").TextContent.Should().Contain("30",
                "the countdown starts with the host-accepted retry, not before");
            cut.Find(".tm-signing-phone-step [role='status'][aria-live='polite']").TextContent.Should().Contain("Code sent");
        });
    }

    /// <summary>LEGACY (regression): without a host verifier, typing a phone number alone still
    /// completes the step — the documented placeholder behaviour from phase 1.</summary>
    [Fact]
    public void Runner_NoPhoneCallbacks_KeepsLegacyCompletion()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("phone", "Phone", SigningFieldType.Phone, required: true)]));

        cut.Find("input.tm-signing-phone-step__input").Input("5550100");

        cut.FindAll(".tm-signing-phone-step__verify").Should().BeEmpty(
            "no host verifier — no Verify action");
        cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeFalse(
            "legacy behaviour: the typed phone completes the step without any OTP check");
    }

    /// <summary>
    /// The runner hands the picked <see cref="IBrowserFile"/> to the HOST and stores the returned
    /// url on the attachment — the file content is read by the host (Blazor Server streams browser
    /// files over SignalR with tight message limits, so the step itself never reads the content).
    /// </summary>
    [Fact]
    public async Task Runner_OnAttachmentSelected_ReceivesBrowserFile_HostReturnsUrl()
    {
        const string hostUrl = "https://host.test/uploads/photo.png";
        SigningAttachmentUploadRequest? uploadRequest = null;
        IReadOnlyDictionary<string, object?>? published = null;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("photo", "Photo", SigningFieldType.Image, required: true)])
            .Add(p => p.OnAttachmentSelected, request =>
            {
                uploadRequest = request;
                return Task.FromResult<string?>(hostUrl);
            })
            .Add(p => p.ValuesChanged, EventCallback.Factory
                .Create<IReadOnlyDictionary<string, object?>>(this, values => published = values)));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("photo.png", "image/png", TestBrowserFile.PngBytes)])));

        uploadRequest.Should().NotBeNull("the picked file is handed to the host");
        uploadRequest!.FieldUuid.Should().Be("photo");
        uploadRequest.File.Name.Should().Be("photo.png");
        uploadRequest.File.Size.Should().Be(TestBrowserFile.PngBytes.Length);

        var attachment = published!["photo"] as IReadOnlyList<TmSigningStepAttachment>;
        attachment.Should().NotBeNull().And.ContainSingle();
        attachment![0].Url.Should().Be(hostUrl, "the host-returned url is stored on the attachment");
        cut.Find("img.tm-signing-attachment-step__preview").GetAttribute("src").Should().Be(hostUrl,
            "the card previews the uploaded file through the host url");
    }

    /// <summary>Phase 6 review item 2: a host resolver returning NULL rejects the file — nothing
    /// is stored, the localized failure shows and the required step stays incomplete.</summary>
    [Fact]
    public async Task Runner_OnAttachmentSelected_NullResult_RejectsFile_StepIncomplete()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("photo", "Photo", SigningFieldType.Image, required: true)])
            .Add(p => p.OnAttachmentSelected, _ => Task.FromResult<string?>(null)));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("photo.png", "image/png", TestBrowserFile.PngBytes)])));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("could not be uploaded");
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue(
                "a rejected file never completes the step");
        });
    }

    /// <summary>Phase 6 review item 2: a THROWING host rejects the file with the localized error —
    /// the runner does not crash and the step stays incomplete.</summary>
    [Fact]
    public async Task Runner_OnAttachmentSelected_Throws_ShowsError_NoCrash()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("photo", "Photo", SigningFieldType.Image, required: true)])
            .Add(p => p.OnAttachmentSelected, _ => throw new InvalidOperationException("storage down")));

        await cut.InvokeAsync(() => cut.FindComponent<TmFileDropZone>().Instance.OnFilesSelected.InvokeAsync(
            new InputFileChangeEventArgs([new TestBrowserFile("photo.png", "image/png", TestBrowserFile.PngBytes)])));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("could not be uploaded");
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue();
        });
    }

    /// <summary>The runner feeds the host's <c>StepContextProvider</c> into the external step:
    /// the provider page url and provider name render for a verification field, the KBA questions
    /// for a knowledge-check field, and a loading or error context takes over the step body /
    /// validation.</summary>
    [Fact]
    public void Runner_ExternalStepContext_ProvidesUrlQuestionsLoadingError()
    {
        var url = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("verify", "ID check", SigningFieldType.Verification, required: true)])
            .Add(p => p.StepContextProvider, field => new SigningExternalStepContext(
                ExternalUrl: "https://provider.test/verify",
                ProviderName: "Acme Check")));

        url.Find(".tm-signing-external-step__link").GetAttribute("href").Should().Be("https://provider.test/verify");
        url.Find(".tm-signing-external-step__provider").TextContent.Should().Contain("Acme Check");

        var kba = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("kba", "KBA", SigningFieldType.Kba, required: true)])
            .Add(p => p.StepContextProvider, field => new SigningExternalStepContext(
                Questions: ["What city?"])));

        kba.Find(".tm-signing-external-step__questions").TextContent.Should().Contain("What city?");

        var loading = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("verify", "ID check", SigningFieldType.Verification, required: true)])
            .Add(p => p.StepContextProvider, field => new SigningExternalStepContext(
                IsLoading: true,
                Error: "Provider down",
                ProviderName: "Acme Check")));

        loading.Find(".tm-signing-external-step__loading").Should().NotBeNull("the context's loading state takes over the body");
        loading.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("Provider down");
    }

    /// <summary>OPT-IN: with <c>OnExternalStepRequested</c> wired the click only NOTIFIES the host
    /// — the step completes solely after the host stores the provider result through
    /// <c>Values</c> as <c>true</c> or the string <c>"true"</c>.</summary>
    [Fact]
    public void Runner_ExternalWithHostCallback_CompletesOnlyAfterHostConfirms()
    {
        SigningExternalStepRequest? requested = null;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("verify", "ID check", SigningFieldType.Verification, required: true)])
            .Add(p => p.OnExternalStepRequested, EventCallback.Factory
                .Create<SigningExternalStepRequest>(this, request => requested = request)));

        cut.Find(".tm-signing-external-step__start-button").Click();

        requested.Should().NotBeNull("the click is forwarded to the host");
        requested!.Field.Uuid.Should().Be("verify");
        requested.Answers.Should().BeEmpty("a plain verification start carries no answers");
        cut.FindAll(".tm-signing-external-step__verified").Should().BeEmpty(
            "the runner must not show a verified state it did not receive");
        cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue(
            "the host-owned flow never completes the step by itself");

        // The host confirms with the bool true: the provider result arrives through Values.
        cut.Render(parameters => parameters
            .Add(p => p.Values, new Dictionary<string, object?> { ["verify"] = true }));

        cut.Find(".tm-signing-external-step__verified").Should().NotBeNull();
        cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeFalse();
    }

    /// <summary>Phase 6 review item 3: with the host callback wired ONLY the confirmed provider
    /// result completes the step — the string "true" (case-insensitive) does, the string
    /// "false" does NOT.</summary>
    [Fact]
    public void Runner_ExternalHostStoresFalseString_StepIncomplete()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("verify", "ID check", SigningFieldType.Verification, required: true)])
            .Add(p => p.OnExternalStepRequested, EventCallback.Factory
                .Create<SigningExternalStepRequest>(this, _ => { })));

        cut.Find(".tm-signing-external-step__start-button").Click();
        cut.Render(parameters => parameters
            .Add(p => p.Values, new Dictionary<string, object?> { ["verify"] = "false" }));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-signing-external-step__verified").Should().BeEmpty(
                "the string \"false\" is not a provider confirmation");
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue(
                "a stored \"false\" keeps the required step incomplete");
        });

        cut.Render(parameters => parameters
            .Add(p => p.Values, new Dictionary<string, object?> { ["verify"] = "TRUE" }));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-signing-external-step__verified").Should().NotBeNull(
                "the string \"true\" (case-insensitive) is the confirmed shape");
            cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeFalse();
        });
    }

    /// <summary>LEGACY (regression): without the host callback, clicking Start still stores
    /// <c>true</c> and completes the step — the documented "click = done" behaviour.</summary>
    [Fact]
    public void Runner_ExternalWithoutCallback_KeepsLegacyClickCompletes()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("verify", "ID check", SigningFieldType.Verification, required: true)]));

        cut.Find(".tm-signing-external-step__start-button").Click();

        cut.Find(".tm-signing-external-step__verified").Should().NotBeNull(
            "legacy behaviour: the click stores true and completes the step");
        cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeFalse();
    }

    /// <summary>Phase 6 review item 4: the KBA submit hands the typed ANSWERS to the host through
    /// the request — and the runner never stores them as the field value.</summary>
    [Fact]
    public void Runner_KbaSubmit_HostReceivesAnswers_AnswersNeverStoredAsFieldValue()
    {
        Services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
        SigningExternalStepRequest? submitted = null;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("kba", "KBA", SigningFieldType.Kba, required: true)])
            .Add(p => p.StepContextProvider, field => new SigningExternalStepContext(
                Questions: ["What city?"]))
            .Add(p => p.OnExternalStepRequested, EventCallback.Factory
                .Create<SigningExternalStepRequest>(this, request => submitted = request)));

        cut.Find("input.tm-signing-external-step__answer").Input("Brno");
        cut.Find(".tm-signing-external-step__submit").Click();

        submitted.Should().NotBeNull("the KBA submit reaches the host");
        submitted!.Field.Uuid.Should().Be("kba");
        submitted.Answers["What city?"].Should().Be("Brno");

        // Nothing was stored: the answers ride the request only, so the required step stays
        // incomplete until the host stores the provider result through Values.
        cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue();
    }

    private static SigningField CreateField(
        string uuid,
        string name,
        SigningFieldType type,
        double y = 0.1,
        bool required = false)
    {
        return new SigningField
        {
            Uuid = uuid,
            Name = name,
            Type = type,
            Required = required,
            Areas =
            [
                new SigningFieldArea
                {
                    AttachmentUuid = "doc",
                    Page = 0,
                    X = 0.1,
                    Y = y,
                    Width = 0.2,
                    Height = 0.05
                }
            ]
        };
    }
}
