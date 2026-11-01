using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

public partial class TmPdfTemplateDesigner
{
    /// <summary>
    /// Commits one move or resize. The shared pointer helper captures the pointer, so this is the only
    /// path a real browser has: the deltas are already normalized against the measured page surface.
    /// Primitives keep the designer payload records internal — they are not part of the component contract.
    /// </summary>
    [JSInvokable]
    public Task CommitFieldDrag(string? fieldUuid, string? handle, string? areaUuid, double normalizedDx, double normalizedDy)
        => CommitFieldDrag(new DesignerDragCommit
        {
            FieldUuid = fieldUuid,
            Handle = handle,
            AreaUuid = areaUuid,
            NormalizedDx = normalizedDx,
            NormalizedDy = normalizedDy
        });

    private async Task CommitFieldDrag(DesignerDragCommit? commit)
    {
        if (Disabled || ResolvedLayout == TmLayoutMode.Mobile || commit is null || string.IsNullOrWhiteSpace(commit.FieldUuid))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(commit.Handle)
            && Enum.TryParse<SigningResizeHandle>(commit.Handle, out var handle))
        {
            var field = _fields.FirstOrDefault(item => item.Uuid == commit.FieldUuid);
            var area = field?.Areas.FirstOrDefault(item =>
                string.IsNullOrWhiteSpace(commit.AreaUuid) || item.Uuid == commit.AreaUuid)
                ?? field?.Areas.FirstOrDefault();
            if (field is null || area is null)
            {
                return;
            }

            _selectedFieldUuids.Clear();
            _selectedFieldUuids.Add(field.Uuid);
            // A new area instance: the overlay skips rendering when the Area parameter is the same object.
            ReplaceArea(field, area, SigningGeometryHelper.Resize(Clone(area), handle, commit.NormalizedDx, commit.NormalizedDy, MinWidth, MinHeight));
            await InvokeAsync(StateHasChanged);
            await NotifyFieldsChangedAsync();
            return;
        }

        if (!_selectedFieldUuids.Contains(commit.FieldUuid))
        {
            _selectedFieldUuids.Clear();
            _selectedFieldUuids.Add(commit.FieldUuid);
        }

        var snapshots = _fields
            .Where(field => _selectedFieldUuids.Contains(field.Uuid))
            .SelectMany(field => field.Areas.Select(area => new AreaSnapshot(field.Uuid, area.Uuid, Clone(area))))
            .ToArray();
        _moveState = new MoveState(0, 0, snapshots);
        ApplyMove(commit.NormalizedDx, commit.NormalizedDy);
        _moveState = null;
        await NotifyFieldsChangedAsync();
    }

    /// <summary>
    /// Places the palette type under a pointer that ended on a page. Capture swallows the page's own
    /// pointerup, so the session reports the point directly.
    /// </summary>
    [JSInvokable]
    public Task CommitPaletteDrop(string? pageKey, double x, double y, string? type = null)
        => CommitPaletteDrop(new PaletteDropCommit { PageKey = pageKey, X = x, Y = y, Type = type });

    /// <summary>
    /// An off-page release or a cancelled palette drag places nothing and leaves draw mode alone.
    /// A tap is not this path: the palette button's own click is the only activation.
    /// </summary>
    [JSInvokable]
    public Task PaletteDragCancelled()
    {
        _dragType = null;
        return Task.CompletedTask;
    }

    private async Task CommitPaletteDrop(PaletteDropCommit? commit)
    {
        var type = _dragType ?? (Enum.TryParse<SigningFieldType>(commit?.Type, out var parsed) ? parsed : (SigningFieldType?)null);
        if (Disabled || _drawType.HasValue || commit is null || type is not { } fieldType)
        {
            return;
        }

        var page = Documents.FirstOrDefault(item => GetPageKey(item) == commit.PageKey) ?? VisibleDocuments.FirstOrDefault();
        if (page is null)
        {
            return;
        }

        _dragType = null;
        var area = CreateDefaultAreaAtPoint(page, fieldType, Clamp(commit.X, 0, 1), Clamp(commit.Y, 0, 1));
        var field = CreateField(fieldType, area);
        _fields.Add(field);
        _selectedFieldUuids.Clear();
        _selectedFieldUuids.Add(field.Uuid);
        _dragType = null;
        _drawType = null;
        await NotifyFieldsChangedAsync();
    }

    private async Task HandlePalettePointerUpAsync(SigningDocumentPage page, PointerEventArgs args)
    {
        if (Disabled || _dragType is not { } type)
        {
            return;
        }

        var point = await ToPointAsync(page, args);
        var area = CreateDefaultAreaAtPoint(page, type, point.X, point.Y);
        var field = CreateField(type, area);
        _fields.Add(field);
        _selectedFieldUuids.Clear();
        _selectedFieldUuids.Add(field.Uuid);
        _dragType = null;
        _drawType = null;
        await NotifyFieldsChangedAsync();
    }

    private async Task HandlePagePointerDownAsync(SigningDocumentPage page, PointerEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        // A field or handle press stops bubbling, so it never starts a selection box. The pointer
        // session still sees that press: its listener is on the capture phase. A palette drag is
        // the same kind of press. The JS session stops it before this handler; the Blazor fallback
        // has no module, so it sees the press and recognises it by the drag the palette pointerdown set.
        if (_moveState is not null || _resizeState is not null || _dragType.HasValue)
        {
            return;
        }

        var point = await ToPointAsync(page, args);
        if (_drawType.HasValue)
        {
            _drawState = new DrawState(page, point.X, point.Y, new SigningFieldArea
            {
                Uuid = Guid.NewGuid().ToString("N"),
                AttachmentUuid = page.AttachmentUuid,
                Page = page.PageIndex,
                X = point.X,
                Y = point.Y
            });
            return;
        }

        _selectedFieldUuids.Clear();
        _drawState = new DrawState(page, point.X, point.Y, new SigningFieldArea
        {
            Uuid = "selection",
            AttachmentUuid = page.AttachmentUuid,
            Page = page.PageIndex,
            X = point.X,
            Y = point.Y
        }, isSelectionBox: true);
        await Task.CompletedTask;
    }

    private async Task HandlePagePointerMoveAsync(SigningDocumentPage page, PointerEventArgs args)
    {
        if (_dragType.HasValue)
        {
            return;
        }

        if (_drawState is null || _drawState.PageKey != GetPageKey(page))
        {
            return;
        }

        var point = await ToPointAsync(page, args);
        _drawState.Area = CreateAreaFromPoints(_drawState.Page, _drawState.StartX, _drawState.StartY, point.X, point.Y, _drawState.Area.Uuid);
    }

    private async Task HandlePagePointerUpAsync(SigningDocumentPage page, PointerEventArgs args)
    {
        if (!_fieldDragAttached)
        {
            await HandlePalettePointerUpAsync(page, args);
        }

        // A palette release places a field. It must not also close a selection box or a draw.
        // PointerEventArgs.Type is "pointerup", never the target tag, so a tag check cannot see this.
        if (_dragType.HasValue)
        {
            _drawState = null;
            return;
        }

        if (_drawState is null || _drawState.PageKey != GetPageKey(page))
        {
            return;
        }

        var point = await ToPointAsync(page, args);
        _drawState.Area = CreateAreaFromPoints(_drawState.Page, _drawState.StartX, _drawState.StartY, point.X, point.Y, _drawState.Area.Uuid);
        await HandlePointerUpAsync(args);
    }

    private async Task HandlePointerMoveAsync(PointerEventArgs args)
    {
        // A real browser commits through the pointer helper (CommitFieldDrag). These handlers stay for
        // bUnit, which has no module, and must not double-apply a gesture the helper already owns.
        if (_fieldDragAttached)
        {
            return;
        }

        if (_moveState is not null)
        {
            var deltaX = (args.ClientX - _moveState.StartClientX) / DefaultPageWidth;
            var deltaY = (args.ClientY - _moveState.StartClientY) / DefaultPageHeight;
            ApplyMove(deltaX, deltaY);
            return;
        }

        if (_resizeState is not null)
        {
            var deltaX = (args.ClientX - _resizeState.StartClientX) / DefaultPageWidth;
            var deltaY = (args.ClientY - _resizeState.StartClientY) / DefaultPageHeight;
            ApplyResize(deltaX, deltaY);
            await NotifyFieldsChangedAsync();
        }
    }

    private async Task HandlePointerUpAsync(PointerEventArgs args)
    {
        if (_drawState is not null)
        {
            var state = _drawState;
            _drawState = null;

            if (state.Area.Width < MinWidth || state.Area.Height < MinHeight)
            {
                return;
            }

            if (state.IsSelectionBox)
            {
                SelectFieldsInRectangle(state.Page, state.Area);
                return;
            }

            if (_drawType.HasValue)
            {
                var field = CreateField(_drawType.Value, state.Area);
                _fields.Add(field);
                _selectedFieldUuids.Clear();
                _selectedFieldUuids.Add(field.Uuid);
                await NotifyFieldsChangedAsync();
            }
        }

        if (_moveState is not null)
        {
            _moveState = null;
            if (!_fieldDragAttached)
            {
                await NotifyFieldsChangedAsync();
            }
        }

        _resizeState = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_fieldDrag is not null)
            {
                await _fieldDrag.InvokeVoidAsync("detach");
                await _fieldDrag.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone.
        }
        catch (JSException)
        {
            // The module was disposed with the page.
        }

        _selfRef?.Dispose();
        _selfRef = null;
        if (_jsModule is not null)
        {
            try
            {
                await _jsModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is already gone.
            }
        }
    }

    private async Task HandleDesignerKeyDownAsync(KeyboardEventArgs args)
    {
        // KeyboardEventArgs.Type is the key event ("keydown"), never the target element, so a guard
        // on it cannot tell Detect from the workspace. The JS listener stops the bubble for a form
        // control before this handler runs; bUnit has no module and dispatches the handler directly.
        if (Disabled)
        {
            return;
        }

        if (args.Key is "Escape" && _contextMenu is not null)
        {
            _contextMenu = null;
            _contextMenuPending = false;
            return;
        }

        // APG: Shift+F10 and the ContextMenu key open the menu for the focused field.
        if (args.Key is "ContextMenu" || (args.Key is "F10" && args.ShiftKey))
        {
            var field = _fields.FirstOrDefault(item => _selectedFieldUuids.Contains(item.Uuid));
            if (field is not null)
            {
                await OpenFieldContextMenuAsync(new TmSigningFieldOverlayPointerEventArgs(
                    field,
                    field.Areas.FirstOrDefault() ?? new SigningFieldArea(),
                    new MouseEventArgs()));
            }

            return;
        }

        if (_selectedFieldUuids.Count == 0)
        {
            return;
        }

        if (args.Key is "Delete")
        {
            await DeleteSelectedAsync();
            return;
        }

        var modifier = args.CtrlKey || args.MetaKey;
        if (modifier && args.Key is "c" or "C")
        {
            await CopySelectionAsync();
            return;
        }

        if (modifier && args.Key is "v" or "V")
        {
            if (_contextAction is null)
            {
                var page = CurrentPage ?? VisibleDocuments.FirstOrDefault();
                if (page is not null)
                {
                    _contextAction = new ContextMenuState(
                        ContextMenuKind.Page, 0, 0, null, page.AttachmentUuid, page.PageIndex, 0.5, 0.5);
                }
            }

            await PasteFieldAsync();
            return;
        }

        var (deltaX, deltaY) = args.Key switch
        {
            "ArrowLeft" => (-0.01, 0d),
            "ArrowRight" => (0.01, 0d),
            "ArrowUp" => (0d, -0.01),
            "ArrowDown" => (0d, 0.01),
            _ => (0d, 0d)
        };
        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        foreach (var field in _fields.Where(item => _selectedFieldUuids.Contains(item.Uuid)))
        {
            foreach (var area in field.Areas)
            {
                var moved = SigningGeometryHelper.Move(area, deltaX, deltaY);
                area.X = moved.X;
                area.Y = moved.Y;
            }
        }

        await NotifyFieldsChangedAsync();
    }
}
