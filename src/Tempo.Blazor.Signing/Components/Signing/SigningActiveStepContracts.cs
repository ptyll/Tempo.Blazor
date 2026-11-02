using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Host request to dispatch an OTP code for a phone signing field (Signing redesign phase 6,
/// S2-5). The runner raises it through <see cref="TmSigningFormRunner.OnSendPhoneCode"/> when the
/// signer asks for a code; without that callback wired, no request is ever raised and the phone
/// step keeps its documented cosmetic behaviour (no SMS is sent).
/// </summary>
/// <remarks>
/// RATE LIMITING NOTE: the component throttles only the UI (the Send action is disabled while the
/// resend countdown runs). The HOST owns the real rate limiting and attempt counting — a
/// malicious client can raise the callback arbitrarily often, so count dispatches per number,
/// per session and per time window on the host side.
/// </remarks>
/// <param name="FieldUuid">UUID of the phone signing field the code belongs to.</param>
/// <param name="Phone">The normalized full number ("+420777123456") — what an SMS provider dials.</param>
public sealed record SigningPhoneCodeRequest(string FieldUuid, string Phone)
{
    /// <summary>
    /// Token the RUNNER owns — cancelled when the runner is disposed, or when a NEWER request for
    /// the SAME field and operation (a fresh send) supersedes this one. Requests on other fields
    /// or other operations never cancel it. A host dispatching the code asynchronously can honour
    /// it to stop work for a step that no longer exists.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Host request to verify a typed OTP code for a phone signing field (Signing redesign phase 6,
/// S2-5). The host's answer is the ONLY confirmation the runner accepts: <c>true</c> completes
/// the step, <c>false</c> shows the localized invalid-code message — until then the phone field
/// never completes, no matter what value it carries.
/// </summary>
/// <remarks>
/// <para>
/// RATE LIMITING NOTE: the host owns attempt counting and throttling for the verification —
/// never trust the component to have limited the tries.
/// </para>
/// <para>
/// VERIFICATION STATE NOTE: the runner keeps the confirmation INTERNALLY (uuid → verified
/// number); it is NOT part of <c>Values</c> and is not persisted. A resumed session must
/// re-verify, and a host that needs its own record of the verification stores it on its side
/// when the callback answers <c>true</c>.
/// </para>
/// </remarks>
/// <param name="FieldUuid">UUID of the phone signing field the code belongs to.</param>
/// <param name="Phone">The normalized full number the code was sent to.</param>
/// <param name="Code">The typed one-time code, verbatim.</param>
public sealed record SigningPhoneVerificationRequest(string FieldUuid, string Phone, string? Code)
{
    /// <summary>
    /// Token the RUNNER owns (cancelled when the runner is disposed, or when a newer VERIFY for
    /// the same field supersedes it) — a host verifying against a provider can honour it to stop
    /// work for a step that no longer exists.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Host request raised when the signer starts an external flow — verification, KBA answer
/// submission or payment (Signing redesign phase 6, SI2). The runner raises it through
/// <see cref="TmSigningFormRunner.OnExternalStepRequested"/>; with the callback wired the runner
/// stores NOTHING on the click itself — the step completes solely after the host stores the
/// provider result (value <c>true</c>) through <see cref="TmSigningFormRunner.Values"/>.
/// </summary>
/// <remarks>
/// ANSWERS NOTE: <see cref="Answers"/> carries the inline KBA answers for THIS request only —
/// the runner never stores them as the field value (the field value is the provider result the
/// host stores), so a leaked answer cannot complete anything.
/// </remarks>
/// <param name="Field">The external signing field the flow was started for.</param>
/// <param name="Answers">The typed KBA answers keyed by question (empty for verification / payment starts).</param>
public sealed record SigningExternalStepRequest(SigningField Field, IReadOnlyDictionary<string, string?> Answers)
{
    /// <summary>
    /// Token the RUNNER owns (cancelled when the runner is disposed, or when a newer flow start
    /// for the same field supersedes it) — a host driving the provider flow asynchronously can
    /// honour it to stop work for a step that no longer exists.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Host-provided context for an external verification / KBA / payment signing field (Signing
/// redesign phase 6, SI2). The runner resolves it from
/// <see cref="TmSigningFormRunner.StepContextProvider"/> on every render and feeds the external
/// step: the provider link, the inline KBA questions, the provider's loading state and error, and
/// the provider's display name. The CONTEXT NEVER CONFIRMS the step — only a stored
/// provider result (value <c>true</c> in <c>Values</c>) does.
/// </summary>
/// <param name="ExternalUrl">Provider-hosted page the signer is sent to (opens in a new tab).</param>
/// <param name="Questions">Inline KBA questions; each answer input is labelled by its question.</param>
/// <param name="IsLoading">Whether the provider operation is currently running.</param>
/// <param name="Error">Provider error shown as the step's validation message.</param>
/// <param name="ProviderName">Display name of the verification / payment provider.</param>
public sealed record SigningExternalStepContext(
    string? ExternalUrl = null,
    IReadOnlyList<string>? Questions = null,
    bool IsLoading = false,
    string? Error = null,
    string? ProviderName = null);

/// <summary>
/// Host upload request for an attachment signing field (Signing redesign phase 6, S2-5). The
/// runner raises it through <see cref="TmSigningFormRunner.OnAttachmentSelected"/> and stores the
/// returned url on the attachment; a <c>null</c> url (or a throwing host) REJECTS the file.
/// SECURITY NOTE: the file CONTENT is read by the HOST (<c>OpenReadStream</c>) — never inside the
/// component — because on Blazor Server browser files stream over SignalR with tight message
/// limits. Without the callback the picked files stay client-side metadata (the documented
/// placeholder behaviour).
/// </summary>
/// <remarks>
/// ENFORCEMENT NOTE: <see cref="TmSigningAttachmentStep.MaxFileSize"/> is a CLIENT-REPORTED limit
/// (the browser's declared <see cref="IBrowserFile.Size"/>) and is UX only — the HOST must
/// enforce the real limit when consuming the content:
/// <c>file.OpenReadStream(maxAllowedSize)</c> throws for anything larger.
/// </remarks>
/// <param name="FieldUuid">UUID of the attachment signing field the file belongs to.</param>
/// <param name="File">The picked browser file. Consume its stream in the host, in one pass.</param>
public sealed record SigningAttachmentUploadRequest(string FieldUuid, IBrowserFile File)
{
    /// <summary>
    /// Token the RUNNER owns (cancelled when the runner is disposed, or when a newer upload for
    /// the same field supersedes it) — a host uploading asynchronously can honour it to stop work
    /// for a step that no longer exists.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
