using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 7, decision <c>MODELING-UNIFIED-OPERATION-ENGINE</c> (§6.4 "one semantics, same errors"),
/// the bUnit half: the SAME logical edits a user performs in <see cref="TmModelingEditor"/> — an
/// insert, a connect and a lane move — are captured as the editor's v2 change set, saved through
/// <see cref="DemoModelingModelWriter"/> (the documented host-writer shape: translate with
/// <see cref="ModelingChangeSetTranslator"/>, apply through the ONE
/// <see cref="ModelingOperationEngine"/> under <see cref="ModelingOperationPolicy.Authoring"/>), and
/// then applied again as the equivalent <c>operationsJson</c> through
/// <see cref="ModelingOperationEngine.ApplyJson"/> — the exact entry point
/// <c>modeling_apply_operations</c> uses. Elements, relationships and views must be identical, and a
/// batch that fails must fail with the same messages and codes on both callers.
/// <para>
/// The relationship rules are allow-all on BOTH paths on purpose: this suite pins the BATCH
/// semantics (what the editor's change set means once translated) against the MCP JSON front end,
/// not the notation matrix — that is the notation-rule suites' contract, and the demo model's
/// "mixed" notation would otherwise make the rule set, rather than the engine, the subject.
/// </para>
/// </summary>
public sealed class ModelingEditorMcpParityTests : LocalizationTestBase
{
    /// <summary>The wire shape both paths serialize models and operations with (the MCP shape).</summary>
    private static readonly JsonSerializerOptions ModelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly ModelingEditorTestClock _clock = new();
    private readonly AllowAllRelationshipRules _rules = new();
    private readonly AllowAllViewpointRules _viewpointRules = new();

    public ModelingEditorMcpParityTests()
    {
        // Registered BEFORE AddTempoBlazorModeling: its TryAddSingleton keeps these instances for the
        // editor's [Inject] AND for the DI-created DemoModelingModelWriter, so both callers judge the
        // exact same rule set (the demo view's "overview" viewpoint is not part of the BPMN profile).
        Services.AddSingleton<IModelingRelationshipRulesProvider>(_rules);
        Services.AddSingleton<IModelingViewpointRulesProvider>(_viewpointRules);
        Services.AddTempoBlazorModeling();
        Services.AddSingleton<TimeProvider>(_clock);
    }

    // ── the two paths ────────────────────────────────────────────────────────────

    /// <summary>The editor's save executor: the demo writer the host wiring resolves for the demo key.</summary>
    private DemoModelingModelWriter Writer => (DemoModelingModelWriter)Services.GetRequiredService<IModelingModelWriter>();

    /// <summary>The MCP path: the same operations JSON applied through the shared engine.</summary>
    private async Task<ModelingOperationResult> ApplyOperationsJsonAsync(
        ModelingModelDto model,
        string operationsJson,
        CancellationToken cancellationToken = default)
    {
        var resolver = await ModelingPropertySchemaResolver.PreloadAsync(
            new DemoModelingPropertySchemaProvider(),
            model,
            ModelingPropertySchemaResolver.PropertySchemaPairsFromJson(model, operationsJson),
            model.Views.FirstOrDefault()?.ViewpointKey,
            CultureInfo.CurrentUICulture.Name,
            cancellationToken);

        return ModelingOperationEngine.ApplyJson(
            model,
            operationsJson,
            new ModelingOperationContext
            {
                RelationshipRules = _rules,
                ViewpointRules = _viewpointRules,
                ResolvePropertySchema = resolver,
                Policy = ModelingOperationPolicy.Authoring
            });
    }

    /// <summary>The change set → the translated batch the writer applies (the documented order).</summary>
    private static ModelingChangeSetTranslation Translate(ModelingModelChangeSet changes)
    {
        var translation = ModelingChangeSetTranslator.ToOperationBatch(changes);
        translation.Success.Should().BeTrue(
            string.Join(" | ", translation.Errors.Select(error => error.Message)));
        return translation;
    }

    /// <summary>The change set → the exact operations JSON the MCP tool accepts.</summary>
    private static string ToOperationsJson(ModelingChangeSetTranslation translation)
        => JsonSerializer.Serialize(translation.Batch.Operations, ModelJson);

    private IRenderedComponent<TmModelingEditor> RenderEditor()
        => Render<TmModelingEditor>(parameters => parameters
            .Add(component => component.ProviderKey, DemoModelingModelProvider.ProviderKeyValue)
            // The demo model is authored as BPMN: the notation key gives the editor the container
            // vocabulary (Pool/Lane/SubProcess) the lane move needs; the writer validates through the
            // model's own notation, and the allow-all rules make the two judges agree.
            .Add(component => component.NotationKey, "bpmn2")
            .Add(component => component.AllowModelEditing, true)
            .Add(component => component.WriterCapabilities, ModelingModelWriterCapabilities.All));

    /// <summary>Drives the three edits the parity contract names and returns the ids it used.</summary>
    private static async Task<(string TaskId, string RelationshipId, string LaneId)> DriveAuthoringAsync(
        IRenderedComponent<TmModelingEditor> cut)
    {
        var idsBefore = cut.Instance.TestModel!.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var palette = cut.Instance.InsertTypeOptions;
        var taskEntry = palette.FirstOrDefault(entry =>
                string.Equals(entry.SemanticType, "UserTask", StringComparison.OrdinalIgnoreCase))
            ?? palette.First(entry => !string.Equals(entry.SemanticType, "Lane", StringComparison.OrdinalIgnoreCase));

        // 1. Insert: a palette shape becomes a semantic element + its view node (a task shape).
        await PaletteInsertAsync(cut, "parity-palette-task", taskEntry.StencilId, 700, 120);
        var taskId = cut.Instance.TestModel!.Elements
            .Single(element => !idsBefore.Contains(element.Id)).Id;

        // 2. Insert a container the lane move can target ("Insert element" sheet → Lane at the root).
        await cut.InvokeAsync(() => cut.Instance.OpenInsertElementDialogAsync());
        cut.Find("[data-testid='modeling-insert-type']").Change("Lane");
        await cut.InvokeAsync(() => cut.Instance.ConfirmInsertElementAsync());
        var laneId = cut.Instance.TestModel!.Elements
            .Single(element => !idsBefore.Contains(element.Id) && element.SemanticType == "Lane").Id;

        // 3. Connect: the non-drag "Connect to" path commits a relationship + its view connection.
        await SelectElementAsync(cut, taskId);
        await cut.InvokeAsync(() => cut.Instance.OpenConnectDialogAsync());
        await cut.InvokeAsync(() => cut.Instance.SelectConnectTargetForTestAsync("bpmn-validate-order"));
        await cut.InvokeAsync(() => cut.Instance.SelectConnectTypeForTestAsync("SequenceFlow"));
        await cut.InvokeAsync(() => cut.Instance.ConfirmConnectAsync());
        cut.Instance.RelationshipDialogOpen.Should().BeFalse();
        cut.Instance.TestStructureErrorText.Should().BeNull("the pair is allowed");

        var relationshipId = cut.Instance.TestModel!.Relationships
            .Single(relationship => relationship.SourceElementId == taskId).Id;

        // 4. Lane move: the "Move to container" alternative re-parents the semantic element AND its
        //    view node in ONE layout transaction (the drag-into-a-lane gesture has the same contract).
        await SelectElementAsync(cut, "bpmn-start-order");
        await cut.InvokeAsync(() => cut.Instance.TestOpenMoveToContainerAsync());
        await cut.InvokeAsync(() => cut.Instance.TestSetMoveToContainerAsync(laneId));
        await cut.InvokeAsync(() => cut.Instance.TestConfirmMoveToContainerAsync());
        await cut.InvokeAsync(() => cut.Instance.TestFlushLayoutAnnouncementAsync());
        cut.Instance.TestModel!.Elements.Single(element => element.Id == "bpmn-start-order").ParentId
            .Should().Be(laneId);

        return (taskId, relationshipId, laneId);
    }

    // ── the parity specs ─────────────────────────────────────────────────────────

    [Fact]
    public async Task The_editors_insert_connect_and_lane_move_save_is_the_same_batch_through_ApplyJson()
    {
        var writer = Writer;
        // The writer's persisted copy is the editor's baseline; capture it BEFORE the save.
        var baseline = ModelingOperationEngine.Clone(writer.PersistedModel);

        using var cut = RenderEditor();
        WaitLoaded(cut);
        var (taskId, relationshipId, laneId) = await DriveAuthoringAsync(cut);

        var changeSet = cut.Instance.TestPendingChangeSet;
        changeSet.Should().NotBeNull();
        changeSet!.AddedElements.Select(element => element.Id).Should().BeEquivalentTo([taskId, laneId]);
        changeSet.AddedRelationships.Select(relationship => relationship.Id).Should().BeEquivalentTo([relationshipId]);
        changeSet.Elements.Should().ContainSingle(change => change.ElementId == "bpmn-start-order" && change.ParentId == laneId,
            "the lane move is a semantic re-parent");
        changeSet.ViewChanges.Should().NotBeEmpty("the new nodes and the moved node are layout changes too");

        var save = await cut.InvokeAsync(() => cut.Instance.SaveModelChangesAsync());
        save.Should().NotBeNull();
        save!.Succeeded.Should().BeTrue(Describe(save));
        save.Issues.Should().BeEmpty();
        writer.SaveCount.Should().Be(1, "the editor's save is the writer's only write");

        var persisted = writer.PersistedModel;
        persisted.Elements.Should().ContainSingle(element => element.Id == taskId && element.SemanticType == "UserTask",
            "the inserted task is in the persisted model");
        persisted.Elements.Single(element => element.Id == "bpmn-start-order").ParentId.Should().Be(laneId,
            "the lane move is persisted");
        persisted.Relationships.Should().ContainSingle(relationship => relationship.Id == relationshipId,
            "the connected relationship is persisted");

        var translation = Translate(changeSet);
        var applied = ModelingOperationEngine.Clone(baseline);
        var outcome = await ApplyOperationsJsonAsync(applied, ToOperationsJson(translation));

        outcome.Success.Should().BeTrue(Describe(outcome));
        outcome.Applied.Should().Be(translation.Batch.Operations.Count,
            "the JSON batch applies every operation the change set translated to");
        outcome.Errors.Should().BeEmpty();
        outcome.CreatedIds.Should().BeEmpty("both the editor's batch and the JSON batch carry explicit ids");

        Semantics(applied).Should().Be(Semantics(persisted),
            "the editor's save and the same operationsJson produce one model");
        Messages(outcome).Should().Equal(Issues(save), "neither path reports an error");
    }

    [Fact]
    public async Task The_same_batch_fails_with_the_same_messages_and_codes_on_both_paths()
    {
        var writer = Writer;
        var baseline = ModelingOperationEngine.Clone(writer.PersistedModel);

        using var cut = RenderEditor();
        WaitLoaded(cut);
        var (taskId, _, _) = await DriveAuthoringAsync(cut);
        var changeSet = cut.Instance.TestPendingChangeSet!;

        // A batch the editor's own save would reject: one added relationship points at an element that
        // does not exist. The FAILING batch is the editor's captured change set plus that one op, so the
        // two callers are compared on the same batch (an editor commit never creates an invalid change
        // set — its pre-commit validation is itself the engine).
        var invalid = JsonSerializer.Deserialize<ModelingModelChangeSet>(
            JsonSerializer.Serialize(changeSet, ModelJson), ModelJson)!;
        invalid.AddedRelationships.Add(new ModelingRelationshipAdd
        {
            Id = "parity-ghost-flow",
            RelationshipType = "SequenceFlow",
            SourceElementId = taskId,
            TargetElementId = "ghost"
        });

        var translation = Translate(invalid);
        var operationsJson = ToOperationsJson(translation);
        var writerResult = await writer.SaveChangesAsync(invalid, CancellationToken.None);
        writerResult.Succeeded.Should().BeFalse("the batch references a missing element");
        writerResult.Issues.Should().NotBeEmpty();
        writer.SaveCount.Should().Be(0, "a rejected batch persists nothing");
        Semantics(writer.PersistedModel).Should().Be(Semantics(baseline), "the writer's copy is untouched");

        var target = ModelingOperationEngine.Clone(baseline);
        var outcome = await ApplyOperationsJsonAsync(target, operationsJson);
        outcome.Success.Should().BeFalse("the same batch fails through the JSON front end too");
        outcome.Errors.Should().OnlyContain(error => error.Code == "not_found");

        Messages(outcome).Should().Equal(Issues(writerResult),
            "the engine's messages are the writer's messages, byte for byte");
        CodesOf(writerResult.Issues).Should().Equal(
            outcome.Errors.Select(error => error.Code),
            "the writer's issue ids carry the same engine codes");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static void WaitLoaded(IRenderedComponent<TmModelingEditor> cut)
    {
        cut.WaitForState(() => cut.Instance.TestModel is not null);
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']")
            .GetAttribute("data-state").Should().Be("loaded"));
    }

    private static async Task SelectElementAsync(IRenderedComponent<TmModelingEditor> cut, string elementId)
    {
        await cut.InvokeAsync(() => cut.Instance.TestSelectElementForTestAsync(elementId));
        cut.WaitForAssertion(() => cut.Instance.TestSelectedElementId.Should().Be(elementId));
    }

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

    /// <summary>The semantic triplet the contract compares (metadata and source versions excluded —
    /// the writer and the engine keep different version bookkeeping).</summary>
    private static string Semantics(ModelingModelDto model)
        => $"{Json(model.Elements)}|{Json(model.Relationships)}|{Json(model.Views)}";

    private static string Json(object? value) => JsonSerializer.Serialize(value, ModelJson);

    private static IReadOnlyList<string> Messages(ModelingOperationResult outcome)
        => [.. outcome.Errors.Select(error => error.Message)];

    private static IReadOnlyList<string> Issues(ModelingModelSaveResult result)
        => [.. result.Issues.Select(issue => issue.Message)];

    /// <summary>The writer encodes the engine code as the last dash-separated segment of its issue id.</summary>
    private static IReadOnlyList<string> CodesOf(IReadOnlyList<ModelingIssueDto> issues)
        => [.. issues.Select(issue => issue.Id[(issue.Id.LastIndexOf('-') + 1)..])];

    private static string Describe(ModelingModelSaveResult result)
        => result.Succeeded ? string.Empty : string.Join(" | ", result.Issues.Select(issue => issue.Message));

    private static string Describe(ModelingOperationResult result)
        => result.Success
            ? string.Empty
            : string.Join(" | ", result.Errors.Select(error => $"{error.Code}: {error.Message}"));

    /// <summary>
    /// The allow-all rules the two paths share (registered before <c>AddTempoBlazorModeling</c>, so the
    /// editor and the DI-created demo writer resolve the SAME instance). This suite compares the batch
    /// semantics, not the notation matrix; the demo model's "mixed" notation would otherwise make the
    /// rules the subject of every assertion.
    /// </summary>
    private sealed class AllowAllRelationshipRules : IModelingRelationshipRulesProvider
    {
        public bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType)
            => true;

        public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
            => ModelingRelationshipRuleResult.Valid;
    }

    /// <summary>
    /// The allow-all viewpoint rules the two paths share (same reason as the relationship rules): the
    /// demo view's "overview" viewpoint is not in the BPMN 2.0 profile, and this suite is about the
    /// batch the editor's change set means, not about the viewpoint matrix.
    /// </summary>
    private sealed class AllowAllViewpointRules : IModelingViewpointRulesProvider
    {
        public bool IsElementAllowedInViewpoint(string notationKey, string viewpointKey, string semanticType)
            => true;
    }
}
