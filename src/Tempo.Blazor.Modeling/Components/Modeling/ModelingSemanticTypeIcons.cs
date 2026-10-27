using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Maps a modeling semantic type (the vocabulary emitted by the built-in notation profiles:
/// BPMN tasks/events/gateways, UML classifiers, ArchiMate elements, ERD entities, …) to an
/// <see cref="IconNames"/> icon. The rules key off the semantic type's keyword, so every
/// profile's vocabulary resolves without per-profile tables; a type no rule recognizes falls
/// back to the generic shape icon (MO4 — no DTO change, purely presentational).
/// </summary>
internal static class ModelingSemanticTypeIcons
{
    /// <summary>Fallback icon for semantic types the keyword rules do not recognize.</summary>
    internal const string Fallback = IconNames.Box;

    /// <summary>Resolves the node/group icon for a semantic type. Never returns null or empty.</summary>
    internal static string ForSemanticType(string? semanticType)
    {
        if (string.IsNullOrWhiteSpace(semanticType))
        {
            return Fallback;
        }

        var key = semanticType.Trim();

        // Contains is ordinal-ignore-case; order matters ("userTask" is task-like, not user-like).
        if (ContainsAny(key, "usertask", "task", "process", "activity", "step", "function"))
        {
            return IconNames.Activity;
        }

        if (ContainsAny(key, "entity", "table"))
        {
            return IconNames.Table;
        }

        if (ContainsAny(key, "interface", "component", "service", "node", "object", "class"))
        {
            return IconNames.Box;
        }

        if (ContainsAny(key, "actor", "person", "role", "stakeholder", "user"))
        {
            return IconNames.User;
        }

        if (ContainsAny(key, "usecase", "use case"))
        {
            return IconNames.Circle;
        }

        if (ContainsAny(key, "event", "message", "trigger", "notification"))
        {
            return IconNames.Message;
        }

        if (ContainsAny(key, "gateway", "decision", "condition"))
        {
            return IconNames.GitCompare;
        }

        if (ContainsAny(key, "pool", "lane", "group", "boundary", "frame", "package", "module"))
        {
            return IconNames.Package;
        }

        if (ContainsAny(key, "data", "store", "database"))
        {
            return IconNames.Database;
        }

        if (ContainsAny(key, "flow", "sequence"))
        {
            return IconNames.ArrowRight;
        }

        if (ContainsAny(key, "requirement", "note", "annotation", "comment", "documentation"))
        {
            return IconNames.FileText;
        }

        if (ContainsAny(key, "relationship", "association", "relation", "link", "edge", "reference"))
        {
            return IconNames.Link2;
        }

        return Fallback;
    }

    private static bool ContainsAny(string value, params string[] keywords)
        => keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
