using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// A flat color picker that combines the color gradient, palette, and preview
/// in a single panel.
/// </summary>
public partial class TmFlatColorPicker
{
    private string _currentValue = string.Empty;
    private string _hexValue = string.Empty;

    // ── Parameters ───────────────────────────────────────────────

    /// <summary>The current color value.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Event fired when the color value changes.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Output format. Default is <see cref="ColorFormat.Hex"/>.</summary>
    [Parameter] public ColorFormat Format { get; set; } = ColorFormat.Hex;

    /// <summary>Whether to show the alpha channel. Default true.</summary>
    [Parameter] public bool ShowAlpha { get; set; } = true;

    /// <summary>Whether to show the gradient color area. Default true.</summary>
    [Parameter] public bool ShowGradient { get; set; } = true;

    /// <summary>Whether to show the hex color input. Default true.</summary>
    [Parameter] public bool ShowHexInput { get; set; } = true;

    /// <summary>Whether to show the color palette. Default true.</summary>
    [Parameter] public bool ShowPalette { get; set; } = true;

    /// <summary>Predefined colors to display in the palette.</summary>
    [Parameter] public IReadOnlyList<string>? PaletteColors { get; set; }

    /// <summary>
    /// Accessible names for the palette colors, passed through to the embedded
    /// <see cref="TmColorPalette"/> (see <see cref="TmColorPalette.ColorNames"/> for the exact
    /// matching and fallback rules). Named swatches announce the localized "Select color {0}"
    /// template with their name; unnamed colors announce their raw value.
    /// </summary>
    [Parameter] public IReadOnlyDictionary<string, string>? PaletteColorNames { get; set; }

    /// <summary>Number of columns in the palette grid. Default 8.</summary>
    [Parameter] public int PaletteColumns { get; set; } = 8;

    /// <summary>Whether to show the preview row. Default true.</summary>
    [Parameter] public bool ShowPreview { get; set; } = true;

    /// <summary>Whether to show the clear button in the palette. Default true.</summary>
    [Parameter] public bool ShowClearButton { get; set; } = true;

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional attributes spread onto the wrapper.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Lifecycle ────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _currentValue = Value ?? string.Empty;
        _hexValue = NormalizeHex(_currentValue);
    }

    // ── Event handlers ───────────────────────────────────────────

    private async Task OnGradientValueChanged(string value)
    {
        _currentValue = value;
        _hexValue = NormalizeHex(value);
        await ValueChanged.InvokeAsync(value);
    }

    private async Task OnPaletteValueChanged(string value)
    {
        _currentValue = value;
        _hexValue = NormalizeHex(value);
        await ValueChanged.InvokeAsync(value);
    }

    private async Task OnHexValueChangedAsync(ChangeEventArgs args)
    {
        var normalized = NormalizeHex(args.Value?.ToString());
        _hexValue = normalized;
        if (!IsValidHexColor(normalized))
        {
            return;
        }

        _currentValue = normalized;
        await ValueChanged.InvokeAsync(normalized);
    }

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('#'))
        {
            trimmed = $"#{trimmed}";
        }

        if (trimmed.Length == 4 && trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            return $"#{trimmed[1]}{trimmed[1]}{trimmed[2]}{trimmed[2]}{trimmed[3]}{trimmed[3]}".ToLowerInvariant();
        }

        return trimmed.Length == 7 && trimmed.Skip(1).All(Uri.IsHexDigit)
            ? trimmed.ToLowerInvariant()
            : trimmed;
    }

    private static bool IsValidHexColor(string value)
        => value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}
