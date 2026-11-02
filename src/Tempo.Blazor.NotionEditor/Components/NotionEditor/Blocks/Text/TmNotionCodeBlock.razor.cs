using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Text;

/// <summary>
/// Self-contained code block with language selector, textarea editor, copy button, and optional caption.
/// Uses a dedicated JS keyboard handler (initCodeKeyboardHandler) that handles Tab/Shift+Tab
/// indentation and Backspace-on-empty — Tab does NOT move focus like other text blocks.
/// </summary>
public partial class TmNotionCodeBlock : TmComponentBase, IAsyncDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public ICodeBlockContent? Content   { get; set; }
    [Parameter] public bool               ReadOnly  { get; set; }
    [Parameter] public bool               IsFocused { get; set; }

    /// <summary>
    /// Enables the read-only Markdown preview toggle. The toggle only appears when the
    /// block's language is Markdown; the block always opens in editor mode.
    /// </summary>
    [Parameter] public bool AllowMarkdownPreview { get; set; } = true;

    /// <summary>Fired on blur when the code text has changed. Arg = new code string.</summary>
    [Parameter] public EventCallback<string>  OnCodeSaved         { get; set; }

    /// <summary>Fired when the user selects a new language. Arg = language name (null → plain text).</summary>
    [Parameter] public EventCallback<string?> OnLanguageChanged   { get; set; }

    /// <summary>Fired on caption blur when the caption has changed. Arg = new caption (null if empty).</summary>
    [Parameter] public EventCallback<string?> OnCaptionSaved      { get; set; }

    /// <summary>Fired when Backspace is pressed in an empty code block.</summary>
    [Parameter] public EventCallback          OnDeleteRequested   { get; set; }

    /// <summary>Fired when the textarea receives focus.</summary>
    [Parameter] public EventCallback          OnFocused           { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private ElementReference                           _textareaRef;
    private string?                                    _highlightedLanguage;
    private bool                                       _codeDirty;
    private MarkupString                               _highlightedCode;
    private string                                     _highlightLanguageClass = string.Empty;
    private ElementReference                           _captionRef;
    private DotNetObjectReference<TmNotionCodeBlock>?  _dotNetRef;
    private bool                                       _kbInitialized;
    private bool                                       _captionDirty;
    private ICodeBlockContent?                         _lastContent;
    private string                                     _selectedLanguage = "Plain Text";
    private bool                                       _codeCopied;
    private bool                                       _previewVisible;

    // ── Computed ─────────────────────────────────────────────────────────────

    private string _displayLanguage =>
        string.IsNullOrWhiteSpace(_selectedLanguage) ? "Plain Text" : _selectedLanguage;

    private bool _isMarkdown =>
        string.Equals(_displayLanguage, MarkdownLanguage, StringComparison.OrdinalIgnoreCase);

    private bool _canPreviewMarkdown => AllowMarkdownPreview && _isMarkdown;

    private bool _showPreview => _canPreviewMarkdown && _previewVisible;

    private string _wrapClass =>
        Content?.WrapLines == true ? "tm-notion-code-block--wrap" : string.Empty;

    private bool _showCaption =>
        !string.IsNullOrEmpty(Content?.Caption) || !ReadOnly;

    // ── Language list ─────────────────────────────────────────────────────────

    private static readonly string[] _languages =
    [
        "Plain Text",
        "JavaScript", "TypeScript", "JSX", "TSX",
        "Python", "C#", "Java", "Go", "Rust",
        "C", "C++", "PHP", "Ruby", "Swift", "Kotlin", "Dart", "Scala", "R",
        "SQL", "HTML", "CSS", "SCSS", "XML",
        "JSON", "YAML", "TOML", "INI",
        "Markdown", "Bash", "PowerShell", "Shell", "Batch",
        "Docker", "Terraform", "Nginx", "Apache"
    ];

    /// <summary>
    /// The options the dropdown offers. A language stored under a name the list does not carry —
    /// "yaml" instead of "YAML", or something exotic — is appended, otherwise the select would fall
    /// back to its first option and claim the block is plain text.
    /// </summary>
    private IEnumerable<string> _languageOptions =>
        _languages.Contains(_selectedLanguage, StringComparer.Ordinal)
            ? _languages
            : _languages.Append(_selectedLanguage);

    /// <summary>Maps a stored language onto the canonical spelling used by the dropdown.</summary>
    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "Plain Text";

        return _languages.FirstOrDefault(known => string.Equals(known, language, StringComparison.OrdinalIgnoreCase))
            ?? language;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(Content, _lastContent))
        {
            // The capability can be switched off while the preview is open.
            if (!_canPreviewMarkdown) _previewVisible = false;
            return;
        }

        _lastContent      = Content;
        _selectedLanguage = NormalizeLanguage(Content?.Language);
        _captionDirty     = false;
        _kbInitialized    = false;

        // Leaving Markdown must drop the preview, otherwise the block would render
        // stale HTML for a language that has no preview.
        if (!_canPreviewMarkdown) _previewVisible = false;
    }

    // ── Markdown preview ─────────────────────────────────────────────────────

    private const string MarkdownLanguage = "Markdown";

    private async Task TogglePreviewAsync()
    {
        if (!_canPreviewMarkdown) return;

        // Flush pending edits so the preview never renders a stale source.
        if (!ReadOnly && !_previewVisible && _codeDirty)
        {
            await OnCodeBlurAsync();
        }

        _previewVisible = !_previewVisible;
        _kbInitialized = false;
        // The textarea is about to leave the DOM. Drop its watcher now — destroyBlock's
        // early return cannot see an element that is no longer rendered.
        if (_previewVisible && _textareaRef.Context is not null)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.destroyBlock", _textareaRef); }
            catch { /* SSR / test */ }
        }
    }

    /// <summary>
    /// Renders the block's Markdown through the package's own importer/exporter pair,
    /// which HTML-encodes untrusted text and whitelists link schemes. No extra dependency.
    /// </summary>
    private MarkupString RenderMarkdownPreview()
    {
        var code = Content?.Code ?? string.Empty;

        // An "empty" code textarea stores a lone <br>; treat any break-and-whitespace-only source as
        // empty so its preview is empty too, rather than a stray blank line.
        if (string.IsNullOrWhiteSpace(BreakRegex().Replace(code, string.Empty)))
        {
            return new MarkupString(string.Empty);
        }

        var blocks = NotionMarkdownImporter.Import(code, Guid.Empty);
        return new MarkupString(NotionHtmlExporter.Export(blocks));
    }

    [System.Text.RegularExpressions.GeneratedRegex("<br\\s*/?>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex BreakRegex();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // In preview mode the textarea is not rendered, so there is nothing to wire up.
        if (_showPreview)
        {
            return;
        }

        var code = Content?.Code ?? string.Empty;

        if (!_kbInitialized)
        {
            _kbInitialized = true;
            _dotNetRef?.Dispose();
            _dotNetRef = DotNetObjectReference.Create(this);
            try
            {
                if (!ReadOnly)
                    await JS.InvokeVoidAsync("tmNotionEditor.initCodeKeyboardHandler", _textareaRef, _dotNetRef);

                await JS.InvokeVoidAsync("tmNotionEditor.setCode", _textareaRef, code);

                if (!string.IsNullOrEmpty(Content?.Caption))
                    await JS.InvokeVoidAsync("tmNotionEditor.setHtml", _captionRef, Content.Caption);
            }
            catch { }

            // Paint once, right after the code is loaded — fire-and-forget so the highlight interop
            // never blocks the render pipeline. Awaiting it here made a page full of code blocks
            // shift layout as each one painted in turn, which stalled clicks mid-scroll.
            _ = RefreshHighlightAsync();
        }
    }

    /// <summary>
    /// Rebuilds the highlighted HTML from the textarea's live value and stores it as a MarkupString,
    /// so Blazor owns the code element and no render can wipe the colours. Prism is optional.
    /// </summary>
    /// <summary>
    /// Highlights the stored code. Used on load and on a language change: it never touches the DOM,
    /// so it stays cheap even on a page full of code blocks.
    /// </summary>
    private Task RefreshHighlightAsync() => ApplyHighlightAsync(Content?.Code ?? string.Empty);

    /// <summary>Highlights the live textarea value; used while the user is typing.</summary>
    private async Task RefreshHighlightFromLiveAsync()
    {
        string code;
        try { code = await JS.InvokeAsync<string>("tmNotionEditor.getCode", _textareaRef); }
        catch { return; }

        await ApplyHighlightAsync(code);
    }

    /// <summary>
    /// Rebuilds the highlighted HTML and stores it as a MarkupString, so Blazor owns the code
    /// element and no render can wipe the colours. Prism is optional.
    /// </summary>
    private async Task ApplyHighlightAsync(string code)
    {
        var prismId = NotionCodeLanguage.ToPrismId(_selectedLanguage);

        string html;
        try { html = await JS.InvokeAsync<string>("tmNotionEditor.highlightToHtml", code, prismId); }
        catch { return; }

        var languageClass = prismId is null ? string.Empty : $"language-{prismId}";
        if (html == _highlightedCode.Value && languageClass == _highlightLanguageClass) return;

        _highlightedLanguage    = prismId;
        _highlightedCode        = new MarkupString(html);
        _highlightLanguageClass = languageClass;
        StateHasChanged();
    }


    private async Task HandleCodeInputAsync()
    {
        _codeDirty = true;
        await RefreshHighlightFromLiveAsync();
    }

    // ── Code blur / focus ─────────────────────────────────────────────────────

    private async Task OnCodeBlurAsync()
    {
        if (!_codeDirty || ReadOnly) return;
        _codeDirty = false;
        try
        {
            var code = await JS.InvokeAsync<string>("tmNotionEditor.getCode", _textareaRef);
            await OnCodeSaved.InvokeAsync(code);
        }
        catch { }
    }

    private async Task HandleFocusAsync() => await OnFocused.InvokeAsync();

    // ── Caption blur ──────────────────────────────────────────────────────────

    private async Task OnCaptionBlurAsync()
    {
        if (!_captionDirty || ReadOnly) return;
        _captionDirty = false;
        try
        {
            var html = await JS.InvokeAsync<string>("tmNotionEditor.getHtml", _captionRef);
            var text = string.IsNullOrWhiteSpace(html) ? null : html;
            await OnCaptionSaved.InvokeAsync(text);
        }
        catch { }
    }

    // ── Language change ───────────────────────────────────────────────────────

    private async Task HandleLanguageChangedAsync()
    {
        await RefreshHighlightAsync();
        var lang = string.IsNullOrWhiteSpace(_selectedLanguage) ? null : _selectedLanguage;
        await OnLanguageChanged.InvokeAsync(lang);
    }

    // ── Copy ──────────────────────────────────────────────────────────────────

    private async Task CopyCodeAsync()
    {
        try
        {
            var code = await JS.InvokeAsync<string>("tmNotionEditor.getCode", _textareaRef);
            if (string.IsNullOrEmpty(code) && Content?.Code is { Length: > 0 } stored)
                code = stored;
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", code);
            _codeCopied = true;
            StateHasChanged();
            await Task.Delay(2000);
            _codeCopied = false;
            StateHasChanged();
        }
        catch { }
    }

    // ── JS callback — must match notion-editor.js ─────────────────────────────

    [JSInvokable]
    public async Task OnBackspaceOnEmpty() => await OnDeleteRequested.InvokeAsync();

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_kbInitialized)
        {
            try { await JS.InvokeVoidAsync("tmNotionEditor.destroyBlock", _textareaRef); }
            catch { }
        }
        _dotNetRef?.Dispose();
    }
}
