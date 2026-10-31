using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Serialization;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class DemoModelingModelProviderTests
{
    [Fact]
    public async Task Provider_returns_non_empty_mixed_model()
    {
        var provider = new DemoModelingModelProvider();

        var model = await provider.GetModelAsync(new ModelingModelRequest(), CancellationToken.None);

        model.Elements.Should().HaveCountGreaterThanOrEqualTo(5);
        model.Relationships.Should().HaveCountGreaterThanOrEqualTo(4);
        model.Elements.Select(element => element.Notation).Should().Contain("bpmn");
        model.Elements.Select(element => element.Notation).Should().Contain("archimate");
        model.SupportedNotations.Should().Contain(["bpmn", "archimate"]);
        model.Views.Should().NotBeEmpty();
    }

    [Fact]
    public void Provider_key_is_unique_constant()
    {
        var provider = new DemoModelingModelProvider();
        var field = typeof(DemoModelingModelProvider).GetField(
            nameof(DemoModelingModelProvider.ProviderKeyValue),
            BindingFlags.Public | BindingFlags.Static);

        field.Should().NotBeNull();
        field!.IsLiteral.Should().BeTrue();
        provider.ProviderKey.Should().Be(DemoModelingModelProvider.ProviderKeyValue);
        provider.ProviderKey.Should().Be("tempo.demo.modeling");
    }

    [Fact]
    public async Task Cancelled_token_throws_before_returning_data()
    {
        var provider = new DemoModelingModelProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => provider.GetModelAsync(new ModelingModelRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Provider_returns_elements_with_required_identity_and_name()
    {
        var provider = new DemoModelingModelProvider();

        var model = await provider.GetModelAsync(new ModelingModelRequest(), CancellationToken.None);

        model.Elements.Should().OnlyContain(element => !string.IsNullOrWhiteSpace(element.Id));
        model.Elements.Should().OnlyContain(element => !string.IsNullOrWhiteSpace(element.Name));
    }

    [Fact]
    public void AddTempoBlazorModeling_registers_demo_provider_with_try_add_enumerable()
    {
        var services = new ServiceCollection();

        services.AddTempoBlazorModeling();

        using var provider = services.BuildServiceProvider();
        var providers = provider.GetServices<IModelingModelProvider>().ToList();

        providers.Should().ContainSingle(modelProvider => modelProvider is DemoModelingModelProvider);
        providers.Select(modelProvider => modelProvider.ProviderKey)
            .Should().ContainSingle(DemoModelingModelProvider.ProviderKeyValue);
    }

    [Fact]
    public async Task Demo_model_view_can_be_projected_to_diagram_serializer_roundtrip()
    {
        var provider = new DemoModelingModelProvider();
        var model = await provider.GetModelAsync(new ModelingModelRequest(), CancellationToken.None);

        var document = CreateSmokeDiagramDocument(model);
        var json = DiagramSerializer.Serialize(document);
        var restored = DiagramSerializer.Deserialize(json);

        restored.Nodes.Should().HaveCount(model.Views[0].Nodes.Count);
        restored.Edges.Should().HaveCount(model.Views[0].Connections.Count);
        restored.Nodes.Select(node => node.Id).Should().BeEquivalentTo(model.Views[0].Nodes.Select(node => node.ElementId));
    }

    private static DiagramDocument CreateSmokeDiagramDocument(ModelingModelDto model)
    {
        var view = model.Views[0];
        var elements = model.Elements.ToDictionary(element => element.Id, StringComparer.Ordinal);
        var relationships = model.Relationships.ToDictionary(relationship => relationship.Id, StringComparer.Ordinal);

        var page = new DiagramPage
        {
            Id = view.Id,
            Name = view.Name,
            Layers =
            [
                new DiagramLayer
                {
                    Id = "modeling-demo",
                    Name = "Modeling demo",
                    Order = 0
                }
            ]
        };

        foreach (var viewNode in view.Nodes)
        {
            var element = elements[viewNode.ElementId];
            page.Nodes.Add(new DiagramNode
            {
                Id = viewNode.ElementId,
                StencilId = GetStringProperty(element.Properties, "stencilId") ?? "basic.rectangle",
                X = viewNode.X,
                Y = viewNode.Y,
                W = viewNode.Width,
                H = viewNode.Height,
                LayerId = "modeling-demo",
                Data =
                {
                    ["label"] = element.Name,
                    ["sourceId"] = element.SourceId,
                    ["sourceType"] = element.SourceType
                }
            });
        }

        foreach (var connection in view.Connections)
        {
            var relationship = relationships[connection.RelationshipId];
            page.Edges.Add(new DiagramEdge
            {
                Id = connection.RelationshipId,
                SourceNodeId = connection.SourceNodeId,
                TargetNodeId = connection.TargetNodeId,
                ConnectorType = relationship.RelationshipType,
                Label = relationship.Name,
                LayerId = "modeling-demo"
            });
        }

        return new DiagramDocument
        {
            Id = model.Id,
            Title = model.Title,
            Pages = [page],
            ActivePageIndex = 0
        };
    }

    private static string? GetStringProperty(Dictionary<string, JsonElement> properties, string key)
    {
        return properties.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    // ── Phase 12: ParentId demo data ──────────────────────────────────────────

    [Fact]
    public async Task Default_model_stays_flat()
    {
        // A model without ParentId must look and behave exactly as before (the Auto grouping
        // keeps the SemanticType groups) — the default demo provider must not carry parents.
        var provider = new DemoModelingModelProvider();

        var model = await provider.GetModelAsync(new ModelingModelRequest(), CancellationToken.None);

        model.Elements.Select(element => element.ParentId)
            .Should().OnlyContain(parentId => string.IsNullOrEmpty(parentId));
    }

    [Fact]
    public void Hierarchy_model_parents_the_order_activities_under_the_subprocess()
    {
        var model = DemoModelingModelProvider.CreateHierarchyModel();

        var subprocess = model.Elements.Should().ContainSingle(element => element.Id == "bpmn-subprocess-fulfillment")
            .Subject;
        subprocess.SemanticType.Should().Be("subprocess");
        // Declared FIRST so the container paints below its children in the generated diagram.
        model.Elements[0].Id.Should().Be("bpmn-subprocess-fulfillment");
        model.Elements
            .Where(element => element.Id is "bpmn-start-order" or "bpmn-validate-order" or "bpmn-ship-order")
            .Select(element => element.ParentId)
            .Should().OnlyContain(parentId => parentId == "bpmn-subprocess-fulfillment");

        // The subprocess takes part in the view as a plain node — the diagram nesting is NOT
        // driven by element ParentId (the confirmed phase-12 generator decision).
        model.Views[0].Nodes.Should().Contain(node => node.ElementId == "bpmn-subprocess-fulfillment");
        model.Views[0].Nodes.Should().OnlyContain(node => node.ParentNodeId == null);
    }
}
