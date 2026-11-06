using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.NotionEditor.Interfaces;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.TempoBlocks;

public partial class TmNotionWireframeEditModal : ComponentBase
{
    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired]
    public Guid WireframeDocumentId { get; set; }

    [Parameter]
    public IWireframeDocumentProvider? Provider { get; set; }

    [Parameter] public EventCallback<(WireframeDocument Document, string SvgPreview)> OnSaved     { get; set; }
    [Parameter] public EventCallback                                                    OnDiscarded { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private DynamicComponent? _editorRef;
    private WireframeDocument? _document;
    private bool                _loading = true;
    private bool                _saving;
    private bool                _dirty;
    private static readonly Type? WireframeEditorComponentType = ResolveWireframeEditorComponentType();

    [CascadingParameter(Name = TmNotionTempoEditorModal.EditorLayoutScope)]
    public TmLayoutMode? TempoEditorLayout { get; set; }

    private bool CompactEditor => TempoEditorLayout == TmLayoutMode.Mobile;

    private Dictionary<string, object?> WireframeEditorParameters => new()
    {
        ["Document"] = _document,
        ["DocumentChanged"] = EventCallback.Factory.Create<WireframeDocument>(this, OnDocumentChanged),
        // Below 1024 the shell reports a compact layout. At 1024 and above the editor classifies
        // its own container.
        ["LayoutMode"] = CompactEditor ? TmLayoutMode.Mobile : TmLayoutMode.Auto,
        ["ShowToolbox"] = !CompactEditor,
        ["ShowPropertiesPanel"] = !CompactEditor,
        ["ShowMinimap"] = !CompactEditor,
        ["ShowToolbar"] = true,
        ["ShowGrid"] = true,
        ["ShowPageTabs"] = true,
        ["Class"] = "tm-notion-wireframe-edit-modal__editor"
    };

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override async Task OnInitializedAsync()
    {
        if (Provider is not null)
        {
            try { _document = await Provider.GetWireframeDocumentAsync(WireframeDocumentId); }
            catch { }
        }
        if (_document is null)
        {
            _document = new WireframeDocument();
            _document.EnsureActivePage();
        }
        _loading = false;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        if (_document is null || _saving) return;
        _saving = true;
        string svg = string.Empty;
        try
        {
            svg = await TryExportSvgAsync();
        }
        catch { }
        finally
        {
            _saving = false;
        }
        await OnSaved.InvokeAsync((_document, svg));
    }

    private Task OnDocumentChanged(WireframeDocument value)
    {
        _document = value;
        _dirty = true;
        return Task.CompletedTask;
    }

    private async Task DiscardAsync() => await OnDiscarded.InvokeAsync();

    private async Task<string> TryExportSvgAsync()
    {
        var instance = _editorRef?.Instance;
        var method = instance?.GetType().GetMethod("ExportSvgAsync");
        if (method?.Invoke(instance, null) is Task<string> task)
        {
            return await task;
        }

        return string.Empty;
    }

    private static Type? ResolveWireframeEditorComponentType()
        => Type.GetType("Tempo.Blazor.Components.Wireframe.TmWireframeEditor, Tempo.Blazor.Wireframe")
           ?? AppDomain.CurrentDomain.GetAssemblies()
               .Select(assembly => assembly.GetType("Tempo.Blazor.Components.Wireframe.TmWireframeEditor", throwOnError: false))
               .FirstOrDefault(type => type is not null);
}
