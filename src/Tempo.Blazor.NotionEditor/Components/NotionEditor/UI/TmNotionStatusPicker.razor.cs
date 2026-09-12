using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.NotionEditor.Enums;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionStatusPicker : ComponentBase
{
    private const int MaxLabelLength = 120;
    private static readonly NotionStatusColor[] Colors = Enum.GetValues<NotionStatusColor>();

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Shows or hides the picker.</summary>
    [Parameter] public bool Visible { get; set; }

    /// <summary>Fixed top position in CSS pixels.</summary>
    [Parameter] public double Top { get; set; }

    /// <summary>Fixed left position in CSS pixels.</summary>
    [Parameter] public double Left { get; set; }

    /// <summary>Initial label used when opening the picker for an existing chip.</summary>
    [Parameter] public string? InitialLabel { get; set; }

    /// <summary>Initial color used when opening the picker.</summary>
    [Parameter] public NotionStatusColor InitialColor { get; set; } = NotionStatusColor.Gray;

    /// <summary>Raised when the user confirms a non-empty status label.</summary>
    [Parameter] public EventCallback<(string Label, NotionStatusColor Color)> OnInserted { get; set; }

    /// <summary>Raised when the user dismisses the picker.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    private ElementReference _pickerRef;
    private ElementReference _inputRef;
    private bool _wasVisible;
    private bool _needsFocus;
    private double _top;
    private double _left;
    private string _label = string.Empty;
    private NotionStatusColor _color = NotionStatusColor.Gray;

    private bool IsInsertDisabled => string.IsNullOrWhiteSpace(_label);

    private string PreviewLabel => string.IsNullOrWhiteSpace(_label)
        ? Loc["Notion_Status_Placeholder"]
        : _label.Trim();

    private string PreviewClass => $"tm-notion-status tm-notion-status--{CssColor(_color)}";

    protected override void OnParametersSet()
    {
        if (Visible && !_wasVisible)
        {
            _top = Top;
            _left = Left;
            _label = InitialLabel ?? string.Empty;
            _color = InitialColor;
            _needsFocus = true;
        }

        _wasVisible = Visible;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Visible)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.adjustSlashMenuPosition", _pickerRef); } catch { }
        }

        if (_needsFocus && Visible)
        {
            _needsFocus = false;
            try { await _inputRef.FocusAsync(); } catch { }
        }
    }

    private void HandleLabelInputAsync(ChangeEventArgs e)
    {
        var value = e.Value?.ToString() ?? string.Empty;
        _label = value.Length > MaxLabelLength ? value[..MaxLabelLength] : value;
    }

    private void SelectColor(NotionStatusColor color) => _color = color;

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            // No Enter handling here: it is handled on the label input itself
            // (HandleInputKeyDownAsync). Keydowns from the color swatches and
            // the insert <button> also bubble to this container — the browser
            // turns Enter on a focused <button> into a click on keydown, so a
            // container-level Enter->insert would fire InsertAsync twice on the
            // button, and would submit while merely picking a color.
            case "Escape":
                await OnClosed.InvokeAsync();
                break;
        }
    }

    // Enter inside the text input produces no native click, so this is the one
    // place where keyboard-activated insert belongs.
    private async Task HandleInputKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !IsInsertDisabled)
            await InsertAsync();
    }

    private async Task InsertAsync()
    {
        var label = _label.Trim();
        if (label.Length == 0)
        {
            return;
        }

        await OnInserted.InvokeAsync((label, _color));
    }

    private async Task HandleBackdropClickAsync() => await OnClosed.InvokeAsync();

    private static string CssColor(NotionStatusColor color) => color.ToString().ToLowerInvariant();
}
