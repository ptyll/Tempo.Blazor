using System.Collections.ObjectModel;

namespace Tempo.Blazor.Models;

/// <summary>
/// One span of a trace: a named, timed unit of work with an optional parent, an outcome and an
/// optional bag of numeric metrics (tokens in/out, cost, …).
/// <para>
/// The model is data only — it carries no callbacks and no rendering hints. A trace is a forest that
/// a host supplies as a FLAT list; <c>TraceWaterfallLayout</c> builds the tree from
/// <see cref="ParentId"/>:
/// </para>
/// <list type="bullet">
/// <item>a span whose <see cref="ParentId"/> is null/blank is a root;</item>
/// <item>a span whose <see cref="ParentId"/> names no supplied span is an ORPHAN and is attached
///   under the root, so its data is never silently dropped;</item>
/// <item>a span in a parent cycle (A→B→A, or a span parented to itself) is REJECTED — it is
///   reported by the layout instead of throwing or recursing forever, and its children become
///   orphans under the root.</item>
/// </list>
/// <para>
/// <see cref="Id"/> must be unique within the list. A span with an empty <see cref="Id"/> — and every
/// span that repeats an already-seen id — is rejected as invalid; its children resolve as orphans.
/// </para>
/// </summary>
public sealed record TraceSpan
{
    /// <summary>
    /// The stable identity of the span within one trace, used for the parent links, the selection
    /// (<c>@bind-SelectedSpanId</c>) and the row keys. Required and unique; an empty id is rejected.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The <see cref="Id"/> of the parent span, or <see langword="null"/> for a root span. A value
    /// that names no supplied span makes the span an orphan (rendered under the root); a value that
    /// closes a cycle rejects the cycle instead of hanging the layout.
    /// </summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// The human name of the span ("gpt-4o completion", "search_web", …). Renderers fall back to
    /// <see cref="Id"/> for a blank name, so a row is never nameless.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What the span represents. Drives the row's icon and its <c>--tm-trace-*</c> colour.</summary>
    public TraceSpanKind Kind { get; init; } = TraceSpanKind.Custom;

    /// <summary>The absolute start of the span on the trace clock.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// The absolute end of the span. <see langword="null"/> means "not finished yet": the layout
    /// extends the bar to the caller-supplied "now", which is what makes a
    /// <see cref="TraceSpanStatus.Running"/> span grow in live mode. A value EARLIER than
    /// <see cref="StartedAt"/> is treated as a zero-length span, never as a negative bar.
    /// </summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>The outcome of the span. <see cref="TraceSpanStatus.Running"/> keeps the live ticker going.</summary>
    public TraceSpanStatus Status { get; init; } = TraceSpanStatus.Ok;

    /// <summary>
    /// The numeric metrics of the span, keyed by an ordinal key (<c>tokens_in</c>, <c>tokens_out</c>,
    /// <c>cost</c>, …). <c>TmTraceWaterfall.MetricColumns</c> decides which keys are visible; the
    /// layout sums every key over each subtree, so a parent can show its own value plus its subtree
    /// total. Default: an empty, shared, read-only map.
    /// </summary>
    public IReadOnlyDictionary<string, double> Metrics { get; init; } = NoMetrics;

    /// <summary>
    /// Free-form string attributes of the span (model name, tool version, HTTP status, …). The
    /// waterfall does not interpret them — they are for the host's detail template and for tooling
    /// that serialises the trace. Default: an empty, shared, read-only map.
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = NoAttributes;

    /// <summary>
    /// The shared empty metrics map. Wrapped in a <see cref="ReadOnlyDictionary{TKey,TValue}"/> (not
    /// merely exposed as <see cref="IReadOnlyDictionary{TKey,TValue}"/> over a mutable
    /// <see cref="Dictionary{TKey,TValue}"/>): a caller can cast an interface back to its implementation,
    /// and since this instance is shared by every span that does not set <see cref="Metrics"/>, a
    /// successful cast would let one caller change the default of all of them. The wrapper throws
    /// <see cref="NotSupportedException"/> instead.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, double> NoMetrics =
        new ReadOnlyDictionary<string, double>(new Dictionary<string, double>(StringComparer.Ordinal));

    /// <summary>
    /// The shared empty attributes map, wrapped for the same reason as <see cref="NoMetrics"/>:
    /// the default is shared by every span, so it must not be reachable as a mutable dictionary.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> NoAttributes =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
}
