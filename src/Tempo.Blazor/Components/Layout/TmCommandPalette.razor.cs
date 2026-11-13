using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// Searchable command palette overlay (Cmd+K style). Local <see cref="Actions"/> filter
/// synchronously (title, description, code); an optional asynchronous <see cref="SearchProvider"/>
/// extends the list with remote results (appended after the local ones), with debounce,
/// cancellation of superseded requests, loading / empty / transient-error states and a polite
/// result-count announcement. All new capabilities are opt-in except <see cref="ShowIcons"/>
/// (default true since 2.11.0 — see the CHANGELOG "Breaking / Migration" section).
/// </summary>
public partial class TmCommandPalette : IAsyncDisposable
{
    private string _query = string.Empty;
    private List<ICommandPaletteAction> _filtered = new();

    // Async results are kept apart from the synchronous local matches: local results render
    // immediately and always first (DEC-CMDPALETTE-ASYNC O3); the combined list is rebuilt on
    // every change.
    private List<ICommandPaletteAction> _asyncResults = [];
    private CommandPaletteFetchState _fetchState = CommandPaletteFetchState.Ok;
    private string? _liveMessage;

    private int _highlightedIndex;

    // Stable per instance so a group's aria-labelledby survives re-renders and two palettes on one
    // page never share label ids.
    private readonly string _groupLabelPrefix = $"tm-palette-group-{Guid.NewGuid():N}";

    // Combobox wiring (cluster-B §12, f4057a47): the input is a role=combobox whose
    // aria-activedescendant names the highlighted option, so the options need stable ids.
    private readonly string _listId = $"tm-palette-list-{Guid.NewGuid():N}";

    private string OptionId(int index) => $"{_listId}-opt-{index}";

    // ── Async search pipeline state ──────────────────────────────────────────

    // Debounce timer (Clock-driven so tests can run it from a FakeTimeProvider).
    private ITimer? _searchTimer;

    // Cancellation of the in-flight provider call.
    private CancellationTokenSource? _searchCts;

    // Monotonic request number: a response is applied only when it still belongs to the newest
    // request, so a stale response is dropped even when the provider ignored the token.
    private int _requestSeq;

    // Internal (provider-driven) half of the effective loading state; the host's IsLoading ORs in.
    private bool _searching;

    /// <summary>Effective loading state: an internal fetch is running or the host said IsLoading.</summary>
    private bool Loading => _searching || IsLoading;

    /// <summary>
    /// Clock driving the debounce timer. Resolved through <see cref="IServiceProvider"/> so a
    /// consumer that never registers a <see cref="TimeProvider"/> still gets
    /// <see cref="TimeProvider.System"/> — a bare <c>[Inject] TimeProvider</c> would throw for
    /// exactly those consumers. Tests register a FakeTimeProvider.
    /// </summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    private TimeProvider Clock
        => _clock ??= Services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
    private TimeProvider? _clock;

    // ── Existing chrome state ────────────────────────────────────────────────

    private TmFocusScope? _scope;
    private ElementReference _backdrop;
    private ElementReference _input;

    // Set by the retry: the retry button unmounts with the alert, so focus would fall to <body>;
    // the next render moves it to the search input instead.
    private bool _focusInputAfterRender;

    // True while at least one visible item carries an icon: only then is the icon slot rendered
    // (an all-icon-less list gets no dead gutter).
    private bool _anyIcon;
    private PromotedScopeGate? _gate;
    private bool _wasOpen;

    // Where the active press started. The backdrop sets it on its own pointerdown; the element
    // fence around the panel resets it (and stops the event), so the click handler below can tell
    // an outside click from the end of a drag-select that started on the panel.
    private bool _pressOnBackdrop;

    /// <summary>Whether the command palette is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Fires when IsOpen should change (two-way binding).</summary>
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    /// <summary>Actions available in the palette.</summary>
    [Parameter] public IEnumerable<ICommandPaletteAction>? Actions { get; set; }

    /// <summary>
    /// Groups actions by <see cref="ICommandPaletteAction.Category"/>: a heading per category and
    /// a divider (CSS border) between groups. A null/empty category renders its options without a
    /// group wrapper, because a <c>role=group</c> must be named. Defaults to false, which keeps the
    /// historical flat list.
    /// </summary>
    [Parameter] public bool GroupByCategory { get; set; }

    /// <summary>
    /// Renders each <c>+</c>-separated part of a shortcut as its own <c>kbd</c>. Defaults to false,
    /// which keeps the shortcut in a single <c>kbd</c>.
    /// </summary>
    [Parameter] public bool SplitShortcuts { get; set; }

    // ── TG-010 additions (DEC-CMDPALETTE-ASYNC) ──────────────────────────────

    /// <summary>
    /// Renders <see cref="ICommandPaletteAction.Icon"/> through <c>TmIcon</c> at the leading edge of
    /// each item; an item without an icon gets an empty slot of the same width so titles line up.
    /// Defaults to true (the icon existed in the model since 2.0 and was silently ignored before
    /// 2.11.0 — a visual breaking change, see the CHANGELOG "Breaking / Migration" section).
    /// </summary>
    [Parameter] public bool ShowIcons { get; set; } = true;

    /// <summary>
    /// Asynchronous search source: invoked with the debounced query and a cancellation token that
    /// is cancelled when the query changes, the palette closes or the component disposes. Results
    /// are appended after the local <see cref="Actions"/> matches. Null (the default) keeps the
    /// palette purely local.
    /// </summary>
    [Parameter] public Func<string, CancellationToken, Task<CommandPaletteSearchResult>>? SearchProvider { get; set; }

    /// <summary>Debounce delay in milliseconds from the last keystroke before the provider runs. 0 calls it immediately. Defaults to 300.</summary>
    [Parameter] public int DebounceMs { get; set; } = 300;

    /// <summary>
    /// Minimum query length that triggers <see cref="SearchProvider"/>. 0 calls the provider even
    /// for the empty query (e.g. "recent items"). Defaults to 1.
    /// </summary>
    [Parameter] public int MinChars { get; set; } = 1;

    /// <summary>Host-driven loading state (for hosts that fill <see cref="Actions"/> themselves); ORs with the internal provider state.</summary>
    [Parameter] public bool IsLoading { get; set; }

    /// <summary>Overrides the default loading rows (three <c>TmSkeleton</c> bars) shown while a search runs.</summary>
    [Parameter] public RenderFragment? LoadingTemplate { get; set; }

    /// <summary>Overrides the empty state; receives the current query.</summary>
    [Parameter] public RenderFragment<string>? EmptyTemplate { get; set; }

    /// <summary>Overrides the inline transient-error state; receives the retry callback.</summary>
    [Parameter] public RenderFragment<Func<Task>>? TransientErrorTemplate { get; set; }

    /// <summary>Overrides the trailing slot of each item (badge + code by default).</summary>
    [Parameter] public RenderFragment<ICommandPaletteAction>? ItemTrailingTemplate { get; set; }

    /// <summary>Overrides the whole content of each item; the option wrapper (role, id, aria-selected, click) stays the component's.</summary>
    [Parameter] public RenderFragment<ICommandPaletteAction>? ItemTemplate { get; set; }

    /// <summary>Marks the query in the title and the code via <c>TmHighlightedText</c>. Defaults to false.</summary>
    [Parameter] public bool HighlightMatches { get; set; }

    /// <summary>Renders the keyboard-hint footer (↑↓ navigate, ↵ select, Esc close). Defaults to false.</summary>
    [Parameter] public bool ShowFooter { get; set; }

    /// <summary>Overrides the footer content; renders even without <see cref="ShowFooter"/>.</summary>
    [Parameter] public RenderFragment? FooterTemplate { get; set; }

    /// <summary>Additional CSS class for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes for the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Maps the model's badge tone (Abstractions, UI-free) to the core TmBadge variant.</summary>
    private static BadgeVariant BadgeVariantFor(CommandPaletteBadgeTone tone) => tone switch
    {
        CommandPaletteBadgeTone.Primary => BadgeVariant.Primary,
        CommandPaletteBadgeTone.Success => BadgeVariant.Success,
        CommandPaletteBadgeTone.Warning => BadgeVariant.Warning,
        CommandPaletteBadgeTone.Danger => BadgeVariant.Danger,
        CommandPaletteBadgeTone.Info => BadgeVariant.Info,
        _ => BadgeVariant.Default,
    };

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // Actions replaced while open (host refilled them): the highlight follows its action by Id.
        var keptId = _wasOpen && IsOpen ? HighlightedId() : null;

        if (IsOpen && !_wasOpen)
        {
            _wasOpen = true;
            // Fresh state every time the palette opens: empty query, first item highlighted, no
            // stale async results or announcements from the previous open.
            _query = string.Empty;
            _highlightedIndex = 0;
            _asyncResults = [];
            _fetchState = CommandPaletteFetchState.Ok;
            _liveMessage = null;
        }
        else if (!IsOpen && _wasOpen)
        {
            _wasOpen = false;
            CancelSearchTimer();
            CancelRequest();
            _searching = false;
        }

        RecomputeFiltered();
        if (keptId is not null)
        {
            RestoreHighlightById(keptId);
        }
    }

    private Task HandleInputAsync(ChangeEventArgs e)
    {
        // The highlight follows the action it points at: kept by Id while the action survives the
        // query change, reset to the first item when it does not (the classic combobox behavior is
        // "first item highlighted" — for the items that actually remain).
        var highlightedId = HighlightedId();
        _query = e.Value?.ToString() ?? string.Empty;
        _fetchState = CommandPaletteFetchState.Ok;
        CancelSearchTimer();
        RecomputeFiltered();
        RestoreHighlightById(highlightedId);

        // MinChars counts the TRIMMED query: whitespace alone is not a search term. The query is
        // captured once so a later keystroke cannot change what an armed timer searches for.
        var query = _query;
        if (SearchProvider is null || query.Trim().Length < MinChars)
        {
            // Below the provider threshold (or no provider at all): cancel anything in flight and
            // answer from the local list alone. The recompute above already ran; it is repeated
            // only when stale async results have to be dropped.
            CancelRequest();
            _searching = false;
            if (_asyncResults.Count > 0)
            {
                _asyncResults = [];
                RecomputeFiltered();
                RestoreHighlightById(highlightedId);
            }

            return Task.CompletedTask;
        }

        if (DebounceMs <= 0)
        {
            // Fire-and-forget on purpose (no debounce), but never unobserved.
            _ = DispatchExceptionAsync(() => StartSearchAsync(query));
            return Task.CompletedTask;
        }

        // Single-shot timer on the injected clock. The callback closes over `timer` itself so the
        // ReferenceEquals check inside can tell "the currently armed timer" apart from this one —
        // a callback already queued when a newer keystroke cancelled it must not deliver.
        ITimer? timer = null;
        timer = Clock.CreateTimer(
            _ => _ = OnDebounceElapsedAsync(query, timer!),
            null,
            TimeSpan.FromMilliseconds(DebounceMs),
            Timeout.InfiniteTimeSpan);
        _searchTimer = timer;
        // The wait before the provider runs already counts as loading (skeleton + busy listbox).
        _searching = true;
        return Task.CompletedTask;
    }

    private async Task OnDebounceElapsedAsync(string query, ITimer timer)
    {
        // The callback runs on a thread-pool thread (TimeProvider.System) or the test thread
        // advancing a FakeTimeProvider; hop to the renderer before touching component state.
        await DispatchExceptionAsync(() => InvokeAsync(async () =>
        {
            if (!ReferenceEquals(_searchTimer, timer))
            {
                return;
            }

            _searchTimer = null;
            timer.Dispose();
            await StartSearchAsync(query);
        }));
    }

    /// <summary>
    /// Runs a fire-and-forget step so a failure is observed instead of surfacing as an unobserved
    /// task exception. A provider failure is already the transient state inside
    /// <see cref="StartSearchAsync"/>; anything else here (a disposed renderer, a localizer throw)
    /// is swallowed after a best-effort fall-back to the transient state.
    /// </summary>
    private async Task DispatchExceptionAsync(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Superseded, closed or disposed: nothing to report.
        }
        catch (Exception)
        {
            try
            {
                await InvokeAsync(() =>
                {
                    _searching = false;
                    _fetchState = CommandPaletteFetchState.Transient;
                    StateHasChanged();
                });
            }
            catch (Exception)
            {
                // The renderer is gone; there is nothing left to update.
            }
        }
    }

    private async Task StartSearchAsync(string query)
    {
        CancelRequest();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var seq = ++_requestSeq;

        _searching = true;
        _liveMessage = Loc["TmCommandPalette_Loading"];
        StateHasChanged();

        try
        {
            var result = await SearchProvider!(query, cts.Token);
            if (cts.IsCancellationRequested || seq != _requestSeq)
            {
                return;
            }

            _fetchState = result.State;
            _asyncResults = result.State == CommandPaletteFetchState.Ok ? result.Items.ToList() : [];
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested || seq != _requestSeq)
        {
            // Superseded by a newer keystroke, a close or a dispose — silently ignored.
            return;
        }
        catch (Exception)
        {
            // Any other failure is a transient outage, never "no results"; the component never
            // retries on its own — the user or the host does.
            if (cts.IsCancellationRequested || seq != _requestSeq)
            {
                return;
            }

            _fetchState = CommandPaletteFetchState.Transient;
            _asyncResults = [];
        }
        finally
        {
            if (seq == _requestSeq)
            {
                // A newer keystroke may already have armed the debounce timer: keep loading then.
                _searching = _searchTimer is not null;
            }
        }

        // The highlight follows the action it pointed at: kept by Id when the new result set still
        // contains it, reset to the first item otherwise.
        var highlightedId = HighlightedId();
        RecomputeFiltered();
        RestoreHighlightById(highlightedId);

        _liveMessage = Loc.Plural("TmCommandPalette_ResultCount", _filtered.Count);
        StateHasChanged();
    }

    private string? HighlightedId()
        => _highlightedIndex >= 0 && _highlightedIndex < _filtered.Count
            ? _filtered[_highlightedIndex].Id
            : null;

    private void RestoreHighlightById(string? highlightedId)
    {
        if (_filtered.Count == 0)
        {
            _highlightedIndex = 0;
            return;
        }

        var kept = highlightedId is not null ? _filtered.FindIndex(a => a.Id == highlightedId) : -1;
        _highlightedIndex = kept >= 0 ? kept : 0;
    }

    private Task RetrySearchAsync()
    {
        if (SearchProvider is null || _query.Trim().Length < MinChars)
        {
            return Task.CompletedTask;
        }

        // Leave the error state at once: the alert (and the retry button that holds focus)
        // unmounts, so the skeleton shows while the new request runs and focus moves to the input.
        _fetchState = CommandPaletteFetchState.Ok;
        _focusInputAfterRender = true;
        return StartSearchAsync(_query);
    }

    private void CancelSearchTimer()
    {
        _searchTimer?.Dispose();
        _searchTimer = null;
    }

    private void CancelRequest()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    private void RecomputeFiltered()
    {
        var source = Actions ?? Enumerable.Empty<ICommandPaletteAction>();
        var matched = string.IsNullOrWhiteSpace(_query)
            ? source
            : source.Where(a => a.Title.Contains(_query, StringComparison.OrdinalIgnoreCase)
                             || (a.Description?.Contains(_query, StringComparison.OrdinalIgnoreCase) == true)
                             || (a.Code?.Contains(_query, StringComparison.OrdinalIgnoreCase) == true));

        // Local matches always come first; the async results are appended after them (O3).
        // Grouping keeps the caller's order and only pulls a category's items together, so a list
        // that already arrives grouped does not get reordered.
        var combined = matched.Concat(_asyncResults);
        _filtered = GroupByCategory
            ? combined.GroupBy(GroupOf).SelectMany(group => group).ToList()
            : combined.ToList();

        _anyIcon = _filtered.Any(a => !string.IsNullOrEmpty(a.Icon));

        if (_highlightedIndex >= _filtered.Count)
        {
            _highlightedIndex = Math.Max(0, _filtered.Count - 1);
        }
    }

    private IEnumerable<(string Label, List<int> Indexes)> GroupedActions()
    {
        var groups = new List<(string Label, List<int> Indexes)>();
        for (var i = 0; i < _filtered.Count; i++)
        {
            var label = GroupOf(_filtered[i]);
            if (groups.Count == 0 || groups[^1].Label != label)
            {
                groups.Add((label, new List<int>()));
            }

            groups[^1].Indexes.Add(i);
        }

        return groups;
    }

    // Escape is the focus scope's, document-level, so it closes the palette wherever focus sits.
    // A container-level Escape handler on top of it closed the palette twice. Arrow/Enter
    // navigation stays on the search input: the close control is a native <button>, and a
    // container-level Enter case ran the highlighted action when the user pressed Enter on "close".
    // Arrow/Enter keydowns inside a text input produce no native click, so
    // this input-level handler is where list navigation + execution belong.
    private async Task HandleInputKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowDown":
                if (_filtered.Count > 0)
                {
                    _highlightedIndex = (_highlightedIndex + 1) % _filtered.Count;
                }
                break;
            case "ArrowUp":
                if (_filtered.Count > 0)
                {
                    _highlightedIndex = (_highlightedIndex - 1 + _filtered.Count) % _filtered.Count;
                }
                break;
            case "Enter":
                if (_highlightedIndex >= 0 && _highlightedIndex < _filtered.Count)
                {
                    var action = _filtered[_highlightedIndex];
                    // Enter keydown is followed by a keypress in the SAME browser task. Closing
                    // right here restores focus to the opener (a native button) before that
                    // keypress lands, and the button's own Enter activation re-opens the palette
                    // the key just closed (docs/keyboard-activation-convention.md, first section).
                    // Yielding to a later task lets the keypress finish on the input first. The
                    // alternatives are weighed in docs/keyboard-activation-convention.md
                    // ("Closing on Enter: why a Task.Delay").
                    await Task.Delay(1);
                    await ExecuteAsync(action);
                }
                break;
        }
    }

    private async Task ExecuteAsync(ICommandPaletteAction action)
    {
        // Close through the same path as Escape/backdrop: it cancels a possibly in-flight search
        // before the host unmounts us.
        await CloseAsync();
        await action.Execute();
    }

    private async Task CloseAsync()
    {
        CancelSearchTimer();
        CancelRequest();
        _searching = false;
        await IsOpenChanged.InvokeAsync(false);
    }

    // The click-to-dismiss path. A click lands on the backdrop both for a plain outside click AND
    // for a drag-select that started on the input and released over the backdrop; only the former
    // may close (review, phase 7), and the flag says which one this is.
    private Task HandleBackdropClickAsync()
    {
        if (!_pressOnBackdrop)
        {
            return Task.CompletedTask;
        }

        _pressOnBackdrop = false;
        return CloseAsync();
    }

    private static string GroupOf(ICommandPaletteAction action) => action.Category ?? string.Empty;

    // "Ctrl++" is Ctrl and the plus key: a trailing separator is itself a key, so only the empty
    // parts that a doubled separator leaves in the MIDDLE are dropped.
    private static IEnumerable<string> SplitShortcut(string shortcut)
    {
        var parts = shortcut.Split('+');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (part.Length > 0)
            {
                yield return part;
            }
            else if (i == parts.Length - 1 && parts.Length > 1)
            {
                yield return "+";
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Cold start: import the focus-trap module and install its document-level opener
            // records NOW, so the FIRST open of the session restores its opener.
            await FocusTrap.EnsureLoadedAsync(JS);
        }

        if (_focusInputAfterRender)
        {
            _focusInputAfterRender = false;
            if (IsOpen)
            {
                try
                {
                    await _input.FocusAsync();
                }
                catch (Exception ex) when (ex is JSException or InvalidOperationException or ObjectDisposedException)
                {
                    // The input unmounted (palette closed) between the render and the call.
                }
            }
        }

        // U1: promote the backdrop right after the render that inserted it. Closing unmounts the
        // whole subtree, and a detached top-layer element is hidden by the browser automatically —
        // no demote call is needed. The gate also holds the trap back until the promote completed
        // (c51ddf81 #1) and drops a promote that finished after the close (c51ddf81 #2).
        _gate ??= new PromotedScopeGate(JS);
        if (await _gate.AfterRenderAsync(IsOpen, () => IsOpen, _backdrop))
        {
            StateHasChanged();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        CancelSearchTimer();
        CancelRequest();
        if (_scope is not null) await _scope.DisposeAsync();
        if (_gate is not null) await _gate.DisposeAsync();
    }
}
