using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 3 / decision <c>MODELING-AUTHORING-CHANGESET</c> §9.1/§9.2: the SEMANTIC authoring surface of
/// <see cref="TmModelingEditor"/> — a palette insert becomes a model element plus its view node (never
/// a silent diagram node), a drawn or dialog-built relationship is validated by the notation rules
/// BEFORE it is committed, a rejected combination shows the rules' own explanation, a cascade delete
/// expands descendants and incident relationships behind a confirmation, and every path is ONE
/// undoable step. The non-drag alternatives (Insert element / Connect to / Delete) cover WCAG 2.5.7.
/// </summary>
public sealed class TmModelingEditorAuthoringTests : LocalizationTestBase
{
    private readonly ModelingEditorTestClock _clock = new();

    public TmModelingEditorAuthoringTests()
    {
        Services.AddTempoBlazorModeling();
        Services.AddSingleton<TimeProvider>(_clock);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new AuthoringModelingModelProvider()));
    }

    // ── Palette insert ──────────────────────────────────────────────────────

    [Fact]
    public async Task Palette_insert_creates_the_semantic_element_and_its_view_node()
    {
        var writer = AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        var laneNode = cut.Instance.TestDocument!.Nodes.Single(node => string.Equals(NodeElementId(node), "lane-sales", StringComparison.Ordinal));
        var idsBefore = cut.Instance.TestModel!.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        await PaletteInsertAsync(cut, "palette-node-1", "bpmn2.task.user", laneNode.X + 40, laneNode.Y + 30);

        var added = cut.Instance.TestModel!.Elements.Single(element => !idsBefore.Contains(element.Id));
        added.SemanticType.Should().Be("UserTask", "the palette stencil maps back to its semantic type");
        added.Name.Should().Contain("(new)",
            "the default name is the localized type label plus the (new) marker — phase 3 review U5");
        added.ParentId.Should().Be("lane-sales", "the container the node was dropped into becomes the ParentId");

        // The view node holds the drop geometry and the parent ELEMENT id (never a diagram node id).
        var viewNode = cut.Instance.TestModel.Views[0].Nodes.Single(node => node.ElementId == added.Id);
        viewNode.ParentNodeId.Should().Be("lane-sales");

        // The diagram was REBUILT from the model: the raw palette node is gone, the generated node carries the id.
        cut.Instance.TestDocument!.Nodes.Should().NotContain(node => node.Id == "palette-node-1");
        cut.Instance.TestDocument.Nodes.Should().Contain(node => NodeElementId(node) == added.Id);

        // One undoable step: the pending set carries AddedElements + the matching view change.
        var pending = cut.Instance.TestPendingChangeSet!;
        pending.AddedElements.Should().ContainSingle(entry => entry.Id == added.Id);
        pending.ViewChanges.Should().ContainSingle(view => view.Nodes.Any(node => node.ElementId == added.Id));

        // The tree (and the inspector) react in the same step.
        cut.FindAll("[data-testid='modeling-tree-node-" + added.Id + "']").Should().HaveCount(1,
            "the model tree renders the inserted element immediately");

        await cut.InvokeAsync(() => cut.Instance.SaveModelChangesAsync());
        writer.Calls.Should().ContainSingle();
        writer.Calls[0].AddedElements.Should().ContainSingle(entry => entry.Id == added.Id);
        writer.Calls[0].ViewChanges.Should().ContainSingle(view => view.Nodes.Any(node => node.ElementId == added.Id));
    }

    [Fact]
    public async Task Palette_insert_without_a_notation_shape_is_rejected_and_the_raw_node_is_dropped()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");
        var elementCount = cut.Instance.TestModel!.Elements.Count;

        await PaletteInsertAsync(cut, "palette-node-2", "general.rectangle", 350, 120);

        cut.Instance.TestModel!.Elements.Should().HaveCount(elementCount, "an unmapped shape must never create a model element");
        cut.Instance.TestDocument!.Nodes.Should().NotContain(node => node.Id == "palette-node-2");
        cut.Instance.TestStructureErrorText.Should().NotBeNull();
        cut.Instance.TestStructureAnnouncement.Should().NotBeNull();
    }

    [Fact]
    public async Task Palette_insert_is_hidden_without_the_structure_capability()
    {
        Services.AddSingleton<IModelingModelWriter>(
            new AuthoringModelingModelWriter(AuthoringModelingModelProvider.Key, ModelingModelWriterCapabilities.Content));
        using var cut = RenderEditor();

        cut.FindAll("[data-testid='modeling-insert-element']").Should().BeEmpty("a content-only writer cannot persist structure");
        cut.Find("[data-testid='modeling-capability-hint']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-editor']").GetAttribute("data-can-edit-structure").Should().Be("false");
    }

    // ── Non-drag insert ─────────────────────────────────────────────────────

    [Fact]
    public async Task Non_drag_insert_creates_the_element_in_the_chosen_container()
    {
        AddStructureWriter();
        using var cut = RenderEditor();

        await cut.InvokeAsync(() => cut.Instance.OpenInsertElementDialogAsync());
        cut.Find("[data-testid='modeling-insert-element-dialog']").Should().NotBeNull();

        await cut.InvokeAsync(() => cut.Instance.ConfirmInsertElementAsync());

        var inserted = cut.Instance.TestModel!.Elements.Single(element => element.SemanticType == cut.Instance.InsertTypeOptions[0].SemanticType);
        inserted.Id.Should().NotBeNullOrWhiteSpace();
        cut.Instance.TestPendingChangeSet!.AddedElements.Should().ContainSingle(entry => entry.Id == inserted.Id);
        cut.Instance.TestStructureAnnouncement.Should().NotBeNull("the insert is announced in the live region");
        cut.Find("[data-testid='modeling-structure-live']").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Non_drag_insert_uses_the_selected_container()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "lane-sales");

        await cut.InvokeAsync(() => cut.Instance.OpenInsertElementDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.ConfirmInsertElementAsync());

        var inserted = cut.Instance.TestModel!.Elements.Single(element => element.Id != "pool-pool" && element.SemanticType == cut.Instance.InsertTypeOptions[0].SemanticType);
        inserted.ParentId.Should().Be("lane-sales", "a container selection preselects that container as the parent");
    }

    // ── Relationship drawing ────────────────────────────────────────────────

    [Fact]
    public async Task Drawn_relationship_offers_only_the_rules_allowed_types_and_commits_the_pick()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        // Two tasks in DIFFERENT pools: SequenceFlow is not offered (it cannot cross a pool), while
        // MessageFlow and Association are.
        await DrawEdgeAsync(cut, "drawn-edge-2", NodeIdForElement(cut, "task-a"), NodeIdForElement(cut, "task-b"));

        cut.Instance.RelationshipDialogOpen.Should().BeTrue();
        cut.Instance.TestPendingRelationshipTypes.Should().Contain("MessageFlow").And.Contain("Association");
        cut.Instance.TestPendingRelationshipTypes.Should().NotContain("SequenceFlow");

        await cut.InvokeAsync(() => cut.Instance.ConfirmConnectAsync());

        cut.Instance.TestModel!.Relationships.Should().Contain(item => item.SourceElementId == "task-a"
            && item.TargetElementId == "task-b"
            && item.RelationshipType == "MessageFlow");
    }

    [Fact]
    public async Task Drawn_sequence_flow_across_pools_is_refused_with_the_rules_explanation()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");
        var relationshipCount = cut.Instance.TestModel!.Relationships.Count;

        await DrawEdgeAsync(cut, "drawn-edge-3", NodeIdForElement(cut, "task-a"), NodeIdForElement(cut, "task-b"), "sequenceFlow");

        cut.Instance.TestStructureErrorText.Should().NotBeNull();
        cut.Instance.TestStructureErrorText.Should().Contain("cross Pool",
            "the refusal carries the notation rules provider's own explanation");
        cut.Instance.TestStructureErrorTitle.Should().NotBeNull();
        cut.Find("[data-testid='modeling-structure-error']").Should().NotBeNull();
        cut.Instance.TestModel!.Relationships.Should().HaveCount(relationshipCount,
            "the implied SequenceFlow never reaches the model");

        // Cancelling the picker drops the drawn edge from the projected document.
        await cut.InvokeAsync(() => cut.Instance.CloseRelationshipDialogAsync());
        cut.Instance.TestDocument!.Edges.Should().NotContain(edge => edge.Id == "drawn-edge-3");
        cut.Instance.TestModel!.Relationships.Should().HaveCount(relationshipCount);
    }

    [Fact]
    public async Task Pair_with_no_allowed_relationship_is_refused_outright()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "ai-1");
        var relationshipCount = cut.Instance.TestModel!.Relationships.Count;

        // An element type the BPMN rules do not know: no relationship type is valid for the pair.
        await DrawEdgeAsync(cut, "drawn-edge-4", NodeIdForElement(cut, "ai-1"), NodeIdForElement(cut, "ai-2"));

        cut.Instance.RelationshipDialogOpen.Should().BeFalse();
        cut.Instance.TestPendingRelationshipTypes.Should().BeEmpty();
        cut.Instance.TestModel!.Relationships.Should().HaveCount(relationshipCount);
        cut.Instance.TestDocument!.Edges.Should().NotContain(edge => edge.Id == "drawn-edge-4");
        cut.Instance.TestStructureErrorText.Should().NotBeNull();
    }

    [Fact]
    public async Task Connect_to_keyboard_path_creates_the_relationship()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        await cut.InvokeAsync(() => cut.Instance.OpenConnectDialogAsync());
        cut.Find("[data-testid='modeling-relationship-dialog']").Should().NotBeNull();

        await cut.InvokeAsync(() => cut.Instance.ConfirmConnectAsync());

        cut.Instance.TestModel!.Relationships.Should().Contain(item => item.SourceElementId == "task-a"
            && item.TargetElementId == cut.Instance.ConnectTargetOptions[0].Id);
        cut.Instance.TestStructureAnnouncement.Should().NotBeNull();
    }

    [Fact]
    public async Task Connect_to_reports_the_rejection_inline_for_a_disallowed_pair()
    {
        AddStructureWriter();
        using var cut = RenderEditor();

        // An element type the BPMN rules do not know: no relationship type is valid for the pair.
        await SelectElementAsync(cut, "ai-1");
        await cut.InvokeAsync(() => cut.Instance.OpenConnectDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.SelectConnectTargetForTestAsync("ai-2"));

        cut.Instance.ConnectRejection.Should().NotBeNull("no relationship type is allowed for the pair");
        cut.Instance.ConnectTypeOptions.Should().BeEmpty();
        cut.Find("[data-testid='modeling-relationship-rejection']").Should().NotBeNull();
    }

    [Fact]
    public async Task Multi_notation_model_validates_against_the_editors_effective_notation()
    {
        // The host model SUPPORTS several notations ("mixed") and the editor renders it as bpmn2.
        // The rules picker and the engine must judge the SAME notation — otherwise the picker offers
        // a connection the engine then refuses (the editor/MCP parity rule).
        AddStructureWriter();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new MixedNotationAuthoringModelProvider()));
        using var cut = Render<TmModelingEditor>(builder =>
        {
            builder.Add(component => component.ProviderKey, MixedNotationAuthoringModelProvider.Key);
            builder.Add(component => component.NotationKey, "bpmn2");
            builder.Add(component => component.AllowModelEditing, true);
            builder.Add(component => component.WriterCapabilities, ModelingModelWriterCapabilities.All);
        });

        await SelectElementAsync(cut, "task-a");
        await cut.InvokeAsync(() => cut.Instance.OpenConnectDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.SelectConnectTargetForTestAsync("task-b"));
        cut.Instance.ConnectTypeOptions.Should().Contain("MessageFlow",
            "the BPMN rules apply because the EDITOR renders bpmn2, not the model's 'mixed'");

        await cut.InvokeAsync(() => cut.Instance.ConfirmConnectAsync());

        cut.Instance.TestStructureErrorText.Should().BeNull("the engine validates under the same notation");
        cut.Instance.TestModel!.Relationships.Should().Contain(item => item.SourceElementId == "task-a"
            && item.TargetElementId == "task-b");
    }

    // ── Cascade delete ──────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_expands_the_cascade_asks_for_confirmation_and_undo_restores_everything()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "lane-sales");

        await cut.InvokeAsync(() => cut.Instance.RequestDeleteSelectedAsync());

        cut.Instance.DeleteConfirmationOpen.Should().BeTrue();
        cut.Instance.PendingDeleteElementCount.Should().Be(3, "the lane, its task and the task's end event");
        cut.Instance.PendingDeleteRelationshipCount.Should().Be(1, "the flow inside the lane");

        await cut.InvokeAsync(() => cut.Instance.ConfirmDeleteAsync());

        cut.Instance.TestModel!.Elements.Should().NotContain(element => element.Id == "lane-sales");
        cut.Instance.TestModel.Elements.Should().NotContain(element => element.Id == "task-a");
        cut.Instance.TestModel.Relationships.Should().BeEmpty();
        cut.Instance.TestModel.Views[0].Nodes.Should().NotContain(node => node.ElementId == "lane-sales");
        cut.Instance.TestPendingChangeSet!.RemovedElementIds.Should().Contain(["lane-sales", "task-a"]);
        cut.Instance.TestPendingChangeSet.RemovedRelationshipIds.Should().ContainSingle();
        cut.Instance.TestStructureAnnouncement.Should().NotBeNull();

        // One undo restores the whole cascade (elements, relationships, view nodes).
        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.TestModel!.Elements.Should().Contain(element => element.Id == "lane-sales");
        cut.Instance.TestModel.Elements.Should().Contain(element => element.Id == "task-a");
        cut.Instance.TestModel.Relationships.Should().ContainSingle();
        cut.Instance.TestModel.Views[0].Nodes.Should().Contain(node => node.ElementId == "lane-sales");
        cut.Instance.HasPendingModelChanges.Should().BeFalse("undo restored the loaded baseline");
    }

    [Fact]
    public async Task Delete_confirmation_can_be_cancelled_and_keeps_the_model()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        await cut.InvokeAsync(() => cut.Instance.RequestDeleteSelectedAsync());
        cut.Instance.DeleteConfirmationOpen.Should().BeTrue();
        await cut.InvokeAsync(() => cut.Instance.CancelDelete());

        cut.Instance.TestModel!.Elements.Should().Contain(element => element.Id == "task-a");
        cut.Instance.DeleteConfirmationOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Node_deleted_in_the_diagram_becomes_a_confirmed_semantic_delete()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        // The embedded editor removed the node from its document (the Delete key / context menu).
        await cut.InvokeAsync(async () =>
        {
            var document = cut.Instance.TestDocument!;
            var node = document.Nodes.Single(candidate => NodeElementId(candidate) == "task-a");
            document.Nodes.Remove(node);
            await cut.Instance.SyncStructureFromDocumentAsync(document);
        });

        cut.Instance.DeleteConfirmationOpen.Should().BeTrue("the diagram gesture asks before it deletes semantics");
        await cut.InvokeAsync(() => cut.Instance.ConfirmDeleteAsync());

        cut.Instance.TestModel!.Elements.Should().NotContain(element => element.Id == "task-a");
        cut.Instance.TestDocument!.Nodes.Should().NotContain(node => NodeElementId(node) == "task-a");
    }

    [Fact]
    public async Task Tree_and_inspector_react_to_a_removed_element()
    {
        AddStructureWriter();
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-b");

        await cut.InvokeAsync(() => cut.Instance.RequestDeleteSelectedAsync());
        await cut.InvokeAsync(() => cut.Instance.ConfirmDeleteAsync());

        cut.FindAll("[data-testid='modeling-tree-node-task-b']").Should().BeEmpty();
        cut.Find("[data-testid='modeling-inspector']").TextContent.Should().NotContain("Ship order");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private AuthoringModelingModelWriter AddStructureWriter()
    {
        var writer = new AuthoringModelingModelWriter(AuthoringModelingModelProvider.Key, ModelingModelWriterCapabilities.All);
        Services.AddSingleton<IModelingModelWriter>(writer);
        return writer;
    }

    private IRenderedComponent<TmModelingEditor> RenderEditor()
        => Render<TmModelingEditor>(builder =>
        {
            builder.Add(component => component.ProviderKey, AuthoringModelingModelProvider.Key);
            builder.Add(component => component.AllowModelEditing, true);
            builder.Add(component => component.WriterCapabilities, ModelingModelWriterCapabilities.All);
            builder.Add(component => component.OnModelChanged,
                EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { }));
        });

    private static async Task SelectElementAsync(IRenderedComponent<TmModelingEditor> cut, string elementId)
    {
        await cut.InvokeAsync(() => cut.Instance.TestSelectElementForTestAsync(elementId));
        cut.WaitForAssertion(() => cut.Instance.TestSelectedElementId.Should().Be(elementId));
    }

    private static string NodeIdForElement(IRenderedComponent<TmModelingEditor> cut, string elementId)
        => cut.Instance.TestDocument!.Nodes.Single(node => NodeElementId(node) == elementId).Id;

    private static string? NodeElementId(DiagramNode node)
        => node.Data.TryGetValue("modelElementId", out var value) ? value?.ToString() : null;

    private static async Task PaletteInsertAsync(
        IRenderedComponent<TmModelingEditor> cut,
        string nodeId,
        string stencilId,
        double x,
        double y)
    {
        await cut.InvokeAsync(async () =>
        {
            var document = cut.Instance.TestDocument!;
            document.Nodes.Add(new DiagramNode
            {
                Id = nodeId,
                StencilId = stencilId,
                X = x,
                Y = y,
                W = 160,
                H = 88,
                Data = new Dictionary<string, object>(StringComparer.Ordinal) { ["label"] = "Palette shape" }
            });
            await cut.Instance.SyncStructureFromDocumentAsync(document);
        });
    }

    private static async Task DrawEdgeAsync(
        IRenderedComponent<TmModelingEditor> cut,
        string edgeId,
        string sourceNodeId,
        string targetNodeId,
        string connectorType = "sequenceFlow")
    {
        await cut.InvokeAsync(async () =>
        {
            var document = cut.Instance.TestDocument!;
            document.Edges.Add(new DiagramEdge
            {
                Id = edgeId,
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
                ConnectorType = connectorType,
                Label = "Drawn flow"
            });
            await cut.Instance.SyncStructureFromDocumentAsync(document);
        });
    }
}

/// <summary>
/// Phase 3 test model: two BPMN pools (so a SequenceFlow across pools is a rules violation and a
/// MessageFlow is the only allowed type), one lane inside the first pool carrying a task and an end
/// event with one flow between them — everything the cascade delete and the relationship rules need.
/// </summary>
internal sealed class AuthoringModelingModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.authoring";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var model = new ModelingModelDto
        {
            Id = "modeling-editor-authoring",
            Title = "Modeling editor authoring",
            Notation = "bpmn2",
            SupportedNotations = ["bpmn2"],
            Metadata = new ModelingMetadataDto
            {
                SourceSystem = "Unit tests",
                SourceVersion = "2026.07",
                IsFresh = true
            },
            Elements =
            [
                new() { Id = "pool-pool", Notation = "bpmn2", SemanticType = "Pool", Name = "Order pool" },
                new() { Id = "lane-sales", Notation = "bpmn2", SemanticType = "Lane", Name = "Sales", ParentId = "pool-pool" },
                new() { Id = "task-a", Notation = "bpmn2", SemanticType = "UserTask", Name = "Approve request", ParentId = "lane-sales" },
                new() { Id = "end-a", Notation = "bpmn2", SemanticType = "EndEvent", Name = "Approved", ParentId = "lane-sales" },
                new() { Id = "pool-audit", Notation = "bpmn2", SemanticType = "Pool", Name = "Audit pool" },
                new() { Id = "task-b", Notation = "bpmn2", SemanticType = "UserTask", Name = "Ship order", ParentId = "pool-audit" },
                new() { Id = "ai-1", Notation = "bpmn2", SemanticType = "AiTask", Name = "AI classify order" },
                new() { Id = "ai-2", Notation = "bpmn2", SemanticType = "AiTask", Name = "AI score order" }
            ]
        };

        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "flow-approve-end",
            SourceElementId = "task-a",
            TargetElementId = "end-a",
            RelationshipType = "SequenceFlow",
            Name = "Approved"
        });

        model.Views.Add(new ModelingViewDto
        {
            Id = "authoring-view",
            Name = "Authoring view",
            Notation = "bpmn2",
            Nodes =
            [
                new() { ElementId = "pool-pool", X = 40, Y = 40, Width = 520, Height = 260 },
                new() { ElementId = "lane-sales", X = 70, Y = 70, Width = 460, Height = 200, ParentNodeId = "pool-pool" },
                new() { ElementId = "task-a", X = 110, Y = 120, Width = 160, Height = 88, ParentNodeId = "lane-sales" },
                new() { ElementId = "end-a", X = 330, Y = 130, Width = 64, Height = 64, ParentNodeId = "lane-sales" },
                new() { ElementId = "pool-audit", X = 40, Y = 360, Width = 520, Height = 200 },
                new() { ElementId = "task-b", X = 110, Y = 410, Width = 160, Height = 88, ParentNodeId = "pool-audit" },
                new() { ElementId = "ai-1", X = 640, Y = 120, Width = 160, Height = 88 },
                new() { ElementId = "ai-2", X = 640, Y = 260, Width = 160, Height = 88 }
            ],
            Connections =
            [
                new() { RelationshipId = "flow-approve-end", SourceNodeId = "task-a", TargetNodeId = "end-a" }
            ]
        });

        return Task.FromResult(model);
    }
}

/// <summary>
/// The same authoring model, but the model itself SUPPORTS several notations ("mixed") — the host
/// scenario where the editor's notation override and the model's own notation disagree.
/// </summary>
internal sealed class MixedNotationAuthoringModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.authoring.mixed";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        var model = new AuthoringModelingModelProvider()
            .GetModelAsync(request, cancellationToken)
            .GetAwaiter()
            .GetResult();
        model.Notation = "mixed";
        model.SupportedNotations = ["bpmn2", "archimate"];
        return Task.FromResult(model);
    }
}

/// <summary>A writer advertising the given capabilities that records every change set.</summary>
internal sealed class AuthoringModelingModelWriter(string providerKey, ModelingModelWriterCapabilities capabilities)
    : IModelingModelWriter
{
    public string ProviderKey { get; } = providerKey;

    public ModelingModelWriterCapabilities Capabilities { get; } = capabilities;

    public List<ModelingModelChangeSet> Calls { get; } = [];

    public Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken)
    {
        Calls.Add(changes);
        return Task.FromResult(new ModelingModelSaveResult { Succeeded = true });
    }
}
