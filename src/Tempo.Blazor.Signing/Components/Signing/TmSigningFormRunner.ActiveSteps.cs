using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <content>
/// The active-steps contract (Signing redesign phase 6, S2-5/SI2): the phone OTP, the attachment
/// upload and the external verification become HOST-CONFIRMED steps. Everything here is OPT-IN —
/// a host that wires none of the callbacks keeps byte-identical legacy behaviour (the documented
/// "click = done" contract of phase 1). The handlers never decide an outcome themselves: the OTP
/// validity is the host's answer, the upload result is the host's url, and the external flow's
/// completion is the provider result the host stores through <c>Values</c>.
/// </content>
public partial class TmSigningFormRunner
{
    // The typed OTP drafts per phone field (phase 6). The draft is NEVER stored as the field
    // value — only the normalized phone number is — so a leaked code cannot complete anything.
    // Kept PER FIELD so navigating away and back restores the draft (phase 6 review item 12).
    private readonly Dictionary<string, string?> _phoneCodeDrafts = new(StringComparer.Ordinal);

    // The fields whose OTP the HOST verified, mapped to the VERIFIED NUMBER (phase 6 review
    // item 1): the confirmation is bound to the number it verified, so editing the number
    // (or a host-pushed value change) silently voids it. Completion state, not presentation:
    // with OnVerifyPhoneCode wired, IsFieldComplete reads ONLY this map for phone fields.
    // Runner-INTERNAL — never part of Values — so a resumed session re-verifies and a host
    // keeps its own record on its side.
    private readonly Dictionary<string, string> _verifiedPhoneNumbers = new(StringComparer.Ordinal);

    // The fields a code was successfully sent for (phase 6 review item 12): the OTP row survives
    // navigating away and back. Cleared only when the host send FAILED (no fake progress).
    private readonly HashSet<string> _phoneCodeSent = new(StringComparer.Ordinal);

    // The fields whose CURRENT validation message targets the OTP input (phase 6 review item 8):
    // the error is owned by the OTP input (aria-invalid + describedby), the phone input stays
    // clean, and typing a new draft clears it.
    private readonly HashSet<string> _phoneOtpInvalid = new(StringComparer.Ordinal);

    // In-flight guards (phase 6 review item 5): a Verify/Send already running blocks a second
    // raise until the host answers — the UI disables the actions, the runner enforces it.
    private readonly HashSet<string> _phoneSendInFlight = new(StringComparer.Ordinal);
    private readonly HashSet<string> _phoneVerifyInFlight = new(StringComparer.Ordinal);

    // The runner-owned cancellation of the active-step host callbacks (phase 6 review item 10,
    // hardened): ONE source PER FIELD+OPERATION instead of one global source. A new request no
    // longer cancels every previous host operation — a still-running dispatch/upload on another
    // field (or another operation on the same field) keeps its token; only a newer request for
    // the SAME field and operation supersedes its predecessor. Every live source is cancelled and
    // released in DisposeAsync.
    private readonly Dictionary<(string FieldUuid, ActiveStepOperation Operation), CancellationTokenSource> _activeStepCts = new();

    // The OTP-row reveal REQUESTED by a host-accepted send (phase 6 review fix) and performed
    // ONCE by OnAfterRenderAsync after the render applied the sent state: the code row (input +
    // Verify + resend countdown) scrolls into view with block:'nearest' — the input-only focus
    // scroll of the step leaves the Verify action and the countdown clipped below the mobile
    // sheet's visible area. Handlers never measure (the same contract as the phase-5 auto-scroll).
    private bool _pendingOtpRowReveal;

    /// <summary>The active-step host operations, one cancellation slot each.</summary>
    private enum ActiveStepOperation
    {
        /// <summary>The phone OTP dispatch (<c>OnSendPhoneCode</c>).</summary>
        SendPhoneCode,

        /// <summary>The phone OTP verification (<c>OnVerifyPhoneCode</c>).</summary>
        VerifyPhoneCode,

        /// <summary>The attachment upload (<c>OnAttachmentSelected</c>).</summary>
        UploadAttachment,

        /// <summary>The external flow start (<c>OnExternalStepRequested</c>).</summary>
        ExternalStep
    }

    private CancellationToken GetActiveStepCancellationToken(string fieldUuid, ActiveStepOperation operation)
    {
        var key = (fieldUuid, operation);
        if (_activeStepCts.TryGetValue(key, out var previous))
        {
            // A newer request for the SAME field and operation supersedes its predecessor —
            // never a global cancel (the old single-source design cancelled a concurrent upload
            // on ANOTHER field just because a send was raised somewhere else).
            previous.Cancel();
            previous.Dispose();
        }

        var cts = new CancellationTokenSource();
        _activeStepCts[key] = cts;
        return cts.Token;
    }

    /// <summary>
    /// Whether the host confirmed THIS NUMBER's OTP (phase 6 review item 1): the stored
    /// confirmation must match the field's current normalized value — a different number voids
    /// the confirmation.
    /// </summary>
    private bool IsPhoneVerified(SigningField field)
        => OnVerifyPhoneCode is not null
            && _verifiedPhoneNumbers.TryGetValue(field.Uuid, out var verifiedNumber)
            && string.Equals(FormatValue(GetValue(field.Uuid)), verifiedNumber, StringComparison.Ordinal);

    /// <summary>Whether a code was successfully sent for this field (the OTP row persists across navigation).</summary>
    private bool IsPhoneCodeSent(string fieldUuid) => _phoneCodeSent.Contains(fieldUuid);

    /// <summary>The typed OTP draft of this field, preserved across navigation (item 12).</summary>
    private string? GetPhoneCodeDraft(string fieldUuid)
        => _phoneCodeDrafts.TryGetValue(fieldUuid, out var code) ? code : null;

    /// <summary>
    /// The Verify callback handed to the phone step — present only when a host verifier is wired,
    /// so a legacy runner renders no Verify action at all.
    /// </summary>
    private EventCallback GetPhoneVerifyCallback(string fieldUuid)
        => OnVerifyPhoneCode is not null
            ? EventCallback.Factory.Create(this, () => VerifyPhoneCodeAsync(fieldUuid))
            : default;

    /// <summary>
    /// The Change-number callback handed to the phone step (phase 6 review item 1): clears the
    /// host confirmation so the verified state unlocks and the number becomes editable again.
    /// </summary>
    private EventCallback GetPhoneChangeNumberCallback(string fieldUuid)
        => OnVerifyPhoneCode is not null
            ? EventCallback.Factory.Create(this, () => HandlePhoneChangeNumberAsync(fieldUuid))
            : default;

    /// <summary>
    /// The Send callback handed to the phone step — ALWAYS present, so a resend invalidates a
    /// previous confirmation even when the host wired only the verifier; the host forwarding
    /// happens only when <see cref="OnSendPhoneCode"/> is wired.
    /// </summary>
    private EventCallback<string> GetPhoneSendCallback(string fieldUuid)
        => EventCallback.Factory.Create<string>(this, phone => SendPhoneCodeAsync(fieldUuid, phone));

    /// <summary>
    /// The upload resolver handed to the attachment step — present only when a host uploader is
    /// wired, so a legacy runner keeps the client-side-only attachment behaviour.
    /// </summary>
    private Func<IBrowserFile, Task<string?>>? GetAttachmentUploadResolver(string fieldUuid)
        => OnAttachmentSelected is null
            ? null
            : file => UploadAttachmentAsync(fieldUuid, file);

    private Task SetPhoneCodeDraftAsync(string fieldUuid, string? code)
    {
        _phoneCodeDrafts[fieldUuid] = code;

        // OTP error ownership (phase 6 review item 8): a NEW (non-empty) draft invalidates the
        // old invalid-code error — the message and the OTP-targeted state clear together.
        if (!string.IsNullOrEmpty(code) && _phoneOtpInvalid.Remove(fieldUuid))
        {
            _validationMessage = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>Clears the host confirmation when the signer asks to change the number (item 1) —
    /// the number becomes editable, the stale code-sent state resets and the step is incomplete
    /// until the new number is verified.</summary>
    private Task HandlePhoneChangeNumberAsync(string fieldUuid)
    {
        _verifiedPhoneNumbers.Remove(fieldUuid);
        _phoneCodeSent.Remove(fieldUuid);
        if (_phoneOtpInvalid.Remove(fieldUuid))
        {
            _validationMessage = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Forwards the send to the host (when wired) and invalidates a previous confirmation — a NEW
    /// code means the old one (if any) no longer proves anything. The PREVIOUS sent state is
    /// cleared while the dispatch runs: the phone step (runner-hosted) hides its OTP row for the
    /// duration and the false→true flip on SUCCESS restarts the countdown/focus/announcement,
    /// while a FAILURE leaves the state cleared — no fake progress (phase 6 review item 5). A
    /// failing host shows the localized unavailable message.
    /// </summary>
    private async Task SendPhoneCodeAsync(string fieldUuid, string phone)
    {
        if (phone is null)
        {
            return;
        }

        if (!_phoneSendInFlight.Add(fieldUuid))
        {
            return;
        }

        try
        {
            _verifiedPhoneNumbers.Remove(fieldUuid);
            _phoneCodeSent.Remove(fieldUuid);
            if (OnSendPhoneCode.HasDelegate)
            {
                await OnSendPhoneCode.InvokeAsync(new SigningPhoneCodeRequest(fieldUuid, phone)
                {
                    CancellationToken = GetActiveStepCancellationToken(fieldUuid, ActiveStepOperation.SendPhoneCode)
                });
            }

            _phoneCodeSent.Add(fieldUuid);
            // The send was accepted: reveal the code row after this render (OnAfterRenderAsync),
            // so the OTP input, the Verify action and the fresh countdown are all visible — the
            // mobile sheet's visible area included.
            _pendingOtpRowReveal = true;
        }
        catch (OperationCanceledException)
        {
            // The host (or the runner teardown) cancelled the dispatch: no progress, no error.
        }
        catch (Exception)
        {
            _phoneCodeSent.Remove(fieldUuid);
            _validationMessage = Loc["TmSigningStep_VerificationUnavailable"];
        }
        finally
        {
            _phoneSendInFlight.Remove(fieldUuid);
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Verifies the typed draft against the HOST (phase 6): <c>true</c> records the confirmation
    /// — bound to the verified NUMBER — and completes the step, <c>false</c> shows the localized
    /// invalid-code message through the step shell with the error owned by the OTP input (item 8).
    /// A throwing host shows the localized unavailable message and NEVER records a confirmation
    /// (item 5). Without a wired verifier this never runs (the callback is only handed to the
    /// step when the host hooked in).
    /// </summary>
    private async Task VerifyPhoneCodeAsync(string fieldUuid)
    {
        if (OnVerifyPhoneCode is null || !_phoneVerifyInFlight.Add(fieldUuid))
        {
            return;
        }

        try
        {
            var phone = FormatValue(GetValue(fieldUuid));
            _phoneCodeDrafts.TryGetValue(fieldUuid, out var code);
            var verified = await OnVerifyPhoneCode(new SigningPhoneVerificationRequest(fieldUuid, phone, code)
            {
                CancellationToken = GetActiveStepCancellationToken(fieldUuid, ActiveStepOperation.VerifyPhoneCode)
            });
            if (verified)
            {
                _verifiedPhoneNumbers[fieldUuid] = phone;
                _phoneOtpInvalid.Remove(fieldUuid);
                _validationMessage = null;
                // Verification success moves focus back to the step title (phase 6 review item 7,
                // WCAG 2.4.3): the shell consumes the navigation signal after the re-render.
                _stepFocusSignal.Version++;
            }
            else
            {
                _verifiedPhoneNumbers.Remove(fieldUuid);
                _phoneOtpInvalid.Add(fieldUuid);
                _validationMessage = Loc["TmSigningStep_InvalidCode"];
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the host or the runner teardown: no state change, never "verified".
        }
        catch (Exception)
        {
            _validationMessage = Loc["TmSigningStep_VerificationUnavailable"];
        }
        finally
        {
            _phoneVerifyInFlight.Remove(fieldUuid);
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Hands the picked file to the HOST and returns the uploaded url (phase 6). The content is
    /// read by the host — never here — because on Blazor Server browser files stream over SignalR
    /// with tight message limits. The request carries the field's own upload cancellation token
    /// (item 10): a request raised elsewhere never cancels this upload.
    /// </summary>
    private async Task<string?> UploadAttachmentAsync(string fieldUuid, IBrowserFile file)
    {
        if (OnAttachmentSelected is null)
        {
            return null;
        }

        return await OnAttachmentSelected(new SigningAttachmentUploadRequest(fieldUuid, file)
        {
            CancellationToken = GetActiveStepCancellationToken(fieldUuid, ActiveStepOperation.UploadAttachment)
        });
    }

    /// <summary>
    /// The external step's Start / Checkout handler (phase 6): with
    /// <see cref="OnExternalStepRequested"/> wired the click only NOTIFIES the host — the step
    /// completes solely after the host stores the provider result (value <c>true</c>) through
    /// <see cref="Values"/>. Without the callback the legacy behaviour keeps storing
    /// <c>true</c> on the click (the documented "click = done" contract of phase 1 — presentation
    /// state, never proof of a verification or payment).
    /// </summary>
    private Task StartExternalStepAsync(SigningField field)
        => StartExternalStepAsync(new SigningExternalStepRequest(field, new Dictionary<string, string?>()));

    /// <summary>
    /// The KBA submit handler (phase 6 review item 4): raises the host request WITH the typed
    /// answers — the answers ride the request only and are NEVER stored as the field value.
    /// </summary>
    private async Task StartExternalStepAsync(SigningExternalStepRequest request)
    {
        if (OnExternalStepRequested.HasDelegate)
        {
            await OnExternalStepRequested.InvokeAsync(new SigningExternalStepRequest(
                request.Field, request.Answers)
            {
                CancellationToken = GetActiveStepCancellationToken(request.Field.Uuid, ActiveStepOperation.ExternalStep)
            });
            return;
        }

        await SetFieldValueAsync(request.Field.Uuid, true);
    }

    /// <summary>
    /// Resolves the host's provider context for an external field (phase 6): url, KBA questions,
    /// loading state, error and provider name. Evaluated per render; a provider without a hook
    /// keeps the step's own defaults.
    /// </summary>
    private SigningExternalStepContext? ResolveExternalStepContext(SigningField field)
        => StepContextProvider?.Invoke(field);
}
