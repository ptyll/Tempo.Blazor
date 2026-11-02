using System.Text.RegularExpressions;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Default submitter-role colours shared by <see cref="TmRecipientRoleEditor"/> (which serializes
/// them into templates as the colour of every newly created role) and the field editor's colour
/// input fallback.
/// <para>
/// The values are the light-theme counterparts of the <c>--tm-signing-role-1..6</c> tokens declared
/// in the Signing stylesheet, but they stay hex ON PURPOSE: they are serialized data — a stored
/// template carries the colour string verbatim, and the value feeds <c>&lt;input type="color"&gt;</c>,
/// which accepts concrete colours only. Hosts rebrand the CSS side by overriding the tokens —
/// <see cref="ToCssColor"/> maps every default-palette colour onto its token reference, so a
/// rebrand reaches stored default colours without touching template data; a host that wants new
/// templates to start from its own palette assigns <see cref="SigningSubmitterRole.Color"/> itself
/// before handing roles to the editors.
/// </para>
/// </summary>
public static class SigningRolePalette
{
    /// <summary>
    /// Role 6's colour before phase 1 darkened it to <c>#a16207</c> — stored templates may still
    /// carry it, so it stays an alias of the role-6 token.
    /// </summary>
    private const string LegacyRole6Color = "#ca8a04";

    /// <summary>Default role colours, indexed by the zero-based role order.</summary>
    public static readonly IReadOnlyList<string> DefaultColors =
    [
        "#2563eb",
        "#16a34a",
        "#dc2626",
        "#9333ea",
        "#0891b2",
        "#a16207"
    ];

    /// <summary>The default colour for the role at zero-based <paramref name="order"/> (wraps around).</summary>
    public static string ColorForOrder(int order)
    {
        var index = ((order % DefaultColors.Count) + DefaultColors.Count) % DefaultColors.Count;
        return DefaultColors[index];
    }

    /// <summary>
    /// Maps a stored role colour onto the stylesheet: a default-palette value (case-insensitive)
    /// becomes the matching <c>var(--tm-signing-role-N)</c> token reference — hosts that rebrand the
    /// tokens then rebrand every default-coloured role at once — the pre-phase-1 role-6 colour
    /// <c>#ca8a04</c> (kept by stored templates) maps onto <c>var(--tm-signing-role-6)</c> like its
    /// successor, and any other value passes through verbatim.
    /// </summary>
    public static string? ToCssColor(string? color)
    {
        for (var i = 0; i < DefaultColors.Count; i++)
        {
            if (string.Equals(DefaultColors[i], color, StringComparison.OrdinalIgnoreCase))
            {
                return $"var(--tm-signing-role-{i + 1})";
            }
        }

        if (string.Equals(LegacyRole6Color, color, StringComparison.OrdinalIgnoreCase))
        {
            return "var(--tm-signing-role-6)";
        }

        return color;
    }

    /// <summary>
    /// The CSS colour of the submitter role that owns <paramref name="submitterUuid"/>: the role's
    /// stored colour through <see cref="ToCssColor"/>, or the palette token of the role's order when
    /// the role carries no colour. <c>null</c> when no role matches — the overlay keeps its
    /// primary-tint default.
    /// </summary>
    internal static string? ResolveRoleColor(IReadOnlyList<SigningSubmitterRole> roles, string? submitterUuid) =>
        TryFindRole(roles, submitterUuid, out var role, out var order)
            ? role.Color is null
                ? $"var(--tm-signing-role-{(order % DefaultColors.Count) + 1})"
                : ToCssColor(role.Color)
            : null;

    /// <summary>
    /// The concrete colour of the submitter role that owns <paramref name="submitterUuid"/>, for the
    /// field editor's <c>&lt;input type="color"&gt;</c> (it cannot render a <c>var()</c> reference):
    /// the role's stored colour, else the palette colour of the role's order. <c>null</c> when no
    /// role matches.
    /// </summary>
    internal static string? ResolveRoleHexColor(IReadOnlyList<SigningSubmitterRole> roles, string? submitterUuid) =>
        TryFindRole(roles, submitterUuid, out var role, out var order)
            ? role.Color ?? ColorForOrder(order)
            : null;

    /// <summary>
    /// The text colour for a role-coloured pill: the WCAG relative-luminance decision between the
    /// white and the near-black ink, by the HIGHER ACTUAL CONTRAST RATIO of the two (the crossover
    /// sits near a 0.18 relative luminance — colours below it read better dark-on-white, above it
    /// white-on-dark). The return is always a token reference, never a hex: default-palette colours
    /// (and the legacy role-6 value) resolve to their per-theme <c>--tm-signing-role-N-on</c> tokens,
    /// so a theme variant or a host rebrand reaches the text with the paint; a colour-less pill (no
    /// role colour — the paint falls back to the primary fill) and an unparsable value ride
    /// <c>var(--tm-color-on-primary)</c>, which the theme resolves; a custom colour returns the tone
    /// token the pill class maps — <c>var(--tm-signing-role-ink-dark)</c> (declared on <c>:root</c>
    /// only, never redefined by a theme) for a light paint, <c>var(--tm-color-on-primary-fixed)</c>
    /// for a dark one.
    /// </summary>
    internal static string ContrastTextFor(string? color)
    {
        if (TryGetPaletteTokenIndex(color, out var index))
        {
            return $"var(--tm-signing-role-{index + 1}-on)";
        }

        return IsLightPaint(color) switch
        {
            true => "var(--tm-signing-role-ink-dark)",
            false => "var(--tm-color-on-primary-fixed)",
            _ => "var(--tm-color-on-primary)"
        };
    }

    /// <summary>
    /// The tone the pill markup should class for: <c>null</c> when the style already carries the
    /// resolved text (<c>--tm-signing-role-N-on</c> / <c>--tm-color-on-primary</c> — palette
    /// colours, no colour at all, unparsable values); <c>true</c> when the custom paint is light
    /// and the near-black ink must ride a class; <c>false</c> when the white ink does. Markup
    /// maps the classes onto the fixed inks (<c>--tm-signing-role-ink-dark</c> /
    /// <c>--tm-color-on-primary-fixed</c>) — never an inline hex, and never
    /// <c>--tm-color-gray-900</c>, which dark and indigo themes redefine to white.
    /// </summary>
    internal static bool? IsLightPaint(string? color)
    {
        if (TryGetPaletteTokenIndex(color, out _))
        {
            return null;
        }

        return TryResolvePaint(color, out var paint) ? !WhiteInkWins(paint) : null;
    }

    /// <summary>Whether the WHITE ink holds the higher contrast ratio on the paint.</summary>
    private static bool WhiteInkWins((double R, double G, double B) paint)
    {
        var luminance = 0.2126 * Linearize(paint.R / 255.0)
            + 0.7152 * Linearize(paint.G / 255.0)
            + 0.0722 * Linearize(paint.B / 255.0);
        var whiteRatio = 1.05 / (luminance + 0.05);
        var darkRatio = (luminance + 0.05) / (DarkInkLuminance + 0.05);
        return whiteRatio >= darkRatio;
    }

    /// <summary>The WCAG relative luminance of the near-black ink (#111827).</summary>
    private const double DarkInkLuminance = 0.009297;

    /// <summary>
    /// The concrete sRGB channels of a stored colour; <c>false</c> when the value is empty or
    /// unparsable (a host token string where concrete data was expected paints nothing custom).
    /// Hex (#rgb/#rgba/#rrggbb/#rrggbbaa) and rgb()/rgba() forms parse; a <c>var()</c> reference
    /// does not — its concrete paint depends on the theme.
    /// </summary>
    private static bool TryResolvePaint(string? color, out (double R, double G, double B) paint)
    {
        paint = default;
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        var value = color.Trim();
        if (value[0] == '#' && value.Length is 4 or 5 or 7 or 9)
        {
            var digits = value[1..];
            if (digits.All(Uri.IsHexDigit))
            {
                if (digits.Length is 3 or 4)
                {
                    digits = string.Concat(digits.Select(digit => $"{digit}{digit}").ToArray());
                }

                paint = (
                    Convert.ToInt32(digits[..2], 16),
                    Convert.ToInt32(digits.Substring(2, 2), 16),
                    Convert.ToInt32(digits.Substring(4, 2), 16));
                return true;
            }

            return false;
        }

        var channels = RgbCall.Match(value);
        if (channels.Success
            && byte.TryParse(channels.Groups["r"].Value, out var r)
            && byte.TryParse(channels.Groups["g"].Value, out var g)
            && byte.TryParse(channels.Groups["b"].Value, out var b))
        {
            paint = (r, g, b);
            return true;
        }

        return false;
    }

    private static readonly Regex RgbCall = new(
        @"^rgba?\s*\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    /// <summary>
    /// Maps a stored colour onto the zero-based palette slot: a default-palette value
    /// (case-insensitive), or the pre-phase-1 role-6 colour <c>#ca8a04</c> kept by stored
    /// templates, onto slot 5.
    /// </summary>
    private static bool TryGetPaletteTokenIndex(string? color, out int index)
    {
        for (var i = 0; i < DefaultColors.Count; i++)
        {
            if (string.Equals(DefaultColors[i], color, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }

        if (string.Equals(LegacyRole6Color, color, StringComparison.OrdinalIgnoreCase))
        {
            index = DefaultColors.Count - 1;
            return true;
        }

        index = -1;
        return false;
    }

    private static double Linearize(double channel)
    {
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static bool TryFindRole(
        IReadOnlyList<SigningSubmitterRole> roles,
        string? submitterUuid,
        out SigningSubmitterRole role,
        out int order)
    {
        role = null!;
        order = -1;
        if (roles.Count == 0 || string.IsNullOrWhiteSpace(submitterUuid))
        {
            return false;
        }

        for (var i = 0; i < roles.Count; i++)
        {
            if (string.Equals(roles[i].Uuid, submitterUuid, StringComparison.Ordinal))
            {
                role = roles[i];
                order = i;
                return true;
            }
        }

        return false;
    }
}
