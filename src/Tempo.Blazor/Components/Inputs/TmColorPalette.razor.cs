using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// A color palette that displays predefined colors in a grid.
/// Users can click a swatch to select a color.
/// <para>
/// Accessibility: the grid is a <c>role="radiogroup"</c> labelled with the localized
/// "Select color" text, and every swatch is a native <c>&lt;button role="radio"&gt;</c> that
/// announces its own color through <see cref="ColorNames"/> (or its raw value when unnamed),
/// carries <c>aria-checked</c> for the selection and a roving tabindex with
/// arrows/Home/End moving the focus (Enter/Space activate the native button — exactly one
/// activation per key press).
/// </para>
/// <para>
/// Keyboard model: the arrow keys, <c>Home</c> and <c>End</c> move focus only — selection does
/// NOT follow focus — so keyboard navigation over a palette embedded in a
/// <see cref="TmFlatColorPicker"/> or <see cref="TmColorPicker"/> never commits a color (and
/// never closes the picker) while the user is merely moving through the swatches. Enter or
/// Space performs the selection through the native button activation. The roving tabindex and
/// the keyboard-focus ring clear again on <c>focusout</c>, so leaving the palette restores the
/// selected swatch as the tab stop.
/// </para>
/// </summary>
public partial class TmColorPalette
{
    private ElementReference _rootElement;
    private int _keyboardIndex;
    private bool _hasKeyboardFocus;
    private bool _focusSwatchAfterRender;

    // Case-insensitive name lookup rebuilt in OnParametersSet whenever the ColorNames or Colors
    // reference changes; exact-case keys win over case-variant duplicates and blank names are
    // dropped (a color with no usable name falls back to its value).
    private Dictionary<string, string>? _colorNamesLookup;
    private IReadOnlyDictionary<string, string>? _colorNamesSource;
    private IReadOnlyList<string>? _colorsSource;

    // ── Default palette ──────────────────────────────────────────
    private static readonly string[] DefaultColors =
    [
        "#000000", "#1A1A1A", "#333333", "#4D4D4D", "#666666", "#808080", "#999999", "#B3B3B3",
        "#FF0000", "#FF4D4D", "#FF6666", "#FF8080", "#FF9999", "#FFB3B3", "#FFCCCC", "#FFE6E6",
        "#00FF00", "#4DFF4D", "#66FF66", "#80FF80", "#99FF99", "#B3FFB3", "#CCFFCC", "#E6FFE6",
        "#0000FF", "#4D4DFF", "#6666FF", "#8080FF", "#9999FF", "#B3B3FF", "#CCCCFF", "#E6E6FF",
        "#FFFF00", "#FFFF4D", "#FFFF66", "#FFFF80", "#FFFF99", "#FFFFB3", "#FFFFCC", "#FFFFE6",
        "#FF00FF", "#FF4DFF", "#FF66FF", "#FF80FF", "#FF99FF", "#FFB3FF", "#FFCCFF", "#FFE6FF",
        "#00FFFF", "#4DFFFF", "#66FFFF", "#80FFFF", "#99FFFF", "#B3FFFF", "#CCFFFF", "#E6FFFF",
        "#FFFFFF", "#1A1AFF", "#331AFF", "#4D1AFF", "#661AFF", "#801AFF", "#991AFF", "#B31AFF",
    ];

    // ── Parameters ───────────────────────────────────────────────

    /// <summary>The currently selected color value.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Event fired when a color is selected.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Predefined colors to display. Defaults to a standard palette.</summary>
    [Parameter] public IReadOnlyList<string>? Colors { get; set; }

    /// <summary>
    /// Accessible names for the colors, keyed by the color string exactly as it appears in
    /// <see cref="Colors"/> (keys match case-insensitively, so <c>"#2e7d32"</c> names a
    /// <c>"#2E7D32"</c> swatch). A named swatch announces the localized
    /// <c>TmColorPalette_SelectColorNamed</c> template ("Select color {0}") with its name; a
    /// color without an entry — or with an empty name — falls back to announcing its raw value
    /// ("Select color #2E7D32"), so a screen reader can always tell the swatches apart
    /// (WCAG 1.1.1/4.1.2). Colors that are not in the palette may carry names too; they are
    /// simply never used.
    /// </summary>
    [Parameter] public IReadOnlyDictionary<string, string>? ColorNames { get; set; }

    /// <summary>Number of columns in the grid. Default 8.</summary>
    [Parameter] public int Columns { get; set; } = 8;

    /// <summary>Shows a clear button. Default true.</summary>
    [Parameter] public bool ShowClearButton { get; set; } = true;

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional attributes spread onto the wrapper.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    // ── Computed ─────────────────────────────────────────────────

    private IReadOnlyList<string> _effectiveColors => Colors ?? DefaultColors;

    private bool IsSelected(string color)
        => string.Equals(color, Value, StringComparison.OrdinalIgnoreCase);

    private string GetSwatchClass(string color, int index)
    {
        var classes = new List<string> { "tm-color-palette-swatch" };
        if (IsSelected(color))
        {
            classes.Add("tm-color-palette-swatch--selected");
        }

        if (_hasKeyboardFocus && index == _keyboardIndex)
        {
            classes.Add("tm-color-palette-swatch--keyboard-focus");
        }

        return string.Join(" ", classes);
    }

    private string GetSwatchTabIndex(int index)
        => index == _keyboardIndex ? "0" : "-1";

    /// <summary>
    /// The accessible name of one swatch: the localized <c>TmColorPalette_SelectColorNamed</c>
    /// template ("Select color {0}") formatted with the color's <see cref="ColorNames"/> name,
    /// or with the raw color value when the color has no name.
    /// <para>
    /// Backward compatible with custom <c>ITmLocalizer</c> implementations that predate the
    /// <c>TmColorPalette_SelectColorNamed</c> key: when <see cref="ITmLocalizer.TryGetString"/>
    /// reports the template missing, the label falls back to the plain
    /// <c>TmColorPalette_SelectColor</c> text followed by the name, so such hosts render
    /// "Select color #FF0000" instead of a raw <c>[key]</c> placeholder.
    /// </para>
    /// </summary>
    private string GetSwatchAriaLabel(string color)
    {
        var name = ResolveColorName(color);
        return Loc.TryGetString("TmColorPalette_SelectColorNamed", out var template)
            ? string.Format(Loc.Culture, template, name)
            : $"{Loc["TmColorPalette_SelectColor"]} {name}";
    }

    private string ResolveColorName(string color)
        => TryGetColorName(color, out var name) ? name : color;

    private bool TryGetColorName(string color, [NotNullWhen(true)] out string? name)
    {
        name = null;
        if (_colorNamesLookup is not null && _colorNamesLookup.TryGetValue(color, out var lookupName))
        {
            name = lookupName;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Rebuilds the case-insensitive color-name lookup when the <see cref="ColorNames"/> or
    /// <see cref="Colors"/> reference changed. Exact-case keys win over case-variant duplicates
    /// and blank names are dropped, so those colors announce their raw value instead.
    /// </summary>
    private void RebuildColorNamesLookup()
    {
        if (ReferenceEquals(_colorNamesSource, ColorNames)
            && ReferenceEquals(_colorsSource, Colors))
        {
            return;
        }

        _colorNamesSource = ColorNames;
        _colorsSource = Colors;
        if (ColorNames is not { Count: > 0 })
        {
            _colorNamesLookup = null;
            return;
        }

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ColorNames)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                lookup[key] = value;
            }
        }

        // A key spelled exactly like a palette color beats a case-variant duplicate: read the
        // exact match straight off the source dictionary and overwrite the lookup entry.
        foreach (var color in _effectiveColors)
        {
            if (ColorNames.TryGetValue(color, out var exact) && !string.IsNullOrWhiteSpace(exact))
            {
                lookup[color] = exact;
            }
        }

        _colorNamesLookup = lookup.Count > 0 ? lookup : null;
    }

    private static string AriaBool(bool value)
        => value ? "true" : "false";

    // ── Lifecycle ────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        RebuildColorNamesLookup();

        var colors = _effectiveColors;
        if (colors.Count == 0)
        {
            _keyboardIndex = 0;
            return;
        }

        var selectedIndex = !string.IsNullOrWhiteSpace(Value)
            ? colors.ToList().FindIndex(color => string.Equals(color, Value, StringComparison.OrdinalIgnoreCase))
            : -1;

        if (selectedIndex >= 0 && !_hasKeyboardFocus)
        {
            _keyboardIndex = selectedIndex;
        }
        else
        {
            _keyboardIndex = Math.Clamp(_keyboardIndex, 0, colors.Count - 1);
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusSwatchAfterRender)
        {
            return;
        }

        _focusSwatchAfterRender = false;
        try
        {
            await JSRuntime.InvokeVoidAsync("tmColorPicker.focusPaletteSwatch", _rootElement, _keyboardIndex);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    // ── Actions ──────────────────────────────────────────────────

    private async Task SelectColorAsync(string color)
    {
        Value = color;
        var index = _effectiveColors.ToList().FindIndex(candidate => string.Equals(candidate, color, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _keyboardIndex = index;
        }

        await ValueChanged.InvokeAsync(color);
    }

    private async Task ClearAsync()
    {
        Value = null;
        await ValueChanged.InvokeAsync(string.Empty);
    }

    private void SetKeyboardIndex(int index)
    {
        _hasKeyboardFocus = true;
        _keyboardIndex = Math.Clamp(index, 0, Math.Max(0, _effectiveColors.Count - 1));
    }

    /// <summary>
    /// Focus left the grid (focusout bubbles from the swatch that lost it): drop the keyboard
    /// state so the stale focus ring disappears and <see cref="OnParametersSet"/> can move the
    /// roving tabindex back to the selected swatch. Focus moving BETWEEN swatches also passes
    /// through here, but the follow-up <c>focus</c> on the next swatch re-arms the flag.
    /// </summary>
    private void HandleSwatchFocusOut(FocusEventArgs args)
        => _hasKeyboardFocus = false;

    private void HandleSwatchKeyDown(KeyboardEventArgs args, int index)
    {
        if (_effectiveColors.Count == 0)
        {
            return;
        }

        _hasKeyboardFocus = true;
        _keyboardIndex = Math.Clamp(index, 0, _effectiveColors.Count - 1);

        var nextIndex = args.Key switch
        {
            "ArrowRight" => MoveIndex(_keyboardIndex, 1),
            "ArrowLeft" => MoveIndex(_keyboardIndex, -1),
            "ArrowDown" => MoveIndex(_keyboardIndex, Math.Max(1, Columns)),
            "ArrowUp" => MoveIndex(_keyboardIndex, -Math.Max(1, Columns)),
            "Home" => 0,
            "End" => _effectiveColors.Count - 1,
            _ => _keyboardIndex
        };

        if (nextIndex != _keyboardIndex)
        {
            _keyboardIndex = nextIndex;
            _focusSwatchAfterRender = true;
            return;
        }

        // No Enter/Space emulation: swatches are native <button> elements, so
        // the browser itself produces the activation click (Enter on keydown,
        // Space on keyup) which reaches SelectColorAsync via @onclick.
        // Selecting here too fired ValueChanged twice per key press.
    }

    private int MoveIndex(int index, int delta)
    {
        var count = _effectiveColors.Count;
        if (count == 0)
        {
            return 0;
        }

        var next = index + delta;
        if (next < 0)
        {
            return 0;
        }

        if (next >= count)
        {
            return count - 1;
        }

        return next;
    }
}
