using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>The one header of a Notion shell panel: title, optional subtitle, actions and close.</summary>
public partial class TmNotionPanelHeader : TmComponentBase
{
    /// <summary>The panel title. Rendered as an <c>h2</c>.</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    /// <summary>Optional subtitle under the title.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Host actions rendered before the close button.</summary>
    [Parameter] public RenderFragment? Actions { get; set; }

    /// <summary>Raised by the close button and by Escape.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Moves focus to the (<c>tabindex="-1"</c>) title when the panel opens. Defaults to true: the
    /// docked shell panel's focus scope closes on Escape only while focus is inside it, and the
    /// toolbar trigger that opened the panel otherwise keeps focus outside.
    /// </summary>
    [Parameter] public bool FocusOnOpen { get; set; } = true;

    private ElementReference _titleRef;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (!firstRender || !FocusOnOpen)
            return;

        try { await _titleRef.FocusAsync(preventScroll: true); }
        catch { /* prerender / renderer without JS */ }
    }

    private Task CloseAsync() => OnClosed.InvokeAsync();

    private Task OnKeyDown(KeyboardEventArgs args)
        => args.Key == "Escape" ? OnClosed.InvokeAsync() : Task.CompletedTask;
}
