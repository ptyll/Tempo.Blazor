// Code-behind for TmRichEditorFull.razor. The @code block moved here in 2.9.1: the component
// outgrew the 1200-line source ceiling (SourceFileLengthTests, F13/N270), so the logic lives in
// this partial class and the .razor keeps only the markup and directives.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Models;
using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Components.Activity;

public partial class TmRichEditorFull
{
    /// <summary>
    /// The HTML content of the editor.
    /// </summary>
    [Parameter] public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Callback when the value changes.
    /// </summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>
    /// Placeholder text when empty.
    /// </summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>
    /// Height of the editor (e.g., "400px").
    /// </summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>
    /// Maximum length of content.
    /// </summary>
    [Parameter] public int MaxLength { get; set; }

    /// <summary>
    /// Whether to show word count.
    /// </summary>
    [Parameter] public bool ShowWordCount { get; set; } = true;

    /// <summary>
    /// Whether mentions are supported.
    /// </summary>
    [Parameter] public bool SupportsMentions { get; set; }

    /// <summary>
    /// Data provider for mentions.
    /// </summary>
    [Parameter] public ITmPeopleProvider? MentionProvider { get; set; }

    /// <summary>
    /// Whether images are supported.
    /// </summary>
    [Parameter] public bool SupportsImages { get; set; } = true;

    /// <summary>
    /// Whether tables are supported.
    /// </summary>
    [Parameter] public bool SupportsTables { get; set; } = true;

    /// <summary>
    /// Whether code blocks are supported.
    /// </summary>
    [Parameter] public bool SupportsCodeBlocks { get; set; } = true;

    /// <summary>
    /// Whether blockquotes are supported.
    /// </summary>
    [Parameter] public bool SupportsBlockquotes { get; set; } = true;

    /// <summary>
    /// Whether tokens/variables are supported.
    /// </summary>
    [Parameter] public bool SupportsTokens { get; set; }

    /// <summary>
    /// Data provider for token search.
    /// </summary>
    [Parameter] public ITokenDataProvider? TokenProvider { get; set; }

    /// <summary>
    /// Trigger string that activates the token dropdown (default: "{{").
    /// </summary>
    [Parameter] public string TokenTrigger { get; set; } = "{{";

    /// <summary>
    /// Callback when the user requests to create a new token. Parameter is the pre-filled name from query.
    /// </summary>
    [Parameter] public EventCallback<string?> OnTokenCreateRequested { get; set; }

    /// <summary>
    /// Callback when a token is inserted into the editor.
    /// </summary>
    [Parameter] public EventCallback<IToken> OnTokenInserted { get; set; }

    /// <summary>
    /// Callback when the user hovers over a token chip in the editor.
    /// Receives <see cref="TokenHoverArgs"/> with full token metadata and the chip's bounding rect
    /// in client (viewport) coordinates, so the application can position its own tooltip.
    /// Only fires when <see cref="TokenProvider"/> is set.
    /// </summary>
    [Parameter] public EventCallback<TokenHoverArgs> OnTokenHover { get; set; }

    /// <summary>
    /// Callback when the user moves the cursor away from a token chip.
    /// Use this to hide the tooltip shown in response to <see cref="OnTokenHover"/>.
    /// </summary>
    [Parameter] public EventCallback OnTokenHoverEnd { get; set; }

    /// <summary>
    /// Whether the editor is disabled.
    /// </summary>
    [Parameter] public bool IsDisabled { get; set; }

    /// <summary>
    /// Additional CSS class for the container.
    /// </summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Additional attributes for the editor container.
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private ElementReference _editorRef;

    /// <summary>Stable per-instance id of the content surface; the dialogs restore focus to it.</summary>
    private string _editorId { get; } = $"tm-rte-editor-{Guid.NewGuid().ToString("N")[..8]}";

    private List<string> _activeFormats = new();
    private bool _isEmpty = true;
    private int _charCount = 0;
    private int _wordCount = 0;
    private bool _showLinkDialog;
    private bool _showImageDialog;
    private bool _showTableDialog;
    private bool _showVideoDialog;
    private bool _showFindReplaceDialog;
    private bool _showMentionDropdown;
    private string _mentionQuery = string.Empty;
    private int _highlightedMentionIndex;
    private List<MentionUser> _mentionUsers = new();
    private string _selectedText = string.Empty;
    private DotNetObjectReference<TmRichEditorFull>? _dotNetRef;
    private bool _isInitialized = false;
    private bool _showTokenDropdown;
    private string _tokenQuery = string.Empty;
    private int _tokenHighlightedIndex;
    private List<TokenItem> _availableTokens = new();
    private bool _isLoadingTokens;
    private bool _tokenOpenedViaTrigger;
    private bool _suppressKeyDown;
    private bool _isInsertingToken;

    // FZE-10 review: the dialog UNMOUNTS before the caret bookmark is replayed and the insert
    // runs. The TmModal focus trap owns inert/focus while the dialog is rendered — moving the
    // selection back into the editor underneath an open trap is exactly the bug this fixes. So
    // the confirm/cancel handlers only close the dialog, stash what should happen next, and
    // OnAfterRenderAsync replays it once the render WITHOUT the dialog completed.
    private Func<Task>? _pendingAfterDialogClose;
    private bool _restoreCaretAfterDialogClose;

    private bool IsOverLimit => MaxLength > 0 && _charCount > MaxLength;

    /// <summary>
    /// True while a dialog is open. The caret bookmark is a JS-side Range record (a WeakMap in
    /// richEditor.js), but the getHtml/setHtml sync must still not run while a dialog is open:
    /// the find/replace works on the live DOM and the Value the parent holds may be stale
    /// relative to it — an external setHtml here would wipe the user's replaces (the same
    /// guard <c>_isInsertingToken</c> provides for the token flow).
    /// </summary>
    private bool AnyDialogOpen => _showLinkDialog || _showImageDialog || _showTableDialog
        || _showVideoDialog || _showFindReplaceDialog;

    private string GetHeightStyle()
    {
        return !string.IsNullOrEmpty(Height) ? $"height: {Height};" : "";
    }

    private void UpdateCounts(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            _charCount = 0;
            _wordCount = 0;
            _isEmpty = true;
            return;
        }

        // Strip HTML tags
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ");
        _charCount = text.Length;
        _wordCount = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        _isEmpty = string.IsNullOrWhiteSpace(text) || html == "<br>" || html == "<br/>" || html == "<br />" || html == "<div><br></div>";
    }

    protected override void OnParametersSet()
    {
        UpdateCounts(Value);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            try
            {
                // Initialize content from Value parameter
                if (!string.IsNullOrEmpty(Value))
                {
                    await JS.InvokeVoidAsync("tmRichEditor.setHtml", _editorRef, Value);
                }
                await JS.InvokeVoidAsync("tmRichEditor.initKeyboardShortcuts", _editorRef);
                if (TokenProvider != null && (OnTokenHover.HasDelegate || OnTokenHoverEnd.HasDelegate))
                {
                    await JS.InvokeVoidAsync("tmRichEditor.initTokenHoverPreview", _editorRef, _dotNetRef);
                }
                _isInitialized = true;
            }
            catch { /* JS not available in tests / SSR */ }
        }
        else if (!AnyDialogOpen && (_pendingAfterDialogClose is not null || _restoreCaretAfterDialogClose))
        {
            // The dialog just unmounted — the focus trap released the editor, so the caret
            // bookmark can be replayed and the stashed insert can run. Both interop calls are
            // best-effort: a JSException/JSDisconnectedException here runs on the render
            // pipeline and would kill a Blazor Server circuit.
            var restore = _restoreCaretAfterDialogClose;
            var insert = _pendingAfterDialogClose;
            _restoreCaretAfterDialogClose = false;
            _pendingAfterDialogClose = null;
            if (restore)
            {
                await RestoreCursorPositionAsync();
            }
            if (insert is not null)
            {
                try
                {
                    await insert();
                }
                catch (JSException) { }
                catch (JSDisconnectedException) { }
                catch (TaskCanceledException) { }
            }
        }
        else if (_isInitialized && !_isInsertingToken && !AnyDialogOpen)
        {
            // Sync external Value changes to editor (only if different from current)
            try
            {
                var currentHtml = await JS.InvokeAsync<string>("tmRichEditor.getHtml", _editorRef);
                if (currentHtml != Value)
                {
                    await JS.InvokeVoidAsync("tmRichEditor.setHtml", _editorRef, Value ?? "");
                }
            }
            catch { }
        }
    }

    private async Task HandleInput()
    {
        if (_isInsertingToken) return;
        try
        {
            var html = await JS.InvokeAsync<string>("tmRichEditor.getHtml", _editorRef);
            Value = html ?? string.Empty;
            UpdateCounts(Value);
            await ValueChanged.InvokeAsync(Value);

            // Update token query if dropdown is open
            if (_showTokenDropdown && SupportsTokens)
            {
                await UpdateTokenQueryAsync();
            }
            // Detect {{ token trigger from actual content (works with any input method)
            else if (SupportsTokens && !_showTokenDropdown)
            {
                var query = await JS.InvokeAsync<string?>("tmRichEditor.getTextBeforeCursorForToken", _editorRef, TokenTrigger);
                if (query != null)
                {
                    _tokenQuery = query;
                    _showTokenDropdown = true;
                    _tokenOpenedViaTrigger = true;
                    _tokenHighlightedIndex = 0;
                    await LoadTokensAsync();
                }
            }

            await InvokeAsync(StateHasChanged);
        }
        catch { /* JS not available in tests / SSR */ }
    }

    private async Task HandleFormat(string format)
    {
        if (IsDisabled) return;

        // Handle dialog-based formats
        switch (format)
        {
            case "link":
                _selectedText = await GetSelectedTextAsync();
                await SaveCursorPositionAsync();
                _showLinkDialog = true;
                await InvokeAsync(StateHasChanged);
                return;
            case "image":
                if (SupportsImages)
                {
                    await SaveCursorPositionAsync();
                    _showImageDialog = true;
                    await InvokeAsync(StateHasChanged);
                }
                return;
            case "table":
                if (SupportsTables)
                {
                    await SaveCursorPositionAsync();
                    _showTableDialog = true;
                    await InvokeAsync(StateHasChanged);
                }
                return;
            case "video":
                await SaveCursorPositionAsync();
                _showVideoDialog = true;
                await InvokeAsync(StateHasChanged);
                return;
            case "findReplace":
                await SaveCursorPositionAsync();
                _showFindReplaceDialog = true;
                await InvokeAsync(StateHasChanged);
                return;
        }

        // Handle task list
        if (format == "taskList")
        {
            try
            {
                await JS.InvokeVoidAsync("tmRichEditor.toggleTaskList");
            }
            catch { }
            await UpdateActiveFormats();
            return;
        }

        // Handle color formats
        if (format.StartsWith("foreColor:") || format.StartsWith("hiliteColor:"))
        {
            var parts = format.Split(':');
            if (parts.Length == 2)
            {
                try
                {
                    await JS.InvokeVoidAsync("tmRichEditor.execCommand", parts[0], parts[1]);
                }
                catch { }
            }
            return;
        }

        // Handle horizontal rule
        if (format == "horizontalRule")
        {
            try
            {
                await JS.InvokeVoidAsync("tmRichEditor.insertHorizontalRule");
            }
            catch { }
            return;
        }

        // Standard format commands with UI toggle
        if (_activeFormats.Contains(format))
        {
            _activeFormats.Remove(format);
        }
        else
        {
            // For mutually exclusive formats (headings), remove others
            if (format.StartsWith("h") && format.Length == 2)
            {
                _activeFormats.RemoveAll(f => f.StartsWith("h") && f.Length == 2);
            }
            _activeFormats.Add(format);
        }

        try
        {
            await JS.InvokeVoidAsync("tmRichEditor.execCommand", format);
        }
        catch { /* JS not available in tests / SSR */ }

        // Note: StateHasChanged is intentionally not called here
        // to avoid re-rendering that would lose editor focus.
        // The UI state (active formats) is already updated above.
    }

    private async Task HandleEmojiSelected(string emoji)
    {
        if (IsDisabled) return;

        try
        {
            await JS.InvokeVoidAsync("tmRichEditor.insertEmoji", emoji);
            await HandleInput(); // Update content
        }
        catch { /* JS not available */ }
    }

    private async Task UpdateActiveFormats()
    {
        var formats = new[] { "bold", "italic", "underline", "strikethrough", "insertUnorderedList", "insertOrderedList", "h1", "h2", "h3", "h4", "blockquote", "code" };
        _activeFormats.Clear();
        
        foreach (var format in formats)
        {
            try
            {
                var isActive = await JS.InvokeAsync<bool>("tmRichEditor.queryCommandState", format);
                if (isActive)
                {
                    _activeFormats.Add(format);
                }
            }
            catch { }
        }
        
        await InvokeAsync(StateHasChanged);
    }

    private async Task<string> GetSelectedTextAsync()
    {
        try
        {
            return await JS.InvokeAsync<string>("tmRichEditor.getSelectedText") ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void HandleFocus()
    {
        // Handle focus event
    }

    private void HandleBlur()
    {
        // Handle blur event
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        _suppressKeyDown = false;

        // Handle token dropdown navigation
        if (_showTokenDropdown)
        {
            switch (e.Key)
            {
                case "ArrowUp":
                    _suppressKeyDown = true;
                    await MoveTokenHighlightUp();
                    return;
                case "ArrowDown":
                    _suppressKeyDown = true;
                    await MoveTokenHighlightDown();
                    return;
                case "Enter":
                    _suppressKeyDown = true;
                    await SelectHighlightedToken();
                    return;
                case "Escape":
                    _suppressKeyDown = true;
                    _showTokenDropdown = false;
                    return;
            }

            // Continue typing updates query
            if (e.Key.Length == 1 && !e.CtrlKey && !e.MetaKey)
            {
                // Query will be updated via HandleInput -> UpdateTokenQueryAsync
            }
        }
        // Handle mention navigation
        else if (_showMentionDropdown)
        {
            switch (e.Key)
            {
                case "ArrowUp":
                    _suppressKeyDown = true;
                    await MoveMentionHighlightUp();
                    break;
                case "ArrowDown":
                    _suppressKeyDown = true;
                    await MoveMentionHighlightDown();
                    break;
                case "Enter":
                    _suppressKeyDown = true;
                    await SelectHighlightedMention();
                    break;
                case "Escape":
                    _suppressKeyDown = true;
                    _showMentionDropdown = false;
                    break;
            }
        }
        // Check for @ mention trigger
        else if (e.Key == "@" && SupportsMentions)
        {
            _mentionQuery = "";
            _showMentionDropdown = true;
            _highlightedMentionIndex = 0;
            await LoadMentionUsersAsync();
        }
        // Ctrl+F for find
        else if (e.Key == "f" && (e.CtrlKey || e.MetaKey))
        {
            await SaveCursorPositionAsync();
            _showFindReplaceDialog = true;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadMentionUsersAsync()
    {
        if (MentionProvider == null) return;
        
        var users = await MentionProvider.SearchAsync(new TmPeopleQuery { SearchText = _mentionQuery, Take = 8 });
        _mentionUsers = users.Select(ToMentionUser).ToList();
        _showMentionDropdown = _mentionUsers.Count > 0;
        await InvokeAsync(StateHasChanged);
    }

    private async Task MoveMentionHighlightUp()
    {
        if (_highlightedMentionIndex > 0)
        {
            _highlightedMentionIndex--;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task MoveMentionHighlightDown()
    {
        if (_highlightedMentionIndex < _mentionUsers.Count - 1)
        {
            _highlightedMentionIndex++;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SelectHighlightedMention()
    {
        if (_highlightedMentionIndex >= 0 && _highlightedMentionIndex < _mentionUsers.Count)
        {
            await OnMentionSelected(_mentionUsers[_highlightedMentionIndex]);
        }
    }

    /// <summary>
    /// Saves the caret/selection as a DOM bookmark (richEditor.js) so the insertion or the caret
    /// can land at the same spot after the modal dialog closed and took the focus with it.
    /// </summary>
    private async Task SaveCursorPositionAsync()
    {
        try { await JS.InvokeVoidAsync("tmRichEditor.saveCursorPosition", _editorRef); }
        catch { /* JS not available in tests / SSR */ }
    }

    /// <summary>
    /// Restores focus into the editor and replays the saved caret/selection. Called on every
    /// dialog close path — before the insertion on confirm, alone on cancel.
    /// </summary>
    private async Task RestoreCursorPositionAsync()
    {
        try { await JS.InvokeVoidAsync("tmRichEditor.restoreCursorPosition", _editorRef); }
        catch { /* JS not available in tests / SSR */ }
    }

    private async Task OnLinkConfirm((string Url, string Text) link)
    {
        _showLinkDialog = false;
        _restoreCaretAfterDialogClose = true;
        _pendingAfterDialogClose = async () =>
        {
            await JS.InvokeVoidAsync("tmRichEditor.insertLink", _editorRef, link.Url, link.Text);
            await HandleInput(); // Update content
        };
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnLinkCancel()
    {
        _showLinkDialog = false;
        _restoreCaretAfterDialogClose = true;
        _pendingAfterDialogClose = null;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnImageConfirm((string Url, string Alt) image)
    {
        _showImageDialog = false;
        _restoreCaretAfterDialogClose = true;
        _pendingAfterDialogClose = async () =>
        {
            await JS.InvokeVoidAsync("tmRichEditor.insertImage", _editorRef, image.Url, image.Alt);
            await HandleInput(); // Update content
        };
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnImageCancel()
    {
        _showImageDialog = false;
        _restoreCaretAfterDialogClose = true;
        _pendingAfterDialogClose = null;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnTableConfirm((int Rows, int Cols) table)
    {
        _showTableDialog = false;
        _restoreCaretAfterDialogClose = true;
        _pendingAfterDialogClose = async () =>
        {
            await JS.InvokeVoidAsync("tmRichEditor.insertTable", _editorRef, table.Rows, table.Cols);
            await HandleInput(); // Update content
        };
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnTableCancel()
    {
        _showTableDialog = false;
        _restoreCaretAfterDialogClose = true;
        _pendingAfterDialogClose = null;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnVideoConfirm((string Url, string Title) video)
    {
        var embedUrl = ConvertToEmbedUrl(video.Url);
        _showVideoDialog = false;
        // Restore on EVERY close path, insert only for a usable embed URL.
        _restoreCaretAfterDialogClose = true;
        _pendingAfterDialogClose = string.IsNullOrEmpty(embedUrl)
            ? null
            : async () =>
            {
                await JS.InvokeVoidAsync("tmRichEditor.insertVideo", _editorRef, embedUrl, video.Title);
                await HandleInput(); // Update content
            };
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnVideoCancel()
    {
        _showVideoDialog = false;
        _restoreCaretAfterDialogClose = true;
        _pendingAfterDialogClose = null;
        await InvokeAsync(StateHasChanged);
    }

    private string ConvertToEmbedUrl(string url)
    {
        // YouTube
        if (url.Contains("youtube.com/watch"))
        {
            var videoId = System.Text.RegularExpressions.Regex.Match(url, @"v=([^&]+)").Groups[1].Value;
            if (!string.IsNullOrEmpty(videoId))
                return $"https://www.youtube.com/embed/{videoId}";
        }
        else if (url.Contains("youtu.be/"))
        {
            var videoId = url.Split('/').Last();
            return $"https://www.youtube.com/embed/{videoId}";
        }
        // Vimeo
        else if (url.Contains("vimeo.com/"))
        {
            var videoId = url.Split('/').Last();
            return $"https://player.vimeo.com/video/{videoId}";
        }
        return url;
    }

    /// <summary>
    /// Replaces the first occurrence of the find text. Runs while the dialog stays open, so it
    /// does not go through the close-then-act pipeline. Returns the REAL replacement count
    /// from <c>tmRichEditor.findAndReplace</c> so the dialog can report it (0 → "no matches").
    /// </summary>
    private async Task<int> OnReplace((string Find, string Replace) args)
    {
        try
        {
            var count = await JS.InvokeAsync<int>("tmRichEditor.findAndReplace", _editorRef, args.Find, args.Replace, false);
            await HandleInput(); // Update content
            return count;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Replaces every occurrence of the find text. Returns the REAL replacement count from
    /// <c>tmRichEditor.findAndReplace</c> so the dialog can report it (0 → "no matches").
    /// </summary>
    private async Task<int> OnReplaceAll((string Find, string Replace) args)
    {
        try
        {
            var count = await JS.InvokeAsync<int>("tmRichEditor.findAndReplace", _editorRef, args.Find, args.Replace, true);
            await HandleInput(); // Update content
            return count;
        }
        catch { return 0; }
    }

    private async Task OnFindReplaceCancel()
    {
        // Close first; the caret bookmark is replayed after the dialog unmounted.
        _showFindReplaceDialog = false;
        _restoreCaretAfterDialogClose = true;
        _pendingAfterDialogClose = null;
        await InvokeAsync(StateHasChanged);
    }

    // ── Token/Variable system ──

    private async Task LoadTokensAsync()
    {
        if (TokenProvider == null) return;

        _isLoadingTokens = true;
        var tokens = await TokenProvider.SearchTokensAsync(_tokenQuery);
        _availableTokens = tokens.Select(t => new TokenItem
        {
            Key = t.Key,
            DisplayName = t.DisplayName,
            Description = t.Description,
            Category = t.Category,
            Icon = t.Icon,
            ColorClass = t.ColorClass,
            TypeLabel = t.TypeLabel
        }).ToList();
        _isLoadingTokens = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task UpdateTokenQueryAsync()
    {
        try
        {
            var query = await JS.InvokeAsync<string?>("tmRichEditor.getTextBeforeCursorForToken", _editorRef, TokenTrigger);
            if (query == null)
            {
                _showTokenDropdown = false;
            }
            else if (query != _tokenQuery)
            {
                _tokenQuery = query;
                _tokenHighlightedIndex = 0;
                await LoadTokensAsync();
            }
        }
        catch { /* JS not available */ }
    }

    private async Task MoveTokenHighlightUp()
    {
        if (_tokenHighlightedIndex > 0)
        {
            _tokenHighlightedIndex--;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task MoveTokenHighlightDown()
    {
        var maxIndex = _availableTokens.Count - 1 + (TokenProvider?.SupportsCreation == true ? 1 : 0);
        if (_tokenHighlightedIndex < maxIndex)
        {
            _tokenHighlightedIndex++;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SelectHighlightedToken()
    {
        // Check if "Create new" is highlighted
        if (TokenProvider?.SupportsCreation == true && _tokenHighlightedIndex == _availableTokens.Count)
        {
            await OnTokenCreateFromDropdown(string.IsNullOrWhiteSpace(_tokenQuery) ? null : _tokenQuery);
            return;
        }

        if (_tokenHighlightedIndex >= 0 && _tokenHighlightedIndex < _availableTokens.Count)
        {
            await OnTokenSelected(_availableTokens[_tokenHighlightedIndex]);
        }
    }

    private async Task OnTokenSelected(TokenItem token)
    {
        var viaTrigger = _tokenOpenedViaTrigger;
        _showTokenDropdown = false;
        _tokenOpenedViaTrigger = false;
        try
        {
            _isInsertingToken = true;
            if (viaTrigger)
            {
                await JS.InvokeVoidAsync("tmRichEditor.replaceTokenTrigger", _editorRef, token.Key, token.DisplayName, BuildTokenMeta(token));
            }
            else
            {
                await JS.InvokeVoidAsync("tmRichEditor.insertToken", _editorRef, token.Key, token.DisplayName, BuildTokenMeta(token));
            }
            // Sync HTML value (HandleInput and OnAfterRenderAsync are blocked by _isInsertingToken)
            var html = await JS.InvokeAsync<string>("tmRichEditor.getHtml", _editorRef);
            Value = html ?? string.Empty;
            UpdateCounts(Value);
            await ValueChanged.InvokeAsync(Value);
        }
        catch { /* JS not available in tests / SSR */ }
        finally
        {
            _isInsertingToken = false;
        }

        await OnTokenInserted.InvokeAsync(CreateTokenFromItem(token));
    }

    /// <summary>Called from JS when the user moves the mouse over a token chip.</summary>
    [JSInvokable]
    public async Task OnTokenHoverEnter(TokenHoverJsArgs args)
    {
        if (!OnTokenHover.HasDelegate) return;
        var hoverArgs = new TokenHoverArgs
        {
            Key = args.Key,
            DisplayName = args.DisplayName,
            Description = args.Description,
            Icon = args.Icon,
            ColorClass = args.ColorClass,
            TypeLabel = args.TypeLabel,
            RectLeft = args.RectLeft,
            RectTop = args.RectTop,
            RectRight = args.RectRight,
            RectBottom = args.RectBottom,
            RectWidth = args.RectWidth,
            RectHeight = args.RectHeight
        };
        await OnTokenHover.InvokeAsync(hoverArgs);
    }

    /// <summary>Called from JS when the user moves the mouse away from a token chip.</summary>
    [JSInvokable]
    public async Task OnTokenHoverLeave()
    {
        if (OnTokenHoverEnd.HasDelegate)
            await OnTokenHoverEnd.InvokeAsync();
    }

    private async Task OnTokenCreateFromDropdown(string? query)
    {
        _showTokenDropdown = false;
        try
        {
            _isInsertingToken = true;
            await JS.InvokeVoidAsync("tmRichEditor.deleteTokenTrigger", _editorRef);
            // Sync Value so the cursor marker survives Blazor re-renders
            var html = await JS.InvokeAsync<string>("tmRichEditor.getHtml", _editorRef);
            Value = html ?? string.Empty;
            UpdateCounts(Value);
            await ValueChanged.InvokeAsync(Value);
        }
        catch { /* JS not available */ }
        finally
        {
            _isInsertingToken = false;
        }
        await OnTokenCreateRequested.InvokeAsync(query);
    }

    private void OnTokenIndexChanged(int index)
    {
        _tokenHighlightedIndex = index;
    }

    private async Task HandleTokenButtonClicked()
    {
        if (_availableTokens.Count == 0 && TokenProvider != null)
        {
            _tokenQuery = "";
            await LoadTokensAsync();
        }

        if (_availableTokens.Count > 0 || TokenProvider?.SupportsCreation == true)
        {
            _showTokenDropdown = !_showTokenDropdown;
            _tokenOpenedViaTrigger = false;
            _tokenHighlightedIndex = 0;
            await InvokeAsync(StateHasChanged);
        }
        else
        {
            await OnTokenCreateRequested.InvokeAsync(null);
        }
    }

    /// <summary>
    /// Refreshes the token list from the provider.
    /// Call this after creating/updating tokens so the autocomplete reflects changes.
    /// </summary>
    public async Task RefreshTokensAsync()
    {
        TokenProvider?.Refresh();
        await LoadTokensAsync();
    }

    /// <summary>
    /// Programmatically inserts a token chip into the editor.
    /// Call this from parent component after a token is created via dialog.
    /// </summary>
    public async Task InsertTokenAsync(string key, string? displayName = null)
    {
        try
        {
            _isInsertingToken = true;
            await JS.InvokeVoidAsync("tmRichEditor.insertToken", _editorRef, key, displayName ?? key);
            var html = await JS.InvokeAsync<string>("tmRichEditor.getHtml", _editorRef);
            Value = html ?? string.Empty;
            UpdateCounts(Value);
            await ValueChanged.InvokeAsync(Value);
        }
        catch { /* JS not available in tests / SSR */ }
        finally
        {
            _isInsertingToken = false;
        }
    }

    private static TokenProxy CreateTokenFromItem(TokenItem item) => new()
    {
        Key = item.Key,
        DisplayName = item.DisplayName,
        Description = item.Description,
        Category = item.Category,
        Icon = item.Icon,
        ColorClass = item.ColorClass,
        TypeLabel = item.TypeLabel
    };

    private static object BuildTokenMeta(TokenItem token) => new
    {
        description = token.Description,
        icon = token.Icon,
        colorClass = token.ColorClass,
        typeLabel = token.TypeLabel
    };

    private class TokenProxy : Tempo.Blazor.Interfaces.IToken
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Category { get; set; }
        public string? Icon { get; set; }
        public string? ColorClass { get; set; }
        public string? TypeLabel { get; set; }
    }

    private async Task OnMentionSelected(MentionUser user)
    {
        _showMentionDropdown = false;
        try
        {
            await JS.InvokeVoidAsync("tmRichEditor.insertMention", user.UserName, user.DisplayName, user.Id);
            await HandleInput(); // Update content
        }
        catch { /* JS not available in tests / SSR */ }
    }

    private void OnMentionIndexChanged(int index)
    {
        _highlightedMentionIndex = index;
    }

    /// <summary>
    /// Gets the current HTML content.
    /// </summary>
    public string GetHtml()
    {
        return Value ?? string.Empty;
    }

    /// <summary>
    /// Clears the editor content.
    /// </summary>
    public async Task Clear()
    {
        Value = string.Empty;
        try
        {
            await JS.InvokeVoidAsync("tmRichEditor.setHtml", _editorRef, "");
        }
        catch { }
        _charCount = 0;
        _wordCount = 0;
        _isEmpty = true;
        _activeFormats.Clear();
        await ValueChanged.InvokeAsync(Value);
        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        try { await JS.InvokeVoidAsync("tmRichEditor.destroy", _editorRef); }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }
        catch (TaskCanceledException) { }
        _dotNetRef?.Dispose();
    }

    // Test helper methods
    public async Task NotifyContentChangedForTest(string html)
    {
        Value = html;
        UpdateCounts(html);
        await ValueChanged.InvokeAsync(html);
        await InvokeAsync(StateHasChanged);
    }

    public async Task TriggerMentionSearchForTest(string query)
    {
        if (MentionProvider == null) return;
        _mentionQuery = query;
        var users = await MentionProvider.SearchAsync(new TmPeopleQuery { SearchText = query, Take = 8 });
        _mentionUsers = users.Select(ToMentionUser).ToList();
        _showMentionDropdown = _mentionUsers.Count > 0;
        _highlightedMentionIndex = 0;
        await InvokeAsync(StateHasChanged);
    }

    public async Task InsertMentionChipForTest(TmUserRef user)
    {
        var mentionUser = new MentionUser
        {
            Id = user.Id,
            UserName = string.IsNullOrWhiteSpace(user.UserName) ? user.Id : user.UserName,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Id : user.DisplayName,
            AvatarUrl = user.AvatarUrl
        };
        await OnMentionSelected(mentionUser);
    }

    private static MentionUser ToMentionUser(TmUser user)
        => new()
        {
            Id = user.Id,
            UserName = string.IsNullOrWhiteSpace(user.UserName) ? user.Id : user.UserName,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Id : user.DisplayName,
            AvatarUrl = user.AvatarUrl
        };

    public async Task TriggerTokenSearchForTest(string query)
    {
        if (TokenProvider == null) return;
        _tokenQuery = query;
        var tokens = await TokenProvider.SearchTokensAsync(query);
        _availableTokens = tokens.Select(t => new TokenItem
        {
            Key = t.Key,
            DisplayName = t.DisplayName,
            Description = t.Description,
            Category = t.Category,
            Icon = t.Icon,
            ColorClass = t.ColorClass,
            TypeLabel = t.TypeLabel
        }).ToList();
        _showTokenDropdown = true;
        _tokenHighlightedIndex = 0;
        await InvokeAsync(StateHasChanged);
    }

    public async Task InsertTokenChipForTest(IToken token)
    {
        var tokenItem = new TokenItem
        {
            Key = token.Key,
            DisplayName = token.DisplayName,
            Description = token.Description,
            Category = token.Category
        };
        await OnTokenSelected(tokenItem);
    }
}
