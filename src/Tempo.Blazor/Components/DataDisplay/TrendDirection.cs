namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// Direction of a metric's movement, as a trend surface paints it (plan ff043b2e).
/// </summary>
/// <remarks>
/// <para>
/// The direction is the FACT ("did the number go up"), not the verdict ("was that good"). The
/// verdict is <c>PositiveIsGood</c> on <see cref="TmStatCard"/> and decides only the tone — a falling
/// churn rate is <see cref="Down"/> and good news at the same time.
/// </para>
/// <para>
/// <see cref="Auto"/> means "derive it from the numeric delta", which is what a host that only
/// passes a delta wants; the three explicit members exist for a metric whose delta is unknown but
/// whose direction is not.
/// </para>
/// </remarks>
public enum TrendDirection
{
    /// <summary>Derive the direction from the sign of the delta (a positive delta is <see cref="Up"/>).</summary>
    Auto = 0,

    /// <summary>The metric rose.</summary>
    Up = 1,

    /// <summary>The metric fell.</summary>
    Down = 2,

    /// <summary>The metric did not move.</summary>
    Flat = 3
}
