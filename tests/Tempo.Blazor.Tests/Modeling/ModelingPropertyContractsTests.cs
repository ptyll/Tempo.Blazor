using System.Text.Json;
using System.Text.Json.Serialization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 5 / decision <c>MODELING-PROPERTY-SCHEMA</c>: the wire contract of the property schema, the
/// lookup request/result and the two provider interfaces. A host implements these in its own
/// assembly, so the DTOs must round-trip through <see cref="JsonSerializer"/> with the host's
/// options and must never require a converter (docs/modeling-authoring.md §5.1–§5.4).
/// </summary>
public sealed class ModelingPropertyContractsTests
{
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void Schema_dto_defaults_are_empty_lists_and_non_null_strings()
    {
        var schema = new ModelingPropertySchemaDto();

        schema.SchemaId.Should().BeEmpty();
        schema.Version.Should().BeEmpty();
        schema.Notation.Should().BeEmpty();
        schema.SemanticType.Should().BeEmpty();
        schema.Sections.Should().NotBeNull().And.BeEmpty();

        var section = new ModelingPropertySectionDto();
        section.Key.Should().BeEmpty();
        section.Label.Should().BeEmpty();
        section.Fields.Should().NotBeNull().And.BeEmpty();

        var field = new ModelingPropertyFieldDto();
        field.Key.Should().BeEmpty();
        field.Type.Should().Be(ModelingPropertyFieldType.String);
        field.Options.Should().NotBeNull().And.BeEmpty();
        field.ItemFields.Should().NotBeNull().And.BeEmpty();
        field.Lookup.Should().BeNull();

        new ModelingPropertyOptionDto().Value.Should().BeEmpty();
    }

    [Fact]
    public void Schema_dto_null_lists_are_normalized_to_empty()
    {
        var section = new ModelingPropertySectionDto { Fields = null! };
        section.Fields.Should().NotBeNull().And.BeEmpty();

        var field = new ModelingPropertyFieldDto { Options = null!, ItemFields = null! };
        field.Options.Should().NotBeNull().And.BeEmpty();
        field.ItemFields.Should().NotBeNull().And.BeEmpty();

        var schema = new ModelingPropertySchemaDto { Sections = null! };
        schema.Sections.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Lookup_request_and_result_defaults_are_safe()
    {
        var request = new ModelingLookupRequest();
        request.SearchText.Should().BeEmpty();
        request.Take.Should().Be(20);
        request.Skip.Should().Be(0);
        request.ContinuationToken.Should().BeNull();

        var result = new ModelingLookupResultDto();
        result.Items.Should().NotBeNull().And.BeEmpty();
        result.HasMore.Should().BeFalse();
        result.ContinuationToken.Should().BeNull();

        var descriptor = new ModelingLookupDescriptor();
        descriptor.MinQueryLength.Should().Be(2);
        descriptor.PageSize.Should().Be(20);
        descriptor.SearchRequired.Should().BeTrue();
    }

    [Fact]
    public void Schema_round_trips_through_json_with_the_typed_entity_reference_shape()
    {
        var schema = new ModelingPropertySchemaDto
        {
            SchemaId = "prompthelper.bpmn.step",
            Version = "4",
            Notation = "bpmn2",
            SemanticType = "Task",
            Sections =
            [
                new ModelingPropertySectionDto
                {
                    Key = "ownership",
                    Label = "Ownership",
                    Order = 2,
                    Fields =
                    [
                        new ModelingPropertyFieldDto
                        {
                            Key = "role",
                            Label = "Role",
                            Type = ModelingPropertyFieldType.EntityReference,
                            Required = true,
                            Lookup = new ModelingLookupDescriptor
                            {
                                LookupType = "role",
                                EntityType = "processRole",
                                MinQueryLength = 2,
                                PageSize = 10,
                                SearchRequired = false
                            }
                        },
                        new ModelingPropertyFieldDto
                        {
                            Key = "exceptions",
                            Label = "Exceptions",
                            Type = ModelingPropertyFieldType.StructuredList,
                            ItemFields =
                            [
                                new ModelingPropertyFieldDto
                                {
                                    Key = "code",
                                    Label = "Code",
                                    Type = ModelingPropertyFieldType.String,
                                    Required = true,
                                    MaxLength = 40
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(schema, WireOptions);
        var roundTrip = JsonSerializer.Deserialize<ModelingPropertySchemaDto>(json, WireOptions)!;

        json.Should().Contain("\"semanticType\":\"Task\"");
        json.Should().Contain("\"lookupType\":\"role\"");
        roundTrip.SchemaId.Should().Be("prompthelper.bpmn.step");
        roundTrip.Version.Should().Be("4");
        roundTrip.Sections.Should().ContainSingle();
        roundTrip.Sections[0].Fields[0].Type.Should().Be(ModelingPropertyFieldType.EntityReference);
        roundTrip.Sections[0].Fields[0].Lookup!.SearchRequired.Should().BeFalse();
        roundTrip.Sections[0].Fields[1].ItemFields[0].MaxLength.Should().Be(40);
    }

    [Fact]
    public void Lookup_result_round_trips_and_carries_the_display_snapshot_fields()
    {
        var result = new ModelingLookupResultDto
        {
            TotalCount = 3,
            HasMore = true,
            ContinuationToken = "cursor-2",
            Items =
            [
                new ModelingLookupItemDto
                {
                    RefType = "role",
                    RefId = "role-42",
                    Display = "Risk owner",
                    Description = "Owns the risk register",
                    SecondaryText = "Sales • CZ"
                }
            ]
        };

        var json = JsonSerializer.Serialize(result, WireOptions);
        var roundTrip = JsonSerializer.Deserialize<ModelingLookupResultDto>(json, WireOptions)!;

        roundTrip.TotalCount.Should().Be(3);
        roundTrip.HasMore.Should().BeTrue();
        roundTrip.ContinuationToken.Should().Be("cursor-2");
        roundTrip.Items.Should().ContainSingle();
        roundTrip.Items[0].RefType.Should().Be("role");
        roundTrip.Items[0].RefId.Should().Be("role-42");
        roundTrip.Items[0].Display.Should().Be("Risk owner");
        roundTrip.Items[0].SecondaryText.Should().Be("Sales • CZ");
    }

    [Fact]
    public void Schema_provider_interface_is_resolved_by_provider_key()
    {
        var provider = new StubSchemaProvider();

        provider.ProviderKey.Should().Be("tempo.tests.schema");
        provider.GetSchemaAsync(new ModelingPropertySchemaRequest { ModelId = "m", SemanticType = "Task" }, CancellationToken.None)
            .GetAwaiter().GetResult()!
            .SemanticType.Should().Be("Task");
    }

    [Fact]
    public void Lookup_provider_interface_is_resolved_by_provider_key()
    {
        var provider = new StubLookupProvider();

        provider.ProviderKey.Should().Be("tempo.tests.schema");
        provider.LookupAsync(new ModelingLookupRequest { LookupType = "role", SearchText = "ris" }, CancellationToken.None)
            .GetAwaiter().GetResult()
            .Items.Should().ContainSingle(item => item.RefId == "role-42");
    }

    private sealed class StubSchemaProvider : IModelingPropertySchemaProvider
    {
        public string ProviderKey => "tempo.tests.schema";

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(ModelingPropertySchemaRequest request, CancellationToken cancellationToken)
            => Task.FromResult<ModelingPropertySchemaDto?>(new ModelingPropertySchemaDto
            {
                SchemaId = "stub",
                Version = "1",
                SemanticType = request.SemanticType
            });
    }

    private sealed class StubLookupProvider : IModelingLookupProvider
    {
        public string ProviderKey => "tempo.tests.schema";

        public Task<ModelingLookupResultDto> LookupAsync(ModelingLookupRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ModelingLookupResultDto
            {
                Items =
                [
                    new ModelingLookupItemDto { RefType = request.LookupType, RefId = "role-42", Display = "Risk owner" }
                ],
                TotalCount = 1
            });
    }

    [Fact]
    public void A_json_element_default_value_round_trips()
    {
        var schema = new ModelingPropertySchemaDto
        {
            SchemaId = "defaults",
            Version = "1",
            Sections =
            [
                new ModelingPropertySectionDto
                {
                    Key = "main",
                    Label = "Main",
                    Fields =
                    [
                        new ModelingPropertyFieldDto
                        {
                            Key = "priority",
                            Label = "Priority",
                            DefaultValue = JsonSerializer.SerializeToElement("normal")
                        }
                    ]
                }
            ]
        };

        var roundTrip = JsonSerializer.Deserialize<ModelingPropertySchemaDto>(
            JsonSerializer.Serialize(schema, WireOptions), WireOptions)!;

        roundTrip.Sections[0].Fields[0].DefaultValue!.Value.GetString().Should().Be("normal");
    }

    // ── Review A1 / A10: the resolve request and the typed reference ─────────

    [Fact]
    public void The_resolve_request_defaults_to_an_empty_reference_list()
    {
        var request = new ModelingLookupResolveRequest();

        request.ModelId.Should().BeEmpty();
        request.Culture.Should().BeEmpty();
        request.References.Should().BeEmpty();
    }

    [Fact]
    public async Task A_lookup_provider_without_resolve_support_answers_null()
    {
        // The default interface member (review A1): an existing host compiles and "cannot re-resolve",
        // which the inspector renders as NO hint rather than as a stale reference.
        IModelingLookupProvider provider = new StubLookupProvider();

        var result = await provider.ResolveAsync(new ModelingLookupResolveRequest(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public void The_typed_reference_reads_and_writes_the_documented_shape()
    {
        var json = JsonSerializer.SerializeToElement(new
        {
            type = "role",
            id = "role-42",
            display = "Risk owner",
            snapshotAt = "2026-11-06T09:24:33Z"
        });

        ModelingEntityReference.TryParse(json, out var reference).Should().BeTrue();
        reference.Type.Should().Be("role");
        reference.Id.Should().Be("role-42");
        reference.Display.Should().Be("Risk owner");
        reference.SnapshotAt.Should().Be("2026-11-06T09:24:33Z");
        reference.Label.Should().Be("Risk owner");

        var roundTrip = reference.ToJsonElement();
        roundTrip.GetProperty("type").GetString().Should().Be("role");
        roundTrip.GetProperty("id").GetString().Should().Be("role-42");
        roundTrip.GetProperty("display").GetString().Should().Be("Risk owner");
        roundTrip.GetProperty("snapshotAt").GetString().Should().Be("2026-11-06T09:24:33Z");
    }

    [Fact]
    public void The_typed_reference_requires_type_and_id_and_omits_an_unknown_snapshot()
    {
        ModelingEntityReference.TryParse(JsonSerializer.SerializeToElement(new { type = "role" }), out _)
            .Should().BeFalse();
        ModelingEntityReference.TryParse(JsonSerializer.SerializeToElement("role-42"), out _)
            .Should().BeFalse();
        ModelingEntityReference.TryParse(JsonSerializer.SerializeToElement(new { type = "role", id = "r1" }))
            .Should().NotBeNull();

        var value = new ModelingEntityReference("role", "r1", "Risk owner", null).ToJsonElement();
        value.TryGetProperty("snapshotAt", out _).Should().BeFalse("an unknown snapshot stays absent");
        new ModelingEntityReference("role", "r1", "", null).Label.Should().Be("r1");
    }

    [Fact]
    public void A_picked_lookup_item_becomes_a_reference_with_a_fresh_snapshot()
    {
        var item = new ModelingLookupItemDto { RefType = "system", RefId = "sys-erp", Display = "ERP" };

        var reference = ModelingEntityReference.FromLookupItem(item, new DateTimeOffset(2026, 11, 6, 9, 24, 33, TimeSpan.Zero));

        reference.Type.Should().Be("system");
        reference.Id.Should().Be("sys-erp");
        reference.Display.Should().Be("ERP");
        reference.SnapshotAt.Should().Be("2026-11-06T09:24:33Z");
    }
}
