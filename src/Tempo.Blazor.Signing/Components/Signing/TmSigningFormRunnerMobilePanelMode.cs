namespace Tempo.Blazor.Components.Signing;

/// <summary>Initial mobile panel presentation mode for <see cref="TmSigningFormRunner"/>.</summary>
/// <remarks>
/// Signing redesign phase 5 keeps the enum additive-only: <see cref="Collapsed"/> presents the
/// PEEK state (handle + done count + the primary "Sign now" call to action above the document),
/// and <see cref="Expanded"/> presents the sheet with the field list and the current step. The
/// FULL edit state (a signature or a text entry holds focus) is the SAME expanded sheet — it caps
/// at 85dvh at every width, so there is no third value, no resize-on-focus and no JS.
/// </remarks>
public enum TmSigningFormRunnerMobilePanelMode
{
    /// <summary>The mobile step panel starts expanded.</summary>
    Expanded,

    /// <summary>The mobile step panel starts collapsed — the peek state (phase 5).</summary>
    Collapsed
}
