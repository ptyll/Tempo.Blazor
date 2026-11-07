using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbRecordDetail
{
    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IDatabaseRecord             Record       { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField> Fields     { get; set; } = [];
    [Parameter] public string   DatabaseName { get; set; } = string.Empty;
    [Parameter] public bool     ReadOnly     { get; set; }

    /// <summary>Whether the detail dialog is open. Defaults to <c>true</c> — the host renders the
    /// component only while a record is open, and tests render it directly.</summary>
    [Parameter] public bool Show { get; set; } = true;

    [Parameter] public EventCallback                  OnClose         { get; set; }
    [Parameter] public EventCallback<IDatabaseRecord> OnRecordUpdated { get; set; }
    [Parameter] public EventCallback<IDatabaseRecord> OnOpenAsPage    { get; set; }

    /// <summary>
    /// Raised after the user confirms deleting this record. The detail does not call the provider
    /// itself — the host owns the row list and the <c>DeleteRecordAsync</c> call.
    /// </summary>
    [Parameter] public EventCallback<IDatabaseRecord> OnRecordDeleted { get; set; }

    private bool _showDeleteConfirm;

    // ── Field editing state ──────────────────────────────────────────────────

    private Dictionary<string, object?> _localFields = [];
    private Guid?                       _editingFieldId;

    // ── Title editing ────────────────────────────────────────────────────────

    private string _titleBuffer = string.Empty;

    // ── Save indicator ───────────────────────────────────────────────────────

    private bool              _showSaved;
    private CancellationTokenSource? _savedCts;

    // ── Content blocks ───────────────────────────────────────────────────────

    private List<IPageBlock> _contentBlocks = [];
    private bool             _loadingBlocks;
    private Guid?            _activeBlockId;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private Guid _lastRecordId;

    protected override async Task OnParametersSetAsync()
    {
        if (Record.Id == _lastRecordId) return;
        _lastRecordId    = Record.Id;
        _localFields     = Record.Fields.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        _titleBuffer     = TitleValue;
        _editingFieldId  = null;
        await LoadBlocksAsync();
    }

    private async Task LoadBlocksAsync()
    {
        if (Context.BlockService is null) return;
        _loadingBlocks = true;
        StateHasChanged();
        try
        {
            var blocks = await Context.BlockService.GetBlocksAsync(Record.Id.ToString());
            _contentBlocks = blocks.OrderBy(b => b.Order).ToList();
        }
        catch { _contentBlocks = []; }
        finally
        {
            _loadingBlocks = false;
            StateHasChanged();
        }
    }

    // ── Properties ────────────────────────────────────────────────────────────

    private string TitleValue
    {
        get
        {
            var tf = Fields.FirstOrDefault(f => f.IsPrimary);
            if (tf is null) return string.Empty;
            return _localFields.TryGetValue(tf.Id.ToString(), out var v) && v is string s ? s : string.Empty;
        }
    }

    private string RecordTitle => TitleValue is { Length: > 0 } t ? t : Loc["TmNotionDbRecordDetail_Untitled"];

    private IDatabaseField? PrimaryField => Fields.FirstOrDefault(f => f.IsPrimary);

    private IEnumerable<IDatabaseField> NonPrimaryFields
        => Fields.Where(f => !f.IsPrimary);

    private async Task CommitTitleAsync()
    {
        var tf = PrimaryField;
        if (tf is null) return;
        _localFields[tf.Id.ToString()] = _titleBuffer;
        await SaveAsync();
    }

    /// <summary>
    /// The single close path (×, Escape, the footer button): commits a pending title edit BEFORE
    /// raising <see cref="OnClose"/>, so a title typed but never blurred (Escape does not blur)
    /// still reaches the provider and <see cref="OnRecordUpdated"/>.
    /// </summary>
    private async Task HandleCloseAsync()
    {
        if (!ReadOnly && PrimaryField is { } primary)
        {
            var saved = _localFields.TryGetValue(primary.Id.ToString(), out var v) && v is string s ? s : string.Empty;
            if (!string.Equals(saved, _titleBuffer, StringComparison.Ordinal))
                await CommitTitleAsync();
        }

        await OnClose.InvokeAsync();
    }

    private async Task HandleCellCommitAsync(IDatabaseField field, object? value)
    {
        _localFields[field.Id.ToString()] = value;
        _editingFieldId = null;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        var updated = new DatabaseRecord
        {
            Id                 = Record.Id,
            DatabaseId         = Record.DatabaseId,
            ParentRecordId     = Record.ParentRecordId,
            Fields             = new Dictionary<string, object?>(_localFields),
            CreatedAt          = Record.CreatedAt,
            CreatedByUserId    = Record.CreatedByUserId,
            LastEditedAt       = DateTime.UtcNow,
            LastEditedByUserId = Record.LastEditedByUserId
        };
        try
        {
            var result = await Context.DatabaseProvider.UpdateRecordAsync(Record.DatabaseId.ToString(), updated);
            await OnRecordUpdated.InvokeAsync(result);
            _ = ShowSavedIndicatorAsync();
        }
        catch { /* silently ignore — UI already updated optimistically */ }
    }

    private async Task ShowSavedIndicatorAsync()
    {
        _savedCts?.Cancel();
        _savedCts = new CancellationTokenSource();
        var token = _savedCts.Token;
        _showSaved = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            await Task.Delay(2000, token);
            _showSaved = false;
            await InvokeAsync(StateHasChanged);
        }
        catch (TaskCanceledException) { }
    }

    // ── Block handlers ────────────────────────────────────────────────────────

    private async Task HandleAddBlockAfterAsync((string AfterBlockId, BlockType Type, string? InitialHtml) args)
    {
        if (Context.BlockService is null || ReadOnly) return;

        var afterBlock  = _contentBlocks.FirstOrDefault(b => b.Id.ToString() == args.AfterBlockId);
        var insertOrder = afterBlock is null
            ? (_contentBlocks.Count > 0 ? _contentBlocks.Max(b => b.Order) + 1 : 0)
            : afterBlock.Order + 1;

        var newBlock = new PageBlock
        {
            Id      = Guid.NewGuid(),
            PageId  = Record.Id,
            Type    = args.Type,
            Order   = insertOrder,
            Content = CreateDefaultContent(args.Type, args.InitialHtml)
        };

        try
        {
            var created   = await Context.BlockService.CreateBlockAsync(Record.Id.ToString(), newBlock, args.AfterBlockId);
            var insertIdx = afterBlock is null
                ? _contentBlocks.Count
                : _contentBlocks.IndexOf(afterBlock) + 1;
            _contentBlocks.Insert(Math.Clamp(insertIdx, 0, _contentBlocks.Count), created);
            _activeBlockId = created.Id;
            StateHasChanged();
        }
        catch { /* ignore */ }
    }

    private async Task HandleAddBlockAtEndAsync()
    {
        var lastId = _contentBlocks.Count > 0 ? _contentBlocks[^1].Id.ToString() : null;
        await HandleAddBlockAfterAsync((lastId ?? string.Empty, BlockType.Paragraph, null));
    }

    private Task HandleBlockFocusedAsync(string blockId)
    {
        if (Guid.TryParse(blockId, out var id)) _activeBlockId = id;
        return Task.CompletedTask;
    }

    private Task HandleBlockUpdatedAsync(IPageBlock updated)
    {
        var idx = _contentBlocks.FindIndex(b => b.Id == updated.Id);
        if (idx >= 0) _contentBlocks[idx] = updated;
        return Task.CompletedTask;
    }

    private async Task HandleBlockDeletedAsync(string blockId)
    {
        if (Context.BlockService is null || ReadOnly) return;
        var block = _contentBlocks.FirstOrDefault(b => b.Id.ToString() == blockId);
        if (block is null) return;
        try
        {
            await Context.BlockService.DeleteBlockAsync(blockId);
            _contentBlocks.Remove(block);
            StateHasChanged();
        }
        catch { /* ignore */ }
    }

    private async Task HandleBlockDuplicatedAsync(IPageBlock source)
    {
        if (Context.BlockService is null || ReadOnly) return;
        try
        {
            var dup      = await Context.BlockService.DuplicateBlockAsync(source.Id.ToString());
            var insertAt = _contentBlocks.FindIndex(b => b.Id == source.Id) + 1;
            _contentBlocks.Insert(Math.Clamp(insertAt, 0, _contentBlocks.Count), dup);
            StateHasChanged();
        }
        catch { /* ignore */ }
    }

    private async Task HandleReorderAsync((int source, int target) args)
    {
        if (ReadOnly || args.source < 0 || args.source >= _contentBlocks.Count) return;
        var block   = _contentBlocks[args.source];
        var target  = Math.Clamp(args.source < args.target ? args.target - 1 : args.target, 0, _contentBlocks.Count - 1);
        _contentBlocks.RemoveAt(args.source);
        _contentBlocks.Insert(target, block);
        StateHasChanged();
        try
        {
            await Context.BlockService.ReorderBlocksAsync(Record.Id.ToString(), _contentBlocks.Select(b => b.Id.ToString()));
        }
        catch { await LoadBlocksAsync(); }
    }

    private async Task HandleConvertBlockAsync((string blockId, BlockType newType) args)
    {
        if (Context.BlockService is null || ReadOnly) return;
        try
        {
            var converted = await Context.BlockService.ConvertBlockTypeAsync(args.blockId, args.newType);
            var idx       = _contentBlocks.FindIndex(b => b.Id.ToString() == args.blockId);
            if (idx >= 0) { _contentBlocks[idx] = converted; StateHasChanged(); }
        }
        catch { /* ignore */ }
    }

    // ── "Open as full page" ───────────────────────────────────────────────────

    private async Task HandleOpenAsPageAsync()
    {
        await OnOpenAsPage.InvokeAsync(Record);
        await OnClose.InvokeAsync();
    }

    private void AskDelete() => _showDeleteConfirm = true;

    private string? _deleteError;

    private async Task HandleDeleteConfirmAsync(bool? confirmed)
    {
        _showDeleteConfirm = false;
        if (confirmed != true) return;
        // The block closes this detail only after the provider accepts the delete. A throw
        // leaves the dialog open and surfaces the localized error.
        try
        {
            await OnRecordDeleted.InvokeAsync(Record);
        }
        catch (Exception)
        {
            _deleteError = Loc["TmNotionDbRecordDetail_DeleteFailed"];
            return;
        }
    }

    // Field type icons come from DatabaseFieldIcons (DB-2).

    // ── Block content factory ─────────────────────────────────────────────────

    private static IBlockContent CreateDefaultContent(BlockType type, string? html) => type switch
    {
        BlockType.Heading1     => new HeadingBlockContent { Html = html, Level = 1 },
        BlockType.Heading2     => new HeadingBlockContent { Html = html, Level = 2 },
        BlockType.Heading3     => new HeadingBlockContent { Html = html, Level = 3 },
        BlockType.BulletList   => new ListBlockContent   { Html = html },
        BlockType.NumberedList => new ListBlockContent   { Html = html },
        BlockType.TodoItem     => new TodoBlockContent   { Html = html },
        BlockType.Toggle       => new ToggleBlockContent { Html = html },
        BlockType.Quote        => new TextBlockContent   { Html = html },
        BlockType.Callout      => new CalloutBlockContent { IconEmoji = "💡", Html = html },
        BlockType.Code         => new CodeBlockContent(),
        BlockType.Divider      => new DividerBlockContent(),
        _                      => new TextBlockContent   { Html = html }
    };
}
