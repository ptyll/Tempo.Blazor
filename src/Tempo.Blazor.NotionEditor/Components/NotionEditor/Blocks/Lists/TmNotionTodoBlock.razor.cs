using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Lists;

/// <summary>
/// Self-contained todo (checkbox) list item. Owns its JS keyboard-handler lifecycle.
/// Checkbox click fires OnCheckedChanged so the parent persists the new IsChecked value.
/// Enter on an empty item fires OnTypeConvert("paragraph").
/// Enter on a non-empty item fires OnEnterSplit to create a new sibling todo.
/// </summary>
public partial class TmNotionTodoBlock : ComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public ITodoBlockContent? Content     { get; set; }
    [Parameter] public bool               ReadOnly    { get; set; }
    [Parameter] public bool               IsFocused   { get; set; }
    [Parameter] public string?            Placeholder { get; set; }

    /// <summary>Fired when the checkbox is toggled. Arg = new checked state.</summary>
    [Parameter] public EventCallback<bool>                      OnCheckedChanged  { get; set; }

    /// <summary>Fired when the todo assignee changes. Args = assignee ID and display name.</summary>
    [Parameter] public EventCallback<(string? AssigneeId, string? AssigneeDisplayName)> OnAssigneeChanged { get; set; }

    /// <summary>Fired when the todo due date changes. Arg = selected due date or null.</summary>
    [Parameter] public EventCallback<DateTime?> OnDueDateChanged { get; set; }

    /// <summary>Fired on blur when HTML has changed. Arg = new HTML.</summary>
    [Parameter] public EventCallback<string>                    OnContentSaved    { get; set; }

    /// <summary>Fired when Enter is pressed on a non-empty item. Arg = HTML after the split point.</summary>
    [Parameter] public EventCallback<string>                    OnEnterSplit      { get; set; }

    /// <summary>Fired when Backspace is pressed on an empty item.</summary>
    [Parameter] public EventCallback                            OnDeleteRequested { get; set; }

    /// <summary>
    /// Raised when Backspace is pressed at the start of a non-empty block. The payload is the
    /// block's current, sanitized HTML, which the previous block absorbs before this one is deleted.
    /// </summary>
    [Parameter] public EventCallback<string>                    OnMergeWithPrevious { get; set; }

    /// <summary>
    /// Raised when pasted HTML carries more than one block element. The payload is the raw
    /// clipboard HTML; the consumer turns it into page blocks.
    /// </summary>
    [Parameter] public EventCallback<string>                    OnStructuredPaste { get; set; }

    /// <summary>
    /// Fired when a markdown or conversion shortcut is detected.
    /// Special value "paragraph" means the user pressed Enter on an empty item.
    /// </summary>
    [Parameter] public EventCallback<string>                    OnTypeConvert     { get; set; }

    /// <summary>Fired when the editable receives focus.</summary>
    [Parameter] public EventCallback                            OnFocused         { get; set; }

    /// <summary>Fired when '/' is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnSlashMenu       { get; set; }

    /// <summary>Fired when '@' is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnMentionMenu     { get; set; }

    /// <summary>Fired when '[[' is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnPageLinkMenu    { get; set; }

    /// <summary>Fired when '{{' token syntax is typed. Args = (top, left) caret coords.</summary>
    [Parameter] public EventCallback<(double Top, double Left)> OnTokenMenu       { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private ElementReference                             _editableRef;
    private DotNetObjectReference<TmNotionTodoBlock>?   _dotNetRef;
    private bool                                        _kbInitialized;
    private bool                                        _dirty;
    private string?                                     _lastHtml;
    private string?                                     _lastPropHtml;
    private ITodoBlockContent?                          _lastContent;
    private bool                                        _isChecked;
    private bool                                        _showAssigneePicker;
    private bool                                        _showDatePicker;
    private string                                      _assigneeQuery = string.Empty;
    private IReadOnlyList<TmUser>                       _assignees = [];
    private bool                                        _loadingAssignees;

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Computed CSS ─────────────────────────────────────────────────────────

    private string _alignClass => Content?.Alignment switch
    {
        TextAlignment.Center => "tm-notion-align-center",
        TextAlignment.Right  => "tm-notion-align-right",
        _                    => string.Empty
    };

    private string _rootClass =>
        $"tm-notion-todo {(_isChecked ? "tm-notion-todo--checked" : string.Empty)} {(IsOverdue ? "tm-notion-todo--overdue" : string.Empty)} {_bgClass}";

    private string _bgClass =>
        string.IsNullOrEmpty(Content?.BackgroundColor)
            ? string.Empty
            : $"tm-notion-bg-{Content.BackgroundColor}";

    private bool HasAssignee =>
        !string.IsNullOrWhiteSpace(Content?.AssigneeDisplayName) ||
        !string.IsNullOrWhiteSpace(Content?.AssigneeId);

    private bool HasDueDate => Content?.DueDate is not null;

    private bool IsOverdue =>
        !_isChecked &&
        Content?.DueDate is DateTime dueDate &&
        dueDate.Date < DateTime.Today;

    private DateOnly? DueDateOnly =>
        Content?.DueDate is DateTime dueDate ? DateOnly.FromDateTime(dueDate) : null;

    private string DueClass
    {
        get
        {
            var css = "tm-notion-todo__due";
            if (IsOverdue)
                return $"{css} tm-notion-todo__due--overdue";

            if (Content?.DueDate is not DateTime dueDate)
                return css;

            if (dueDate.Date == DateTime.Today)
                return $"{css} tm-notion-todo__due--today";

            if (dueDate.Date == DateTime.Today.AddDays(1))
                return $"{css} tm-notion-todo__due--tomorrow";

            return css;
        }
    }

    private string DueText
    {
        get
        {
            if (Content?.DueDate is not DateTime dueDate)
                return string.Empty;

            return IsOverdue
                ? $"{Loc["Notion_Todo_Overdue"]} · {dueDate:d}"
                : dueDate.ToString("d");
        }
    }

    private string AssigneeDisplayName =>
        !string.IsNullOrWhiteSpace(Content?.AssigneeDisplayName)
            ? Content.AssigneeDisplayName
            : Content?.AssigneeId ?? string.Empty;

    private string AssigneeInitials
    {
        get
        {
            return UserInitials(AssigneeDisplayName);
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(Content, _lastContent)) return;
        _lastContent   = Content;
        _lastPropHtml  = Content?.Html;
        _isChecked     = Content?.IsChecked ?? false;
        _dirty         = false;
        _kbInitialized = false;
        _lastHtml      = null;
        _showAssigneePicker = false;
        _showDatePicker = false;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (ReadOnly) return;

        var html = Content?.Html ?? string.Empty;

        if (!_kbInitialized)
        {
            _kbInitialized = true;
            _lastHtml      = html;
            _dotNetRef?.Dispose();
            _dotNetRef = DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("tmNotionEditor.initKeyboardHandler", _editableRef, _dotNetRef);
                // A re-init can follow a Content-instance swap while the editable still
                // holds unsaved DOM text (e.g. Enter-split keystrokes replayed by
                // focusAtStart); the JS dirty flag is the only trustworthy witness.
                if (await JS.InvokeAsync<bool>("tmNotionEditor.isEditableDirty", _editableRef))
                    _dirty = true;
                else
                    await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _editableRef, SanitizeForRender(html));
                if (IsFocused)
                    await JS.InvokeVoidAsync("tmNotionEditor.focusAtStart", _editableRef);
            }
            catch { }
        }
        else if (!_dirty && html != _lastHtml)
        {
            // The dirty flag only tracks `input` events. DOM surgery done elsewhere can leave
            // unsaved edits behind, so compare the live DOM before overwriting it.
            if (await HasUnsavedDomEditsAsync())
            {
                _dirty = true;
                return;
            }

            // A same-instance prop can only legitimately advance via in-place mutation.
            // When it still carries the value we already consumed, this render is stale
            // (e.g. queued before an Enter-split setHtml) and writing it would revert live edits.
            if (html != _lastPropHtml)
            {
                _lastHtml     = html;
                _lastPropHtml = html;
                try { await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _editableRef, SanitizeForRender(html)); }
                catch { }
            }
        }
    }

    // ── Checkbox ──────────────────────────────────────────────────────────────

    private async Task HandleCheckboxChangeAsync(ChangeEventArgs e)
    {
        _isChecked = !_isChecked;
        await OnCheckedChanged.InvokeAsync(_isChecked);
    }

    // ── Blur / focus ──────────────────────────────────────────────────────────

    private async Task OnBlurAsync()
    {
        if (!_dirty || ReadOnly) return;
        try
        {
            var html = await JS.InvokeAsync<string>("tmNotionEditor.getHtml", _editableRef);
            var sanitized = NotionInlineHtmlSanitizer.SanitizeBlockContent(html);
            _lastHtml = sanitized;
            await OnContentSaved.InvokeAsync(sanitized);
        }
        catch { }
        finally
        {
            _dirty = false;
        }
    }

    private async Task HandleFocusAsync() => await OnFocused.InvokeAsync();

    private async Task ToggleAssigneePickerAsync()
    {
        if (ReadOnly || Context.MentionProvider is null)
            return;

        _showAssigneePicker = !_showAssigneePicker;
        _showDatePicker = false;
        if (_showAssigneePicker)
            await LoadAssigneesAsync();
    }

    private void ToggleDatePicker()
    {
        if (ReadOnly)
            return;

        _showDatePicker = !_showDatePicker;
        _showAssigneePicker = false;
    }

    private async Task HandleAssigneeSearchAsync(ChangeEventArgs args)
    {
        _assigneeQuery = args.Value?.ToString() ?? string.Empty;
        await LoadAssigneesAsync();
    }

    private async Task LoadAssigneesAsync()
    {
        if (Context.MentionProvider is null)
        {
            _assignees = [];
            return;
        }

        _loadingAssignees = true;
        try
        {
            _assignees = await Context.MentionProvider.SearchAsync(new TmPeopleQuery { SearchText = _assigneeQuery, Take = 8 });
        }
        finally
        {
            _loadingAssignees = false;
        }
    }

    private async Task SelectAssigneeAsync(TmUser user)
    {
        _showAssigneePicker = false;
        await OnAssigneeChanged.InvokeAsync((user.Id, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Id : user.DisplayName));
    }

    private async Task ClearAssigneeAsync()
    {
        _showAssigneePicker = false;
        await OnAssigneeChanged.InvokeAsync((null, null));
    }

    private async Task HandleDueDateSelectedAsync(DateOnly? dueDate)
    {
        _showDatePicker = false;
        await OnDueDateChanged.InvokeAsync(dueDate.HasValue
            ? dueDate.Value.ToDateTime(TimeOnly.MinValue)
            : null);
    }

    // ── JS keyboard callbacks — names MUST match notion-editor.js ─────────────

    [JSInvokable]
    public async Task OnEnterPressed(string beforeHtml, string afterHtml)
    {
        if (IsEmptyHtml(beforeHtml))
        {
            await OnTypeConvert.InvokeAsync("paragraph");
            return;
        }

        var before = NotionInlineHtmlSanitizer.SanitizeBlockContent(beforeHtml);
        _lastHtml = before;
        _dirty    = false;

        // Write the left-hand half back explicitly. Relying on the save producing a new Content
        // reference would leave the source block showing the whole pre-split text whenever the
        // provider hands the same instance back.
        try { await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _editableRef, before); }
        catch { }

        await OnContentSaved.InvokeAsync(before);
        await OnEnterSplit.InvokeAsync(NotionInlineHtmlSanitizer.SanitizeBlockContent(afterHtml));
    }

    [JSInvokable]
    public async Task OnBackspaceOnEmpty() => await OnDeleteRequested.InvokeAsync();

    /// <summary>
    /// Called from notion-editor.js when Backspace is pressed while the caret sits before the
    /// first character. Blocks used without a merge consumer simply keep their text.
    /// </summary>
    [JSInvokable]
    public async Task OnBackspaceAtStart(string html)
    {
        if (!OnMergeWithPrevious.HasDelegate) return;
        await OnMergeWithPrevious.InvokeAsync(NotionInlineHtmlSanitizer.SanitizeBlockContent(html));
    }

    /// <summary>
    /// Called from notion-editor.js when the clipboard HTML has several block elements. Blocks
    /// used without a consumer fall back to the inline paste that JS already performed.
    /// </summary>
    [JSInvokable]
    public async Task OnHtmlPasted(string html)
    {
        if (OnStructuredPaste.HasDelegate) await OnStructuredPaste.InvokeAsync(html);
    }

    [JSInvokable]
    public void OnTabPressed(bool shiftKey) { /* Todo items do not support indentation */ }

    [JSInvokable]
    public void OnArrowUp() { }

    [JSInvokable]
    public void OnArrowDown() { }

    [JSInvokable]
    public async Task OnMarkdownShortcut(string shortcut) =>
        await OnTypeConvert.InvokeAsync(shortcut);

    [JSInvokable]
    public async Task OnSlashTriggered(double top, double left) =>
        await OnSlashMenu.InvokeAsync((top, left));

    [JSInvokable]
    public async Task OnMentionTriggered(double top, double left) =>
        await OnMentionMenu.InvokeAsync((top, left));

    [JSInvokable]
    public async Task OnPageLinkTriggered(double top, double left) =>
        await OnPageLinkMenu.InvokeAsync((top, left));

    [JSInvokable]
    public async Task OnTokenTriggered(double top, double left) =>
        await OnTokenMenu.InvokeAsync((top, left));

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_kbInitialized)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.destroyBlock", _editableRef); }
            catch { }
        }
        _dotNetRef?.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsEmptyHtml(string html) =>
        string.IsNullOrWhiteSpace(html) || html.Trim() is "" or "<br>" or "<br/>" or "&nbsp;";

    private static string UserInitials(string? displayName)
    {
        var name = displayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 1
            ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
            : string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    /// <summary>
    /// Block content is written into the DOM with innerHTML, so a stored onerror payload would run
    /// on render. Sanitize on the way in and on the way out.
    /// </summary>
    private static string SanitizeForRender(string html) =>
        NotionInlineHtmlSanitizer.SanitizeBlockContent(html);

    /// <summary>True when the DOM holds edits the dirty flag never saw (JS-driven DOM surgery).</summary>
    private async Task<bool> HasUnsavedDomEditsAsync()
    {
        try
        {
            var live = await JS.InvokeAsync<string>("tmNotionEditor.getHtml", _editableRef);
            return _lastHtml is not null && live != _lastHtml;
        }
        catch
        {
            return false;
        }
    }
}
