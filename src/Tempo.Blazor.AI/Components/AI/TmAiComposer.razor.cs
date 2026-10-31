using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.AI.Services;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Code-behind of <see cref="TmAiComposer"/>: the draft, the submit contract, the model selector, the
/// attachment list and the parameter synchronisation. The markup stays in the component file so the ARIA
/// contract reads in one place; the suggestion listbox lives in <c>TmAiComposer.Suggestions.cs</c>, the file
/// picker in <c>TmAiComposer.Attachments.cs</c> and the interop in <c>TmAiComposer.Interop.cs</c>.
/// </summary>
public partial class TmAiComposer
{
    private readonly List<AiComposerAttachment> _attachments = [];
    private readonly List<AiMentionItem> _mentions = [];

    private ElementReference _rootRef;
    private ElementReference _textareaRef;
    private ElementReference _suggestionsRef;

    private string _text = string.Empty;
    private string? _lastTextParameter;
    private string? _selectedModel;
    private string? _lastSelectedModelParameter;
    private IReadOnlyList<AiComposerAttachment>? _lastAttachmentsParameter;
    private string? _announcement;
    private int _announcementSequence;
    private bool _listeningAnnouncement;
    private int _caret = -1;
    private bool _submitting;
    private bool _disposed;

    /// <summary>
    /// The draft. Two-way bound: the component raises <see cref="TextChanged"/> on every edit and clears the
    /// draft after a successful submit. A host that reassigns the value to something the component did not type
    /// replaces the draft (the suggestion list closes and the recorded command/mentions are dropped); a host
    /// echoing the value it received is not a replacement.
    /// </summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Raised with the draft on every edit and with an empty string after a successful submit.</summary>
    [Parameter] public EventCallback<string?> TextChanged { get; set; }

    /// <summary>Placeholder of the prompt. Default <see langword="null"/> uses the localized "Ask anything…".</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>
    /// Accessible name of the prompt. Default <see langword="null"/> uses the localized "Message". Set it when
    /// the page has several composers so a screen-reader user can tell them apart.
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Disables the prompt, the picker, the voice control and the send/stop slot.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Raised with the submitted prompt: the trimmed text, the attachments, the selected model, the mentions the
    /// draft references and the slash command it started from. The draft, the attachments and the recorded
    /// mentions are cleared only after the handler completed — a handler that throws keeps the reader's text.
    /// Without a bound handler the send control is not offered.
    /// </summary>
    [Parameter] public EventCallback<AiComposerSubmit> OnSubmit { get; set; }

    /// <summary>
    /// Whether a generation is running. The send slot renders the Stop control instead of Send while it is
    /// true, and Enter cannot start a second generation.
    /// </summary>
    [Parameter] public bool IsGenerating { get; set; }

    /// <summary>Raised when the reader activates the Stop control of a running generation.</summary>
    [Parameter] public EventCallback OnStop { get; set; }

    /// <summary>
    /// Stick the composer to the bottom of its scroll container at every width. Default <see langword="false"/>
    /// — the narrow (mobile) layout is sticky on its own, this forces it in a wide container too.
    /// </summary>
    [Parameter] public bool Sticky { get; set; }

    /// <summary>
    /// How many rows the prompt may grow to before it starts scrolling. Default 8. The module clamps the
    /// autosize to this row count, and a value of zero or less leaves the height uncapped.
    /// </summary>
    [Parameter] public int MaxRows { get; set; } = 8;

    /// <summary>Offer the attachment picker and the chip list. Default <see langword="true"/>.</summary>
    [Parameter] public bool EnableAttachments { get; set; } = true;

    /// <summary>
    /// How many files one prompt may carry. Default 5. A zero (or negative) value refuses every file with the
    /// localized "at most 0 files" message, which is how a host turns attachments off without removing the
    /// picker.
    /// </summary>
    [Parameter] public int MaxAttachments { get; set; } = 5;

    /// <summary>
    /// The largest file the composer accepts, in bytes. Default 10 MB. Zero (or negative) disables the size
    /// check; the browser and the host's upload path remain responsible for what they will actually read.
    /// </summary>
    [Parameter] public long MaxFileSize { get; set; } = 10L * 1024 * 1024;

    /// <summary>Accepted file types forwarded to the browser picker (for example <c>"image/*,.pdf"</c>).</summary>
    [Parameter] public string? Accept { get; set; }

    /// <summary>
    /// Attachments seeded by the host. A new list whose ids differ from the current chips replaces them;
    /// echoing the list the component raised through <see cref="AttachmentsChanged"/> is not a replacement.
    /// </summary>
    [Parameter] public IReadOnlyList<AiComposerAttachment>? Attachments { get; set; }

    /// <summary>Raised with every accepted attachment change (a pick, a removal, the post-submit clear).</summary>
    [Parameter] public EventCallback<IReadOnlyList<AiComposerAttachment>> AttachmentsChanged { get; set; }

    /// <summary>
    /// Slash commands offered after the reader types <c>/</c>. A command is filtered by its title (and its
    /// description), picking one inserts its <see cref="AiSlashCommand.Prompt"/> (or <c>/{Title}</c>) into the
    /// draft and the next submit carries it as <see cref="AiComposerSubmit.Command"/>.
    /// </summary>
    [Parameter] public IReadOnlyList<AiSlashCommand>? Commands { get; set; }

    /// <summary>
    /// The source of the <c>@</c> mentions. The composer calls it with the typed query, debounced by
    /// <see cref="MentionDebounce"/> and cancelled as soon as the query changes or the list closes. Without a
    /// provider the <c>@</c> trigger does nothing.
    /// </summary>
    [Parameter] public IAiMentionProvider? MentionProvider { get; set; }

    /// <summary>
    /// Quiet period before a mention query is sent. Default 200 ms. Zero sends every keystroke, which is only
    /// reasonable for an in-memory provider.
    /// </summary>
    [Parameter] public TimeSpan MentionDebounce { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// The models the reader may pick from. An empty (or null) list hides the built-in select unless
    /// <see cref="ModelSelectorTemplate"/> supplies a picker of its own.
    /// </summary>
    [Parameter] public IReadOnlyList<string>? Models { get; set; }

    /// <summary>
    /// Identifier of the model the draft targets, two-way bound through <see cref="SelectedModelChanged"/>. A
    /// host echoing the value it received is not a replacement.
    /// </summary>
    [Parameter] public string? SelectedModel { get; set; }

    /// <summary>Raised with the model the reader picked in the built-in select.</summary>
    [Parameter] public EventCallback<string?> SelectedModelChanged { get; set; }

    /// <summary>
    /// Replaces the built-in model select with the host's own picker — a full model picker with descriptions,
    /// a provider switcher, … The host binds its own control to <see cref="SelectedModel"/>; the composer only
    /// carries the value into the submit.
    /// </summary>
    [Parameter] public RenderFragment? ModelSelectorTemplate { get; set; }

    /// <summary>
    /// Offer the opt-in voice input. Default <see langword="false"/>: the control renders only when this is
    /// true AND the browser exposes the Web Speech API, so a browser without speech recognition never shows a
    /// dead button.
    /// </summary>
    [Parameter] public bool EnableVoiceInput { get; set; }

    /// <summary>
    /// BCP-47 language of the speech recognizer. Default <see langword="null"/> uses the document's language
    /// and then <c>en-US</c>.
    /// </summary>
    [Parameter] public string? SpeechLanguage { get; set; }

    /// <summary>
    /// Track the on-screen keyboard so a sticky composer stays above it (the iOS <c>visualViewport</c> pattern
    /// of <c>TmChat.TrackVirtualKeyboard</c>). Default <see langword="false"/>: no viewport listeners and no
    /// inline knob.
    /// </summary>
    [Parameter] public bool TrackVirtualKeyboard { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The clock behind the mention debounce; resolved from the service provider so a host keeps the last word.</summary>
    private TimeProvider Clock => _clock ??= Services.GetService<TimeProvider>() ?? TimeProvider.System;

    private TimeProvider? _clock;

    /// <summary>The service provider, injected so the debounce can resolve the host's <see cref="TimeProvider"/>.</summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>The current draft (the textarea's visible value).</summary>
    private string Draft => _text;

    private string PlaceholderText => Placeholder ?? Loc["TmAiComposer_Placeholder"];

    private string LabelText => AriaLabel ?? Loc["TmAiComposer_Label"];

    /// <summary>
    /// Whether the send control is offered: no submit is in flight, a handler is bound, the prompt is not empty
    /// and nothing is running. The in-flight guard is what makes a second Enter or a double click a no-op —
    /// <see cref="IsGenerating"/> is a parameter and therefore does not move until the host re-renders.
    /// </summary>
    private bool CanSubmit => !_submitting && !Disabled && !IsGenerating && OnSubmit.HasDelegate
        && !string.IsNullOrWhiteSpace(_text);

    /// <summary>The attachment chips currently held by the composer (a read-only view of the internal list).</summary>
    public IReadOnlyList<AiComposerAttachment> CurrentAttachments => _attachments;

    /// <summary>Whether a speech-recognition session is running right now.</summary>
    public bool IsListening => _isListening;

    /// <summary>The stable id of the suggestion listbox of this instance (unique per component instance).</summary>
    private readonly string _suggestionsId = $"tm-ai-composer-{Guid.NewGuid():N}";

    /// <summary>The id of the announcement region of this instance (derived, so it needs no second guid).</summary>
    private string _announcementId => $"{_suggestionsId}-announcement";

    // ── Lifecycle ──────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        SyncDraftFromParameter();
        SyncAttachmentsFromParameter();
        SyncModelFromParameter();

        if (_isListening && (Disabled || !EnableVoiceInput))
        {
            // The microphone control disappears with this state; a recognizer must not keep listening behind a
            // control the reader can no longer see.
            _ = StopListeningAsync();
        }
    }

    /// <summary>
    /// Adopts a draft the HOST replaced. A value equal to the current draft is the host echoing the value this
    /// component raised through <see cref="TextChanged"/> — treating that as a replacement would close the
    /// suggestion list on every keystroke of a two-way bound composer.
    /// </summary>
    private void SyncDraftFromParameter()
    {
        if (string.Equals(Text, _lastTextParameter, StringComparison.Ordinal))
        {
            return;
        }

        _lastTextParameter = Text;
        var incoming = Text ?? string.Empty;
        if (string.Equals(incoming, _text, StringComparison.Ordinal))
        {
            return;
        }

        _text = incoming;
        _caret = -1;
        CloseSuggestions();
        _mentions.Clear();
        _selectedCommand = null;
        _resizeAfterRender = true;
    }

    private void SyncModelFromParameter()
    {
        if (string.Equals(SelectedModel, _lastSelectedModelParameter, StringComparison.Ordinal))
        {
            return;
        }

        _lastSelectedModelParameter = SelectedModel;
        _selectedModel = SelectedModel;
    }

    // ── Model selector ─────────────────────────────────────────────────────────────────────────

    private IReadOnlyList<string> ModelList => Models ?? Array.Empty<string>();

    private string? EffectiveModel => _selectedModel;

    /// <summary>Whether a model picker renders at all (the built-in select, or the host template).</summary>
    private bool ShowModelSelector => ModelSelectorTemplate is null && ModelList.Count > 0;

    private async Task OnModelChangedAsync(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        _selectedModel = string.IsNullOrEmpty(value) ? null : value;
        if (SelectedModelChanged.HasDelegate)
        {
            await SelectedModelChanged.InvokeAsync(_selectedModel);
        }
    }

    // ── Submit ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the draft: builds the <see cref="AiComposerSubmit"/> from the text, the attachments, the model, the
    /// mentions and the recorded command, raises <see cref="OnSubmit"/> and clears the draft afterwards. It is a
    /// no-op while <see cref="Disabled"/> or <see cref="IsGenerating"/>, with a whitespace-only draft, without a
    /// bound handler, or while a previous submit is still in flight — <see cref="IsGenerating"/> is a parameter,
    /// so a fast second Enter or a double click would otherwise start two generations. Public so a host's own
    /// send control (or a shortcut) goes through the same path.
    /// </summary>
    /// <remarks>
    /// The post-submit clear is a compare against the snapshot that was actually sent, not a blind reset: text
    /// the reader typed while a slow handler ran, an attachment picked in the meantime and a mention added to a
    /// newer draft all survive. A handler that throws leaves the draft in place and re-arms the slot.
    /// </remarks>
    /// <returns>A task that completes when the handler finished and the state it owned was cleared.</returns>
    public async Task SubmitAsync()
    {
        if (!CanSubmit)
        {
            return;
        }

        var sentText = _text;
        var sentAttachments = _attachments.ToArray();
        var submit = new AiComposerSubmit
        {
            Text = sentText.Trim(),
            Attachments = sentAttachments,
            Model = _selectedModel,
            Mentions = _mentions.ToArray(),
            Command = _selectedCommand,
        };

        _submitting = true;
        try
        {
            // Render the disabled slot BEFORE the handler runs: the reader's next Enter or click has to hit a
            // Send control that is already out of commission.
            await InvokeAsync(StateHasChanged);
            await OnSubmit.InvokeAsync(submit);

            await ClearSentDraftAsync(sentText);
            await ClearSentAttachmentsAsync(sentAttachments);
        }
        finally
        {
            // The slot has to be re-armed in the DOM even when the handler threw: a Send control left disabled
            // by a failed generation would strand the reader with no way to try again.
            _submitting = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Clears the draft, the caret, the recorded mentions and the command, but only while the draft still is what
    /// was sent. Anything typed during the handler belongs to the next prompt.
    /// </summary>
    private async Task ClearSentDraftAsync(string sentText)
    {
        if (!string.Equals(_text, sentText, StringComparison.Ordinal))
        {
            return;
        }

        _text = string.Empty;
        _caret = -1;
        _mentions.Clear();
        _selectedCommand = null;
        CloseSuggestions();
        _announcement = null;
        _resizeAfterRender = true;

        if (TextChanged.HasDelegate)
        {
            await TextChanged.InvokeAsync(_text);
        }
    }

    /// <summary>
    /// Drops exactly the attachments that were submitted (compared by reference against the snapshot) and keeps
    /// the ones the reader added while the handler ran.
    /// </summary>
    private async Task ClearSentAttachmentsAsync(IReadOnlyList<AiComposerAttachment> sentAttachments)
    {
        var removed = false;
        foreach (var attachment in sentAttachments)
        {
            removed |= _attachments.Remove(attachment);
        }

        if (!removed || !AttachmentsChanged.HasDelegate)
        {
            return;
        }

        await AttachmentsChanged.InvokeAsync(_attachments.ToArray());
    }

    /// <summary>Handler of the built-in Stop control; a composer without a handler still renders the slot.</summary>
    private Task OnStopClickAsync() => OnStop.HasDelegate ? OnStop.InvokeAsync() : Task.CompletedTask;

    // ── Announcements ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the component's single live region. The paragraph itself is persistent and only its keyed child is
    /// replaced: a live region that is re-created with its text already in place is not announced, while a new
    /// child inserted into the existing region is — so even an identical wording is heard again.
    /// </summary>
    private void Announce(string text)
    {
        _announcement = text;
        _announcementSequence++;
    }
}
