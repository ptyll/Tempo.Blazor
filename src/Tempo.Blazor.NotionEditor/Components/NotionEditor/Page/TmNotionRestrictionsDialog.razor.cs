using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Page;

public partial class TmNotionRestrictionsDialog : ComponentBase
{
    /// <summary>Controls whether the dialog is visible.</summary>
    [Parameter] public bool Visible { get; set; }

    /// <summary>Raised when dialog visibility changes.</summary>
    [Parameter] public EventCallback<bool> VisibleChanged { get; set; }

    /// <summary>Page identifier whose restrictions are edited.</summary>
    [Parameter, EditorRequired] public Guid PageId { get; set; }

    /// <summary>Provider used for loading and saving restrictions.</summary>
    [Parameter] public INotionPermissionProvider? Provider { get; set; }

    /// <summary>Effective permission displayed by the dialog when inherited.</summary>
    [Parameter] public PageEffectivePermissionDto? EffectivePermission { get; set; }

    /// <summary>Raised after restrictions are saved successfully.</summary>
    [Parameter] public EventCallback OnSaved { get; set; }

    /// <summary>Forced layout. Mobile presents records as cards and the dialog as a sheet.</summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The cascaded viewport. Auto sheets when the viewport is mobile.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] public TmLayoutContext? Layout { get; set; }

    /// <summary>The layout the dialog is presenting. A host pins it; Auto reads the viewport cascade.</summary>
    private TmLayoutMode ResolvedLayout =>
        LayoutMode != TmLayoutMode.Auto ? LayoutMode : Layout?.Resolved ?? TmLayoutMode.Auto;

    private bool _loadedForVisible;
    private bool _loading;
    private bool _saving;
    private string? _error;
    private PageRestrictionMode _mode = PageRestrictionMode.Open;
    private readonly List<PageRestrictionEntryDto> _entries = [];
    private PageRestrictionSubjectType _newSubjectType = PageRestrictionSubjectType.User;
    private string _newSubjectId = string.Empty;
    private PageRestrictionPermission _newPermission = PageRestrictionPermission.View;

    protected override async Task OnParametersSetAsync()
    {
        if (!Visible)
        {
            _loadedForVisible = false;
            return;
        }

        if (!_loadedForVisible)
        {
            _loadedForVisible = true;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        if (Provider is null) return;

        _loading = true;
        _error = null;
        try
        {
            var restrictions = await Provider.GetRestrictionsAsync(PageId);
            _mode = restrictions.Mode;
            _entries.Clear();
            _entries.AddRange(restrictions.Entries.Select(CloneEntry));
        }
        catch
        {
            _error = Loc["Notion_Restrictions_LoadError"];
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SaveAsync()
    {
        if (Provider is null || _saving) return;

        _saving = true;
        _error = null;
        try
        {
            await Provider.SetRestrictionsAsync(new PageRestrictionDto
            {
                PageId = PageId,
                Mode = _mode,
                Entries = _entries.Select(CloneEntry).ToArray()
            });
            await OnSaved.InvokeAsync();
            await CloseAsync();
        }
        catch
        {
            _error = Loc["Notion_Restrictions_SaveError"];
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task CloseAsync()
    {
        _error = null;
        await VisibleChanged.InvokeAsync(false);
    }

    private void RemoveRestrictions()
    {
        _mode = PageRestrictionMode.Open;
        _entries.Clear();
    }

    private void AddEntry()
    {
        var subjectId = _newSubjectId.Trim();
        if (string.IsNullOrWhiteSpace(subjectId)) return;

        var existing = _entries.FirstOrDefault(entry =>
            entry.SubjectType == _newSubjectType &&
            string.Equals(entry.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _entries.Add(new PageRestrictionEntryDto
            {
                SubjectType = _newSubjectType,
                SubjectId = subjectId,
                Permission = _newPermission
            });
        }
        else
        {
            existing.Permission = _newPermission;
        }

        _newSubjectId = string.Empty;
    }

    private void RemoveEntry(PageRestrictionEntryDto entry)
        => _entries.Remove(entry);

    private void UpdateEntryPermission(PageRestrictionEntryDto entry, ChangeEventArgs args)
    {
        if (Enum.TryParse<PageRestrictionPermission>(args.Value?.ToString(), out var permission) &&
            permission is PageRestrictionPermission.View or PageRestrictionPermission.Comment or PageRestrictionPermission.Edit)
            entry.Permission = permission;
    }

    private void HandleModeChanged(ChangeEventArgs args)
    {
        if (Enum.TryParse<PageRestrictionMode>(args.Value?.ToString(), out var mode))
            _mode = mode;
    }

    private void HandleSubjectTypeChanged(ChangeEventArgs args)
    {
        if (Enum.TryParse<PageRestrictionSubjectType>(args.Value?.ToString(), out var type))
            _newSubjectType = type;
    }

    private void HandlePermissionChanged(ChangeEventArgs args)
    {
        if (Enum.TryParse<PageRestrictionPermission>(args.Value?.ToString(), out var permission) &&
            permission is PageRestrictionPermission.View or PageRestrictionPermission.Comment or PageRestrictionPermission.Edit)
            _newPermission = permission;
    }

    private string SubjectTypeText(PageRestrictionSubjectType type)
        => type == PageRestrictionSubjectType.User
            ? Loc["Notion_Restrictions_User"]
            : Loc["Notion_Restrictions_Group"];

    private static PageRestrictionEntryDto CloneEntry(PageRestrictionEntryDto entry) => new()
    {
        SubjectType = entry.SubjectType,
        SubjectId = entry.SubjectId,
        Permission = entry.Permission
    };
}
