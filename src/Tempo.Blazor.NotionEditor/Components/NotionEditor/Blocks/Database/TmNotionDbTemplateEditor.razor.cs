using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbTemplateEditor : ComponentBase
{
    // ── Cascaded context ──────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IDatabaseRecordTemplate           Template        { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField>     Fields          { get; set; } = [];
    [Parameter] public string   DatabaseName { get; set; } = string.Empty;
    [Parameter] public bool     ReadOnly     { get; set; }

    /// <summary>Whether the editor dialog is open. Defaults to <c>true</c> — the host renders the
    /// component only while a template is being edited.</summary>
    [Parameter] public bool Show { get; set; } = true;

    [Parameter] public EventCallback                             OnClose          { get; set; }
    [Parameter] public EventCallback<IDatabaseRecordTemplate>   OnTemplateSaved  { get; set; }

    // ── Editing state ─────────────────────────────────────────────────────────

    private string                       _nameBuffer     = string.Empty;
    private string?                      _iconEmoji;
    private Dictionary<string, object?>  _localFields    = [];
    private List<IPageBlock>             _localBlocks    = [];
    private Guid?                        _editingFieldId;
    private Guid?                        _activeBlockId;
    private Guid                         _lastTemplateId;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        if (Template.Id == _lastTemplateId) return;
        _lastTemplateId = Template.Id;
        _nameBuffer     = Template.Name;
        _iconEmoji      = Template.IconEmoji;
        _localFields    = Template.DefaultFields.ToDictionary(k => k.Key, k => k.Value);
        _localBlocks    = Template.TemplateBlocks.ToList();
        _editingFieldId = null;
    }

    // ── Properties ────────────────────────────────────────────────────────────

    private string TemplateName => _nameBuffer is { Length: > 0 } n ? n : Loc["TmNotionDbTemplateEditor_Untitled"];

    private IEnumerable<IDatabaseField> NonPrimaryFields
        => Fields.Where(f => !f.IsPrimary);

    // ── Save ──────────────────────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        var updated = new DatabaseRecordTemplate
        {
            Id             = Template.Id,
            DatabaseId     = Template.DatabaseId,
            Name           = _nameBuffer.Trim().Length > 0 ? _nameBuffer.Trim() : Template.Name,
            IconEmoji      = _iconEmoji,
            DefaultFields  = new Dictionary<string, object?>(_localFields),
            TemplateBlocks = _localBlocks.ToList()
        };
        try
        {
            var result = await Context.DatabaseProvider.UpdateTemplateAsync(
                Template.DatabaseId.ToString(), updated);
            await OnTemplateSaved.InvokeAsync(result);
        }
        catch { /* silently ignore */ }
    }

    private async Task HandleCellCommitAsync(IDatabaseField field, object? value)
    {
        _localFields[field.Id.ToString()] = value;
        _editingFieldId = null;
        await SaveAsync();
    }

    private async Task CommitNameAsync()
    {
        await SaveAsync();
    }

    /// <summary>
    /// The single close path (×, Escape, the footer button): commits a pending name edit BEFORE
    /// raising <see cref="OnClose"/>, so a name typed but never blurred (Escape does not blur)
    /// still reaches the provider and <see cref="OnTemplateSaved"/>.
    /// </summary>
    private async Task HandleCloseAsync()
    {
        if (!ReadOnly && !string.Equals(_nameBuffer, Template.Name, StringComparison.Ordinal))
            await CommitNameAsync();

        await OnClose.InvokeAsync();
    }

    // ── Block handlers (all local — no provider) ──────────────────────────────

    private async Task HandleAddBlockAfterAsync((string AfterBlockId, BlockType Type, string? InitialHtml) args)
    {
        var afterBlock  = _localBlocks.FirstOrDefault(b => b.Id.ToString() == args.AfterBlockId);
        var insertOrder = afterBlock is null
            ? (_localBlocks.Count > 0 ? _localBlocks.Max(b => b.Order) + 1 : 0)
            : afterBlock.Order + 1;

        var newBlock = new PageBlock
        {
            Id      = Guid.NewGuid(),
            Type    = args.Type,
            Order   = insertOrder,
            Content = CreateDefaultContent(args.Type, args.InitialHtml)
        };

        var insertIdx = afterBlock is null
            ? _localBlocks.Count
            : _localBlocks.IndexOf(afterBlock) + 1;
        _localBlocks.Insert(Math.Clamp(insertIdx, 0, _localBlocks.Count), newBlock);
        _activeBlockId = newBlock.Id;
        StateHasChanged();
        await SaveAsync();
    }

    private async Task HandleAddBlockAtEndAsync()
    {
        var lastId = _localBlocks.Count > 0 ? _localBlocks[^1].Id.ToString() : string.Empty;
        await HandleAddBlockAfterAsync((lastId, BlockType.Paragraph, null));
    }

    private Task HandleBlockFocusedAsync(string blockId)
    {
        if (Guid.TryParse(blockId, out var id)) _activeBlockId = id;
        return Task.CompletedTask;
    }

    private async Task HandleBlockUpdatedAsync(IPageBlock updated)
    {
        var idx = _localBlocks.FindIndex(b => b.Id == updated.Id);
        if (idx >= 0) _localBlocks[idx] = updated;
        await SaveAsync();
    }

    private async Task HandleBlockDeletedAsync(string blockId)
    {
        var removed = _localBlocks.RemoveAll(b => b.Id.ToString() == blockId) > 0;
        if (removed)
        {
            StateHasChanged();
            await SaveAsync();
        }
    }

    private async Task HandleBlockDuplicatedAsync(IPageBlock source)
    {
        var dup = new PageBlock
        {
            Id      = Guid.NewGuid(),
            Type    = source.Type,
            Order   = source.Order + 1,
            Content = source.Content
        };
        var insertAt = _localBlocks.FindIndex(b => b.Id == source.Id) + 1;
        _localBlocks.Insert(Math.Clamp(insertAt, 0, _localBlocks.Count), dup);
        StateHasChanged();
        await SaveAsync();
    }

    private async Task HandleReorderAsync((int source, int target) args)
    {
        if (args.source < 0 || args.source >= _localBlocks.Count) return;
        var block  = _localBlocks[args.source];
        var target = Math.Clamp(args.source < args.target ? args.target - 1 : args.target,
                                0, _localBlocks.Count - 1);
        _localBlocks.RemoveAt(args.source);
        _localBlocks.Insert(target, block);
        StateHasChanged();
        await SaveAsync();
    }

    private async Task HandleConvertBlockAsync((string blockId, BlockType newType) args)
    {
        var idx = _localBlocks.FindIndex(b => b.Id.ToString() == args.blockId);
        if (idx < 0) return;
        var old = _localBlocks[idx];
        _localBlocks[idx] = new PageBlock
        {
            Id      = old.Id,
            Type    = args.newType,
            Order   = old.Order,
            Content = CreateDefaultContent(args.newType, null)
        };
        StateHasChanged();
        await SaveAsync();
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
