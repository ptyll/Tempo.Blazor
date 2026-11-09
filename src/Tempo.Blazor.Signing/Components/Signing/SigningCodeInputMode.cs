namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// How <see cref="TmSigningPhoneStep"/> renders the one-time-code input („Nové komponenty 1/7"
/// fáze 5). The default preserves today's behaviour; the segmented mode is the opt-in
/// <see cref="TmOtpInput"/> integration.
/// </summary>
public enum SigningCodeInputMode
{
    /// <summary>
    /// The code types into the single core <c>TmTextInput</c> with
    /// <c>inputmode="numeric"</c> + <c>autocomplete="one-time-code"</c> — today's behaviour, and
    /// the default so existing hosts are unaffected.
    /// </summary>
    Single,

    /// <summary>
    /// The code types into the segmented core <see cref="TmOtpInput"/> (paste distribution,
    /// per-cell labels, invalid/verified states). Completing the code automatically starts the
    /// verification when a verifier is wired (<c>OnVerifyCode</c> / the runner's
    /// <c>OnVerifyPhoneCode</c>); a resend clears and refocuses the input.
    /// </summary>
    Segmented
}
