using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// A test double for <see cref="ITmUserTimeZone"/>: a component under test resolves a zone without
/// JS interop. <see cref="SetZone"/> behaves like the real one (marks the zone resolved, raises
/// <see cref="ITmUserTimeZone.Changed"/> once the value actually changes).
/// </summary>
internal sealed class StubTmUserTimeZone(TimeZoneInfo zone, bool resolved = true) : ITmUserTimeZone
{
    public TimeZoneInfo Zone { get; private set; } = zone;

    public bool IsResolved { get; private set; } = resolved;

    public event EventHandler? Changed;

    public void SetZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var raise = !IsResolved || !zone.Equals(Zone);
        Zone = zone;
        IsResolved = true;
        if (raise)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raises <see cref="Changed"/> (the real implementation does this when the probe lands).</summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
