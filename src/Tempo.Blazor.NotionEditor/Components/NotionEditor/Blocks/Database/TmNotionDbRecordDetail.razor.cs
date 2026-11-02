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
            await ShowSavedIndicatorAsync();
        }
        catch { /* silently ignore — UI already updated optimistically */ }
    }

    private async Task ShowSavedIndicatorAsync()
    {
        _savedCts?.Cancel();
        _savedCts = new CancellationTokenSource();
        var token = _savedCts.Token;
        _showSaved = true;
        StateHasChanged();
        try
        {
            await Task.Delay(2000, token);
            _showSaved = false;
            StateHasChanged();
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

    // ── Field type icon ───────────────────────────────────────────────────────

    internal static Microsoft.AspNetCore.Components.MarkupString FieldTypeIcon(DatabaseFieldType t) =>
        (Microsoft.AspNetCore.Components.MarkupString)(t switch
        {
            DatabaseFieldType.Text        => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><path d='M4 7h16M4 12h16M4 17h10' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
            DatabaseFieldType.Number      => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><path d='M7 20l4-16M13 20l4-16M3 10h18M3 14h18' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
            DatabaseFieldType.Select      => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><circle cx='12' cy='12' r='9' stroke='currentColor' stroke-width='1.5'/><circle cx='12' cy='12' r='4' fill='currentColor'/></svg>",
            DatabaseFieldType.MultiSelect => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><rect x='2' y='5' width='9' height='6' rx='1' fill='currentColor'/><rect x='13' y='5' width='9' height='6' rx='1' fill='currentColor'/><rect x='2' y='14' width='9' height='6' rx='1' fill='currentColor'/></svg>",
            DatabaseFieldType.Status      => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><circle cx='12' cy='12' r='9' stroke='currentColor' stroke-width='1.5'/><path d='M8 12l3 3 5-5' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
            DatabaseFieldType.Date or DatabaseFieldType.DateRange => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><rect x='3' y='4' width='18' height='17' rx='2' stroke='currentColor' stroke-width='1.5'/><path d='M16 2v4M8 2v4M3 10h18' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
            DatabaseFieldType.Person or DatabaseFieldType.CreatedBy or DatabaseFieldType.LastEditedBy => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><circle cx='12' cy='8' r='4' stroke='currentColor' stroke-width='1.5'/><path d='M4 20c0-4 3.6-7 8-7s8 3 8 7' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
            DatabaseFieldType.Files       => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z' stroke='currentColor' stroke-width='1.5'/><polyline points='14 2 14 8 20 8' stroke='currentColor' stroke-width='1.5'/></svg>",
            DatabaseFieldType.Checkbox    => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><rect x='3' y='3' width='18' height='18' rx='2' stroke='currentColor' stroke-width='1.5'/><path d='M7 12l4 4 6-6' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
            DatabaseFieldType.Url         => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><path d='M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/><path d='M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
            DatabaseFieldType.Email       => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><rect x='2' y='4' width='20' height='16' rx='2' stroke='currentColor' stroke-width='1.5'/><path d='M2 8l10 6 10-6' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
            DatabaseFieldType.Phone       => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><path d='M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
            DatabaseFieldType.Formula     => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><path d='M3 3h7v2H5v14h5v2H3zm18 0h-7v2h5v14h-5v2h7z' fill='currentColor' opacity='.4'/><path d='M9 12h6m-3-3v6' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
            DatabaseFieldType.Relation    => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><path d='M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
            DatabaseFieldType.Rollup      => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><path d='M3 3v18h18' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/><path d='M7 16l4-6 4 4 4-8' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
            DatabaseFieldType.CreatedTime or DatabaseFieldType.LastEditedTime => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><circle cx='12' cy='12' r='9' stroke='currentColor' stroke-width='1.5'/><path d='M12 7v5l3 3' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
            _ => "<svg width='14' height='14' viewBox='0 0 24 24' fill='none'><path d='M4 7h16M4 12h16M4 17h10' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>"
        });

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
