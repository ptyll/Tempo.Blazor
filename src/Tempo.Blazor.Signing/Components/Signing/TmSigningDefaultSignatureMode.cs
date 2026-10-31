namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// The default capture mode a signature step starts in (<c>Signing redesign phase 5</c>, SI3).
/// The template's stored <c>Preferences.Format</c> always wins over this setting; it decides only
/// when the template is silent.
/// </summary>
public enum TmSigningDefaultSignatureMode
{
    /// <summary>
    /// Resolve per pointer: <see cref="TmSignatureCaptureMode.Draw"/> on a coarse pointer (a
    /// finger — the honest first presentation on touch), otherwise today's
    /// <see cref="TmSignatureCaptureMode.Typed"/>. The default.
    /// </summary>
    Auto,

    /// <summary>The signature step starts on the drawing pad.</summary>
    Draw,

    /// <summary>The signature step starts on the typed entry.</summary>
    Typed,

    /// <summary>The signature step starts on the image upload.</summary>
    Upload
}
