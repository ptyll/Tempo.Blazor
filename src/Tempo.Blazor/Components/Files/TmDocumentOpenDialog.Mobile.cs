using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.Components.Files;

// The mobile layout of the dialog (wireframe redesign plan ce19aa8c, phase 9): drill-down folder rows instead of the tree,
// the back button, two-line document rows, the per-row menu and the action bar. Every member here reuses the desktop state
// (_currentFolder, _tree, _selected, BeginRename / BeginDelete ...): the layout changes what is drawn, never what is returned.
public partial class TmDocumentOpenDialog
{
    /// <summary>One drill-down folder row of the mobile layout. <see cref="Key"/> is the folder path (or the Recent key).</summary>
    private sealed record MobileFolder(string Key, string Label, string Icon, bool IsRecent);

    /// <summary>
    /// The rows above the documents: the subfolders of the CURRENT folder, taken from the folder tree the dialog already holds
    /// (no provider member is added), and the opt-in Recent node at the library root. A search or the Recent node lists documents only.
    /// </summary>
    private IReadOnlyList<MobileFolder> MobileFolderRows()
    {
        if (_tree is null || _recent || !string.IsNullOrEmpty(_search))
        {
            return [];
        }

        // Cached per (tree instance, current folder, Recent opt-in): the markup asks twice per render, and the tree walk is not free on a big library.
        // The tree is replaced (never mutated) on every reload, so reference equality is the invalidation.
        if (ReferenceEquals(_folderRowsTree, _tree) && _folderRowsFolder == _currentFolder && _folderRowsRecent == ShowRecent && _folderRows is not null)
        {
            return _folderRows;
        }

        var rows = new List<MobileFolder>();
        if (ShowRecent && _currentFolder == "/")
        {
            rows.Add(new MobileFolder(RecentKey, Loc["TmDocumentOpenDialog_Recent"], IconNames.Clock, true));
        }

        var current = FindFolder(_tree, _currentFolder);
        if (current is not null)
        {
            rows.AddRange(current.Children.Select(child => new MobileFolder(child.Path, child.Name, IconNames.Folder, false)));
        }

        _folderRowsTree = _tree;
        _folderRowsFolder = _currentFolder;
        _folderRowsRecent = ShowRecent;
        return _folderRows = rows;
    }

    private IReadOnlyList<MobileFolder>? _folderRows;
    private DocumentLibraryFolder? _folderRowsTree;
    private string? _folderRowsFolder;
    private bool _folderRowsRecent;

    private static DocumentLibraryFolder? FindFolder(DocumentLibraryFolder folder, string path)
    {
        if (folder.Path == path)
        {
            return folder;
        }

        foreach (var child in folder.Children)
        {
            if (FindFolder(child, path) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The name of the place the drill-down is in (library root, Recent node or the current folder): what the polite status announces after a step.</summary>
    private string CurrentPlaceName => _recent ? Loc["TmDocumentOpenDialog_Recent"] : _currentFolder == "/" ? Loc["TmDocumentOpenDialog_RootFolder"] : _currentFolder[(_currentFolder.LastIndexOf('/') + 1)..];

    /// <summary>Whether the back button shows: anywhere but the library root with no search typed.</summary>
    private bool CanGoBack => _recent || _currentFolder != "/" || !string.IsNullOrEmpty(_search);

    private static string ParentOf(string path)
    {
        var index = path.LastIndexOf('/');
        return index <= 0 ? "/" : path[..index];
    }

    /// <summary>Taps a folder row: navigates into it and hands focus to the back button (the folder row that was tapped is gone).</summary>
    private async Task OpenFolderAsync(string key)
    {
        if (key == RecentKey)
        {
            await NavigateToRecentAsync();
        }
        else
        {
            await NavigateToAsync(key);
        }

        _pendingFocus = ("control", "back");
    }

    /// <summary>
    /// Steps ONE level up. Back during a search only clears the search and STAYS in the current folder (the folder the search was typed in);
    /// the next Back leaves the Recent node for the root, else goes to the parent folder. Focus returns to the folder
    /// row that was left (it is a child of the place we arrive in), or to the back button / search field when there is no such row.
    /// </summary>
    private async Task GoBackAsync()
    {
        var left = _recent ? RecentKey : _currentFolder;
        if (!string.IsNullOrEmpty(_search))
        {
            _search = string.Empty;
            _searchCts?.Cancel();
            await SafeReloadAsync(resetPaging: true);
            FocusControlOrFallback();
            return;
        }

        await NavigateToAsync(_recent ? "/" : ParentOf(_currentFolder));
        if (MobileFolderRows().Any(r => r.Key == left))
        {
            _pendingFocus = ("folder", left);
        }
        else
        {
            FocusControlOrFallback();
        }
    }

    /// <summary>After a step back the old focus target is gone: the back button while there is somewhere to go back to, else the search field.</summary>
    private void FocusControlOrFallback()
    {
        if (CanGoBack)
        {
            _pendingFocus = ("control", "back");
        }
        else if (CanSearch)
        {
            _pendingFocus = ("control", "search");
        }
        else
        {
            _pendingFocus = null;
        }
    }

    /// <summary>
    /// A double click / double tap on a row or card opens the document on desktop and tablet. On mobile it does NOT: a tap on a folder row
    /// lands the next tap on whatever row now sits at that spot, within the double-tap window, so a quick drill-down would open a document
    /// the user only meant to select. Mobile opens through the explicit Open button (or Enter on the focused row).
    /// </summary>
    private Task OnItemDoubleClickAsync(DocumentLibraryEntry entry) => Mobile ? Task.CompletedTask : ConfirmAsync(entry);

    /// <summary>The second line of a document row: the modification date, then the author when there is one.</summary>
    private static string RowMeta(DocumentLibraryEntry entry)
    {
        var date = entry.ModifiedAt.ToString("yyyy-MM-dd");
        return string.IsNullOrWhiteSpace(entry.Author) ? date : date + " · " + entry.Author;
    }

    /// <summary>The ⋯ of a row: the row becomes the selection, then the menu entry runs the same flow as the desktop buttons.</summary>
    private Task OnRowMenuSelectAsync(DocumentLibraryEntry entry, string value)
    {
        Select(entry);
        if (value == "rename")
        {
            BeginRenameMobile();
        }
        else if (value == "delete")
        {
            BeginDeleteMobile();
        }

        return Task.CompletedTask;
    }

    // The inline bars sit in the sticky head, away from the control that opened them (the footer tile, the row menu): focus moves INTO the
    // bar, so a keyboard / switch user lands in the field that appeared. A delete confirmation lands on its safe button (Cancel), never on
    // the destructive one. Desktop keeps its toolbar behaviour (nothing is focused automatically).
    private void BeginNewFolderMobile()
    {
        BeginNewFolder();
        _pendingFocus = ("control", "new-folder-input");
    }

    private void BeginRenameMobile()
    {
        BeginRename();
        if (_renaming)
        {
            _pendingFocus = ("control", "rename-input");
        }
    }

    private void BeginDeleteMobile()
    {
        BeginDelete();
        if (_confirmingDelete)
        {
            _pendingFocus = ("control", "delete-cancel");
        }
    }

    private void CancelNewFolder() => _creatingFolder = false;

    private void CancelRename()
    {
        _renaming = false;
        _renameError = false;
    }

    /// <summary>
    /// The footer action bar: New folder / Rename / Delete, each gated by the provider's capability. Rename and Delete are always
    /// present and disabled until a document is selected, like the desktop toolbar; New folder is off on the Recent node.
    /// </summary>
    private IReadOnlyList<TmActionItem> MobileActionItems()
    {
        var items = new List<TmActionItem>();
        if (CanCreateFolder)
        {
            items.Add(new TmActionItem
            {
                Id = "new-folder",
                Label = Loc["TmDocumentOpenDialog_NewFolder"],
                Icon = IconNames.FolderPlus,
                Disabled = _recent,
                OnClick = EventCallback.Factory.Create(this, BeginNewFolderMobile)
            });
        }

        if (CanRename)
        {
            items.Add(new TmActionItem
            {
                Id = "rename",
                Label = Loc["TmDocumentOpenDialog_Rename"],
                Icon = IconNames.Pencil,
                Disabled = _selected is null,
                OnClick = EventCallback.Factory.Create(this, BeginRenameMobile)
            });
        }

        if (CanDelete)
        {
            items.Add(new TmActionItem
            {
                Id = "delete",
                Label = Loc["TmDocumentOpenDialog_Delete"],
                Icon = IconNames.Trash,
                Danger = true,
                Disabled = _selected is null,
                OnClick = EventCallback.Factory.Create(this, BeginDeleteMobile)
            });
        }

        return items;
    }
}
