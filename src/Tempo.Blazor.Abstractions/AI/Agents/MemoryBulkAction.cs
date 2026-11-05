namespace Tempo.Blazor.AI.Models;

/// <summary>
/// An action the reader applied to a SELECTION of memories at once.
/// <para>
/// A bulk action is ONE request carrying many memories; the surface reports it once through <c>OnBulkAction</c> instead
/// of firing the single-row callback per row, so a host can save, pause or forget the selection in one round-trip.
/// </para>
/// </summary>
public enum MemoryBulkAction
{
    /// <summary>Stop using every selected memory, keeping it stored.</summary>
    Pause = 0,

    /// <summary>Use every selected memory again.</summary>
    Resume = 1,

    /// <summary>Remove every selected memory.</summary>
    Forget = 2,
}
