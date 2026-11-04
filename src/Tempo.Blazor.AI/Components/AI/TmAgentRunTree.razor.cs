using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Formatting;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The run of an agent as a tree: one row per agent, sub-agent, tool call or step, with the state, the model,
/// the elapsed time, the cost and the tokens of each — and, on a parent row, the total of everything under
/// it.
/// <para>
/// The nodes are a FLAT list and the tree comes from <see cref="AgentRunNode.ParentId"/>. A node whose parent
/// names no supplied node is an ORPHAN attached under the root (marked as one, never silently dropped), a
/// parent cycle is rejected (a malformed run cannot loop or throw), and a duplicate id is read once.
/// </para>
/// <para>
/// The surface is a <c>role="treegrid"</c>: one roving tab stop, ↑/↓ move between the visible rows, → expands
/// a collapsed row or walks into the first child, ← collapses an expanded row or walks to the parent,
/// Home/End jump to the edges, and Enter (keydown) or Space (keyup) selects the row. The selection is two-way
/// (<c>@bind-SelectedNodeId</c>) and a host that supplies <see cref="DetailTemplate"/> gets a detail region
/// next to the grid. One polite live region carries the component's own events — select, expand, collapse and
/// the filter's count — and nothing else: the ticking clocks are not live.
/// </para>
/// <para>
/// The <see cref="ShowOnly"/> filter never breaks the tree: a row survives when it matches OR when a
/// surviving descendant needs it as its ancestor, so "only failed" still shows the chain that failed.
/// </para>
/// <para>
/// The component NEVER mutates <see cref="Nodes"/>: it renders the host's list, owns only the reader's view
/// state (filter, collapsed rows, selection when the host does not bind it) and reports the selection.
/// </para>
/// </summary>
public partial class TmAgentRunTree : TmComponentBase, IDisposable
{
    private readonly string _id = $"tm-ai-agent-run-tree-{Guid.NewGuid():N}";

    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElementReference> _rowRefs = new(StringComparer.Ordinal);

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private TimeProvider _clock = TimeProvider.System;
    private AiElapsedTicker? _ticker;
    private AgentRunTreeInterop? _interop;
    private bool _disposed;

    /// <summary>The instant this render paints with: sampled ONCE per render, so every row's clock and every
    /// roll-up describe the same moment.</summary>
    private DateTimeOffset _now;

    private AgentRunTreeLayoutResult _layout = new();
    private AgentRunFilter _filter = AgentRunFilter.All;
    private AgentRunFilter _lastShowOnly;
    private string? _selectedNodeId;
    private string? _lastSelectedParam;
    private string? _activeNodeId;
    private string _announcement = string.Empty;
    private bool _announcementMarker;
    private ElementReference _gridRef;
    private string? _guardedGridId;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The run, as a flat list. The tree is built from <see cref="AgentRunNode.ParentId"/>; the component
    /// never mutates this list. Null or empty renders <see cref="EmptyContent"/> or the localized empty
    /// sentence instead of an empty treegrid.
    /// </summary>
    [Parameter] public IReadOnlyList<AgentRunNode>? Nodes { get; set; }

    /// <summary>
    /// The selected node's id, two-way bound with <see cref="SelectedNodeIdChanged"/>
    /// (<c>@bind-SelectedNodeId</c>). The matching row carries <c>aria-selected="true"</c> and the detail
    /// region renders when <see cref="DetailTemplate"/> is supplied. A host may keep the component
    /// uncontrolled: the reader's selection is then internal and still renders the detail.
    /// </summary>
    [Parameter] public string? SelectedNodeId { get; set; }

    /// <summary>Fires with the newly selected node id (a row click, Enter or Space).</summary>
    [Parameter] public EventCallback<string?> SelectedNodeIdChanged { get; set; }

    /// <summary>
    /// The detail panel of the selected node, rendered in a named <c>region</c> below the grid. Null renders
    /// no detail panel — the grid alone is a complete surface.
    /// </summary>
    [Parameter] public RenderFragment<AgentRunNode>? DetailTemplate { get; set; }

    /// <summary>
    /// Which nodes the reader wants to see, two-way bound with <see cref="ShowOnlyChanged"/> (the built-in
    /// "show only" control raises it). The filter keeps the ancestors of every row it keeps. Default
    /// <see cref="AgentRunFilter.All"/>.
    /// </summary>
    [Parameter] public AgentRunFilter ShowOnly { get; set; } = AgentRunFilter.All;

    /// <summary>Fires when the reader picks another value of the "show only" control.</summary>
    [Parameter] public EventCallback<AgentRunFilter> ShowOnlyChanged { get; set; }

    /// <summary>Whether the "show only" control is rendered. Default <c>true</c>; the filter still applies
    /// when the host pins <see cref="ShowOnly"/> without the control.</summary>
    [Parameter] public bool ShowFilter { get; set; } = true;

    /// <summary>
    /// An optional heading above the grid. It also becomes the grid's accessible name when
    /// <see cref="AriaLabel"/> is not set.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>; set <c>false</c> when the host page
    /// already provides one. The grid stays named through <see cref="AriaLabel"/> or the localized
    /// default.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>Whether the model column is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowModel { get; set; } = true;

    /// <summary>Whether the elapsed-time column is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTime { get; set; } = true;

    /// <summary>Whether the cost column is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowCost { get; set; } = true;

    /// <summary>Whether the tokens column is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTokens { get; set; } = true;

    /// <summary>
    /// Formats a cost given in MICRO-UNITS (1,000,000 is 1.00 of the currency). Null uses
    /// <see cref="AiCostFormatter"/> with <see cref="Culture"/> — the culture's own currency, with
    /// micro-unit precision below one hundredth of a unit. A host with a credit price model overrides it.
    /// </summary>
    [Parameter] public Func<long, string>? CostFormatter { get; set; }

    /// <summary>
    /// The culture that formats the costs and the token counts. Null (the default) means the current
    /// culture at render time. The durations are clock-style (<c>m:ss</c>) on purpose and are not
    /// localized.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>
    /// The clock the live timers read and schedule from. Null (the default) uses the registered
    /// <see cref="System.TimeProvider"/> and falls back to <see cref="System.TimeProvider.System"/> — the
    /// parameter exists for a host (and a test) that wants a different clock than the app-wide one.
    /// </summary>
    [Parameter] public TimeProvider? TimeProvider { get; set; }

    /// <summary>
    /// Rendered instead of the localized "No nodes yet." when <see cref="Nodes"/> is null or empty. The
    /// component's instructions and its live region stay rendered either way.
    /// </summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>
    /// The accessible name of the treegrid. Null names it by the rendered heading — so the name a reader
    /// hears is the one they see — and, without a heading, by the localized "Agent run tree".
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived state ────────────────────────────────────────────────────────

    /// <summary>Latest announcement, mirrored into the component's single live region.</summary>
    public string Announcement => _announcement;

    /// <summary>The filter currently applied (the reader's choice wins until the host changes the
    /// parameter).</summary>
    public AgentRunFilter Filter => _filter;

    /// <summary>How many rows the current filter and collapse state leave visible.</summary>
    public int VisibleRowCount => _layout.Rows.Count;

    /// <summary>The selected node, resolved against the host's list; null when nothing is selected.</summary>
    public AgentRunNode? SelectedNode
        => _selectedNodeId is { } id
            ? Nodes?.FirstOrDefault(node => node is not null && string.Equals(node.Id, id, StringComparison.Ordinal))
            : null;

    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    private string TitleId => _id + "-title";

    private string InstructionsId => _id + "-instructions";

    private IReadOnlyList<AgentRunNode> NodeList => Nodes ?? [];

    private bool IsEmpty => _layout.Rows.Count == 0 && NodeList.Count == 0;

    /// <summary>Whether the run has nodes the filter hides — the empty state is then a filtered empty
    /// state, not an empty run.</summary>
    private bool IsFilteredEmpty => _layout.Rows.Count == 0 && NodeList.Count > 0;

    private string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmAgentRunTree_Title"] : Title!;

    private int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    private string? GridAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? null : AriaLabel;

    private string? GridLabelledBy => GridAriaLabel is null && ShowTitle ? TitleId : null;

    private string? GridFallbackLabel
        => GridAriaLabel is null && !ShowTitle ? Loc["TmAgentRunTree_AriaLabel"] : null;

    private int ColumnCount => 2 + (ShowModel ? 1 : 0) + (ShowTime ? 1 : 0) + (ShowCost ? 1 : 0)
        + (ShowTokens ? 1 : 0);

    private string FilterSlug => _filter switch
    {
        AgentRunFilter.Running => "running",
        AgentRunFilter.Failed => "failed",
        _ => "all",
    };

    private string RootClass
    {
        get
        {
            var classes = new StringBuilder("tm-ai-agent-run-tree");
            if (!string.IsNullOrEmpty(Class))
            {
                classes.Append(' ').Append(Class);
            }

            return classes.ToString();
        }
    }

    /// <summary>The three filter segments, named by the localizer.</summary>
    private IReadOnlyList<SegmentedOption<AgentRunFilter>> FilterOptions =>
    [
        new() { Label = Loc["TmAgentRunTree_FilterAll"], Value = AgentRunFilter.All },
        new() { Label = Loc["TmAgentRunTree_FilterRunning"], Value = AgentRunFilter.Running },
        new() { Label = Loc["TmAgentRunTree_FilterFailed"], Value = AgentRunFilter.Failed },
    ];

    private bool NeedsTicking => !_disposed
        && NodeList.Any(node => node is not null
            && node.Status == AgentStepStatus.Running
            && node.StartedAt is not null);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _clock = TimeProvider
            ?? ServiceProvider.GetService<TimeProvider>()
            ?? System.TimeProvider.System;
        _ticker = new AiElapsedTicker(_clock, OnTickAsync);
        _interop = new AgentRunTreeInterop(JS);
        _filter = ShowOnly;
        _lastShowOnly = ShowOnly;
        _lastSelectedParam = SelectedNodeId;
        _selectedNodeId = SelectedNodeId;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The clock a host pins wins over the one captured at initialization.
        var clock = TimeProvider
            ?? ServiceProvider.GetService<TimeProvider>()
            ?? System.TimeProvider.System;
        if (!ReferenceEquals(clock, _clock))
        {
            _clock = clock;
            _ticker?.Dispose();
            _ticker = new AiElapsedTicker(_clock, OnTickAsync);
        }

        // A host that changes the parameter wins; the reader's own choice survives an unrelated render.
        if (ShowOnly != _lastShowOnly)
        {
            _lastShowOnly = ShowOnly;
            _filter = ShowOnly;
        }

        if (!string.Equals(SelectedNodeId, _lastSelectedParam, StringComparison.Ordinal))
        {
            _lastSelectedParam = SelectedNodeId;
            _selectedNodeId = SelectedNodeId;
        }

        Recompute();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (NeedsTicking)
        {
            _ticker?.Start();
        }
        else
        {
            _ticker?.Stop();
        }

        if (_layout.Rows.Count > 0 && _guardedGridId != _gridRef.Id)
        {
            _guardedGridId = _gridRef.Id;
            if (_interop is not null)
            {
                await _interop.AttachAsync(_gridRef);
            }
        }

        if (_pendingFocusNodeId is { } pending)
        {
            _pendingFocusNodeId = null;
            await FocusRowAsync(pending);
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the node with <paramref name="nodeId"/> — the same path a click, Enter or Space takes. A
    /// no-op for a null id; an id that names no supplied node still raises the callback (the host may know a
    /// node this render does not show).
    /// </summary>
    /// <param name="nodeId">The node to select, or null to clear the selection.</param>
    public async Task SelectNodeAsync(string? nodeId)
    {
        _selectedNodeId = nodeId;
        _lastSelectedParam = SelectedNodeId;
        if (nodeId is not null)
        {
            var node = Nodes?.FirstOrDefault(candidate =>
                candidate is not null && string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
            if (node is not null)
            {
                Announce(Loc["TmAgentRunTree_Selected", TitleText(node)]);
                _activeNodeId = nodeId;
            }
        }

        await SelectedNodeIdChanged.InvokeAsync(nodeId);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Collapses or expands the node with <paramref name="nodeId"/>, the same path the disclosure button and
    /// ←/→ take. A no-op for a node without children.
    /// </summary>
    /// <param name="nodeId">The node to toggle.</param>
    /// <param name="collapsed">Whether its children should be hidden.</param>
    public Task SetCollapsedAsync(string nodeId, bool collapsed)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return Task.CompletedTask;
        }

        var row = _layout.Rows.FirstOrDefault(candidate =>
            string.Equals(candidate.Node.Id, nodeId, StringComparison.Ordinal));
        if (row is null || !row.HasChildren)
        {
            return Task.CompletedTask;
        }

        if (collapsed == row.IsCollapsed)
        {
            return Task.CompletedTask;
        }

        if (collapsed)
        {
            _collapsed.Add(nodeId);
        }
        else
        {
            _collapsed.Remove(nodeId);
        }

        Announce(Loc[collapsed ? "TmAgentRunTree_Collapsed" : "TmAgentRunTree_Expanded", TitleText(row.Node)]);
        Recompute();
        _pendingFocusNodeId = nodeId;
        return InvokeAsync(StateHasChanged);
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    private async Task HandleRowKeyDownAsync(AgentRunTreeRow row, KeyboardEventArgs args)
    {
        var index = row.Index;
        switch (args.Key)
        {
            case "ArrowDown":
                await MoveToAsync(index + 1);
                break;
            case "ArrowUp":
                await MoveToAsync(index - 1);
                break;
            case "Home":
                await MoveToAsync(0);
                break;
            case "End":
                await MoveToAsync(_layout.Rows.Count - 1);
                break;
            case "ArrowRight":
                // APG treegrid: expand a collapsed row, walk into the first child of an expanded one and do
                // nothing on a leaf (its next row is a sibling, not a child).
                if (row.HasChildren)
                {
                    if (row.IsCollapsed)
                    {
                        await SetCollapsedAsync(row.Node.Id, false);
                    }
                    else
                    {
                        await MoveToAsync(index + 1);
                    }
                }

                break;
            case "ArrowLeft":
                if (row.HasChildren && !row.IsCollapsed)
                {
                    await SetCollapsedAsync(row.Node.Id, true);
                }
                else
                {
                    await MoveToParentAsync(row);
                }

                break;
            case "Enter":
                // Enter activates on KEYDOWN, guarded against auto-repeat; Space fires on KEYUP, exactly
                // where a native button fires its click (docs/keyboard-activation-convention.md).
                if (!args.Repeat)
                {
                    await SelectAsync(row);
                }

                break;
        }
    }

    private async Task HandleRowKeyUpAsync(AgentRunTreeRow row, KeyboardEventArgs args)
    {
        if (args.Key is " " or "Spacebar")
        {
            await SelectAsync(row);
        }
    }

    private Task MoveToAsync(int index)
    {
        if (_layout.Rows.Count == 0)
        {
            return Task.CompletedTask;
        }

        var target = _layout.Rows[Math.Clamp(index, 0, _layout.Rows.Count - 1)];
        _activeNodeId = target.Node.Id;
        _pendingFocusNodeId = target.Node.Id;
        return InvokeAsync(StateHasChanged);
    }

    private Task MoveToParentAsync(AgentRunTreeRow row)
    {
        for (var index = row.Index - 1; index >= 0; index--)
        {
            if (_layout.Rows[index].Depth < row.Depth)
            {
                return MoveToAsync(index);
            }
        }

        return Task.CompletedTask;
    }

    private async Task SelectAsync(AgentRunTreeRow row)
    {
        _activeNodeId = row.Node.Id;
        await SelectNodeAsync(row.Node.Id);
    }

    private Task ToggleAsync(AgentRunTreeRow row)
        => SetCollapsedAsync(row.Node.Id, !row.IsCollapsed);

    private Task OnFilterChangedAsync(AgentRunFilter filter)
    {
        if (filter == _filter)
        {
            return Task.CompletedTask;
        }

        _filter = filter;
        _lastShowOnly = filter;
        Recompute();

        // The filter can hide the focused row: the focus (and the roving tab stop) falls back to the first
        // visible one, so the grid never leaves the reader on a row that no longer exists.
        _activeNodeId = _layout.Rows.Count == 0 ? null : _layout.Rows[0].Node.Id;
        if (_activeNodeId is not null)
        {
            _pendingFocusNodeId = _activeNodeId;
        }

        Announce(Loc["TmAgentRunTree_Filtered",
            _layout.Rows.Count.ToString(CultureInfo.CurrentCulture),
            _layout.TotalNodeCount.ToString(CultureInfo.CurrentCulture)]);

        return ShowOnlyChanged.HasDelegate
            ? RaiseFilterChangedAsync(filter)
            : InvokeAsync(StateHasChanged);
    }

    private async Task RaiseFilterChangedAsync(AgentRunFilter value)
    {
        await ShowOnlyChanged.InvokeAsync(value);
        await InvokeAsync(StateHasChanged);
    }

    // ── Rendering helpers ────────────────────────────────────────────────────

    /// <summary>Rebuilds the visible forest at one clock reading.</summary>
    private void Recompute()
    {
        if (!_disposed)
        {
            _now = _clock.GetUtcNow();
        }

        _layout = AgentRunTreeLayout.Build(NodeList, _filter, _collapsed, _now);

        // A collapsed id that left the run would otherwise keep a phantom entry forever.
        var live = new HashSet<string>(NodeList.Select(node => node.Id), StringComparer.Ordinal);
        _collapsed.RemoveWhere(id => !live.Contains(id));

        if (_activeNodeId is null || _layout.Rows.All(row => row.Node.Id != _activeNodeId))
        {
            _activeNodeId = _layout.Rows.Count > 0 ? _layout.Rows[0].Node.Id : null;
        }

        var references = new HashSet<string>(_layout.Rows.Select(row => row.Node.Id), StringComparer.Ordinal);
        foreach (var stale in _rowRefs.Keys.Where(key => !references.Contains(key)).ToList())
        {
            _rowRefs.Remove(stale);
        }
    }

    private bool IsActive(AgentRunTreeRow row)
        => string.Equals(row.Node.Id, _activeNodeId, StringComparison.Ordinal);

    private bool IsSelected(AgentRunTreeRow row)
        => string.Equals(row.Node.Id, _selectedNodeId, StringComparison.Ordinal);

    private string TitleText(AgentRunNode node)
        => string.IsNullOrWhiteSpace(node.Title) ? Loc["TmAgentRunTree_Untitled"] : node.Title;

    private string LabelClass(AgentRunNode node)
        => string.IsNullOrWhiteSpace(node.Title)
            ? "tm-ai-agent-run-tree__label tm-ai-agent-run-tree__label--placeholder"
            : "tm-ai-agent-run-tree__label";

    private static string IndentStyle(AgentRunTreeRow row)
        => $" --tm-agent-run-tree-depth: {row.Depth.ToString(CultureInfo.InvariantCulture)}";

    private string StatusText(AgentStepStatus status) => status switch
    {
        AgentStepStatus.Running => Loc["TmAgentRun_StatusRunning"],
        AgentStepStatus.Done => Loc["TmAgentRun_StatusDone"],
        AgentStepStatus.Failed => Loc["TmAgentRun_StatusFailed"],
        AgentStepStatus.Skipped => Loc["TmAgentRun_StatusSkipped"],
        _ => Loc["TmAgentRun_StatusPending"],
    };

    private static string StatusSlug(AgentStepStatus status) => status switch
    {
        AgentStepStatus.Running => "running",
        AgentStepStatus.Done => "done",
        AgentStepStatus.Failed => "failed",
        AgentStepStatus.Skipped => "skipped",
        _ => "pending",
    };

    private static string StatusIcon(AgentStepStatus status) => status switch
    {
        AgentStepStatus.Running => IconNames.Play,
        AgentStepStatus.Done => IconNames.CheckCircle,
        AgentStepStatus.Failed => IconNames.XCircle,
        AgentStepStatus.Skipped => IconNames.SkipForward,
        _ => IconNames.Circle,
    };

    private static string KindSlug(AgentRunNodeKind kind) => kind switch
    {
        AgentRunNodeKind.SubAgent => "sub-agent",
        AgentRunNodeKind.Tool => "tool",
        AgentRunNodeKind.Approval => "approval",
        AgentRunNodeKind.Step => "step",
        _ => "agent",
    };

    private static string KindIcon(AgentRunNodeKind kind) => kind switch
    {
        AgentRunNodeKind.SubAgent => IconNames.Users,
        AgentRunNodeKind.Tool => IconNames.Terminal,
        AgentRunNodeKind.Approval => IconNames.ShieldCheck,
        AgentRunNodeKind.Step => IconNames.Circle,
        _ => IconNames.Cpu,
    };

    private string KindText(AgentRunNodeKind kind) => kind switch
    {
        AgentRunNodeKind.SubAgent => Loc["TmAgentRunNodeKind_SubAgent"],
        AgentRunNodeKind.Tool => Loc["TmAgentRunNodeKind_Tool"],
        AgentRunNodeKind.Approval => Loc["TmAgentRunNodeKind_Approval"],
        AgentRunNodeKind.Step => Loc["TmAgentRunNodeKind_Step"],
        _ => Loc["TmAgentRunNodeKind_Agent"],
    };

    private string FormatCost(long microUnits)
        => CostFormatter is { } formatter ? formatter(microUnits) : AiCostFormatter.Format(microUnits, EffectiveCulture);

    /// <summary>Whether the subtree reports a duration at all (a dash is not a total).</summary>
    private static bool HasTime(AgentRunTreeRow row) => row.Rollup.Duration is not null;

    /// <summary>Whether the subtree reports a cost at all.</summary>
    private static bool HasCost(AgentRunTreeRow row) => row.Rollup.CostMicroUnits is not null;

    /// <summary>Whether the subtree reports tokens at all.</summary>
    private static bool HasTokens(AgentRunTreeRow row) => row.Rollup.Tokens is not null;

    private string TimeCell(AgentRunTreeRow row)
        => row.Rollup.Duration is { } duration ? AiClockFormatter.FormatClock(duration) : "—";

    private string CostCell(AgentRunTreeRow row)
        => row.Rollup.CostMicroUnits is { } microUnits ? FormatCost(microUnits) : "—";

    private string TokensCell(AgentRunTreeRow row)
        => row.Rollup.Tokens is { } tokens ? tokens.ToString("N0", EffectiveCulture) : "—";

    /// <summary>
    /// The accessible name of a summary cell: the visible number is only half the fact — a screen reader
    /// must also hear that it is the whole subtree's total, not this row's own.
    /// </summary>
    private string? RollupLabel(AgentRunTreeRow row, string value, bool reported)
        => row.IsSummary && reported ? Loc["TmAgentRunTree_Rollup", TitleText(row.Node), value] : null;

    /// <summary>
    /// A failed cell's accessible name: the visible state word plus the reason behind it. Null for a healthy
    /// row, so the cell's content stays its own name.
    /// </summary>
    private string? StatusAriaLabel(AgentRunTreeRow row)
        => string.IsNullOrWhiteSpace(row.Node.Error)
            ? null
            : Loc["TmAgentRunTree_StatusWithError", StatusText(row.Node.Status), row.Node.Error];

    private string ToggleLabel(AgentRunTreeRow row)
        => Loc[row.IsCollapsed ? "TmAgentRunTree_Expand" : "TmAgentRunTree_Collapse", TitleText(row.Node)];

    private void OnRowFocus(string nodeId)
    {
        if (!string.Equals(_activeNodeId, nodeId, StringComparison.Ordinal))
        {
            _activeNodeId = nodeId;
        }
    }

    // ── Clock ────────────────────────────────────────────────────────────────

    private async Task OnTickAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                Recompute();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer went away between the tick and the hop.
        }
        catch (InvalidOperationException)
        {
            // The renderer has already been torn down.
        }
    }

    // ── Focus ────────────────────────────────────────────────────────────────

    private string? _pendingFocusNodeId;

    private async Task FocusRowAsync(string nodeId)
    {
        if (!_rowRefs.TryGetValue(nodeId, out var element) || element.Id is null)
        {
            return;
        }

        try
        {
            await element.FocusAsync(preventScroll: true);
        }
        catch (JSDisconnectedException)
        {
            // Blazor Server: the circuit dropped, so there is no focus to move.
        }
        catch (JSException)
        {
            // The element left the DOM between the render and the focus call.
        }
        catch (InvalidOperationException)
        {
            // No renderer, or the element reference is no longer attached.
        }
    }

    /// <summary>
    /// Replaces the live region's text. Sticky (never cleared on a re-render, which could wipe an
    /// announcement before assistive technology read it); when two consecutive transitions would write the
    /// same sentence, one invisible marker character is toggled so the region still sees a mutation.
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ticker?.Dispose();
        _ticker = null;

        var interop = _interop;
        _interop = null;
        if (interop is not null)
        {
            _ = interop.DisposeAsync().AsTask();
        }
    }
}
