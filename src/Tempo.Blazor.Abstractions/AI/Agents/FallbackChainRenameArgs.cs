namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The payload of a committed rename in the fallback chain editor: which model the reader renamed and the new label
/// they typed. The host applies it to its own persistence — the component never writes back to the host's catalogue.
/// </summary>
public sealed record FallbackChainRenameArgs
{
    /// <summary>Creates the rename payload.</summary>
    /// <param name="ModelId">The stable identity of the chain entry that was renamed.</param>
    /// <param name="NewLabel">The label the reader committed. Never empty: an empty draft rolls back instead.</param>
    public FallbackChainRenameArgs(string ModelId, string NewLabel)
    {
        this.ModelId = ModelId;
        this.NewLabel = NewLabel;
    }

    /// <summary>Stable identity of the model in the chain. Matches <see cref="FallbackChainEntry.ModelId"/>.</summary>
    public string ModelId { get; }

    /// <summary>The label the reader committed. The host owns what it means for its own persistence.</summary>
    public string NewLabel { get; }
}
