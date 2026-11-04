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

    private Task CloseAsync() => OnClosed.InvokeAsync();

    private Task OnKeyDown(KeyboardEventArgs args)
        => args.Key == "Escape" ? OnClosed.InvokeAsync() : Task.CompletedTask;
}
