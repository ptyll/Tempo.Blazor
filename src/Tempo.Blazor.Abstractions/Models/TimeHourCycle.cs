namespace Tempo.Blazor.Models;

/// <summary>
/// 12-hour versus 24-hour clock for a time selector. <see cref="Auto"/> follows the culture's
/// <c>ShortTimePattern</c> (<c>h</c> → 12-hour, otherwise 24-hour), so <c>en-US</c> is 12-hour
/// and <c>en-GB</c>/<c>cs</c>/<c>sk</c>/<c>fr</c> are 24-hour.
/// </summary>
public enum TimeHourCycle
{
    /// <summary>Follow the culture's short-time pattern. The default.</summary>
    Auto = 0,

    /// <summary>24-hour clock (hours 0–23).</summary>
    H23 = 1,

    /// <summary>12-hour clock with an AM/PM period (hours 1–12).</summary>
    H12 = 2,
}
