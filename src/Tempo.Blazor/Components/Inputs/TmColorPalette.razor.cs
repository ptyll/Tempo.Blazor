using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// A color palette that displays predefined colors in a grid.
/// Users can click a swatch to select a color.
/// </summary>
public partial class TmColorPalette
{
    private ElementReference _rootElement;
    private int _keyboardIndex;
    private bool _hasKeyboardFocus;
    private bool _focusSwatchAfterRender;

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

    private static string AriaBool(bool value)
        => value ? "true" : "false";

    // ── Lifecycle ────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
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
