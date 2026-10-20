namespace Tempo.Blazor.Components.Inputs;

/// <summary>How <see cref="TmRadioGroup{T}"/> presents its options.</summary>
public enum RadioAppearance
{
    /// <summary>Radio buttons. The historical appearance.</summary>
    Radio,

    /// <summary>
    /// A single-choice segmented control: one pressed segment at a time. Use it for a value
    /// (Desktop/Tablet/Mobile); use <c>TmTabs Variant=Pill</c> for switching content.
    /// </summary>
    Segmented
}
