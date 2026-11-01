namespace Tempo.Blazor.Abstractions.Models.Reviews;

/// <summary>
/// The visual weight of a <see cref="ReviewQueueQuickAction"/>.
/// </summary>
public enum ReviewQueueQuickActionVariant
{
    /// <summary>A neutral secondary button. The default.</summary>
    Secondary = 0,

    /// <summary>The affirmative choice of a two-button decision inbox.</summary>
    Primary = 1,

    /// <summary>A destructive choice — the button takes the danger colour.</summary>
    Danger = 2,
}
