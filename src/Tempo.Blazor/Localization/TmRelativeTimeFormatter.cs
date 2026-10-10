namespace Tempo.Blazor.Localization;

/// <summary>The default <see cref="ITmRelativeTimeFormatter"/>, backed by <see cref="ITmLocalizer"/> and a <see cref="TimeProvider"/>.</summary>
public sealed class TmRelativeTimeFormatter : ITmRelativeTimeFormatter
{
    /// <summary>Creates the formatter.</summary>
    /// <param name="localizer">Source of the <c>Tm_Time_*</c> strings.</param>
    /// <param name="clock">The clock; <see cref="TimeProvider.System"/> when null.</param>
    public TmRelativeTimeFormatter(ITmLocalizer localizer, TimeProvider? clock = null)
    {
    }

    /// <inheritdoc />
    public string Format(DateTimeOffset value, DateTimeOffset? now = null, TimeZoneInfo? timeZone = null)
        => throw new NotImplementedException();
}