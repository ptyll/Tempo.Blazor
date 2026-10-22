using System.Globalization;
using System.Text.RegularExpressions;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// The ONE place where a model value becomes a CSS declaration on the WYSIWYG canvas (Fáze 9A).
/// <para>
/// The canvas paints untrusted model data (an imported MJML template, a JSON payload from a host) through
/// a <c>style</c> attribute, and a <c>style</c> attribute is a declaration list: concatenating a raw value
/// lets a crafted one close a declaration and add another (the classic
/// <c>font-family: x; background-image: url(https://tracker/…)</c>). Every value is therefore parsed
/// against a strict allow-list — a length, an allow-listed colour (<see cref="EmailColorPolicy"/>), one of
/// a fixed keyword set, or a plain font stack — and anything else is DROPPED, never escaped into
/// something that still parses.
/// </para>
/// <para>
/// URLs are handled separately and never reach CSS at all: <see cref="Url"/> answers an allow-listed URL
/// (<see cref="EmailUrlPolicy"/>, the same list the render pipeline uses) and the canvas paints it as an
/// <c>&lt;img&gt;</c>, so no <c>url(…)</c> expression is ever built from model data.
/// </para>
/// </summary>
internal static class EmailCanvasValue
{
    /// <summary>A CSS length: a number with a unit (<c>16px</c>, <c>100%</c>, <c>1.5rem</c>).</summary>
    private static readonly Regex LengthPattern = new(
        @"^(?:0|-?\d+(?:\.\d+)?(?:px|%|em|rem|pt|vw|vh))$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    /// <summary>A unitless number (a <c>line-height</c> multiplier, a <c>font-weight</c>).</summary>
    private static readonly Regex NumberPattern = new(
        @"^\d+(?:\.\d+)?$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// A font stack: names, quoted names and commas — never <c>;</c>, <c>(</c>, <c>)</c> or <c>:</c>. A quoted
    /// name must be BALANCED (review fix): the old pattern accepted a lone <c>"</c>/<c>'</c>, and a value with
    /// an unbalanced quote escapes the declaration it is interpolated into, so the rest of the value became
    /// CSS the author never wrote.
    /// </summary>
    private static readonly Regex FontFamilyPattern = new(
        """^(?:[A-Za-z0-9 _-]+|'[A-Za-z0-9 _-]+'|"[A-Za-z0-9 _-]+")(?:\s*,\s*(?:[A-Za-z0-9 _-]+|'[A-Za-z0-9 _-]+'|"[A-Za-z0-9 _-]+"))*$""",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>The longest font stack the canvas paints (the value is bounded before the regex runs).</summary>
    private const int MaxFontFamilyLength = 200;

    private static readonly HashSet<string> BorderStyles = new(StringComparer.OrdinalIgnoreCase)
        { "none", "solid", "dashed", "dotted", "double" };

    private static readonly HashSet<string> Alignments = new(StringComparer.OrdinalIgnoreCase)
        { "left", "center", "right", "justify" };

    private static readonly HashSet<string> VerticalAlignments = new(StringComparer.OrdinalIgnoreCase)
        { "top", "middle", "bottom" };

    private static readonly HashSet<string> FontStyles = new(StringComparer.OrdinalIgnoreCase)
        { "normal", "italic", "oblique" };

    private static readonly HashSet<string> FontWeights = new(StringComparer.OrdinalIgnoreCase)
        { "normal", "bold", "lighter", "bolder", "100", "200", "300", "400", "500", "600", "700", "800", "900" };

    private static readonly HashSet<string> TextDecorations = new(StringComparer.OrdinalIgnoreCase)
        { "none", "underline", "line-through", "overline" };

    private static readonly HashSet<string> TextTransforms = new(StringComparer.OrdinalIgnoreCase)
        { "none", "uppercase", "lowercase", "capitalize" };

    /// <summary>A validated CSS length, or <see langword="null"/> when the value is not one.</summary>
    public static string? Length(string? value)
        => value is not null && LengthPattern.IsMatch(value.Trim()) ? value.Trim() : null;

    /// <summary>A length that only accepts a <c>px</c> value, returned as the bare number (an <c>img</c> width).</summary>
    public static string? PixelNumber(string? value)
    {
        var length = Length(value);
        return length is not null && length.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            ? length[..^2]
            : null;
    }

    /// <summary>A validated colour from the pipeline's colour allow-list, or <see langword="null"/>.</summary>
    public static string? Color(string? value)
        => EmailColorPolicy.IsAllowed(value) ? value!.Trim() : null;

    /// <summary>A validated font stack, or <see langword="null"/>.</summary>
    public static string? FontFamily(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        return trimmed.Length is > 0 and <= MaxFontFamilyLength && FontFamilyPattern.IsMatch(trimmed)
            ? trimmed
            : null;
    }

    /// <summary>A unitless number (a <c>line-height</c> multiplier), or <see langword="null"/>.</summary>
    public static string? Number(string? value)
        => value is not null && NumberPattern.IsMatch(value.Trim()) ? value.Trim() : null;

    /// <summary>A <c>line-height</c>: a multiplier or a length.</summary>
    public static string? LineHeight(string? value) => Number(value) ?? Length(value);

    /// <summary>One of the four horizontal alignments, or <see langword="null"/>.</summary>
    public static string? Align(string? value) => Keyword(value, Alignments);

    /// <summary>One of the three vertical alignments, or <see langword="null"/>.</summary>
    public static string? VerticalAlign(string? value) => Keyword(value, VerticalAlignments);

    /// <summary>One of the CSS font styles, or <see langword="null"/>.</summary>
    public static string? FontStyle(string? value) => Keyword(value, FontStyles);

    /// <summary>A CSS font weight (a keyword or 100–900), or <see langword="null"/>.</summary>
    public static string? FontWeight(string? value) => Keyword(value, FontWeights);

    /// <summary>One of the CSS text decorations, or <see langword="null"/>.</summary>
    public static string? TextDecoration(string? value) => Keyword(value, TextDecorations);

    /// <summary>One of the CSS text transforms, or <see langword="null"/>.</summary>
    public static string? TextTransform(string? value) => Keyword(value, TextTransforms);

    /// <summary>
    /// A <c>padding</c> shorthand of one to four validated lengths (<c>10px</c>, <c>10px 25px</c>, …), or
    /// <see langword="null"/>. A value with any non-length part is dropped as a whole — half a shorthand is
    /// not the author's intent.
    /// </summary>
    public static string? Padding(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 4) return null;
        return parts.All(part => Length(part) is not null)
            ? string.Join(' ', parts)
            : null;
    }

    /// <summary>
    /// A shorthand border (<c>2px solid #000000</c>), or <see langword="null"/>. <c>none</c> alone is a
    /// valid border.
    /// </summary>
    public static string? Border(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase)) return "none";

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return null;
        var width = Length(parts[0]);
        var style = BorderStyles.Contains(parts[1]) ? parts[1].ToLowerInvariant() : null;
        var color = Color(parts[2]);
        return width is not null && style is not null && color is not null
            ? $"{width} {style} {color}"
            : null;
    }

    /// <summary>A CSS border style keyword, or <see langword="null"/>.</summary>
    public static string? BorderStyle(string? value) => Keyword(value, BorderStyles);

    /// <summary>
    /// An allow-listed URL (<c>http</c>/<c>https</c>/<c>mailto</c>/<c>tel</c>/relative), or
    /// <see langword="null"/> — the canvas never turns a refused value into a request.
    /// </summary>
    public static string? Url(string? value)
        => EmailUrlPolicy.IsAllowed(value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    /// <summary>
    /// The value the canvas may paint as an <c>&lt;img&gt;</c>, or <see langword="null"/>.
    /// <para>
    /// <b>Stricter than <see cref="Url"/> on purpose (review fix).</b> The pipeline's allow-list answers "may
    /// this be a LINK in the message", which legitimately includes <c>mailto:</c>, <c>tel:</c> and a relative
    /// path. An image is a REQUEST, so the canvas accepts only what a browser can fetch as an image: an
    /// absolute <c>http</c>/<c>https</c> URL without a template token. A relative value such as
    /// <c>/api/logout</c> (an import or a JSON payload can carry any string) used to become a GET against the
    /// editor's own origin; <c>mailto:</c>/<c>tel:</c> became a request that can only fail. Neither may happen,
    /// so the caller shows a placeholder instead.
    /// </para>
    /// </summary>
    public static string? ImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!EmailUrlPolicy.IsAllowed(value)) return null;

        var trimmed = value.Trim();
        // A token is never substituted on the canvas, so it cannot be resolved into an image source either.
        if (trimmed.Contains("{{", StringComparison.Ordinal) || trimmed.Contains("{%", StringComparison.Ordinal))
        {
            return null;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }

    /// <summary>
    /// Joins the given declarations into a <c>style</c> attribute value, skipping the ones that did not
    /// validate (a <see langword="null"/> value is "this value may not be painted", never "empty").
    /// </summary>
    public static string Style(params (string Name, string? Value)[] declarations)
    {
        var parts = new List<string>(declarations.Length);
        foreach (var (name, value) in declarations)
        {
            if (!string.IsNullOrEmpty(value))
            {
                parts.Add(name + ":" + value);
            }
        }

        return parts.Count == 0 ? string.Empty : string.Join(';', parts);
    }

    private static string? Keyword(string? value, HashSet<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return allowed.Contains(trimmed) ? trimmed.ToLowerInvariant() : null;
    }

    /// <summary>
    /// The effective padding declarations of a block. MJML semantics: a specific <c>padding-top</c> (etc.)
    /// wins over that side of the shorthand, so a document that overrides one side keeps the other three
    /// from <c>padding</c>. When nothing overrides a side the shorthand is emitted as written.
    /// </summary>
    public static string PaddingStyle(string? shorthand, string? top, string? right, string? bottom, string? left)
    {
        var parsed = Padding(shorthand);
        var overrides = (Length(top), Length(right), Length(bottom), Length(left));
        if (overrides is (null, null, null, null))
        {
            return Style(("padding", parsed));
        }

        var sides = Expand(parsed);
        return Style(
            ("padding-top", overrides.Item1 ?? sides[0]),
            ("padding-right", overrides.Item2 ?? sides[1]),
            ("padding-bottom", overrides.Item3 ?? sides[2]),
            ("padding-left", overrides.Item4 ?? sides[3]));
    }

    /// <summary>Expands a CSS padding shorthand (1–4 values) into top/right/bottom/left.</summary>
    private static string?[] Expand(string? shorthand)
    {
        var parts = shorthand?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        return parts.Length switch
        {
            1 => [parts[0], parts[0], parts[0], parts[0]],
            2 => [parts[0], parts[1], parts[0], parts[1]],
            3 => [parts[0], parts[1], parts[2], parts[1]],
            4 => [parts[0], parts[1], parts[2], parts[3]],
            _ => [null, null, null, null],
        };
    }

    /// <summary>Formats a number for CSS without a culture-specific decimal separator.</summary>
    public static string Invariant(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
