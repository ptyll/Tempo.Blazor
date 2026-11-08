using System.Text.Json;
using Tempo.Blazor.Mcp.Modeling;
using Tempo.Blazor.Mcp.Tests.Fixtures;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>
/// Phase 7, the mandatory parity contract of decision <c>MODELING-UNIFIED-OPERATION-ENGINE</c>
/// (§6.4 "one semantics, same errors"): the same logical batch applied through
/// <c>modeling_apply_operations</c> and through the editor's SAVE path yields the same model and the
/// same error list.
/// <para>
/// The editor's save path is represented by <see cref="ReferenceModelingWriter">the documented host
/// writer pattern</see> — the editor's contribution is the v2 change set; a writer translates it with
/// <see cref="ModelingChangeSetTranslator"/> and applies it through the shared
/// <see cref="ModelingOperationEngine"/> under the <see cref="ModelingOperationPolicy.Authoring"/>
/// policy, with the property-schema resolver preloaded. The MCP tool now runs the SAME policy, which
/// is what makes the two agree. The editor's LIVE commands intentionally carry no resolver (§5.6), so
/// property parity is defined against the SAVE — exactly as this file does it.
/// </para>
/// </summary>
public class ModelingMcpParityTests
{
    private const string ProviderKey = "tests.parity-writer";

    // ── the two paths ────────────────────────────────────────────────────────────

    private static async Task<(JsonElement Result, ModelingModelDto? Saved)> ApplyMcp(
        ModelingModelDto model,
        string ops,
        IModelingRelationshipRulesProvider? rules = null,
        IModelingPropertySchemaProvider? schema = null,
        DateTime? expectedModifiedAt = null)
    {
        var backend = new FakeModelingBackend();
        var id = backend.Add("Parity", "/", ModelingOperationEngine.Clone(model));
        var json = await ModelingOperationTools.ApplyOperations(
            backend,
            backend,
            id,
            ops,
            rules,
            expectedModifiedAt ?? backend.ModifiedAtOf(id),
            schema is null ? null : [schema]);
        var result = JsonDocument.Parse(json).RootElement.Clone();
        return (result, backend.SaveCount > 0 ? backend.ModelOf(id) : null);
    }

    private static async Task<ModelingModelSaveResult> SaveEditor(
        ModelingModelDto model,
        ModelingModelChangeSet changes,
        IModelingRelationshipRulesProvider? rules = null,
        IModelingPropertySchemaProvider? schema = null,
        string? baseVersion = null)
    {
        var writer = new ReferenceModelingWriter(
            model,
            relationshipRules: rules,
            propertySchemas: schema,
            providerKey: ProviderKey);
        changes.ModelId = "parity";
        changes.ProviderKey = ProviderKey;
        changes.BaseVersion = baseVersion ?? writer.Version;
        return await writer.SaveChangesAsync(changes, CancellationToken.None);
    }

    private static ModelingModelDto Seed()
    {
        var model = new ModelingModelDto { Id = "parity", Title = "Parity", Notation = "archimate" };
        model.Elements.Add(new ModelingElementDto
        {
            Id = "actor",
            Name = "Customer",
            SemanticType = "BusinessActor",
            Notation = "archimate"
        });
        model.Elements.Add(new ModelingElementDto
        {
            Id = "role",
            Name = "Buyer",
            SemanticType = "BusinessRole",
            Notation = "archimate"
        });
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "assign",
            RelationshipType = "Assignment",
            SourceElementId = "actor",
            TargetElementId = "role"
        });
        return model;
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value, McpJson.Options);

    // ── the parity specs ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_structural_batch_with_layout_yields_the_same_model_through_both_paths()
    {
        var mcpOps = """
        [
          { "op": "add_element", "id": "billing", "name": "Billing", "semanticType": "ApplicationComponent", "notation": "archimate", "properties": { "owner": "finance" } },
          { "op": "add_relationship", "id": "serve", "relationshipType": "Serving", "sourceElementId": "billing", "targetElementId": "actor" },
          { "op": "set_view_node", "elementId": "actor", "x": 40, "y": 60 },
          { "op": "set_view_node", "elementId": "billing", "x": 320, "y": 60, "width": 200, "height": 80 },
          { "op": "set_view_connection", "relationshipId": "serve", "waypoints": [ { "x": 240, "y": 100 } ] }
        ]
        """;

        var (mcp, mcpModel) = await ApplyMcp(Seed(), mcpOps, FakeModelingNotation.RelationshipRules());
        mcp.GetProperty("success").GetBoolean().Should().BeTrue();
        mcpModel.Should().NotBeNull();

        var changes = new ModelingModelChangeSet
        {
            AddedElements =
            [
                new ModelingElementAdd
                {
                    Id = "billing",
                    Name = "Billing",
                    SemanticType = "ApplicationComponent",
                    Notation = "archimate",
                    Properties = { ["owner"] = JsonSerializer.SerializeToElement("finance") }
                }
            ],
            AddedRelationships =
            [
                new ModelingRelationshipAdd
                {
                    Id = "serve",
                    RelationshipType = "Serving",
                    SourceElementId = "billing",
                    TargetElementId = "actor"
                }
            ],
            ViewChanges =
            [
                new ModelingViewChange
                {
                    ViewId = string.Empty,
                    Nodes =
                    [
                        new ModelingViewNodeChange { ElementId = "actor", X = 40, Y = 60 },
                        new ModelingViewNodeChange { ElementId = "billing", X = 320, Y = 60, Width = 200, Height = 80 }
                    ],
                    Connections =
                    [
                        new ModelingViewConnectionChange
                        {
                            RelationshipId = "serve",
                            Waypoints = [new ModelingViewWaypointDto { X = 240, Y = 100 }]
                        }
                    ]
                }
            ]
        };

        var editor = await SaveEditor(Seed(), changes, FakeModelingNotation.RelationshipRules());
        editor.Succeeded.Should().BeTrue(editor.Message);

        Json(mcpModel!.Elements).Should().Be(Json(editor.Model!.Elements), "the same batch is the same elements");
        Json(mcpModel.Relationships).Should().Be(
            Json(editor.Model!.Relationships),
            "the same batch is the same relationships (adds, endpoints and properties)");
        Json(mcpModel.Views).Should().Be(
            Json(editor.Model!.Views),
            "the same layout ops are the same views (nodes, geometry and waypoints)");
        mcpModel.Views.Should().ContainSingle();

        mcp.GetProperty("diff").GetProperty("addedElementIds")[0].GetString().Should().Be("billing");
        mcp.GetProperty("diff").GetProperty("addedRelationshipIds")[0].GetString().Should().Be("serve");
        mcp.GetProperty("diff").GetProperty("counts").GetProperty("layoutOnlyElements").GetInt32()
            .Should().Be(1, "the pre-existing element gained a view node; the new one is simply added");
    }

    [Fact]
    public async Task An_invalid_relationship_fails_both_paths_with_the_same_message()
    {
        var ops = """
        [ { "op": "add_relationship", "relationshipType": "Composition", "sourceElementId": "actor", "targetElementId": "role" } ]
        """;

        var (mcp, _) = await ApplyMcp(Seed(), ops, FakeModelingNotation.RelationshipRules());
        mcp.GetProperty("success").GetBoolean().Should().BeFalse();
        var mcpMessage = mcp.GetProperty("validationErrors")[0].GetString();

        var editor = await SaveEditor(
            Seed(),
            new ModelingModelChangeSet
            {
                AddedRelationships =
                [
                    new ModelingRelationshipAdd
                    {
                        Id = "bad",
                        RelationshipType = "Composition",
                        SourceElementId = "actor",
                        TargetElementId = "role"
                    }
                ]
            },
            FakeModelingNotation.RelationshipRules());

        editor.Succeeded.Should().BeFalse();
        editor.Issues.Should().ContainSingle();
        mcpMessage.Should().Be(editor.Issues[0].Message, "the error text is the engine's, byte for byte");
        editor.Issues[0].Id.Should().EndWith("-invalid_relationship");
    }

    [Fact]
    public async Task A_missing_required_property_fails_both_paths_with_the_same_message()
    {
        var schema = new RequiredOwnerSchemaProvider();

        var ops = """
        [ { "op": "add_element", "id": "billing", "name": "Billing", "semanticType": "ApplicationComponent", "notation": "archimate" } ]
        """;

        var (mcp, _) = await ApplyMcp(Seed(), ops, FakeModelingNotation.RelationshipRules(), schema);
        mcp.GetProperty("success").GetBoolean().Should().BeFalse();
        var mcpMessage = mcp.GetProperty("validationErrors")[0].GetString();
        mcpMessage.Should().Contain("Owner");

        var editor = await SaveEditor(
            Seed(),
            new ModelingModelChangeSet
            {
                AddedElements =
                [
                    new ModelingElementAdd
                    {
                        Id = "billing",
                        Name = "Billing",
                        SemanticType = "ApplicationComponent",
                        Notation = "archimate"
                    }
                ]
            },
            FakeModelingNotation.RelationshipRules(),
            schema);

        editor.Succeeded.Should().BeFalse();
        mcpMessage.Should().Be(editor.Issues[0].Message,
            "the editor's save and the MCP batch run the same shared property validator over the full set");
        editor.Issues[0].Id.Should().EndWith("-property_required");
    }

    [Fact]
    public async Task A_parent_move_that_invalidates_a_flow_fails_both_paths_with_the_same_message()
    {
        var model = Seed();
        model.Elements.Add(new ModelingElementDto { Id = "lane-a", Name = "Lane A", SemanticType = "Grouping" });
        model.Elements.Add(new ModelingElementDto { Id = "lane-b", Name = "Lane B", SemanticType = "Grouping" });
        model.Elements[0].ParentId = "lane-a";
        model.Elements[1].ParentId = "lane-a";
        var rules = new SameParentRules();

        // The move itself is a valid update; the POST-BATCH re-validation is what rejects the flow.
        var (mcp, _) = await ApplyMcp(model, """[ { "op": "update_element", "id": "role", "parentId": "lane-b" } ]""", rules);
        mcp.GetProperty("success").GetBoolean().Should().BeFalse();
        var mcpMessage = mcp.GetProperty("validationErrors")[0].GetString();

        var editor = await SaveEditor(
            model,
            new ModelingModelChangeSet
            {
                Elements = [new ModelingElementChange { ElementId = "role", ParentId = "lane-b" }]
            },
            rules);

        editor.Succeeded.Should().BeFalse();
        mcpMessage.Should().Be(editor.Issues[0].Message,
            "both paths re-validate the relationships incident to a parent change after the batch");
        mcpMessage.Should().Contain("Cross-lane flow is not allowed.");
        editor.Issues[0].Id.Should().EndWith("-invalid_relationship");
    }

    [Fact]
    public async Task A_stale_concurrency_token_fails_both_paths_without_persisting_and_returns_the_current_model()
    {
        var backend = new FakeModelingBackend();
        var id = backend.Add("Parity", "/", Seed());
        var baseline = backend.ModelOf(id);
        var stale = backend.ModifiedAtOf(id).AddSeconds(-30);

        var mcp = JsonDocument.Parse(await ModelingOperationTools.ApplyOperations(
            backend,
            backend,
            id,
            "[]",
            FakeModelingNotation.RelationshipRules(),
            stale)).RootElement;
        mcp.GetProperty("error").GetString().Should().Be("conflict");
        mcp.GetProperty("message").GetString().Should().Contain("Re-read with").And.Contain("modeling_get_model_tree");
        backend.SaveCount.Should().Be(0, "the stale MCP batch never reaches the provider");
        Semantics(backend.ModelOf(id)).Should().Be(Semantics(baseline), "the stale MCP batch changes nothing");

        var writer = new ReferenceModelingWriter(
            Seed(),
            relationshipRules: FakeModelingNotation.RelationshipRules(),
            providerKey: ProviderKey);
        var changeSet = new ModelingModelChangeSet
        {
            ModelId = "parity",
            ProviderKey = ProviderKey,
            // The editor bases its edit on the version it LOADED ("v0"); the source already moved on.
            BaseVersion = "v0",
            Elements = [new ModelingElementChange { ElementId = "role", Name = "Purchaser" }]
        };
        var editor = await writer.SaveChangesAsync(changeSet, CancellationToken.None);

        editor.Succeeded.Should().BeFalse();
        editor.IsConflict.Should().BeTrue();
        editor.Message.Should().Contain("v0").And.Contain("v1");
        editor.Model.Should().NotBeNull("the conflict carries the current model for the editor's reload path");
        writer.SaveCount.Should().Be(0, "the stale editor save never reaches the writer");
        Semantics(writer.PersistedModel).Should().Be(Semantics(baseline), "the stale editor save changes nothing");
        Semantics(editor.Model!).Should().Be(Semantics(baseline), "both paths hand back the model the source is actually on");
    }

    /// <summary>The semantic triplet both concurrency paths must leave untouched (metadata excluded —
    /// the two writers keep different source-version bookkeeping).</summary>
    private static string Semantics(ModelingModelDto model)
        => $"{Json(model.Elements)}|{Json(model.Relationships)}|{Json(model.Views)}";

    // ── fakes ────────────────────────────────────────────────────────────────────

    /// <summary>A property schema that requires <c>owner</c> on an <c>ApplicationComponent</c>.</summary>
    private sealed class RequiredOwnerSchemaProvider : IModelingPropertySchemaProvider
    {
        public string ProviderKey => "tests.parity-schema";

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(
            ModelingPropertySchemaRequest request,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(request.SemanticType, "ApplicationComponent", StringComparison.Ordinal))
            {
                return Task.FromResult<ModelingPropertySchemaDto?>(null);
            }

            return Task.FromResult<ModelingPropertySchemaDto?>(new ModelingPropertySchemaDto
            {
                SchemaId = "tests.parity.component",
                Version = "1",
                Notation = "archimate",
                SemanticType = "ApplicationComponent",
                Sections =
                [
                    new ModelingPropertySectionDto
                    {
                        Key = "ownership",
                        Label = "Ownership",
                        Fields =
                        [
                            new ModelingPropertyFieldDto
                            {
                                Key = "owner",
                                Label = "Owner",
                                Type = ModelingPropertyFieldType.String,
                                Required = true
                            }
                        ]
                    }
                ]
            });
        }
    }

    /// <summary>A notation rule that only allows a relationship inside ONE parent (lane/pool).</summary>
    private sealed class SameParentRules : IModelingRelationshipRulesProvider
    {
        public bool IsValidRelationship(
            string notationKey,
            string sourceType,
            string targetType,
            string relationshipType) => true;

        public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
            => string.Equals(context.SourceElement.ParentId, context.TargetElement.ParentId, StringComparison.Ordinal)
                ? ModelingRelationshipRuleResult.Valid
                : ModelingRelationshipRuleResult.Invalid(
                    "Cross-lane flow is not allowed.",
                    "Keep both ends in one lane.");
    }
}
