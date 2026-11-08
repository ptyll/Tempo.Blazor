using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Tests.Fixtures;

/// <summary>
/// A minimal ArchiMate-like notation profile used to exercise the modeling MCP notation-rule
/// enforcement without referencing the Blazor.Modeling package (which ships the real
/// ArchimateRelationshipMatrix wired into the host at runtime).
/// </summary>
public sealed class TestArchimateProfile : IModelingNotationProfile
{
    public string NotationKey => "archimate";
    public string DisplayName => "ArchiMate (test)";
    public IReadOnlyCollection<string> SupportedElementTypes { get; } =
        ["BusinessActor", "BusinessRole", "ApplicationComponent"];
    public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } =
        ["Serving", "Assignment", "Association"];
    public IReadOnlyCollection<string> SupportedViewpointKeys { get; } = ["Layered"];
}

/// <summary>Test factory for the Abstractions-level notation rule stack.</summary>
public static class FakeModelingNotation
{
    public static IModelingNotationProfileProvider Registry()
        => new ModelingNotationProfileRegistry([new TestArchimateProfile()]);

    public static IModelingRelationshipRulesProvider RelationshipRules()
        => new ModelingRelationshipRulesProvider(Registry());

    /// <summary>
    /// An allow-all relationship rules provider: every type pair is valid. The contract snapshot's
    /// <c>applied</c> batch uses it because the seeded model is <c>bpmn2</c> while this fixture's
    /// registry only ships an ArchiMate profile — the snapshot pins the success RESPONSE SHAPE (the
    /// diff ids/counts an agent reads), not the notation matrix (that is the notation-rule suites' job).
    /// </summary>
    public static IModelingRelationshipRulesProvider AllowAllRules() => new AllowAllRelationshipRules();

    private sealed class AllowAllRelationshipRules : IModelingRelationshipRulesProvider
    {
        public bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType)
            => true;

        public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
            => ModelingRelationshipRuleResult.Valid;
    }
}

/// <summary>A deterministic projector that renders one node per element and one edge per relationship.</summary>
public sealed class FakeModelingDiagramProjector : IModelingDiagramProjector
{
    public ModelingDiagramGenerationResultDto Generate(
        ModelingModelDto model,
        ModelingDiagramGenerationOptionsDto? options = null)
    {
        var page = new DiagramPage { Name = "View" };
        var nodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        double x = 40;
        foreach (var element in model.Elements)
        {
            var node = new DiagramNode { StencilId = "general.rectangle", X = x, Y = 40, W = 120, H = 60 };
            node.Data["label"] = element.Name;
            nodeIds[element.Id] = node.Id;
            page.Nodes.Add(node);
            x += 200;
        }
        foreach (var relationship in model.Relationships)
        {
            if (nodeIds.TryGetValue(relationship.SourceElementId, out var s) &&
                nodeIds.TryGetValue(relationship.TargetElementId, out var t))
            {
                page.Edges.Add(new DiagramEdge { SourceNodeId = s, TargetNodeId = t, Label = relationship.Name });
            }
        }

        var doc = new DiagramDocument { Title = model.Title };
        doc.Pages.Add(page);

        return new ModelingDiagramGenerationResultDto
        {
            Document = doc,
            Issues = [],
            GeneratedAt = DateTimeOffset.UnixEpoch
        };
    }
}
