using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Net;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Commands;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Page;

/// <summary>Simple DOMRect wrapper for JS interop.</summary>
public sealed class DomRect
{
    public double Top    { get; set; }
    public double Left   { get; set; }
    public double Width  { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// Renders a single Notion page: header (via TmNotionPageHeader), block list and comments area.
/// Loads blocks via <see cref="NotionEditorContext.BlockService"/> and manages block state.
/// </summary>
public partial class TmNotionPage : ComponentBase, IAsyncDisposable
{
    private const string AggregateConflictTestId = "notion-page-conflict";

    // ── DI ────────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>The page to display (required).</summary>
    [Parameter, EditorRequired]
    public INotionPage Page { get; set; } = default!;

    /// <summary>When true all editing interactions are disabled.</summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>Raised after the page metadata (title, icon, cover) is saved.</summary>
    [Parameter]
    public EventCallback<INotionPage> OnPageUpdated { get; set; }

    /// <summary>Raised when the user clicks a child-page link or mention.</summary>
    [Parameter]
    public EventCallback<string> OnNavigateToPage { get; set; }

    /// <summary>Effective permission for the current user on this page.</summary>
    [Parameter]
    public PageEffectivePermissionDto? EffectivePermission { get; set; }

    /// <summary>Raised after page restrictions are saved.</summary>
    [Parameter]
    public EventCallback OnPermissionsChanged { get; set; }

    /// <summary>
    /// Called when the user clicks "Create token" in the token dropdown.
    /// Arg = current search query (may be empty). Return the newly created
    /// token (Key, DisplayName, ColorClass) so the editor can insert it
    /// automatically, or <c>null</c> if the user cancelled.
    /// </summary>
    [Parameter]
    public Func<string, Task<(string Key, string DisplayName, string? ColorClass)?>?>? OnCreateTokenRequested { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private List<IPageBlock> _blocks          = [];

    /// <summary>Undo / redo history for this page. Structural edits go through it.</summary>
    private readonly NotionCommandStack _commands = new();
    private bool             _isLoadingBlocks;
    private string?          _loadBlocksError;
    private bool             _hasAggregateConflict;
    private bool             _resolvingAggregateConflict;
    private string?          _aggregateConflictError;
    private Guid?            _activeBlockId;
    private INotionPage?     _lastPage;

    private ElementReference _pageRef;
    private bool             _historyVisible;
    private bool             _pageInfoVisible;
    private bool             _shareVisible;
    private bool             _restrictionsVisible;
    private bool             _collabSubscribed;

    // ── Block comment panel state ─────────────────────────────────────────────

    private bool   _blockCommentVisible;
    private string _blockCommentBlockId = string.Empty;
    private double _blockCommentTop;
    private double _blockCommentLeft;
    private bool   _blockCommentStartInNewThreadMode;
    /// <summary>Per-block comment summary for margin-thread indicators and hover tooltip.</summary>
    public sealed record BlockCommentInfo(
        int Unresolved,
        int ResolvedUnread,
        bool HasUnreadActivity,
        string? LastAuthorName,
        string? LastAuthorAvatar,
        string? LastEntryText,
        DateTime? LastEntryTime,
        int ThreadCount);

    private readonly Dictionary<string, BlockCommentInfo> _blockCommentCounts = new();

    // ── Text comment panel state ──────────────────────────────────────────────

    private bool   _textCommentVisible;
    private string _textCommentId       = string.Empty;
    private string _textCommentBlockId  = string.Empty;
    private int    _textCommentStartOffset;
    private int    _textCommentEndOffset;
    private string _textCommentHighlight = string.Empty;
    private double _textCommentTop;
    private double _textCommentLeft;

    // ── Page comment panel state ──────────────────────────────────────────────

    private bool _pageCommentExpanded;

    // ── Page-level unresolved comment count (header badge) ────────────────────

    private int _pageUnresolvedCommentCount;

    // ── Slash menu state ─────────────────────────────────────────────────────

    private bool   _slashMenuVisible;
    private double _slashMenuTop;
    private double _slashMenuLeft;
    private string _slashBlockId = string.Empty;

    // ── Inline status picker state ───────────────────────────────────────────

    private bool              _statusPickerVisible;
    private double            _statusPickerTop;
    private double            _statusPickerLeft;
    private string?           _statusInitialLabel;
    private NotionStatusColor _statusInitialColor = NotionStatusColor.Gray;
    private bool              _statusEditingExistingChip;
    private string            _statusEditingBlockId = string.Empty;
    private int               _statusEditingChipIndex = -1;

    // ── AI menu state ───────────────────────────────────────────────────────

    private bool             _aiMenuVisible;
    private bool             _aiMenuPanel;
    private double           _aiMenuTop;
    private double           _aiMenuLeft;
    private NotionAiMenuMode _aiMenuMode = NotionAiMenuMode.Generate;
    private string           _aiSourceText = string.Empty;
    private string           _aiContextHtml = string.Empty;
    private string           _aiTargetBlockId = string.Empty;
    private bool             _aiReplaceSavedSelection;

    // ── Token dropdown state ──────────────────────────────────────────────────

    private bool   _tokenDropdownVisible;
    private double _tokenDropdownTop;
    private double _tokenDropdownLeft;
    private string _tokenBlockId    = string.Empty;
    private string? _tokenCurrentKey;          // key of chip being replaced (null = insert mode)
    private bool   _tokenIsEditMode;           // true when dropdown was opened by clicking an existing chip

    // ── Mention menu state ────────────────────────────────────────────────────

    private bool   _mentionMenuVisible;
    private double _mentionMenuTop;
    private double _mentionMenuLeft;
    private string _mentionBlockId  = string.Empty;
    private bool   _mentionPagesOnly;

    // ── Inline toolbar state ──────────────────────────────────────────────────

    private bool          _toolbarVisible;
    private double        _toolbarTop;
    private double        _toolbarLeft;
    private bool          _toolbarIsBold;
    private bool          _toolbarIsItalic;
    private bool          _toolbarIsUnderline;
    private bool          _toolbarIsStrikethrough;
    private bool          _toolbarIsCode;
    private string        _toolbarCurrentHref  = string.Empty;
    private TextAlignment _toolbarCurrentAlign  = TextAlignment.Left;
    private string        _toolbarBlockId       = string.Empty;
    private string        _toolbarSelectedText  = string.Empty;
    private DotNetObjectReference<TmNotionPage>? _toolbarDotNetRef;
    private bool          _toolbarWatcherReady;

    // ── Computed ─────────────────────────────────────────────────────────────

    private string _pageMods => string.Concat(
        Page.IsFullWidth ? " tm-notion-page--full-width"  : string.Empty,
        Page.IsSmallText ? " tm-notion-page--small-text"  : string.Empty,
        ReadOnly         ? " tm-notion-page--readonly"     : string.Empty
    ).TrimStart();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        Context.BlockCreated = HandleExternalBlockCreatedAsync;

        if (!ReferenceEquals(Page, _lastPage))
        {
            _lastPage = Page;

            if (!_collabSubscribed && Context.CollaborationSync is { } sync)
            {
                sync.RemoteBlockChanged += OnRemoteBlockChanged;
                _collabSubscribed = true;
            }

            await RefreshAsync();
        }
    }

    private async void OnRemoteBlockChanged(BlockChange _)
    {
        // Remote edit arrived — reload all blocks for the page (last-write-wins)
        await InvokeAsync(RefreshAsync);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !ReadOnly)
        {
            _toolbarDotNetRef = DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.initSelectionWatcher", _pageRef, _toolbarDotNetRef);
                _toolbarWatcherReady = true;

                // Ctrl+Z / Ctrl+Y drive the editor's own history. While the caret sits in a block the
                // user is still typing into, the browser keeps them for its character-level undo.
                await JS.InvokeVoidAsync("tmNotionEditor.initUndoRedo", _toolbarDotNetRef);
            }
            catch { /* SSR / test */ }
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.registerPageDotNetRef", _toolbarDotNetRef);
            }
            catch { /* SSR / test */ }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Reloads all blocks for the current page.</summary>
    public async Task RefreshAsync()
    {
        if (_isLoadingBlocks) return;

        _isLoadingBlocks = true;
        _loadBlocksError = null;
        StateHasChanged();

        try
        {
            var result = await Context.BlockService.GetBlocksAsync(Page.Id.ToString());
            _blocks = [.. result.OrderBy(b => b.Order)];
        }
        catch
        {
            SetBlockOperationError();
        }
        finally
        {
            _isLoadingBlocks = false;
            await LoadPageUnresolvedCommentCountAsync();
            StateHasChanged();
        }
    }

    private async Task HandleExternalBlockCreatedAsync(IPageBlock block)
    {
        if (block.PageId != Page.Id || _blocks.Any(existing => existing.Id == block.Id))
            return;

        var insertIdx = _blocks.FindIndex(existing => existing.Order > block.Order);
        if (insertIdx < 0)
            insertIdx = _blocks.Count;

        _blocks.Insert(Math.Clamp(insertIdx, 0, _blocks.Count), block);
        _activeBlockId = block.Id;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Loads the total unresolved comment count for the current page (header badge).</summary>
    private async Task LoadPageUnresolvedCommentCountAsync()
    {
        if (Context.CommentProvider is null) return;
        try
        {
            _pageUnresolvedCommentCount = await Context.CommentProvider.GetUnresolvedCommentsCountAsync(Page.Id.ToString());
        }
        catch
        {
            _pageUnresolvedCommentCount = 0;
        }
    }

    /// <summary>
    /// Creates a new block of the given type and inserts it after <paramref name="afterBlockId"/>.
    /// Passing <c>null</c> appends the block at the end. <paramref name="initialHtml"/> pre-fills
    /// the new block's HTML content (used when Enter splits a paragraph at the caret position).
    /// </summary>
    public async Task AddBlockAsync(BlockType type, string? afterBlockId = null, string? initialHtml = null)
    {
        if (ReadOnly) return;

        var afterBlock   = afterBlockId is null ? null : _blocks.FirstOrDefault(b => b.Id.ToString() == afterBlockId);
        var insertOrder  = afterBlock is null
            ? (_blocks.Count > 0 ? _blocks.Max(b => b.Order) + 1 : 0)
            : afterBlock.Order + 1;

        var newBlock = new PageBlock
        {
            Id      = Guid.NewGuid(),
            PageId  = Page.Id,
            Type    = type,
            Order   = insertOrder,
            Content = CreateDefaultContent(type, initialHtml)
        };

        try
        {
            var created   = await Context.BlockService.CreateBlockAsync(Page.Id.ToString(), newBlock, afterBlockId);
            var insertIdx = afterBlock is null
                ? _blocks.Count
                : _blocks.IndexOf(afterBlock) + 1;

            _blocks.Insert(Math.Clamp(insertIdx, 0, _blocks.Count), created);
            _activeBlockId = created.Id;
            StateHasChanged();
        }
        catch
        {
            SetBlockOperationError();
            StateHasChanged();
        }
    }

    /// <summary>Deletes the block with the given ID, leaving the caret at the end of its predecessor.</summary>
    public async Task DeleteBlockAsync(string blockId)
    {
        if (ReadOnly) return;

        var index = _blocks.FindIndex(b => b.Id.ToString() == blockId);
        if (index < 0) return;

        var block    = _blocks[index];
        var previous = index > 0 ? _blocks[index - 1] : null;

        try
        {
            // The subtree has to be captured before the delete cascades it away, or undo has
            // nothing to put back.
            var descendants = await CollectDescendantsAsync(block);

            await _commands.PushAsync(new DeleteBlockCommand(
                Context.BlockService, _blocks, Page.Id.ToString(), block, descendants));

            if (_activeBlockId == block.Id) _activeBlockId = previous?.Id;
            StateHasChanged();

            if (previous is not null)
            {
                // Backspace on an empty block should feel like deleting a character: the caret
                // continues where the text above ends.
                await PlaceCaretAtEndAsync(previous);
            }
        }
        catch
        {
            SetBlockOperationError();
            StateHasChanged();
        }
    }

    /// <summary>
    /// Folds a block into its predecessor: the predecessor absorbs <paramref name="html"/> at its
    /// end, this block is deleted, and the caret lands on the seam between the two former blocks.
    /// A block with no predecessor stays where it is.
    /// </summary>
    public async Task MergeBlockIntoPreviousAsync(string blockId, string html)
    {
        if (ReadOnly) return;

        var index = _blocks.FindIndex(b => b.Id.ToString() == blockId);
        if (index <= 0) return;

        var previous = _blocks[index - 1];
        if (previous.Content is not ITextBlockContent previousText) return;

        var seam   = NotionBlockMerger.CaretOffsetForSeam(previousText.Html);
        var merged = NotionBlockContentFactory.WithHtml(previous, NotionBlockMerger.Join(previousText.Html, html));

        try
        {
            await Context.BlockService.UpdateBlockAndDeleteAsync(merged, blockId);
        }
        catch
        {
            SetBlockOperationError();
            StateHasChanged();
            return;
        }

        _blocks.RemoveAt(index);
        _blocks[index - 1] = merged;
        _blocks            = [.. _blocks]; // new reference — cascading consumers (ToC) see the change
        _activeBlockId     = merged.Id;
        StateHasChanged();

        await RestoreCaretAsync(merged.Id.ToString(), seam);
    }

    private async Task PlaceCaretAtEndAsync(IPageBlock block) =>
        await RestoreCaretAsync(
            block.Id.ToString(),
            NotionBlockMerger.CaretOffsetForSeam(NotionBlockContentFactory.HtmlOf(block.Content)));

    /// <summary>
    /// Reorders blocks using an optimistic local update, then persists via the provider.
    /// Rolls back on failure.
    /// </summary>
    public async Task ReorderBlocksAsync(int sourceIndex, int targetIndex)
    {
        if (ReadOnly || sourceIndex < 0 || sourceIndex >= _blocks.Count) return;

        var block            = _blocks[sourceIndex];
        var normalizedTarget = Math.Clamp(
            sourceIndex < targetIndex ? targetIndex - 1 : targetIndex,
            0, _blocks.Count - 1);

        _blocks.RemoveAt(sourceIndex);
        _blocks.Insert(normalizedTarget, block);
        RenumberLocalOrder();
        StateHasChanged();

        try
        {
            await Context.BlockService.ReorderBlocksAsync(
                Page.Id.ToString(),
                _blocks.Select(b => b.Id.ToString()));
        }
        catch
        {
            await RefreshAsync();
        }
    }

    /// <summary>Sets the currently focused block. Called by child block components.</summary>
    public void SetActiveBlock(Guid? blockId)
    {
        if (_activeBlockId == blockId) return;
        _activeBlockId = blockId;
        StateHasChanged();
    }

    /// <summary>Whether an editor-level undo step is available.</summary>
    public bool CanUndo => _commands.CanUndo;

    /// <summary>Whether an editor-level redo step is available.</summary>
    public bool CanRedo => _commands.CanRedo;

    /// <summary>Undoes the last structural edit and re-renders the page.</summary>
    [JSInvokable]
    public async Task UndoAsync()
    {
        if (ReadOnly || !_commands.CanUndo) return;

        await _commands.UndoAsync();
        await RefreshAsync();
    }

    /// <summary>Redoes the last undone structural edit and re-renders the page.</summary>
    [JSInvokable]
    public async Task RedoAsync()
    {
        if (ReadOnly || !_commands.CanRedo) return;

        await _commands.RedoAsync();
        await RefreshAsync();
    }

    /// <summary>Every block nested under <paramref name="block"/>, deepest last.</summary>
    private async Task<List<IPageBlock>> CollectDescendantsAsync(IPageBlock block)
    {
        var result = new List<IPageBlock>();
        var frontier = new Queue<IPageBlock>();
        frontier.Enqueue(block);

        while (frontier.Count > 0)
        {
            var parent = frontier.Dequeue();
            IEnumerable<IPageBlock> children;
            try { children = await Context.BlockService.GetChildBlocksAsync(parent.Id.ToString()); }
            catch { continue; }

            foreach (var child in children)
            {
                result.Add(child);
                frontier.Enqueue(child);
            }
        }

        return result;
    }

    /// <summary>Returns the current ordered list of blocks (read-only view).</summary>
    public IReadOnlyList<IPageBlock> Blocks => _blocks;

    /// <summary>The block that currently holds the caret, or <c>null</c> when the page is not focused.</summary>
    public Guid? ActiveBlockId => _activeBlockId;

    // ── Page settings handlers ────────────────────────────────────────────────

    private async Task OnPageSettingsUpdated(INotionPage updated)
    {
        await OnPageUpdated.InvokeAsync(updated);
        StateHasChanged();
    }

    private async Task HandlePageDeletedAsync()
    {
        await OnNavigateToPage.InvokeAsync(string.Empty);
    }

    private Task HandlePageHistoryRequestedAsync()
    {
        _historyVisible = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandlePageInfoRequestedAsync()
    {
        _pageInfoVisible = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandlePageInfoVisibleChangedAsync(bool visible)
    {
        _pageInfoVisible = visible;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandleShareRequestedAsync()
    {
        _shareVisible = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandleShareVisibleChangedAsync(bool visible)
    {
        _shareVisible = visible;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandleRestrictionsRequestedAsync()
    {
        _restrictionsVisible = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandleRestrictionsVisibleChangedAsync(bool visible)
    {
        _restrictionsVisible = visible;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task HandleRestrictionsSavedAsync()
    {
        await OnPermissionsChanged.InvokeAsync();
        await RefreshAsync();
    }

    private Task HandleAISummarizeRequestedAsync()
    {
        OpenAIPagePanel(NotionAiMenuMode.Summarize);
        return Task.CompletedTask;
    }

    private Task HandleAIAskPageRequestedAsync()
    {
        OpenAIPagePanel(NotionAiMenuMode.Ask);
        return Task.CompletedTask;
    }

    private async Task HandleNavigateToImportedPageAsync(string pageId)
    {
        await OnNavigateToPage.InvokeAsync(pageId);
    }

    private async Task HandleHistoryRestoredAsync(string pageId)
    {
        _historyVisible = false;
        await RefreshAsync();
    }

    // ── Title enter handler ───────────────────────────────────────────────────

    private async Task HandleTitleEnterPressedAsync() =>
        await AddBlockAsync(BlockType.Paragraph);

    // ── Block list event handlers ─────────────────────────────────────────────

    private async Task HandleBlockReorderAsync((int source, int target) args) =>
        await ReorderBlocksAsync(args.source, args.target);

    private async Task HandleExternalBlockDroppedAsync(MoveNotionBlockRequest request)
    {
        if (ReadOnly) return;

        try
        {
            await Context.BlockService.MoveBlockAsync(request);
            var result = await Context.BlockService.GetBlocksAsync(Page.Id.ToString());
            _blocks = [.. result.OrderBy(b => b.Order)];
            StateHasChanged();
        }
        catch
        {
            await RefreshAsync();
        }
    }

    private Task HandleExternalBlockRemovedAsync(string blockId)
    {
        var block = _blocks.FirstOrDefault(b => b.Id.ToString() == blockId);
        if (block is not null)
        {
            _blocks.Remove(block);
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    private Task HandleBlockFocusedAsync(string blockId)
    {
        if (Guid.TryParse(blockId, out var id)) SetActiveBlock(id);
        return Task.CompletedTask;
    }

    private Task HandleMoveFocusAsync((string BlockId, int Direction) request)
    {
        if (!Guid.TryParse(request.BlockId, out var id) || request.Direction == 0)
            return Task.CompletedTask;

        var index = _blocks.FindIndex(block => block.Id == id);
        if (index < 0)
            return Task.CompletedTask;

        var targetIndex = Math.Clamp(index + request.Direction, 0, _blocks.Count - 1);
        if (targetIndex == index)
            return Task.CompletedTask;

        SetActiveBlock(_blocks[targetIndex].Id);
        return Task.CompletedTask;
    }

    private async Task HandleBlockDeletedAsync(string blockId) =>
        await DeleteBlockAsync(blockId);

    private Task HandleBlockUpdatedAsync(IPageBlock updated)
    {
        var idx = _blocks.FindIndex(b => b.Id == updated.Id);
        if (idx < 0) return Task.CompletedTask;
        _blocks[idx] = updated;
        _blocks = [.._blocks]; // new reference — CascadingValue consumers (ToC) detect the change
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the block's live contenteditable value and caret position before a conversion.
    /// Text typed since the last blur lives only in the DOM, so converting from the stored
    /// content would silently discard it.
    /// </summary>
    private async Task<(string? Html, int? CaretOffset)> CaptureLiveEditsAsync(string blockId)
    {
        try
        {
            var html = await JS.InvokeAsync<string?>("tmNotionEditor.getEditableHtml", blockId);
            var caret = await JS.InvokeAsync<int?>("tmNotionEditor.getCaretOffset", blockId);
            return (html, caret);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Restores the caret to the offset it had before the conversion re-rendered the block.</summary>
    private async Task RestoreCaretAsync(string blockId, int? caretOffset)
    {
        if (caretOffset is null) return;

        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.setCaretOffset", blockId, caretOffset.Value);
        }
        catch
        {
            // The converted block may have no editable (table, divider) — nothing to restore.
        }
    }

    /// <summary>Pulls the block's children back from the provider, e.g. the rows a Table conversion created.</summary>
    private async Task SyncConvertedChildrenAsync(IPageBlock converted)
    {
        if (!CanHoldChildBlocks(converted.Type)) return;

        try
        {
            var children = await Context.BlockService.GetChildBlocksAsync(converted.Id.ToString());
            _blocks.RemoveAll(block => block.ParentBlockId == converted.Id);
            _blocks.AddRange(children);
        }
        catch
        {
            // Leave the client list as-is; the block renders its own children on next load.
        }
    }

    private static bool CanHoldChildBlocks(BlockType type) => type
        is BlockType.Table
        or BlockType.Toggle
        or BlockType.ColumnList
        or BlockType.Column;

    private async Task HandleConvertBlockAsync((string blockId, BlockType newType) args)
    {
        if (ReadOnly) return;

        var (liveHtml, caretOffset) = await CaptureLiveEditsAsync(args.blockId);

        var source = _blocks.FirstOrDefault(block => block.Id.ToString() == args.blockId);

        try
        {
            var converted = await Context.BlockService.ConvertBlockTypeAsync(args.blockId, args.newType, liveHtml);
            var idx = _blocks.FindIndex(b => b.Id == converted.Id);
            if (idx >= 0) _blocks[idx] = converted;

            // Recorded, not executed: the provider already did the conversion. Undo puts the old
            // type and the old content back, so a heading demoted by mistake keeps its text.
            if (source is not null)
            {
                _commands.Record(new ConvertBlockCommand(
                    Context.BlockService, _blocks, converted.Id,
                    source.Type, source.Content, converted.Type, converted.Content));
            }

            await SyncConvertedChildrenAsync(converted);

            Context.RaiseBlockConverted(converted);
            _activeBlockId = converted.Id;
            _blocks = [.._blocks];
            StateHasChanged();

            await RestoreCaretAsync(converted.Id.ToString(), caretOffset);
        }
        catch
        {
            SetBlockOperationError();
            StateHasChanged();
        }
    }

    private async Task HandleAddBlockAfterAsync((string AfterBlockId, BlockType Type, string? InitialHtml) args) =>
        await AddBlockAsync(args.Type, args.AfterBlockId, args.InitialHtml);

    private async Task HandleInsertTemplateBlocksAfterAsync((string AfterBlockId, IReadOnlyList<IPageBlock> Blocks) args)
    {
        if (ReadOnly || args.Blocks.Count == 0) return;

        var afterBlock = _blocks.FirstOrDefault(b => b.Id.ToString() == args.AfterBlockId);
        var baseOrder  = afterBlock?.Order ?? (_blocks.Count > 0 ? _blocks.Max(b => b.Order) : 0);

        // Every inserted block gets a fresh id, so a parent's children have to be re-pointed at it.
        // A pasted table arrives as a Table plus its TableRow children; without the remap the table
        // renders empty and the orphaned rows fall back to paragraphs.
        var idMap = args.Blocks.ToDictionary(src => src.Id, _ => Guid.NewGuid());

        var newBlocks = args.Blocks.Select((src, i) => (IPageBlock)new PageBlock
        {
            Id            = idMap[src.Id],
            PageId        = Page.Id,
            ParentBlockId = src.ParentBlockId is { } parent && idMap.TryGetValue(parent, out var mapped)
                ? mapped
                : null,
            Type          = src.Type,
            Order         = src.ParentBlockId is null ? baseOrder + i + 1 : src.Order,
            Content       = src.Content,
            CreatedAt     = DateTime.UtcNow,
            LastEditedAt  = DateTime.UtcNow
        }).ToList();

        try
        {
            if (Context.AggregateSession is null)
            {
                _loadBlocksError = Loc["TmNotionPage_AggregateProviderRequired"];
                StateHasChanged();
                return;
            }

            var afterId = Guid.TryParse(args.AfterBlockId, out var parsedAfterId)
                ? parsedAfterId
                : (Guid?)null;
            var result = await Context.AggregateSession.ApplyAsync(snapshot =>
                NotionCanonicalBlockBridge.InsertBlocks(snapshot, newBlocks, afterId));
            if (!result.Success && !result.Conflict)
            {
                _loadBlocksError = string.Join(
                    Environment.NewLine,
                    result.Issues.Select(issue => issue.Message));
                StateHasChanged();
                return;
            }

            var insertIdx = afterBlock is null ? _blocks.Count : _blocks.IndexOf(afterBlock) + 1;

            // _blocks holds the page's top level only; a container fetches its own children.
            foreach (var b in newBlocks.Where(b => b.ParentBlockId is null).OrderBy(b => b.Order))
                _blocks.Insert(Math.Clamp(insertIdx++, 0, _blocks.Count), b);
            _hasAggregateConflict = result.Conflict;
            _aggregateConflictError = null;
            _loadBlocksError = null;
            StateHasChanged();
        }
        catch
        {
            SetBlockOperationError();
            StateHasChanged();
        }
    }

    private async Task ReloadAggregateConflictAsync()
    {
        if (Context.AggregateSession is null)
            return;

        _resolvingAggregateConflict = true;
        _aggregateConflictError = null;
        try
        {
            var result = await Context.AggregateSession.ReloadAsync();
            if (!result.Success)
            {
                _aggregateConflictError = FormatAggregateIssues(result.Issues);
                return;
            }

            _hasAggregateConflict = false;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _aggregateConflictError = ex.Message;
        }
        finally
        {
            _resolvingAggregateConflict = false;
            StateHasChanged();
        }
    }

    private async Task ReapplyAggregateConflictAsync()
    {
        if (Context.AggregateSession is null)
            return;

        _resolvingAggregateConflict = true;
        _aggregateConflictError = null;
        try
        {
            var result = await Context.AggregateSession.ReapplyAsync();
            _hasAggregateConflict = Context.AggregateSession.HasPendingConflict;
            if (!result.Success)
            {
                _aggregateConflictError = FormatAggregateIssues(result.Issues);
            }
        }
        catch (Exception ex)
        {
            _aggregateConflictError = ex.Message;
        }
        finally
        {
            _resolvingAggregateConflict = false;
            StateHasChanged();
        }
    }

    private static string FormatAggregateIssues(IEnumerable<NotionAggregateIssue> issues)
        => string.Join(Environment.NewLine, issues.Select(issue => issue.Message));

    private void SetBlockOperationError()
    {
        _hasAggregateConflict = Context.AggregateSession?.HasPendingConflict == true;
        _loadBlocksError = _hasAggregateConflict
            ? null
            : Loc["TmNotionPage_BlocksLoadError"];
    }

    private async Task HandleAddBlockAtEndAsync() =>
        await AddBlockAsync(BlockType.Paragraph);

    private async Task HandleBlockDuplicatedAsync(IPageBlock source) =>
        await DuplicateBlockAsync(source);

    // ── Comment handlers ──────────────────────────────────────────────────────

    private async Task HandleBlockCommentAsync(string blockId)
    {
        _blockCommentBlockId = blockId;
        _blockCommentTop     = 150;
        _blockCommentLeft    = 300;
        _blockCommentStartInNewThreadMode = false;
        _blockCommentVisible = true;
        StateHasChanged();

        // Mark threads as read for current user
        if (Context.CommentProvider is not null)
        {
            try
            {
                var comments = await Context.CommentProvider.GetBlockCommentsAsync(blockId);
                foreach (var c in comments)
                    await Context.CommentProvider.MarkThreadAsReadAsync(c.Id, "demo");
                await HandleBlockCommentCountChangedAsync(blockId);
            }
            catch { }
        }

        // Try to position near the block
        try
        {
            var rect = await JS.InvokeAsync<DomRect>("tmNotionEditor.getBlockBoundingRect", blockId);
            if (rect is not null)
            {
                _blockCommentTop  = rect.Top;
                _blockCommentLeft = rect.Left + rect.Width + 8;
                StateHasChanged();
            }
        }
        catch { }

        // Pre-load comment counts for margin thread indicator (resolved = resolved-but-unread)
        if (Context.CommentProvider is not null)
        {
            try
            {
                var comments = await Context.CommentProvider.GetBlockCommentsAsync(blockId);
                _blockCommentCounts[blockId] = ComputeBlockCommentInfo(comments);
                StateHasChanged();
            }
            catch { }
        }
    }

    private Task HandleBlockCommentClosedAsync()
    {
        _blockCommentVisible = false;
        _blockCommentBlockId = string.Empty;
        _blockCommentStartInNewThreadMode = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task HandleBlockCommentCountChangedAsync(string blockId)
    {
        if (Context.CommentProvider is null || string.IsNullOrEmpty(blockId)) return;
        try
        {
            var comments = await Context.CommentProvider.GetBlockCommentsAsync(blockId);
            var info = ComputeBlockCommentInfo(comments);
            if (info.Unresolved > 0 || info.ResolvedUnread > 0)
                _blockCommentCounts[blockId] = info;
            else
                _blockCommentCounts.Remove(blockId);
            await LoadPageUnresolvedCommentCountAsync();
            StateHasChanged();
        }
        catch { }
    }

    private static BlockCommentInfo ComputeBlockCommentInfo(IEnumerable<TmCommentThread> comments)
    {
        var list = comments.ToList();
        var unresolved   = list.Count(c => !c.IsResolved());
        var resolvedUnread = list.Count(c => c.IsResolved() && !c.ReadByUserIds.Contains("demo"));

        // Find the latest entry across all threads for tooltip data
        TmCommentEntry? latest = null;
        foreach (var c in list)
        {
            foreach (var e in c.Entries)
            {
                if (latest is null || e.CreatedAt > latest.CreatedAt)
                    latest = e;
            }
        }

        var text = latest?.HtmlContent();
        if (!string.IsNullOrEmpty(text))
        {
            // Strip HTML tags for tooltip preview
            text = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty);
            if (text.Length > 120)
                text = text[..120] + "…";
        }

        var hasUnread = list.Any(c => c.LastActivityAt().HasValue && !c.ReadByUserIds.Contains("demo"));

        return new BlockCommentInfo(
            unresolved,
            resolvedUnread,
            hasUnread,
            latest?.AuthorDisplayName(),
            latest?.AuthorAvatarUrl(),
            text,
            latest?.CreatedAt.UtcDateTime,
            list.Count);
    }

    private async Task HandleBlockNewThreadAsync(string blockId)
    {
        _blockCommentBlockId = blockId;
        _blockCommentTop     = 150;
        _blockCommentLeft    = 300;
        _blockCommentStartInNewThreadMode = true;
        _blockCommentVisible = true;
        StateHasChanged();

        // Mark existing threads as read for current user
        if (Context.CommentProvider is not null)
        {
            try
            {
                var comments = await Context.CommentProvider.GetBlockCommentsAsync(blockId);
                foreach (var c in comments)
                    await Context.CommentProvider.MarkThreadAsReadAsync(c.Id, "demo");
                await HandleBlockCommentCountChangedAsync(blockId);
            }
            catch { }
        }

        // Try to position near the block
        try
        {
            var rect = await JS.InvokeAsync<DomRect>("tmNotionEditor.getBlockBoundingRect", blockId);
            if (rect is not null)
            {
                _blockCommentTop  = rect.Top;
                _blockCommentLeft = rect.Left + rect.Width + 8;
                StateHasChanged();
            }
        }
        catch { }

        // Pre-load comment counts
        if (Context.CommentProvider is not null)
        {
            try
            {
                var comments = await Context.CommentProvider.GetBlockCommentsAsync(blockId);
                _blockCommentCounts[blockId] = ComputeBlockCommentInfo(comments);
                StateHasChanged();
            }
            catch { }
        }
    }

    private Task HandleTextCommentAsync(string commentId)
    {
        _textCommentId       = commentId;
        _textCommentBlockId  = _toolbarBlockId;
        _textCommentTop      = _toolbarTop;
        _textCommentLeft     = _toolbarLeft + 40;
        _textCommentVisible  = true;
        _toolbarVisible      = false; // hide toolbar when comment panel opens
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandleTextCommentClosedAsync()
    {
        _textCommentVisible     = false;
        _textCommentId          = string.Empty;
        _textCommentBlockId     = string.Empty;
        _textCommentStartOffset = 0;
        _textCommentEndOffset   = 0;
        _textCommentHighlight   = string.Empty;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task HandleTextCommentResolvedAsync()
    {
        _textCommentVisible     = false;
        _textCommentId          = string.Empty;
        _textCommentBlockId     = string.Empty;
        _textCommentStartOffset = 0;
        _textCommentEndOffset   = 0;
        _textCommentHighlight   = string.Empty;
        await LoadPageUnresolvedCommentCountAsync();
        StateHasChanged();
    }

    private Task HandlePageCommentExpandedChangedAsync(bool expanded)
    {
        _pageCommentExpanded = expanded;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Scrolls the page to the first block with an unresolved comment (header badge click).</summary>
    private async Task HandleCommentBadgeClickedAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.scrollToFirstUnresolvedComment");
        }
        catch { /* SSR / test */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates an identical copy of <paramref name="source"/> and inserts it after it.</summary>
    public async Task DuplicateBlockAsync(IPageBlock source)
    {
        if (ReadOnly) return;

        var duplicate = new PageBlock
        {
            Id            = Guid.NewGuid(),
            PageId        = source.PageId,
            ParentBlockId = source.ParentBlockId,
            Type          = source.Type,
            Order         = source.Order + 1,
            Content       = source.Content,
            CreatedAt     = DateTime.UtcNow,
            LastEditedAt  = DateTime.UtcNow
        };

        try
        {
            var created  = await Context.BlockService.CreateBlockAsync(
                source.PageId.ToString(), duplicate, source.Id.ToString());
            var srcIdx   = _blocks.FindIndex(b => b.Id == source.Id);
            var insertAt = srcIdx >= 0 ? srcIdx + 1 : _blocks.Count;
            _blocks.Insert(Math.Clamp(insertAt, 0, _blocks.Count), created);
            _activeBlockId = created.Id;
            StateHasChanged();
        }
        catch
        {
            SetBlockOperationError();
            StateHasChanged();
        }
    }

    // ── Click / keyboard on content area ─────────────────────────────────────

    private async Task OnContentAreaClickAsync()
    {
        if (!ReadOnly && _blocks.Count == 0)
            await AddBlockAsync(BlockType.Paragraph);
    }

    private async Task OnEmptyHintClickAsync()
    {
        if (!ReadOnly) await AddBlockAsync(BlockType.Paragraph);
    }

    private async Task OnEmptyHintKeyDownAsync(KeyboardEventArgs e)
    {
        if (!ReadOnly && (e.Key == "Enter" || e.Key == " "))
            await AddBlockAsync(BlockType.Paragraph);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RenumberLocalOrder()
    {
        for (var i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i] is PageBlock pb) pb.Order = i;
        }
    }

    private static IBlockContent CreateDefaultContent(BlockType type, string? initialHtml = null) => type switch
    {
        BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3
            => new HeadingBlockContent
            {
                Html  = initialHtml,
                Level = type switch { BlockType.Heading1 => 1, BlockType.Heading2 => 2, _ => 3 }
            },
        BlockType.Quote        => new TextBlockContent  { Html = initialHtml },
        BlockType.Callout      => new CalloutBlockContent { IconEmoji = "💡", Html = initialHtml },
        BlockType.Code         => new CodeBlockContent(),
        BlockType.Equation     => new EquationBlockContent { Expression = string.Empty },
        BlockType.Divider      => new DividerBlockContent(),
        BlockType.BulletList   => new ListBlockContent  { Html = initialHtml },
        BlockType.NumberedList => new ListBlockContent  { Html = initialHtml },
        BlockType.TodoItem     => new TodoBlockContent  { Html = initialHtml },
        BlockType.Toggle       => new ToggleBlockContent   { Html = initialHtml },
        BlockType.Table        => new TableBlockContent(),
        BlockType.ColumnList   => new ColumnListBlockContent(),
        BlockType.Breadcrumb   => new BreadcrumbBlockContent(),
        BlockType.Image        => new ImageBlockContent(),
        BlockType.Video        => new VideoBlockContent(),
        BlockType.Audio        => new AudioBlockContent(),
        BlockType.File         => new FileBlockContent(),
        BlockType.Pdf          => new PdfBlockContent(),
        BlockType.Bookmark     => new BookmarkBlockContent(),
        BlockType.Embed        => new EmbedBlockContent(),
        BlockType.Diagram      => new DiagramBlockContent(),
        BlockType.Wireframe    => new WireframeBlockContent(),
        BlockType.Spreadsheet  => new SpreadsheetBlockContent(),
        BlockType.WorkItem     => new WorkItemBlockContent(),
        BlockType.ContentByLabel => new ContentByLabelBlockContent(),
        _                      => new TextBlockContent  { Html = initialHtml }
    };

    // ── Slash menu handlers ───────────────────────────────────────────────────

    private Task HandleSlashMenuOpenedAsync((string BlockId, double Top, double Left) args)
    {
        _slashBlockId   = args.BlockId;
        _slashMenuTop   = args.Top;
        _slashMenuLeft  = args.Left;
        _slashMenuVisible = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task HandleSlashItemSelectedAsync(BlockType selectedType)
    {
        _slashMenuVisible = false;

        if (!string.IsNullOrEmpty(_slashBlockId))
            await HandleConvertBlockAsync((_slashBlockId, selectedType));

        _slashBlockId = string.Empty;
        StateHasChanged();
    }

    private async Task HandleSlashMenuItemSelectedAsync(SlashMenuItem item)
    {
        _slashMenuVisible = false;

        if (item.Action == SlashMenuAction.InsertStatus)
        {
            _statusPickerTop = _slashMenuTop;
            _statusPickerLeft = _slashMenuLeft;
            _statusInitialLabel = null;
            _statusInitialColor = NotionStatusColor.Gray;
            _statusEditingExistingChip = false;
            _statusPickerVisible = true;
            StateHasChanged();
            return;
        }

        if (!string.IsNullOrEmpty(_slashBlockId))
        {
            if (item.CalloutVariant is { } variant)
            {
                await ConvertBlockToCalloutVariantAsync(_slashBlockId, variant);
            }
            else
            {
                await HandleConvertBlockAsync((_slashBlockId, item.Type));
            }
        }

        _slashBlockId = string.Empty;
        StateHasChanged();
    }

    private Task HandleSlashAISelectedAsync()
    {
        _slashMenuVisible = false;
        _aiMenuVisible = true;
        _aiMenuPanel = false;
        _aiMenuMode = NotionAiMenuMode.Generate;
        _aiMenuTop = _slashMenuTop;
        _aiMenuLeft = _slashMenuLeft;
        _aiSourceText = string.Empty;
        _aiContextHtml = BuildPageContextHtml();
        _aiTargetBlockId = _slashBlockId;
        _aiReplaceSavedSelection = false;
        _slashBlockId = string.Empty;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandleSlashMenuClosedAsync()
    {
        _slashMenuVisible = false;
        _slashBlockId     = string.Empty;
        StateHasChanged();
        return Task.CompletedTask;
    }

    // ── Token dropdown handlers ────────────────────────────────────────────────

    private Task HandleTokenMenuOpenedAsync((string BlockId, double Top, double Left) args)
    {
        _tokenBlockId          = args.BlockId;
        _tokenDropdownTop      = args.Top;
        _tokenDropdownLeft     = args.Left;
        _tokenCurrentKey       = null;
        _tokenIsEditMode       = false;
        _tokenDropdownVisible  = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnTokenChipClicked(string key, double top, double left)
    {
        _tokenCurrentKey      = key;
        _tokenIsEditMode      = true;
        _tokenDropdownTop     = top;
        _tokenDropdownLeft    = left;
        _tokenDropdownVisible = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task HandleTokenItemSelectedAsync((string Key, string DisplayName, string? ColorClass) args)
    {
        _tokenDropdownVisible = false;
        var wasEdit = _tokenIsEditMode;
        _tokenBlockId    = string.Empty;
        _tokenCurrentKey = null;
        _tokenIsEditMode = false;
        StateHasChanged();

        try
        {
            if (wasEdit)
                await JS.InvokeVoidAsync("tmNotionEditor.replaceNotionToken", args.Key, args.DisplayName, args.ColorClass);
            else
                await JS.InvokeVoidAsync("tmNotionEditor.insertNotionToken", args.Key, args.DisplayName, args.ColorClass);
        }
        catch { }
    }

    private async Task HandleTokenDropdownClosedAsync()
    {
        _tokenDropdownVisible = false;
        var wasEdit = _tokenIsEditMode;
        _tokenBlockId    = string.Empty;
        _tokenCurrentKey = null;
        _tokenIsEditMode = false;
        StateHasChanged();

        try
        {
            if (wasEdit)
                await JS.InvokeVoidAsync("tmNotionEditor.cancelChipEdit");
            else
                await JS.InvokeVoidAsync("tmNotionEditor.cancelTokenTrigger");
        }
        catch { }
    }

    private async Task HandleTokenCreateRequestedAsync(string query)
    {
        // Close dropdown visually but keep JS trigger/chip-edit state alive
        // so we can insert the new token into the correct position afterwards.
        _tokenDropdownVisible = false;
        var wasEdit = _tokenIsEditMode;
        _tokenIsEditMode = false;
        _tokenCurrentKey = null;
        StateHasChanged();

        if (OnCreateTokenRequested is null)
        {
            try { await JS.InvokeVoidAsync(wasEdit ? "tmNotionEditor.cancelChipEdit" : "tmNotionEditor.cancelTokenTrigger"); } catch { }
            return;
        }

        (string Key, string DisplayName, string? ColorClass)? result = null;
        try { result = await OnCreateTokenRequested(query); } catch { }

        if (result.HasValue)
        {
            try
            {
                var jsMethod = wasEdit ? "tmNotionEditor.replaceNotionToken" : "tmNotionEditor.insertNotionToken";
                await JS.InvokeVoidAsync(jsMethod, result.Value.Key, result.Value.DisplayName, result.Value.ColorClass);
            }
            catch { }
        }
        else
        {
            try { await JS.InvokeVoidAsync(wasEdit ? "tmNotionEditor.cancelChipEdit" : "tmNotionEditor.cancelTokenTrigger"); } catch { }
        }
    }

    // ── Mention menu handlers ─────────────────────────────────────────────────

    private Task HandleMentionMenuOpenedAsync((string BlockId, double Top, double Left) args)
    {
        _mentionBlockId     = args.BlockId;
        _mentionMenuTop     = args.Top;
        _mentionMenuLeft    = args.Left;
        _mentionMenuVisible = true;
        _mentionPagesOnly   = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task HandlePageLinkMenuOpenedAsync((string BlockId, double Top, double Left) args)
    {
        _mentionBlockId     = args.BlockId;
        _mentionMenuTop     = args.Top;
        _mentionMenuLeft    = args.Left;
        _mentionMenuVisible = true;
        _mentionPagesOnly   = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task HandleMentionItemSelectedAsync((string Type, string Id, string Display) args)
    {
        _mentionMenuVisible = false;
        _mentionBlockId     = string.Empty;
        StateHasChanged();

        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.insertMentionChip", args.Type, args.Id, args.Display);
        }
        catch { }
    }

    private Task HandleMentionMenuClosedAsync()
    {
        _mentionMenuVisible = false;
        _mentionBlockId     = string.Empty;
        StateHasChanged();
        return Task.CompletedTask;
    }

    // ── Inline toolbar JS callbacks ───────────────────────────────────────────

    [JSInvokable]
    public Task OnToolbarSelectionChanged(
        double top, double left,
        bool isBold, bool isItalic, bool isUnderline, bool isStrikethrough, bool isCode,
        string currentHref, string blockId, string selectedText)
    {
        return InvokeAsync(() =>
        {
            _toolbarVisible        = true;
            _toolbarTop            = top;
            _toolbarLeft           = left;
            _toolbarIsBold         = isBold;
            _toolbarIsItalic       = isItalic;
            _toolbarIsUnderline    = isUnderline;
            _toolbarIsStrikethrough = isStrikethrough;
            _toolbarIsCode         = isCode;
            _toolbarCurrentHref    = currentHref;
            _toolbarBlockId        = blockId;
            _toolbarSelectedText   = selectedText;

            var block = _blocks.FirstOrDefault(b => b.Id.ToString() == blockId);
            _toolbarCurrentAlign = block?.Content is ITextBlockContent tc ? tc.Alignment : TextAlignment.Left;

            StateHasChanged();
        });
    }

    [JSInvokable]
    public Task OnToolbarSelectionCleared()
    {
        return InvokeAsync(() =>
        {
            if (!_toolbarVisible) return;
            _toolbarVisible = false;
            _toolbarSelectedText = string.Empty;
            StateHasChanged();
        });
    }

    [JSInvokable]
    public Task OnTextCommentCreated(string blockId, string commentId, string highlightedText, int startOffset, int endOffset, double top, double left)
    {
        _textCommentId          = commentId;
        _textCommentBlockId     = blockId;
        _textCommentStartOffset = startOffset;
        _textCommentEndOffset   = endOffset;
        _textCommentHighlight   = highlightedText;
        _textCommentTop         = top;
        _textCommentLeft        = left + 40;
        _textCommentVisible     = true;
        _toolbarVisible         = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnTextCommentMarkClicked(string commentId, string blockId, double top, double left)
    {
        _textCommentId      = commentId;
        _textCommentBlockId = blockId;
        _textCommentTop     = top;
        _textCommentLeft    = left + 40;
        _textCommentVisible = true;
        _toolbarVisible     = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    // ── Inline toolbar event handlers ─────────────────────────────────────────

    private async Task HandleToolbarTurnIntoAsync(BlockType newType)
    {
        if (!string.IsNullOrEmpty(_toolbarBlockId))
            await HandleConvertBlockAsync((_toolbarBlockId, newType));
    }

    private async Task HandleToolbarAlignAsync(TextAlignment alignment)
    {
        _toolbarCurrentAlign = alignment;
        var block = _blocks.FirstOrDefault(b => b.Id.ToString() == _toolbarBlockId);
        if (block is null) return;

        var applied = block.Content switch
        {
            TextBlockContent    tb => (tb.Alignment = alignment) == alignment,
            HeadingBlockContent hb => (hb.Alignment = alignment) == alignment,
            ListBlockContent    lb => (lb.Alignment = alignment) == alignment,
            TodoBlockContent    td => (td.Alignment = alignment) == alignment,
            ToggleBlockContent  tg => (tg.Alignment = alignment) == alignment,
            CalloutBlockContent cb => (cb.Alignment = alignment) == alignment,
            _                      => false
        };

        if (applied)
        {
            try { await Context.BlockService.UpdateBlockAsync(block); }
            catch { /* best-effort */ }
            StateHasChanged();
        }
    }

    private async Task HandleToolbarAIAsync()
    {
        if (Context.AIProvider is null) return;

        _aiMenuVisible = true;
        _aiMenuPanel = false;
        _aiMenuMode = NotionAiMenuMode.Improve;
        _aiMenuTop = _toolbarTop + 42;
        _aiMenuLeft = _toolbarLeft;
        _aiSourceText = string.IsNullOrWhiteSpace(_toolbarSelectedText)
            ? await GetSelectedTextAsync()
            : _toolbarSelectedText;
        _aiContextHtml = GetBlockHtml(_toolbarBlockId);
        _aiTargetBlockId = _toolbarBlockId;
        _aiReplaceSavedSelection = true;
        _toolbarVisible = false;
        StateHasChanged();
    }

    private async Task ConvertBlockToCalloutVariantAsync(string blockId, CalloutVariant variant)
    {
        if (ReadOnly) return;

        var existing = _blocks.FirstOrDefault(b => b.Id.ToString() == blockId);
        var (liveHtml, caretOffset) = await CaptureLiveEditsAsync(blockId);
        var html = liveHtml ?? (existing?.Content is ITextBlockContent text ? text.Html : string.Empty);

        try
        {
            var converted = await Context.BlockService.ConvertBlockTypeAsync(blockId, BlockType.Callout, liveHtml);
            var content = converted.Content as ICalloutBlockContent;
            var updated = new PageBlock
            {
                Id = converted.Id,
                PageId = converted.PageId,
                ParentBlockId = converted.ParentBlockId,
                Type = BlockType.Callout,
                Order = converted.Order,
                CreatedAt = converted.CreatedAt,
                LastEditedAt = DateTime.UtcNow,
                Content = new CalloutBlockContent
                {
                    Html = content?.Html ?? html,
                    IconEmoji = content?.IconEmoji,
                    IconImageUrl = content?.IconImageUrl,
                    Variant = variant,
                    BackgroundColor = content?.BackgroundColor,
                    TextColor = content?.TextColor,
                    Alignment = content?.Alignment ?? TextAlignment.Left
                }
            };

            await Context.BlockService.UpdateBlockAsync(updated);
            var idx = _blocks.FindIndex(b => b.Id == updated.Id);
            if (idx >= 0) _blocks[idx] = updated;
            Context.RaiseBlockConverted(updated);
            _activeBlockId = updated.Id;
            StateHasChanged();

            await RestoreCaretAsync(updated.Id.ToString(), caretOffset);
        }
        catch
        {
            SetBlockOperationError();
        }
    }

    private async Task HandleStatusInsertedAsync((string Label, NotionStatusColor Color) status)
    {
        var html = BuildStatusChipHtml(status.Label, status.Color);

        try
        {
            if (_statusEditingExistingChip)
            {
                await JS.InvokeVoidAsync(
                    "tmNotionEditor.replaceActiveStatusChip",
                    html,
                    _statusEditingBlockId,
                    _statusEditingChipIndex);
            }
            else
            {
                await JS.InvokeVoidAsync("tmNotionEditor.insertSlashHtml", html);
            }
        }
        catch { }

        _statusPickerVisible = false;
        _statusInitialLabel = null;
        _statusInitialColor = NotionStatusColor.Gray;
        _statusEditingExistingChip = false;
        _statusEditingBlockId = string.Empty;
        _statusEditingChipIndex = -1;
        _slashBlockId = string.Empty;
        StateHasChanged();
    }

    private async Task HandleStatusPickerClosedAsync()
    {
        _statusPickerVisible = false;
        _statusInitialLabel = null;
        _statusInitialColor = NotionStatusColor.Gray;
        _statusEditingExistingChip = false;
        _statusEditingBlockId = string.Empty;
        _statusEditingChipIndex = -1;
        try { await JS.InvokeVoidAsync("tmNotionEditor.cancelStatusEdit"); } catch { }
        StateHasChanged();
    }

    [JSInvokable]
    public Task OnInlineStatusClicked(string blockId, string label, string color, DomRect rect, int chipIndex)
    {
        _statusPickerTop = rect.Top + rect.Height + 6;
        _statusPickerLeft = rect.Left;
        _statusInitialLabel = label;
        _statusInitialColor = Enum.TryParse<NotionStatusColor>(color, ignoreCase: true, out var parsed)
            ? parsed
            : NotionStatusColor.Gray;
        _statusEditingExistingChip = true;
        _statusEditingBlockId = blockId;
        _statusEditingChipIndex = chipIndex;
        _statusPickerVisible = true;
        _toolbarVisible = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private static string BuildStatusChipHtml(string label, NotionStatusColor color)
    {
        var trimmed = label.Trim();
        var encoded = WebUtility.HtmlEncode(trimmed);
        var cssColor = color.ToString().ToLowerInvariant();
        return $"""<span contenteditable="false" class="tm-notion-status tm-notion-status--{cssColor}" data-status-label="{encoded}" data-status-color="{cssColor}"><span class="tm-notion-status__label">{encoded}</span></span>""";
    }

    private async Task HandleAIAcceptedAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return;

        if (_aiReplaceSavedSelection)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.replaceSavedSelectionWithHtml", html); }
            catch { }
        }
        else if (!string.IsNullOrWhiteSpace(_aiTargetBlockId))
        {
            await ApplyHtmlToBlockAsync(_aiTargetBlockId, html);
        }
        else
        {
            await AddBlockAsync(BlockType.Paragraph, initialHtml: html);
        }

        await HandleAIClosedAsync();
    }

    private Task HandleAIClosedAsync()
    {
        _aiMenuVisible = false;
        _aiMenuPanel = false;
        _aiSourceText = string.Empty;
        _aiContextHtml = string.Empty;
        _aiTargetBlockId = string.Empty;
        _aiReplaceSavedSelection = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private void OpenAIPagePanel(NotionAiMenuMode mode)
    {
        if (Context.AIProvider is null) return;

        _aiMenuVisible = true;
        _aiMenuPanel = true;
        _aiMenuMode = mode;
        _aiMenuTop = 96;
        _aiMenuLeft = 0;
        _aiSourceText = string.Empty;
        _aiContextHtml = BuildPageContextHtml();
        _aiTargetBlockId = string.Empty;
        _aiReplaceSavedSelection = false;
        StateHasChanged();
    }

    private async Task<string> GetSelectedTextAsync()
    {
        try { return await JS.InvokeAsync<string>("tmNotionEditor.getSelectedText"); }
        catch { return string.Empty; }
    }

    private string BuildPageContextHtml()
        => string.Join("\n", _blocks.Select(block => GetBlockHtml(block.Id.ToString())).Where(html => !string.IsNullOrWhiteSpace(html)));

    private string GetBlockHtml(string blockId)
    {
        var block = _blocks.FirstOrDefault(b => b.Id.ToString() == blockId);
        return block?.Content switch
        {
            ITextBlockContent text => text.Html,
            _ => string.Empty
        };
    }

    private async Task ApplyHtmlToBlockAsync(string blockId, string html)
    {
        var block = _blocks.FirstOrDefault(b => b.Id.ToString() == blockId);
        if (block is null)
        {
            await AddBlockAsync(BlockType.Paragraph, initialHtml: html);
            return;
        }

        var applied = block.Content switch
        {
            TextBlockContent text => ApplyHtml(text, html),
            HeadingBlockContent heading => ApplyHtml(heading, html),
            ListBlockContent list => ApplyHtml(list, html),
            TodoBlockContent todo => ApplyHtml(todo, html),
            ToggleBlockContent toggle => ApplyHtml(toggle, html),
            CalloutBlockContent callout => ApplyHtml(callout, html),
            _ => false
        };

        if (!applied)
        {
            await AddBlockAsync(BlockType.Paragraph, blockId, html);
            return;
        }

        try
        {
            await Context.BlockService.UpdateBlockAsync(block);
            _blocks = [.._blocks];
            StateHasChanged();
        }
        catch
        {
            SetBlockOperationError();
            StateHasChanged();
        }
    }

    private static bool ApplyHtml(TextBlockContent content, string html)
    {
        content.Html = html;
        return true;
    }

    private static bool ApplyHtml(HeadingBlockContent content, string html)
    {
        content.Html = html;
        return true;
    }

    private static bool ApplyHtml(ListBlockContent content, string html)
    {
        content.Html = html;
        return true;
    }

    private static bool ApplyHtml(TodoBlockContent content, string html)
    {
        content.Html = html;
        return true;
    }

    private static bool ApplyHtml(ToggleBlockContent content, string html)
    {
        content.Html = html;
        return true;
    }

    private static bool ApplyHtml(CalloutBlockContent content, string html)
    {
        content.Html = html;
        return true;
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (Context.BlockCreated == HandleExternalBlockCreatedAsync)
            Context.BlockCreated = null;

        if (_collabSubscribed && Context.CollaborationSync is { } sync)
            sync.RemoteBlockChanged -= OnRemoteBlockChanged;

        if (_toolbarWatcherReady)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.destroySelectionWatcher", _pageRef); }
            catch { }
        }
        _toolbarDotNetRef?.Dispose();
    }
}
