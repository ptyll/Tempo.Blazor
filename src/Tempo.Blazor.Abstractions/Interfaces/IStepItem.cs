namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a step in TmStepper workflow component.
/// </summary>
public interface IStepItem
{
    /// <summary>Unique identifier for this step.</summary>
    string Id { get; }

    /// <summary>Display label for the step.</summary>
    string Label { get; }

    /// <summary>Optional description shown below the label.</summary>
    string? Description { get; }

    /// <summary>Optional icon name from IconNames constants (overrides step number).</summary>
    string? Icon { get; }
}
