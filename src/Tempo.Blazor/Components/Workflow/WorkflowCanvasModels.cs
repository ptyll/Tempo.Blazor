namespace Tempo.Blazor.Components.Workflow;

/// <summary>State type in the workflow canvas.</summary>
public enum CanvasStateType
{
    Initial,
    Intermediate,
    Final
}

/// <summary>A state (node) in the workflow canvas.</summary>
public sealed record CanvasState
{
    /// <summary>Unique identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; set; }

    /// <summary>State type (initial, intermediate, final).</summary>
    public CanvasStateType Type { get; set; } = CanvasStateType.Intermediate;

    /// <summary>X position on canvas.</summary>
    public double X { get; set; }

    /// <summary>Y position on canvas.</summary>
    public double Y { get; set; }

    /// <summary>Optional color.</summary>
    public string? Color { get; set; }

    /// <summary>Optional icon identifier.</summary>
    public string? Icon { get; set; }

    /// <summary>Optional system/technical name.</summary>
    public string? SystemName { get; set; }
}

/// <summary>A transition (edge) between two states.</summary>
public sealed record CanvasTransition
{
    /// <summary>Unique identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Source state ID.</summary>
    public required string FromStateId { get; init; }

    /// <summary>Target state ID.</summary>
    public required string ToStateId { get; init; }

    /// <summary>Optional label on the transition.</summary>
    public string? Label { get; set; }

    /// <summary>Optional color.</summary>
    public string? Color { get; set; }
}

/// <summary>Full workflow canvas definition.</summary>
public sealed class WorkflowCanvasDefinition
{
    /// <summary>All states in the workflow.</summary>
    public List<CanvasState> States { get; set; } = [];

    /// <summary>All transitions in the workflow.</summary>
    public List<CanvasTransition> Transitions { get; set; } = [];
}
