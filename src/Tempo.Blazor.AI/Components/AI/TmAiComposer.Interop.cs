using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Interop half of <see cref="TmAiComposer"/> (plan 7f21f26b, Fáze 6). The package's ES module
/// <c>ai-composer.js</c> owns the Enter/IME guard, the autosize, the caret measurement of the suggestion listbox,
/// the Web Speech session and the opt-in virtual-keyboard tracker; this partial is the .NET side of that
/// contract. The module is imported through <see cref="IJSObjectReference"/> (no <c>&lt;script&gt;</c> tag, no
/// eval), and a failed import leaves the component fully usable: the C# keydown fallback keeps Enter working and
/// the autosize simply does not happen.
/// </summary>
public partial class TmAiComposer
{
    private const string InteropModulePath = "./_content/Tempo.Blazor.AI/js/ai-composer.js";

    private IJSObjectReference? _module;
    private DotNetObjectReference<TmAiComposer>? _self;
    private bool _attached;
    private int? _attachedMaxRows;
    private bool _interopUnavailable;
    private bool _speechChecked;
    private bool _speechSupported;
    private bool _isListening;
    private bool _viewportAttached;
    private bool _resizeAfterRender = true;
    private bool _placeSuggestionsAfterRender;
    private bool? _lastIsGenerating;
    private bool _focusActionSlotAfterRender;

    /// <summary>Whether the browser exposes the Web Speech API (probed once, and only when voice is opted in).</summary>
    private bool ShowVoiceButton => EnableVoiceInput && !Disabled && _speechSupported;

    /// <summary>The span that holds the one send/stop control — the focus scope of the slot swap.</summary>
    private ElementReference _actionSlotRef;

    /// <summary>The selector of the slot's replacement control (a disabled Send matches nothing).</summary>
    private const string ActionSlotSelector = "button:not([disabled])";

    /// <summary>
    /// Runs before the render, so it can still see the DOM the reader is in: when <see cref="IsGenerating"/>
    /// flips, the slot swaps Send for Stop (or back) and the control that held the focus leaves the tree. The
    /// answer decides whether the swap moves the focus to the replacement or leaves it alone (WCAG 2.4.3) — a
    /// generation started from the keyboard keeps the caret in the prompt.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        if (_lastIsGenerating is not { } previous)
        {
            _lastIsGenerating = IsGenerating;
            return;
        }

        if (previous == IsGenerating)
        {
            return;
        }

        _lastIsGenerating = IsGenerating;
        _focusActionSlotAfterRender = await IsActionSlotFocusedAsync();
    }

    private async Task<bool> IsActionSlotFocusedAsync()
    {
        var module = _module;
        if (module is null)
        {
            return false;
        }

        try
        {
            return await module.InvokeAsync<bool>("isFocusWithin", _actionSlotRef);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Moves the focus to the slot's replacement control. A disabled replacement (the empty-draft Send after a
    /// send or a stop) cannot take the focus, so the prompt is the fallback target instead of a lost focus.
    /// </summary>
    private async Task RestoreActionSlotFocusAsync(IJSObjectReference module)
    {
        if (!IsGenerating && !CanSubmit)
        {
            await FocusTextareaAsync();
            return;
        }

        try
        {
            await module.InvokeVoidAsync("focusFirst", _actionSlotRef, ActionSlotSelector);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            await FocusTextareaAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || _interopUnavailable)
        {
            return;
        }

        if (firstRender)
        {
            await ImportModuleAsync();
        }

        var module = _module;
        if (module is null)
        {
            _focusActionSlotAfterRender = false;
            return;
        }

        if (EnableVoiceInput && !Disabled && !_speechChecked)
        {
            await DetectSpeechAsync(module);
        }

        if (TrackVirtualKeyboard)
        {
            await AttachViewportTrackerAsync(module);
        }
        else
        {
            await DetachViewportTrackerAsync(module);
        }

        if (!_attached || _attachedMaxRows != MaxRows)
        {
            await AttachComposerAsync(module);
        }

        if (_resizeAfterRender)
        {
            _resizeAfterRender = false;
            await ResizeAsync(module);
        }

        if (_placeSuggestionsAfterRender && ShowSuggestions)
        {
            _placeSuggestionsAfterRender = false;
            await PlaceSuggestionsAsync(module);
        }

        if (_focusActionSlotAfterRender)
        {
            _focusActionSlotAfterRender = false;
            await RestoreActionSlotFocusAsync(module);
        }

        if (_focusTextareaAfterRender)
        {
            _focusTextareaAfterRender = false;
            await FocusTextareaAsync();
        }
    }

    private async Task ImportModuleAsync()
    {
        try
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", InteropModulePath);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            _interopUnavailable = true;
        }
    }

    private async Task AttachComposerAsync(IJSObjectReference module)
    {
        _self ??= DotNetObjectReference.Create(this);
        try
        {
            await module.InvokeVoidAsync(
                "attach",
                _textareaRef,
                _self,
                new { maxRows = MaxRows, submitMethod = nameof(OnComposerSubmit) });
            _attached = true;
            _attachedMaxRows = MaxRows;
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            _attached = false;
            _attachedMaxRows = null;
        }
    }

    private async Task ResizeAsync(IJSObjectReference module)
    {
        try
        {
            await module.InvokeVoidAsync("resize", _textareaRef, MaxRows);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // Static rendering: the textarea keeps the CSS height, the autosize is best effort.
        }
    }

    private async Task PlaceSuggestionsAsync(IJSObjectReference module)
    {
        try
        {
            await module.InvokeVoidAsync("placeSuggestions", _textareaRef, _suggestionsRef);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
        }
    }

    private async Task FocusTextareaAsync()
    {
        try
        {
            await _textareaRef.FocusAsync();
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // No interop (static rendering): the focus move is best effort.
        }
    }

    // ── Virtual keyboard ───────────────────────────────────────────────────────────────────────

    private async Task AttachViewportTrackerAsync(IJSObjectReference module)
    {
        if (_viewportAttached)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("attachViewportTracker", _rootRef, new { });
            _viewportAttached = true;
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            _viewportAttached = false;
        }
    }

    private async Task DetachViewportTrackerAsync(IJSObjectReference module)
    {
        if (!_viewportAttached)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("detachViewportTracker", _rootRef);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
        }
        finally
        {
            _viewportAttached = false;
        }
    }

    // ── Speech ────────────────────────────────────────────────────────────────────────────────

    private async Task DetectSpeechAsync(IJSObjectReference module)
    {
        _speechChecked = true;
        var supported = false;
        try
        {
            supported = await module.InvokeAsync<bool>("speechSupported");
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            supported = false;
        }

        if (supported == _speechSupported)
        {
            return;
        }

        _speechSupported = supported;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleVoiceAsync()
    {
        if (_isListening)
        {
            await StopListeningAsync();
            return;
        }

        var module = _module;
        if (module is null || Disabled)
        {
            return;
        }

        var started = false;
        try
        {
            _self ??= DotNetObjectReference.Create(this);
            started = await module.InvokeAsync<bool>(
                "startSpeech",
                _textareaRef,
                _self,
                new { language = SpeechLanguage });
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            started = false;
        }

        if (!started)
        {
            Announce(Loc["TmAiComposer_VoiceError"]);
            await InvokeAsync(StateHasChanged);
            return;
        }

        _isListening = true;
        _listeningAnnouncement = true;
        Announce(Loc["TmAiComposer_VoiceListening"]);
        await InvokeAsync(StateHasChanged);
    }

    private async Task StopListeningAsync()
    {
        _isListening = false;
        ClearListeningAnnouncement();
        var module = _module;
        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("stopSpeech", _textareaRef);
            }
            catch (Exception exception) when (IsInteropFailure(exception))
            {
            }
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Called by the module with a recognized transcript. The final transcript is inserted at the caret (with the
    /// separating space a typist would add) and raised through <see cref="TextChanged"/>; an interim result is
    /// ignored so the draft does not flicker between guesses.
    /// </summary>
    /// <param name="text">The recognized text.</param>
    /// <param name="isFinal">Whether the recognizer considers the phrase final.</param>
    /// <returns>A task that completes when the draft was updated.</returns>
    [JSInvokable]
    public Task OnSpeechResult(string? text, bool isFinal) => InvokeAsync(async () =>
    {
        if (_disposed || !isFinal || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var insertion = text.Trim();
        var caret = _caret < 0 ? _text.Length : Math.Clamp(_caret, 0, _text.Length);
        var prefix = _text[..caret];
        var suffix = _text[caret..];
        var spacer = caret > 0 && !char.IsWhiteSpace(_text[caret - 1]) ? " " : string.Empty;

        _text = prefix + spacer + insertion + suffix;
        _caret = (prefix + spacer + insertion).Length;
        _resizeAfterRender = true;

        if (TextChanged.HasDelegate)
        {
            await TextChanged.InvokeAsync(_text);
        }

        StateHasChanged();
    });

    /// <summary>Called by the module when the recognizer reports an error. The session is over and the failure is announced.</summary>
    /// <param name="error">The Web Speech error code (for example <c>not-allowed</c>).</param>
    /// <returns>A task that completes when the state was applied.</returns>
    [JSInvokable]
    public Task OnSpeechError(string? error) => InvokeAsync(() =>
    {
        if (_disposed)
        {
            return;
        }

        _isListening = false;
        _listeningAnnouncement = false;
        Announce(Loc["TmAiComposer_VoiceError"]);
        StateHasChanged();
    });

    /// <summary>Called by the module when the recognizer session ended (on its own or after a stop).</summary>
    /// <returns>A task that completes when the state was applied.</returns>
    [JSInvokable]
    public Task OnSpeechEnded() => InvokeAsync(() =>
    {
        if (_disposed)
        {
            return;
        }

        _isListening = false;
        ClearListeningAnnouncement();
        StateHasChanged();
    });

    /// <summary>
    /// Drops the "Listening…" note once the session is over: it is also the prompt's <c>aria-describedby</c>
    /// target, and a stale "listening" description next to a stopped microphone would be a lie.
    /// </summary>
    private void ClearListeningAnnouncement()
    {
        if (!_listeningAnnouncement)
        {
            return;
        }

        _listeningAnnouncement = false;
        _announcement = null;
    }

    /// <summary>
    /// Called by the module when its keydown listener accepted an Enter (the IME guard and the "is the combobox
    /// open?" check already ran there). Routes into the same <see cref="SubmitAsync"/> path as the Send control so
    /// exactly one activation happens per press; a host handler that throws is dispatched back to the renderer.
    /// </summary>
    /// <returns>A task that completes when the submit finished.</returns>
    [JSInvokable]
    public Task OnComposerSubmit() => InvokeAsync(async () =>
    {
        try
        {
            await SubmitAsync();
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    });

    private static bool IsInteropFailure(Exception exception)
        => exception is JSException or JSDisconnectedException or InvalidOperationException
            or NotSupportedException or ObjectDisposedException;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelMentionSearch();

        var module = _module;
        _module = null;

        if (module is not null)
        {
            if (_viewportAttached)
            {
                try
                {
                    await module.InvokeVoidAsync("detachViewportTracker", _rootRef);
                }
                catch (Exception exception) when (IsInteropFailure(exception))
                {
                }
            }

            try
            {
                await module.InvokeVoidAsync("stopSpeech", _textareaRef);
            }
            catch (Exception exception) when (IsInteropFailure(exception))
            {
            }

            if (_attached)
            {
                try
                {
                    await module.InvokeVoidAsync("detach", _textareaRef);
                }
                catch (Exception exception) when (IsInteropFailure(exception))
                {
                }
            }

            try
            {
                await module.DisposeAsync();
            }
            catch (Exception exception) when (IsInteropFailure(exception))
            {
            }
        }

        _viewportAttached = false;
        _attached = false;
        _attachedMaxRows = null;
        _self?.Dispose();
        _self = null;
    }
}
