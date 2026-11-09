using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.NotionEditor.Interfaces;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.TempoBlocks;

public partial class TmNotionSpreadsheetEditModal : ComponentBase
{
    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired]
    public Guid SpreadsheetDocumentId { get; set; }

    [Parameter]
    public ISpreadsheetDocumentProvider? Provider { get; set; }

    [Parameter] public EventCallback<SpreadsheetWorkbook> OnSaved     { get; set; }
    [Parameter] public EventCallback                       OnDiscarded { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private DynamicComponent? _spreadsheetRef;
    private SpreadsheetWorkbook _workbook = new();
    private bool _loading = true;
    private bool _saving;
    private bool _dirty;
    private static readonly Type? SpreadsheetComponentType = ResolveSpreadsheetComponentType();

    [CascadingParameter(Name = TmNotionTempoEditorModal.EditorLayoutScope)]
    public TmLayoutMode? TempoEditorLayout { get; set; }

    private bool CompactEditor => TempoEditorLayout == TmLayoutMode.Mobile;

    private Dictionary<string, object?> SpreadsheetEditorParameters
    {
        get
        {
            var parameters = new Dictionary<string, object?>
            {
                ["InitialWorkbook"] = _workbook,
                ["OnChange"] = EventCallback.Factory.Create<SpreadsheetChangeEventArgs>(this, _ => _dirty = true),
                ["Mode"] = SpreadsheetMode.Full,
                ["Height"] = "calc(100vh - 120px)",
                ["Width"] = "100%",
            };
            // Below 1024 the shell's compact layout wins. At 1024 and above the key stays absent so
            // the spreadsheet classifies its own container.
            if (CompactEditor)
                parameters["LayoutMode"] = TmLayoutMode.Mobile;
            return parameters;
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override async Task OnInitializedAsync()
    {
        if (Provider is not null)
        {
            try
            {
                var loaded = await Provider.GetSpreadsheetDocumentAsync(SpreadsheetDocumentId);
                if (loaded is not null)
                    _workbook = loaded;
            }
            catch { }
        }
        if (_workbook.Sheets.Count == 0)
            _workbook.AddSheet("Sheet1"); // constructor may not have run (e.g. deserialized from JSON)
        _loading = false;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        if (_saving) return;
        _saving = true;
        var workbookToSave = GetRenderedWorkbook() ?? _workbook;
        try
        {
            if (Provider is not null)
            {
                try { workbookToSave = await Provider.SaveSpreadsheetDocumentAsync(SpreadsheetDocumentId, workbookToSave); }
                catch { }
            }
        }
        finally
        {
            _saving = false;
        }
        await OnSaved.InvokeAsync(workbookToSave);
    }

    private async Task DiscardAsync() => await OnDiscarded.InvokeAsync();

    private SpreadsheetWorkbook? GetRenderedWorkbook()
    {
        var instance = _spreadsheetRef?.Instance;
        var property = instance?.GetType().GetProperty("Workbook");
        return property?.GetValue(instance) as SpreadsheetWorkbook;
    }

    private static Type? ResolveSpreadsheetComponentType()
        => Type.GetType("Tempo.Blazor.Components.Spreadsheet.TmSpreadsheet, Tempo.Blazor.Spreadsheet")
           ?? AppDomain.CurrentDomain.GetAssemblies()
               .Select(assembly => assembly.GetType("Tempo.Blazor.Components.Spreadsheet.TmSpreadsheet", throwOnError: false))
               .FirstOrDefault(type => type is not null);
}
