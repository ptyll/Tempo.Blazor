using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The agents' branches as one table: a row per changed file, a column per agent, a cell wherever that agent changed
/// that file — and a row that more than one agent claimed called out as a conflict the reader has to settle.
/// <para>
/// The table is COMPUTED, never drawn by hand: <see cref="ConflictMatrix"/> decides which paths exist, which of them are
/// conflicts, their order and their directory groups, so the picture and the counts in the summary cannot disagree.
/// Conflicting rows come first, a directory follows the row order rather than the other way round, and every cell names
/// the change with an icon AND a word (<c>Modified</c>, <c>Deleted</c>, <c>was src/Old.cs</c>) instead of a colour.
/// </para>
/// <para>
/// A conflicting file carries its own side choice — one agent's version, or "merge it by hand" — bound two-way through
/// <see cref="Resolutions"/>. The merge action stays DISABLED until every conflict is decided, and its hint states how
/// many are still open, so a reader is never left guessing why the button does nothing. The reader's decisions survive a
/// host that re-renders the same list and are replaced only when the host MOVES the parameter.
/// </para>
/// <para>
/// Accessibility: the surface is a NATIVE <c>&lt;table&gt;</c> and declares no grid role — a header cell per agent (its
/// name and branch), a <c>&lt;th scope="rowgroup"&gt;</c> per directory, a <c>&lt;th scope="row"&gt;</c> per file and a
/// resolution cell that names that row header through <c>headers</c>. A <c>role="grid"</c> table without a roving
/// tabindex makes a screen reader switch to focus mode and expect arrow keys the surface does not handle, which is why
/// the native semantics are left alone. The side choice is a NATIVE radio group per file, so the browser's own
/// arrow-key behaviour applies and each file is its own group. Directories expand and collapse through native buttons,
/// and the surface owns exactly ONE polite live region that announces a resolution, a merge, a filter change and a
/// collapsed directory.
/// </para>
/// </summary>
public partial class TmConflictRadar : TmComponentBase
{
    private readonly string _id = $"tm-ai-radar-{Guid.NewGuid():n}"[..23];

    private readonly Dictionary<string, ConflictResolution> _decisions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _conflictPositions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _rowPositions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    private IReadOnlyList<AgentBranchChanges> _branches = [];
    private IReadOnlyList<ConflictMatrixDirectoryView> _directories = [];
    private ConflictMatrix _matrix = ConflictMatrix.Empty;

    private IReadOnlyList<ConflictResolution>? _lastResolutions;
    private IReadOnlySet<string>? _lastCollapsed;

    private bool _conflictOnly;
    private bool _lastConflictOnly;
    private bool _conflictOnlySeeded;

    private double? _measuredWidth;

    private int _radioVersion;

    private string _announcement = string.Empty;
    private bool _announcementMarker;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// One entry per agent branch: the agent, the branch it works on and every path it changed. Null or empty renders
    /// the localized empty state. An agent that changed nothing is not a column (a column with no cells would only
    /// widen the table).
    /// </summary>
    [Parameter] public IReadOnlyList<AgentBranchChanges>? Agents { get; set; }

    /// <summary>
    /// The reader's decisions, one per conflicting file, two-way bound with <see cref="ResolutionsChanged"/>
    /// (<c>@bind-Resolutions</c>). Null means nothing is decided yet, so every conflict is unresolved and the merge stays
    /// disabled. The reader's own pick wins until the host supplies a NEW list instance.
    /// </summary>
    [Parameter] public IReadOnlyList<ConflictResolution>? Resolutions { get; set; }

    /// <summary>Fires with the full, ordered decision list whenever the reader decides a file.</summary>
    [Parameter] public EventCallback<IReadOnlyList<ConflictResolution>> ResolutionsChanged { get; set; }

    /// <summary>
    /// Raised once per committed merge, with every file the merge touches and the accepted decision for each conflict.
    /// The surface changes nothing itself: the host applies the merge in its own repository and re-renders.
    /// </summary>
    [Parameter] public EventCallback<ConflictMergeRequest> OnMerge { get; set; }

    /// <summary>
    /// Whether only conflicting files are shown, two-way bound with <see cref="ConflictOnlyChanged"/>
    /// (<c>@bind-ConflictOnly</c>). Default <c>false</c>. The reader's pick wins until the host MOVES the parameter.
    /// </summary>
    [Parameter] public bool ConflictOnly { get; set; }

    /// <summary>Fires with the new filter state.</summary>
    [Parameter] public EventCallback<bool> ConflictOnlyChanged { get; set; }

    /// <summary>
    /// How the branches are presented: the file × agent table, a card per file, or
    /// <see cref="ConflictRadarPresentation.Auto"/> (the default) which picks the table while the surface's own
    /// container is at least <c>TmBreakpoints.Sm</c> wide and the cards below it. The container is measured through
    /// <c>TmLayoutObserver</c>, so the phone presentation also reaches a narrow panel inside a wide page.
    /// </summary>
    [Parameter] public ConflictRadarPresentation Presentation { get; set; } = ConflictRadarPresentation.Auto;

    /// <summary>
    /// The directories whose file rows are hidden, two-way bound with <see cref="CollapsedDirectoriesChanged"/>
    /// (<c>@bind-CollapsedDirectories</c>). Null means every directory is open. The reader's own folds win until the
    /// host MOVES the parameter (a new <see cref="IReadOnlySet{T}"/> instance replaces them); a directory that no
    /// longer exists in <see cref="Agents"/> is dropped from the settled set.
    /// </summary>
    [Parameter] public IReadOnlySet<string>? CollapsedDirectories { get; set; }

    /// <summary>Fires with the settled set of collapsed directories whenever the reader folds or unfolds one.</summary>
    [Parameter] public EventCallback<IReadOnlySet<string>> CollapsedDirectoriesChanged { get; set; }

    /// <summary>The heading above the grid. Null renders the localized default ("Conflict radar").</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>The accessible name of the grid. Null uses the localized default ("Conflict radar").</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The matrix the grid is painted from.</summary>
    internal ConflictMatrix Matrix => _matrix;

    /// <summary>The directory groups actually rendered (after the conflicts-only filter).</summary>
    internal IReadOnlyList<ConflictMatrixDirectoryView> Directories => _directories;

    /// <summary>The filter state being shown.</summary>
    internal bool IsConflictOnly => _conflictOnly;

    /// <summary>The presentation actually rendered: the host's pin, or the measurement against the mobile breakpoint.</summary>
    internal ConflictRadarPresentation EffectivePresentation
        => ResolvePresentation(Presentation, _measuredWidth);

    /// <summary>Whether the card presentation is the one on screen.</summary>
    internal bool ShowCards => EffectivePresentation == ConflictRadarPresentation.Cards;

    /// <summary>The stable slug of the rendered presentation, used as the root's <c>data-presentation</c> hook.</summary>
    internal string PresentationSlug => ShowCards ? "cards" : "grid";

    /// <summary>
    /// How many rows the table actually renders: the header row, one row per visible directory group, one row per
    /// visible file and one more row per conflicting file (its side choice). The number is COMPUTED from the same
    /// filtered, collapsed view the markup paints, so <c>aria-rowcount</c> can never disagree with the DOM.
    /// </summary>
    internal int RenderedRowCount
    {
        get
        {
            var count = 1; // the header row
            foreach (var group in _directories)
            {
                count += 1; // the directory row
                if (group.Collapsed)
                {
                    continue;
                }

                count += group.Rows.Count + group.Rows.Count(row => row.IsConflict);
            }

            return count;
        }
    }

    /// <summary>
    /// How many columns the table actually renders: the file column plus one per agent with something to show.
    /// </summary>
    internal int RenderedColumnCount => _matrix.ColumnCount + 1;

    /// <summary>
    /// Settles a pin against a measured container width. A pinned presentation always wins; an
    /// <see cref="ConflictRadarPresentation.Auto"/> one is the table until a measurement says the container is narrower
    /// than <see cref="TmBreakpoints.Sm"/> — before the first measurement (static render, prerender, bUnit) the table
    /// is the safe answer, because it degrades to a scroller rather than to a card list nobody asked for.
    /// </summary>
    /// <param name="pinned">The host's <see cref="Presentation"/>.</param>
    /// <param name="width">The measured container width in CSS pixels, or null.</param>
    /// <returns>The presentation to render.</returns>
    internal static ConflictRadarPresentation ResolvePresentation(ConflictRadarPresentation pinned, double? width)
    {
        if (pinned != ConflictRadarPresentation.Auto)
        {
            return pinned;
        }

        // TmBreakpoints.Classify rejects a non-finite or negative width by design; a degenerate measurement (0 from a
        // hidden container) is "not measured yet", not a phone.
        if (width is not { } measured || !double.IsFinite(measured) || measured <= 0)
        {
            return ConflictRadarPresentation.Grid;
        }

        return TmBreakpoints.Classify(measured) == TmLayoutMode.Mobile
            ? ConflictRadarPresentation.Cards
            : ConflictRadarPresentation.Grid;
    }

    /// <summary>How many conflicts still wait for a decision.</summary>
    internal int UnresolvedCount => _matrix.Unresolved(_decisions.Values).Count;

    /// <summary>Whether the merge may be committed.</summary>
    internal bool CanMerge => !_matrix.IsEmpty && _matrix.IsReadyToMerge(_decisions.Values);

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    internal int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    internal string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmConflictRadar_Title"] : Title!;

    /// <summary>The settled accessible name of the grid.</summary>
    internal string EffectiveLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Loc["TmConflictRadar_AreaLabel"] : AriaLabel!;

    /// <summary>The one polite live region's current text.</summary>
    internal string Announcement => _announcement;

    /// <summary>The one-line summary above the grid.</summary>
    internal string SummaryText => Loc["TmConflictRadar_Summary",
        Count(_matrix.RowCount), Count(_matrix.ColumnCount), Count(_matrix.ConflictCount)];

    /// <summary>The hint attached to the merge action: why it is unavailable, or that it is ready.</summary>
    internal string MergeHint
    {
        get
        {
            if (CanMerge)
            {
                return Loc["TmConflictRadar_ReadyToMerge"];
            }

            return Loc.Plural("TmConflictRadar_Unresolved", UnresolvedCount);
        }
    }

    /// <summary>Whether the localized "nothing matched" state is the one to render.</summary>
    internal bool ShowFilteredEmpty => !_matrix.IsEmpty && _directories.Count == 0;

    private string TitleId => _id + "-title";

    private string InstructionsId => _id + "-instructions";

    private string MergeHintId => _id + "-merge-hint";

    private string RootClass
    {
        get
        {
            var root = _conflictOnly ? "tm-ai-radar tm-ai-radar--conflicts-only" : "tm-ai-radar";
            return string.IsNullOrWhiteSpace(Class) ? root : root + " " + Class;
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _branches = Agents is null ? [] : [.. Agents.Where(branch => branch is not null)];

        // The matrix comes FIRST: the collapse set is settled against the directories the branches actually produce.
        _matrix = new ConflictMatrix(_branches);
        _conflictPositions.Clear();
        for (var index = 0; index < _matrix.Conflicts.Count; index++)
        {
            _conflictPositions[_matrix.Conflicts[index].Path] = index;
        }

        _rowPositions.Clear();
        for (var index = 0; index < _matrix.Rows.Count; index++)
        {
            _rowPositions[_matrix.Rows[index].Path] = index;
        }

        // Settle the reader's decisions against the host's list: a MOVED parameter (a new instance) is the host
        // speaking, so it replaces the picks; a same-instance re-render cannot undo them.
        if (!ReferenceEquals(Resolutions, _lastResolutions))
        {
            _lastResolutions = Resolutions;
            _decisions.Clear();
            _radioVersion++;
            if (Resolutions is not null)
            {
                foreach (var resolution in Resolutions)
                {
                    if (resolution is not null)
                    {
                        _decisions[resolution.Path] = resolution;
                    }
                }
            }
        }

        // The filter follows the host only when its parameter MOVES; the reader's pick is never recorded as "seen".
        if (!_conflictOnlySeeded || ConflictOnly != _lastConflictOnly)
        {
            _conflictOnlySeeded = true;
            _lastConflictOnly = ConflictOnly;
            _conflictOnly = ConflictOnly;
        }

        // The fold set follows the same contract as the decisions: a NEW instance is the host speaking, a re-render of
        // the same instance is not. The set is COPIED, so a reader's fold can never mutate the host's own collection.
        if (!ReferenceEquals(CollapsedDirectories, _lastCollapsed))
        {
            _lastCollapsed = CollapsedDirectories;
            _collapsed.Clear();
            if (CollapsedDirectories is not null)
            {
                foreach (var directory in CollapsedDirectories)
                {
                    if (directory is not null)
                    {
                        _collapsed.Add(directory);
                    }
                }
            }
        }

        PruneCollapsed();
        RebuildDirectories();
    }

    private void PruneCollapsed()
    {
        if (_collapsed.Count == 0)
        {
            return;
        }

        List<string>? stale = null;
        foreach (var directory in _collapsed)
        {
            if (!_matrix.Directories.Any(group => string.Equals(group.Directory, directory, StringComparison.Ordinal)))
            {
                (stale ??= []).Add(directory);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var directory in stale)
        {
            _collapsed.Remove(directory);
        }
    }

    private void RebuildDirectories()
    {
        var views = new List<ConflictMatrixDirectoryView>(_matrix.Directories.Count);
        foreach (var group in _matrix.Directories)
        {
            var rows = _conflictOnly
                ? [.. group.Rows.Where(row => row.IsConflict)]
                : group.Rows;

            if (rows.Count == 0)
            {
                continue;
            }

            views.Add(new ConflictMatrixDirectoryView(group.Directory, _collapsed.Contains(group.Directory), rows));
        }

        _directories = views;
    }

    // ── Interaction ──────────────────────────────────────────────────────────

    /// <summary>The filter handler: paints the filtered rows at once, then reports the state to the host.</summary>
    /// <param name="onlyConflicts">Whether only conflicts should stay visible.</param>
    /// <returns>A task that completes when the host's callback returned.</returns>
    internal async Task OnConflictOnlyChangedAsync(bool onlyConflicts)
    {
        if (onlyConflicts == _conflictOnly)
        {
            return;
        }

        _conflictOnly = onlyConflicts;
        RebuildDirectories();
        Announce(onlyConflicts
            ? Loc["TmConflictRadar_AnnounceConflictsOnly"]
            : Loc["TmConflictRadar_AnnounceEveryFile"]);
        StateHasChanged();
        await ConflictOnlyChanged.InvokeAsync(onlyConflicts);
    }

    /// <summary>Collapses or expands one directory group and reports the settled set to the host.</summary>
    /// <param name="directory">The directory (an empty string is the repository root).</param>
    /// <returns>A task that completes when the host's callback returned.</returns>
    internal async Task ToggleDirectoryAsync(string directory)
    {
        var name = DirectoryName(directory);
        if (!_collapsed.Remove(directory))
        {
            _collapsed.Add(directory);
            Announce(Loc["TmConflictRadar_AnnounceDirectoryCollapsed", name]);
        }
        else
        {
            Announce(Loc["TmConflictRadar_AnnounceDirectoryExpanded", name]);
        }

        RebuildDirectories();
        StateHasChanged();
        await CollapsedDirectoriesChanged.InvokeAsync(CollapsedSnapshot());
    }

    /// <summary>Whether a directory group is collapsed.</summary>
    /// <param name="directory">The directory to test.</param>
    /// <returns>Whether its rows are hidden.</returns>
    internal bool IsCollapsed(string directory) => _collapsed.Contains(directory);

    /// <summary>
    /// The settled collapse set as the ordered, immutable snapshot the component reports — a fresh instance every
    /// time, so a host that echoes it back reads as the host speaking and can never alias the component's state.
    /// </summary>
    /// <returns>The collapsed directories, ordinal-ordered.</returns>
    internal IReadOnlySet<string> CollapsedSnapshot()
        => new HashSet<string>(_collapsed.OrderBy(directory => directory, StringComparer.Ordinal), StringComparer.Ordinal);

    /// <summary>
    /// Records the container width the nested <c>TmLayoutObserver</c> reported (an
    /// <see cref="ConflictRadarPresentation.Auto"/> surface only) and repaints when it changes the presentation.
    /// </summary>
    /// <param name="width">The measured inline size in CSS pixels.</param>
    /// <returns>A task that completes when the repaint was queued.</returns>
    internal Task OnContainerWidthAsync(double width)
    {
        if (Presentation != ConflictRadarPresentation.Auto
            || !double.IsFinite(width)
            || width <= 0
            || _measuredWidth == width)
        {
            return Task.CompletedTask;
        }

        _measuredWidth = width;
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>Records the reader's side for one conflicting file and reports the full decision list.</summary>
    /// <param name="row">The conflicting row being decided.</param>
    /// <param name="kind">Whether one agent wins or the reader merges the file by hand.</param>
    /// <param name="agentId">The winning agent's id for an agent decision.</param>
    /// <returns>A task that completes when the host's callback returned.</returns>
    internal async Task OnSideChosenAsync(ConflictMatrixRow row, ConflictResolutionKind kind, string? agentId)
    {
        ConflictResolution resolution;
        try
        {
            resolution = new ConflictResolution(row.Path, kind, agentId);
        }
        catch (ArgumentException)
        {
            // A radio that names neither a real side nor a manual merge cannot happen through the markup; refusing it
            // keeps a broken host value from throwing out of a render tree.
            return;
        }

        _decisions[row.Path] = resolution;
        RebuildDirectories();
        Announce(Loc["TmConflictRadar_AnnounceResolved", row.Path, SideText(row, resolution)]);
        StateHasChanged();
        await ResolutionsChanged.InvokeAsync(Snapshot());
    }

    /// <summary>Reports the merge to the host, once every conflict is decided.</summary>
    /// <returns>A task that completes when the host's callback returned.</returns>
    internal async Task OnMergeAsync()
    {
        if (!CanMerge)
        {
            return;
        }

        var request = _matrix.BuildMergeRequest(_decisions.Values);
        RebuildDirectories();
        Announce(Loc["TmConflictRadar_AnnounceMerged", Count(request.FileCount), Count(request.ResolvedConflictCount)]);
        StateHasChanged();
        await OnMerge.InvokeAsync(request);
    }

    /// <summary>The accepted decision for one row, if the matrix accepts it.</summary>
    /// <param name="row">The row to look at.</param>
    /// <returns>The decision, or null.</returns>
    internal ConflictResolution? Decision(ConflictMatrixRow row)
        => _matrix.AcceptedResolution(row.Path, _decisions.Values);

    /// <summary>Whether one agent's side is the accepted decision for the row.</summary>
    /// <param name="row">The row to look at.</param>
    /// <param name="agentId">The agent to test.</param>
    /// <returns>Whether the agent's version wins.</returns>
    internal bool IsAgentSide(ConflictMatrixRow row, string? agentId)
        => Decision(row) is { IsAgentSide: true } resolution
           && string.Equals(resolution.AgentId, agentId, StringComparison.Ordinal);

    /// <summary>Whether the row is decided as a manual merge.</summary>
    /// <param name="row">The row to look at.</param>
    /// <returns>Whether the reader will merge the file by hand.</returns>
    internal bool IsManualSide(ConflictMatrixRow row) => Decision(row) is { IsManual: true };

    /// <summary>The decisions as the ordered list the component reports (conflict order, then any other path).</summary>
    /// <returns>The snapshot.</returns>
    internal IReadOnlyList<ConflictResolution> Snapshot()
    {
        var ordered = new List<ConflictResolution>(_decisions.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in _matrix.Conflicts)
        {
            if (_decisions.TryGetValue(row.Path, out var resolution) && seen.Add(row.Path))
            {
                ordered.Add(resolution);
            }
        }

        foreach (var (path, resolution) in _decisions.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (seen.Add(path))
            {
                ordered.Add(resolution);
            }
        }

        return ordered;
    }

    // ── Presentation helpers ─────────────────────────────────────────────────

    /// <summary>The localized activity word of a change kind.</summary>
    /// <param name="kind">The kind to name.</param>
    /// <returns>The localized word.</returns>
    internal string KindText(ChangeKind kind) => Loc[kind switch
    {
        ChangeKind.Added => "TmConflictRadar_ChangeAdded",
        ChangeKind.Deleted => "TmConflictRadar_ChangeDeleted",
        ChangeKind.Renamed => "TmConflictRadar_ChangeRenamed",
        _ => "TmConflictRadar_ChangeModified",
    }];

    /// <summary>The stable slug of a change kind, used as the <c>data-kind</c> hook instead of a localized word.</summary>
    /// <param name="kind">The kind to name.</param>
    /// <returns><c>added</c>, <c>modified</c>, <c>deleted</c> or <c>renamed</c>.</returns>
    internal static string KindSlug(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "added",
        ChangeKind.Deleted => "deleted",
        ChangeKind.Renamed => "renamed",
        _ => "modified",
    };

    /// <summary>The icon that agrees with a change kind.</summary>
    /// <param name="kind">The kind to draw.</param>
    /// <returns>The icon name.</returns>
    internal static string KindIcon(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => IconNames.Plus,
        ChangeKind.Deleted => IconNames.Trash,
        ChangeKind.Renamed => IconNames.ArrowRight,
        _ => IconNames.Edit,
    };

    /// <summary>The directory heading: the path itself, or the localized word for the repository root.</summary>
    /// <param name="directory">The directory (empty for the root).</param>
    /// <returns>The heading text.</returns>
    internal string DirectoryName(string directory)
        => string.IsNullOrEmpty(directory) ? Loc["TmConflictRadar_RootDirectory"] : directory;

    /// <summary>The branch line of an agent's column header.</summary>
    /// <param name="column">The agent's branch changes.</param>
    /// <returns>The branch name or the localized fallback.</returns>
    internal string BranchText(AgentBranchChanges column)
        => column.HasBranch ? column.Branch : Loc["TmConflictRadar_NoBranch"];

    /// <summary>The agent name used in a side choice.</summary>
    /// <param name="agent">The agent to name.</param>
    /// <returns>The host's name or the localized fallback.</returns>
    internal string AgentName(AgentRef agent) => agent.HasName ? agent.Name : Loc["TmAgentBadge_NameFallback"];

    /// <summary>A file count as a grammatical localized phrase.</summary>
    /// <param name="count">The count.</param>
    /// <returns>e.g. "4 files".</returns>
    internal string FileCount(int count) => Loc.Plural("TmConflictRadar_FileCount", count);

    /// <summary>A conflict count as a grammatical localized phrase.</summary>
    /// <param name="count">The count.</param>
    /// <returns>e.g. "1 conflict".</returns>
    internal string ConflictCount(int count) => Loc.Plural("TmConflictRadar_ConflictCount", count);

    /// <summary>The group's file and conflict counts, as localized grammatical phrases.</summary>
    /// <param name="group">The directory group being rendered.</param>
    /// <returns>e.g. "2 files · 1 conflict".</returns>
    internal string DirectoryMeta(ConflictMatrixDirectoryView group)
        => Loc["TmConflictRadar_DirectoryMeta",
            FileCount(group.Rows.Count),
            ConflictCount(group.Rows.Count(row => row.IsConflict))];

    /// <summary>The stable name of one file's radio group.</summary>
    /// <param name="row">The conflict being decided.</param>
    /// <returns>The group's <c>name</c> attribute.</returns>
    internal string RadioGroupName(ConflictMatrixRow row)
        => _id + "-side-" + (_conflictPositions.TryGetValue(row.Path, out var index) ? index : 0)
            .ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The stable id of a file row's <c>scope="row"</c> header cell. A conflicting file's RESOLUTION row points at it
    /// through <c>headers</c>, so the side choice is read as belonging to the file above it instead of as an anonymous
    /// row. Unique per row, because every rendered row is in the matrix's row list.
    /// </summary>
    /// <param name="row">The row to identify.</param>
    /// <returns>The <c>id</c> of that row's header cell.</returns>
    internal string RowHeaderId(ConflictMatrixRow row)
        => _id + "-row-" + (_rowPositions.TryGetValue(row.Path, out var index) ? index : 0)
            .ToString(CultureInfo.InvariantCulture);

    /// <summary>The re-mount version of a file's radios: bumped when the host replaces the decision list.</summary>
    internal int RadioVersion => _radioVersion;

    /// <summary>The accessible name of one file's side choice.</summary>
    /// <param name="row">The conflict being decided.</param>
    /// <returns>e.g. "Resolve README.md".</returns>
    internal string ResolveLabel(ConflictMatrixRow row) => Loc["TmConflictRadar_ResolveLabel", row.Path];

    private string SideText(ConflictMatrixRow row, ConflictResolution resolution)
    {
        if (resolution.IsManual)
        {
            return Loc["TmConflictRadar_ManualSide"];
        }

        foreach (var cell in row.Changed)
        {
            if (string.Equals(cell.Agent.Id, resolution.AgentId, StringComparison.Ordinal))
            {
                return AgentName(cell.Agent);
            }
        }

        return Loc["TmConflictRadar_ManualSide"];
    }

    private static string Count(int value) => value.ToString(CultureInfo.CurrentCulture);

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

    /// <summary>
    /// The surface heading. Razor cannot build a dynamic element NAME (&lt;h@Level&gt; is not a thing), so the heading is
    /// composed here at the requested level — the same approach the rest of the AI package uses.
    /// </summary>
    private RenderFragment SurfaceTitle => Heading(EffectiveHeadingLevel, "tm-ai-radar__title", TitleId,
        TestId("radar-title"), builder => builder.AddContent(0, HeadingText));

    /// <summary>One heading element at the requested level, clamped into the range a sectioning element allows.</summary>
    private static RenderFragment Heading(int level, string cssClass, string id, string? testId, RenderFragment content)
        => builder =>
        {
            builder.OpenElement(0, "h" + Math.Clamp(level, 2, 6).ToString(CultureInfo.InvariantCulture));
            builder.AddAttribute(1, "class", cssClass);
            builder.AddAttribute(2, "id", id);
            if (testId is not null)
            {
                builder.AddAttribute(3, "data-testid", testId);
            }

            builder.AddContent(4, content);
            builder.CloseElement();
        };

    /// <summary>One directory group as the grid renders it (the filter and the collapse applied).</summary>
    /// <param name="Directory">The directory (empty for the repository root).</param>
    /// <param name="Collapsed">Whether the group's rows are hidden.</param>
    /// <param name="Rows">The rows to render.</param>
    internal sealed record ConflictMatrixDirectoryView(
        string Directory,
        bool Collapsed,
        IReadOnlyList<ConflictMatrixRow> Rows);
}
