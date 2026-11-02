using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Suggestion half of <see cref="TmAiComposer"/> (plan 7f21f26b, Fáze 6): the <c>/</c> slash-command list and
/// the host-provided <c>@</c> mentions share one combobox. The textarea keeps the DOM focus (APG combobox
/// pattern), the highlighted option is exposed through <c>aria-activedescendant</c>, and the module's keydown
/// only suppresses the native newline while the list is expanded — the C# handler below is the single owner of
/// the option activation.
/// </summary>
public partial class TmAiComposer
{
    /// <summary>Which suggestion source the open listbox shows.</summary>
    private enum SuggestionMode
    {
        None,
        Commands,
        Mentions,
    }

    private SuggestionMode _mode;
    private string _query = string.Empty;
    private int _triggerIndex = -1;
    private int _activeIndex;
    private IReadOnlyList<AiSlashCommand> _commandOptions = Array.Empty<AiSlashCommand>();
    private IReadOnlyList<AiMentionItem> _mentionOptions = Array.Empty<AiMentionItem>();
    private AiSlashCommand? _selectedCommand;
    private CancellationTokenSource? _mentionCts;
    private int _mentionGeneration;
    private bool _focusTextareaAfterRender;

    private IReadOnlyList<AiSlashCommand> CommandList => Commands ?? Array.Empty<AiSlashCommand>();

    /// <summary>Whether the listbox is rendered AND has a target for <c>aria-activedescendant</c>.</summary>
    private bool ShowSuggestions => _mode != SuggestionMode.None && OptionCount > 0;

    private int OptionCount => _mode switch
    {
        SuggestionMode.Commands => _commandOptions.Count,
        SuggestionMode.Mentions => _mentionOptions.Count,
        _ => 0,
    };

    private int ActiveIndex => OptionCount == 0 ? 0 : Math.Clamp(_activeIndex, 0, OptionCount - 1);

    private string? ActiveOptionId => ShowSuggestions ? OptionId(ActiveIndex) : null;

    private string SuggestionLabel => _mode == SuggestionMode.Commands
        ? Loc["TmAiComposer_Commands"]
        : Loc["TmAiComposer_Mentions"];

    private string OptionId(int index) => $"{_suggestionsId}-option-{index}";

    private string? OptionIcon(int index) => _mode switch
    {
        SuggestionMode.Commands => _commandOptions[index].Icon,
        SuggestionMode.Mentions => _mentionOptions[index].Icon,
        _ => null,
    };

    private string? OptionTitle(int index) => _mode switch
    {
        SuggestionMode.Commands => _commandOptions[index].Title,
        SuggestionMode.Mentions => _mentionOptions[index].Label,
        _ => null,
    };

    private string? OptionDescription(int index) => _mode switch
    {
        SuggestionMode.Commands => _commandOptions[index].Description,
        SuggestionMode.Mentions => _mentionOptions[index].Description,
        _ => null,
    };

    // ── Input and caret ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a textarea edit. The caret is unknown here (Blazor has no selection API), so the trigger token is
    /// looked up at the end of the draft; the module's own <c>OnCaretChanged</c> report refines it a moment
    /// later, which is what makes an edit in the middle of the draft behave the same way.
    /// </summary>
    private async Task OnInputAsync(ChangeEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        _text = args.Value?.ToString() ?? string.Empty;
        _caret = -1;
        _announcement = null;
        UpdateSuggestions();

        if (TextChanged.HasDelegate)
        {
            await TextChanged.InvokeAsync(_text);
        }
    }

    /// <summary>
    /// Called by the package's ES module after an input, click, keyup or selection change. The reported caret is
    /// the authority for the trigger token; it also re-positions the listbox after the render.
    /// </summary>
    /// <param name="caret">The textarea's <c>selectionStart</c>.</param>
    /// <returns>A task that completes when the state was applied.</returns>
    [JSInvokable]
    public Task OnCaretChanged(int caret) => InvokeAsync(() =>
    {
        if (_disposed)
        {
            return;
        }

        _caret = Math.Clamp(caret, 0, _text.Length);

        // A caret report arrives on every click, keyup and selection change. Re-rendering all of them would be a
        // Blazor round-trip per keystroke on a Server circuit, so the render happens only when the suggestion
        // state actually moved.
        var previousMode = _mode;
        var previousQuery = _query;
        var previousTrigger = _triggerIndex;
        var previousCount = OptionCount;
        var previousActive = _activeIndex;

        UpdateSuggestions();

        if (previousMode != _mode || previousQuery != _query || previousTrigger != _triggerIndex
            || previousCount != OptionCount || previousActive != _activeIndex)
        {
            StateHasChanged();
        }
    });

    // ── Trigger detection ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes the open suggestion source from the token in front of the caret. A trigger must start a
    /// whitespace-delimited token — <c>and/or</c> and <c>bob@example</c> are text, not commands or mentions.
    /// </summary>
    private void UpdateSuggestions()
    {
        if (Disabled)
        {
            CloseSuggestions();
            return;
        }

        var caret = _caret < 0 ? _text.Length : Math.Clamp(_caret, 0, _text.Length);
        var start = caret;
        while (start > 0 && !char.IsWhiteSpace(_text[start - 1]))
        {
            start--;
        }

        var token = _text[start..caret];
        if (token.Length == 0)
        {
            CloseSuggestions();
            return;
        }

        if (token[0] == '/' && CommandList.Count > 0)
        {
            OpenCommandSuggestion(start, token[1..]);
            return;
        }

        if (token[0] == '@' && MentionProvider is not null)
        {
            OpenMentionSuggestion(start, token[1..]);
            return;
        }

        CloseSuggestions();
    }

    private void OpenCommandSuggestion(int triggerIndex, string query)
    {
        var options = CommandList
            .Where(command => Matches(command, query))
            .ToArray();

        var sameQuery = _mode == SuggestionMode.Commands
            && _triggerIndex == triggerIndex
            && string.Equals(_query, query, StringComparison.Ordinal);

        CancelMentionSearch();
        _mode = SuggestionMode.Commands;
        _query = query;
        _triggerIndex = triggerIndex;
        _commandOptions = options;
        _mentionOptions = Array.Empty<AiMentionItem>();

        if (!sameQuery)
        {
            _activeIndex = 0;
        }

        if (options.Length == 0)
        {
            CloseSuggestions();
        }

        _placeSuggestionsAfterRender = true;
    }

    private static bool Matches(AiSlashCommand command, string query)
        => query.Length == 0
            || command.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (command.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private void OpenMentionSuggestion(int triggerIndex, string query)
    {
        var sameQuery = _mode == SuggestionMode.Mentions
            && _triggerIndex == triggerIndex
            && string.Equals(_query, query, StringComparison.Ordinal);

        _mode = SuggestionMode.Mentions;
        _query = query;
        _triggerIndex = triggerIndex;

        if (!sameQuery)
        {
            _activeIndex = 0;
            _mentionOptions = Array.Empty<AiMentionItem>();
            ScheduleMentionSearch(query);
        }

        _placeSuggestionsAfterRender = true;
    }

    /// <summary>
    /// Starts the debounced mention query. Every keystroke supersedes the previous one: the old token source is
    /// cancelled (so a host's in-flight request stops) and a generation counter drops a result that still lands
    /// after its query went away.
    /// </summary>
    private void ScheduleMentionSearch(string query)
    {
        CancelMentionSearch();

        var cts = new CancellationTokenSource();
        _mentionCts = cts;
        var generation = ++_mentionGeneration;
        _ = SearchMentionsAsync(query, generation, cts.Token);
    }

    private async Task SearchMentionsAsync(string query, int generation, CancellationToken token)
    {
        try
        {
            if (MentionDebounce > TimeSpan.Zero)
            {
                await Task.Delay(MentionDebounce, Clock, token);
            }

            var provider = MentionProvider;
            if (provider is null || token.IsCancellationRequested)
            {
                return;
            }

            var items = await provider.SearchAsync(query, token);
            if (token.IsCancellationRequested || _disposed)
            {
                return;
            }

            await InvokeAsync(() =>
            {
                if (generation != _mentionGeneration || _disposed)
                {
                    return;
                }

                _mentionOptions = items ?? Array.Empty<AiMentionItem>();
                _activeIndex = Math.Clamp(_activeIndex, 0, Math.Max(0, _mentionOptions.Count - 1));
                _placeSuggestionsAfterRender = true;
                StateHasChanged();
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query (or by closing the list) — the result is no longer wanted.
        }
        catch (Exception) when (!_disposed)
        {
            await InvokeAsync(() =>
            {
                if (generation != _mentionGeneration)
                {
                    return;
                }

                CloseSuggestions();
                Announce(Loc["TmAiComposer_MentionError"]);
                StateHasChanged();
            });
        }
    }

    private void CancelMentionSearch()
    {
        _mentionGeneration++;
        var cts = _mentionCts;
        _mentionCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already released; nothing to cancel.
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// Closes the listbox and releases the pending query. The draft is untouched: Escape hides the suggestions,
    /// it does not discard what the reader typed.
    /// </summary>
    private void CloseSuggestions()
    {
        CancelMentionSearch();
        _mode = SuggestionMode.None;
        _query = string.Empty;
        _triggerIndex = -1;
        _activeIndex = 0;
        _commandOptions = Array.Empty<AiSlashCommand>();
        _mentionOptions = Array.Empty<AiMentionItem>();
    }

    // ── Keyboard ───────────────────────────────────────────────────────────────────────────────

    private async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        if (ShowSuggestions)
        {
            switch (args.Key)
            {
                case "ArrowDown":
                    MoveActive(1);
                    return;
                case "ArrowUp":
                    MoveActive(-1);
                    return;
                case "Home":
                    SetActive(0);
                    return;
                case "End":
                    SetActive(OptionCount - 1);
                    return;
                case "Enter":
                    if (IsImeComposition(args) || args.ShiftKey || args.AltKey || args.CtrlKey || args.MetaKey)
                    {
                        return;
                    }

                    await SelectActiveSuggestionAsync();
                    return;
                case "Escape":
                case "Tab":
                    CloseSuggestions();
                    return;
            }

            return;
        }

        if (args.Key != "Enter" || IsImeComposition(args))
        {
            return;
        }

        if (args.ShiftKey || args.AltKey || args.CtrlKey || args.MetaKey)
        {
            return;
        }

        // Once the module attached, its keydown listener owns Enter (it alone can see the IME composition of a
        // net8 host) and calls OnComposerSubmit. The C# handler is the pre-attach fallback.
        if (_attached)
        {
            return;
        }

        await SubmitAsync();
    }

    private void MoveActive(int delta)
    {
        if (OptionCount == 0)
        {
            return;
        }

        var next = (ActiveIndex + delta) % OptionCount;
        if (next < 0)
        {
            next += OptionCount;
        }

        SetActive(next);
    }

    private void SetActive(int index)
    {
        _activeIndex = Math.Clamp(index, 0, Math.Max(0, OptionCount - 1));
        _placeSuggestionsAfterRender = true;
    }

    /// <summary>
    /// The IME guard of the C# fallback. <c>KeyboardEventArgs.IsComposing</c> exists only on .NET 9+, so a net8
    /// host relies on the module's native guard; this keeps the behaviour identical where the flag exists.
    /// </summary>
    private static bool IsImeComposition(KeyboardEventArgs args) =>
#if NET9_0_OR_GREATER
        args.IsComposing;
#else
        false;
#endif

    // ── Selection ─────────────────────────────────────────────────────────────────────────────

    private Task SelectActiveSuggestionAsync() => SelectSuggestionAsync(ActiveIndex);

    /// <summary>
    /// Activates one option: a slash command inserts its prompt text and stays recorded for the submit; a
    /// mention replaces the typed <c>@query</c> with <c>@Label</c> and is recorded once. The focus returns to the
    /// prompt after the render, because the pointer path can activate an option too.
    /// </summary>
    /// <param name="index">Index into the currently open option list.</param>
    /// <returns>A task that completes when the draft was updated and re-rendered.</returns>
    private async Task SelectSuggestionAsync(int index)
    {
        if (!ShowSuggestions || index < 0 || index >= OptionCount)
        {
            return;
        }

        if (_mode == SuggestionMode.Commands)
        {
            var command = _commandOptions[index];
            InsertAtTrigger(command.Prompt ?? $"/{command.Title}");
            _selectedCommand = command;
        }
        else
        {
            var mention = _mentionOptions[index];
            InsertAtTrigger($"@{mention.Label}");
            if (!_mentions.Any(existing => string.Equals(existing.Id, mention.Id, StringComparison.Ordinal)))
            {
                _mentions.Add(mention);
            }
        }

        CloseSuggestions();
        _focusTextareaAfterRender = true;
        _resizeAfterRender = true;

        if (TextChanged.HasDelegate)
        {
            await TextChanged.InvokeAsync(_text);
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Replaces the trigger token (<c>/…</c> or <c>@…</c>) in front of the caret with <paramref name="insertion"/>
    /// and adds the separating space the reader would otherwise type.
    /// </summary>
    private void InsertAtTrigger(string insertion)
    {
        var start = Math.Clamp(_triggerIndex, 0, _text.Length);
        var caret = _caret < 0 ? _text.Length : Math.Clamp(_caret, start, _text.Length);
        var prefix = _text[..start];
        var suffix = _text[caret..];

        var spacer = insertion.Length > 0 && !insertion.EndsWith(' ') && !suffix.StartsWith(' ')
            ? " "
            : string.Empty;

        _text = prefix + insertion + spacer + suffix;
        _caret = (prefix + insertion + spacer).Length;
    }
}
