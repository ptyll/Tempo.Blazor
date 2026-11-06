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
/// Phase 3 review — the STRUCTURE contracts (A1–A4, A7, A9, A11, A12): a viewless model is seeded in
/// the same undo step as its first insert, the picker and the engine judge the same viewpoint, a page
/// switch and a duplicated node are never mistaken for deletions, the cascade delete touches every
/// view and restores original ordering on undo, and an insert into a container respects the
/// container's bounds.
/// </summary>
public sealed class TmModelingEditorStructureReviewTests : LocalizationTestBase
{
    private readonly ModelingEditorTestClock _clock = new();

    public TmModelingEditorStructureReviewTests()
    {
        Services.AddTempoBlazorModeling();
        Services.AddSingleton<TimeProvider>(_clock);
    }

    // ── A1: viewless model ──────────────────────────────────────────────────

    [Fact]
    public async Task Viewless_model_insert_seeds_the_default_view_in_the_same_undo_step()
    {
        RegisterProvider(new ViewlessAuthoringModelProvider());
        var writer = AddStructureWriter(ViewlessAuthoringModelProvider.Key);
        using var cut = RenderEditor(ViewlessAuthoringModelProvider.Key);

        cut.Instance.TestModel!.Views.Should().BeEmpty("the fixture really starts without a view");
        cut.Instance.TestDocument!.Nodes.Should().HaveCount(2);
        cut.Instance.TestDocument.Edges.Should().HaveCount(1);

        await PaletteInsertAsync(cut, "palette-node-viewless", "bpmn2.task.user", 420, 140);

        // The seed created the "default" view with the rendered scene AND the new element.
        var view = cut.Instance.TestModel.Views.Should().ContainSingle().Subject;
        view.Id.Should().Be("default");
        view.Nodes.Should().HaveCount(3, "two pre-existing rendered nodes plus the inserted one");
        view.Connections.Should().ContainSingle(connection => connection.RelationshipId == "flow-ab");

        // The intention carries the COMPLETE seeded scene plus the insert (one writer payload).
        var viewChanges = cut.Instance.TestPendingChangeSet!.ViewChanges;
        viewChanges.Should().ContainSingle();
        viewChanges[0].ViewId.Should().Be("default");
        viewChanges[0].Nodes.Select(node => node.ElementId).Should().BeEquivalentTo(["task-a", "task-b", ArgumentMatcherForInserted(cut)]);
        viewChanges[0].Connections.Should().ContainSingle(connection => connection.RelationshipId == "flow-ab");

        cut.Instance.TestDocument!.Nodes.Should().HaveCount(3);
        cut.Instance.TestDocument.Edges.Should().HaveCount(1);

        // ONE undo removes the insert AND the view the seed created.
        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.TestModel!.Views.Should().BeEmpty("the seeded view belongs to the undo step");
        cut.Instance.TestModel.Elements.Should().HaveCount(2);
        cut.Instance.TestModel.Relationships.Should().ContainSingle();
        cut.Instance.TestDocument!.Nodes.Should().HaveCount(2, "the preview is back to its pre-insert projection");
        cut.Instance.TestDocument.Edges.Should().HaveCount(1);
        cut.Instance.HasPendingModelChanges.Should().BeFalse();
        writer.Calls.Should().BeEmpty("nothing was saved — undo happened before the manual save");
    }

    [Fact]
    public async Task Viewless_model_connect_seeds_the_view_too()
    {
        RegisterProvider(new ViewlessAuthoringModelProvider());
        AddStructureWriter(ViewlessAuthoringModelProvider.Key);
        using var cut = RenderEditor(ViewlessAuthoringModelProvider.Key);

        await SelectElementAsync(cut, "task-a");
        await cut.InvokeAsync(() => cut.Instance.OpenConnectDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.SelectConnectTargetForTestAsync("task-b"));
        await cut.InvokeAsync(() => cut.Instance.ConfirmConnectAsync());

        cut.Instance.TestModel!.Views.Should().ContainSingle(view => view.Id == "default");
        cut.Instance.TestModel.Views[0].Connections.Should().HaveCount(2, "the seeded connection plus the new one");

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.TestModel.Views.Should().BeEmpty();
    }

    // ── A2: viewpoint parity ────────────────────────────────────────────────

    [Fact]
    public async Task Uml_include_is_offered_under_the_use_case_viewpoint_and_commits()
    {
        RegisterProvider(new UmlUseCaseModelProvider());
        AddStructureWriter(UmlUseCaseModelProvider.Key);
        using var cut = RenderEditor(UmlUseCaseModelProvider.Key, editor => editor
            .Add(p => p.ViewpointKey, "UseCaseDiagram"));

        await SelectElementAsync(cut, "uc-a");
        await cut.InvokeAsync(() => cut.Instance.OpenConnectDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.SelectConnectTargetForTestAsync("uc-b"));

        cut.Instance.ConnectTypeOptions.Should().Contain("Include",
            "the picker and the engine share the editor's ViewpointKey (A2)");

        await cut.InvokeAsync(() => cut.Instance.SelectConnectTypeForTestAsync("Include"));
        await cut.InvokeAsync(() => cut.Instance.ConfirmConnectAsync());

        cut.Instance.TestStructureErrorText.Should().BeNull("Include is legal in UseCaseDiagram");
        cut.Instance.TestModel!.Relationships.Should().Contain(relationship =>
            relationship.RelationshipType == "Include"
            && relationship.SourceElementId == "uc-a"
            && relationship.TargetElementId == "uc-b");
    }

    [Fact]
    public async Task Uml_include_is_refused_without_the_use_case_viewpoint()
    {
        RegisterProvider(new UmlUseCaseModelProvider());
        AddStructureWriter(UmlUseCaseModelProvider.Key);
        using var cut = RenderEditor(UmlUseCaseModelProvider.Key);

        await SelectElementAsync(cut, "uc-a");
        await cut.InvokeAsync(() => cut.Instance.OpenConnectDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.SelectConnectTargetForTestAsync("uc-b"));

        cut.Instance.ConnectTypeOptions.Should().NotContain("Include",
            "the same rules refuse Include outside UseCaseDiagram — the picker mirrors the engine");
    }

    // ── A3: page switch / dialog page tabs ──────────────────────────────────

    [Fact]
    public async Task Page_switch_inside_the_editor_is_navigation_not_a_delete()
    {
        RegisterProvider(new AuthoringModelingModelProvider());
        AddStructureWriter(AuthoringModelingModelProvider.Key);
        using var cut = RenderEditor(AuthoringModelingModelProvider.Key);
        var elementCount = cut.Instance.TestModel!.Elements.Count;

        await cut.InvokeAsync(async () =>
        {
            var document = cut.Instance.TestDocument!;
            document.Pages.Add(new DiagramPage { Id = "page-two", Name = "Second page" });
            document.ActivePageIndex = 1;
            await cut.Instance.SyncStructureFromDocumentAsync(document);
        });

        cut.Instance.DeleteConfirmationOpen.Should().BeFalse(
            "a page switch must not read the other page's nodes as deletions (A3)");
        cut.Instance.TestModel!.Elements.Should().HaveCount(elementCount);
        cut.Instance.TestStructureErrorText.Should().BeNull();
    }

    [Fact]
    public async Task Dialog_hides_page_tabs_while_structure_editing_is_enabled()
    {
        RegisterProvider(new AuthoringModelingModelProvider());
        AddStructureWriter(AuthoringModelingModelProvider.Key);
        using var cut = RenderEditor(AuthoringModelingModelProvider.Key);

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor__page-tabs").Should().BeEmpty(
            "ShowPageTabs=!CanEditModelStructure hides the page gesture in an authoring dialog");
    }

    [Fact]
    public async Task Dialog_keeps_page_tabs_for_a_content_only_preview()
    {
        RegisterProvider(new AuthoringModelingModelProvider());
        Services.AddSingleton<IModelingModelWriter>(
            new AuthoringModelingModelWriter(AuthoringModelingModelProvider.Key, ModelingModelWriterCapabilities.Content));
        using var cut = RenderEditor(AuthoringModelingModelProvider.Key);

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor__page-tabs").Should().NotBeEmpty(
            "a read-only preview keeps the page navigation");
    }

    // ── A4: duplicated modelElementId ───────────────────────────────────────

    [Fact]
    public async Task Duplicate_node_deleted_from_the_document_keeps_the_model_element()
    {
        RegisterProvider(new AuthoringModelingModelProvider());
        AddStructureWriter(AuthoringModelingModelProvider.Key);
        using var cut = RenderEditor(AuthoringModelingModelProvider.Key);
        var elementCount = cut.Instance.TestModel!.Elements.Count;

        await cut.InvokeAsync(async () =>
        {
            var document = cut.Instance.TestDocument!;
            var original = document.Nodes.Single(node => NodeElementId(node) == "task-a");
            // The generator supports a model element rendered by two nodes (a duplicated placement);
            // the SECOND node carries the SAME modelElementId.
            document.Nodes.Add(new DiagramNode
            {
                Id = "duplicate-task-a",
                StencilId = original.StencilId,
                X = original.X + 260,
                Y = original.Y,
                W = original.W,
                H = original.H,
                Data = new Dictionary<string, object>(original.Data, StringComparer.Ordinal)
            });
            await cut.Instance.SyncStructureFromDocumentAsync(document);

            var duplicate = document.Nodes.Single(node => node.Id == "duplicate-task-a");
            document.Nodes.Remove(duplicate);
            await cut.Instance.SyncStructureFromDocumentAsync(document);
        });

        cut.Instance.DeleteConfirmationOpen.Should().BeFalse(
            "the live node still carries task-a — deleting one placement is not deleting the element (A4)");
        cut.Instance.TestModel!.Elements.Should().HaveCount(elementCount);
    }

    // ── A7: lossless delete undo ordering ───────────────────────────────────

    [Fact]
    public async Task Delete_undo_restores_original_element_relationship_and_node_order()
    {
        RegisterProvider(new AuthoringModelingModelProvider());
        AddStructureWriter(AuthoringModelingModelProvider.Key);
        using var cut = RenderEditor(AuthoringModelingModelProvider.Key);

        var elementsBefore = cut.Instance.TestModel!.Elements.Select(element => element.Id).ToList();
        var relationshipsBefore = cut.Instance.TestModel.Relationships.Select(relationship => relationship.Id).ToList();
        var nodesBefore = cut.Instance.TestModel.Views[0].Nodes.Select(node => node.ElementId).ToList();

        await SelectElementAsync(cut, "lane-sales");
        await cut.InvokeAsync(() => cut.Instance.RequestDeleteSelectedAsync());
        await cut.InvokeAsync(() => cut.Instance.ConfirmDeleteAsync());

        cut.Instance.TestModel!.Elements.Should().NotContain(element => element.Id == "lane-sales");

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());

        cut.Instance.TestModel!.Elements.Select(element => element.Id).Should().Equal(elementsBefore,
            "undo re-inserts at the captured index (A7)");
        cut.Instance.TestModel.Relationships.Select(relationship => relationship.Id).Should().Equal(relationshipsBefore);
        cut.Instance.TestModel.Views[0].Nodes.Select(node => node.ElementId).Should().Equal(nodesBefore,
            "the view nodes return to their original positions, not to the end");
    }

    // ── A9: multi-view delete ───────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_the_layout_from_every_view_and_undo_restores_both()
    {
        RegisterProvider(new MultiViewAuthoringModelProvider());
        AddStructureWriter(MultiViewAuthoringModelProvider.Key);
        using var cut = RenderEditor(MultiViewAuthoringModelProvider.Key);

        cut.Instance.TestModel!.Views.Should().HaveCount(2);

        await SelectElementAsync(cut, "lane-sales");
        await cut.InvokeAsync(() => cut.Instance.RequestDeleteSelectedAsync());
        await cut.InvokeAsync(() => cut.Instance.ConfirmDeleteAsync());

        foreach (var view in cut.Instance.TestModel!.Views)
        {
            view.Nodes.Should().NotContain(node => node.ElementId == "lane-sales", $"view '{view.Id}' drops the lane");
            view.Nodes.Should().NotContain(node => node.ElementId == "task-a", $"view '{view.Id}' drops the lane's task");
            view.Connections.Should().NotContain(connection => connection.RelationshipId == "flow-inside");
        }

        var pending = cut.Instance.TestPendingChangeSet!;
        pending.ViewChanges.Should().HaveCount(2, "one ModelingViewChange per affected view (A9)");
        pending.ViewChanges.Select(change => change.ViewId).Should().BeEquivalentTo(["view-one", "view-two"]);

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        foreach (var view in cut.Instance.TestModel!.Views)
        {
            view.Nodes.Should().Contain(node => node.ElementId == "lane-sales", $"undo restores view '{view.Id}'");
            view.Nodes.Should().Contain(node => node.ElementId == "task-a");
            view.Connections.Should().Contain(connection => connection.RelationshipId == "flow-inside");
        }
    }

    // ── A12: insert into a container ────────────────────────────────────────

    [Fact]
    public async Task Insert_into_a_container_places_the_node_inside_the_container_bounds()
    {
        RegisterProvider(new AuthoringModelingModelProvider());
        AddStructureWriter(AuthoringModelingModelProvider.Key);
        using var cut = RenderEditor(AuthoringModelingModelProvider.Key);

        await SelectElementAsync(cut, "lane-sales");
        await cut.InvokeAsync(() => cut.Instance.OpenInsertElementDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.ConfirmInsertElementAsync());

        var laneNode = cut.Instance.TestModel!.Views[0].Nodes.Single(node => node.ElementId == "lane-sales");
        var inserted = cut.Instance.TestModel.Elements.Single(element => element.Id != "lane-sales"
            && element.ParentId == "lane-sales"
            && element.SemanticType == cut.Instance.InsertTypeOptions[0].SemanticType);
        var insertedNode = cut.Instance.TestModel.Views[0].Nodes.Single(node => node.ElementId == inserted.Id);

        insertedNode.X.Should().BeGreaterThan(laneNode.X);
        insertedNode.Y.Should().BeGreaterThan(laneNode.Y);
        (insertedNode.X + insertedNode.Width).Should().BeLessThanOrEqualTo(laneNode.X + laneNode.Width,
            "the inserted rectangle stays inside the container it was dropped into (A12)");
        (insertedNode.Y + insertedNode.Height).Should().BeLessThanOrEqualTo(laneNode.Y + laneNode.Height);
    }

    [Fact]
    public async Task Insert_into_a_container_grows_it_in_the_same_command_and_undo_reverts_the_growth()
    {
        RegisterProvider(new TightContainerAuthoringModelProvider());
        AddStructureWriter(TightContainerAuthoringModelProvider.Key);
        using var cut = RenderEditor(TightContainerAuthoringModelProvider.Key);

        await SelectElementAsync(cut, "lane-tight");
        var laneBefore = cut.Instance.TestModel!.Views[0].Nodes.Single(node => node.ElementId == "lane-tight");
        var widthBefore = laneBefore.Width;
        var heightBefore = laneBefore.Height;

        await cut.InvokeAsync(() => cut.Instance.OpenInsertElementDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.ConfirmInsertElementAsync());

        var laneAfter = cut.Instance.TestModel!.Views[0].Nodes.Single(node => node.ElementId == "lane-tight");
        (laneAfter.Width > widthBefore || laneAfter.Height > heightBefore).Should().BeTrue(
            "the container GROWS to hold the new node (A12) — in the same command");
        cut.Instance.TestPendingChangeSet!.ViewChanges[0].Nodes.Should().Contain(node => node.ElementId == "lane-tight",
            "the growth is part of the reported view change");

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        var laneRestored = cut.Instance.TestModel!.Views[0].Nodes.Single(node => node.ElementId == "lane-tight");
        laneRestored.Width.Should().Be(widthBefore);
        laneRestored.Height.Should().Be(heightBefore);
    }

    // ── A11: undo / redo / one UI-driven insert ─────────────────────────────

    [Fact]
    public async Task Insert_undo_and_redo_keep_model_and_document_in_step()
    {
        RegisterProvider(new AuthoringModelingModelProvider());
        AddStructureWriter(AuthoringModelingModelProvider.Key);
        using var cut = RenderEditor(AuthoringModelingModelProvider.Key);
        var elementCount = cut.Instance.TestModel!.Elements.Count;
        var nodeCount = cut.Instance.TestDocument!.Nodes.Count;

        await PaletteInsertAsync(cut, "palette-undo", "bpmn2.task.user", 640, 520);
        cut.Instance.TestModel!.Elements.Should().HaveCount(elementCount + 1);
        cut.Instance.HasPendingModelChanges.Should().BeTrue();

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.TestModel!.Elements.Should().HaveCount(elementCount);
        cut.Instance.TestDocument!.Nodes.Should().HaveCount(nodeCount);
        cut.Instance.HasPendingModelChanges.Should().BeFalse("undo restored the loaded baseline");

        await cut.InvokeAsync(() => cut.Instance.RedoModelEditAsync());
        cut.Instance.TestModel!.Elements.Should().HaveCount(elementCount + 1, "redo re-applies the SAME command");
        cut.Instance.TestDocument!.Nodes.Should().HaveCount(nodeCount + 1);
        cut.Instance.HasPendingModelChanges.Should().BeTrue();
    }

    [Fact]
    public async Task Connect_undo_removes_the_relationship_and_its_connection()
    {
        RegisterProvider(new AuthoringModelingModelProvider());
        AddStructureWriter(AuthoringModelingModelProvider.Key);
        using var cut = RenderEditor(AuthoringModelingModelProvider.Key);
        var relationshipCount = cut.Instance.TestModel!.Relationships.Count;

        await SelectElementAsync(cut, "task-a");
        await cut.InvokeAsync(() => cut.Instance.OpenConnectDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.SelectConnectTargetForTestAsync("task-b"));
        await cut.InvokeAsync(() => cut.Instance.ConfirmConnectAsync());

        cut.Instance.TestModel!.Relationships.Should().HaveCount(relationshipCount + 1);
        var addedRelationshipId = cut.Instance.TestModel.Relationships[cut.Instance.TestModel.Relationships.Count - 1].Id;
        cut.Instance.TestModel.Views[0].Connections.Should().Contain(connection =>
            connection.RelationshipId == addedRelationshipId);

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.TestModel!.Relationships.Should().HaveCount(relationshipCount);
        cut.Instance.TestModel.Views[0].Connections.Should().HaveCount(1, "the view connection went with it");
        cut.Instance.HasPendingModelChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_redo_removes_the_cascade_again()
    {
        RegisterProvider(new AuthoringModelingModelProvider());
        AddStructureWriter(AuthoringModelingModelProvider.Key);
        using var cut = RenderEditor(AuthoringModelingModelProvider.Key);

        await SelectElementAsync(cut, "lane-sales");
        await cut.InvokeAsync(() => cut.Instance.RequestDeleteSelectedAsync());
        await cut.InvokeAsync(() => cut.Instance.ConfirmDeleteAsync());
        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.TestModel!.Elements.Should().Contain(element => element.Id == "lane-sales");

        await cut.InvokeAsync(() => cut.Instance.RedoModelEditAsync());

        cut.Instance.TestModel!.Elements.Should().NotContain(element => element.Id == "lane-sales");
        cut.Instance.TestModel.Elements.Should().NotContain(element => element.Id == "task-a");
        cut.Instance.TestModel.Relationships.Should().BeEmpty();
    }

    [Fact]
    public async Task Non_drag_insert_through_the_rendered_dialog_commits_the_element()
    {
        RegisterProvider(new AuthoringModelingModelProvider());
        AddStructureWriter(AuthoringModelingModelProvider.Key);
        using var cut = RenderEditor(AuthoringModelingModelProvider.Key);
        var elementCount = cut.Instance.TestModel!.Elements.Count;

        cut.Find("[data-testid='modeling-insert-element']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-insert-element-dialog']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-insert-container']").Change("lane-sales");
        cut.Find("[data-testid='modeling-insert-name']").Input("Reviewed order");
        cut.Find("[data-testid='modeling-insert-confirm']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-insert-element-dialog']").Should().BeEmpty());
        cut.Instance.TestModel!.Elements.Should().HaveCount(elementCount + 1);
        var inserted = cut.Instance.TestModel.Elements.Single(element => element.Name == "Reviewed order");
        inserted.ParentId.Should().Be("lane-sales");
        cut.Instance.TestPendingChangeSet!.AddedElements.Should().ContainSingle(entry => entry.Id == inserted.Id);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void RegisterProvider(IModelingModelProvider provider)
        => Services.TryAddEnumerable(ServiceDescriptor.Singleton(provider));

    private AuthoringModelingModelWriter AddStructureWriter(string providerKey)
    {
        var writer = new AuthoringModelingModelWriter(providerKey, ModelingModelWriterCapabilities.All);
        Services.AddSingleton<IModelingModelWriter>(writer);
        return writer;
    }

    private IRenderedComponent<TmModelingEditor> RenderEditor(
        string providerKey,
        Action<ComponentParameterCollectionBuilder<TmModelingEditor>>? configure = null)
    {
        var cut = Render<TmModelingEditor>(builder =>
        {
            builder.Add(component => component.ProviderKey, providerKey);
            builder.Add(component => component.AllowModelEditing, true);
            builder.Add(component => component.WriterCapabilities, ModelingModelWriterCapabilities.All);
            builder.Add(component => component.OnModelChanged,
                EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { }));
            configure?.Invoke(builder);
        });

        cut.WaitForAssertion(
            () => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"),
            TimeSpan.FromSeconds(5));
        return cut;
    }

    private static Task SelectElementAsync(IRenderedComponent<TmModelingEditor> cut, string elementId)
        => cut.InvokeAsync(() => cut.Instance.TestSelectElementForTestAsync(elementId));

    private static string NodeElementId(DiagramNode node)
        => node.Data.TryGetValue("modelElementId", out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static string ArgumentMatcherForInserted(IRenderedComponent<TmModelingEditor> cut)
        => cut.Instance.TestModel!.Elements.Single(element =>
            element.Id is not ("task-a" or "task-b")).Id;

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
}

/// <summary>A BPMN 2.0 model WITHOUT a view: the preview renders everything at grid positions.</summary>
internal sealed class ViewlessAuthoringModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.review.viewless";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = new ModelingModelDto
        {
            Id = "modeling-review-viewless",
            Title = "Viewless authoring",
            Notation = "bpmn2",
            SupportedNotations = ["bpmn2"],
            Metadata = new ModelingMetadataDto { SourceSystem = "Unit tests", SourceVersion = "2026.11" },
            Elements =
            [
                new() { Id = "task-a", Notation = "bpmn2", SemanticType = "UserTask", Name = "Approve request" },
                new() { Id = "task-b", Notation = "bpmn2", SemanticType = "UserTask", Name = "Ship order" }
            ]
        };
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "flow-ab",
            SourceElementId = "task-a",
            TargetElementId = "task-b",
            RelationshipType = "SequenceFlow",
            Name = string.Empty
        });
        return Task.FromResult(model);
    }
}

/// <summary>UML use cases: the Include relationship is legal only under UseCaseDiagram.</summary>
internal sealed class UmlUseCaseModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.review.uml-usecase";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = new ModelingModelDto
        {
            Id = "modeling-review-uml",
            Title = "UML use cases",
            Notation = "uml25",
            SupportedNotations = ["uml25"],
            Metadata = new ModelingMetadataDto { SourceSystem = "Unit tests" },
            Elements =
            [
                new() { Id = "uc-a", Notation = "uml25", SemanticType = "UseCase", Name = "Checkout" },
                new() { Id = "uc-b", Notation = "uml25", SemanticType = "UseCase", Name = "Payment" }
            ]
        };
        // The VIEW carries no viewpoint on purpose: the editor's ViewpointKey parameter is what
        // the review (A2) added to the engine context, so the positive/negative pair isolates it.
        model.Views.Add(new ModelingViewDto
        {
            Id = "usecase-view",
            Name = "Use cases",
            Notation = "uml25",
            Nodes =
            [
                new() { ElementId = "uc-a", X = 80, Y = 80, Width = 160, Height = 90 },
                new() { ElementId = "uc-b", X = 320, Y = 80, Width = 160, Height = 90 }
            ]
        });
        return Task.FromResult(model);
    }
}

/// <summary>A lane + task + flow rendered into TWO views — the multi-view delete fixture (A9).</summary>
internal sealed class MultiViewAuthoringModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.review.multiview";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = new ModelingModelDto
        {
            Id = "modeling-review-multiview",
            Title = "Multi-view authoring",
            Notation = "bpmn2",
            SupportedNotations = ["bpmn2"],
            Metadata = new ModelingMetadataDto { SourceSystem = "Unit tests" },
            Elements =
            [
                new() { Id = "pool-pool", Notation = "bpmn2", SemanticType = "Pool", Name = "Order pool" },
                new() { Id = "lane-sales", Notation = "bpmn2", SemanticType = "Lane", Name = "Sales", ParentId = "pool-pool" },
                new() { Id = "task-a", Notation = "bpmn2", SemanticType = "UserTask", Name = "Approve request", ParentId = "lane-sales" },
                new() { Id = "task-outside", Notation = "bpmn2", SemanticType = "UserTask", Name = "Outside task" }
            ]
        };
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "flow-inside",
            SourceElementId = "task-a",
            TargetElementId = "task-outside",
            RelationshipType = "SequenceFlow",
            Name = string.Empty
        });

        foreach (var (viewId, offset) in new[] { ("view-one", 0d), ("view-two", 600d) })
        {
            model.Views.Add(new ModelingViewDto
            {
                Id = viewId,
                Name = viewId,
                Notation = "bpmn2",
                Nodes =
                [
                    new() { ElementId = "pool-pool", X = 40 + offset, Y = 40, Width = 520, Height = 260 },
                    new() { ElementId = "lane-sales", X = 70 + offset, Y = 70, Width = 460, Height = 200, ParentNodeId = "pool-pool" },
                    new() { ElementId = "task-a", X = 110 + offset, Y = 120, Width = 160, Height = 88, ParentNodeId = "lane-sales" },
                    new() { ElementId = "task-outside", X = 640 + offset, Y = 120, Width = 160, Height = 88 }
                ],
                Connections = [new() { RelationshipId = "flow-inside", SourceNodeId = "task-a", TargetNodeId = "task-outside" }]
            });
        }

        return Task.FromResult(model);
    }
}

/// <summary>A lane whose rendered box is too small for the default insert — the growth fixture (A12).</summary>
internal sealed class TightContainerAuthoringModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.review.tight";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = new ModelingModelDto
        {
            Id = "modeling-review-tight",
            Title = "Tight container",
            Notation = "bpmn2",
            SupportedNotations = ["bpmn2"],
            Metadata = new ModelingMetadataDto { SourceSystem = "Unit tests" },
            Elements =
            [
                new() { Id = "pool-pool", Notation = "bpmn2", SemanticType = "Pool", Name = "Order pool" },
                new() { Id = "lane-tight", Notation = "bpmn2", SemanticType = "Lane", Name = "Tight lane", ParentId = "pool-pool" }
            ]
        };
        model.Views.Add(new ModelingViewDto
        {
            Id = "tight-view",
            Name = "Tight",
            Notation = "bpmn2",
            Nodes =
            [
                new() { ElementId = "pool-pool", X = 40, Y = 40, Width = 500, Height = 240 },
                new() { ElementId = "lane-tight", X = 70, Y = 70, Width = 170, Height = 120, ParentNodeId = "pool-pool" }
            ]
        });
        return Task.FromResult(model);
    }
}
