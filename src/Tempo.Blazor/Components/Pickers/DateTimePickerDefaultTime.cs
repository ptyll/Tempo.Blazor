namespace Tempo.Blazor.Components.Pickers;

/// <summary>
/// Time applied when the user first picks a date on an empty <see cref="TmDateTimePicker"/>.
/// </summary>
public enum DateTimePickerDefaultTime
{
    /// <summary>00:00:00 (today's behaviour).</summary>
    Midnight = 0,

    /// <summary>The current local time of the injected <see cref="TimeProvider"/> (fallback <see cref="TimeProvider.System"/>).</summary>
    Now = 1
}
