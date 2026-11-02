using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

public partial class TmPdfTemplateDesigner
{

    private Task CopyContextFieldFromInspectorAsync(SigningField field)
    {
        _contextAction = new ContextMenuState(ContextMenuKind.Field, 0, 0, field.Uuid, field.Areas.FirstOrDefault()?.AttachmentUuid, field.Areas.FirstOrDefault()?.Page ?? _currentPageIndex, null, null);
        return CopyContextFieldAsync();
    }

    private Task DeleteContextFieldFromInspectorAsync(SigningField field)
    {
        _contextAction = new ContextMenuState(ContextMenuKind.Field, 0, 0, field.Uuid, null, _currentPageIndex, null, null);
        return DeleteContextFieldAsync();
    }

    private Task PasteFromInspectorAsync()
    {
        var page = CurrentPage ?? VisibleDocuments.FirstOrDefault();
        if (page is null)
        {
            return Task.CompletedTask;
        }

        _contextAction = new ContextMenuState(ContextMenuKind.Page, 0, 0, null, page.AttachmentUuid, page.PageIndex, 0.5, 0.5);
        return PasteFieldAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedFieldUuids.Count == 0)
        {
            return;
        }

        _fields.RemoveAll(field => _selectedFieldUuids.Contains(field.Uuid));
        _selectedFieldUuids.Clear();
        _contextMenu = null;
        await NotifyFieldsChangedAsync();
    }

    /// <summary>A 500 ms touch long-press on a field, reported by the pointer session.</summary>
    [JSInvokable]
    public Task OpenFieldContextMenu(string? fieldUuid, double clientX, double clientY)
        => OpenFieldContextMenu(new FieldContextMenuRequest
        {
            FieldUuid = fieldUuid,
            ClientX = clientX,
            ClientY = clientY
        });

    private Task OpenFieldContextMenu(FieldContextMenuRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.FieldUuid))
        {
            return Task.CompletedTask;
        }

        var field = _fields.FirstOrDefault(item => item.Uuid == request.FieldUuid);
        if (field is null)
        {
            return Task.CompletedTask;
        }

        return OpenFieldContextMenuAsync(new TmSigningFieldOverlayPointerEventArgs(
            field,
            field.Areas.FirstOrDefault(),
            new MouseEventArgs { ClientX = request.ClientX, ClientY = request.ClientY }));
    }

    private async Task OpenFieldContextMenuAsync(TmSigningFieldOverlayPointerEventArgs args)
    {
        if (args.Field is null)
        {
            return;
        }

        if (!_selectedFieldUuids.Contains(args.Field.Uuid))
        {
            _selectedFieldUuids.Clear();
            _selectedFieldUuids.Add(args.Field.Uuid);
        }

        var origin = await GetFixedOriginAsync();
        _contextMenu = new ContextMenuState(
            SelectedFieldCount > 1 ? ContextMenuKind.Selection : ContextMenuKind.Field,
            args.MouseEventArgs.ClientX - origin.Left,
            args.MouseEventArgs.ClientY - origin.Top,
            args.Field.Uuid,
            args.Area?.AttachmentUuid,
            args.Area?.Page ?? 0,
            null,
            null);
        _contextMenuPending = true;
    }

    private async Task OpenPageContextMenu(SigningDocumentPage page, MouseEventArgs args)
    {
        var point = await ToPointAsync(page, args);
        var origin = await GetFixedOriginAsync();
        _contextMenu = new ContextMenuState(
            ContextMenuKind.Page,
            args.ClientX - origin.Left,
            args.ClientY - origin.Top,
            null,
            page.AttachmentUuid,
            page.PageIndex,
            point.X,
            point.Y);
        _contextMenuPending = true;
    }

    // Built during render, while the menu is open. Close unmounts the item before the handler
    // runs, so the callback must already hold the field or page it will act on.
    private EventCallback CopyMenuField(string? fieldUuid) =>
        EventCallback.Factory.Create(this, () => CopyFieldAsync(fieldUuid));

    private EventCallback DeleteMenuField(string? fieldUuid) =>
        EventCallback.Factory.Create(this, () => DeleteFieldAsync(fieldUuid));

    private EventCallback PasteMenuField(ContextMenuState? target) =>
        EventCallback.Factory.Create(this, () => PasteFieldAsync(target));

    private EventCallback CopyMenuSelection() =>
        EventCallback.Factory.Create(this, CopySelectionAsync);

    private Task CopyContextFieldAsync() => CopyFieldAsync(_contextAction?.FieldUuid);

    private Task CopyFieldAsync(string? fieldUuid)
    {
        var field = _fields.FirstOrDefault(item => item.Uuid == fieldUuid);
        _clipboardFields.Clear();
        if (field is not null)
        {
            _clipboardFields.Add(Clone(field));
            _clipboardStatus = Loc["TmPdfTemplateDesigner_FieldCopied"];
        }

        _contextMenu = null;
        return InvokeAsync(StateHasChanged);
    }

    private Task CopySelectionAsync()
    {
        _clipboardFields.Clear();
        _clipboardFields.AddRange(_fields
            .Where(item => _selectedFieldUuids.Contains(item.Uuid))
            .Select(Clone));

        if (_clipboardFields.Count > 0)
        {
            _clipboardStatus = _clipboardFields.Count == 1
                ? Loc["TmPdfTemplateDesigner_FieldCopied"]
                : Loc["TmPdfTemplateDesigner_SelectionCopied"];
        }

        _contextMenu = null;
        return InvokeAsync(StateHasChanged);
    }

    private Task DeleteContextFieldAsync() => DeleteFieldAsync(_contextAction?.FieldUuid);

    private async Task DeleteFieldAsync(string? fieldUuid)
    {
        if (fieldUuid is null)
        {
            return;
        }

        _fields.RemoveAll(field => field.Uuid == fieldUuid);
        _selectedFieldUuids.Remove(fieldUuid);
        _contextMenu = null;
        await NotifyFieldsChangedAsync();
    }

    private Task PasteFieldAsync() => PasteFieldAsync(_contextAction);

    private async Task PasteFieldAsync(ContextMenuState? target)
    {
        if (_clipboardFields.Count == 0 || target is null)
        {
            return;
        }

        var clones = _clipboardFields.Select(Clone).ToList();
        var allAreas = clones.SelectMany(field => field.Areas).ToArray();
        var sourceBounds = allAreas.Length > 0
            ? SigningGeometryHelper.GetSelectionRectangle(allAreas)
            : new SigningRectangle(0, 0, MinWidth, MinHeight);
        var targetX = target.DocumentX ?? Clamp(sourceBounds.X + 0.03 + sourceBounds.Width / 2, 0, 1);
        var targetY = target.DocumentY ?? Clamp(sourceBounds.Y + 0.03 + sourceBounds.Height / 2, 0, 1);
        var desiredX = Clamp(targetX - sourceBounds.Width / 2, 0, Math.Max(0, 1 - sourceBounds.Width));
        var desiredY = Clamp(targetY - sourceBounds.Height / 2, 0, Math.Max(0, 1 - sourceBounds.Height));
        var deltaX = desiredX - sourceBounds.X;
        var deltaY = desiredY - sourceBounds.Y;

        foreach (var clone in clones)
        {
            clone.Uuid = Guid.NewGuid().ToString("N");
            clone.Name = $"{clone.Name} copy".Trim();
            foreach (var area in clone.Areas)
            {
                area.Uuid = Guid.NewGuid().ToString("N");
                area.AttachmentUuid = target.AttachmentUuid;
                area.Page = target.PageIndex;
                area.X = Clamp(area.X + deltaX, 0, Math.Max(0, 1 - area.Width));
                area.Y = Clamp(area.Y + deltaY, 0, Math.Max(0, 1 - area.Height));
            }
        }

        _fields.AddRange(clones);
        _selectedFieldUuids.Clear();
        foreach (var clone in clones)
        {
            _selectedFieldUuids.Add(clone.Uuid);
        }

        _contextMenu = null;
        _clipboardStatus = clones.Count == 1
            ? Loc["TmPdfTemplateDesigner_FieldPasted"]
            : Loc["TmPdfTemplateDesigner_SelectionPasted"];
        await NotifyFieldsChangedAsync();
    }

    private async Task CopyFieldToAllPagesAsync(SigningField field)
    {
        var target = _fields.FirstOrDefault(item => item.Uuid == field.Uuid);
        var sourceArea = target?.Areas.FirstOrDefault();
        if (target is null || sourceArea is null)
        {
            return;
        }

        foreach (var page in Documents.Where(page => page.AttachmentUuid == sourceArea.AttachmentUuid))
        {
            if (target.Areas.Any(area => area.AttachmentUuid == page.AttachmentUuid && area.Page == page.PageIndex))
            {
                continue;
            }

            var copy = Clone(sourceArea);
            copy.Uuid = Guid.NewGuid().ToString("N");
            copy.Page = page.PageIndex;
            copy.AttachmentUuid = page.AttachmentUuid;
            target.Areas.Add(copy);
        }

        await NotifyFieldsChangedAsync();
    }
}
