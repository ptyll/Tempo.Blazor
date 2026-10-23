using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// A Word-like "Open document" dialog over an <see cref="ITempoDocumentLibraryProvider"/>:
/// a collapsible folder tree, breadcrumb, searchable and sortable list/grid of documents, optional
/// folder/document management, and a link-or-copy choice presented as radio cards. Emits a
/// <see cref="DocumentOpenResult"/> on confirm.
/// </summary>
/// <remarks>
/// The folder tree follows the WAI-ARIA tree pattern (one roving tab stop; Up/Down move, Left/Right
/// collapse/expand or step to the parent/first child, Home/End jump, Enter/Space navigate). The list rows and
/// grid cards are one roving tab stop each with the arrow keys (the grid steps by its measured column count).
/// The dialog measures its own body and switches to a compact tree and a footer with the buttons on their own row
/// when the container is narrow; <see cref="LayoutMode"/> forces that for tests and hosts. Below 640 px (the mobile layout)
/// it becomes a fullscreen panel: no tree, the subfolders of the current folder as drill-down rows with a back button and the
/// breadcrumb, documents as two-line rows with a preview and a per-row "⋯" menu, and a sticky footer with the Link / Copy cards,
/// a <see cref="Tempo.Blazor.Components.Actions.TmMobileActionBar"/> (New folder / Rename / Delete) and the primary Open.
/// </remarks>
public partial class TmDocumentOpenDialog : ComponentBase, IAsyncDisposable
{
    private const string GuardModulePath = "./_content/Tempo.Blazor/Components/Files/TmDocumentOpenDialog.razor.js";

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ─────────────────────────────────────────────────────────────

    /// <summary>The library to browse. Required.</summary>
    [Parameter, EditorRequired] public ITempoDocumentLibraryProvider Provider { get; set; } = default!;

    /// <summary>Which kind of documents to list.</summary>
    [Parameter] public TempoDocumentKind Kind { get; set; }

    /// <summary>Whether the dialog is shown.</summary>
    [Parameter] public bool Open { get; set; }

    /// <summary>Raised when the dialog opens or closes.</summary>
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>Whether to offer the link-or-copy choice. Defaults to <c>true</c>.</summary>
    [Parameter] public bool ShowModeToggle { get; set; } = true;

    /// <summary>Initial insert mode. Defaults to <see cref="DocumentOpenMode.Link"/>.</summary>
    [Parameter] public DocumentOpenMode DefaultMode { get; set; } = DocumentOpenMode.Link;

    /// <summary>Raised with the chosen document when the user confirms.</summary>
    [Parameter] public EventCallback<DocumentOpenResult> OnSelected { get; set; }

    /// <summary>Raised when the user cancels.</summary>
    [Parameter] public EventCallback OnCancelled { get; set; }

    /// <summary>Page size for browsing. Defaults to 50.</summary>
    [Parameter] public int PageSize { get; set; } = 50;

    /// <summary>Search debounce in milliseconds. Defaults to 300; set 0 in tests.</summary>
    [Parameter] public int SearchDebounceMs { get; set; } = 300;

    /// <summary>
    /// Adds a virtual "Recent" node above the folder tree. Selecting it browses with
    /// <c>Scope = <see cref="DocumentLibraryScope.Library"/></c>, <c>SortField = Modified</c>, <c>Descending = true</c> and
    /// <c>Take = <see cref="PageSize"/></c> through the existing <see cref="ITempoDocumentLibraryProvider.BrowseAsync"/> — no
    /// provider member is added. The query is explicitly library-wide; a provider that does not support
    /// <see cref="DocumentLibraryScope.Library"/> may degrade it to the root folder (see
    /// <see cref="DocumentLibraryQuery.Scope"/>). Defaults to <c>false</c>.
    /// </summary>
    [Parameter] public bool ShowRecent { get; set; }

    /// <summary>
    /// The layout the dialog renders. <see cref="TmLayoutMode.Auto"/> (default) measures the dialog body: below 768 px it goes
    /// compact (narrow tree, footer buttons on their own row) and below 640 px it becomes the mobile layout — a fullscreen
    /// panel with drill-down folder rows, two-line document rows, a row menu and a sticky footer holding the action bar.
    /// <see cref="TmLayoutMode.Tablet"/> forces the compact layout, <see cref="TmLayoutMode.Mobile"/> the fullscreen mobile
    /// layout and <see cref="TmLayoutMode.Desktop"/> the wide one, without importing the observer script — so a test or a
    /// host renders a layout without a DOM. The returned <see cref="DocumentOpenResult"/> is the same in every layout.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    // ── State ──────────────────────────────────────────────────────────────────

    private bool _wasOpen;
    private bool _loading;
    private bool _error;

    private DocumentLibraryFolder? _tree;
    private string _currentFolder = "/";
    private readonly List<DocumentLibraryEntry> _entries = [];
    private int _totalCount;

    private Guid? _selectedId;
    private DocumentLibraryEntry? _selected;

    private DialogView _view = DialogView.List;
    private DocumentLibrarySortField _sortField = DocumentLibrarySortField.Name;
    private bool _descending;

    private string _search = string.Empty;
    private DocumentOpenMode _mode = DocumentOpenMode.Link;

    private bool _creatingFolder;
    private string _newFolderName = string.Empty;

    private bool _renaming;
    private string _renameName = string.Empty;
    private bool _renameError;

    private bool _confirmingDelete;

    // Virtual "Recent" node (ShowRecent): browses the whole provider by modification time. The sort the user had is
    // parked in _preRecentSort and restored when they leave the node.
    private const string RecentKey = "recent:";
    private bool _recent;
    private (DocumentLibrarySortField Field, bool Descending)? _preRecentSort;

    // Tree: a flat, level-annotated list (WAI-ARIA tree pattern). Folders are expanded unless listed here.
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private string? _treeFocusKey;
    private Guid? _itemFocusId;

    // Focus moved by the keyboard is applied after the next render, when the roving tabindex is in the DOM.
    private (string Kind, string Key)? _pendingFocus;

    // Per-instance so two dialogs on one page never share a radio group.
    private readonly string _treeId = $"dod-tree-{Guid.NewGuid():n}";
    private readonly string _modeGroup = $"dod-mode-{Guid.NewGuid():n}";

    // The dialog body is measured with its own thresholds: below 768 px of body width the tree narrows and the footer
    // buttons take their own row. The XLarge modal is at most 900 px (body 852 px), so a desktop / landscape-tablet viewport
    // keeps the wide layout and only a narrower window or container goes compact. 768 is a TmBreakpoints literal, so the
    // stylesheet's @container rules and this markup branch agree.
    private const int NarrowBelowPx = TmBreakpoints.Sm; // mirrored by the stylesheet's @container tm-layout (width < 640px)
    private const int CompactBelowPx = TmBreakpoints.Md; // below this the layout is Tablet (compact tree, footer on its own row)
    private static readonly TmLayoutBreakpoints ContainerBreakpoints = new(NarrowBelowPx, CompactBelowPx);

    // Hysteresis against a presentation flap. The dialog body is narrower than the fullscreen body at the same viewport: the dialog
    // loses the overlay padding, the modal margin and the body padding on both sides (2 * (16 + 16 + 24) = 112 px between 640 and 767 px),
    // the fullscreen mobile body has none of them (it is edge to edge). The viewport that measured "mobile" in the dialog therefore
    // measures 112 px WIDER once the panel is up and would resolve to "tablet" again, swapping the dialog back in, forever. While the
    // panel is up the observer classifies with a mobile threshold that many pixels wider, so one viewport stays mobile in both
    // presentations. 640 + 112 = 752 stays below the 768 tablet step, so the tablet band remains reachable.
    // SOURCE TOKENS (keep in step if any of them changes): .tm-modal-overlay padding var(--tm-space-4) = 16 px and .tm-modal margin
    // var(--tm-space-4) = 16 px below 768 px (both _modal.css), .tm-modal-body padding var(--tm-space-6) = 24 px (_modal.css); each is
    // paid on BOTH sides: 2 * (16 + 16 + 24) = 112. The bUnit hysteresis test pins 752 = 640 + 112 and the tablet band below 768.
    private const int FullscreenChromePx = 112;
    private static readonly TmLayoutBreakpoints FullscreenBreakpoints = new(NarrowBelowPx + FullscreenChromePx, CompactBelowPx);
    private TmLayoutObserver? _observer;

    private CancellationTokenSource? _searchCts;

    // Every reload takes a version before it awaits the provider; only the latest one may touch the entries, the total and
    // the selection, so an older (slower) folder / search / sort request can never overwrite a newer one.
    private int _reloadVersion;

    // Space scroll guard + roving focus (TmDocumentOpenDialog.razor.js): attached to the dialog root while it is open.
    private ElementReference _root;
    private LazyJsModule? _guardModule;
    private bool _guardAttached;
    private string? _guardRootId;
    private bool _liftAttached;
    private bool _liftRequested;
    private string? _liftRootId;

    private enum DialogView { List, Grid }

    private bool CanSearch => Provider.Capabilities.HasFlag(DocumentLibraryCapabilities.Search);
    private bool CanCreateFolder => Provider.Capabilities.HasFlag(DocumentLibraryCapabilities.CreateFolder);
    private bool CanRename => Provider.Capabilities.HasFlag(DocumentLibraryCapabilities.Rename);
    private bool CanDelete => Provider.Capabilities.HasFlag(DocumentLibraryCapabilities.Delete);

    /// <summary>The layout in force: the forced <see cref="LayoutMode"/>, or what the observer measured (Desktop before the first measurement).</summary>
    private TmLayoutMode Resolved => LayoutMode != TmLayoutMode.Auto
        ? LayoutMode
        : _observer?.Resolved ?? FirstFrameLayout;

    /// <summary>The viewport's layout (an app-level <c>TmLayoutObserver IsViewportScope</c>), when the host provides one.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] private TmLayoutContext? Viewport { get; set; }

    /// <summary>
    /// The layout of the first frame, before the dialog body is measured. A phone-width viewport (below 640 px) makes the body narrower
    /// still, so with a viewport scope the dialog opens straight into the mobile layout instead of flashing the wide dialog first;
    /// without one it is Desktop, like every other measuring component.
    /// </summary>
    private TmLayoutMode FirstFrameLayout => Viewport is { IsMobile: true } ? TmLayoutMode.Mobile : TmLayoutMode.Desktop;

    private bool Compact => Resolved != TmLayoutMode.Desktop;

    /// <summary>The mobile layout: a fullscreen panel, drill-down folders, two-line rows, the action bar in the footer.</summary>
    private bool Mobile => Resolved == TmLayoutMode.Mobile;

    private string LayoutCss => Resolved switch
    {
        TmLayoutMode.Mobile => "mobile",
        TmLayoutMode.Tablet => "tablet",
        _ => "desktop"
    };

    // The footer sits outside the observer (it is the modal's Footer slot), so a measured change re-renders the dialog.
    private Task OnLayoutResolved(TmLayoutMode mode)
    {
        StateHasChanged();
        return Task.CompletedTask;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        if (Open && !_wasOpen)
        {
            _wasOpen = true;
            ResetState();
            await LoadAsync();
        }
        else if (!Open)
        {
            _wasOpen = false;
        }
        else if (!ShowRecent && _recent)
        {
            // The node is gone: leave it, or no tree item would be active and the sort buttons stay disabled.
            LeaveRecent();
            await SafeReloadAsync(resetPaging: true);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Both registrations are keyed by _treeId (a per-instance constant), never by the element a later call would have to hold again:
        // Blazor may replace the dialog root when the presentation flips between the dialog and the fullscreen panel, and a detach that
        // handed over a different element than the attach saw would leave the listener (and the overlay padding) behind forever.
        // The root's ElementReference id tells a replaced root from the same one; the module moves the registration to the new root.
        if (Open && _root.Id is not null && (!_guardAttached || _guardRootId != _root.Id))
        {
            _guardAttached = true;
            _guardRootId = _root.Id;
            _guardModule ??= new LazyJsModule(JS, GuardModulePath);
            await _guardModule.TryInvokeVoidAsync("attachSpaceGuard", _root, _treeId);
        }
        else if (!Open && _guardAttached)
        {
            _guardAttached = false;
            _guardRootId = null;
            if (_guardModule is { IsImported: true })
            {
                await _guardModule.TryInvokeVoidAsync("detachSpaceGuard", _treeId);
            }
        }

        // Mobile: the fullscreen panel is fixed to the LAYOUT viewport, which an on-screen keyboard does not shrink (TmModal only
        // tracks the keyboard for a bottom sheet). The dialog's module pads the overlay by the hidden gap (the measure the
        // bottom sheets use) so the sticky footer - the action bar and the primary Open - stays on screen while a field
        // is being edited. Attached while the mobile panel is up; the module's answer decides whether it is (false = the overlay is not
        // in the DOM yet: the module retries on animation frames and the next render asks again, idempotently).
        if (Open && Mobile && _root.Id is not null && (!_liftAttached || _liftRootId != _root.Id))
        {
            _liftRootId = _root.Id;
            _guardModule ??= new LazyJsModule(JS, GuardModulePath);
            var (ok, attached) = await _guardModule.TryInvokeAsync<bool>("attachKeyboardLift", _root, _treeId);
            _liftAttached = ok && attached;
            _liftRequested = true;
            if (!_liftAttached)
            {
                _liftRootId = null;
            }
        }
        else if ((_liftAttached || _liftRequested) && !(Open && Mobile))
        {
            await DetachKeyboardLiftAsync();
        }

        if (Open && _pendingFocus is { } focus && _root.Id is not null)
        {
            _pendingFocus = null;
            _guardModule ??= new LazyJsModule(JS, GuardModulePath);
            await _guardModule.TryInvokeVoidAsync("focusRoving", _root, focus.Kind, focus.Key);
        }
    }

    // Releases every listener the lift added (window resize, visualViewport resize / scroll), cancels a pending retry and restores the overlay.
    private async Task DetachKeyboardLiftAsync()
    {
        _liftAttached = false;
        _liftRequested = false;
        _liftRootId = null;
        if (_guardModule is { IsImported: true })
        {
            await _guardModule.TryInvokeVoidAsync("detachKeyboardLift", _treeId);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        if (_guardModule is not null)
        {
            // The listeners live on window / visualViewport, which outlive this component: release them BEFORE the module reference goes.
            // TryInvokeVoidAsync swallows JSDisconnectedException (a torn-down circuit has nothing left to release).
            if (_liftAttached || _liftRequested)
            {
                await DetachKeyboardLiftAsync();
            }

            if (_guardAttached)
            {
                _guardAttached = false;
                await _guardModule.TryInvokeVoidAsync("detachSpaceGuard", _treeId);
            }

            try
            {
                await _guardModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
    private void ResetState()
    {
        _loading = false;
        _error = false;
        _tree = null;
        _currentFolder = "/";
        _entries.Clear();
        _totalCount = 0;
        _selectedId = null;
        _selected = null;
        _view = DialogView.List;
        _sortField = DocumentLibrarySortField.Name;
        _descending = false;
        _search = string.Empty;
        _mode = DefaultMode;
        _creatingFolder = false;
        _renaming = false;
        _renameError = false;
        _confirmingDelete = false;
        _recent = false;
        _preRecentSort = null;
        _collapsed.Clear();
        _treeFocusKey = null;
        _itemFocusId = null;
        _pendingFocus = null;
        _reloadVersion++; // an in-flight request from a previous open is stale
    }

    // ── Loading ──────────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        _loading = true;
        _error = false;
        StateHasChanged();
        try
        {
            _tree = await Provider.GetFolderTreeAsync(Kind);
            await ReloadEntriesAsync(resetPaging: true);
        }
        catch
        {
            _error = true;
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task ReloadEntriesAsync(bool resetPaging = true)
    {
        var version = ++_reloadVersion;
        // The offset is the count BEFORE the await: nothing else may change _entries while this request is the latest.
        var skip = resetPaging ? 0 : _entries.Count;
        if (resetPaging)
        {
            _entries.Clear();
            _itemFocusId = null;
        }

        var page = await Provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = Kind,
            // Recent and a search are library-wide queries: they state Scope = Library instead of leaning on a null
            // FolderPath (DOCLIB-QUERY-SCOPE). A provider without Library support degrades to the folder scope.
            FolderPath = LibraryWide ? null : _currentFolder,
            Scope = LibraryWide ? DocumentLibraryScope.Library : DocumentLibraryScope.Folder,
            Search = string.IsNullOrEmpty(_search) ? null : _search,
            SortField = _sortField,
            Descending = _descending,
            Skip = skip,
            Take = PageSize
        });

        if (version != _reloadVersion)
        {
            return; // superseded by a newer request: its result (and any failure) is not ours to apply
        }

        _entries.AddRange(page.Items);
        _totalCount = page.TotalCount;

        // Keep selection only if still present, and re-resolve it so a rename shows the new name in the Open result.
        if (_selectedId is { } id)
        {
            var current = _entries.FirstOrDefault(e => e.Id == id);
            if (current is null)
            {
                _selectedId = null;
                _selected = null;
            }
            else
            {
                _selected = current;
            }
        }
    }

    // The Recent node and an active search look at the whole library; everything else is the current folder.
    private bool LibraryWide => _recent || !string.IsNullOrEmpty(_search);

    private bool HasMore => _entries.Count < _totalCount;

    private async Task LoadMoreAsync() => await SafeReloadAsync(resetPaging: false);

    private async Task SafeReloadAsync(bool resetPaging)
    {
        // ReloadEntriesAsync takes its version synchronously, so this is the version of the request started below.
        var started = _reloadVersion + 1;
        try
        {
            await ReloadEntriesAsync(resetPaging);
        }
        catch
        {
            if (started == _reloadVersion)
            {
                _error = true; // a stale request's failure must not flag the dialog the newer request is serving
            }
        }
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    private async Task NavigateToAsync(string folderPath)
    {
        LeaveRecent();
        _currentFolder = folderPath;
        _search = string.Empty;
        _creatingFolder = false;
        _renaming = false;
        _confirmingDelete = false;
        await SafeReloadAsync(resetPaging: true);
    }

    private async Task NavigateToRecentAsync()
    {
        if (!_recent)
        {
            // The recency order is part of the node: remember the user's own sort so leaving restores it.
            _preRecentSort = (_sortField, _descending);
        }

        _recent = true;
        _sortField = DocumentLibrarySortField.Modified;
        _descending = true;
        _search = string.Empty;
        _creatingFolder = false;
        _renaming = false;
        _confirmingDelete = false;
        await SafeReloadAsync(resetPaging: true);
    }

    private void LeaveRecent()
    {
        if (!_recent)
        {
            return;
        }

        _recent = false;
        if (_preRecentSort is { } sort)
        {
            _sortField = sort.Field;
            _descending = sort.Descending;
        }

        _preRecentSort = null;
    }

    private readonly record struct Crumb(string Path, string Name, bool IsHome);

    private IReadOnlyList<Crumb> Breadcrumb()
    {
        // The first crumb is always the library root, shown as the home icon.
        var crumbs = new List<Crumb> { new("/", Loc["TmDocumentOpenDialog_RootFolder"], true) };
        if (_recent)
        {
            crumbs.Add(new Crumb(RecentKey, Loc["TmDocumentOpenDialog_Recent"], false));
            return crumbs;
        }

        if (_currentFolder == "/")
        {
            return crumbs;
        }

        var segments = _currentFolder.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var acc = string.Empty;
        foreach (var seg in segments)
        {
            acc += "/" + seg;
            crumbs.Add(new Crumb(acc, seg, false));
        }

        return crumbs;
    }

    // ── Folder tree (WAI-ARIA tree pattern) ─────────────────────────────────────────

    /// <summary>One visible tree row. <see cref="Key"/> is the folder path (or <see cref="RecentKey"/>).</summary>
    private sealed record TreeItem(
        string Key, string Label, string Icon, int Level, bool HasChildren, bool Expanded, bool IsRecent, string? ParentKey)
    {
        /// <summary>Position among its siblings (WAI-ARIA <c>aria-posinset</c>); set after the list is built.</summary>
        public int PosInSet { get; set; } = 1;

        /// <summary>Sibling count (<c>aria-setsize</c>); set after the list is built.</summary>
        public int SetSize { get; set; } = 1;

        /// <summary>A DOM-id-safe suffix for the label element the item is named by.</summary>
        public string LabelId => IsRecent ? "recent" : Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(Key));

        /// <summary>Depth below the first level, for the indentation.</summary>
        public int Depth => Level - 1;

        /// <summary>Only a folder with children shows a chevron.</summary>
        public bool Toggleable => HasChildren;
    }

    /// <summary>The visible rows: the optional Recent node, then the folder tree minus the children of collapsed folders.</summary>
    private List<TreeItem> VisibleTree()
    {
        var items = new List<TreeItem>();
        if (ShowRecent)
        {
            items.Add(new TreeItem(RecentKey, Loc["TmDocumentOpenDialog_Recent"], IconNames.Clock, 1, false, false, true, null));
        }

        if (_tree is not null)
        {
            Append(_tree, 1, null);
        }

        // aria-setsize / aria-posinset: counted per parent among the VISIBLE siblings.
        foreach (var group in items.GroupBy(i => i.ParentKey))
        {
            var siblings = group.ToList();
            for (var i = 0; i < siblings.Count; i++)
            {
                siblings[i].PosInSet = i + 1;
                siblings[i].SetSize = siblings.Count;
            }
        }

        return items;

        void Append(DocumentLibraryFolder folder, int level, string? parentKey)
        {
            var hasChildren = folder.Children.Count > 0;
            var expanded = hasChildren && !_collapsed.Contains(folder.Path);
            var icon = folder.Path == "/" ? IconNames.Folder : expanded ? IconNames.FolderOpen : IconNames.Folder;
            items.Add(new TreeItem(
                folder.Path,
                folder.Path == "/" ? Loc["TmDocumentOpenDialog_RootFolder"] : folder.Name,
                icon, level, hasChildren, expanded, false, parentKey));
            if (expanded)
            {
                foreach (var child in folder.Children)
                {
                    Append(child, level + 1, folder.Path);
                }
            }
        }
    }

    // The current place is the active row. When it sits inside a COLLAPSED folder its row is not rendered, so the nearest
    // collapsed ancestor stands in for it - aria-selected stays visible on the tree.
    private bool IsActiveTreeItem(TreeItem item)
    {
        if (item.IsRecent)
        {
            return _recent;
        }

        if (_recent)
        {
            return false;
        }

        return item.Key == _currentFolder
            || (item.HasChildren && !item.Expanded && IsAncestorPath(item.Key, _currentFolder));
    }

    private static bool IsAncestorPath(string ancestor, string path)
        => ancestor == "/"
            ? path != "/"
            : path.StartsWith(ancestor + "/", StringComparison.Ordinal);

    /// <summary>The ONE row that is in the tab order: the keyboard-focused one, else the current place, else the first row.</summary>
    private string TreeTabStop(List<TreeItem> items)
    {
        if (_treeFocusKey is { } focus && items.Any(i => i.Key == focus))
        {
            return focus;
        }

        var active = items.FirstOrDefault(IsActiveTreeItem);
        return (active ?? items.FirstOrDefault())?.Key ?? string.Empty;
    }

    private async Task ActivateTreeItemAsync(TreeItem item)
    {
        _treeFocusKey = item.Key;
        if (item.IsRecent)
        {
            await NavigateToRecentAsync();
        }
        else
        {
            await NavigateToAsync(item.Key);
        }
    }

    // The chevron is a mouse affordance: after a click DOM focus would sit on the (fenced) button and the arrow keys would do
    // nothing, so the focus goes back to the tree item.
    private void ToggleTreeItemByMouse(TreeItem item)
    {
        ToggleTreeItem(item);
        FocusTree(item.Key);
    }

    private void ToggleTreeItem(TreeItem item)
    {
        if (!item.HasChildren)
        {
            return;
        }

        if (!_collapsed.Remove(item.Key))
        {
            _collapsed.Add(item.Key);
            // The keyboard-focused row may be one of the rows that just disappeared: the collapsed node takes the focus stop.
            if (_treeFocusKey is { } focus && IsAncestorPath(item.Key, focus))
            {
                _treeFocusKey = item.Key;
            }
        }
    }

    // The tree row is a div[role=treeitem] — a non-native focusable — so Enter (keydown, auto-repeat ignored) and
    // Space (keyup) are emulated per docs/keyboard-activation-convention.md. The arrows are the WAI-ARIA tree pattern.
    private async Task OnTreeKeyDownAsync(KeyboardEventArgs e, string key)
    {
        var items = VisibleTree();
        var index = items.FindIndex(i => i.Key == key);
        if (index < 0)
        {
            return;
        }

        var item = items[index];
        switch (e.Key)
        {
            case "Enter" when !e.Repeat:
                await ActivateTreeItemAsync(item);
                break;
            case "ArrowDown":
                FocusTree(items[Math.Min(index + 1, items.Count - 1)].Key);
                break;
            case "ArrowUp":
                FocusTree(items[Math.Max(index - 1, 0)].Key);
                break;
            case "Home":
                FocusTree(items[0].Key);
                break;
            case "End":
                FocusTree(items[^1].Key);
                break;
            case "ArrowRight":
                if (item.HasChildren && !item.Expanded)
                {
                    ToggleTreeItem(item);
                    FocusTree(item.Key);
                }
                else if (item.HasChildren && index + 1 < items.Count)
                {
                    FocusTree(items[index + 1].Key);
                }

                break;
            case "ArrowLeft":
                if (item.HasChildren && item.Expanded)
                {
                    ToggleTreeItem(item);
                    FocusTree(item.Key);
                }
                else if (item.ParentKey is { } parent)
                {
                    FocusTree(parent);
                }

                break;
        }
    }

    private async Task OnTreeKeyUpAsync(KeyboardEventArgs e, TreeItem item)
    {
        if (e.Key == " ")
        {
            await ActivateTreeItemAsync(item);
        }
    }

    private void FocusTree(string key)
    {
        _treeFocusKey = key;
        _pendingFocus = ("tree", key);
    }

    // ── Search ─────────────────────────────────────────────────────────────────

    private async Task OnSearchInputAsync(ChangeEventArgs e)
    {
        _search = e.Value?.ToString() ?? string.Empty;
        // A search is its own library-wide query: it replaces the Recent node (its fixed order) instead of mixing with it.
        if (!string.IsNullOrEmpty(_search))
        {
            LeaveRecent();
        }

        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        try
        {
            if (SearchDebounceMs > 0)
            {
                await Task.Delay(SearchDebounceMs, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await SafeReloadAsync(resetPaging: true);
        StateHasChanged();
    }

    // ── Sorting ──────────────────────────────────────────────────────────────────

    private async Task SortByAsync(DocumentLibrarySortField field)
    {
        // The Recent node is "modified, newest first" by definition: the header buttons are disabled there.
        if (_recent)
        {
            return;
        }

        if (_sortField == field)
        {
            _descending = !_descending;
        }
        else
        {
            _sortField = field;
            _descending = false;
        }

        await SafeReloadAsync(resetPaging: true);
    }

    // ── Selection ──────────────────────────────────────────────────────────────

    // Rows (div role=row) and grid cards (div role=option) are non-native focusables, so the library
    // emulates activation (docs/keyboard-activation-convention.md): Enter opens on keydown (auto-repeat
    // ignored), Space selects on keyup — where a native button would click. The arrows / Home / End are one
    // roving tab stop: they move FOCUS (not the selection), the grid steps a row by its measured column count.
    private async Task OnItemKeyDownAsync(KeyboardEventArgs e, DocumentLibraryEntry entry, bool grid)
    {
        switch (e.Key)
        {
            case "Enter" when !e.Repeat:
                await ConfirmAsync(entry);
                return;
            case "ArrowDown":
                MoveItemFocus(entry, grid ? await GridColumnsAsync() : 1);
                return;
            case "ArrowUp":
                MoveItemFocus(entry, grid ? -await GridColumnsAsync() : -1);
                return;
            case "ArrowRight" when grid:
                MoveItemFocus(entry, 1);
                return;
            case "ArrowLeft" when grid:
                MoveItemFocus(entry, -1);
                return;
            case "Home" when _entries.Count > 0:
                FocusItem(_entries[0]);
                return;
            case "End" when _entries.Count > 0:
                FocusItem(_entries[^1]);
                return;
        }
    }

    private void MoveItemFocus(DocumentLibraryEntry from, int delta)
    {
        var index = _entries.FindIndex(x => x.Id == from.Id);
        var target = index + delta;
        // Past an edge (first / last row, or a grid column step beyond the last row) nothing happens - no clamping.
        if (index < 0 || target < 0 || target >= _entries.Count)
        {
            return;
        }

        FocusItem(_entries[target]);
    }

    private void FocusItem(DocumentLibraryEntry entry)
    {
        _itemFocusId = entry.Id;
        _pendingFocus = ("item", entry.Id.ToString());
    }

    /// <summary>How many cards share a grid row right now (the grid is auto-fill, so only the browser knows). 1 when it cannot be measured.</summary>
    private async Task<int> GridColumnsAsync()
    {
        if (_root.Id is null)
        {
            return 1;
        }

        _guardModule ??= new LazyJsModule(JS, GuardModulePath);
        var (ok, columns) = await _guardModule.TryInvokeAsync<int>("getGridColumns", _root);
        return ok && columns > 0 ? columns : 1;
    }

    /// <summary>The ONE row/card in the tab order: the keyboard-focused one, else the selected one, else the first.</summary>
    private Guid? ItemTabStop()
    {
        if (_itemFocusId is { } focus && _entries.Any(x => x.Id == focus))
        {
            return focus;
        }

        if (_selectedId is { } selected && _entries.Any(x => x.Id == selected))
        {
            return selected;
        }

        return _entries.Count > 0 ? _entries[0].Id : null;
    }

    private void OnItemKeyUp(KeyboardEventArgs e, DocumentLibraryEntry entry)
    {
        if (e.Key == " ")
        {
            Select(entry);
        }
    }

    /// <summary>The <c>aria-sort</c> value of a sortable column header.</summary>
    private string AriaSort(DocumentLibrarySortField field)
        => _sortField != field ? "none" : _descending ? "descending" : "ascending";

    private void Select(DocumentLibraryEntry entry)
    {
        _selectedId = entry.Id;
        _selected = entry;
        _itemFocusId = entry.Id;
        _renaming = false;
        _confirmingDelete = false;
        _renameError = false;
        _creatingFolder = false;
    }

    private async Task ConfirmAsync(DocumentLibraryEntry entry)
    {
        Select(entry);
        await ConfirmSelectionAsync();
    }

    private async Task ConfirmSelectionAsync()
    {
        if (_selected is null)
        {
            return;
        }

        await OnSelected.InvokeAsync(new DocumentOpenResult
        {
            DocumentId = _selected.Id,
            Kind = _selected.Kind,
            Mode = _mode,
            Name = _selected.Name
        });
        await CloseAsync();
    }

    private async Task CancelAsync()
    {
        await OnCancelled.InvokeAsync();
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        if (OpenChanged.HasDelegate)
        {
            await OpenChanged.InvokeAsync(false);
        }
    }

    private void SetView(DialogView view) => _view = view;

    private void SetMode(DocumentOpenMode mode) => _mode = mode;

    // ── New folder ───────────────────────────────────────────────────────────────

    private void BeginNewFolder()
    {
        _creatingFolder = true;
        _newFolderName = string.Empty;
    }

    private async Task ConfirmNewFolderAsync()
    {
        var name = _newFolderName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return;
        }

        try
        {
            await Provider.CreateFolderAsync(Kind, _currentFolder, name);
            _tree = await Provider.GetFolderTreeAsync(Kind);
            _creatingFolder = false;
        }
        catch
        {
            _error = true;
        }
    }

    // ── Rename ───────────────────────────────────────────────────────────────────

    private void BeginRename()
    {
        if (_selected is null)
        {
            return;
        }

        _creatingFolder = false;
        _confirmingDelete = false;
        _renaming = true;
        _renameError = false;
        _renameName = _selected.Name;
    }

    private async Task ConfirmRenameAsync()
    {
        if (_selected is null)
        {
            return;
        }

        var name = _renameName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            _renameError = true;
            return;
        }

        try
        {
            await Provider.RenameDocumentAsync(Kind, _selected.Id, name);
            _renaming = false;
            await SafeReloadAsync(resetPaging: true);
        }
        catch
        {
            _error = true;
        }
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    private void BeginDelete()
    {
        if (_selected is not null)
        {
            _creatingFolder = false;
            _renaming = false;
            _confirmingDelete = true;
        }
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_selected is null)
        {
            return;
        }

        try
        {
            await Provider.DeleteDocumentsAsync(Kind, [_selected.Id]);
            _confirmingDelete = false;
            _selectedId = null;
            _selected = null;
            await SafeReloadAsync(resetPaging: true);
        }
        catch
        {
            _error = true;
        }
    }

    private async Task RetryAsync() => await LoadAsync();
}
