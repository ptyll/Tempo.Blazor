using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>Supported top-level modes in the Notion AI inline menu.</summary>
public enum NotionAiMenuMode
{
    /// <summary>Generate new page content from a prompt.</summary>
    Generate,

    /// <summary>Improve selected text.</summary>
    Improve,

    /// <summary>Summarize the current page.</summary>
    Summarize,

    /// <summary>Ask a page-scoped question.</summary>
    Ask
}

public partial class TmNotionAiMenu : TmComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime Js { get; set; } = default!;

    [CascadingParameter] private NotionEditorContext? Context { get; set; }

    /// <summary>Controls whether the AI menu is rendered.</summary>
    [Parameter] public bool Visible { get; set; }

    /// <summary>Fixed top coordinate for inline menu placement.</summary>
    [Parameter] public double Top { get; set; }

    /// <summary>Fixed left coordinate for inline menu placement.</summary>
    [Parameter] public double Left { get; set; }

    /// <summary>Forced layout. Mobile presents the menu as the core sheet.</summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The cascaded viewport. An Auto menu sheets when the viewport is mobile.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] public TmLayoutContext? Layout { get; set; }

    private TmLayoutMode ResolvedLayout =>
        LayoutMode != TmLayoutMode.Auto ? LayoutMode : Layout?.Resolved ?? TmLayoutMode.Auto;

    /// <summary>Renders the component as a wider page-level panel.</summary>
    [Parameter] public bool Panel { get; set; }

    /// <summary>Initial mode selected when the menu opens.</summary>
    [Parameter] public NotionAiMenuMode InitialMode { get; set; } = NotionAiMenuMode.Generate;

    /// <summary>Optional page identifier used for summary and page-scoped questions.</summary>
    [Parameter] public string? PageId { get; set; }

    /// <summary>Selected plain text used by the improve mode.</summary>
    [Parameter] public string? SourceText { get; set; }

    /// <summary>Selected or page HTML context passed to the provider.</summary>
    [Parameter] public string? ContextHtml { get; set; }

    /// <summary>Optional target culture for translation requests.</summary>
    [Parameter] public string? TargetCulture { get; set; }

    /// <summary>Explicit provider used by tests or advanced integrations. Defaults to the editor context provider.</summary>
    [Parameter] public INotionAIProvider? Provider { get; set; }

    /// <summary>Raised when generated output is accepted. The argument is sanitized HTML.</summary>
    [Parameter] public EventCallback<string> OnAccepted { get; set; }

    /// <summary>Raised when the user closes or discards the menu.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    private static readonly (NotionAiMenuMode Mode, string Key, string TestId, string Icon)[] Modes =
    [
        (NotionAiMenuMode.Generate, "Notion_AI_Generate", "notion-ai-mode-generate", IconNames.WandSparkles),
        (NotionAiMenuMode.Improve, "Notion_AI_Improve", "notion-ai-mode-improve", IconNames.Pencil),
        (NotionAiMenuMode.Summarize, "Notion_AI_Summarize", "notion-ai-mode-summarize", IconNames.List),
        (NotionAiMenuMode.Ask, "Notion_AI_Ask", "notion-ai-mode-ask", IconNames.Message),
    ];

    private static readonly (AiImproveMode Mode, string Key, string TestId)[] ImproveOptions =
    [
        (AiImproveMode.Grammar,    "Notion_AI_Mode_Grammar",   "notion-ai-improve-grammar"),
        (AiImproveMode.Shorten,    "Notion_AI_Mode_Shorten",   "notion-ai-improve-shorten"),
        (AiImproveMode.Lengthen,   "Notion_AI_Mode_Lengthen",  "notion-ai-improve-lengthen"),
        (AiImproveMode.ChangeTone, "Notion_AI_Mode_Tone",      "notion-ai-improve-tone"),
        (AiImproveMode.Simplify,   "Notion_AI_Mode_Simplify",  "notion-ai-improve-simplify"),
        (AiImproveMode.Translate,  "Notion_AI_Mode_Translate", "notion-ai-improve-translate"),
    ];

    private NotionAiMenuMode _mode;
    private AiImproveMode _improveMode = AiImproveMode.Grammar;
    private string _prompt = string.Empty;

    /// <summary>Maximum characters accepted by the prompt. Defaults to 2000.</summary>
    [Parameter] public int PromptMaxLength { get; set; } = 2000;
    private string _question = string.Empty;
    private string _streamedText = string.Empty;
    private string? _error;
    private bool _isStreaming;
    private bool _wasVisible;
    private AIRequestSnapshot? _lastRequest;
    private CancellationTokenSource? _streamCts;

    private INotionAIProvider? ActiveProvider => Provider ?? Context?.AIProvider;

    private ElementReference _anchorRef;

    private string AnchorStyle => Panel
        ? "top:calc(var(--tm-space-8) + 48px);right:var(--tm-space-8);"
        : string.Create(CultureInfo.InvariantCulture, $"top:{Top}px;left:{Left}px");

    private string PanelClass => Panel ? "tm-notion-ai tm-notion-ai--panel" : "tm-notion-ai";

    private bool _hasResult => !_isStreaming && !string.IsNullOrWhiteSpace(_streamedText);

    private static string ModeTabId(NotionAiMenuMode mode) => "notion-ai-tab-" + mode.ToString().ToLowerInvariant();

    protected override void OnParametersSet()
    {
        if (Visible && !_wasVisible)
        {
            _mode = InitialMode;
            _streamedText = string.Empty;
            _error = null;
            _lastRequest = null;
            if (_mode == NotionAiMenuMode.Summarize)
            {
                _prompt = string.Empty;
            }
        }

        _wasVisible = Visible;
    }

    private string ModeButtonClass(NotionAiMenuMode mode)
        => _mode == mode ? "tm-notion-ai__tab tm-notion-ai__tab--active" : "tm-notion-ai__tab";

    private readonly ElementReference[] _tabElements = new ElementReference[Modes.Length];

    private static int ModeIndex(NotionAiMenuMode mode)
        => Array.FindIndex(Modes, candidate => candidate.Mode == mode);

    private async Task SetMode(NotionAiMenuMode mode)
    {
        if (_isStreaming || _mode == mode) return;

        _mode = mode;
        _error = null;
        _streamedText = string.Empty;
        await InvokeAsync(StateHasChanged);
        var index = ModeIndex(mode);
        if (index >= 0)
        {
            try { await _tabElements[index].FocusAsync(); }
            catch (InvalidOperationException) { }
        }
    }

    private void HandlePromptInput(ChangeEventArgs args)
    {
        var value = args.Value?.ToString() ?? string.Empty;
        _prompt = value.Length > PromptMaxLength ? value[..PromptMaxLength] : value;
    }

    private void HandleQuestionInput(ChangeEventArgs args) => _question = args.Value?.ToString() ?? string.Empty;

    private async Task RunAsync()
    {
        var snapshot = BuildSnapshot();
        if (snapshot is null) return;

        await ExecuteAsync(snapshot);
    }

    private async Task RetryAsync()
    {
        if (_lastRequest is null) return;

        var snapshot = _lastRequest;
        CancelStreaming();
        await ExecuteAsync(snapshot);
    }

    private async Task ExecuteAsync(AIRequestSnapshot snapshot)
    {
        var provider = ActiveProvider;
        if (provider is null) return;

        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = new CancellationTokenSource();
        _lastRequest = snapshot;
        _streamedText = string.Empty;
        _error = null;
        _isStreaming = true;

        try
        {
            switch (snapshot.Mode)
            {
                case NotionAiMenuMode.Generate:
                    var request = new AiCompletionRequest
                    {
                        Prompt = snapshot.Prompt,
                        ContextHtml = ContextHtml,
                        PageId = PageId,
                        TargetCulture = TargetCulture
                    };
                    await AppendStreamAsync(provider.GenerateAsync(request, _streamCts.Token), _streamCts.Token);
                    break;

                case NotionAiMenuMode.Improve:
                    await AppendStreamAsync(provider.ImproveTextAsync(snapshot.SourceText, snapshot.ImproveMode, _streamCts.Token), _streamCts.Token);
                    break;

                case NotionAiMenuMode.Summarize:
                    _streamedText = await provider.SummarizePageAsync(snapshot.PageId, _streamCts.Token);
                    break;

                case NotionAiMenuMode.Ask:
                    await AppendStreamAsync(provider.AnswerQuestionAsync(snapshot.Question, snapshot.PageId, _streamCts.Token), _streamCts.Token);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            _error = null;
        }
        catch
        {
            _error = Loc["Notion_AI_Error"];
        }
        finally
        {
            _isStreaming = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task AppendStreamAsync(IAsyncEnumerable<string> stream, CancellationToken cancellationToken)
    {
        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            _streamedText += chunk;
            await InvokeAsync(StateHasChanged);
        }
    }

    private AIRequestSnapshot? BuildSnapshot()
    {
        _error = null;

        return _mode switch
        {
            NotionAiMenuMode.Generate => BuildGenerateSnapshot(),
            NotionAiMenuMode.Improve => BuildImproveSnapshot(),
            NotionAiMenuMode.Summarize => new AIRequestSnapshot(NotionAiMenuMode.Summarize, string.Empty, string.Empty, _improveMode, PageId ?? string.Empty, string.Empty),
            NotionAiMenuMode.Ask => BuildAskSnapshot(),
            _ => null
        };
    }

    private AIRequestSnapshot? BuildGenerateSnapshot()
    {
        if (string.IsNullOrWhiteSpace(_prompt))
        {
            _error = Loc["Notion_AI_EmptyPrompt"];
            return null;
        }

        return new AIRequestSnapshot(NotionAiMenuMode.Generate, _prompt.Trim(), string.Empty, _improveMode, PageId ?? string.Empty, string.Empty);
    }

    private AIRequestSnapshot? BuildImproveSnapshot()
    {
        var text = SourceText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            _error = Loc["Notion_AI_SourceEmpty"];
            return null;
        }

        return new AIRequestSnapshot(NotionAiMenuMode.Improve, string.Empty, text, _improveMode, PageId ?? string.Empty, string.Empty);
    }

    private AIRequestSnapshot? BuildAskSnapshot()
    {
        if (string.IsNullOrWhiteSpace(_question))
        {
            _error = Loc["Notion_AI_EmptyPrompt"];
            return null;
        }

        return new AIRequestSnapshot(NotionAiMenuMode.Ask, string.Empty, string.Empty, _improveMode, PageId ?? string.Empty, _question.Trim());
    }

    private async Task CopyAsync()
    {
        if (string.IsNullOrWhiteSpace(_streamedText) || _isStreaming) return;

        try
        {
            await Js.InvokeVoidAsync("tmNotionEditor.copyToClipboard", _streamedText);
        }
        catch (JSException)
        {
            // A host without the interop still keeps the result on screen.
        }
    }

    private async Task HandleModeKeyAsync(KeyboardEventArgs args, NotionAiMenuMode current)
    {
        var index = Array.FindIndex(Modes, mode => mode.Mode == current);
        var next = args.Key switch
        {
            "ArrowRight" or "ArrowDown" => Modes[(index + 1) % Modes.Length].Mode,
            "ArrowLeft" or "ArrowUp" => Modes[(index - 1 + Modes.Length) % Modes.Length].Mode,
            "Home" => Modes[0].Mode,
            "End" => Modes[^1].Mode,
            _ => (NotionAiMenuMode?)null,
        };
        if (next is { } target)
            await SetMode(target);
    }

    private async Task AcceptAsync()
    {
        if (string.IsNullOrWhiteSpace(_streamedText) || _isStreaming) return;

        await OnAccepted.InvokeAsync(ConvertTextToHtml(_streamedText));
        await CloseAsync();
    }

    private async Task DiscardAsync()
    {
        CancelStreaming();
        _streamedText = string.Empty;
        _error = null;
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        CancelStreaming();
        await OnClosed.InvokeAsync();
    }

    /// <summary>
    /// JS-driven dismissal (Escape or outside pointerdown from overlay.js) — same effects as
    /// the close button: cancel an in-flight stream and notify OnClosed.
    /// </summary>
    private async Task SetOpenFromJsAsync(bool open)
    {
        if (!open)
        {
            await CloseAsync();
        }
    }

    private void CancelStreaming()
    {
        if (!_isStreaming) return;

        _streamCts?.Cancel();
        _isStreaming = false;
    }

    private async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            await CloseAsync();
            return;
        }

        if (args.Key == "Enter" && (args.CtrlKey || args.MetaKey) && !_isStreaming)
        {
            await RunAsync();
        }
    }

    private static string ConvertTextToHtml(string text)
    {
        var encoded = HtmlEncoder.Default.Encode(text.Trim());
        return encoded.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
    }

    public ValueTask DisposeAsync()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record AIRequestSnapshot(
        NotionAiMenuMode Mode,
        string Prompt,
        string SourceText,
        AiImproveMode ImproveMode,
        string PageId,
        string Question);
}
