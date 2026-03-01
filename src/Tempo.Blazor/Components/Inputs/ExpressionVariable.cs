namespace Tempo.Blazor.Components.Inputs;

/// <summary>A variable available for insertion into an expression editor.</summary>
public sealed record ExpressionVariable
{
    /// <summary>Variable name (e.g. "sender.name").</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description.</summary>
    public required string Description { get; init; }

    /// <summary>Data type hint (e.g. "string", "DateTime").</summary>
    public string Type { get; init; } = "string";
}
