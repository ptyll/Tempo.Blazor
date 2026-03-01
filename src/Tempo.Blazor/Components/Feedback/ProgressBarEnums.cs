namespace Tempo.Blazor.Components.Feedback;

/// <summary>Size variants for the progress bar.</summary>
public enum ProgressBarSize
{
    Sm,
    Md,
    Lg
}

/// <summary>Color variant for the progress bar.</summary>
public enum ProgressBarVariant
{
    Default,
    Success,
    Warning,
    Error,
    Gradient
}

/// <summary>A single segment in a multi-segment progress bar.</summary>
public sealed record ProgressSegment(double Value, string Color, string? Label = null);
