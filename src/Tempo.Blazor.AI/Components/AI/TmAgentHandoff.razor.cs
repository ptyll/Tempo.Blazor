using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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

    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _lastExpandedParameter;
    private bool _expandedSeeded;

    private AgentHandoffViewMode _viewMode = AgentHandoffViewMode.Chain;
    private AgentHandoffViewMode _lastViewModeParameter = AgentHandoffViewMode.Chain;

    private string? _focusedStepId;
    private bool _toggleHadFocus;
    private string? _pendingFocusStepId;
    private readonly Dictionary<string, ToggleHandle> _handles = new(StringComparer.Ordinal);

    private string _announcement = string.Empty;
    private bool _announcementMarker;

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

        var ids = new HashSet<string>(_steps.Select(step => step.Id), StringComparer.Ordinal);

        if (!_expandedSeeded || !ReferenceEquals(ExpandedStepIds, _lastExpandedParameter))
        {
            _expandedSeeded = true;
            _lastExpandedParameter = ExpandedStepIds;
            _expanded.Clear();

            if (ExpandedStepIds is null)
            {
                foreach (var id in ids)
                {
                    _expanded.Add(id);
                }
            }
            else
            {
                foreach (var id in ExpandedStepIds)
                {
                    if (!string.IsNullOrEmpty(id) && ids.Contains(id))
                    {
                        _expanded.Add(id);
                    }
                }
            }
        }
        else
        {
            _expanded.RemoveWhere(id => !ids.Contains(id));
        }

        RepairFocus(previous, ids);
        _summary = HandoffChainSummary.Create(_steps);
        PruneHandles(ids);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The focus move happens AFTER the paint: the step that has to receive it carries a new toggle in this very
        // render, and a browser only focuses an element once it is patched.
        if (_pendingFocusStepId is { } pending)
        {
            _pendingFocusStepId = null;
            await FocusToggleAfterRenderAsync(pending);
        }
    }

    // ── Focus ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Keeps the focus inside the chain: when the step the reader was on disappears from the host's data, the focus
    /// would drop onto the document body, so it is handed to the step that took the removed one's place and the move is
    /// announced once.
    /// </summary>
    private void RepairFocus(IReadOnlyList<HandoffStep> previous, HashSet<string> ids)
    {
        if (_focusedStepId is not { } focused || ids.Contains(focused))
        {
            return;
        }

        var index = IndexOf(previous, focused);
        _focusedStepId = null;

        if (!_toggleHadFocus || _steps.Count == 0)
        {
            return;
        }

        var target = _steps[Math.Clamp(index < 0 ? 0 : index, 0, _steps.Count - 1)].Id;
        _focusedStepId = target;
        _pendingFocusStepId = target;
        Announce(Loc["TmAgentHandoff_FocusLost"]);
    }

    private static int IndexOf(IReadOnlyList<HandoffStep> steps, string stepId)
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

    private void PruneHandles(HashSet<string> ids)
    {
        if (_handles.Count == 0)
        {
            return;
        }

        List<string>? stale = null;
        foreach (var id in _handles.Keys)
        {
            if (!ids.Contains(id))
            {
                (stale ??= []).Add(id);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var id in stale)
        {
            _handles.Remove(id);
        }
    }

    private ToggleHandle HandleFor(string stepId)
    {
        if (!_handles.TryGetValue(stepId, out var handle))
        {
            handle = new ToggleHandle();
            _handles[stepId] = handle;
        }

        return handle;
    }

    private void OnToggleFocused(string stepId)
    {
        _toggleHadFocus = true;
        _focusedStepId = stepId;
    }

    private async Task FocusToggleAfterRenderAsync(string stepId)
    {
        if (!_handles.TryGetValue(stepId, out var handle) || handle.Ref.Id is null)
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

    private bool IsExpanded(string stepId) => _expanded.Contains(stepId);

    private string? DetailIdOf(int position) => _id + "-detail-" + position.ToString(CultureInfo.InvariantCulture);

    private async Task ToggleAsync(HandoffStep step)
    {
        if (!_expanded.Remove(step.Id))
        {
            _expanded.Add(step.Id);
        }

        _focusedStepId = step.Id;
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
        => [.. _steps.Where(step => _expanded.Contains(step.Id)).Select(step => step.Id)];

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
