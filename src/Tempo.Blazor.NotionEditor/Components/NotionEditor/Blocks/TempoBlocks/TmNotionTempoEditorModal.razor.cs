using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.TempoBlocks;

/// <summary>
/// The shared edit-modal shell of the Diagram, Spreadsheet and Wireframe Notion blocks (N12).
/// Discard while <see cref="IsDirty"/> is true confirms first. Escape is the core TmModal's own
/// handler and takes the same discard path; the confirm dialog sits above the modal, so Escape
/// closes the confirm first.
/// </summary>
public partial class TmNotionTempoEditorModal : ComponentBase
{
    /// <summary>The modal title.</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    /// <summary>True while the document is loading. The editor body is not mounted.</summary>
    [Parameter] public bool Loading { get; set; }

    /// <summary>True while a save is in flight. Both footer actions disable.</summary>
    [Parameter] public bool Saving { get; set; }

    /// <summary>True when the editor package is absent. Save stays disabled.</summary>
    [Parameter] public bool Missing { get; set; }

    /// <summary>The missing-package message. Rendered only while <see cref="Missing"/>.</summary>
    [Parameter] public string? MissingText { get; set; }

    /// <summary>True once the embedded editor has changed the document.</summary>
    [Parameter] public bool IsDirty { get; set; }

    /// <summary>The discard action label.</summary>
    [Parameter, EditorRequired] public string DiscardText { get; set; } = string.Empty;

    /// <summary>The save action label.</summary>
    [Parameter, EditorRequired] public string SaveText { get; set; } = string.Empty;

    /// <summary>The dirty-discard confirm title.</summary>
    [Parameter, EditorRequired] public string ConfirmTitle { get; set; } = string.Empty;

    /// <summary>The dirty-discard confirm message.</summary>
    [Parameter, EditorRequired] public string ConfirmMessage { get; set; } = string.Empty;

    /// <summary>The confirm action of the dirty-discard dialog.</summary>
    [Parameter, EditorRequired] public string ConfirmOkText { get; set; } = string.Empty;

    /// <summary>The cancel action of the dirty-discard dialog.</summary>
    [Parameter, EditorRequired] public string ConfirmCancelText { get; set; } = string.Empty;

    /// <summary>The editor body. Not rendered while loading or missing.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Raised by the save action.</summary>
    [Parameter] public EventCallback OnSave { get; set; }

    /// <summary>Raised once a discard is confirmed (immediately when not dirty).</summary>
    [Parameter] public EventCallback OnDiscard { get; set; }

    /// <summary>Legacy status class kept on the loading and missing states.</summary>
    [Parameter] public string? StatusClass { get; set; }

    /// <summary>Legacy class kept on the discard action.</summary>
    [Parameter] public string? DiscardClass { get; set; }

    /// <summary>Legacy class kept on the save action.</summary>
    [Parameter] public string? SaveClass { get; set; }

    /// <summary>Extra classes on the modal root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>The modal test id.</summary>
    [Parameter] public string? TestId { get; set; }

    /// <summary>Cascading name for <see cref="EditorLayout"/>, read by the embedded edit modals.</summary>
    public const string EditorLayoutScope = "TmNotionTempoEditorLayout";

    /// <summary>Below 1024 is compact; from 1280 the panel is XLarge with Maximize.</summary>
    public static readonly TmLayoutBreakpoints EditorBands = new(1024, 1280);

    /// <summary>
    /// Forced layout. Auto measures the viewport with <see cref="EditorBands"/>, then falls back to
    /// the cascaded viewport until that measurement arrives.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The cascaded viewport, used until the 1024/1280 probe reports.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] public TmLayoutContext? Layout { get; set; }

    private bool _confirming;
    private bool _maximized;
    private TmLayoutMode? _measured;

    private TmLayoutMode Band =>
        LayoutMode != TmLayoutMode.Auto ? LayoutMode
        : _measured ?? Layout?.Resolved ?? TmLayoutMode.Desktop;

    /// <summary>A viewport at or above 1280 is the only band that can grow to fullscreen.</summary>
    private bool CanMaximize => Band == TmLayoutMode.Desktop;

    private ModalSize ModalSize =>
        Band == TmLayoutMode.Desktop && _maximized ? ModalSize.Fullscreen : ModalSize.XLarge;

    /// <summary>Below 1024 the embedded editor is compact: no toolbox, inspector or minimap.</summary>
    public TmLayoutMode EditorLayout =>
        Band == TmLayoutMode.Mobile ? TmLayoutMode.Mobile : TmLayoutMode.Desktop;

    private void OnBandMeasured(TmLayoutMode mode) => _measured = mode;

    private void ToggleMaximize() => _maximized = !_maximized;

    private async Task RequestDiscardAsync()
    {
        if (Saving) return;
        if (IsDirty)
        {
            _confirming = true;
            return;
        }

        await OnDiscard.InvokeAsync();
    }

    private async Task ResolveConfirmAsync(bool? confirmed)
    {
        _confirming = false;
        if (confirmed == true)
        {
            await OnDiscard.InvokeAsync();
        }
    }
}
