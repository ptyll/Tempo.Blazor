using System.Text.Json;
using Tempo.Blazor.Mcp.Modeling;
using Tempo.Blazor.Mcp.Tests.Fixtures;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>
/// Phase 7: <c>modeling_get_property_schema</c> — the read half of the shared property-schema
/// contract. The agent reads exactly the schema <c>modeling_apply_operations</c> validates against
/// (same provider resolution, same request shape).
/// </summary>
public class ModelingPropertySchemaToolsTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static (FakeModelingBackend backend, Guid id) Seeded()
    {
        var model = new ModelingModelDto { Notation = "bpmn2" };
        model.Elements.Add(new ModelingElementDto
        {
            Id = "task",
            Name = "Validate order",
            SemanticType = "Task",
            Notation = "bpmn2"
        });
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "flow",
            RelationshipType = "SequenceFlow",
            SourceElementId = "task",
            TargetElementId = "task"
        });
        model.Views.Add(new ModelingViewDto { Id = "default", ViewpointKey = "Process" });
        var backend = new FakeModelingBackend();
        var id = backend.Add("Schema", "/", model);
        return (backend, id);
    }

    [Fact]
    public async Task Without_a_provider_the_tool_reports_unsupported()
    {
        var (backend, id) = Seeded();

        var result = Parse(await ModelingPropertySchemaTools.GetPropertySchema(backend, backend, modelId: id, elementId: "task"));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("unsupported");
    }

    [Fact]
    public async Task An_element_id_resolves_the_notation_type_viewpoint_and_culture()
    {
        var (backend, id) = Seeded();
        var provider = new RecordingSchemaProvider(schemaFor: "Task");

        var result = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend,
            backend,
            [provider],
            modelId: id,
            elementId: "task",
            culture: "cs-CZ"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hasSchema").GetBoolean().Should().BeTrue();
        result.GetProperty("providerKey").GetString().Should().Be(provider.ProviderKey);
        result.GetProperty("notation").GetString().Should().Be("bpmn2");
        result.GetProperty("semanticType").GetString().Should().Be("Task");
        result.GetProperty("viewpointKey").GetString().Should().Be("Process");
        result.GetProperty("culture").GetString().Should().Be("cs-CZ");
        result.GetProperty("schema").GetProperty("sections")[0].GetProperty("fields")[0]
            .GetProperty("key").GetString().Should().Be("instructions");
        result.GetProperty("schema").GetProperty("sections")[0].GetProperty("fields")[0]
            .GetProperty("required").GetBoolean().Should().BeTrue();

        provider.LastRequest.Should().NotBeNull();
        provider.LastRequest!.ModelId.Should().Be(id.ToString());
        provider.LastRequest.Culture.Should().Be("cs-CZ");
        provider.LastRequest.ViewpointKey.Should().Be("Process");
    }

    [Fact]
    public async Task A_relationship_id_is_looked_up_by_its_relationship_type()
    {
        var (backend, id) = Seeded();
        var provider = new RecordingSchemaProvider(schemaFor: "SequenceFlow");

        var result = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend,
            backend,
            [provider],
            modelId: id,
            relationshipId: "flow"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("semanticType").GetString().Should().Be("SequenceFlow");
        result.GetProperty("notation").GetString().Should().Be("bpmn2");
    }

    [Fact]
    public async Task A_stateless_query_needs_no_model()
    {
        var backend = new FakeModelingBackend();
        var provider = new RecordingSchemaProvider(schemaFor: "Task");

        var result = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend,
            backend,
            [provider],
            notation: "bpmn2",
            semanticType: "Task"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("modelId", out _).Should().BeFalse("null members are omitted from the envelope");
        result.GetProperty("semanticType").GetString().Should().Be("Task");
    }

    [Fact]
    public async Task No_schema_for_the_type_is_a_successful_read_with_hasSchema_false()
    {
        var (backend, id) = Seeded();
        var provider = new RecordingSchemaProvider(schemaFor: "ServiceTask");

        var result = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend,
            backend,
            [provider],
            modelId: id,
            elementId: "task"));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hasSchema").GetBoolean().Should().BeFalse();
        result.TryGetProperty("schema", out _).Should().BeFalse("null members are omitted from the envelope");
    }

    [Fact]
    public async Task Unknown_ids_are_not_found()
    {
        var (backend, id) = Seeded();
        var provider = new RecordingSchemaProvider(schemaFor: "Task");

        var missingElement = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend, backend, [provider], modelId: id, elementId: "missing"));
        missingElement.GetProperty("error").GetString().Should().Be("not_found");

        var missingRelationship = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend, backend, [provider], modelId: id, relationshipId: "missing"));
        missingRelationship.GetProperty("error").GetString().Should().Be("not_found");

        var missingModel = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend, backend, [provider], modelId: Guid.NewGuid(), elementId: "task"));
        missingModel.GetProperty("error").GetString().Should().Be("not_found");
    }

    [Fact]
    public async Task An_element_id_without_a_model_is_a_validation_error()
    {
        var backend = new FakeModelingBackend();

        var result = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend, backend, [new RecordingSchemaProvider("Task")], elementId: "task"));

        result.GetProperty("error").GetString().Should().Be("validation_failed");
        result.GetProperty("message").GetString().Should().Contain("elementId requires modelId");
    }

    [Fact]
    public async Task A_missing_semantic_type_is_a_validation_error()
    {
        var backend = new FakeModelingBackend();

        var result = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend, backend, [new RecordingSchemaProvider("Task")], notation: "bpmn2"));

        result.GetProperty("error").GetString().Should().Be("validation_failed");
        result.GetProperty("message").GetString().Should().Contain("semanticType is required");
    }

    [Fact]
    public async Task A_failing_provider_is_reported_as_an_error()
    {
        var backend = new FakeModelingBackend();

        var result = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend, backend, [new ThrowingSchemaProvider()], notation: "bpmn2", semanticType: "Task"));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("error");
        result.GetProperty("message").GetString().Should().Contain("boom");
    }

    [Fact]
    public async Task The_requested_provider_key_selects_the_provider_and_a_stale_key_falls_back_to_the_first()
    {
        var backend = new FakeModelingBackend();
        var first = new RecordingSchemaProvider("Task", key: "schema.one");
        var second = new RecordingSchemaProvider("Task", key: "schema.two");

        var selected = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend, backend, [first, second], propertySchemaProviderKey: "schema.two",
            notation: "bpmn2", semanticType: "Task"));
        selected.GetProperty("providerKey").GetString().Should().Be("schema.two");

        var fallback = Parse(await ModelingPropertySchemaTools.GetPropertySchema(
            backend, backend, [first, second], propertySchemaProviderKey: "schema.missing",
            notation: "bpmn2", semanticType: "Task"));
        fallback.GetProperty("providerKey").GetString().Should().Be("schema.one");
    }

    // ── fakes ────────────────────────────────────────────────────────────────────

    private sealed class RecordingSchemaProvider : IModelingPropertySchemaProvider
    {
        private readonly string _schemaFor;

        public RecordingSchemaProvider(string schemaFor, string key = "tests.schema")
        {
            _schemaFor = schemaFor;
            ProviderKey = key;
        }

        public string ProviderKey { get; }

        public ModelingPropertySchemaRequest? LastRequest { get; private set; }

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(
            ModelingPropertySchemaRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (!string.Equals(request.SemanticType, _schemaFor, StringComparison.Ordinal))
            {
                return Task.FromResult<ModelingPropertySchemaDto?>(null);
            }

            return Task.FromResult<ModelingPropertySchemaDto?>(new ModelingPropertySchemaDto
            {
                SchemaId = "tests.schema.task",
                Version = "7",
                Notation = request.Notation,
                SemanticType = request.SemanticType,
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
            });
        }
    }

    private sealed class ThrowingSchemaProvider : IModelingPropertySchemaProvider
    {
        public string ProviderKey => "tests.schema.throwing";

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(
            ModelingPropertySchemaRequest request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }
}
