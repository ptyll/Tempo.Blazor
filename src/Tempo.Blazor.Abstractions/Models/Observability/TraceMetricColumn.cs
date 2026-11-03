namespace Tempo.Blazor.Models;

/// <summary>
/// One numeric metric column of a trace waterfall (<c>TmTraceWaterfall.MetricColumns</c>): the key
/// looked up in <see cref="TraceSpan.Metrics"/>, the header a reader sees and the .NET numeric
/// format string the value is rendered with.
/// </summary>
/// <param name="Key">
/// The metric key, matched ORDINALLY against <see cref="TraceSpan.Metrics"/>. A column whose key no
/// span carries renders an empty cell rather than a zero, so "not reported" and "reported as zero"
/// stay distinguishable.
/// </param>
/// <param name="Label">The column header. Required; a blank label falls back to <paramref name="Key"/>.</param>
/// <param name="Format">
/// The .NET numeric format string (<c>"N0"</c>, <c>"0.00"</c>, <c>"P0"</c>, …) applied with the
/// component's culture. <see langword="null"/>/blank means <c>"N0"</c>.
/// </param>
public sealed record TraceMetricColumn(string Key, string Label, string? Format = null);
