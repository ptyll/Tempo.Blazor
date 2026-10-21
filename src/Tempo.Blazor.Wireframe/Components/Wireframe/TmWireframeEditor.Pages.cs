using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

// Page management of the horizontal page tabs (PageNavigation=Tabs) and the Ctrl+PageUp / Ctrl+PageDown switch; the
// Pages panel (PageNavigation=Panel) mutates the same document through WireframePageOperations. Kept beside the editor
// to hold the main file under the source-file length ceiling.
public partial class TmWireframeEditor
{
    // ── Page management ───────────────────────────────────────────────────────

    private async Task AddPage()
    {
        if (_document is null || IsReadOnly) return;

        var page = new WireframePage
        {
            Name = $"{Loc["TmWireframe_Page"]} {_document.Pages.Count + 1}",
            Width = _activePage?.Width ?? 1280,
            Height = _activePage?.Height ?? 800,
        };

        _document.Pages.Add(page);
        await SwitchPage(page.Id);
        await OnDocumentChanged(_document);
    }

    private async Task DuplicatePage()
    {
        if (_document is null || _activePage is null || IsReadOnly) return;

        var json = System.Text.Json.JsonSerializer.Serialize(_activePage, WireframeJsonOptions.Default);
        var copy = System.Text.Json.JsonSerializer.Deserialize<WireframePage>(json, WireframeJsonOptions.Default)!;
        copy.Id = "p" + Guid.NewGuid().ToString("N")[..7];
        copy.Name = $"{_activePage.Name} ({Loc["TmWireframe_Copy"]})";

        _document.Pages.Add(copy);
        await SwitchPage(copy.Id);
        await OnDocumentChanged(_document);
    }

    private async Task DeletePage(string pageId)
    {
        if (_document is null || IsReadOnly) return;
        if (_document.Pages.Count <= 1) return;

        var page = _document.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null) return;

        _document.Pages.Remove(page);
        if (_pageCommandStacks.Remove(pageId, out var stack))
            stack.OnStackChanged -= OnStackChanged;

        // Switch to nearest page
        var newActive = _document.Pages.FirstOrDefault();
        if (newActive is not null)
            await SwitchPage(newActive.Id);

        await OnDocumentChanged(_document);
    }

    private async Task SwitchPage(string pageId)
    {
        if (_document is null || _activePage?.Id == pageId) return;

        // Clear selection and context menu before switching
        _selectedIds = [];
        _ctxMenuOpen = false;
        if (_canvas is not null)
            await _canvas.ClearSelection();

        _document.ActivePageId = pageId;
        _activePage = _document.ActivePage;
        _activeLayerId = _document.ActiveLayerId;

        // Get or create command stack for the new page
        if (_activePage is not null)
        {
            if (!_pageCommandStacks.TryGetValue(_activePage.Id, out var stack))
            {
                stack = new WireframeCommandStack();
                stack.OnStackChanged += OnStackChanged;
                _pageCommandStacks[_activePage.Id] = stack;
            }
            _commandStack = stack;
        }

        StateHasChanged();
        await OnDocumentChanged(_document);
    }

    private void StartRenamePage(string pageId)
    {
        if (IsReadOnly) return;
        _renamingPageId = pageId;
        StateHasChanged();
    }

    private async Task OnPageRenamed(string pageId, ChangeEventArgs e)
    {
        if (_document is null || IsReadOnly) return;
        var page = _document.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null) return;
        page.Name = e.Value?.ToString() ?? page.Name;
        _renamingPageId = null;
        await OnDocumentChanged(_document);
    }

    private void OnPageDragStart(string pageId)
    {
        if (IsReadOnly) return;
        _draggedPageId = pageId;
    }

    private void OnPageDrop(string targetPageId)
    {
        if (_document is null || IsReadOnly || _draggedPageId is null || _draggedPageId == targetPageId) return;

        var draggedIdx = _document.Pages.FindIndex(p => p.Id == _draggedPageId);
        var targetIdx = _document.Pages.FindIndex(p => p.Id == targetPageId);
        if (draggedIdx < 0 || targetIdx < 0) return;

        var page = _document.Pages[draggedIdx];
        _document.Pages.RemoveAt(draggedIdx);
        _document.Pages.Insert(targetIdx, page);

        _draggedPageId = null;
        _ = OnDocumentChanged(_document);
    }

    private void OnRenameInputKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            _renamingPageId = null;
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (_document is null) return;

        if (e.CtrlKey && e.Key == "PageUp")
        {
            var idx = _document.Pages.FindIndex(p => p.Id == _activePage?.Id);
            if (idx > 0)
                await SwitchPage(_document.Pages[idx - 1].Id);
        }
        else if (e.CtrlKey && e.Key == "PageDown")
        {
            var idx = _document.Pages.FindIndex(p => p.Id == _activePage?.Id);
            if (idx >= 0 && idx < _document.Pages.Count - 1)
                await SwitchPage(_document.Pages[idx + 1].Id);
        }
    }

}
