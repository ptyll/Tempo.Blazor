using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Displays an interactive signing field overlay positioned by normalized document coordinates.</summary>
public partial class TmSigningFieldOverlay
{
    private static readonly SigningResizeHandle[] ResizeHandles = Enum.GetValues<SigningResizeHandle>();

    /// <summary>Signing field definition to render.</summary>
    [Parameter] public SigningField? Field { get; set; }

    /// <summary>Normalized document area used to position the overlay.</summary>
    [Parameter] public SigningFieldArea? Area { get; set; }

    /// <summary>Current field value. The expected value shape depends on the field type.</summary>
    [Parameter] public object? Value { get; set; }

    /// <summary>Whether the field is currently selected.</summary>
    [Parameter] public bool Selected { get; set; }

    /// <summary>Whether the field currently has focus.</summary>
    [Parameter] public bool Focused { get; set; }

    /// <summary>Whether the field is in an invalid state.</summary>
    [Parameter] public bool Invalid { get; set; }

    /// <summary>Whether the field has been completed.</summary>
    [Parameter] public bool Completed { get; set; }

    /// <summary>Whether the overlay should be rendered as read-only.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Whether the overlay should be disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether a pointer down on the overlay starts a move operation.</summary>
    [Parameter] public bool Draggable { get; set; }

    /// <summary>Whether resize handles should be shown.</summary>
    [Parameter] public bool Editable { get; set; }

    /// <summary>Whether the browser context menu is prevented for context menu interactions. Defaults to true.</summary>
    [Parameter] public bool PreventDefaultContextMenu { get; set; } = true;

    /// <summary>Culture used to resolve localized field and option text.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Fallback culture used when localized text is missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>
    /// Submitter role colour rendered as the inline <c>--tm-signing-role-color</c> custom property
    /// (plus the <c>tm-signing-field--role</c> class), from which the stylesheet derives the
    /// overlay's border and background with <c>color-mix()</c>. Accepts hex, a
    /// <c>var(--tm-signing-role-N)</c> token reference, an rgb/hsl/oklch function or one of the CSS
    /// basic colour keywords; every other value is dropped by the sanitizer. When null (or dropped)
    /// the overlay keeps the primary-tint default.
    /// </summary>
    [Parameter] public string? RoleColor { get; set; }

    /// <summary>
    /// Short prompt rendered inside the overlay while it is NOT completed (Signing redesign
    /// phase 3) — the runner passes the localized "Click to sign / fill in" call to action for
    /// the CURRENT field only, and only while that field is empty and required. Hidden once the
    /// field is completed or when null/whitespace, so the designer's compact presentation stays
    /// prompt-free. The label + prompt head is a plain flex pair (no size container and no
    /// width-based hiding — a container truncated labels to stubs), so a host that wants a prompt
    /// on every field should keep the set small for the same readability reasons.
    /// </summary>
    [Parameter] public string? Prompt { get; set; }

    /// <summary>Callback invoked when the overlay is clicked.</summary>
    [Parameter] public EventCallback<TmSigningFieldOverlayPointerEventArgs> OnClick { get; set; }

    /// <summary>Callback invoked when the overlay is double-clicked.</summary>
    [Parameter] public EventCallback<TmSigningFieldOverlayPointerEventArgs> OnDoubleClick { get; set; }

    /// <summary>Callback invoked when the overlay receives a context menu interaction.</summary>
    [Parameter] public EventCallback<TmSigningFieldOverlayPointerEventArgs> OnContextMenu { get; set; }

    /// <summary>Callback invoked when a move operation should start.</summary>
    [Parameter] public EventCallback<TmSigningFieldOverlayPointerEventArgs> OnStartMove { get; set; }

    /// <summary>Callback invoked when a resize operation should start.</summary>
    [Parameter] public EventCallback<TmSigningFieldOverlayResizeEventArgs> OnStartResize { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private bool IsDisabled => Disabled || Field?.ReadOnly == true && ReadOnly;

    private int TabIndex => IsDisabled ? -1 : 0;

    private bool StopMouseDownPropagation => Draggable;

    /// <summary>
    /// The CSS basic colour keywords accepted as a bare colour name (case-insensitive). The sanitizer
    /// only admits this closed set, so an arbitrary word can never ride the inline style.
    /// </summary>
    private static readonly HashSet<string> BasicColorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "aqua", "black", "blue", "fuchsia", "gray", "green", "lime", "maroon",
        "navy", "olive", "purple", "red", "silver", "teal", "white", "yellow"
    };

    /// <summary>
    /// Allow-list for the inline <c>--tm-signing-role-color</c> value: 3–8 digit hex, a Tempo role
    /// token reference, a functional rgb/hsl/oklch colour, or one of the CSS basic colour keywords
    /// (<see cref="BasicColorKeywords"/>). Everything else — and therefore every character that
    /// could break out of the inline style — is dropped.
    /// </summary>
    private static readonly Regex RoleColorPattern = new(
        @"^(#[0-9a-fA-F]{3,8}|var\(--tm-[a-z0-9-]+\)|(?:rgb|hsl|oklch)a?\([0-9.,%\s/]+\))$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    /// <summary>The role colour when it matches the allow-list, otherwise <c>null</c>.</summary>
    private string? SafeRoleColor
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RoleColor))
            {
                return null;
            }

            var candidate = RoleColor.Trim();

            return RoleColorPattern.IsMatch(candidate) || BasicColorKeywords.Contains(candidate)
                ? candidate
                : null;
        }
    }

    private bool HasRoleColor => SafeRoleColor is not null;

    private bool ShowPrompt => !Completed && !string.IsNullOrWhiteSpace(Prompt);

    private string Label => SigningTextResolver.FieldLabel(Field, Culture, FallbackCulture, Loc);

    private string AriaLabel => Field?.Required == true
        ? string.Create(CultureInfo.InvariantCulture, $"{Label}, {Loc["TmSigning_Required"]}")
        : Label;

    private string HeadingText => SigningTextResolver.FieldTitle(Field, Culture, FallbackCulture, Loc);

    private string PlaceholderText => SigningTextResolver.FieldPlaceholder(Field, Culture, FallbackCulture, Loc);

    private string LocalizedTypeName => SigningTextResolver.FieldTypeLabel(Field?.Type ?? SigningFieldType.Text, Loc);

    // Signing redesign phase 3: the map moved to the shared SigningFieldTypeIcons so the palette,
    // the overlays and the runner's field list render the same icon per field type.
    private string IconName => SigningFieldTypeIcons.Get(Field?.Type ?? SigningFieldType.Text);

    private string RootClass
    {
        get
        {
            var classes = new List<string>
            {
                "tm-signing-field",
                "tm-signing-field-overlay"
            };

            AddClass(classes, Selected, "tm-signing-field--selected");
            AddClass(classes, Selected, "tm-signing-field-overlay--selected");
            AddClass(classes, Focused, "tm-signing-field--focused");
            AddClass(classes, HasRoleColor, "tm-signing-field--role");
            AddClass(classes, Invalid, "tm-signing-field--invalid");
            AddClass(classes, Completed, "tm-signing-field--completed");
            AddClass(classes, ReadOnly || Field?.ReadOnly == true, "tm-signing-field--read-only");
            AddClass(classes, IsDisabled, "tm-signing-field--disabled");
            AddClass(classes, Draggable, "tm-signing-field--draggable");

            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    private string PositionStyle
    {
        get
        {
            var roleColor = SafeRoleColor;
            var roleStyle = roleColor is null
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" --tm-signing-role-color:{roleColor};");

            if (Area is null)
            {
                return roleStyle.TrimStart();
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"left: {FormatPercent(Area.X)}%; top: {FormatPercent(Area.Y)}%; width: {FormatPercent(Area.Width)}%; height: {FormatPercent(Area.Height)}%;{roleStyle}");
        }
    }

    private string TextValue => FormatValue(Value ?? Field?.DefaultValue);

    private string DisplayValue => string.IsNullOrWhiteSpace(TextValue) && ShouldShowTypePlaceholder
        ? LocalizedTypeName
        : TextValue;

    private string ImageValue => Value as string ?? Field?.DefaultValue as string ?? string.Empty;

    private bool HasImageValue => IsImageSource(ImageValue);

    private bool ShouldShowTypePlaceholder => Field?.Type is SigningFieldType.File
        or SigningFieldType.Payment
        or SigningFieldType.Phone
        or SigningFieldType.Verification
        or SigningFieldType.Kba;

    private string CheckboxClass => IsChecked(Value)
        ? "tm-signing-field__checkbox tm-signing-field__checkbox--checked"
        : "tm-signing-field__checkbox";

    private static void AddClass(List<string> classes, bool condition, string cssClass)
    {
        if (condition)
        {
            classes.Add(cssClass);
        }
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    private static bool IsChecked(object? value)
    {
        return value is bool boolValue && boolValue;
    }

    private static string FormatPercent(double value)
    {
        return (value * 100d).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool IsImageSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith("./", StringComparison.Ordinal)
            || value.StartsWith("../", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";
    }

    private string GetOptionClass(SigningFieldOption option)
    {
        var classes = new List<string> { "tm-signing-field__option" };

        if (IsOptionSelected(option))
        {
            classes.Add("tm-signing-field__option--checked");
        }

        return string.Join(" ", classes);
    }

    private string GetOptionLabel(SigningFieldOption option)
    {
        return SigningTextResolver.OptionLabel(option, Culture, FallbackCulture);
    }

    private bool IsOptionSelected(SigningFieldOption option)
    {
        var selected = Field?.Type switch
        {
            SigningFieldType.Radio => Area?.OptionUuid ?? FormatValue(Value),
            SigningFieldType.Select => FormatValue(Value),
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(selected))
        {
            return string.Equals(option.Uuid, selected, StringComparison.Ordinal)
                || string.Equals(option.Value, selected, StringComparison.Ordinal);
        }

        if (Field?.Type is not SigningFieldType.Multiple)
        {
            return false;
        }

        return GetSelectedValues(Value)
            .Any(value => string.Equals(option.Uuid, value, StringComparison.Ordinal)
                || string.Equals(option.Value, value, StringComparison.Ordinal));
    }

    private static IEnumerable<string> GetSelectedValues(object? value)
    {
        if (value is null or string)
        {
            return value is string text && !string.IsNullOrWhiteSpace(text)
                ? [text]
                : [];
        }

        if (value is IEnumerable<string> strings)
        {
            return strings;
        }

        if (value is IEnumerable enumerable)
        {
            return enumerable.Cast<object?>()
                .Select(FormatValue)
                .Where(text => !string.IsNullOrWhiteSpace(text));
        }

        return [FormatValue(value)];
    }

    private static string GetResizeHandleClass(SigningResizeHandle handle)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"tm-signing-field__resize-handle tm-signing-field__resize-handle--{ToKebabCase(handle)}");
    }

    private static string ToKebabCase(SigningResizeHandle handle)
    {
        return handle switch
        {
            SigningResizeHandle.NorthWest => "north-west",
            SigningResizeHandle.North => "north",
            SigningResizeHandle.NorthEast => "north-east",
            SigningResizeHandle.East => "east",
            SigningResizeHandle.SouthEast => "south-east",
            SigningResizeHandle.South => "south",
            SigningResizeHandle.SouthWest => "south-west",
            SigningResizeHandle.West => "west",
            _ => handle.ToString().ToLowerInvariant()
        };
    }

    private Task HandleClickAsync(MouseEventArgs args)
    {
        return Field is null || IsDisabled || !OnClick.HasDelegate
            ? Task.CompletedTask
            : OnClick.InvokeAsync(new TmSigningFieldOverlayPointerEventArgs(Field, Area, args));
    }

    private Task HandleDoubleClickAsync(MouseEventArgs args)
    {
        return Field is null || IsDisabled || !OnDoubleClick.HasDelegate
            ? Task.CompletedTask
            : OnDoubleClick.InvokeAsync(new TmSigningFieldOverlayPointerEventArgs(Field, Area, args));
    }

    private Task HandleContextMenuAsync(MouseEventArgs args)
    {
        return Field is null || IsDisabled || !OnContextMenu.HasDelegate
            ? Task.CompletedTask
            : OnContextMenu.InvokeAsync(new TmSigningFieldOverlayPointerEventArgs(Field, Area, args));
    }

    private Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key is not ("Enter" or " ") || Field is null || IsDisabled || !OnClick.HasDelegate)
        {
            return Task.CompletedTask;
        }

        return OnClick.InvokeAsync(new TmSigningFieldOverlayPointerEventArgs(Field, Area, new MouseEventArgs()));
    }

    private Task HandleStartMoveAsync(MouseEventArgs args)
    {
        return Field is null || IsDisabled || !Draggable || !OnStartMove.HasDelegate
            ? Task.CompletedTask
            : OnStartMove.InvokeAsync(new TmSigningFieldOverlayPointerEventArgs(Field, Area, args));
    }

    private Task HandleStartResizeAsync(SigningResizeHandle handle, MouseEventArgs args)
    {
        return Field is null || IsDisabled || !Editable || !OnStartResize.HasDelegate
            ? Task.CompletedTask
            : OnStartResize.InvokeAsync(new TmSigningFieldOverlayResizeEventArgs(Field, Area, handle, args));
    }
}
