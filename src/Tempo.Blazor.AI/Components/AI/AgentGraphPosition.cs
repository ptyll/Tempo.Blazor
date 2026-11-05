namespace Tempo.Blazor.Components.AI;

/// <summary>
/// A fixed point of an agent graph's canvas, in pixels from the canvas's top-left corner — the type a host uses to pin
/// a node exactly where it wants it.
/// <para>
/// Pinning is the escape hatch for a surface whose node order matters to the reader (a hand-placed architecture sketch,
/// a restored layout): a pinned node keeps its exact coordinate and everything else is still laid out around it, and
/// the canvas grows so the pinned box is never cut off.
/// </para>
/// </summary>
public sealed record AgentGraphPosition
{
    /// <summary>Creates a position.</summary>
    /// <param name="x">Distance in pixels from the canvas's left edge. A negative value is treated as zero.</param>
    /// <param name="y">Distance in pixels from the canvas's top edge. A negative value is treated as zero.</param>
    public AgentGraphPosition(double x, double y)
    {
        X = double.IsFinite(x) && x > 0 ? x : 0;
        Y = double.IsFinite(y) && y > 0 ? y : 0;
    }

    /// <summary>Distance in pixels from the canvas's left edge; never negative.</summary>
    public double X { get; init; }

    /// <summary>Distance in pixels from the canvas's top edge; never negative.</summary>
    public double Y { get; init; }
}
