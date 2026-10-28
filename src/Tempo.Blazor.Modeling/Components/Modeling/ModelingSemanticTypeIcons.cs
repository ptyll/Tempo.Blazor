using System.Collections.Frozen;
using System.Text;
using System.Collections.Concurrent;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Maps a modeling semantic type (the vocabulary emitted by the built-in notation profiles:
/// BPMN tasks/events/gateways, UML classifiers, ArchiMate elements, ERD entities, …) to an
/// <see cref="IconNames"/> icon. Resolution is two-stage and memoized:
/// <list type="number">
/// <item>an exact, ordinal-ignore-case lookup in a frozen dictionary of every BUILT-IN
/// vocabulary type (BPMN / UML / ArchiMate 3.2 / ERD — see <c>BuiltInModelingProfiles.cs</c>),</item>
/// <item>keyword rules matched on the camel/Pascal-case word TOKENS of the type (never
/// substrings — "Precondition" is not "condition", "Framework" is not "frame"), with
/// data/store and relationship/association deliberately ahead of object/class so
/// <c>DataObject</c> resolves to the database icon.</item>
/// </list>
/// A type no rule recognizes falls back to the generic shape icon (MO4 — no DTO change,
/// purely presentational).
/// </summary>
internal static class ModelingSemanticTypeIcons
{
    /// <summary>Fallback icon for semantic types the exact table and the token rules do not recognize.</summary>
    internal const string Fallback = IconNames.Box;

    private static readonly ConcurrentDictionary<string, string> Memo = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Exact icons for the built-in vocabularies. Values mirror what the token rules
    /// would resolve so the table is a fast path, not a second opinion.</summary>
    private static readonly FrozenDictionary<string, string> ExactMatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // BPMN — tasks and subprocesses are activity-like.
        ["Task"] = IconNames.Activity,
        ["UserTask"] = IconNames.Activity,
        ["ServiceTask"] = IconNames.Activity,
        ["SendTask"] = IconNames.Activity,
        ["ReceiveTask"] = IconNames.Activity,
        ["ScriptTask"] = IconNames.Activity,
        ["BusinessRuleTask"] = IconNames.Activity,
        ["ManualTask"] = IconNames.Activity,
        ["SubProcess"] = IconNames.Activity,
        ["EventSubProcess"] = IconNames.Activity,
        ["CallActivity"] = IconNames.Activity,
        // BPMN — events.
        ["StartEvent"] = IconNames.Message,
        ["EndEvent"] = IconNames.Message,
        ["IntermediateCatchEvent"] = IconNames.Message,
        ["IntermediateThrowEvent"] = IconNames.Message,
        ["BoundaryEvent"] = IconNames.Message,
        // BPMN — gateways (the branching icon, even the event-based one).
        ["ExclusiveGateway"] = IconNames.GitCompare,
        ["ParallelGateway"] = IconNames.GitCompare,
        ["InclusiveGateway"] = IconNames.GitCompare,
        ["ComplexGateway"] = IconNames.GitCompare,
        ["EventBasedGateway"] = IconNames.GitCompare,
        // BPMN — grouping, data and annotation.
        ["Pool"] = IconNames.Package,
        ["Lane"] = IconNames.Package,
        ["Group"] = IconNames.Package,
        ["DataObject"] = IconNames.Database,
        ["DataStore"] = IconNames.Database,
        ["TextAnnotation"] = IconNames.FileText,
        // UML.
        ["Class"] = IconNames.Box,
        ["Interface"] = IconNames.Box,
        ["AbstractClass"] = IconNames.Box,
        ["Enumeration"] = IconNames.Box,
        ["Package"] = IconNames.Package,
        ["Component"] = IconNames.Box,
        ["Node"] = IconNames.Box,
        ["Artifact"] = IconNames.Box,
        ["UseCase"] = IconNames.Circle,
        ["Actor"] = IconNames.User,
        ["State"] = IconNames.Box,
        ["PseudoState"] = IconNames.Box,
        ["Activity"] = IconNames.Activity,
        ["Action"] = IconNames.Box,
        ["ObjectNode"] = IconNames.Box,
        ["Lifeline"] = IconNames.Box,
        ["CombinedFragment"] = IconNames.Box,
        ["Collaboration"] = IconNames.Box,
        // ArchiMate 3.2 — behaviour elements are activity-like, events are messages,
        // roles are users, passive structure stays the generic shape.
        ["Capability"] = IconNames.Box,
        ["CourseOfAction"] = IconNames.Activity,
        ["Resource"] = IconNames.Box,
        ["ValueStream"] = IconNames.Activity,
        ["BusinessActor"] = IconNames.User,
        ["BusinessRole"] = IconNames.User,
        ["BusinessCollaboration"] = IconNames.Box,
        ["BusinessInterface"] = IconNames.Box,
        ["BusinessProcess"] = IconNames.Activity,
        ["BusinessFunction"] = IconNames.Activity,
        ["BusinessInteraction"] = IconNames.Box,
        ["BusinessEvent"] = IconNames.Message,
        ["BusinessService"] = IconNames.Box,
        ["BusinessObject"] = IconNames.Box,
        ["Contract"] = IconNames.Box,
        ["Representation"] = IconNames.Box,
        ["Product"] = IconNames.Box,
        ["ApplicationComponent"] = IconNames.Box,
        ["ApplicationCollaboration"] = IconNames.Box,
        ["ApplicationInterface"] = IconNames.Box,
        ["ApplicationFunction"] = IconNames.Activity,
        ["ApplicationInteraction"] = IconNames.Box,
        ["ApplicationProcess"] = IconNames.Activity,
        ["ApplicationEvent"] = IconNames.Message,
        ["ApplicationService"] = IconNames.Box,
        ["Device"] = IconNames.Box,
        ["SystemSoftware"] = IconNames.Box,
        ["TechnologyCollaboration"] = IconNames.Box,
        ["TechnologyInterface"] = IconNames.Box,
        ["Path"] = IconNames.Box,
        ["CommunicationNetwork"] = IconNames.Box,
        ["TechnologyFunction"] = IconNames.Activity,
        ["TechnologyProcess"] = IconNames.Activity,
        ["TechnologyInteraction"] = IconNames.Box,
        ["TechnologyEvent"] = IconNames.Message,
        ["TechnologyService"] = IconNames.Box,
        // (Artifact / Node / DataObject are shared with the UML & BPMN blocks above.)
        ["Equipment"] = IconNames.Box,
        ["Facility"] = IconNames.Box,
        ["DistributionNetwork"] = IconNames.Box,
        ["Material"] = IconNames.Box,
        ["Stakeholder"] = IconNames.User,
        ["Driver"] = IconNames.Box,
        ["Assessment"] = IconNames.Box,
        ["Goal"] = IconNames.Box,
        ["Outcome"] = IconNames.Box,
        ["Principle"] = IconNames.Box,
        ["Requirement"] = IconNames.FileText,
        ["Constraint"] = IconNames.Box,
        ["Meaning"] = IconNames.Box,
        ["Value"] = IconNames.Box,
        ["WorkPackage"] = IconNames.Package,
        ["Deliverable"] = IconNames.Box,
        ["ImplementationEvent"] = IconNames.Message,
        ["Plateau"] = IconNames.Box,
        ["Gap"] = IconNames.Box,
        ["Junction"] = IconNames.Box,
        ["Grouping"] = IconNames.Package,
        ["Location"] = IconNames.Box,
        // ERD.
        ["Entity"] = IconNames.Table,
        ["WeakEntity"] = IconNames.Table,
        ["Attribute"] = IconNames.Box,
        ["MultiValuedAttribute"] = IconNames.Box,
        ["DerivedAttribute"] = IconNames.Box,
        ["KeyAttribute"] = IconNames.Box,
        ["RelationshipSet"] = IconNames.Link2,
        ["WeakRelationshipSet"] = IconNames.Link2,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves the node/group icon for a semantic type. Never returns null or empty.</summary>
    internal static string ForSemanticType(string? semanticType)
    {
        if (string.IsNullOrWhiteSpace(semanticType))
        {
            return Fallback;
        }

        var key = semanticType.Trim();
        return Memo.GetOrAdd(key, static value => Resolve(value));
    }

    private static string Resolve(string key)
    {
        if (ExactMatches.TryGetValue(key, out var exact))
        {
            return exact;
        }

        var tokens = Tokenize(key);
        if (TokensMatchAnyRule(tokens, "gateway", "decision", "condition"))
        {
            return IconNames.GitCompare;
        }

        // Task/process before event so "EventSubProcess" stays activity-like; and data /
        // relationship rules before object/class so "DataObject" is a database, not a box.
        if (TokensMatchAnyRule(tokens, "task", "process", "activity", "step", "function"))
        {
            return IconNames.Activity;
        }

        if (TokensMatchAnyRule(tokens, "event", "message", "notification", "trigger"))
        {
            return IconNames.Message;
        }

        if (TokensMatchAnyRule(tokens, "entity", "table"))
        {
            return IconNames.Table;
        }

        if (TokensMatchAnyRule(tokens, "data", "store", "database", "dataset"))
        {
            return IconNames.Database;
        }

        if (TokensMatchAnyRule(tokens, "relationship", "association", "relation", "link", "edge", "reference"))
        {
            return IconNames.Link2;
        }

        if (TokensMatchAnyRule(tokens, "interface", "component", "service", "node", "object", "class"))
        {
            return IconNames.Box;
        }

        if (TokensMatchAnyRule(tokens, "actor", "person", "role", "user", "stakeholder"))
        {
            return IconNames.User;
        }

        if (TokensMatchAnyRule(tokens, "usecase"))
        {
            return IconNames.Circle;
        }

        if (TokensMatchAnyRule(tokens, "pool", "lane", "group", "grouping", "boundary", "frame", "package", "module"))
        {
            return IconNames.Package;
        }

        if (TokensMatchAnyRule(tokens, "flow", "sequence"))
        {
            return IconNames.ArrowRight;
        }

        if (TokensMatchAnyRule(tokens, "requirement", "note", "annotation", "comment", "documentation"))
        {
            return IconNames.FileText;
        }

        return Fallback;
    }

    /// <summary>Splits a camel/Pascal-case key into lowercase word tokens and also yields the
    /// collapsed form ("UseCase" → "use", "case", "usecase") so multiword keywords match
    /// without substring matching. "MultiValuedAttribute" → "multi", "valued", "attribute";
    /// acronym runs split before the final upper letter ("XMLNode" → "xml", "node").</summary>
    private static IReadOnlyList<string> Tokenize(string key)
    {
        var rawTokens = new List<string>(key.Length);
        var current = new StringBuilder(key.Length);
        for (var index = 0; index < key.Length; index++)
        {
            var character = key[index];
            if (!char.IsAsciiLetterOrDigit(character))
            {
                if (current.Length > 0)
                {
                    rawTokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            if (current.Length > 0)
            {
                var previous = key[index - 1];
                var next = index + 1 < key.Length ? key[index + 1] : default;
                var isBoundary = (char.IsLower(previous) && char.IsUpper(character))
                    || (char.IsUpper(previous) && char.IsUpper(character) && char.IsLower(next));
                if (isBoundary)
                {
                    rawTokens.Add(current.ToString());
                    current.Clear();
                }
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            rawTokens.Add(current.ToString());
        }

        // Add consecutive-pair collapses for phrase keywords ("use case" → "usecase").
        var tokens = new List<string>(rawTokens.Count * 2);
        for (var index = 0; index < rawTokens.Count; index++)
        {
            tokens.Add(rawTokens[index].ToLowerInvariant());
            if (index + 1 < rawTokens.Count)
            {
                tokens.Add(string.Concat(rawTokens[index], rawTokens[index + 1]).ToLowerInvariant());
            }
        }

        return tokens;
    }

    private static bool TokensMatchAnyRule(IReadOnlyList<string> tokens, params string[] keywords)
        => tokens.Any(token => keywords.Contains(token, StringComparer.OrdinalIgnoreCase));
}
