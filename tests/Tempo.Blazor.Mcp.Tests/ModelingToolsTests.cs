using System.Text.Json;
using Tempo.Blazor.Mcp.Modeling;
using Tempo.Blazor.Mcp.Tests.Fixtures;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>End-to-end tests for the modeling MCP tool suite against an in-memory backend.</summary>
public class ModelingToolsTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static (FakeModelingBackend backend, Guid id) SeededArchimate()
    {
        var model = new ModelingModelDto { Notation = "archimate" };
        model.Elements.Add(new ModelingElementDto { Id = "a", Name = "Customer", SemanticType = "BusinessActor", Notation = "archimate" });
        model.Elements.Add(new ModelingElementDto { Id = "r", Name = "Buyer", SemanticType = "BusinessRole", Notation = "archimate" });
        model.Relationships.Add(new ModelingRelationshipDto { Id = "rel1", RelationshipType = "Assignment", SourceElementId = "a", TargetElementId = "r" });
        var backend = new FakeModelingBackend();
        var id = backend.Add("Enterprise", "/Architecture", model);
        return (backend, id);
    }

    // ── list_models ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListModels_ReturnsStoredModels_AndForwardsScope()
    {
        var (backend, _) = SeededArchimate();
        backend.Add("Second", "/Architecture", new ModelingModelDto());

        var result = Parse(await ModelingModelTools.ListModels(backend, folderPath: "/Architecture", scopeAppId: "app-1"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("totalCount").GetInt32().Should().Be(2);
        backend.LastBrowseScopeAppId.Should().Be("app-1");
    }

    // ── get_model_tree ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetModelTree_ReturnsStructuredTree()
    {
        var (backend, id) = SeededArchimate();

        var result = Parse(await ModelingModelTools.GetModelTree(backend, backend, id));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("notation").GetString().Should().Be("archimate");
        result.GetProperty("counts").GetProperty("elements").GetInt32().Should().Be(2);
        result.GetProperty("counts").GetProperty("relationships").GetInt32().Should().Be(1);
        result.GetProperty("elements").GetArrayLength().Should().Be(2);
        result.TryGetProperty("concurrencyToken", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetModelTree_UnknownModel_ReturnsNotFound()
    {
        var backend = new FakeModelingBackend();

        var result = Parse(await ModelingModelTools.GetModelTree(backend, backend, Guid.NewGuid()));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("not_found");
    }

    // ── A3: property schema validation on the MCP path ──────────────────────────

    [Fact]
    public async Task ApplyOperations_With_a_schema_provider_rejects_a_missing_required_field_with_the_engine_message()
    {
        var (backend, id) = SeededArchimate();
        var provider = new McpTaskSchemaProvider();

        // The SAME batch the engine test pins: an add_element with no `instructions`.
        var ops = """
        [ { "op": "add_element", "id": "t", "name": "Task", "semanticType": "Task", "notation": "archimate" } ]
        """;

        var result = Parse(await ModelingOperationTools.ApplyOperations(
            backend,
            backend,
            id,
            ops,
            relationshipRules: null,
            expectedModifiedAt: backend.ModifiedAtOf(id),
            propertySchemaProviders: [provider]));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("validation_failed");

        // The message is the ENGINE's, byte for byte — the MCP path and the engine share the code.
        var engineModel = ModelingOperationEngine.Clone(backend.ModelOf(id));
        var expected = ModelingOperationEngine.ApplyJson(
            engineModel,
            ops,
            new ModelingOperationContext
            {
                Policy = ModelingOperationPolicy.Strict,
                ResolvePropertySchema = request => request.SemanticType == "Task" ? provider.Schema : null
            });

        expected.Errors.Should().ContainSingle();
        expected.Errors[0].Code.Should().Be("property_required");
        result.GetProperty("validationErrors")[0].GetString().Should().Be(expected.Errors[0].Message);
        backend.SaveCount.Should().Be(0, "a failed batch persists nothing");
    }

    [Fact]
    public async Task ApplyOperations_Without_a_schema_provider_validates_no_property_values()
    {
        var (backend, id) = SeededArchimate();
        var ops = """
        [ { "op": "add_element", "id": "t", "name": "Task", "semanticType": "Task", "notation": "archimate" } ]
        """;

        var result = Parse(await ModelingOperationTools.ApplyOperations(
            backend,
            backend,
            id,
            ops,
            relationshipRules: null,
            expectedModifiedAt: backend.ModifiedAtOf(id)));

        result.GetProperty("success").GetBoolean().Should().BeTrue("2.10.0 behavior is unchanged");
        backend.SaveCount.Should().Be(1);
    }

    // ── get_view ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetView_WithProjector_ReturnsDiagramDocument()
    {
        var (backend, id) = SeededArchimate();

        var result = Parse(await ModelingModelTools.GetView(backend, backend, id, new FakeModelingDiagramProjector()));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("document").GetProperty("pages").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetView_WithoutProjector_ReturnsUnsupported()
    {
        var (backend, id) = SeededArchimate();

        var result = Parse(await ModelingModelTools.GetView(backend, backend, id, projector: null));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("unsupported");
    }

    // ── apply_operations ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyOperations_ValidBatch_SavesAndReportsCreatedIds()
    {
        var (backend, id) = SeededArchimate();
        var ops = """
        [
          { "op": "add_element", "id": "c", "name": "Billing", "semanticType": "ApplicationComponent", "notation": "archimate" },
          { "op": "add_relationship", "relationshipType": "Serving", "sourceElementId": "c", "targetElementId": "a" }
        ]
        """;

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops, FakeModelingNotation.RelationshipRules(), backend.ModifiedAtOf(id)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("applied").GetInt32().Should().Be(2);
        result.GetProperty("createdIds").GetArrayLength().Should().Be(1);
        backend.SaveCount.Should().Be(1);
        backend.ModelOf(id).Relationships.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyOperations_InvalidArchimateRelationship_FailsWithoutSaving()
    {
        var (backend, id) = SeededArchimate();
        // "Composition" is not a supported relationship type for this notation.
        var ops = """
        [ { "op": "add_relationship", "relationshipType": "Composition", "sourceElementId": "a", "targetElementId": "r" } ]
        """;

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops, FakeModelingNotation.RelationshipRules(), backend.ModifiedAtOf(id)));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("validation_failed");
        result.GetProperty("validationErrors")[0].GetString().Should().Contain("not valid for notation 'archimate'");
        backend.SaveCount.Should().Be(0);
        backend.ModelOf(id).Relationships.Should().HaveCount(1); // unchanged
    }

    [Fact]
    public async Task ApplyOperations_PartiallyInvalidBatch_IsAtomic()
    {
        var (backend, id) = SeededArchimate();
        // First op is valid, second is invalid → whole batch rejected, nothing saved.
        var ops = """
        [
          { "op": "add_element", "id": "c", "name": "Billing", "semanticType": "ApplicationComponent", "notation": "archimate" },
          { "op": "add_relationship", "relationshipType": "Composition", "sourceElementId": "c", "targetElementId": "a" }
        ]
        """;

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops, FakeModelingNotation.RelationshipRules(), backend.ModifiedAtOf(id)));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        backend.SaveCount.Should().Be(0);
        backend.ModelOf(id).Elements.Should().HaveCount(2); // the valid add_element was NOT persisted
    }

    [Fact]
    public async Task ApplyOperations_UnknownModel_ReturnsNotFound()
    {
        var backend = new FakeModelingBackend();

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, Guid.NewGuid(), "[]", FakeModelingNotation.RelationshipRules()));

        result.GetProperty("error").GetString().Should().Be("not_found");
    }

    [Fact]
    public async Task ApplyOperations_StaleToken_ReturnsConflict()
    {
        var (backend, id) = SeededArchimate();
        var stale = backend.ModifiedAtOf(id).AddSeconds(-30);

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, "[]", FakeModelingNotation.RelationshipRules(), stale));

        result.GetProperty("error").GetString().Should().Be("conflict");
        backend.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyOperations_DeleteReferencedElement_IsRejected()
    {
        var (backend, id) = SeededArchimate();
        var ops = """[ { "op": "delete_element", "id": "a" } ]""";

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops, FakeModelingNotation.RelationshipRules(), backend.ModifiedAtOf(id)));

        result.GetProperty("error").GetString().Should().Be("validation_failed");
        result.GetProperty("validationErrors")[0].GetString().Should().Contain("still referenced");
        backend.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyOperations_DeleteElementAndItsRelationshipTogether_Succeeds()
    {
        var (backend, id) = SeededArchimate();
        var ops = """
        [
          { "op": "delete_relationship", "id": "rel1" },
          { "op": "delete_element", "id": "a" }
        ]
        """;

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops, FakeModelingNotation.RelationshipRules(), backend.ModifiedAtOf(id)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        backend.ModelOf(id).Elements.Should().HaveCount(1);
        backend.ModelOf(id).Relationships.Should().BeEmpty();
    }

    // ── validate ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_ByModelId_FlagsInvalidRelationship()
    {
        var (backend, id) = SeededArchimate();
        backend.ModelOf(id).Relationships.Add(new ModelingRelationshipDto { Id = "bad", RelationshipType = "Composition", SourceElementId = "a", TargetElementId = "r" });

        var result = Parse(await ModelingValidationTools.Validate(backend, backend, FakeModelingNotation.RelationshipRules(), id));

        result.GetProperty("valid").GetBoolean().Should().BeFalse();
        result.GetProperty("issues").EnumerateArray()
            .Should().Contain(i => i.GetProperty("sourceRelationshipId").GetString() == "bad");
    }

    [Fact]
    public async Task Validate_DanglingRelationship_IsFlagged()
    {
        var model = new ModelingModelDto { Notation = "archimate" };
        model.Elements.Add(new ModelingElementDto { Id = "a", Name = "A", SemanticType = "BusinessActor" });
        model.Relationships.Add(new ModelingRelationshipDto { Id = "dangling", RelationshipType = "Association", SourceElementId = "a", TargetElementId = "missing" });
        var json = JsonSerializer.Serialize(model, McpJson.Options);

        var result = Parse(await ModelingValidationTools.Validate(new FakeModelingBackend(), new FakeModelingBackend(), FakeModelingNotation.RelationshipRules(), modelId: null, modelJson: json));

        result.GetProperty("valid").GetBoolean().Should().BeFalse();
        result.GetProperty("issues").EnumerateArray()
            .Should().Contain(i => i.GetProperty("message").GetString()!.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    // ── list_notations ───────────────────────────────────────────────────────────

    [Fact]
    public void ListNotations_WithRegistry_ListsProfiles()
    {
        var result = Parse(ModelingValidationTools.ListNotations(FakeModelingNotation.Registry()));

        result.GetProperty("totalCount").GetInt32().Should().Be(1);
        result.GetProperty("notations")[0].GetProperty("notationKey").GetString().Should().Be("archimate");
        result.GetProperty("notations")[0].GetProperty("supportedRelationshipTypes").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("Serving");
    }

    [Fact]
    public void ListNotations_WithoutProfiles_ReturnsEmpty()
    {
        var result = Parse(ModelingValidationTools.ListNotations());

        result.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    /// <summary>A property-schema provider that requires one field on the Task type (A3).</summary>
    private sealed class McpTaskSchemaProvider : IModelingPropertySchemaProvider
    {
        public string ProviderKey => "tempo.tests.mcp.schema";

        public ModelingPropertySchemaDto Schema { get; } = new()
        {
            SchemaId = "tests.mcp.task",
            Version = "1",
            Notation = "archimate",
            SemanticType = "Task",
            Sections =
            [
                new ModelingPropertySectionDto
                {
                    Key = "execution",
                    Label = "Execution",
                    Fields =
                    [
                        new ModelingPropertyFieldDto
                        {
                            Key = "instructions",
                            Label = "Instructions",
                            Type = ModelingPropertyFieldType.String,
                            Required = true
                        }
                    ]
                }
            ]
        };

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(ModelingPropertySchemaRequest request, CancellationToken cancellationToken)
            => Task.FromResult<ModelingPropertySchemaDto?>(
                string.Equals(request.SemanticType, "Task", StringComparison.Ordinal) ? Schema : null);
    }
}
