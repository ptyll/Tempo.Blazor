using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionColorPicker : ComponentBase
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public bool    Visible           { get; set; }
    [Parameter] public double  Top               { get; set; }
    [Parameter] public double  Left              { get; set; }

    /// <summary>Currently active text-color hex, or <c>null</c> for default.</summary>
    [Parameter] public string? SelectedTextColor { get; set; }

    /// <summary>Currently active background-color hex, or <c>null</c> for default.</summary>
    [Parameter] public string? SelectedBgColor   { get; set; }

    /// <summary>
    /// Raised when a color is chosen.
    /// Exactly one element of the tuple is non-null:
    ///   (hex, null) → text color selected;
    ///   (null, hex) → background color selected.
    ///   (null, null) → default (remove color) selected for either section.
    /// </summary>
    [Parameter] public EventCallback<(string? TextColor, string? BgColor)> OnColorSelected { get; set; }

    /// <summary>Raised when the picker is dismissed without a selection.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>Forced layout. Mobile presents the menu as the core sheet.</summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    // ── Internal model ────────────────────────────────────────────────────────

    private sealed record ColorEntry(string NameKey, string? Hex);

    // ── Static data ───────────────────────────────────────────────────────────

    private static readonly ColorEntry[] _textColors =
    [
        new("TmNotionColor_Default", null),
        new("TmNotionColor_Gray",    "gray"),
        new("TmNotionColor_Brown",   "brown"),
        new("TmNotionColor_Orange",  "orange"),
        new("TmNotionColor_Yellow",  "yellow"),
        new("TmNotionColor_Green",   "green"),
        new("TmNotionColor_Blue",    "blue"),
        new("TmNotionColor_Purple",  "purple"),
        new("TmNotionColor_Pink",    "pink"),
        new("TmNotionColor_Red",     "red"),
    ];

    private static readonly ColorEntry[] _bgColors =
    [
        new("TmNotionColor_Default", null),
        new("TmNotionColor_Gray",    "gray"),
        new("TmNotionColor_Brown",   "brown"),
        new("TmNotionColor_Orange",  "orange"),
        new("TmNotionColor_Yellow",  "yellow"),
        new("TmNotionColor_Green",   "green"),
        new("TmNotionColor_Blue",    "blue"),
        new("TmNotionColor_Purple",  "purple"),
        new("TmNotionColor_Pink",    "pink"),
        new("TmNotionColor_Red",     "red"),
    ];

    // ── State ─────────────────────────────────────────────────────────────────

    private bool   _wasVisible;

    // ── Handlers ──────────────────────────────────────────────────────────────

    private async Task HandleTextColorAsync(string? hex)
        => await OnColorSelected.InvokeAsync((hex, null));

    private async Task HandleBgColorAsync(string? hex)
        => await OnColorSelected.InvokeAsync((null, hex));

    private async Task HandleBackdropClickAsync()
        => await OnClosed.InvokeAsync();

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await OnClosed.InvokeAsync();
    }
}
