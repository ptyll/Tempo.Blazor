using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>A slider component for selecting a numeric value within a range.</summary>
public partial class TmSlider : ComponentBase
{
    /// <summary>The minimum value of the slider. Defaults to 0.</summary>
    [Parameter] public int Min { get; set; }

    /// <summary>The maximum value of the slider. Defaults to 100.</summary>
    [Parameter] public int Max { get; set; } = 100;

    /// <summary>The step increment. Defaults to 1.</summary>
    [Parameter] public int Step { get; set; } = 1;

    /// <summary>The current value of the slider.</summary>
    [Parameter] public int? Value { get; set; }

    /// <summary>Event fired when the value changes.</summary>
    [Parameter] public EventCallback<int?> ValueChanged { get; set; }

    /// <summary>The orientation of the slider. Defaults to Horizontal.</summary>
    [Parameter] public SliderOrientation Orientation { get; set; } = SliderOrientation.Horizontal;

    /// <summary>Whether to display tick marks.</summary>
    [Parameter] public bool ShowTicks { get; set; }

    /// <summary>The interval between tick marks.</summary>
    [Parameter] public int TickInterval { get; set; } = 10;

    /// <summary>Whether to display labels for tick marks.</summary>
    [Parameter] public bool ShowLabels { get; set; }

    /// <summary>Whether to display the current value.</summary>
    [Parameter] public bool ShowValue { get; set; }

    /// <summary>Label shown above the slider.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>
    /// Content rendered at the END of the label row. Rendered only with the slot set; without it
    /// the label markup is unchanged.
    /// </summary>
    [Parameter] public RenderFragment? LabelTrailing { get; set; }

    /// <summary>Whether the slider is disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Additional CSS classes.</summary>
    [Parameter] public string? AdditionalCssClass { get; set; }

    private int _currentValue;

    protected override void OnParametersSet()
    {
        _currentValue = Value.GetValueOrDefault(Min);
    }

    private async Task SetValueAsync(int value)
    {
        value = Math.Max(Min, Math.Min(value, Max));
        if (_currentValue != value)
        {
            _currentValue = value;
            await ValueChanged.InvokeAsync(value);
        }
    }

    private string GetFillStyle()
    {
        var percentage = Max > Min
            ? ((double)(_currentValue - Min) / (Max - Min) * 100)
            : 0;

        return Orientation == SliderOrientation.Vertical
            ? $"height: {percentage.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%;"
            : $"width: {percentage.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%;";
    }

    private string GetTickStyle(int tickValue)
    {
        var percentage = Max > Min
            ? ((double)(tickValue - Min) / (Max - Min) * 100)
            : 0;

        return Orientation == SliderOrientation.Vertical
            ? $"bottom: {percentage.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%;"
            : $"left: {percentage.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%;";
    }

    private string GetTickLabelStyle(int tickValue)
    {
        var percentage = Max > Min
            ? ((double)(tickValue - Min) / (Max - Min) * 100)
            : 0;

        // First label aligns left, last aligns right, others are centered
        var transform = tickValue == Min ? "translateX(0)" :
                        tickValue == Max ? "translateX(-100%)" :
                        "translateX(-50%)";

        var pctStr = percentage.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        return Orientation == SliderOrientation.Vertical
            ? $"bottom: {pctStr}%; transform: {transform};"
            : $"left: {pctStr}%; transform: {transform};";
    }
}
