namespace Tempo.Blazor.Components.Inputs;

/// <summary>A single option in a TmRadioGroup.</summary>
/// <typeparam name="TValue">Value type of the option.</typeparam>
public sealed record RadioOption<TValue>(string Label, TValue Value)
{
    /// <summary>Disables this specific option.</summary>
    public bool IsDisabled { get; init; }

    /// <summary>
    /// BCP 47 language of <see cref="Label"/> when it differs from the surrounding page.
    /// Rendered as <c>lang</c> on the option text; omitted while null or empty.
    /// </summary>
    public string? Lang { get; init; }
}
