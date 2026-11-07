using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.NotionEditor.Blocks.TempoBlocks;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks;

/// <summary>
/// Dispatcher for a single page block — renders the correct markup per BlockType,
/// manages contenteditable lifecycle via JS interop, and surfaces block events to the parent.
/// </summary>
public partial class TmNotionBlock : ComponentBase
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IServiceProvider Services { get; set; } = default!;

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired]
    public IPageBlock Block { get; set; } = default!;

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public int BlockIndex { get; set; }

    [Parameter] public bool IsFocused { get; set; }

    [Parameter] public bool IsSelected { get; set; }

    /// <summary>Number of unresolved block comment threads for this block.</summary>
    [Parameter] public int BlockUnresolvedCount { get; set; }

    /// <summary>Number of resolved-but-unread block comment threads for this block.</summary>
    [Parameter] public int BlockResolvedUnreadCount { get; set; }

    /// <summary>True when any thread on this block has unread activity (new entry, reaction, resolve).</summary>
    [Parameter] public bool BlockHasUnreadActivity { get; set; }

    /// <summary>Total number of comment threads on this block.</summary>
    [Parameter] public int BlockThreadCount { get; set; }

    // ── Tooltip data (latest comment entry across all threads) ───────────────
    [Parameter] public string? BlockLastAuthorName   { get; set; }
    [Parameter] public string? BlockLastAuthorAvatar { get; set; }
    [Parameter] public string? BlockLastEntryText    { get; set; }
    [Parameter] public DateTime? BlockLastEntryTime  { get; set; }

    /// <summary>1-based ordinal for NumberedList blocks, pre-computed by TmNotionBlockList.</summary>
    [Parameter] public int NumberedListNumber { get; set; }

    /// <summary>
    /// Indent level of the list item directly above this block, or <c>null</c> when this is the
    /// first block or the block above is not a list item. Tab may not nest deeper than one level
    /// below it.
    /// </summary>
    [Parameter] public int? PreviousListIndentLevel { get; set; }

    [Parameter] public EventCallback OnFocused { get; set; }

    [Parameter] public EventCallback OnDeleted { get; set; }

    /// <summary>
    /// Raised when Backspace at the start of this block should fold it into its predecessor.
    /// The payload is the block's current HTML.
    /// </summary>
    [Parameter] public EventCallback<string> OnMergeWithPrevious { get; set; }

    [Parameter] public EventCallback<IPageBlock> OnUpdated { get; set; }

    [Parameter] public EventCallback<BlockType> OnConvertTo { get; set; }

    [Parameter] public EventCallback<(BlockType Type, string? InitialHtml)> OnAddAfter { get; set; }

    [Parameter] public EventCallback<IPageBlock> OnDuplicate { get; set; }

    /// <summary>Raised when a '/' typed in this block opens the slash menu. Args are viewport coords.</summary>
    [Parameter] public EventCallback<(string BlockId, double Top, double Left)> OnSlashMenu { get; set; }

    /// <summary>Raised when '@' mention syntax is typed. Args are viewport coords.</summary>
    [Parameter] public EventCallback<(string BlockId, double Top, double Left)> OnMentionMenu { get; set; }

    /// <summary>Raised when '[[' page-link syntax is typed. Args are viewport coords.</summary>
    [Parameter] public EventCallback<(string BlockId, double Top, double Left)> OnPageLinkMenu { get; set; }

    /// <summary>Raised when '{{' token syntax is typed. Args are viewport coords.</summary>
    [Parameter] public EventCallback<(string BlockId, double Top, double Left)> OnTokenMenu { get; set; }

    /// <summary>Raised when a TemplateButton block requests insertion of its template blocks after itself.</summary>
    [Parameter] public EventCallback<IReadOnlyList<IPageBlock>> OnInsertTemplateBlocks { get; set; }

    /// <summary>Raised when a text block requests keyboard focus movement to a sibling block.</summary>
    [Parameter] public EventCallback<(string BlockId, int Direction)> OnMoveFocus { get; set; }

    /// <summary>Raised when the user clicks the Comment button in the block handle menu.</summary>
    [Parameter] public EventCallback OnComment { get; set; }

    /// <summary>Raised when the user clicks the New Thread button in the block handle menu.</summary>
    [Parameter] public EventCallback OnNewThread { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private IPageBlock?       _lastBlock;

    private ElementReference  _blockRef;

    // ── Tooltip state ────────────────────────────────────────────────────────
    private bool   _tooltipVisible;
    private System.Timers.Timer? _tooltipTimer;

    private void ShowTooltipDelayed()
    {
        _tooltipTimer?.Stop();
        _tooltipTimer?.Dispose();
        _tooltipTimer = new System.Timers.Timer(300) { AutoReset = false };
        _tooltipTimer.Elapsed += (_, _) =>
        {
            _tooltipTimer?.Dispose();
            _tooltipTimer = null;
            InvokeAsync(() =>
            {
                if (BlockUnresolvedCount > 0 || BlockResolvedUnreadCount > 0)
                {
                    _tooltipVisible = true;
                    StateHasChanged();
                }
            });
        };
        _tooltipTimer.Start();
    }

    private void HideTooltip()
    {
        _tooltipTimer?.Stop();
        _tooltipTimer?.Dispose();
        _tooltipTimer = null;
        if (_tooltipVisible)
        {
            _tooltipVisible = false;
            StateHasChanged();
        }
    }

    private ITmRelativeTimeFormatter? _relativeTime;

    private ITmRelativeTimeFormatter RelativeTime => _relativeTime ??= TmRelativeTime.Resolve(Services, Loc);

    private string FormatRelativeTime(DateTime? dt)
        => dt is null ? string.Empty : RelativeTime.Format(new DateTimeOffset(dt.Value));

    // ── Computed ─────────────────────────────────────────────────────────────

    private string _blockMods => string.Concat(
        IsFocused  ? " tm-notion-block--focused"  : string.Empty,
        IsSelected ? " tm-notion-block--selected" : string.Empty
    ).TrimStart();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        _lastBlock = Block;
    }

    private async Task OnFocusedAsync() => await OnFocused.InvokeAsync();

    private Task MoveFocusPreviousAsync() =>
        OnMoveFocus.InvokeAsync((Block.Id.ToString(), -1));

    private Task MoveFocusNextAsync() =>
        OnMoveFocus.InvokeAsync((Block.Id.ToString(), 1));

    // ── Todo (TmNotionTodoBlock) callbacks ───────────────────────────────────

    private async Task HandleTodoCheckedChangedAsync(bool isChecked)
    {
        if (Block.Content is not ITodoBlockContent todo) return;
        var updated = BuildBlockWithContent(Block, new TodoBlockContent
        {
            IsChecked       = isChecked,
            AssigneeId      = todo.AssigneeId,
            AssigneeDisplayName = todo.AssigneeDisplayName,
            DueDate         = todo.DueDate,
            IsOverdue       = IsTodoOverdue(todo.DueDate, isChecked),
            Html            = todo.Html,
            BackgroundColor = todo.BackgroundColor,
            TextColor       = todo.TextColor,
            Alignment       = todo.Alignment
        });
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleTodoAssigneeChangedAsync((string? AssigneeId, string? AssigneeDisplayName) assignee)
    {
        if (Block.Content is not ITodoBlockContent todo) return;
        var updated = BuildBlockWithContent(Block, new TodoBlockContent
        {
            IsChecked = todo.IsChecked,
            AssigneeId = string.IsNullOrWhiteSpace(assignee.AssigneeId) ? null : assignee.AssigneeId,
            AssigneeDisplayName = string.IsNullOrWhiteSpace(assignee.AssigneeDisplayName) ? null : assignee.AssigneeDisplayName,
            DueDate = todo.DueDate,
            IsOverdue = IsTodoOverdue(todo.DueDate, todo.IsChecked),
            Html = todo.Html,
            BackgroundColor = todo.BackgroundColor,
            TextColor = todo.TextColor,
            Alignment = todo.Alignment
        });

        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleTodoDueDateChangedAsync(DateTime? dueDate)
    {
        if (Block.Content is not ITodoBlockContent todo) return;
        var updated = BuildBlockWithContent(Block, new TodoBlockContent
        {
            IsChecked = todo.IsChecked,
            AssigneeId = todo.AssigneeId,
            AssigneeDisplayName = todo.AssigneeDisplayName,
            DueDate = dueDate,
            IsOverdue = IsTodoOverdue(dueDate, todo.IsChecked),
            Html = todo.Html,
            BackgroundColor = todo.BackgroundColor,
            TextColor = todo.TextColor,
            Alignment = todo.Alignment
        });

        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleTodoContentSavedAsync(string html)
    {
        var updated = BuildBlockWithHtml(Block, html);
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleTodoEnterSplitAsync(string afterHtml) =>
        await OnAddAfter.InvokeAsync((BlockType.TodoItem, afterHtml));

    private async Task HandleTodoMarkdownShortcutAsync(string shortcut)
    {
        var newType = shortcut switch
        {
            "paragraph" => BlockType.Paragraph,
            "heading1"  => BlockType.Heading1,
            "heading2"  => BlockType.Heading2,
            "heading3"  => BlockType.Heading3,
            "bullet"    => BlockType.BulletList,
            "numbered"  => BlockType.NumberedList,
            "quote"     => BlockType.Quote,
            "code"      => BlockType.Code,
            "divider"   => BlockType.Divider,
            _           => Block.Type
        };
        if (newType != Block.Type)
            await OnConvertTo.InvokeAsync(newType);
    }

    private Task HandleTodoSlashAsync((double Top, double Left) coords) =>
        OnSlashMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleTodoMentionAsync((double Top, double Left) coords) =>
        OnMentionMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleTodoPageLinkAsync((double Top, double Left) coords) =>
        OnPageLinkMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleTodoTokenAsync((double Top, double Left) coords) =>
        OnTokenMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));

    // ── Toggle (TmNotionToggleBlock) callbacks ────────────────────────────────

    private async Task HandleToggleOpenChangedAsync((bool IsOpen, string? Html) args)
    {
        if (Block.Content is not IToggleBlockContent toggle) return;
        var updated = BuildBlockWithContent(Block, new ToggleBlockContent
        {
            IsOpen          = args.IsOpen,
            Html            = args.Html ?? toggle.Html,
            BackgroundColor = toggle.BackgroundColor,
            TextColor       = toggle.TextColor,
            Alignment       = toggle.Alignment
        });
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleToggleContentSavedAsync(string html)
    {
        var updated = BuildBlockWithHtml(Block, html);
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleToggleEnterSplitAsync(string afterHtml) =>
        await OnAddAfter.InvokeAsync((BlockType.Paragraph, afterHtml));

    private async Task HandleToggleMarkdownShortcutAsync(string shortcut)
    {
        var newType = shortcut switch
        {
            "paragraph" => BlockType.Paragraph,
            "heading1"  => BlockType.Heading1,
            "heading2"  => BlockType.Heading2,
            "heading3"  => BlockType.Heading3,
            "bullet"    => BlockType.BulletList,
            "numbered"  => BlockType.NumberedList,
            "todo"      => BlockType.TodoItem,
            "quote"     => BlockType.Quote,
            "code"      => BlockType.Code,
            "divider"   => BlockType.Divider,
            _           => Block.Type
        };
        if (newType != Block.Type)
            await OnConvertTo.InvokeAsync(newType);
    }

    // ── Code (TmNotionCodeBlock) callbacks ───────────────────────────────────

    private async Task HandleCodeSavedAsync(string code)
    {
        if (Block.Content is not ICodeBlockContent cc) return;
        var updated = BuildBlockWithContent(Block, new CodeBlockContent
        {
            Code            = code,
            Language        = cc.Language,
            Caption         = cc.Caption,
            ShowLineNumbers = cc.ShowLineNumbers,
            WrapLines       = cc.WrapLines
        });
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleCodeLanguageChangedAsync(string? language)
    {
        if (Block.Content is not ICodeBlockContent cc) return;
        var updated = BuildBlockWithContent(Block, new CodeBlockContent
        {
            Code            = cc.Code,
            Language        = language,
            Caption         = cc.Caption,
            ShowLineNumbers = cc.ShowLineNumbers,
            WrapLines       = cc.WrapLines
        });
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleCodeCaptionSavedAsync(string? caption)
    {
        if (Block.Content is not ICodeBlockContent cc) return;
        var updated = BuildBlockWithContent(Block, new CodeBlockContent
        {
            Code            = cc.Code,
            Language        = cc.Language,
            Caption         = caption,
            ShowLineNumbers = cc.ShowLineNumbers,
            WrapLines       = cc.WrapLines
        });
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    // ── Child page (TmNotionChildPageBlock) callbacks ────────────────────────

    private async Task HandleChildPageNavigateAsync()
    {
        if (Block.Content is not IChildPageBlockContent cp) return;
        await NavigateToPageAsync(cp.ChildPageId);
    }

    private async Task HandleChildPageRenameCommittedAsync(string newTitle)
    {
        if (Block.Content is not IChildPageBlockContent cp) return;
        var updated = BuildBlockWithContent(Block, new ChildPageBlockContent
        {
            ChildPageId = cp.ChildPageId,
            Title       = newTitle,
            IconEmoji   = cp.IconEmoji
        });
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    // ── Linked page (TmNotionLinkedPageBlock) callbacks ──────────────────────

    private async Task HandleLinkedPageNavigateAsync()
    {
        if (Block.Content is not ILinkedPageBlockContent lp) return;
        await NavigateToPageAsync(lp.LinkedPageId);
    }

    // ── Navigation helper ────────────────────────────────────────────────────

    private Task NavigateToPageAsync(Guid pageId)
    {
        if (Context.NavigateTo is not null)
            return Context.NavigateTo(pageId.ToString());
        return Task.CompletedTask;
    }

    // ── Synced blocks callbacks ──────────────────────────────────────────────

    private async Task HandleSyncedOriginCopySyncIdAsync(Guid syncId)
    {
        try { await JS.InvokeVoidAsync("tmNotionEditor.copyText", syncId.ToString()); }
        catch { }
    }

    private async Task HandleSyncedRefUnsyncAsync(IPageBlock newBlock)
    {
        await OnUpdated.InvokeAsync(newBlock);
    }

    // ── Structured paste ─────────────────────────────────────────────────────

    /// <summary>
    /// Called from notion-editor.js when the clipboard carries HTML with more than one block
    /// element. The markup is imported into real page blocks — headings stay headings, a table
    /// stays a table — instead of being flattened into the block under the caret. Markup that
    /// yields a single block is left to the inline paste path, so pasting a formatted word in
    /// the middle of a sentence never splits it.
    /// </summary>
    [JSInvokable]
    public async Task OnHtmlPasted(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || !OnInsertTemplateBlocks.HasDelegate) return;

        var blocks = NotionHtmlImporter.Import(html, Block.PageId);
        if (blocks.Count == 0) return;

        // A lone paragraph belongs inside the block under the caret. Anything else — a table, a
        // code block, a rule — has no inline form and must become a block of its own.
        if (blocks.Count == 1 && blocks[0].Type == BlockType.Paragraph) return;

        await OnInsertTemplateBlocks.InvokeAsync(blocks);
    }

    // ── Template button (TmNotionTemplateButtonBlock) callbacks ──────────────

    private Task HandleInsertTemplateBlocksAsync(IReadOnlyList<IPageBlock> blocks) =>
        OnInsertTemplateBlocks.InvokeAsync(blocks);

    private async Task HandleTemplateButtonUpdatedAsync(TemplateButtonBlockContent content)
    {
        var updated = BuildBlockWithContent(Block, content);
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    // ── Image (TmNotionImageBlock) callbacks ──────────────────────────────────

    private async Task HandleImageMediaSetAsync((string? FileId, string? Url) media)
    {
        if (Block.Content is not IImageBlockContent img) return;
        var updated = BuildBlockWithContent(Block, new ImageBlockContent
        {
            Url       = media.Url ?? img.Url,
            FileId    = media.FileId ?? img.FileId,
            AltText   = img.AltText,
            Caption   = img.Caption,
            Width     = img.Width,
            Alignment = img.Alignment
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleImageWidthChangedAsync(int width)
    {
        if (Block.Content is not IImageBlockContent img) return;
        var updated = BuildBlockWithContent(Block, new ImageBlockContent
        {
            Url = img.Url, FileId = img.FileId, AltText = img.AltText,
            Caption = img.Caption, Width = width, Alignment = img.Alignment
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleImageCaptionSavedAsync(string? caption)
    {
        if (Block.Content is not IImageBlockContent img) return;
        var updated = BuildBlockWithContent(Block, new ImageBlockContent
        {
            Url = img.Url, FileId = img.FileId, AltText = img.AltText,
            Caption = caption, Width = img.Width, Alignment = img.Alignment
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleImageAlignmentChangedAsync(MediaAlignment alignment)
    {
        if (Block.Content is not IImageBlockContent img) return;
        var updated = BuildBlockWithContent(Block, new ImageBlockContent
        {
            Url = img.Url, FileId = img.FileId, AltText = img.AltText,
            Caption = img.Caption, Width = img.Width, Alignment = alignment
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── Video (TmNotionVideoBlock) callbacks ──────────────────────────────────

    private async Task HandleVideoMediaSetAsync((string? FileId, string? Url) media)
    {
        if (Block.Content is not IVideoBlockContent vid) return;
        var url = media.Url ?? vid.Url;
        var provider = !string.IsNullOrWhiteSpace(url) ? VideoProviderDetector.Detect(url) : vid.Provider;
        var updated = BuildBlockWithContent(Block, new VideoBlockContent
        {
            Url = url, FileId = media.FileId ?? vid.FileId,
            Provider = provider, Caption = vid.Caption, Width = vid.Width
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleVideoWidthChangedAsync(int width)
    {
        if (Block.Content is not IVideoBlockContent vid) return;
        var updated = BuildBlockWithContent(Block, new VideoBlockContent
        {
            Url = vid.Url, FileId = vid.FileId, Provider = vid.Provider,
            Caption = vid.Caption, Width = width
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleVideoCaptionSavedAsync(string? caption)
    {
        if (Block.Content is not IVideoBlockContent vid) return;
        var updated = BuildBlockWithContent(Block, new VideoBlockContent
        {
            Url = vid.Url, FileId = vid.FileId, Provider = vid.Provider,
            Caption = caption, Width = vid.Width
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── Audio (TmNotionAudioBlock) callbacks ──────────────────────────────────

    private async Task HandleAudioMediaSetAsync((string? FileId, string? Url) media)
    {
        if (Block.Content is not IAudioBlockContent aud) return;
        var url = media.Url ?? aud.Url;
        var provider = !string.IsNullOrWhiteSpace(url) ? AudioProviderDetector.Detect(url) : aud.Provider;
        var updated = BuildBlockWithContent(Block, new AudioBlockContent
        {
            Url = url, FileId = media.FileId ?? aud.FileId,
            Provider = provider, Caption = aud.Caption, Width = aud.Width
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleAudioCaptionSavedAsync(string? caption)
    {
        if (Block.Content is not IAudioBlockContent aud) return;
        var updated = BuildBlockWithContent(Block, new AudioBlockContent
        {
            Url = aud.Url, FileId = aud.FileId, Provider = aud.Provider,
            Caption = caption, Width = aud.Width
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── File (TmNotionFileBlock) callbacks ────────────────────────────────────

    private async Task HandleFileMediaSetAsync((string? FileId, string? Url) media)
    {
        if (Block.Content is not IFileBlockContent file) return;
        var updated = BuildBlockWithContent(Block, new FileBlockContent
        {
            Url = media.Url ?? file.Url, FileId = media.FileId ?? file.FileId,
            FileName = file.FileName, FileSizeBytes = file.FileSizeBytes,
            ContentType = file.ContentType, Caption = file.Caption, Width = file.Width
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleFileCaptionSavedAsync(string? caption)
    {
        if (Block.Content is not IFileBlockContent file) return;
        var updated = BuildBlockWithContent(Block, new FileBlockContent
        {
            Url = file.Url, FileId = file.FileId, FileName = file.FileName,
            FileSizeBytes = file.FileSizeBytes, ContentType = file.ContentType,
            Caption = caption, Width = file.Width
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── Bookmark (TmNotionBookmarkBlock) callbacks ────────────────────────────

    private async Task HandleBookmarkResolvedAsync(BookmarkBlockContent resolved)
    {
        var updated = BuildBlockWithContent(Block, resolved);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleBookmarkCaptionSavedAsync(string? caption)
    {
        if (Block.Content is not IBookmarkBlockContent bm) return;
        var updated = BuildBlockWithContent(Block, new BookmarkBlockContent
        {
            Url          = bm.Url,
            Title        = bm.Title,
            Description  = bm.Description,
            CoverImageUrl = bm.CoverImageUrl,
            FaviconUrl   = bm.FaviconUrl,
            Domain       = bm.Domain,
            Caption      = caption
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── Embed (TmNotionEmbedBlock) callbacks ──────────────────────────────────

    private async Task HandleEmbedUrlSetAsync(EmbedBlockContent embed)
    {
        if (Block.Content is IEmbedBlockContent ex)
        {
            embed = new EmbedBlockContent
            {
                Url     = embed.Url,
                Provider = embed.Provider,
                Width   = ex.Width,
                Height  = embed.Height ?? ex.Height,
                Caption = ex.Caption
            };
        }
        var updated = BuildBlockWithContent(Block, embed);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleEmbedResizedAsync((int W, int H) size)
    {
        if (Block.Content is not IEmbedBlockContent em) return;
        var updated = BuildBlockWithContent(Block, new EmbedBlockContent
        {
            Url     = em.Url,
            Provider = em.Provider,
            Width   = size.W,
            Height  = size.H,
            Caption = em.Caption
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleEmbedCaptionSavedAsync(string? caption)
    {
        if (Block.Content is not IEmbedBlockContent em) return;
        var updated = BuildBlockWithContent(Block, new EmbedBlockContent
        {
            Url     = em.Url,
            Provider = em.Provider,
            Width   = em.Width,
            Height  = em.Height,
            Caption = caption
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── PDF (TmNotionPdfBlock) callbacks ──────────────────────────────────────

    private async Task HandlePdfMediaSetAsync((string? FileId, string? Url) media)
    {
        if (Block.Content is not IPdfBlockContent pdf) return;
        var updated = BuildBlockWithContent(Block, new PdfBlockContent
        {
            Url = media.Url ?? pdf.Url, FileId = media.FileId ?? pdf.FileId,
            Caption = pdf.Caption, Width = pdf.Width
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandlePdfCaptionSavedAsync(string? caption)
    {
        if (Block.Content is not IPdfBlockContent pdf) return;
        var updated = BuildBlockWithContent(Block, new PdfBlockContent
        {
            Url = pdf.Url, FileId = pdf.FileId, Caption = caption, Width = pdf.Width
        });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── Diagram (TmNotionDiagramBlock) callbacks ──────────────────────────────

    private async Task HandleDiagramContentSavedAsync(DiagramBlockContent content)
    {
        var updated = BuildBlockWithContent(Block, content);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── Wireframe (TmNotionWireframeBlock) callbacks ──────────────────────────

    private async Task HandleWireframeContentSavedAsync(WireframeBlockContent content)
    {
        var updated = BuildBlockWithContent(Block, content);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleSpreadsheetContentSavedAsync(SpreadsheetBlockContent content)
    {
        var updated = BuildBlockWithContent(Block, content);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── Equation (TmNotionEquationBlock) callbacks ────────────────────────────

    private async Task HandleEquationExpressionSavedAsync(string expression)
    {
        var updated = BuildBlockWithContent(Block, new EquationBlockContent { Expression = expression });
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── Special content blocks ───────────────────────────────────────────────

    private async Task HandleChildrenDisplayContentChangedAsync(ChildrenDisplayBlockContent content)
    {
        var updated = BuildBlockWithContent(Block, content);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleExcerptContentChangedAsync(ExcerptBlockContent content)
    {
        var updated = BuildBlockWithContent(Block, content);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandlePagePropertiesContentChangedAsync(PagePropertiesBlockContent content)
    {
        var updated = BuildBlockWithContent(Block, content);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandlePagePropertiesReportContentChangedAsync(PagePropertiesReportBlockContent content)
    {
        var updated = BuildBlockWithContent(Block, content);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleContentByLabelContentChangedAsync(ContentByLabelBlockContent content)
    {
        var updated = BuildBlockWithContent(Block, content);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    private async Task HandleWorkItemContentChangedAsync(WorkItemBlockContent content)
    {
        var updated = BuildBlockWithContent(Block, content);
        try { await Context.BlockService.UpdateBlockAsync(updated); await OnUpdated.InvokeAsync(updated); }
        catch { }
    }

    // ── Handle button ─────────────────────────────────────────────────────────

    private Task HandleAddClickedAsync() =>
        OnAddAfter.InvokeAsync((BlockType.Paragraph, null));

    private Task HandleDividerAddAfterAsync() =>
        OnAddAfter.InvokeAsync((BlockType.Paragraph, null));

    // ── Paragraph (TmNotionTextBlock) callbacks ───────────────────────────────

    private async Task HandleParagraphContentSavedAsync(string html)
    {
        var updated = BuildBlockWithHtml(Block, html);
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            // A 409 is recorded on the session and then thrown. Swallowing it here used to
            // hide the page banner — surface the pending conflict without failing the keystroke.
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleParagraphEnterPressedAsync(string afterHtml) =>
        await OnAddAfter.InvokeAsync((BlockType.Paragraph, afterHtml));

    private Task HandleParagraphTabAsync(bool _) => Task.CompletedTask;

    private async Task HandleParagraphMarkdownShortcutAsync(string shortcut)
    {
        if (shortcut == "todoDone")
        {
            try
            {
                var converted = await Context.BlockService.ConvertBlockTypeAsync(Block.Id.ToString(), BlockType.TodoItem);

                // The "[x] " prefix has already been stripped by the shortcut handler; whatever
                // the user typed after it must survive the conversion.
                var carriedHtml = converted.Content is ITextBlockContent convertedText
                    ? convertedText.Html
                    : Block.Content is ITextBlockContent originalText ? originalText.Html : string.Empty;

                var updated = new PageBlock
                {
                    Id            = converted.Id,
                    PageId        = converted.PageId,
                    ParentBlockId = converted.ParentBlockId,
                    Type          = BlockType.TodoItem,
                    Order         = converted.Order,
                    Content       = new TodoBlockContent { Html = carriedHtml, IsChecked = true },
                    CreatedAt     = converted.CreatedAt,
                    LastEditedAt  = DateTime.UtcNow
                };

                await Context.BlockService.UpdateBlockAsync(updated);
                await OnUpdated.InvokeAsync(updated);
            }
            catch { }

            return;
        }

        var newType = shortcut switch
        {
            "heading1"  => BlockType.Heading1,
            "heading2"  => BlockType.Heading2,
            "heading3"  => BlockType.Heading3,
            "bullet"    => BlockType.BulletList,
            "numbered"  => BlockType.NumberedList,
            "todo"      => BlockType.TodoItem,
            "quote"     => BlockType.Quote,
            "code"      => BlockType.Code,
            "divider"   => BlockType.Divider,
            _           => Block.Type
        };
        if (newType != Block.Type)
            await OnConvertTo.InvokeAsync(newType);
    }

    private Task HandleParagraphSlashAsync((double Top, double Left) coords) =>
        OnSlashMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleParagraphMentionAsync((double Top, double Left) coords) =>
        OnMentionMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleParagraphPageLinkAsync((double Top, double Left) coords) =>
        OnPageLinkMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleParagraphTokenAsync((double Top, double Left) coords) =>
        OnTokenMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));

    // ── Heading (TmNotionHeadingBlock) callbacks ──────────────────────────────

    private async Task HandleHeadingContentSavedAsync(string html)
    {
        var updated = BuildBlockWithHtml(Block, html);
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleHeadingEnterPressedAsync(string afterHtml) =>
        await OnAddAfter.InvokeAsync((BlockType.Paragraph, afterHtml));

    private Task HandleHeadingTabAsync(bool _) => Task.CompletedTask;

    private async Task HandleHeadingMarkdownShortcutAsync(string shortcut)
    {
        var newType = shortcut switch
        {
            "heading1"  => BlockType.Heading1,
            "heading2"  => BlockType.Heading2,
            "heading3"  => BlockType.Heading3,
            "bullet"    => BlockType.BulletList,
            "numbered"  => BlockType.NumberedList,
            "todo"      => BlockType.TodoItem,
            "quote"     => BlockType.Quote,
            "code"      => BlockType.Code,
            "divider"   => BlockType.Divider,
            _           => Block.Type
        };
        if (newType != Block.Type)
            await OnConvertTo.InvokeAsync(newType);
    }

    private Task HandleHeadingSlashAsync((double Top, double Left) coords) =>
        OnSlashMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleHeadingMentionAsync((double Top, double Left) coords) =>
        OnMentionMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleHeadingPageLinkAsync((double Top, double Left) coords) =>
        OnPageLinkMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleHeadingTokenAsync((double Top, double Left) coords) =>
        OnTokenMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleHeadingToggleAsync(bool _) => Task.CompletedTask;

    // ── Quote (TmNotionQuoteBlock) callbacks ──────────────────────────────────

    private async Task HandleQuoteContentSavedAsync(string html)
    {
        var updated = BuildBlockWithHtml(Block, html);
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleQuoteEnterPressedAsync(string afterHtml) =>
        await OnAddAfter.InvokeAsync((BlockType.Quote, afterHtml));

    private Task HandleQuoteTabAsync(bool _) => Task.CompletedTask;

    private async Task HandleQuoteMarkdownShortcutAsync(string shortcut)
    {
        var newType = shortcut switch
        {
            "heading1"  => BlockType.Heading1,
            "heading2"  => BlockType.Heading2,
            "heading3"  => BlockType.Heading3,
            "bullet"    => BlockType.BulletList,
            "numbered"  => BlockType.NumberedList,
            "todo"      => BlockType.TodoItem,
            "code"      => BlockType.Code,
            "divider"   => BlockType.Divider,
            _           => Block.Type
        };
        if (newType != Block.Type)
            await OnConvertTo.InvokeAsync(newType);
    }

    private Task HandleQuoteSlashAsync((double Top, double Left) coords) =>
        OnSlashMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleQuoteMentionAsync((double Top, double Left) coords) =>
        OnMentionMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleQuotePageLinkAsync((double Top, double Left) coords) =>
        OnPageLinkMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleQuoteTokenAsync((double Top, double Left) coords) =>
        OnTokenMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));

    // ── Callout (TmNotionCalloutBlock) callbacks ──────────────────────────────

    private async Task HandleCalloutContentSavedAsync(string html)
    {
        var updated = BuildBlockWithHtml(Block, html);
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleCalloutEnterPressedAsync(string afterHtml) =>
        await OnAddAfter.InvokeAsync((BlockType.Callout, afterHtml));

    private Task HandleCalloutTabAsync(bool _) => Task.CompletedTask;

    private async Task HandleCalloutMarkdownShortcutAsync(string shortcut)
    {
        var newType = shortcut switch
        {
            "heading1" => BlockType.Heading1,
            "heading2" => BlockType.Heading2,
            "heading3" => BlockType.Heading3,
            "bullet"   => BlockType.BulletList,
            "numbered" => BlockType.NumberedList,
            "todo"     => BlockType.TodoItem,
            "quote"    => BlockType.Quote,
            "code"     => BlockType.Code,
            "divider"  => BlockType.Divider,
            _          => Block.Type
        };
        if (newType != Block.Type)
            await OnConvertTo.InvokeAsync(newType);
    }

    private Task HandleCalloutSlashAsync((double Top, double Left) coords) =>
        OnSlashMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleCalloutMentionAsync((double Top, double Left) coords) =>
        OnMentionMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleCalloutPageLinkAsync((double Top, double Left) coords) =>
        OnPageLinkMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleCalloutTokenAsync((double Top, double Left) coords) =>
        OnTokenMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));

    private async Task HandleCalloutEmojiChangedAsync(string? emoji)
    {
        if (Block.Content is not ICalloutBlockContent cc) return;
        var updated = BuildBlockWithContent(Block, new CalloutBlockContent
        {
            Html            = cc.Html,
            IconEmoji       = emoji,
            IconImageUrl    = cc.IconImageUrl,
            Variant         = cc.Variant,
            BackgroundColor = cc.BackgroundColor,
            TextColor       = cc.TextColor,
            Alignment       = cc.Alignment
        });
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    // ── BulletList (TmNotionBulletListBlock) callbacks ───────────────────────

    private async Task HandleBulletContentSavedAsync(string html)
    {
        var updated = BuildBlockWithHtml(Block, html);
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleBulletEnterSplitAsync(string afterHtml) =>
        await OnAddAfter.InvokeAsync((BlockType.BulletList, afterHtml));

    private async Task HandleBulletTabAsync(bool shiftKey)
    {
        if (Block.Content is not IListBlockContent list) return;
        var newIndent = NotionListIndent.Next(list.IndentLevel, outdent: shiftKey, PreviousListIndentLevel);
        if (newIndent == list.IndentLevel) return;
        var updated = BuildBlockWithContent(Block, new ListBlockContent
        {
            IndentLevel     = newIndent,
            Html            = list.Html,
            BackgroundColor = list.BackgroundColor,
            TextColor       = list.TextColor,
            Alignment       = list.Alignment
        });
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleBulletMarkdownShortcutAsync(string shortcut)
    {
        var newType = shortcut switch
        {
            "paragraph" => BlockType.Paragraph,
            "heading1"  => BlockType.Heading1,
            "heading2"  => BlockType.Heading2,
            "heading3"  => BlockType.Heading3,
            "numbered"  => BlockType.NumberedList,
            "todo"      => BlockType.TodoItem,
            "quote"     => BlockType.Quote,
            "code"      => BlockType.Code,
            "divider"   => BlockType.Divider,
            _           => Block.Type
        };
        if (newType != Block.Type)
            await OnConvertTo.InvokeAsync(newType);
    }

    private Task HandleBulletSlashAsync((double Top, double Left) coords) =>
        OnSlashMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleBulletMentionAsync((double Top, double Left) coords) =>
        OnMentionMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleBulletPageLinkAsync((double Top, double Left) coords) =>
        OnPageLinkMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleBulletTokenAsync((double Top, double Left) coords) =>
        OnTokenMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));

    // ── NumberedList (TmNotionNumberedListBlock) callbacks ───────────────────

    private async Task HandleNumberedContentSavedAsync(string html)
    {
        var updated = BuildBlockWithHtml(Block, html);
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleNumberedEnterSplitAsync(string afterHtml) =>
        await OnAddAfter.InvokeAsync((BlockType.NumberedList, afterHtml));

    private async Task HandleNumberedTabAsync(bool shiftKey)
    {
        if (Block.Content is not IListBlockContent list) return;
        var newIndent = NotionListIndent.Next(list.IndentLevel, outdent: shiftKey, PreviousListIndentLevel);
        if (newIndent == list.IndentLevel) return;
        var updated = BuildBlockWithContent(Block, new ListBlockContent
        {
            IndentLevel     = newIndent,
            Html            = list.Html,
            BackgroundColor = list.BackgroundColor,
            TextColor       = list.TextColor,
            Alignment       = list.Alignment
        });
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleNumberedMarkdownShortcutAsync(string shortcut)
    {
        var newType = shortcut switch
        {
            "paragraph" => BlockType.Paragraph,
            "heading1"  => BlockType.Heading1,
            "heading2"  => BlockType.Heading2,
            "heading3"  => BlockType.Heading3,
            "bullet"    => BlockType.BulletList,
            "todo"      => BlockType.TodoItem,
            "quote"     => BlockType.Quote,
            "code"      => BlockType.Code,
            "divider"   => BlockType.Divider,
            _           => Block.Type
        };
        if (newType != Block.Type)
            await OnConvertTo.InvokeAsync(newType);
    }

    private Task HandleNumberedSlashAsync((double Top, double Left) coords) =>
        OnSlashMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleNumberedMentionAsync((double Top, double Left) coords) =>
        OnMentionMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleNumberedPageLinkAsync((double Top, double Left) coords) =>
        OnPageLinkMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));
    private Task HandleNumberedTokenAsync((double Top, double Left) coords) =>
        OnTokenMenu.InvokeAsync((Block.Id.ToString(), coords.Top, coords.Left));

    // ── Handle context menu actions ───────────────────────────────────────────

    private async Task HandleDuplicateAsync() => await OnDuplicate.InvokeAsync(Block);

    private Task HandleMoveToAsync() => Task.CompletedTask;

    private async Task HandleCopyLinkAsync()
    {
        try
        {
            var fragment = $"#{Block.Id}";
            await JS.InvokeVoidAsync("tmNotionEditor.copyBlockLink", fragment);
        }
        catch { }
    }

    private async Task HandleCommentAsync() => await OnComment.InvokeAsync();

    private async Task HandleNewThreadAsync() => await OnNewThread.InvokeAsync();

    private Task HandleCommentThreadClickedAsync() => OnComment.InvokeAsync();

    private async Task HandleCalloutVariantChangedAsync(CalloutVariant variant)
    {
        if (Block.Content is not ICalloutBlockContent cc) return;

        var updated = BuildBlockWithContent(Block, new CalloutBlockContent
        {
            Html = cc.Html,
            IconEmoji = cc.IconEmoji,
            IconImageUrl = cc.IconImageUrl,
            Variant = variant,
            BackgroundColor = cc.BackgroundColor,
            TextColor = cc.TextColor,
            Alignment = cc.Alignment
        });

        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleTextColorChangeAsync(string? color)
    {
        if (Block.Content is not ITextBlockContent tc) return;
        var updated = BuildBlockWithContent(Block, UpdateTextBlockColor(tc, textColor: color));
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private async Task HandleBackgroundColorChangeAsync(string? color)
    {
        if (Block.Content is not ITextBlockContent tc) return;
        var updated = BuildBlockWithContent(Block, UpdateTextBlockColor(tc, backgroundColor: color));
        try
        {
            await Context.BlockService.UpdateBlockAsync(updated);
            await OnUpdated.InvokeAsync(updated);
        }
        catch
        {
            if (Context.AggregateSession?.HasPendingConflict == true)
                await Context.RaiseAggregateConflictDetectedAsync();
        }
    }

    private static IBlockContent UpdateTextBlockColor(ITextBlockContent src,
        string? textColor = null, string? backgroundColor = null) => src switch
    {
        IHeadingBlockContent hc  => new HeadingBlockContent
        {
            Html = hc.Html, Level = hc.Level, IsToggleable = hc.IsToggleable,
            TextColor = textColor ?? hc.TextColor,
            BackgroundColor = backgroundColor ?? hc.BackgroundColor,
            Alignment = hc.Alignment
        },
        ICalloutBlockContent cc  => new CalloutBlockContent
        {
            Html = cc.Html, IconEmoji = cc.IconEmoji, IconImageUrl = cc.IconImageUrl,
            Variant = cc.Variant,
            TextColor = textColor ?? cc.TextColor,
            BackgroundColor = backgroundColor ?? cc.BackgroundColor,
            Alignment = cc.Alignment
        },
        IListBlockContent lc     => new ListBlockContent
        {
            Html = lc.Html, IndentLevel = lc.IndentLevel,
            TextColor = textColor ?? lc.TextColor,
            BackgroundColor = backgroundColor ?? lc.BackgroundColor,
            Alignment = lc.Alignment
        },
        ITodoBlockContent tc2    => new TodoBlockContent
        {
            Html = tc2.Html, IsChecked = tc2.IsChecked,
            AssigneeId = tc2.AssigneeId,
            AssigneeDisplayName = tc2.AssigneeDisplayName,
            DueDate = tc2.DueDate,
            IsOverdue = IsTodoOverdue(tc2.DueDate, tc2.IsChecked),
            TextColor = textColor ?? tc2.TextColor,
            BackgroundColor = backgroundColor ?? tc2.BackgroundColor,
            Alignment = tc2.Alignment
        },
        IToggleBlockContent tg   => new ToggleBlockContent
        {
            Html = tg.Html, IsOpen = tg.IsOpen,
            TextColor = textColor ?? tg.TextColor,
            BackgroundColor = backgroundColor ?? tg.BackgroundColor,
            Alignment = tg.Alignment
        },
        _                        => new TextBlockContent
        {
            Html = src.Html,
            TextColor = textColor ?? src.TextColor,
            BackgroundColor = backgroundColor ?? src.BackgroundColor,
            Alignment = src.Alignment
        }
    };

    // ── CSS helpers ───────────────────────────────────────────────────────────

    private static string AlignClass(ITextBlockContent? c) => c?.Alignment switch
    {
        TextAlignment.Center => "tm-notion-align-center",
        TextAlignment.Right  => "tm-notion-align-right",
        _                    => string.Empty
    };

    private static string BgClass(ITextBlockContent? c) =>
        string.IsNullOrEmpty(c?.BackgroundColor) ? string.Empty : $"tm-notion-bg-{NotionColorTokens.Name(c.BackgroundColor)}";

    // ── Block content helpers ─────────────────────────────────────────────────


    private static PageBlock BuildBlockWithHtml(IPageBlock src, string html) =>
        NotionBlockContentFactory.WithHtml(src, html);

    private static PageBlock BuildBlockWithContent(IPageBlock src, IBlockContent content) =>
        NotionBlockContentFactory.WithContent(src, content);

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }

    private static bool IsTodoOverdue(DateTime? dueDate, bool isChecked) =>
        !isChecked && dueDate is DateTime date && date.Date < DateTime.Today;

}
