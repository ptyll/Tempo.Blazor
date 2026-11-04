using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// What a context window is made of: the system prompt, the tools, the memory, the files, the conversation and the
/// tool results, measured against the model's budget.
/// <para>
/// The presentation (<see cref="Variant"/>) changes only the PICTURE — one stacked bar (the default), a hundred
/// cells over the budget (so the free headroom is visible) or the core <c>TmChart</c> treemap. Every picture is
/// decorative (<c>aria-hidden</c>); the legend table under it is the representation a reader, a screen reader and
/// a copy/paste get, and it carries the label, the tokens, the whole percentage and the localized Required /
/// Summarized / Omitted words of every section — never a colour-only cue.
/// </para>
/// <para>
/// The total is a single <c>role="meter"</c> element carrying the invariant machine values and a localized
/// <c>aria-valuetext</c>, so the amount is heard once (the visible readout above it is sighted-only duplication).
/// The percentages of the sections come from the largest-remainder split
/// (<see cref="ContextUsageMath.Shares"/>), so they add up to exactly 100 %; a section with tokens whose rounded
/// share is zero reads "&lt; 1 %" and still paints a visible sliver — a token-bearing part is never invisible.
/// </para>
/// <para>
/// A section with <see cref="ContextUsageSection.Items"/> expands into its entries through a disclosure button
/// (a native button carrying <c>aria-expanded</c> and, while open, <c>aria-controls</c>), and a removable section
/// or entry offers the localized Remove action. The component NEVER edits the snapshot: it reports the reader's
/// intent through <see cref="OnRemoveItem"/> (the whole section when
/// <see cref="ContextUsageRemoval.Item"/> is null, one entry otherwise) and <see cref="OnCompact"/>, and the host
/// decides what to do. Because the pressed control can disappear with the removal, the focus is handed back to
/// the section's own disclosure button — or to the component root when that is gone too.
/// </para>
/// <para>
/// The component is transport-agnostic and declares no motion at all: it renders the snapshot it is given and
/// never polls (a polling host would otherwise flicker on every refresh). The ONE polite live region announces a
/// budget CROSSING only, so a page load stays silent.
/// </para>
/// </summary>
public partial class TmContextUsage : TmComponentBase
{
    /// <summary>
    /// The smallest share of the bar a section with tokens may paint, in percent. A one-token section in a huge
    /// context rounds to 0 %; the floor keeps it visible (the bar clips the overflow, so the total can never grow
    /// the card).
    /// </summary>
    private const double MinimumSegmentPercent = 0.5;

    /// <summary>How many cells the grid presentation has: one per percent of the budget.</summary>
    private const int GridCells = 100;

    /// <summary>
    /// The invisible character that makes a repeated announcement a real mutation. Two localized sentences can
    /// coincide, and an identical write into a polite live region is silent in some screen readers; the marker
    /// alternates on and off, so every announcement differs from the one before it.
    /// </summary>
    private const string AnnouncementMarker = "\u200B";

    /// <summary>The per-instance id prefix, so several usage panels can share one page.</summary>
    private readonly string _id = $"tm-context-usage-{Guid.NewGuid():n}";

    /// <summary>The keys of the sections whose entries are currently shown.</summary>
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    /// <summary>The disclosure buttons, by section key — the focus hand-back targets after a removal.</summary>
    private readonly Dictionary<string, ElementReference> _sectionRefs = new(StringComparer.Ordinal);

    private readonly ChartData _chartData = new() { Labels = [], Datasets = [] };

    private ElementReference _rootRef;

    private IReadOnlyList<UsageRow> _rows = [];
    private IReadOnlyList<UsageSegment> _segments = [];
    private IReadOnlyList<UsageCell> _cells = [];
    private IReadOnlyList<ChartTreeNode> _treeNodes = [];

    private string? _highlight;
    private string _announcement = string.Empty;
    private bool _announcementMarker;
    private bool _wasOver;
    private bool _stateKnown;
    private bool _focusRootPending;
    private string? _focusSectionKey;
    private int _used;
    private int _budget;
    private int? _percent;
    private bool _over;
    private int? _cachePercent;
    private bool _hasContent;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The snapshot to render — the budget, the sections and the cache-hit share. Null (or a snapshot with no
    /// sections) renders the localized empty state instead of an empty chart. The component never mutates it and
    /// never polls: a host pushes a new instance as the conversation grows.
    /// </summary>
    [Parameter] public ContextUsageSnapshot? Snapshot { get; set; }

    /// <summary>
    /// How the context is presented. Default <see cref="ContextUsageVariant.Bar"/> (one stacked bar); the grid
    /// shows a hundred cells over the budget and the treemap draws the sections through the core <c>TmChart</c>.
    /// The legend table, the meter and the empty state are identical in every variant.
    /// </summary>
    [Parameter] public ContextUsageVariant Variant { get; set; } = ContextUsageVariant.Bar;

    /// <summary>
    /// Raised when the reader asks to drop something: something reported by a section with
    /// <see cref="ContextUsageSection.CanRemove"/> (the whole section — <see cref="ContextUsageRemoval.Item"/> is
    /// null) or an entry with <see cref="ContextUsageItem.CanRemove"/>. The action is rendered only when this
    /// callback is bound, so a read-only host offers no removal at all.
    /// </summary>
    [Parameter] public EventCallback<ContextUsageRemoval> OnRemoveItem { get; set; }

    /// <summary>
    /// Raised when the reader asks for a compaction (summarise the optional parts). The action is rendered only
    /// when this callback is bound, and it is DISABLED while no section can be compacted
    /// (<see cref="ContextUsageMath.IsCompactable(ContextUsageSnapshot?)"/>), so a button is never offered that
    /// would do nothing.
    /// </summary>
    [Parameter] public EventCallback OnCompact { get; set; }

    /// <summary>
    /// The visible heading of the panel and, through <c>aria-labelledby</c>, the name of the meter. Null renders no
    /// heading; the panel and the meter are then named by <see cref="AriaLabel"/> or the localized default.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>; a host that already titles the card passes
    /// <c>false</c> and keeps naming the meter by <see cref="AriaLabel"/> or the localized default.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>
    /// The heading's level in the page outline, clamped to 2–6. Default 3. The heading is only rendered when
    /// <see cref="Title"/> is set and <see cref="ShowTitle"/> is true.
    /// </summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// The accessible name used when no visible heading names the panel, and the panel root's name. Null falls back
    /// to the localized "Context usage". A rendered <see cref="Title"/> always wins for the meter, so one control
    /// never carries two names.
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>
    /// The culture that formats the token counts and the percentages. Null (the default) means the current culture
    /// at render time. The machine-readable attributes (<c>aria-valuenow</c> / <c>aria-valuemax</c>) are always
    /// invariant — a culture's group separator would change the value a screen reader reads.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived names ────────────────────────────────────────────────────────

    /// <summary>The culture every human-facing number is formatted with.</summary>
    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    /// <summary>Whether a heading is rendered.</summary>
    private bool HasTitle => ShowTitle && !string.IsNullOrWhiteSpace(Title);

    /// <summary>The DOM id of the rendered heading (the meter's <c>aria-labelledby</c> target).</summary>
    private string TitleId => $"{_id}-title";

    /// <summary>The DOM id of the cache-hit badge (one of the meter's description targets).</summary>
    private string CacheId => $"{_id}-cache";

    /// <summary>The DOM id of the over-budget line (one of the meter's description targets).</summary>
    private string OverId => $"{_id}-over";

    /// <summary>The heading tag, clamped to the 2–6 band of the page outline.</summary>
    private string TitleHeadingTag => "h" + Math.Clamp(HeadingLevel, 2, 6).ToString(CultureInfo.InvariantCulture);

    /// <summary>The visible heading, built as a fragment so its tag comes from <see cref="HeadingLevel"/>.</summary>
    private RenderFragment TitleHeading => builder =>
    {
        builder.OpenElement(0, TitleHeadingTag);
        builder.AddAttribute(1, "class", "tm-context-usage__title");
        builder.AddAttribute(2, "id", TitleId);
        builder.AddContent(3, Title);
        builder.CloseElement();
    };

    /// <summary>
    /// The panel's accessible name: the visible heading, then the host's <see cref="AriaLabel"/>, then the
    /// localized default.
    /// </summary>
    private string ResolvedName
        => !string.IsNullOrWhiteSpace(Title) ? Title!.Trim()
            : !string.IsNullOrWhiteSpace(AriaLabel) ? AriaLabel!.Trim()
            : Loc["TmContextUsage_AriaLabel"];

    /// <summary>The table caption, which is the panel's name (the caption itself is visually hidden).</summary>
    private string Caption => ResolvedName;

    /// <summary>The meter's <c>aria-labelledby</c>: only a RENDERED heading may name it.</summary>
    private string? MeterLabelledBy => HasTitle ? TitleId : null;

    /// <summary>The meter's <c>aria-label</c>: only when no rendered heading names it, so it never has two names.</summary>
    private string? MeterAriaLabel => HasTitle ? null : ResolvedName;

    /// <summary>The ids of the lines that add information the meter value cannot carry.</summary>
    private string? MeterDescribedBy
    {
        get
        {
            var ids = new List<string>(2);
            if (_cachePercent is not null)
            {
                ids.Add(CacheId);
            }

            if (_over)
            {
                ids.Add(OverId);
            }

            return ids.Count == 0 ? null : string.Join(' ', ids);
        }
    }

    /// <summary>The meter element's classes: the treemap presentation keeps it as a visually hidden meter.</summary>
    private string MeterClass => Variant == ContextUsageVariant.Treemap
        ? "tm-context-usage__meter tm-context-usage__meter--sr tm-sr-only"
        : "tm-context-usage__meter";

    /// <summary>The root classes: the base class, the presentation modifier, the over-budget mark and the host's own.</summary>
    private string RootClass
    {
        get
        {
            var classes = new StringBuilder("tm-context-usage");
            classes.Append(' ').Append(VariantModifier);
            if (_over)
            {
                classes.Append(" tm-context-usage--over-budget");
            }

            if (!string.IsNullOrEmpty(Class))
            {
                classes.Append(' ').Append(Class);
            }

            return classes.ToString();
        }
    }

    /// <summary>The presentation as a machine-readable slug, rendered as <c>data-variant</c>.</summary>
    private string VariantSlug => Variant switch
    {
        ContextUsageVariant.Grid => "grid",
        ContextUsageVariant.Treemap => "treemap",
        _ => "bar",
    };

    /// <summary>The presentation's CSS modifier.</summary>
    private string VariantModifier => "tm-context-usage--" + VariantSlug;

    /// <summary><c>true</c> while the context is over its budget, otherwise an omitted attribute.</summary>
    private string? OverBudgetAttribute => _over ? "true" : null;

    /// <summary>Whether there is anything to render.</summary>
    private bool HasContent => _hasContent;

    /// <summary>Whether a compaction would change anything.</summary>
    private bool CanCompact => ContextUsageMath.IsCompactable(Snapshot);

    /// <summary>
    /// The visible amount, with the culture's group separators. An unknown budget has no ratio to show, so the
    /// read-out names only what is used — "3,500 / 0 tokens" would read as a nonsensical measurement.
    /// </summary>
    private string AmountText => _budget > 0
        ? string.Format(
            EffectiveCulture,
            Loc["TmContextUsage_Amount"],
            FormatTokens(_used),
            FormatTokens(_budget))
        : string.Format(EffectiveCulture, Loc["TmContextUsage_AmountNoBudget"], FormatTokens(_used));

    /// <summary>The visible share of the budget, or <c>null</c> when the budget is unknown.</summary>
    private string? PercentText => _percent is { } percent ? FormatPercent(percent) : null;

    /// <summary>
    /// The meter's machine value. With a known budget it is CLAMPED to the budget, because ARIA does not allow
    /// <c>aria-valuenow</c> to sit above <c>aria-valuemax</c> and a browser may cap such a value on its own; the
    /// REAL total and the real percentage stay in the visible read-out and in the localized
    /// <c>aria-valuetext</c>, which is what a screen reader actually reads, so an over-budget context is never
    /// under-reported. An unknown budget has no maximum to clamp against, so the used amount is reported as is.
    /// </summary>
    private int MeterValueNow => _budget > 0 ? Math.Min(_used, _budget) : _used;

    /// <summary>
    /// The meter's accessible value text. With a known budget it names the amount, the budget and the whole
    /// percentage; with an unknown budget it says only what is used (no percentage is claimed).
    /// </summary>
    private string ValueText => _percent is { } percent
        ? string.Format(
            EffectiveCulture,
            Loc["TmContextUsage_ValueText"],
            FormatTokens(_used),
            FormatTokens(_budget),
            FormatPercent(percent))
        : string.Format(EffectiveCulture, Loc["TmContextUsage_ValueTextNoBudget"], FormatTokens(_used));

    /// <summary>The cache-hit sentence.</summary>
    /// <param name="percent">The whole percentage served from the cache.</param>
    private string CacheText(int percent)
        => string.Format(EffectiveCulture, Loc["TmContextUsage_CacheHit"], FormatPercent(percent));

    /// <summary>The over-budget line, naming the exact excess.</summary>
    private string OverText
        => string.Format(
            EffectiveCulture,
            Loc["TmContextUsage_OverBudget"],
            FormatTokens(_used - _budget));

    /// <summary>The live region's text with the repeat marker applied.</summary>
    private string AnnouncementText => _announcementMarker ? _announcement + AnnouncementMarker : _announcement;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        Build();

        // A budget CROSSING is the one fact a reader must be told without looking: it is announced once per
        // transition, and a first render says nothing, so a page load never talks over the reader.
        if (_stateKnown && _over != _wasOver)
        {
            Announce(_over ? Loc["TmContextUsage_AnnounceOverBudget"] : Loc["TmContextUsage_AnnounceWithinBudget"]);
        }

        _wasOver = _over;
        _stateKnown = true;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var sectionKey = _focusSectionKey;
        if (sectionKey is not null)
        {
            _focusSectionKey = null;
            if (_sectionRefs.TryGetValue(sectionKey, out var toggle) && await TryFocusAsync(toggle))
            {
                return;
            }

            _focusRootPending = true;
        }

        if (_focusRootPending)
        {
            _focusRootPending = false;
            await TryFocusAsync(_rootRef, preventScroll: true);
        }
    }

    /// <summary>
    /// Moves the focus, best effort — never from the render pipeline with a throw. The very control the hand-back
    /// exists for can leave the page between the render and this call, and a browser then rejects the focus
    /// ("Unable to focus an invalid element") with a <see cref="JSException"/>; a WASM app may have no DOM and a
    /// Server app may have lost its circuit (<see cref="JSDisconnectedException"/>); a detached or disposed
    /// reference answers with <see cref="InvalidOperationException"/>, and a navigation mid-focus with
    /// <see cref="TaskCanceledException"/>. None of them is worth tearing the circuit down from
    /// <c>OnAfterRenderAsync</c>, so all of them are swallowed — the reader keeps their place on the page or, in
    /// the worst case, loses the focus move.
    /// </summary>
    /// <param name="element">The focus target; a detached reference (no id) is skipped.</param>
    /// <param name="preventScroll">Whether the focus move must not scroll the page.</param>
    /// <returns><c>true</c> when the focus move was issued.</returns>
    private static async Task<bool> TryFocusAsync(ElementReference element, bool preventScroll = false)
    {
        if (element.Id is null)
        {
            return false;
        }

        try
        {
            await element.FocusAsync(preventScroll);
            return true;
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            return false;
        }
    }

    // ── Rendering data ───────────────────────────────────────────────────────

    /// <summary>
    /// Turns the snapshot into everything the render needs: the legend rows (with their largest-remainder shares),
    /// the bar segments, the grid cells and the treemap nodes. Runs once per parameter set, so the render is a pure
    /// projection.
    /// </summary>
    private void Build()
    {
        var sections = (Snapshot?.Sections ?? [])
            .Where(section => section is not null)
            .ToList();

        _used = ContextUsageMath.UsedTokens(Snapshot);
        _budget = Math.Max(Snapshot?.BudgetTokens ?? 0, 0);
        _percent = ContextUsageMath.BudgetPercent(Snapshot);
        _over = ContextUsageMath.IsOverBudget(Snapshot);
        _hasContent = sections.Count > 0;

        var cache = Snapshot?.CacheHit;
        _cachePercent = cache is { } fraction && !double.IsNaN(fraction)
            ? (int)Math.Round(Math.Clamp(fraction, 0d, 1d) * 100d, MidpointRounding.AwayFromZero)
            : null;

        var tokens = sections.Select(ContextUsageMath.TokensOf).ToList();
        var shares = ContextUsageMath.Shares(tokens, _used);

        var rows = new List<UsageRow>(sections.Count);
        var segments = new List<UsageSegment>(sections.Count);
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            var items = ItemsOf(section);
            var kind = KindSlug(section.Kind);
            rows.Add(new UsageRow(section, tokens[index], shares[index], items, DetailId(section.Key)));
            segments.Add(new UsageSegment(section.Key, kind, tokens[index], shares[index]));
        }

        _rows = rows;
        _segments = segments;
        _cells = BuildCells(sections, tokens);
        _treeNodes = Variant == ContextUsageVariant.Treemap ? BuildTreeNodes(sections, tokens) : [];

        // A section that left the snapshot must not keep its (invisible) expansion state alive; the prune also
        // stops a key from a previous snapshot lighting up a row of the new one.
        foreach (var stale in _expanded.Where(key => rows.All(row => row.Section.Key != key)).ToList())
        {
            _expanded.Remove(stale);
        }

        // The same goes for the disclosure buttons the focus hand-back targets: a section that left the snapshot
        // — or that lost its last entry and therefore its disclosure button — must not keep a reference around.
        // A stale reference is what a removal followed by a re-render without the section used to focus, and a
        // browser answers that with a JSException that kills a Server circuit.
        foreach (var stale in _sectionRefs.Keys
            .Where(key => rows.All(row => row.Section.Key != key || row.Items.Count == 0))
            .ToList())
        {
            _sectionRefs.Remove(stale);
        }

        // Nothing valid to hand the focus back to: fall through to the root, which always stays on the page.
        if (_focusSectionKey is { } pending && !_sectionRefs.ContainsKey(pending))
        {
            _focusSectionKey = null;
            _focusRootPending = true;
        }

        if (_highlight is { } highlight && rows.All(row => row.Section.Key != highlight))
        {
            _highlight = null;
        }
    }

    /// <summary>The entries of a section, without the nulls a bad host may smuggle in.</summary>
    /// <param name="section">The section.</param>
    private static IReadOnlyList<ContextUsageItem> ItemsOf(ContextUsageSection section)
        => section.Items is { Count: > 0 } items
            ? items.Where(item => item is not null).ToList()
            : [];

    /// <summary>
    /// The grid: one cell per percent of the BUDGET, so the free headroom is visible. A cell is used while its
    /// position is inside the used share and belongs to the section whose span covers its centre; a cell beyond the
    /// used share is empty and owns no section.
    /// </summary>
    /// <param name="sections">The sections in reading order.</param>
    /// <param name="tokens">The tokens of each section, index-aligned.</param>
    private IReadOnlyList<UsageCell> BuildCells(IReadOnlyList<ContextUsageSection> sections, IReadOnlyList<int> tokens)
    {
        var cells = new List<UsageCell>(GridCells);
        if (_budget <= 0 || sections.Count == 0)
        {
            for (var index = 0; index < GridCells; index++)
            {
                cells.Add(new UsageCell(index, null, null, false));
            }

            return cells;
        }

        var starts = new double[sections.Count];
        var running = 0d;
        for (var index = 0; index < sections.Count; index++)
        {
            starts[index] = running;
            running += Math.Max(tokens[index], 0) * 100d / _budget;
        }

        var usedPercent = _used * 100d / _budget;
        var usedCells = (int)Math.Clamp(Math.Ceiling(usedPercent), 0, GridCells);

        for (var index = 0; index < GridCells; index++)
        {
            var used = index < usedCells;
            string? key = null;
            string? kind = null;

            if (used)
            {
                var centre = index + 0.5;
                for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
                {
                    if (starts[sectionIndex] <= centre)
                    {
                        key = sections[sectionIndex].Key;
                        kind = KindSlug(sections[sectionIndex].Kind);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            cells.Add(new UsageCell(index, key, kind, used));
        }

        return cells;
    }

    /// <summary>
    /// The treemap: one group per section and one tile per entry, so the picture maps the same facts the table
    /// carries. A section without entries is one tile.
    /// </summary>
    /// <param name="sections">The sections in reading order.</param>
    /// <param name="tokens">The tokens of each section, index-aligned.</param>
    private static IReadOnlyList<ChartTreeNode> BuildTreeNodes(
        IReadOnlyList<ContextUsageSection> sections,
        IReadOnlyList<int> tokens)
    {
        var nodes = new List<ChartTreeNode>(sections.Count);
        for (var index = 0; index < sections.Count; index++)
        {
            if (tokens[index] <= 0)
            {
                continue;
            }

            var section = sections[index];
            var items = ItemsOf(section);
            nodes.Add(new ChartTreeNode
            {
                Label = section.Label,
                Value = tokens[index],
                Color = $"var(--tm-context-usage-{KindSlug(section.Kind)})",
                Children = items.Count == 0
                    ? null
                    : items.Select(item => new ChartTreeNode
                    {
                        Label = item.Label,
                        Value = Math.Max(item.Tokens, 1),
                    }).ToList(),
            });
        }

        return nodes;
    }

    // ── Presentation helpers ─────────────────────────────────────────────────

    /// <summary>The stable DOM id of a section's detail row (sanitized, so a key is always a usable id).</summary>
    /// <param name="key">The section key.</param>
    private string DetailId(string key)
    {
        var builder = new StringBuilder(_id.Length + key.Length + 8);
        builder.Append(_id).Append("-detail-");
        foreach (var character in key)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        return builder.ToString();
    }

    /// <summary>The tone slug of a section kind — the suffix of its <c>--tm-context-usage-*</c> knob.</summary>
    /// <param name="kind">The kind.</param>
    private static string KindSlug(ContextUsageKind kind) => kind switch
    {
        ContextUsageKind.System => "system",
        ContextUsageKind.Tools => "tools",
        ContextUsageKind.Memory => "memory",
        ContextUsageKind.Files => "files",
        ContextUsageKind.Chat => "chat",
        ContextUsageKind.Results => "results",
        _ => "other",
    };

    /// <summary>The bar segment's classes: its tone and — while its section is highlighted — the mark.</summary>
    /// <param name="segment">The segment.</param>
    private string SegmentClass(UsageSegment segment)
        => IsHighlighted(segment.Key)
            ? $"tm-context-usage__segment tm-context-usage__segment--{segment.Kind} tm-context-usage__segment--highlight"
            : $"tm-context-usage__segment tm-context-usage__segment--{segment.Kind}";

    /// <summary>
    /// The segment's width, floored so a tiny section still shows. The width is the section's share of the BUDGET,
    /// not of the used amount, so the bar's tail is the same free headroom the grid paints — a bar that always
    /// reaches the end would claim a full context. With an unknown budget there is nothing to leave room for, so
    /// the segments fill the bar by their share of the used amount.
    /// </summary>
    /// <param name="segment">The segment.</param>
    private string SegmentStyle(UsageSegment segment)
    {
        var width = segment.Tokens <= 0
            ? 0d
            : _budget > 0
                ? Math.Max(segment.Tokens * 100d / _budget, MinimumSegmentPercent)
                : Math.Max(segment.Share, MinimumSegmentPercent);

        return "inline-size: " + width.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>The grid cell's classes: empty, or used with its section's tone and the highlight mark.</summary>
    /// <param name="cell">The cell.</param>
    private string CellClass(UsageCell cell)
    {
        var classes = new StringBuilder("tm-context-usage__cell");
        if (!cell.IsUsed || cell.Kind is null)
        {
            classes.Append(" tm-context-usage__cell--empty");
            return classes.ToString();
        }

        classes.Append(" tm-context-usage__cell--used");
        classes.Append(" tm-context-usage__cell--").Append(cell.Kind);
        if (IsHighlighted(cell.SectionKey))
        {
            classes.Append(" tm-context-usage__cell--highlight");
        }

        return classes.ToString();
    }

    /// <summary>The legend swatch's classes: the section's tone and, while highlighted, the mark.</summary>
    /// <param name="row">The row.</param>
    private string SwatchClass(UsageRow row)
    {
        var kind = KindSlug(row.Section.Kind);
        return IsHighlighted(row.Section.Key)
            ? $"tm-context-usage__swatch tm-context-usage__swatch--{kind} tm-context-usage__swatch--highlight"
            : $"tm-context-usage__swatch tm-context-usage__swatch--{kind}";
    }

    /// <summary>The legend row's classes: the highlight mark and nothing else.</summary>
    /// <param name="row">The row.</param>
    private string RowClass(UsageRow row)
        => IsHighlighted(row.Section.Key)
            ? "tm-context-usage__row tm-context-usage__row--highlight"
            : "tm-context-usage__row";

    /// <summary>A state word's classes.</summary>
    /// <param name="modifier">The word's modifier (required / summarized / omitted).</param>
    private static string TagClass(string modifier) => $"tm-context-usage__tag tm-context-usage__tag--{modifier}";

    /// <summary>The state words of a section, in a fixed order so two renders never disagree.</summary>
    /// <param name="section">The section.</param>
    private IReadOnlyList<UsageTag> TagsOf(ContextUsageSection section)
    {
        var tags = new List<UsageTag>(3);
        if (section.IsRequired)
        {
            tags.Add(new UsageTag("required", Loc["TmContextUsage_Required"]));
        }

        if (section.IsSummarized)
        {
            tags.Add(new UsageTag("summarized", Loc["TmContextUsage_Summarized"]));
        }

        if (section.IsOmitted)
        {
            tags.Add(new UsageTag("omitted", Loc["TmContextUsage_Omitted"]));
        }

        return tags;
    }

    /// <summary>
    /// The share as words. A section with tokens whose rounded share is zero reads "&lt; 1 %" — a section that
    /// carries a thousand tokens is not "0 %", it is under one percent.
    /// </summary>
    /// <param name="row">The row.</param>
    private string ShareText(UsageRow row)
        => row.Tokens > 0 && row.Share <= 0
            ? Loc["TmContextUsage_ShareUnderOne"]
            : FormatPercent(row.Share);

    /// <summary>The token count with the culture's group separators.</summary>
    /// <param name="tokens">The count.</param>
    private string FormatTokens(int tokens) => tokens.ToString("N0", EffectiveCulture);

    /// <summary>The whole percentage, placed by the culture's own percent pattern.</summary>
    /// <param name="percent">The whole percentage.</param>
    private string FormatPercent(int percent)
        => (percent / 100d).ToString("P0", EffectiveCulture);

    /// <summary>
    /// A machine-readable number for <c>aria-valuenow</c> / <c>aria-valuemax</c>: always invariant, because a
    /// culture's group separator would turn the value a screen reader reads into a different number.
    /// </summary>
    /// <param name="value">The number.</param>
    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    // ── Interaction ──────────────────────────────────────────────────────────

    /// <summary>Whether a section is currently shown expanded.</summary>
    /// <param name="row">The row.</param>
    private bool IsExpanded(UsageRow row) => _expanded.Contains(row.Section.Key);

    /// <summary>Whether a section is the highlighted one.</summary>
    /// <param name="key">The section key; <c>null</c> is never highlighted.</param>
    private bool IsHighlighted(string? key) => key is not null && string.Equals(_highlight, key, StringComparison.Ordinal);

    /// <summary>Opens or closes a section's entry list.</summary>
    /// <param name="key">The section key.</param>
    private void ToggleSection(string key)
    {
        if (!_expanded.Remove(key))
        {
            _expanded.Add(key);
        }
    }

    /// <summary>Highlights a section — from a hovered grid cell or from a focused legend row.</summary>
    /// <param name="key">The section key, or <c>null</c> to clear.</param>
    private void Highlight(string? key)
    {
        if (key is not null)
        {
            _highlight = key;
        }
    }

    /// <summary>Clears the highlight, but only when it still belongs to <paramref name="key"/>.</summary>
    /// <param name="key">The section key.</param>
    private void ClearHighlight(string? key)
    {
        if (key is null || IsHighlighted(key))
        {
            _highlight = null;
        }
    }

    /// <summary>Whether the section-level Remove action is offered: the model allows it and a handler is bound.</summary>
    /// <param name="section">The section.</param>
    private bool CanRemove(ContextUsageSection section) => section.CanRemove && OnRemoveItem.HasDelegate;

    /// <summary>Reports the whole section as the removal target, then hands the focus back.</summary>
    /// <param name="row">The row.</param>
    private async Task RemoveSectionAsync(UsageRow row)
    {
        if (!CanRemove(row.Section))
        {
            return;
        }

        // The pressed button leaves WITH its section — its own (and the section's) disclosure button is gone in
        // the same update — so the panel root is the only target that is still there when the host applies it.
        _focusSectionKey = null;
        _focusRootPending = true;
        await OnRemoveItem.InvokeAsync(new ContextUsageRemoval
        {
            SectionKey = row.Section.Key,
            SectionLabel = row.Section.Label,
        });
    }

    /// <summary>Reports one entry of a section as the removal target, then hands the focus back.</summary>
    /// <param name="row">The row the entry belongs to.</param>
    /// <param name="item">The entry.</param>
    private async Task RemoveItemAsync(UsageRow row, ContextUsageItem item)
    {
        if (!item.CanRemove || !OnRemoveItem.HasDelegate)
        {
            return;
        }

        PrepareFocusAfterRemoval(row);
        await OnRemoveItem.InvokeAsync(new ContextUsageRemoval
        {
            SectionKey = row.Section.Key,
            SectionLabel = row.Section.Label,
            Item = item,
        });
    }

    /// <summary>
    /// Decides where the focus goes once the pressed control disappears with the removal: the section's own
    /// disclosure button while it still has an entry list, otherwise the component root (a real focus target
    /// through <c>tabindex="-1"</c>). The section-level removal never lands here — its disclosure button leaves
    /// with it (see <see cref="RemoveSectionAsync"/>) — and a section that the host removes after an entry
    /// removal is caught by <see cref="Build"/>, which replaces a stored reference that no longer renders.
    /// </summary>
    /// <param name="row">The row the removal was raised from.</param>
    private void PrepareFocusAfterRemoval(UsageRow row)
    {
        if (row.Items.Count > 0)
        {
            _focusSectionKey = row.Section.Key;
        }
        else
        {
            _focusSectionKey = null;
            _focusRootPending = true;
        }
    }

    /// <summary>Raises the compaction request; a compaction with nothing to drop is never reported.</summary>
    private async Task CompactAsync()
    {
        if (!CanCompact || !OnCompact.HasDelegate)
        {
            return;
        }

        await OnCompact.InvokeAsync();
    }

    /// <summary>
    /// Writes the live region's sentence. An identical wording toggles one invisible marker character, so a repeat
    /// is still a mutation for a polite region; a different wording clears the marker.
    /// </summary>
    /// <param name="text">The sentence.</param>
    private void Announce(string text)
    {
        text ??= string.Empty;
        _announcementMarker = text.Length > 0 && string.Equals(text, _announcement, StringComparison.Ordinal)
            ? !_announcementMarker
            : false;
        _announcement = text;
    }

    /// <summary>One legend row: the section, what it measures and the ids its controls use.</summary>
    /// <param name="Section">The section.</param>
    /// <param name="Tokens">The tokens the section contributes.</param>
    /// <param name="Share">The whole percentage of the used context.</param>
    /// <param name="Items">The section's entries, in the host's order.</param>
    /// <param name="DetailId">The DOM id of the detail row rendered while the section is open.</param>
    private sealed record UsageRow(
        ContextUsageSection Section,
        int Tokens,
        int Share,
        IReadOnlyList<ContextUsageItem> Items,
        string DetailId);

    /// <summary>One painted bar segment.</summary>
    /// <param name="Key">The section key.</param>
    /// <param name="Kind">The tone slug of the section kind.</param>
    /// <param name="Tokens">The tokens the segment stands for.</param>
    /// <param name="Share">The whole percentage of the used context.</param>
    private sealed record UsageSegment(string Key, string Kind, int Tokens, int Share);

    /// <summary>One grid cell: its position, its section (null when free) and whether it is used.</summary>
    /// <param name="Index">The cell's position, 0-based.</param>
    /// <param name="SectionKey">The key of the section the cell belongs to, or null for a free cell.</param>
    /// <param name="Kind">The tone slug of that section, or null for a free cell.</param>
    /// <param name="IsUsed">Whether the cell is inside the used share of the budget.</param>
    private readonly record struct UsageCell(int Index, string? SectionKey, string? Kind, bool IsUsed);

    /// <summary>One localized state word of a section.</summary>
    /// <param name="Modifier">The CSS modifier (required / summarized / omitted).</param>
    /// <param name="Text">The localized word.</param>
    private readonly record struct UsageTag(string Modifier, string Text);
}
