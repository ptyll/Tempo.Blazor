using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Inputs;

public partial class TmColorPicker
{
    private bool _isOpen;
    private bool _focusTriggerAfterOpen;
    private bool _focusTriggerAfterClose;
    private string? _pendingValue;
    private ElementReference _rootElement;
    private ElementReference _triggerElement;

    /// <summary>The current color value.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Fires when the color value changes.</summary>
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>Fires when the dropdown open state changes.</summary>
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>Output color format. Defaults to hex.</summary>
    [Parameter] public ColorFormat Format { get; set; } = ColorFormat.Hex;

    /// <summary>Shows alpha channel controls.</summary>
    [Parameter] public bool ShowAlpha { get; set; } = true;

    /// <summary>Shows the gradient color area.</summary>
    [Parameter] public bool ShowGradient { get; set; } = true;

    /// <summary>Shows the hex color input.</summary>
    [Parameter] public bool ShowHexInput { get; set; } = true;

    /// <summary>Shows the preset color palette.</summary>
    [Parameter] public bool ShowPalette { get; set; } = true;

    /// <summary>Predefined colors to display in the palette.</summary>
    [Parameter] public IReadOnlyList<string>? PaletteColors { get; set; }

    /// <summary>Number of columns in the palette grid.</summary>
    [Parameter] public int PaletteColumns { get; set; } = 8;

    /// <summary>Shows the clear color command.</summary>
    [Parameter] public bool ShowClearButton { get; set; } = true;

    /// <summary>
    /// When true, the dropdown shows an Apply button and only closes when the user explicitly clicks it.
    /// Changing the color inside the picker does not close the dropdown.
    /// </summary>
    [Parameter] public bool ShowApplyButton { get; set; }

    /// <summary>Shows a cancel button next to Apply when <see cref="ShowApplyButton"/> is true.</summary>
    [Parameter] public bool ShowCancelButton { get; set; }

    /// <summary>Optional apply button text.</summary>
    [Parameter] public string? ApplyText { get; set; }

    /// <summary>Optional cancel button text.</summary>
    [Parameter] public string? CancelText { get; set; }

    /// <summary>Disables the color picker.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Placeholder text shown when no color is selected.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional attributes rendered on the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private async Task ToggleDropdownAsync()
    {
        if (Disabled)
        {
            return;
        }

        var nextOpen = !_isOpen;
        _isOpen = nextOpen;
        if (nextOpen)
        {
            _pendingValue = Value;
            _focusTriggerAfterOpen = true;
        }
        else
        {
            _focusTriggerAfterClose = true;
        }

        await OpenChanged.InvokeAsync(_isOpen);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusTriggerAfterOpen)
        {
            _focusTriggerAfterOpen = false;
            await _triggerElement.FocusAsync(preventScroll: true);
        }

        if (_focusTriggerAfterClose)
        {
            _focusTriggerAfterClose = false;
            await _triggerElement.FocusAsync(preventScroll: true);
        }
    }

    private async Task OnFlatValueChangedAsync(string? value)
    {
        if (Disabled)
        {
            return;
        }

        _pendingValue = value;
        if (!ShowApplyButton)
        {
            await ValueChanged.InvokeAsync(value);
            _isOpen = false;
            _focusTriggerAfterClose = true;
            await OpenChanged.InvokeAsync(false);
        }
    }

    private async Task ApplyAsync()
    {
        if (Disabled)
        {
            return;
        }

        await ValueChanged.InvokeAsync(_pendingValue);
        _isOpen = false;
        _focusTriggerAfterClose = true;
        await OpenChanged.InvokeAsync(false);
    }

    private async Task HandleTriggerKeyDownAsync(KeyboardEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        if (IsActivationKey(args.Key))
        {
            await ToggleDropdownAsync();
            return;
        }

        if (args.Key == "Escape" && _isOpen)
        {
            await CloseWithoutApplyingAsync();
        }
    }

    // Escape keydown anywhere inside the component still closes without applying — overlay.js's
    // document-level listener normally wins this race (it is the tracked panel), but this handler
    // keeps the keyboard contract working when JS has not loaded, and CloseWithoutApplyingAsync is
    // idempotent when both paths fire.
    private async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        if (args.Key == "Escape" && _isOpen)
        {
            await CloseWithoutApplyingAsync();
        }
    }

    /// <summary>
    /// JS-driven close from TmOverlayPanel (Escape or outside pointerdown from overlay.js). Both
    /// gestures close without applying the pending value — what the retired tmColorPicker engine
    /// did. Focus restore on Escape is handled by overlay.js itself (it focuses the anchor before
    /// notifying .NET), so the Blazor side never re-focuses here.
    /// </summary>
    private async Task OnPanelOpenChangedAsync(bool open)
    {
        if (open == _isOpen)
        {
            return;
        }

        if (open)
        {
            await ToggleDropdownAsync();
        }
        else
        {
            await CloseWithoutApplyingAsync(restoreFocus: false);
        }
    }

    private Task CancelAsync()
        => CloseWithoutApplyingAsync();

    private async Task CloseWithoutApplyingAsync(bool restoreFocus = true)
    {
        var wasOpen = _isOpen;
        _pendingValue = Value;
        _isOpen = false;
        _focusTriggerAfterOpen = false;
        _focusTriggerAfterClose = wasOpen && restoreFocus;
        if (wasOpen)
        {
            await OpenChanged.InvokeAsync(false);
        }
    }

    private static bool IsActivationKey(string? key)
        => key is "Enter" or " " or "Space" or "Spacebar";
}
