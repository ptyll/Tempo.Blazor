using System.Globalization;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// The ONE place the preview scene widths are defined (§3.4/D17): <see cref="TabletWidth"/>,
/// <see cref="MobileWidth"/> and <see cref="DesktopMargin"/> are constants, and
/// <see cref="WidthFor"/> derives the desktop width from the document's own
/// <see cref="TemplateStyles.ContentWidth"/>. The component renders the number, its CSS frames it and the
/// tests assert it — none of them repeats a magic number.
/// </summary>
public static class EmailPreviewMetrics
{
    /// <summary>Tablet preview width in CSS pixels.</summary>
    public const int TabletWidth = 768;

    /// <summary>Mobile preview width in CSS pixels.</summary>
    public const int MobileWidth = 390;

    /// <summary>
    /// The stage margin added around the desktop scene, in CSS pixels: the "paper" of the email sits on the
    /// secondary surface with a little breathing room on both sides.
    /// </summary>
    public const int DesktopMargin = 80;

    /// <summary>The content width used when <see cref="TemplateStyles.ContentWidth"/> is missing or unparsable.</summary>
    public const int DefaultContentWidth = 600;

    /// <summary>Returns the scene width in CSS pixels for <paramref name="device"/>.</summary>
    /// <param name="device">The preview device.</param>
    /// <param name="document">The document whose content width drives the desktop scene.</param>
    public static int WidthFor(EmailPreviewDevice device, EmailTemplateDocument? document) => device switch
    {
        EmailPreviewDevice.Tablet => TabletWidth,
        EmailPreviewDevice.Mobile => MobileWidth,
        _ => ContentWidthOf(document) + DesktopMargin,
    };

    /// <summary>Parses <see cref="TemplateStyles.ContentWidth"/> (<c>600px</c>) into CSS pixels.</summary>
    /// <param name="document">The document, or <see langword="null"/> for the default width.</param>
    public static int ContentWidthOf(EmailTemplateDocument? document)
    {
        var raw = document?.Styles?.ContentWidth;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultContentWidth;
        }

        var digits = raw.Trim();
        if (digits.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[..^2];
        }

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) && width > 0
            ? width
            : DefaultContentWidth;
    }
}
