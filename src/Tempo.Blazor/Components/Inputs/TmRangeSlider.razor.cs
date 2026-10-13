using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>A range slider component for selecting a numeric range with two handles.</summary>
public partial class TmRangeSlider : ComponentBase
{
    /// <summary>The minimum value of the slider. Defaults to 0.</summary>
    [Parameter] public int Min { get; set; }

    /// <summary>The maximum value of the slider. Defaults to 100.</summary>
    [Parameter] public int Max { get; set; } = 100;

    /// <summary>The step increment. Defaults to 1.</summary>
    [Parameter] public int Step { get; set; } = 1;

    /// <summary>The start value of the range.</summary>
    [Parameter] public int? StartValue { get; set; }

    /// <summary>Event fired when the start value changes.</summary>
    [Parameter] public EventCallback<int?> StartValueChanged { get; set; }

    /// <summary>The end value of the range.</summary>
    [Parameter] public int? EndValue { get; set; }

    /// <summary>Event fired when the end value changes.</summary>
    [Parameter] public EventCallback<int?> EndValueChanged { get; set; }

    /// <summary>Whether to display the current values.</summary>
    [Parameter] public bool ShowValue { get; set; }

    /// <summary>Label shown above the slider.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Whether the slider is disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Additional CSS classes.</summary>
    [Parameter] public string? AdditionalCssClass { get; set; }

    private int _currentStartValue;
    private int _currentEndValue;

    protected override void OnParametersSet()
    {
        _currentStartValue = StartValue.GetValueOrDefault(Min);
        _currentEndValue = EndValue.GetValueOrDefault(Max);
    }

    internal string GetCssClass()
    {
        var classes = new System.Text.StringBuilder("tm-range-slider");
        // With both thumbs parked on the same value the end thumb would trap the start one; the
        // start input lifts whenever the start thumb is in the upper half of the range, so the
        // lower half — and the thumb parked in it — stays draggable below.
        if (_currentStartValue > Min + (Max - Min) / 2.0)
        {
            classes.Append(" tm-range-slider--start-on-top");
        }

        if (!string.IsNullOrEmpty(AdditionalCssClass))
        {
            classes.Append(' ').Append(AdditionalCssClass);
        }

        return classes.ToString();
    }

    /// <summary>The start thumb's accessible name: "{Label} – Start" when a Label is set.</summary>
    internal string StartThumbName
        => ComposeThumbName(Loc["TmRangeSlider_Start"]);

    /// <summary>The end thumb's accessible name: "{Label} – End" when a Label is set.</summary>
    internal string EndThumbName
        => ComposeThumbName(Loc["TmRangeSlider_End"]);

    private string ComposeThumbName(string localizedRole)
        => string.IsNullOrEmpty(Label) ? localizedRole : $"{Label} – {localizedRole}";

    private string GetFillStyle()
    {
        var range = Max - Min;

        if (range <= 0) return "inset-inline-start: 0%; width: 100%;";

        var start = ((double)(_currentStartValue - Min) / range * 100);
        var width = ((double)(_currentEndValue - _currentStartValue) / range * 100);

        var startStr = start.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        var widthStr = width.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

        // The logical property mirrors the fill in RTL: the range grows from the inline start.
        return $"inset-inline-start: {startStr}%; width: {widthStr}%;";
    }

    private async Task SetStartValueAsync(int value)
    {
        var clamped = Math.Max(Min, Math.Min(value, _currentEndValue));
        if (clamped != value)
        {
            // Blazor skips a bind whose bound value did not CHANGE, so without this the input would
            // keep the raw out-of-range gesture value and the thumb would sit past the end thumb.
            // Render the raw value first, yield so the DOM takes it, then render the clamped one —
            // the second rewrite always lands.
            _currentStartValue = value;
            StateHasChanged();
            await Task.Yield();
        }
        if (_currentStartValue != clamped)
        {
            _currentStartValue = clamped;
            await StartValueChanged.InvokeAsync(clamped);
            StateHasChanged();
        }
    }

    private async Task SetEndValueAsync(int value)
    {
        var clamped = Math.Max(_currentStartValue, Math.Min(value, Max));
        if (clamped != value)
        {
            // Same forced rewrite as the start thumb: raw first, yield, then the clamped value.
            _currentEndValue = value;
            StateHasChanged();
            await Task.Yield();
        }
        if (_currentEndValue != clamped)
        {
            _currentEndValue = clamped;
            await EndValueChanged.InvokeAsync(clamped);
            StateHasChanged();
        }
    }
}
