using System.Text.RegularExpressions;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The closed set of MJML components and attributes this pipeline may RE-EMIT, plus the kind of value each
/// attribute carries.
/// </summary>
/// <remarks>
/// <para>
/// An attribute preserved from an import (<see cref="Model.Blocks.EmailBlockBase.ExtraAttributes"/>), a
/// per-component default (<c>mj-attributes</c>) and a host-set HTML attribute all come from a foreign document
/// or from a crafted JSON payload, and none of them is a value the editor authored. A DENY filter was used
/// originally (reject <c>on*</c> and <c>style</c>); a deny filter is one invented attribute name away from a
/// live handler (<c>srcdoc</c>, <c>formaction</c>, <c>xlink:href</c>,…), so the rule is the opposite: a NAME is
/// emitted only when it is a known attribute of that tag (or an inert <c>data-*</c>), and the VALUE is judged by
/// the kind of the attribute — a URL through <see cref="EmailUrlPolicy"/>, a colour through
/// <see cref="CssColourValue"/>, a style-bound value through <see cref="CssStyleValue"/>, and anything else
/// through the rule that stops a value from opening a CSS declaration of its own.
/// </para>
/// <para>
/// The SAME predicates serve the importer (which WARNS about the attributes it had to drop, so the loss is
/// visible) and the generator (which drops them), so "refused at import" and "not emitted at export" can never
/// disagree.
/// </para>
/// </remarks>
internal static class MjmlKnownAttributes
{
    /// <summary>Attributes every MJML component accepts (the <c>mj-class</c> / padding / box model set).</summary>
    private static readonly HashSet<string> Universal = Set(
        "css-class", "mj-class", "container-background-color",
        "padding", "padding-top", "padding-right", "padding-bottom", "padding-left");

    /// <summary>The attributes of each MJML component this pipeline supports.</summary>
    private static readonly Dictionary<string, HashSet<string>> ByTag = new(StringComparer.Ordinal)
    {
        ["mj-body"] = Set("width", "background-color"),
        ["mj-section"] = Set(
            "background-color", "background-url", "background-position", "background-repeat", "background-size",
            "border", "border-radius", "border-top", "border-right", "border-bottom", "border-left",
            "direction", "full-width", "text-align", "text-padding"),
        ["mj-column"] = Set(
            "width", "vertical-align", "background-color", "border", "border-radius",
            "border-top", "border-right", "border-bottom", "border-left",
            "inner-background-color", "direction", "text-align"),
        ["mj-group"] = Set("width", "vertical-align", "direction", "background-color", "text-align"),
        ["mj-wrapper"] = Set(
            "background-color", "background-url", "background-position", "background-repeat", "background-size",
            "border", "border-radius", "border-top", "border-right", "border-bottom", "border-left",
            "direction", "full-width", "text-align", "text-padding"),
        ["mj-text"] = Set(
            "color", "font-family", "font-size", "font-style", "font-weight", "letter-spacing", "line-height",
            "height", "text-decoration", "text-transform", "align", "vertical-align"),
        ["mj-button"] = Set(
            "href", "rel", "target", "title", "background-color", "color", "font-family", "font-size",
            "font-style", "font-weight", "letter-spacing", "line-height", "text-align", "text-decoration",
            "text-transform", "align", "vertical-align", "border", "border-radius",
            "border-top", "border-right", "border-bottom", "border-left", "inner-padding", "width", "height",
            "text-padding"),
        ["mj-image"] = Set(
            "src", "srcset", "sizes", "href", "rel", "target", "title", "alt", "align", "width", "height",
            "border", "border-radius", "border-top", "border-right", "border-bottom", "border-left",
            "fluid-on-mobile", "text-align"),
        ["mj-divider"] = Set("border-color", "border-style", "border-width", "width", "align", "text-align"),
        ["mj-spacer"] = Set("height", "text-align"),
        ["mj-table"] = Set(
            "align", "border", "cellpadding", "cellspacing", "color", "font-family", "font-size", "line-height",
            "letter-spacing", "table-layout", "width", "text-align"),
        ["mj-social"] = Set(
            "mode", "align", "icon-size", "icon-height", "border-radius", "color", "font-size", "font-family",
            "font-style", "font-weight", "line-height", "text-padding", "text-decoration", "icon-padding",
            "inner-padding", "text-align"),
        ["mj-social-element"] = Set(
            "name", "href", "src", "srcset", "alt", "title", "target", "rel", "background-color", "color",
            "icon-size", "icon-height", "font-size", "font-family", "font-style", "font-weight", "line-height",
            "text-decoration", "icon-padding"),
        ["mj-navbar"] = Set("align", "base-url", "hamburger", "text-align"),
        ["mj-navbar-link"] = Set(
            "href", "target", "rel", "color", "font-family", "font-size", "font-style", "font-weight",
            "letter-spacing", "line-height", "text-decoration", "text-transform", "padding", "text-align"),
        ["mj-carousel"] = Set(
            "align", "border-radius", "icon-width", "left-icon", "right-icon", "thumbnails", "tb-border",
            "tb-border-radius", "tb-hover-border-color", "tb-selected-border-color", "tb-width", "text-align"),
        ["mj-carousel-image"] = Set("src", "alt", "href", "rel", "target", "title", "thumbnails-src"),
        ["mj-accordion"] = Set(
            "border", "icon-align", "icon-position", "icon-height", "icon-width", "icon-wrapped-url",
            "icon-unwrapped-url", "font-family", "text-align"),
        ["mj-accordion-element"] = Set(
            "background-color", "border", "font-family", "icon-align", "icon-position", "icon-height",
            "icon-width", "icon-wrapped-url", "icon-unwrapped-url"),
        ["mj-accordion-title"] = Set(
            "background-color", "color", "font-family", "font-size", "letter-spacing", "line-height", "padding"),
        ["mj-accordion-text"] = Set(
            "background-color", "color", "font-family", "font-size", "font-weight", "letter-spacing",
            "line-height", "padding", "text-decoration"),
        ["mj-hero"] = Set(
            "background-color", "background-url", "background-width", "background-height", "background-position",
            "mode", "height", "vertical-align", "padding", "text-align"),
        ["mj-font"] = Set("name", "href"),
        ["mj-raw"] = Set(),
        ["mj-breakpoint"] = Set("width"),
        ["mj-style"] = Set("inline"),
        ["mj-selector"] = Set("path"),
        ["mj-html-attribute"] = Set("name"),
    };

    /// <summary>
    /// The components a per-component default (<c>mj-attributes</c> child) may name. A HEAD element is not one:
    /// a key like <c>mj-breakpoint</c> would emit a second breakpoint instead of a default.
    /// </summary>
    private static readonly HashSet<string> PerTagTargets = BuildPerTagTargets();

    /// <summary>Every known attribute; the set of <c>mj-all</c>, which applies to every component.</summary>
    private static readonly HashSet<string> All = BuildAll();

    /// <summary>Attribute names whose value the compiler copies into a CSS declaration.</summary>
    private static readonly HashSet<string> StyleBound = Set(
        "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "border", "border-top", "border-right", "border-bottom", "border-left",
        "border-radius", "border-color", "border-style", "border-width", "tb-border", "tb-border-color",
        "font-family", "font-size", "font-style", "font-weight", "line-height", "letter-spacing",
        "width", "height", "align", "vertical-align", "text-align", "direction", "table-layout",
        "cellpadding", "cellspacing", "background-position", "background-repeat", "background-size",
        "inner-padding", "text-padding", "icon-size", "icon-height", "icon-width", "icon-align",
        "icon-position", "text-decoration", "text-transform", "mode", "thumbnails", "fluid-on-mobile",
        "full-width", "sizes", "background-width", "background-height", "hamburger", "breakpoint");

    /// <summary>Attribute names whose value is a single colour.</summary>
    private static readonly HashSet<string> Colours = Set(
        "color", "background-color", "container-background-color", "border-color", "bgcolor",
        "inner-background-color", "text-color", "icon-color", "tb-border-color", "tb-hover-border-color",
        "tb-selected-border-color");

    /// <summary>An attribute name a MJML consumer accepts: lower-case letters, digits and dashes.</summary>
    private static readonly Regex SafeNamePattern = new(
        "^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The presentational HTML attributes an e-mail may set through <c>mj-html-attributes</c>.</summary>
    private static readonly HashSet<string> HtmlAttributes = Set(
        "class", "id", "lang", "dir", "title", "role", "href", "src", "alt", "target", "rel", "width", "height",
        "align", "valign", "bgcolor", "border", "cellpadding", "cellspacing", "colspan", "rowspan", "name",
        "value", "type", "tabindex", "download", "hreflang", "referrerpolicy");

    /// <summary>An <c>aria-*</c> attribute name (letters, digits and dashes only).</summary>
    private static readonly Regex HtmlAriaPattern = new(
        "^aria-[a-z0-9][a-z0-9-]{0,47}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>An inert <c>data-*</c> attribute (no consumer can act on it).</summary>
    private static readonly Regex DataNamePattern = new(
        "^data-[a-z0-9][a-z0-9-]{0,47}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Answers whether <paramref name="tag"/> names a component a per-component default may target.</summary>
    public static bool IsKnownTag(string? tag) => tag is not null && PerTagTargets.Contains(tag);

    /// <summary>
    /// The attribute names <paramref name="tag"/> accepts: the tag's own set plus the universal set. An unknown
    /// tag accepts nothing — a tag the pipeline does not recognise must not be emitted at all.
    /// </summary>
    public static bool IsAllowed(string? tag, string? name)
    {
        if (!IsSafeName(name)) return false;

        if (Universal.Contains(name!) || DataNamePattern.IsMatch(name)) return true;
        if (tag is null or "mj-all" or "mj-class") return All.Contains(name);
        return ByTag.TryGetValue(tag, out var set) && set.Contains(name);
    }

    /// <summary>
    /// Answers whether the value may be emitted into an attribute of <paramref name="tag"/>: a URL name goes
    /// through the URL allow-list (and is always emittable, blocked values become <c>#</c>), a colour and a
    /// style-bound value have to be one, and any other value may not open a CSS declaration of its own.
    /// </summary>
    public static bool IsSafeValue(string? name, string? value)
    {
        if (string.IsNullOrEmpty(name) || value is null) return true;
        // A URL is always emittable: a blocked scheme becomes '#' (the caller rewrites it) or is dropped by the
        // importer with a warning — it is never written as the value the model carried.
        if (IsUrl(name)) return true;
        if (IsStyleBound(name)) return CssStyleValue.IsPlausible(value, name);
        if (Colours.Contains(name)) return CssColourValue.IsPlausible(value);
        return IsSafeToken(value);
    }

    /// <summary>Answers whether the attribute carries a URL (and therefore has to pass the URL allow-list).</summary>
    public static bool IsUrl(string? name) => name is not null && MarkupGuard.IsUrlAttributeName(name);

    /// <summary>Answers whether the name is a plain lower-case attribute name (never <c>on*</c>/<c>style</c>).</summary>
    public static bool IsSafeName(string? name)
        => !string.IsNullOrEmpty(name)
           && SafeNamePattern.IsMatch(name)
           && !name.StartsWith("on", StringComparison.Ordinal)
           && !string.Equals(name, "style", StringComparison.Ordinal)
           && !string.Equals(name, "srcdoc", StringComparison.Ordinal);

    /// <summary>
    /// The rule for a value of an attribute of unknown kind: it may not carry a character or a function that
    /// would end the attribute or start a CSS declaration of its own
    /// (<c>x;background:url(…)</c>, <c>expression(…)</c>).
    /// </summary>
    public static bool IsSafeToken(string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        if (value.Length > 2048) return false;

        foreach (var c in value)
        {
            if (c is ';' or '{' or '}' or '<' or '>' or '"' or '\'' or '\\') return false;
        }

        var lower = value.ToLowerInvariant();
        return !lower.Contains("url(", StringComparison.Ordinal)
            && !lower.Contains("expression(", StringComparison.Ordinal)
            && !lower.Contains("javascript:", StringComparison.Ordinal)
            && !lower.Contains("@import", StringComparison.Ordinal);
    }

    /// <summary>Answers whether the attribute's value ends up in a CSS declaration (and is style-bound).</summary>
    public static bool IsStyleBound(string? name) => name is not null && StyleBound.Contains(name);

    /// <summary>Answers whether the attribute's value is a colour.</summary>
    public static bool IsColour(string? name) => name is not null && Colours.Contains(name);

    /// <summary>
    /// A class list reduced to the characters a CSS class token may use. A class name is not a script sink, but
    /// a value carrying quotes and spaces is not a class list either — and it would still be quoted into every
    /// downstream attribute.
    /// </summary>
    public static string? SafeClassList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => SafeClassTokenPattern.Replace(token, string.Empty))
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return tokens.Count == 0 ? null : string.Join(' ', tokens);
    }

    /// <summary>
    /// Answers whether a host may set <paramref name="name"/> through <c>mj-html-attributes</c>: an inert
    /// <c>data-*</c>/<c>aria-*</c>, or one of the presentational HTML attributes an e-mail actually needs. Never
    /// <c>style</c>, never an event handler, never <c>srcdoc</c>.
    /// </summary>
    public static bool IsAllowedHtmlAttribute(string? name)
    {
        if (!IsSafeName(name)) return false;

        var lower = name!;
        if (lower.StartsWith("data-", StringComparison.Ordinal)) return DataNamePattern.IsMatch(lower);
        if (lower.StartsWith("aria-", StringComparison.Ordinal)) return HtmlAriaPattern.IsMatch(lower);

        return HtmlAttributes.Contains(lower);
    }

    /// <summary>The characters a class token may keep (everything else is removed).</summary>
    private static readonly Regex SafeClassTokenPattern = new(
        "[^A-Za-z0-9_-]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static HashSet<string> BuildPerTagTargets()
    {
        var targets = new HashSet<string>(ByTag.Keys, StringComparer.Ordinal)
        {
            "mj-all",
        };
        targets.Remove("mj-breakpoint");
        targets.Remove("mj-font");
        targets.Remove("mj-style");
        targets.Remove("mj-raw");
        targets.Remove("mj-selector");
        targets.Remove("mj-html-attribute");
        return targets;
    }

    private static HashSet<string> BuildAll()
    {
        var all = new HashSet<string>(Universal, StringComparer.Ordinal);
        foreach (var set in ByTag.Values)
        {
            foreach (var name in set)
            {
                all.Add(name);
            }
        }

        return all;
    }

    private static HashSet<string> Set(params string[] names) => new(names, StringComparer.Ordinal);
}
