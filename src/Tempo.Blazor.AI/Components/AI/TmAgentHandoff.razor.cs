using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// A run handed from agent to agent as ONE chain: a labelled step per handoff — who handed what to whom, when, how the
/// receiving agent answered — with the things that travelled and the things that were lost (and why) behind a Details
/// button on every step, plus a chain-wide loss summary that names what the run actually dropped.
/// <para>
/// The surface renders the steps it is given and never polls or mutates anything: the host owns the handoff data and
/// the transport, the component owns its view. The step ordering is the host's own chain order — the component never
/// reorders, filters or invents a step.
/// </para>
/// <para>
/// Accessibility: the chain is an ordered list of labelled steps, every status and every item kind is an ICON plus a
/// localized WORD plus a tone (colour is never the only carrier, WCAG 1.4.1), and the passed/lost lists live in a
/// region a native <c>&lt;button&gt;</c> opens and closes with <c>aria-expanded</c> and <c>aria-controls</c> — no key
/// emulation anywhere (docs/keyboard-activation-convention.md). The table view carries exactly the same facts without
/// the geometry, so a screen-reader, keyboard-only or phone reader loses nothing. ONE polite live region speaks only
/// when a step the reader was on disappears from the host's data and the focus has to move.
/// </para>
/// </summary>
public partial class TmAgentHandoff : TmComponentBase
{
    private readonly string _id = $"tm-ai-handoff-{Guid.NewGuid():n}"[..26];

    private List<HandoffStep> _steps = [];
    private HandoffChainSummary _summary = HandoffChainSummary.Empty;

    private readonly HashSet<int> _expandedPositions = [];
    private IReadOnlyList<string>? _lastExpandedParameter;
    private bool _expandedSeeded;

    private AgentHandoffViewMode _viewMode = AgentHandoffViewMode.Chain;
    private AgentHandoffViewMode _lastViewModeParameter = AgentHandoffViewMode.Chain;

    private int _focusedPosition;
    private string? _focusedStepId;
    private bool _toggleHadFocus;
    private int _pendingFocusPosition;
    private readonly Dictionary<int, ToggleHandle> _handles = new();

    private string _announcement = string.Empty;
    private bool _announcementMarker;

    private ILogger? _logger;
    private bool _loggerResolved;
    private bool _duplicateWarned;

    /// <summary>
    /// Optional logging: resolved through <see cref="IServiceProvider"/> so a consumer without logging registered simply
    /// gets no warning — never a missing-service exception.
    /// </summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The handoffs, in chain order. Each entry is one step; null or empty renders the localized empty state, and a null
    /// entry inside the list is skipped. The component never mutates or reorders the list.
    /// </summary>
    [Parameter] public IReadOnlyList<HandoffStep>? Steps { get; set; }

    /// <summary>
    /// Which way the chain runs. Default <see cref="HandoffOrientation.Vertical"/>. The orientation is a layout hook on
    /// the root — both values render the same ordered list, so nothing is lost when a narrow card turns the chain on its
    /// side.
    /// </summary>
    [Parameter] public HandoffOrientation Orientation { get; set; } = HandoffOrientation.Vertical;

    /// <summary>
    /// The presentation, two-way bound with <see cref="ViewModeChanged"/> (<c>@bind-ViewMode</c>). Default
    /// <see cref="AgentHandoffViewMode.Chain"/>. <see cref="AgentHandoffViewMode.Table"/> renders the same steps as one
    /// table, which is the text alternative of the chain.
    /// </summary>
    [Parameter] public AgentHandoffViewMode ViewMode { get; set; } = AgentHandoffViewMode.Chain;

    /// <summary>Fires with the new presentation when the reader uses the View switcher.</summary>
    [Parameter] public EventCallback<AgentHandoffViewMode> ViewModeChanged { get; set; }

    /// <summary>
    /// The heading above the surface. Null renders the localized default ("Agent handoffs"); the heading also names the
    /// surface, so the name a reader hears is the one they see.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// Whether the summary block is rendered. Default <c>true</c>. It carries the settled / total handoff count, the
    /// passed and lost totals, the loss rate and whether anything is still pending.
    /// </summary>
    [Parameter] public bool ShowSummary { get; set; } = true;

    /// <summary>
    /// The ids of the steps whose detail is open, two-way bound with <see cref="ExpandedStepIdsChanged"/>. Null (the
    /// default) means EVERY step is open, because the passed and lost lists are the reason to look at a chain at all;
    /// an empty list closes them all. A host that pins the list keeps control: the reader's toggle reports the new set
    /// and the next render decides.
    /// </summary>
    [Parameter] public IReadOnlyList<string>? ExpandedStepIds { get; set; }

    /// <summary>Fires with the new expanded set when the reader opens or closes a step's detail.</summary>
    [Parameter] public EventCallback<IReadOnlyList<string>?> ExpandedStepIdsChanged { get; set; }

    /// <summary>
    /// The culture that formats the handoff times. Null (the default) means the current culture at render time.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The chain roll-up the summary block prints — the same numbers the steps show.</summary>
    public HandoffChainSummary SummaryData => _summary;

    /// <summary>The steps being rendered, in chain order (null entries removed).</summary>
    public IReadOnlyList<HandoffStep> VisibleSteps => _steps;

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    public int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>
    /// The step heading level: ONE below the surface heading (and never past h6), so a step is a real section of the
    /// surface and a host that renders the surface at h2 cannot end up with a skipped level.
    /// </summary>
    public int StepHeadingLevel => Math.Min(6, EffectiveHeadingLevel + 1);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    public string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmAgentHandoff_Title"] : Title!;

    private string TitleId => _id + "-title";

    private string InstructionsId => _id + "-instructions";

    private CultureInfo FormatCulture => Culture ?? CultureInfo.CurrentCulture;

    private string OrientationSlug => Orientation == HandoffOrientation.Horizontal ? "horizontal" : "vertical";

    private string ViewSlug => _viewMode == AgentHandoffViewMode.Table ? "table" : "chain";

    private string RootClass
    {
        get
        {
            var root = "tm-ai-handoff tm-ai-handoff--" + OrientationSlug;
            return string.IsNullOrWhiteSpace(Class) ? root : root + " " + Class;
        }
    }

    private IReadOnlyList<SegmentedOption<AgentHandoffViewMode>> ViewOptions =>
    [
        new() { Label = Loc["TmAgentHandoff_ViewChain"], Value = AgentHandoffViewMode.Chain },
        new() { Label = Loc["TmAgentHandoff_ViewTable"], Value = AgentHandoffViewMode.Table },
    ];

    private string StepsValue => Loc["TmAgentHandoff_SummaryStepsValue",
        _summary.SettledCount.ToString(CultureInfo.CurrentCulture),
        _summary.StepCount.ToString(CultureInfo.CurrentCulture)];

    private string LossRateValue => Loc["TmAgentHandoff_SummaryRate", PercentText(_summary.LossRate)];

    private string StateValue => _summary.HasPending
        ? Loc["TmAgentHandoff_SummaryPending", _summary.PendingCount.ToString(CultureInfo.CurrentCulture)]
        : Loc["TmAgentHandoff_SummaryComplete"];

    private bool IsEmpty => _steps.Count == 0;

    private bool ShowBody => !IsEmpty;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var previous = _steps;
        _steps = Steps is null ? [] : [.. Steps.Where(step => step is not null)];

        if (ViewMode != _lastViewModeParameter)
        {
            _lastViewModeParameter = ViewMode;
            _viewMode = IsKnownViewMode(ViewMode) ? ViewMode : AgentHandoffViewMode.Chain;
        }

        DetectDuplicateIds();

        if (!_expandedSeeded || !ReferenceEquals(ExpandedStepIds, _lastExpandedParameter))
        {
            _expandedSeeded = true;
            _lastExpandedParameter = ExpandedStepIds;
            _expandedPositions.Clear();

            if (ExpandedStepIds is null)
            {
                for (var position = 1; position <= _steps.Count; position++)
                {
                    _expandedPositions.Add(position);
                }
            }
            else
            {
                var idSet = new HashSet<string>(_steps.Select(step => step.Id), StringComparer.Ordinal);
                foreach (var id in ExpandedStepIds)
                {
                    if (!string.IsNullOrEmpty(id) && idSet.Contains(id))
                    {
                        var position = IndexOfId(_steps, id) + 1;
                        if (position > 0)
                        {
                            _expandedPositions.Add(position);
                        }
                    }
                }
            }
        }
        else
        {
            // The host re-rendered with the SAME ExpandedStepIds reference but may have reordered or inserted steps:
            // remap each remaining expanded position by the step id it held before, so the expanded state follows the
            // STEP and not the raw position. Ids that appear more than once (a host data bug) keep position-only keying.
            _expandedPositions.RemoveWhere(position => position > _steps.Count);

            if (previous.Count > 0 && previous.Count != _steps.Count)
            {
                RemapExpandedPositions(previous);
            }
        }

        RepairFocus(previous);
        _summary = HandoffChainSummary.Create(_steps);
        PruneHandles();
    }

    /// <summary>
    /// Remaps the expanded positions after the host changed the step list (inserted or removed a step) while keeping
    /// the same <see cref="ExpandedStepIds"/> reference: each expanded position is looked up by the step id it held
    /// before, then moved to the position that step occupies now. Step ids that appear more than once keep
    /// position-only keying (there is no unambiguous step to follow).
    /// </summary>
    /// <param name="previous">The step list from the previous parameter set.</param>
    private void RemapExpandedPositions(IReadOnlyList<HandoffStep> previous)
    {
        // Count occurrences to detect duplicate ids — those keep position-only keying.
        var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in _steps)
        {
            idCounts[step.Id] = idCounts.TryGetValue(step.Id, out var count) ? count + 1 : 1;
        }

        var previousIdAtPosition = new Dictionary<int, string>();
        for (var index = 0; index < previous.Count; index++)
        {
            previousIdAtPosition[index + 1] = previous[index].Id;
        }

        var remapped = new HashSet<int>();
        foreach (var position in _expandedPositions)
        {
            if (position > previous.Count)
            {
                continue;
            }

            var stepId = previousIdAtPosition[position];
            if (idCounts.TryGetValue(stepId, out var count) && count == 1)
            {
                var newPosition = IndexOfId(_steps, stepId) + 1;
                if (newPosition > 0)
                {
                    remapped.Add(newPosition);
                }
            }
            else
            {
                // Duplicate id (or gone): keep the position as-is.
                remapped.Add(position);
            }
        }

        _expandedPositions.Clear();
        foreach (var position in remapped)
        {
            _expandedPositions.Add(position);
        }
    }

    /// <summary>
    /// Logs a warning when the host feeds two steps with the same id. Duplicate ids are a host data bug: the DOM stays
    /// valid (every detail id is built from the step POSITION), but the expanded/focus state is keyed by position so the
    /// two steps do not share it. The warning is logged ONCE per duplicate streak — a live page that keeps the bad data
    /// must not flood the log on every parameter set — and names the FIRST duplicate id so the host can find the bad row.
    /// </summary>
    private void DetectDuplicateIds()
    {
        if (_steps.Count < 2)
        {
            _duplicateWarned = false;
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? duplicateId = null;
        foreach (var step in _steps)
        {
            if (!seen.Add(step.Id))
            {
                duplicateId ??= step.Id;
                break;
            }
        }

        if (duplicateId is null)
        {
            _duplicateWarned = false;
            return;
        }

        if (_duplicateWarned)
        {
            return;
        }

        _duplicateWarned = true;

        if (!_loggerResolved)
        {
            _loggerResolved = true;
            _logger = (Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)
                ?.CreateLogger<TmAgentHandoff>();
        }

        _logger?.LogWarning(
            "TmAgentHandoff: duplicate step id \"{DuplicateId}\" detected — expand/focus state is keyed by " +
            "position so the steps stay independent, but the host should supply distinct ids.",
            duplicateId);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The focus move happens AFTER the paint: the step that has to receive it carries a new toggle in this very
        // render, and a browser only focuses an element once it is patched.
        if (_pendingFocusPosition > 0)
        {
            var pending = _pendingFocusPosition;
            _pendingFocusPosition = 0;
            await FocusToggleAfterRenderAsync(pending);
        }
    }

    // ── Focus ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Keeps the focus inside the chain: when the step the reader was on disappears from the host's data — either
    /// because the position is past the end, or because a different step now occupies it — the focus would drop onto
    /// the document body, so it is handed to the step that took the removed one's place and the move is announced once.
    /// </summary>
    private void RepairFocus(IReadOnlyList<HandoffStep> previous)
    {
        if (_focusedPosition <= 0)
        {
            return;
        }

        var stillThere = _focusedPosition <= _steps.Count
                         && _focusedStepId is not null
                         && string.Equals(
                             _steps[_focusedPosition - 1].Id,
                             _focusedStepId,
                             StringComparison.Ordinal);

        if (stillThere)
        {
            return;
        }

        var index = _focusedPosition - 1;
        _focusedPosition = 0;
        _focusedStepId = null;

        if (!_toggleHadFocus || _steps.Count == 0)
        {
            return;
        }

        var target = Math.Clamp(index, 0, _steps.Count - 1) + 1;
        _focusedPosition = target;
        _focusedStepId = _steps[target - 1].Id;
        _pendingFocusPosition = target;
        Announce(Loc["TmAgentHandoff_FocusLost"]);
    }

    private static int IndexOfId(IReadOnlyList<HandoffStep> steps, string stepId)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            if (string.Equals(steps[index].Id, stepId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void PruneHandles()
    {
        if (_handles.Count == 0)
        {
            return;
        }

        List<int>? stale = null;
        foreach (var position in _handles.Keys)
        {
            if (position > _steps.Count)
            {
                (stale ??= []).Add(position);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var position in stale)
        {
            _handles.Remove(position);
        }
    }

    private ToggleHandle HandleFor(int position)
    {
        if (!_handles.TryGetValue(position, out var handle))
        {
            handle = new ToggleHandle();
            _handles[position] = handle;
        }

        return handle;
    }

    private void OnToggleFocused(int position)
    {
        _toggleHadFocus = true;
        _focusedPosition = position;
        _focusedStepId = position <= _steps.Count ? _steps[position - 1].Id : null;
    }

    private async Task FocusToggleAfterRenderAsync(int position)
    {
        if (!_handles.TryGetValue(position, out var handle) || handle.Ref.Id is null)
        {
            return;
        }

        try
        {
            await handle.Ref.FocusAsync();
        }
        catch (Exception exception) when (exception is Microsoft.JSInterop.JSException
            or Microsoft.JSInterop.JSDisconnectedException
            or InvalidOperationException or NotSupportedException or ObjectDisposedException or ArgumentException)
        {
            // No JavaScript, or the element is already gone: the toggle is still reachable with Tab, so nothing is lost.
        }
    }

    // ── Expansion ────────────────────────────────────────────────────────────

    private bool IsExpanded(int position) => _expandedPositions.Contains(position);

    private string? DetailIdOf(int position) => _id + "-detail-" + position.ToString(CultureInfo.InvariantCulture);

    private async Task ToggleAsync(int position)
    {
        if (!_expandedPositions.Remove(position))
        {
            _expandedPositions.Add(position);
        }

        _focusedPosition = position;
        _focusedStepId = position <= _steps.Count ? _steps[position - 1].Id : null;
        // A click means the reader was on this toggle even in a browser that does not focus a clicked button
        // (Safari), so a host update that removes the step still hands the focus somewhere sensible.
        _toggleHadFocus = true;

        if (ExpandedStepIdsChanged.HasDelegate)
        {
            await ExpandedStepIdsChanged.InvokeAsync(MaterializeExpanded());
        }
    }

    /// <summary>The expanded ids in CHAIN order, so a host that stores the reported list keeps a stable value.</summary>
    private IReadOnlyList<string> MaterializeExpanded()
    {
        var result = new List<string>();
        for (var position = 1; position <= _steps.Count; position++)
        {
            if (_expandedPositions.Contains(position))
            {
                result.Add(_steps[position - 1].Id);
            }
        }

        return result;
    }

    private async Task OnViewModeChangedAsync(AgentHandoffViewMode mode)
    {
        if (mode == _viewMode || !IsKnownViewMode(mode))
        {
            return;
        }

        // The host's own value is deliberately NOT marked as seen: the reader's pick wins until the host MOVES the
        // parameter, so a host that re-renders with the same value cannot silently undo the choice.
        _viewMode = mode;
        StateHasChanged();

        if (ViewModeChanged.HasDelegate)
        {
            await ViewModeChanged.InvokeAsync(mode);
        }
    }

    private static bool IsKnownViewMode(AgentHandoffViewMode mode)
        => mode is AgentHandoffViewMode.Chain or AgentHandoffViewMode.Table;

    // ── Formatting ───────────────────────────────────────────────────────────

    private string StepName(int position)
        => Loc["TmAgentHandoff_StepLabel", position.ToString(CultureInfo.CurrentCulture)];

    private string FromText(HandoffStep step)
        => step.HasFrom ? step.FromName : Loc["TmAgentHandoff_UnknownAgent"];

    private string ToText(HandoffStep step)
        => step.HasTo ? step.ToName : Loc["TmAgentHandoff_UnknownAgent"];

    private string SummaryText(HandoffStep step)
        => step.HasSummary ? step.Summary : Loc["TmAgentHandoff_StepFallback"];

    private string TimeText(DateTimeOffset value) => value.ToLocalTime().ToString("t", FormatCulture);

    private string ItemLabel(HandoffItem item)
        => item.HasLabel ? item.Label : Loc["TmAgentHandoff_ItemFallback"];

    private string ItemKindText(HandoffItem item) => HandoffPresentation.KindText(Loc, item.Kind);

    private string PercentText(double rate)
        => Math.Round(rate * 100d, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.CurrentCulture);

    /// <summary>The passed items as one sentence for the table cell (labels only).</summary>
    private string PassedText(HandoffStep step)
        => step.PassedCount == 0
            ? Loc["TmAgentHandoff_None"]
            : string.Join(", ", step.PassedItems.Where(item => item is not null).Select(ItemLabel));

    /// <summary>The lost items as one sentence for the table cell — every label with the reason it never arrived.</summary>
    private string LostText(HandoffStep step)
    {
        if (step.LostCount == 0)
        {
            return Loc["TmAgentHandoff_None"];
        }

        return string.Join("; ", step.LostItems
            .Where(item => item is not null)
            .Select(item => item.HasReason
                ? Loc["TmAgentHandoff_LostItemWithReason", ItemLabel(item), item.Reason!]
                : ItemLabel(item)));
    }

    /// <summary>The chain-wide loss sentence: the distinct labels that were dropped, or the positive "nothing" line.</summary>
    private string LostSummaryText => _summary.HasLosses
        ? Loc["TmAgentHandoff_LostSummary", string.Join(", ", _summary.LostLabels)]
        : Loc["TmAgentHandoff_LostSummaryNone"];

    /// <summary>
    /// Writes the announcement into the ONE polite region. An identical sentence is re-announced by toggling one
    /// invisible marker, because a live region announces a MUTATION and not a repeat of the same text.
    /// </summary>
    private void Announce(string message)
    {
        if (string.Equals(message, _announcement, StringComparison.Ordinal))
        {
            _announcementMarker = !_announcementMarker;
            _announcement = _announcementMarker ? message + "\u200B" : message;
            return;
        }

        _announcementMarker = false;
        _announcement = message;
    }

    // ── Nested shapes ────────────────────────────────────────────────────────

    /// <summary>One mounted toggle's element reference, kept alive across renders so the focus move can reach it.</summary>
    private sealed class ToggleHandle
    {
        /// <summary>The toggle button's element reference, assigned by the render.</summary>
        public ElementReference Ref { get; set; }
    }
}
