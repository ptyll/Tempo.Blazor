using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbTemplateMenu : ComponentBase
{
    // ── Cascaded context ──────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public Guid                             DatabaseId      { get; set; }
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField>    Fields          { get; set; } = [];
    [Parameter]                 public bool                             ReadOnly        { get; set; }

    [Parameter] public EventCallback<IDatabaseRecord>            OnRecordCreated { get; set; }
    [Parameter] public EventCallback<IDatabaseRecordTemplate>    OnEditTemplate  { get; set; }
    [Parameter] public EventCallback                             OnClose         { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private List<IDatabaseRecordTemplate> _templates = [];
    private bool  _loading;
    private Guid? _deletingId;
    private ElementReference _root;

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key != "Escape") return;
        await OnClose.InvokeAsync();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnInitializedAsync()
        => await LoadAsync();

    private async Task LoadAsync()
    {
        if (Context.DatabaseProvider is null) return;
        _loading = true;
        StateHasChanged();
        try
        {
            _templates = (await Context.DatabaseProvider.GetTemplatesAsync(DatabaseId.ToString())).ToList();
        }
        catch { _templates = []; }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private async Task HandleCreateFromTemplateAsync(IDatabaseRecordTemplate template)
    {
        if (Context.DatabaseProvider is null) return;
        try
        {
            var record = await Context.DatabaseProvider.CreateRecordFromTemplateAsync(
                DatabaseId.ToString(), template.Id.ToString());
            await OnRecordCreated.InvokeAsync(record);
            await OnClose.InvokeAsync();
        }
        catch { /* provider not available */ }
    }

    private async Task HandleNewTemplateAsync()
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        try
        {
            var tpl = new DatabaseRecordTemplate
            {
                DatabaseId = DatabaseId,
                Name       = Loc["TmNotionDbTemplateMenu_DefaultName"]
            };
            var created = await Context.DatabaseProvider.CreateTemplateAsync(DatabaseId.ToString(), tpl);
            _templates.Add(created);
            await OnEditTemplate.InvokeAsync(created);
        }
        catch { }
    }

    private async Task HandleDeleteAsync(Guid templateId)
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        try
        {
            await Context.DatabaseProvider.DeleteTemplateAsync(DatabaseId.ToString(), templateId.ToString());
            _templates.RemoveAll(t => t.Id == templateId);
            _deletingId = null;
            StateHasChanged();
        }
        catch { _deletingId = null; }
    }
}
