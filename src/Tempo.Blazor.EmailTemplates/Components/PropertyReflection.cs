using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>The kind of editor a property maps to.</summary>
public enum PropertyFieldKind
{
    /// <summary>Single-line text.</summary>
    Text,

    /// <summary>Multi-line text (e.g. HTML content, CSS).</summary>
    MultilineText,

    /// <summary>A boolean toggle.</summary>
    Bool,

    /// <summary>An integer number.</summary>
    Number,

    /// <summary>A CSS colour, edited with a colour picker (swatch + hex).</summary>
    Color,

    /// <summary>A left/center/right/justify value, edited with toggle buttons (<c>aria-pressed</c>).</summary>
    Alignment,

    /// <summary>One of a fixed set of MJML attribute values, edited with a select.</summary>
    Choice,

    /// <summary>A single CSS length (number + unit, e.g. <c>16px</c> or <c>100%</c>).</summary>
    Length,
}

/// <summary>Where a field belongs in the inspector's Content/Style tabs and its collapsible groups.</summary>
public enum PropertyFieldPlacement
{
    /// <summary>Texts, URLs, alt texts and items — the Content tab.</summary>
    Content,

    /// <summary>Colours, fonts, alignment, dimensions — the Style tab.</summary>
    Style,

    /// <summary>Spacing fields, grouped under the collapsible "Spacing" heading of the Style tab.</summary>
    Padding,

    /// <summary>Fields that rarely change, grouped under the collapsible "Advanced" heading.</summary>
    Advanced,
}

/// <summary>One option of a <see cref="PropertyFieldKind.Choice"/> field: the stored MJML value and its label key.</summary>
/// <param name="Value">The value written into the model (e.g. <c>_blank</c>, <c>fluid-height</c>).</param>
/// <param name="LabelKey">The localization key of the human-readable option label.</param>
public sealed record PropertyChoice(string Value, string LabelKey);

/// <summary>A single editable scalar property of a model object, with reflective get/set and its editor metadata.</summary>
public sealed class PropertyField
{
    private readonly object _target;
    private readonly PropertyInfo _property;

    internal PropertyField(
        object target,
        PropertyInfo property,
        string owner,
        PropertyFieldKind kind,
        PropertyFieldPlacement placement,
        IReadOnlyList<PropertyChoice> choices,
        bool supportsTemplate,
        bool allowsNegative = false)
    {
        _target = target;
        _property = property;
        Owner = owner;
        Kind = kind;
        Placement = placement;
        Choices = choices;
        SupportsTemplate = supportsTemplate;
        AllowsNegative = allowsNegative;
    }

    /// <summary>Gets the property name (also used as the stable editor identifier).</summary>
    public string Name => _property.Name;

    /// <summary>Gets the editor kind.</summary>
    public PropertyFieldKind Kind { get; }

    /// <summary>
    /// Gets the owner token of the label key — the model type without its <c>Email</c> prefix and
    /// <c>Block</c> suffix (e.g. <c>Button</c>, <c>Section</c>, <c>Styles</c>).
    /// </summary>
    public string Owner { get; }

    /// <summary>Gets where the field belongs in the inspector (Content/Style tabs, Spacing/Advanced groups).</summary>
    public PropertyFieldPlacement Placement { get; }

    /// <summary>Gets the options of a <see cref="PropertyFieldKind.Choice"/> or <see cref="PropertyFieldKind.Alignment"/> field.</summary>
    public IReadOnlyList<PropertyChoice> Choices { get; }

    /// <summary>
    /// Gets whether the property holds a template-bearing string, so the inspector offers the
    /// "Insert variable" affordance next to it.
    /// </summary>
    public bool SupportsTemplate { get; }

    /// <summary>
    /// Gets whether a <see cref="PropertyFieldKind.Length"/> field may hold a negative number. Only letter
    /// spacing does — a negative font size or width is not a legal CSS length.
    /// </summary>
    public bool AllowsNegative { get; }

    /// <summary>Gets the owner-specific localization key of the field label.</summary>
    public string LabelKey => $"EmailEditor_Field_{Owner}_{Name}";

    /// <summary>Gets the shared localization key used when the owner adds no meaning to the label.</summary>
    public string SharedLabelKey => $"EmailEditor_Field_{Name}";

    /// <summary>Gets the current value as text (empty when null).</summary>
    public string GetText() => _property.GetValue(_target)?.ToString() ?? string.Empty;

    /// <summary>Sets the text value (stores null for empty on nullable string properties).</summary>
    public void SetText(string? value)
    {
        var isNullable = Nullable.GetUnderlyingType(_property.PropertyType) is not null
            || _property.PropertyType == typeof(string);
        _property.SetValue(_target, string.IsNullOrEmpty(value) && isNullable ? null : value);
    }

    /// <summary>Gets the current boolean value.</summary>
    public bool GetBool() => _property.GetValue(_target) is true;

    /// <summary>Sets the boolean value.</summary>
    public void SetBool(bool value) => _property.SetValue(_target, value);

    /// <summary>Gets the current number value.</summary>
    public int? GetNumber() => _property.GetValue(_target) as int?;

    /// <summary>Sets the number value.</summary>
    public void SetNumber(int? value)
    {
        if (_property.PropertyType == typeof(int)) _property.SetValue(_target, value ?? 0);
        else _property.SetValue(_target, value);
    }

    /// <summary>
    /// Parses the current value as a single CSS length. Returns <see langword="false"/> when the stored
    /// value is not one (an empty value of a nullable property is a valid "unset").
    /// </summary>
    public bool TryGetLength(out decimal number, out string unit)
        => PropertyReflection.TryParseLength(GetText(), out number, out unit);

    /// <summary>Writes a CSS length composed of <paramref name="number"/> and <paramref name="unit"/>.</summary>
    /// <exception cref="ArgumentException">The composed value is not a single CSS length.</exception>
    public void SetLength(decimal number, string unit)
    {
        var value = PropertyReflection.FormatLength(number, unit);
        if (!PropertyReflection.TryParseLength(value, out _, out _))
            throw new ArgumentException($"'{value}' is not a single CSS length.", nameof(unit));
        SetText(value);
    }

    /// <summary>
    /// Whether the property may be empty: a <c>string?</c> (nullable reference annotation) or a nullable
    /// value type. Nullable reference annotations are metadata, not a different <see cref="Type"/>, hence the
    /// nullability context. An empty value of a nullable property means "unset"; on a required one it is an
    /// error and the stored value stays.
    /// </summary>
    public bool IsNullable()
        => Nullability?.WriteState == NullabilityState.Nullable;

    private NullabilityInfo? _nullability;

    private NullabilityInfo? Nullability => _nullability ??= new NullabilityInfoContext().Create(_property);

    /// <summary>Sets a choice/alignment value; an unknown token is rejected instead of written.</summary>
    /// <exception cref="ArgumentException">The value is not one of <see cref="Choices"/>.</exception>
    public void SetChoice(string value)
    {
        if (Choices.Count > 0 && !Choices.Any(choice => string.Equals(choice.Value, value, StringComparison.Ordinal)))
            throw new ArgumentException($"'{value}' is not one of the options of {Name}.", nameof(value));
        SetText(value);
    }
}

/// <summary>
/// Enumerates the editable scalar properties of email model objects via reflection and classifies each one
/// into an editor kind, an inspector placement and (for enum-like MJML attributes) its option list.
/// <para>
/// One table drives the mapping — property name first, with owner-specific overrides where the same MJML
/// attribute means different values per block (e.g. <c>Mode</c> is fluid/fixed height on a hero and
/// horizontal/vertical on social). A property the table does not know stays a plain text field in the
/// Content tab, so a host's custom block property is never silently hidden.
/// </para>
/// </summary>
public static class PropertyReflection
{
    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal)
    {
        "Id", "Type", "MjClasses", "ExtraAttributes", "SchemaVersion", "CreatedAt", "UpdatedAt",
    };

    private static readonly Regex LengthPattern = new(
        @"^(?<number>-?\d+(?:\.\d+)?)\s*(?<unit>px|%|em|rem|pt)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    private static readonly Regex HexColorPattern = new(
        @"^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Regex ColorFunctionPattern = new(
        @"^(?:rgb|rgba|hsl|hsla)\([0-9a-zA-Z.,%\s/+-]+\)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    private static readonly HashSet<string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "transparent", "currentcolor", "inherit", "black", "white", "red", "green", "blue", "yellow",
        "orange", "purple", "pink", "gray", "grey", "silver", "maroon", "olive", "lime", "aqua", "teal",
        "navy", "fuchsia", "darkblue", "darkgray", "darkgrey", "lightgray", "lightgrey", "steelblue",
    };

    private static readonly string[] AlignValues = ["left", "center", "right", "justify"];
    private static readonly string[] VerticalValues = ["top", "middle", "bottom"];
    private static readonly string[] TargetValues = ["_blank", "_self", "_parent", "_top"];

    private sealed record Rule(
        PropertyFieldKind Kind,
        PropertyFieldPlacement Placement,
        string? ChoiceFamily = null,
        string[]? ChoiceValues = null,
        bool Template = false,
        bool AllowNegative = false);

    // The table: `Owner.Property` wins over `Property`. Everything absent is a Content-tab text field.
    private static readonly Dictionary<string, Rule> Rules = BuildRules();

    /// <summary>Returns the editable scalar fields of the target object, in declaration order.</summary>
    public static IReadOnlyList<PropertyField> GetFields(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var owner = OwnerOf(target);
        var fields = new List<PropertyField>();
        foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || Excluded.Contains(property.Name)) continue;

            var type = property.PropertyType;
            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            var rule = Resolve(owner, property.Name);
            var kind = rule?.Kind ?? DefaultKind(underlying, property.Name);
            if (kind is null) continue;

            var choiceValues = rule?.ChoiceValues ?? (kind == PropertyFieldKind.Alignment ? AlignValues : null);
            var choices = choiceValues is null
                ? Array.Empty<PropertyChoice>()
                : choiceValues
                    .Select(value => new PropertyChoice(
                        value, $"EmailEditor_Choice_{rule!.ChoiceFamily}_{TrimValue(value)}"))
                    .ToArray();

            fields.Add(new PropertyField(
                target, property, owner, kind.Value,
                rule?.Placement ?? (kind is PropertyFieldKind.Text or PropertyFieldKind.MultilineText
                    ? PropertyFieldPlacement.Content
                    : PropertyFieldPlacement.Style),
                choices,
                rule?.Template ?? false,
                rule?.AllowNegative ?? false));
        }

        return fields;
    }

    /// <summary>Returns the editable scalar fields of a single inspector placement.</summary>
    public static IReadOnlyList<PropertyField> GetFields(object target, PropertyFieldPlacement placement)
        => GetFields(target).Where(field => field.Placement == placement).ToList();

    /// <summary>
    /// The owner token of the label keys: the model type without its <c>Email</c> prefix and <c>Block</c>
    /// suffix (<c>EmailButtonBlock</c> → <c>Button</c>), with the document types named explicitly.
    /// </summary>
    public static string OwnerOf(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var type = target.GetType();

        if (type.Name == "EmailTemplateDocument") return "Document";
        if (type.Name == "TemplateStyles") return "Styles";

        var name = type.Name;
        if (name.StartsWith("Email", StringComparison.Ordinal)) name = name["Email".Length..];
        if (name.EndsWith("Block", StringComparison.Ordinal)) name = name[..^"Block".Length];
        return name;
    }

    /// <summary>Parses a single CSS length (<c>16px</c>, <c>100%</c>, <c>1.5rem</c>) into its number and unit.</summary>
    /// <param name="value">The stored value.</param>
    /// <param name="number">The numeric part when valid.</param>
    /// <param name="unit">The unit when valid.</param>
    /// <returns><see langword="true"/> when the value is one length; an empty value is never one.</returns>
    public static bool TryParseLength(string? value, out decimal number, out string unit)
    {
        number = 0;
        unit = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var match = LengthPattern.Match(value.Trim());
        if (!match.Success) return false;

        if (!decimal.TryParse(match.Groups["number"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
            return false;

        unit = match.Groups["unit"].Value.ToLowerInvariant();
        return true;
    }

    /// <summary>Formats a length for storage (<c>16</c> + <c>px</c> → <c>16px</c>), invariant culture.</summary>
    public static string FormatLength(decimal number, string unit)
        => number.ToString("0.####", CultureInfo.InvariantCulture) + unit;

    /// <summary>
    /// Whether <paramref name="value"/> is a CSS colour the picker may own: a hex literal, an
    /// <c>rgb()</c>/<c>hsl()</c> function, or a well-known keyword. Anything else is left to the text
    /// editor with a localized error, so garbage from an import is visible and fixable.
    /// </summary>
    public static bool IsValidColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        return HexColorPattern.IsMatch(trimmed)
            || ColorFunctionPattern.IsMatch(trimmed)
            || NamedColors.Contains(trimmed);
    }

    private static Rule? Resolve(string owner, string property)
        => Rules.TryGetValue($"{owner}.{property}", out var ownerRule)
            ? ownerRule
            : Rules.TryGetValue(property, out var sharedRule) ? sharedRule : null;

    private static PropertyFieldKind? DefaultKind(Type underlying, string propertyName)
    {
        if (underlying == typeof(string))
            return propertyName is "Content" or "Css" ? PropertyFieldKind.MultilineText : PropertyFieldKind.Text;
        if (underlying == typeof(bool)) return PropertyFieldKind.Bool;
        if (underlying == typeof(int)) return PropertyFieldKind.Number;
        return null;
    }

    private static string TrimValue(string value) => value.TrimStart('_');

    private static Dictionary<string, Rule> BuildRules()
    {
        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);

        void Add(string property, PropertyFieldKind kind, PropertyFieldPlacement placement,
            string? family = null, string[]? values = null, bool template = false, bool allowNegative = false)
            => rules[property] = new Rule(kind, placement, family, values, template, allowNegative);

        // ── Content ──────────────────────────────────────────────────────────────────────
        Add("Content", PropertyFieldKind.MultilineText, PropertyFieldPlacement.Content, template: true);
        Add("Text", PropertyFieldKind.Text, PropertyFieldPlacement.Content, template: true);
        Add("Href", PropertyFieldKind.Text, PropertyFieldPlacement.Content, template: true);
        Add("Src", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("Alt", PropertyFieldKind.Text, PropertyFieldPlacement.Content, template: true);
        Add("Title", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("Rel", PropertyFieldKind.Text, PropertyFieldPlacement.Advanced);
        Add("Target", PropertyFieldKind.Choice, PropertyFieldPlacement.Content, "Target", TargetValues);
        Add("Name", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("Subject", PropertyFieldKind.Text, PropertyFieldPlacement.Content, template: true);
        Add("Preheader", PropertyFieldKind.Text, PropertyFieldPlacement.Content, template: true);
        Add("Language", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("BaseUrl", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("Label", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("ThumbnailsSrc", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("IconWrappedUrl", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("IconUnwrappedUrl", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("LeftIcon", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("RightIcon", PropertyFieldKind.Text, PropertyFieldPlacement.Content);
        Add("Css", PropertyFieldKind.MultilineText, PropertyFieldPlacement.Content);

        // ── Spacing group ────────────────────────────────────────────────────────────────
        Add("Padding", PropertyFieldKind.Text, PropertyFieldPlacement.Padding);
        Add("PaddingTop", PropertyFieldKind.Length, PropertyFieldPlacement.Padding);
        Add("PaddingRight", PropertyFieldKind.Length, PropertyFieldPlacement.Padding);
        Add("PaddingBottom", PropertyFieldKind.Length, PropertyFieldPlacement.Padding);
        Add("PaddingLeft", PropertyFieldKind.Length, PropertyFieldPlacement.Padding);
        Add("InnerPadding", PropertyFieldKind.Text, PropertyFieldPlacement.Padding);
        Add("TextPadding", PropertyFieldKind.Text, PropertyFieldPlacement.Padding);
        Add("CellPadding", PropertyFieldKind.Text, PropertyFieldPlacement.Padding);
        Add("CellSpacing", PropertyFieldKind.Text, PropertyFieldPlacement.Padding);

        // ── Advanced group ───────────────────────────────────────────────────────────────
        Add("CssClass", PropertyFieldKind.Text, PropertyFieldPlacement.Advanced);
        Add("VisibleWhen", PropertyFieldKind.Text, PropertyFieldPlacement.Advanced);
        Add("ContainerBackgroundColor", PropertyFieldKind.Color, PropertyFieldPlacement.Advanced);
        Add("FullWidth", PropertyFieldKind.Bool, PropertyFieldPlacement.Advanced);
        Add("FluidOnMobile", PropertyFieldKind.Text, PropertyFieldPlacement.Advanced);
        Add("Hamburger", PropertyFieldKind.Text, PropertyFieldPlacement.Advanced);
        Add("IsHeader", PropertyFieldKind.Bool, PropertyFieldPlacement.Advanced);

        // ── Style ────────────────────────────────────────────────────────────────────────
        Add("Color", PropertyFieldKind.Color, PropertyFieldPlacement.Style);
        Add("BackgroundColor", PropertyFieldKind.Color, PropertyFieldPlacement.Style);
        Add("BorderColor", PropertyFieldKind.Color, PropertyFieldPlacement.Style);
        Add("TitleColor", PropertyFieldKind.Color, PropertyFieldPlacement.Style);
        Add("BackgroundUrl", PropertyFieldKind.Text, PropertyFieldPlacement.Style);
        Add("BackgroundPosition", PropertyFieldKind.Text, PropertyFieldPlacement.Style);
        Add("BackgroundRepeat", PropertyFieldKind.Text, PropertyFieldPlacement.Style);
        Add("BackgroundSize", PropertyFieldKind.Text, PropertyFieldPlacement.Style);
        Add("BackgroundWidth", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("BackgroundHeight", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("Border", PropertyFieldKind.Text, PropertyFieldPlacement.Style);
        Add("BorderStyle", PropertyFieldKind.Choice, PropertyFieldPlacement.Style,
            "BorderStyle", ["solid", "dotted", "dashed", "none"]);
        Add("BorderWidth", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("BorderRadius", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("FontFamily", PropertyFieldKind.Text, PropertyFieldPlacement.Style);
        Add("FontSize", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("FontStyle", PropertyFieldKind.Choice, PropertyFieldPlacement.Style,
            "FontStyle", ["normal", "italic"]);
        Add("FontWeight", PropertyFieldKind.Choice, PropertyFieldPlacement.Style,
            "FontWeight", ["400", "700"]);
        Add("LineHeight", PropertyFieldKind.Text, PropertyFieldPlacement.Style);
        Add("LetterSpacing", PropertyFieldKind.Length, PropertyFieldPlacement.Style, allowNegative: true);
        Add("TextAlign", PropertyFieldKind.Alignment, PropertyFieldPlacement.Style, "Align");
        Add("Align", PropertyFieldKind.Alignment, PropertyFieldPlacement.Style, "Align");
        Add("IconAlign", PropertyFieldKind.Choice, PropertyFieldPlacement.Style, "VerticalAlign", VerticalValues);
        Add("VerticalAlign", PropertyFieldKind.Choice, PropertyFieldPlacement.Style, "VerticalAlign", VerticalValues);
        Add("Direction", PropertyFieldKind.Choice, PropertyFieldPlacement.Style, "Direction", ["ltr", "rtl"]);
        Add("IconPosition", PropertyFieldKind.Choice, PropertyFieldPlacement.Style, "IconPosition", ["left", "right"]);
        Add("IconSize", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("IconWidth", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("IconHeight", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("Height", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("Width", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("TextDecoration", PropertyFieldKind.Choice, PropertyFieldPlacement.Style,
            "TextDecoration", ["none", "underline"]);
        Add("TextTransform", PropertyFieldKind.Text, PropertyFieldPlacement.Style);
        Add("TableLayout", PropertyFieldKind.Text, PropertyFieldPlacement.Style);
        Add("Thumbnails", PropertyFieldKind.Text, PropertyFieldPlacement.Style);
        Add("TbBorderRadius", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("Breakpoint", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("ContentWidth", PropertyFieldKind.Length, PropertyFieldPlacement.Style);
        Add("Inline", PropertyFieldKind.Bool, PropertyFieldPlacement.Style);

        // ── Owner-specific overrides (same attribute, different value set) ────────────────
        rules["Hero.Mode"] = new Rule(PropertyFieldKind.Choice, PropertyFieldPlacement.Style,
            "Mode", ["fluid-height", "fixed-height"]);
        rules["Social.Mode"] = new Rule(PropertyFieldKind.Choice, PropertyFieldPlacement.Style,
            "Mode", ["horizontal", "vertical"]);

        return rules;
    }
}
