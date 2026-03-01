namespace Tempo.Blazor.Components.Inputs;

/// <summary>A single option in a TmRadioGroup.</summary>
/// <typeparam name="TValue">Value type of the option.</typeparam>
public sealed record RadioOption<TValue>(string Label, TValue Value)
{
    /// <summary>Disables this specific option.</summary>
    public bool IsDisabled { get; init; }
}
