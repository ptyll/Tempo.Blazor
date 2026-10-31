using System.Globalization;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// The shared date formatter of the Signing read-only surfaces (<see cref="TmSubmissionStatusTimeline"/>,
/// <see cref="TmAuditTrailViewer"/>, <see cref="TmPdfSignatureVerification"/>, <see cref="TmShareLinkPanel"/>).
/// Every panel formats timestamps the same way: to the viewer's local time zone, with the
/// component's ambient culture (<see cref="CultureInfo.CurrentCulture"/> unless a caller passes an
/// explicit culture). Centralising the call replaces the scattered <c>ToString("g")</c> copies so a
/// future culture knob has one place to land.
/// </summary>
internal static class SigningDateFormatter
{
    /// <summary>
    /// Formats <paramref name="value"/> in the viewer's local time zone with the ambient culture's
    /// short date + time pattern.
    /// </summary>
    public static string Format(DateTimeOffset value) => Format(value, CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats <paramref name="value"/> in the viewer's local time zone with
    /// <paramref name="culture"/> (the ambient culture when <c>null</c>).
    /// </summary>
    public static string Format(DateTimeOffset value, CultureInfo? culture)
    {
        var effective = culture ?? CultureInfo.CurrentCulture;
        return value.ToLocalTime().ToString("g", effective);
    }

    /// <summary>
    /// The culture-invariant ISO-8601 form used for the <c>datetime</c> attribute of a
    /// <c>&lt;time&gt;</c> element — never shown to a user.
    /// </summary>
    public static string FormatIso(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
