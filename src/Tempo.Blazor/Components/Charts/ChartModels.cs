namespace Tempo.Blazor.Components.Charts;

/// <summary>Chart type.</summary>
public enum ChartType
{
    Bar,
    Line,
    Pie,
    Donut,
    HorizontalBar
}

/// <summary>Data for TmChart.</summary>
public sealed record ChartData
{
    /// <summary>Category labels (X-axis for bar/line).</summary>
    public required string[] Labels { get; init; }

    /// <summary>One or more datasets.</summary>
    public required ChartDataset[] Datasets { get; init; }
}

/// <summary>A single dataset within chart data.</summary>
public sealed record ChartDataset
{
    /// <summary>Dataset label (used in legend).</summary>
    public required string Label { get; init; }

    /// <summary>Data values.</summary>
    public required double[] Values { get; init; }

    /// <summary>Stroke/border color.</summary>
    public required string Color { get; init; }

    /// <summary>Fill color (defaults to Color with opacity).</summary>
    public string? BackgroundColor { get; init; }
}

/// <summary>Identifies a clicked segment.</summary>
public sealed record ChartSegment(int DatasetIndex, int Index, string Label, double Value);
