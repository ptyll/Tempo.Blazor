using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The one place a fallback surface turns a <see cref="FallbackAttemptStatus"/> or a
/// <see cref="FallbackResolutionPolicy"/> into an icon, a stable CSS tone slug and a localized word.
/// <para>
/// Deliberately internal: the statuses and the policy are the contract, this mapping is presentation. It exists so the
/// flow, the table and the tests cannot drift — every result is always an ICON plus a WORD plus a tone, never a colour
/// alone (WCAG 1.4.1) — and so an unknown value from a newer backend falls back to the passive "not tried" state
/// instead of throwing or claiming a success.
/// </para>
/// </summary>
internal static class FallbackPresentation
{
    /// <summary>The status an unknown value is read as (an unclassified attempt never ran).</summary>
    /// <param name="status">The value to normalise.</param>
    /// <returns><paramref name="status"/> when it is declared, <see cref="FallbackAttemptStatus.NotTried"/> otherwise.</returns>
    public static FallbackAttemptStatus NormalizeStatus(FallbackAttemptStatus status)
        => IsKnownStatus(status) ? status : FallbackAttemptStatus.NotTried;

    /// <summary>Whether <paramref name="status"/> is one of the declared states.</summary>
    /// <param name="status">The value to check.</param>
    /// <returns><c>true</c> for a declared member.</returns>
    public static bool IsKnownStatus(FallbackAttemptStatus status)
        => status is >= FallbackAttemptStatus.NotTried and <= FallbackAttemptStatus.Succeeded;

    /// <summary>The stable CSS modifier slug of a status, never derived from <c>ToString().ToLower()</c>.</summary>
    /// <param name="status">The status to name.</param>
    /// <returns><c>not-tried</c> / <c>skipped</c> / <c>failed</c> / <c>succeeded</c>.</returns>
    public static string StatusTone(FallbackAttemptStatus status) => NormalizeStatus(status) switch
    {
        FallbackAttemptStatus.Skipped => "skipped",
        FallbackAttemptStatus.Failed => "failed",
        FallbackAttemptStatus.Succeeded => "succeeded",
        _ => "not-tried",
    };

    /// <summary>The built-in icon id of a status. Always paired with the word, never the only carrier.</summary>
    /// <param name="status">The status to draw.</param>
    /// <returns>An <see cref="IconNames"/> constant.</returns>
    public static string StatusIcon(FallbackAttemptStatus status) => NormalizeStatus(status) switch
    {
        FallbackAttemptStatus.Skipped => IconNames.SkipForward,
        FallbackAttemptStatus.Failed => IconNames.AlertCircle,
        FallbackAttemptStatus.Succeeded => IconNames.CheckCircle,
        _ => IconNames.Circle,
    };

    /// <summary>The localized word of a status.</summary>
    /// <param name="localizer">The localizer to read the <c>TmFallbackStatus_*</c> keys from.</param>
    /// <param name="status">The status to name.</param>
    /// <returns>The translated word; the "not tried" word for an unknown value.</returns>
    public static string StatusText(ITmLocalizer localizer, FallbackAttemptStatus status)
        => NormalizeStatus(status) switch
        {
            FallbackAttemptStatus.Skipped => localizer["TmFallbackStatus_Skipped"],
            FallbackAttemptStatus.Failed => localizer["TmFallbackStatus_Failed"],
            FallbackAttemptStatus.Succeeded => localizer["TmFallbackStatus_Succeeded"],
            _ => localizer["TmFallbackStatus_NotTried"],
        };

    /// <summary>The resolution policy an unknown value is read as (an unset policy pins the first model).</summary>
    /// <param name="policy">The value to normalise.</param>
    /// <returns><paramref name="policy"/> when it is declared, <see cref="FallbackResolutionPolicy.Pin"/> otherwise.</returns>
    public static FallbackResolutionPolicy NormalizePolicy(FallbackResolutionPolicy policy)
        => IsKnownPolicy(policy) ? policy : FallbackResolutionPolicy.Pin;

    /// <summary>Whether <paramref name="policy"/> is one of the declared policies.</summary>
    /// <param name="policy">The value to check.</param>
    /// <returns><c>true</c> for a declared member.</returns>
    public static bool IsKnownPolicy(FallbackResolutionPolicy policy)
        => policy is >= FallbackResolutionPolicy.Pin and <= FallbackResolutionPolicy.Auto;

    /// <summary>The stable CSS slug of a policy.</summary>
    /// <param name="policy">The policy to name.</param>
    /// <returns><c>pin</c> / <c>fallback</c> / <c>auto</c>.</returns>
    public static string PolicySlug(FallbackResolutionPolicy policy) => NormalizePolicy(policy) switch
    {
        FallbackResolutionPolicy.Fallback => "fallback",
        FallbackResolutionPolicy.Auto => "auto",
        _ => "pin",
    };

    /// <summary>The localized word of a policy.</summary>
    /// <param name="localizer">The localizer to read the <c>TmFallbackPolicy_*</c> keys from.</param>
    /// <param name="policy">The policy to name.</param>
    /// <returns>The translated word; the "pin" word for an unknown value.</returns>
    public static string PolicyText(ITmLocalizer localizer, FallbackResolutionPolicy policy)
        => NormalizePolicy(policy) switch
        {
            FallbackResolutionPolicy.Fallback => localizer["TmFallbackPolicy_Fallback"],
            FallbackResolutionPolicy.Auto => localizer["TmFallbackPolicy_Auto"],
            _ => localizer["TmFallbackPolicy_Pin"],
        };

    /// <summary>Whether <paramref name="mode"/> is one of the declared modes.</summary>
    /// <param name="mode">The value to check.</param>
    /// <returns><c>true</c> for a declared member.</returns>
    public static bool IsKnownMode(FallbackChainMode mode) => mode is FallbackChainMode.View or FallbackChainMode.Edit;

    /// <summary>Whether <paramref name="mode"/> is one of the declared view modes.</summary>
    /// <param name="mode">The value to check.</param>
    /// <returns><c>true</c> for a declared member.</returns>
    public static bool IsKnownViewMode(FallbackChainViewMode mode)
        => mode is FallbackChainViewMode.Flow or FallbackChainViewMode.Table;
}
