using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Fáze 14: the editor's opt-in editing contract — the writer resolution, the model + preview
/// synchronization through ONE <see cref="DiagramCommandTransaction"/> per field commit, undo/redo
/// owned by the editor (Ctrl+Z never reaches the diagram canvas), the Manual/Immediate save modes,
/// the conflict alert with reload and the pending-changes confirmation.
/// </summary>
public sealed class TmModelingEditorEditingTests : LocalizationTestBase
{
    private readonly ModelingEditorTestClock _clock = new();

    public TmModelingEditorEditingTests()
    {
        Services.AddTempoBlazorModeling();
        Services.AddSingleton<TimeProvider>(_clock);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new EditingModelingModelProvider()));
    }

    [Fact]
    public async Task Editing_disabled_without_writer_or_callback()
    {
        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true));

        SelectElementAsync(cut);

        cut.Find("[data-testid='modeling-inspector']").QuerySelectorAll("input, textarea, [role='switch']")
            .Should().BeEmpty("no writer for the provider key and no OnModelChanged delegate — MO2 behavior");
    }

    [Fact]
    public async Task Name_edit_updates_tree_inspector_and_preview_node_label()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { })));

        SelectElementAsync(cut);

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-tree-node-task-a']").TextContent.Should().Contain("Renamed task");
        });
        var node = cut.Instance.TestDocument!.Nodes.Single(n => string.Equals((string?)n.Data["modelElementId"], "task-a", StringComparison.Ordinal));
        ((string)node.Data["label"]).Should().Be("Renamed task");
        ((string)node.Data["name"]).Should().Be("Renamed task");

        // Manual mode (default): the writer is NOT called yet, the change is pending.
        writer.Calls.Should().BeEmpty();
        cut.Instance.HasPendingModelChanges.Should().BeTrue();

        // The inspector keeps showing the committed value (and the draft resets from the model).
        cut.Find("#modeling-inspector-name").GetAttribute("value").Should().Be("Renamed task");
    }

    [Fact]
    public async Task Relationship_name_edit_updates_edge_label()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { })));

        SelectElementAsync(cut);

        cut.Find("[data-testid='modeling-tab-relations']").Click();
        cut.Find("[data-testid='modeling-related-outgoing-relationship-rel-approve-ship']").Click();

        var name = cut.Find("#modeling-inspector-name");
        name.Input("New flow name");
        name.KeyDown("Enter");

        cut.WaitForAssertion(() =>
        {
            var edge = cut.Instance.TestDocument!.Edges.Single(e => string.Equals(e.Id, "rel-approve-ship", StringComparison.Ordinal));
            edge.Label.Should().Be("New flow name");
        });
        cut.Instance.HasPendingModelChanges.Should().BeTrue();
    }

    [Fact]
    public async Task Undo_reverts_model_and_preview_in_one_step()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { })));

        SelectElementAsync(cut);

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());

        var element = cut.Instance.TestModel!.Elements.Single(e => e.Id == "task-a");
        element.Name.Should().Be("Approve request");
        var node = cut.Instance.TestDocument!.Nodes.Single(n => string.Equals((string?)n.Data["modelElementId"], "task-a", StringComparison.Ordinal));
        ((string)node.Data["label"]).Should().Be("Approve request");
        cut.Instance.HasPendingModelChanges.Should().BeFalse("undo restored the loaded baseline");

        cut.Find("[data-testid='modeling-tree-node-task-a']").TextContent.Should().Contain("Approve request");
    }

    [Fact]
    public async Task Redo_reapplies()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { })));

        SelectElementAsync(cut);

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");
        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        await cut.InvokeAsync(() => cut.Instance.RedoModelEditAsync());

        var node = cut.Instance.TestDocument!.Nodes.Single(n => string.Equals((string?)n.Data["modelElementId"], "task-a", StringComparison.Ordinal));
        ((string)node.Data["label"]).Should().Be("Renamed task");
        cut.Instance.HasPendingModelChanges.Should().BeTrue();
    }

    [Fact]
    public async Task Ctrl_Z_in_inspector_does_not_reach_diagram_canvas()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { })));

        SelectElementAsync(cut);

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");
        cut.Instance.TestStack!.CanUndo.Should().BeTrue("the commit pushed one transaction");

        // Ctrl+Z dispatched INSIDE the inspector — the editor's own stack consumes it; the
        // diagram canvas (a separate component with its own JS keyboard scope) never sees it.
        cut.Find("[data-testid='modeling-inspector']")
            .KeyDown(new KeyboardEventArgs { Key = "z", CtrlKey = true });

        cut.Instance.TestStack!.CanUndo.Should().BeFalse("the editor's transaction was undone");
        cut.Instance.TestStack.CanRedo.Should().BeTrue();
        cut.Instance.TestModel!.Elements.Single(e => e.Id == "task-a").Name.Should().Be("Approve request");
    }

    [Fact]
    public async Task Manual_save_calls_writer_with_aggregated_change_set_and_clears_pending()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        var completed = false;
        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { }))
            .Add(p => p.OnSaveCompleted, EventCallback.Factory.Create(this, () => completed = true)));

        SelectElementAsync(cut);

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");
        var lane = cut.Find("[data-testid='modeling-inspector-property-lane'] input");
        lane.Input("Warehouse");
        lane.Blur();

        cut.Instance.HasPendingModelChanges.Should().BeTrue();

        var result = await cut.InvokeAsync(() => cut.Instance.SaveModelChangesAsync());

        result.Should().NotBeNull();
        result!.Succeeded.Should().BeTrue();
        writer.Calls.Should().HaveCount(1);
        var changeSet = writer.Calls[0];
        changeSet.ModelId.Should().Be("modeling-editor-editing");
        changeSet.ProviderKey.Should().Be(EditingModelingModelProvider.Key);
        changeSet.BaseVersion.Should().Be("2026.06");
        changeSet.Elements.Should().ContainSingle();
        changeSet.Elements[0].ElementId.Should().Be("task-a");
        changeSet.Elements[0].Name.Should().Be("Renamed task");
        changeSet.Elements[0].SetProperties.Should().NotBeNull();
        changeSet.Elements[0].SetProperties!["lane"].GetString().Should().Be("Warehouse");
        completed.Should().BeTrue();
        cut.Instance.HasPendingModelChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Immediate_save_calls_writer_per_commit()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.SaveMode, ModelingSaveMode.Immediate)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { })));

        SelectElementAsync(cut);

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");
        var lane = cut.Find("[data-testid='modeling-inspector-property-lane'] input");
        lane.Input("Warehouse");
        lane.Blur();

        cut.WaitForAssertion(() => writer.Calls.Should().HaveCount(2));
        writer.Calls[0].Elements.Should().ContainSingle();
        writer.Calls[0].Elements[0].Name.Should().Be("Renamed task");
        writer.Calls[0].Elements[0].SetProperties.Should().BeNull();
        writer.Calls[1].Elements[0].SetProperties.Should().NotBeNull();
        writer.Calls[1].Elements[0].Name.Should().BeNull();
        cut.Instance.HasPendingModelChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Conflict_result_shows_alert_with_reload()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key)
        {
            NextResult = new ModelingModelSaveResult { Succeeded = false, IsConflict = true, Message = "Version 2026.06 is stale" }
        };
        Services.AddSingleton<IModelingModelWriter>(writer);

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { })));

        SelectElementAsync(cut);

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");
        cut.Find("[data-testid='modeling-inspector-save']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-inspector-conflict']").Should().NotBeNull());
        cut.Find("[data-testid='modeling-inspector-conflict']").TextContent.Should().Contain("Version 2026.06 is stale");
        cut.Instance.HasPendingModelChanges.Should().BeTrue("a conflict keeps the pending changes");

        cut.Find("[data-testid='modeling-conflict-reload']").Click();
        cut.Find("[data-testid='modeling-discard-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Instance.HasPendingModelChanges.Should().BeFalse("the confirmed reload discarded the pending changes");
            cut.Instance.TestModel!.Elements.Single(e => e.Id == "task-a").Name.Should().Be("Approve request");
        });
    }

    [Fact]
    public async Task Reload_with_pending_changes_asks_for_confirmation()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { })));

        await SelectMobileElementAsync(cut);

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");

        // Reload from the mobile action bar asks for confirmation first.
        cut.Find("[data-action-id='reload']").Click();
        cut.Find("[data-testid='modeling-discard-changes-modal']").Should().NotBeNull();

        // Cancel keeps everything.
        cut.Find("[data-testid='modeling-discard-cancel']").Click();
        cut.Instance.HasPendingModelChanges.Should().BeTrue();

        // Confirm discards and reloads.
        cut.Find("[data-action-id='reload']").Click();
        cut.Find("[data-testid='modeling-discard-confirm']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Instance.HasPendingModelChanges.Should().BeFalse();
            cut.Instance.TestModel!.Elements.Single(e => e.Id == "task-a").Name.Should().Be("Approve request");
        });
    }

    [Fact]
    public async Task OnModelChanged_raised_after_each_commit()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        var raised = new List<ModelingModelChangedEventArgs>();
        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, raised.Add)));

        SelectElementAsync(cut);

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");
        var lane = cut.Find("[data-testid='modeling-inspector-property-lane'] input");
        lane.Input("Warehouse");
        lane.Blur();

        raised.Should().HaveCount(2);
        raised[0].ChangeSet.Elements.Should().ContainSingle();
        raised[0].ChangeSet.Elements[0].Name.Should().Be("Renamed task");
        raised[0].Model.Elements.Single(e => e.Id == "task-a").Name.Should().Be("Renamed task");
        raised[1].ChangeSet.Elements[0].Name.Should().BeNull();
        raised[1].ChangeSet.Elements[0].SetProperties!["lane"].GetString().Should().Be("Warehouse");
    }

    [Fact]
    public async Task Open_editor_dialog_reflects_renamed_element()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { })));

        SelectElementAsync(cut);

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        // The dialog body renders the node text through the diagram engine's own markup path —
        // the DOCUMENT the dialog editor received carries the renamed label (the bUnit-visible
        // contract; the canvas paint is covered by E2E).
        var dialogNode = cut.Instance.TestOpenedEditorDocument!.Nodes
            .Single(n => string.Equals((string?)n.Data["modelElementId"], "task-a", StringComparison.Ordinal));
        ((string)dialogNode.Data["label"]).Should().Be("Renamed task");
    }

    [Fact]
    public async Task Mobile_inspector_edit_and_save_from_action_bar()
    {
        var writer = new RecordingModelingModelWriter(EditingModelingModelProvider.Key);
        Services.AddSingleton<IModelingModelWriter>(writer);

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.AllowModelEditing, true)
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.OnModelChanged, EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { })));

        await SelectMobileElementAsync(cut);

        // No pending changes: the sticky Save/Discard entries are NOT in the action bar.
        cut.FindAll("[data-action-id='save']").Should().BeEmpty();

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");

        cut.WaitForAssertion(() => cut.Find("[data-action-id='save']").Should().NotBeNull());
        cut.Find("[data-action-id='save']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Instance.HasPendingModelChanges.Should().BeFalse();
            writer.Calls.Should().HaveCount(1);
            writer.Calls[0].Elements[0].Name.Should().Be("Renamed task");
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IRenderedComponent<TmModelingEditor> RenderEditor(
        Action<ComponentParameterCollectionBuilder<TmModelingEditor>> parameters)
        => Render<TmModelingEditor>(builder =>
        {
            builder.Add(p => p.ProviderKey, EditingModelingModelProvider.Key);
            parameters(builder);
        });

    private static void SelectElementAsync(IRenderedComponent<TmModelingEditor> cut)
    {
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));
        cut.Find("[data-testid='modeling-tree-node-task-a']").Click();
    }

    /// <summary>The compact band mounts ONE panel at a time: a tree tap is G2 browsing (it does
    /// not switch tabs), so the edit flow is Tree tab → tap the node → Inspector tab.</summary>
    private static async Task SelectMobileElementAsync(IRenderedComponent<TmModelingEditor> cut)
    {
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));
        // The compact band mounts ONE panel at a time: Tree tab first, then the tap, then the
        // Inspector tab (a tree tap is G2 browsing and never switches tabs by itself).
        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.Find("[data-testid='modeling-tree-node-task-a']").Click();
        cut.Find("[data-testid='modeling-panel-tab-inspector']").Click();
        await Task.CompletedTask;
    }

    private sealed class RecordingModelingModelWriter : IModelingModelWriter
    {
        public RecordingModelingModelWriter(string providerKey) => ProviderKey = providerKey;

        public string ProviderKey { get; }

        public List<ModelingModelChangeSet> Calls { get; } = [];

        public ModelingModelSaveResult? NextResult { get; set; }

        public Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken)
        {
            Calls.Add(changes);
            return Task.FromResult(NextResult ?? new ModelingModelSaveResult { Succeeded = true });
        }
    }
}

/// <summary>
/// Fáze 14 editing provider: one BPMN task with scalar properties of every JSON kind, a complex
/// object property, governance metadata and one relationship — everything the editable inspector
/// and the change-set aggregation need.
/// </summary>
internal sealed class EditingModelingModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.editing";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var model = new ModelingModelDto
        {
            Id = "modeling-editor-editing",
            Title = "Modeling editor editing",
            Metadata = new ModelingMetadataDto
            {
                SourceSystem = "Unit tests",
                SourceVersion = "2026.06",
                IsFresh = true
            },
            Elements =
            [
                new()
                {
                    Id = "task-a",
                    SourceId = "source-task-a",
                    Notation = "bpmn",
                    SemanticType = "userTask",
                    Name = "Approve request",
                    Description = "Original description",
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["stencilId"] = JsonSerializer.SerializeToElement("bpmn2.task.user"),
                        ["lane"] = JsonSerializer.SerializeToElement("Sales"),
                        ["priority"] = JsonSerializer.SerializeToElement(3),
                        ["active"] = JsonSerializer.SerializeToElement(true),
                        ["ratio"] = JsonSerializer.SerializeToElement(0.5),
                        ["shape"] = JsonSerializer.SerializeToElement(new { a = 1 })
                    },
                    Governance = new ModelingGovernanceDto { TrustLevel = "demo", ReviewState = "approved" }
                },
                new()
                {
                    Id = "task-ship",
                    SourceId = "source-task-ship",
                    Notation = "bpmn",
                    SemanticType = "serviceTask",
                    Name = "Ship order",
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["stencilId"] = JsonSerializer.SerializeToElement("bpmn2.task.service")
                    }
                }
            ],
            Relationships =
            [
                new()
                {
                    Id = "rel-approve-ship",
                    SourceElementId = "task-a",
                    TargetElementId = "task-ship",
                    RelationshipType = "sequenceFlow",
                    Name = "Validated order",
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["priority"] = JsonSerializer.SerializeToElement(3)
                    }
                }
            ]
        };

        model.Views.Add(new ModelingViewDto
        {
            Id = "main-view",
            Name = "Main view",
            Notation = "bpmn",
            Nodes =
            [
                new() { ElementId = "task-a", X = 120, Y = 160, Width = 180, Height = 90 },
                new() { ElementId = "task-ship", X = 420, Y = 160, Width = 180, Height = 90 }
            ],
            Connections =
            [
                new() { RelationshipId = "rel-approve-ship", SourceNodeId = "task-a", TargetNodeId = "task-ship" }
            ]
        });

        return Task.FromResult(model);
    }
}
