using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The presentation rules of <see cref="TmAutonomyDial"/>: where the default stops come from, how a grade is named and
/// which tone a level carries.
/// <para>
/// The mapping lives in one place so the slider, the radio group and the read-out can never disagree about a stop, and so
/// the rules are testable without rendering. Every string comes from <see cref="ITmLocalizer"/>; a level the host labelled
/// itself always wins over the localized default.
/// The presentation rules live INSIDE the package: every other <c>*Presentation</c> helper here is <c>internal</c>, and a
/// public static class would turn shapes like <see cref="LevelTone"/> into a 2.10.0 API promise no host asked for. A host
/// that wants the localized default stops passes no <c>Levels</c> at all.
/// </para>
/// </summary>
internal static class AutonomyPresentation
{
    /// <summary>The number of stops a dial offers when the host supplies none.</summary>
    public const int DefaultLevelCount = 3;

    /// <summary>Whether a cast value is one of the two declared presentations.</summary>
    /// <param name="presentation">The value to check.</param>
    /// <returns><c>true</c> when the value is <see cref="AutonomyDialPresentation.Dial"/> or <see cref="AutonomyDialPresentation.Segmented"/>.</returns>
    public static bool IsKnownPresentation(AutonomyDialPresentation presentation)
        => presentation is AutonomyDialPresentation.Dial or AutonomyDialPresentation.Segmented;

    /// <summary>
    /// The three localized stops a dial renders when the host binds no <c>Levels</c>: ask first, ask when risky, act
    /// freely — the grades every agent product starts from.
    /// </summary>
    /// <param name="localizer">The localizer that names and explains the stops.</param>
    /// <returns>Three stops with the values <c>0</c>, <c>1</c> and <c>2</c>.</returns>
    public static IReadOnlyList<AutonomyLevel> DefaultLevels(ITmLocalizer localizer)
        => localizer is null
            ? []
            :
            [
                new AutonomyLevel(0)
                {
                    Label = localizer["TmAutonomyDial_DefaultLevel0Label"],
                    Description = localizer["TmAutonomyDial_DefaultLevel0Description"],
                },
                new AutonomyLevel(1)
                {
                    Label = localizer["TmAutonomyDial_DefaultLevel1Label"],
                    Description = localizer["TmAutonomyDial_DefaultLevel1Description"],
                },
                new AutonomyLevel(2)
                {
                    Label = localizer["TmAutonomyDial_DefaultLevel2Label"],
                    Description = localizer["TmAutonomyDial_DefaultLevel2Description"],
                },
            ];

    /// <summary>
    /// The label a stop prints: the host's own wording, or a localized fallback that names the position so a stop the host
    /// left unlabelled is still readable.
    /// </summary>
    /// <param name="localizer">The localizer that supplies the fallback.</param>
    /// <param name="level">The stop being named.</param>
    /// <param name="position">The stop's 1-based position.</param>
    /// <returns>A non-empty label.</returns>
    public static string LevelLabel(ITmLocalizer localizer, AutonomyLevel level, int position)
        => level.HasLabel ? level.Label : localizer["TmAutonomyDial_LevelFallback", position];

    /// <summary>The description of a stop, or an empty string when the host supplied none.</summary>
    /// <param name="level">The stop being described.</param>
    /// <returns>The description text, possibly empty.</returns>
    public static string LevelDescription(AutonomyLevel level) => level.Description ?? string.Empty;

    /// <summary>
    /// The tone of a grade: the lowest grades are the most supervised, so only the top one reads as the "free" end. The
    /// tone is a redundant cue — every stop also prints its word.
    /// </summary>
    /// <param name="value">The grade.</param>
    /// <param name="highestValue">The highest grade the dial offers.</param>
    /// <returns><c>supervised</c>, <c>guarded</c> or <c>free</c>.</returns>
    public static string LevelTone(int value, int highestValue)
    {
        if (value >= highestValue)
        {
            return "free";
        }

        return value <= 0 ? "supervised" : "guarded";
    }
}
