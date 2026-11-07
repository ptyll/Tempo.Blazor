using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 5 / decision <c>MODELING-PROPERTY-SCHEMA</c> §5.5/§6.4: the ENGINE half of the shared
/// property validation. The engine is the MCP path, so these tests pin that
/// <c>modeling_apply_operations</c> rejects exactly the value the inspector would reject, with the
/// documented <c>property_*</c> codes and the field in <c>PropertyKey</c>.
/// <para>
/// The resolver arrives through <see cref="ModelingOperationContext.ResolvePropertySchema"/> as a
/// <see cref="Func{ModelingPropertySchemaRequest, ModelingPropertySchemaDto}"/> over the FULL request
/// (§5.1); a relationship is judged with its RELATIONSHIP type as the semantic type. A null resolver
/// (the 2.10.0 default) validates nothing at all. A new/re-typed item's full property set is judged
/// AFTER the batch (review A4), so <c>[add_element, set_properties]</c> in one batch succeeds while a
/// genuinely missing field reports at the add.
/// </para>
/// </summary>
public sealed class ModelingOperationEnginePropertyTests
{
    private const string TaskInstructions = "Instructions";

    private static readonly ModelingPropertySchemaDto TaskSchema = new()
    {
        SchemaId = "test.bpmn.task",
        Version = "1",
        Notation = "bpmn",
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
                        Label = TaskInstructions,
                        Type = ModelingPropertyFieldType.Markdown,
                        Required = true,
                        MaxLength = 2000
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "sla",
                        Label = "SLA",
                        Type = ModelingPropertyFieldType.Integer,
                        Min = 15,
                        Max = 240
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "priority",
                        Label = "Priority",
                        Type = ModelingPropertyFieldType.Enum,
                        Options =
                        [
                            new() { Value = "low", Label = "Low" },
                            new() { Value = "high", Label = "High" }
                        ]
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "code",
                        Label = "Code",
                        Type = ModelingPropertyFieldType.String,
                        Pattern = "^[A-Z]{2}-[0-9]+$"
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "role",
                        Label = "Role",
                        Type = ModelingPropertyFieldType.EntityReference
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
                                Required = true
                            }
                        ]
                    }
                ]
            }
        ]
    };

    private static readonly ModelingPropertySchemaDto FlowSchema = new()
    {
        SchemaId = "test.bpmn.flow",
        Version = "1",
        Notation = "bpmn",
        SemanticType = "SequenceFlow",
        Sections =
        [
            new ModelingPropertySectionDto
            {
                Key = "flow",
                Label = "Flow",
                Fields =
                [
                    new ModelingPropertyFieldDto
                    {
                        Key = "condition",
                        Label = "Condition",
                        Type = ModelingPropertyFieldType.String,
                        Required = true
                    }
                ]
            }
        ]
    };

    // ── add_element ─────────────────────────────────────────────────────────

    [Fact]
    public void Add_element_with_a_missing_required_property_fails_with_property_required()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            Op("add_element", ("id", "task-9"), ("name", "Check stock"), ("semanticType", "Task")),
            TaskSchema);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be("property_required");
        result.Errors[0].Op.Should().Be("add_element");
        result.Errors[0].Index.Should().Be(0);
        result.Errors[0].PropertyKey.Should().Be("instructions");
        result.Errors[0].SuggestedFix.Should().Be(TaskInstructions);
        result.Errors[0].Message.Should().StartWith("operations[0] add_element: ")
            .And.Contain(TaskInstructions);
        result.Errors[0].ElementId.Should().Be("task-9");
        // Review A4: the full property set is judged AFTER the batch (so a follow-up set_properties in
        // the SAME batch can still satisfy it), therefore the failed batch MAY have applied the add to
        // the passed instance — the documented atomic-caller contract is "apply to a Clone and swap on
        // Success", which is what the MCP tool and the demo writer both do.
    }

    [Fact]
    public void Add_element_with_valid_properties_succeeds()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            Op(
                "add_element",
                ("id", "task-9"),
                ("name", "Check stock"),
                ("semanticType", "Task"),
                ("properties", """{ "instructions": "Check the stock level", "sla": 60 }""")),
            TaskSchema);

        result.Success.Should().BeTrue();
        var added = model.Elements.Single(element => element.Id == "task-9");
        added.Properties["instructions"].GetString().Should().Be("Check the stock level");
        added.Properties["sla"].GetInt32().Should().Be(60);
    }

    [Fact]
    public void Add_element_maps_every_validator_code_onto_its_property_star_engine_code()
    {
        var model = CreateModel();

        var outOfRange = Apply(
            model,
            Op("add_element", ("name", "A"), ("semanticType", "Task"), ("properties", """{ "instructions": "x", "sla": 5 }""")),
            TaskSchema);
        outOfRange.Errors[0].Code.Should().Be("property_range");
        outOfRange.Errors[0].PropertyKey.Should().Be("sla");

        var badEnum = Apply(
            model,
            Op("add_element", ("name", "B"), ("semanticType", "Task"), ("properties", """{ "instructions": "x", "priority": "urgent" }""")),
            TaskSchema);
        badEnum.Errors[0].Code.Should().Be("property_enum");
        badEnum.Errors[0].PropertyKey.Should().Be("priority");

        var badPattern = Apply(
            model,
            Op("add_element", ("name", "C"), ("semanticType", "Task"), ("properties", """{ "instructions": "x", "code": "nope" }""")),
            TaskSchema);
        badPattern.Errors[0].Code.Should().Be("property_pattern");
        badPattern.Errors[0].PropertyKey.Should().Be("code");

        var badType = Apply(
            model,
            Op("add_element", ("name", "D"), ("semanticType", "Task"), ("properties", """{ "instructions": 42 }""")),
            TaskSchema);
        badType.Errors[0].Code.Should().Be("property_type");

        var badReference = Apply(
            model,
            Op("add_element", ("name", "E"), ("semanticType", "Task"), ("properties", """{ "instructions": "x", "role": "role-42" }""")),
            TaskSchema);
        badReference.Errors[0].Code.Should().Be("property_reference");
        badReference.Errors[0].PropertyKey.Should().Be("role");

        var tooLong = Apply(
            model,
            Op("add_element", ("name", "F"), ("semanticType", "Task"), ("properties", $$"""{ "instructions": "{{new string('x', 2001)}}" }""")),
            TaskSchema);
        tooLong.Errors[0].Code.Should().Be("property_range");
        tooLong.Errors[0].PropertyKey.Should().Be("instructions");

        var badItem = Apply(
            model,
            Op("add_element", ("name", "G"), ("semanticType", "Task"), ("properties", """{ "instructions": "x", "exceptions": [ { "description": "no code" } ] }""")),
            TaskSchema);
        badItem.Errors[0].Code.Should().Be("property_required");
        badItem.Errors[0].PropertyKey.Should().Be("exceptions[0].code");
    }

    // ── update_element / set_properties ─────────────────────────────────────

    [Fact]
    public void Update_element_validates_only_the_keys_it_touches()
    {
        // task-1 carries NO instructions (a legacy value the schema never saw): patching an unrelated
        // field must not be blocked by that — otherwise an MCP client could never repair a model.
        var model = CreateModel();
        model.Elements.Single(element => element.Id == "task-1").Properties["sla"] =
            JsonSerializer.SerializeToElement(1);

        var untouchedPatch = Apply(
            model,
            Op("update_element", ("id", "task-1"), ("name", "Renamed")),
            TaskSchema);

        untouchedPatch.Success.Should().BeTrue("an untouched invalid legacy value never blocks a targeted fix");
        model.Elements.Single(element => element.Id == "task-1").Name.Should().Be("Renamed");

        var touchedPatch = Apply(
            model,
            Op("update_element", ("id", "task-1"), ("properties", """{ "sla": 2 }""")),
            TaskSchema);

        touchedPatch.Success.Should().BeFalse();
        touchedPatch.Errors[0].Code.Should().Be("property_range");
        touchedPatch.Errors[0].PropertyKey.Should().Be("sla");
    }

    [Fact]
    public void Update_element_removing_a_required_property_fails()
    {
        var model = CreateModel();
        model.Elements.Single(element => element.Id == "task-1").Properties["instructions"] =
            JsonSerializer.SerializeToElement("Do it");

        var result = Apply(
            model,
            Op("update_element", ("id", "task-1"), ("removedProperties", """["instructions"]""")),
            TaskSchema);

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("property_required");
        result.Errors[0].PropertyKey.Should().Be("instructions");
        model.Elements.Single(element => element.Id == "task-1").Properties.Should().ContainKey("instructions");
    }

    [Fact]
    public void Update_element_that_retypes_the_element_validates_the_full_property_set()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            Op("update_element", ("id", "task-1"), ("semanticType", "Task")),
            TaskSchema);

        result.Success.Should().BeFalse("re-typing judges the whole set, so the missing required field fails");
        result.Errors[0].Code.Should().Be("property_required");
    }

    [Fact]
    public void Set_properties_validates_element_and_relationship_values()
    {
        var model = CreateModel();

        var elementResult = Apply(
            model,
            Op("set_properties", ("id", "task-1"), ("properties", """{ "priority": "high" }""")),
            TaskSchema);
        elementResult.Success.Should().BeTrue();
        model.Elements.Single(element => element.Id == "task-1").Properties["priority"].GetString().Should().Be("high");

        var relationshipResult = Apply(
            model,
            Op("set_properties", ("id", "flow-1"), ("properties", """{ "condition": "" }""")),
            FlowSchema);
        relationshipResult.Success.Should().BeFalse("a required relation field set to an empty string is missing");
        relationshipResult.Errors[0].Code.Should().Be("property_required");
        relationshipResult.Errors[0].PropertyKey.Should().Be("condition");
        relationshipResult.Errors[0].RelationshipId.Should().Be("flow-1");
    }

    // ── relationships ───────────────────────────────────────────────────────

    [Fact]
    public void Add_relationship_is_validated_with_its_relationship_type_as_the_semantic_type()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            Op("add_relationship", ("id", "flow-9"), ("relationshipType", "SequenceFlow"), ("sourceElementId", "task-1"), ("targetElementId", "task-2")),
            FlowSchema);

        result.Success.Should().BeFalse();
        result.Errors[0].Code.Should().Be("property_required");
        result.Errors[0].PropertyKey.Should().Be("condition");
        result.Errors[0].RelationshipId.Should().Be("flow-9");
    }

    // ── no schema (the 2.10.0 behavior) ─────────────────────────────────────

    [Fact]
    public void Without_a_resolver_no_property_value_is_validated()
    {
        var model = CreateModel();

        var result = ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch
            {
                Operations = [Op("add_element", ("id", "task-9"), ("name", "No schema"), ("semanticType", "Task"))]
            },
            new ModelingOperationContext { RelationshipRules = new FlowRules() });

        result.Success.Should().BeTrue("the 2.10.0 contract validates no property values");
    }

    [Fact]
    public void A_resolver_returning_no_schema_for_the_pair_validates_nothing()
    {
        var model = CreateModel();

        var result = Apply(
            model,
            Op("add_element", ("id", "task-9"), ("name", "No schema"), ("semanticType", "Task")),
            schema: null);

        result.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("Task", "bpmn", true)]
    [InlineData("task", "bpmn", false)]
    [InlineData("Task", "umb", true)]
    public void The_resolver_receives_the_element_notation_and_semantic_type(string semanticType, string notation, bool expectedResolved)
    {
        var model = CreateModel();
        model.Notation = notation;
        ModelingPropertySchemaRequest? seen = null;

        ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch
            {
                Operations = [Op("add_element", ("name", "Probe"), ("semanticType", semanticType))]
            },
            new ModelingOperationContext
            {
                RelationshipRules = new FlowRules(),
                ResolvePropertySchema = request =>
                {
                    seen = request;
                    return request.SemanticType == "Task" ? TaskSchema : null;
                }
            });

        seen.Should().NotBeNull("the engine always builds a request for a judged pair");
        seen!.Notation.Should().Be(notation);
        seen.SemanticType.Should().Be(semanticType);
        expectedResolved.Should().Be(semanticType == "Task");
    }

    [Fact]
    public void The_resolver_request_carries_the_model_id_viewpoint_and_culture()
    {
        var model = CreateModel();
        model.Views = [new ModelingViewDto { Id = "v1", ViewpointKey = "process" }];
        ModelingPropertySchemaRequest? seen = null;

        ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch
            {
                Operations = [Op("add_element", ("name", "Probe"), ("semanticType", "Task"), ("properties", """{"instructions":"Do it"}"""))]
            },
            new ModelingOperationContext
            {
                RelationshipRules = new FlowRules(),
                ResolvePropertySchema = request =>
                {
                    seen = request;
                    return TaskSchema;
                }
            });

        seen.Should().NotBeNull();
        seen!.ModelId.Should().Be(model.Id);
        seen.ViewpointKey.Should().Be("process", "the engine falls back to the target view's viewpoint");
        seen.Culture.Should().Be(System.Globalization.CultureInfo.CurrentUICulture.Name);
        seen.SchemaVersion.Should().BeNull("the engine never pins a schema version");
    }

    [Fact]
    public void The_caller_viewpoint_override_wins_over_the_target_view()
    {
        var model = CreateModel();
        model.Views = [new ModelingViewDto { Id = "v1", ViewpointKey = "process" }];
        ModelingPropertySchemaRequest? seen = null;

        ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch
            {
                Operations = [Op("add_element", ("name", "Probe"), ("semanticType", "Task"), ("properties", """{"instructions":"Do it"}"""))]
            },
            new ModelingOperationContext
            {
                RelationshipRules = new FlowRules(),
                ViewpointKey = "application",
                ResolvePropertySchema = request =>
                {
                    seen = request;
                    return TaskSchema;
                }
            });

        seen!.ViewpointKey.Should().Be("application");
    }

    // ── A4: the full set is judged after the batch ──────────────────────────

    [Fact]
    public void An_add_followed_by_set_properties_in_one_batch_satisfies_the_required_field()
    {
        var model = CreateModel();
        var result = ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch
            {
                Operations =
                [
                    Op("add_element", ("id", "new-task"), ("name", "Draft"), ("semanticType", "Task")),
                    Op("set_properties", ("id", "new-task"), ("properties", """{"instructions":"Do it"}"""))
                ]
            },
            new ModelingOperationContext
            {
                RelationshipRules = new FlowRules(),
                ResolvePropertySchema = request => request.SemanticType == "Task" ? TaskSchema : null
            });

        result.Success.Should().BeTrue("the follow-up op in the SAME batch fills the required field");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void An_add_without_a_required_field_reports_property_required_at_the_add_index()
    {
        var model = CreateModel();
        var result = ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch
            {
                Operations =
                [
                    Op("add_element", ("id", "new-task"), ("name", "Draft"), ("semanticType", "Task")),
                    Op("add_element", ("id", "other-task"), ("name", "Other"), ("semanticType", "Task"), ("properties", """{"instructions":"Do that"}"""))
                ]
            },
            new ModelingOperationContext
            {
                RelationshipRules = new FlowRules(),
                ResolvePropertySchema = request => request.SemanticType == "Task" ? TaskSchema : null
            });

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Index.Should().Be(0, "the deferred finding reports at the operation that added the item");
        result.Errors[0].Op.Should().Be("add_element");
        result.Errors[0].Code.Should().Be("property_required");
        result.Errors[0].PropertyKey.Should().Be("instructions");
        result.Errors[0].Message.Should().StartWith("operations[0] add_element:");
    }

    [Fact]
    public void A_retyped_element_is_judged_in_full_after_the_batch()
    {
        var model = CreateModel();
        model.Elements.Add(new ModelingElementDto { Id = "task-1", Name = "One", SemanticType = "Task", Notation = "bpmn" });

        var result = ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch
            {
                Operations = [Op("update_element", ("id", "task-1"), ("semanticType", "Task"))]
            },
            new ModelingOperationContext
            {
                RelationshipRules = new FlowRules(),
                ResolvePropertySchema = request => request.SemanticType == "Task" ? TaskSchema : null
            });

        result.Success.Should().BeFalse("a re-typed item is judged against the NEW schema in full");
        result.Errors[0].Index.Should().Be(0);
        result.Errors[0].Code.Should().Be("property_required");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static ModelingOperationResult Apply(
        ModelingModelDto model,
        ModelingOperation operation,
        ModelingPropertySchemaDto? schema)
        => ModelingOperationEngine.Apply(
            model,
            new ModelingOperationBatch { Operations = [operation] },
            new ModelingOperationContext
            {
                RelationshipRules = new FlowRules(),
                ResolvePropertySchema = schema is null ? null : request =>
                    string.Equals(request.Notation, schema.Notation, StringComparison.Ordinal)
                    && string.Equals(request.SemanticType, schema.SemanticType, StringComparison.Ordinal)
                        ? schema
                        : null
            });

    private static ModelingOperation Op(string op, params (string Key, object? Value)[] fields)
    {
        var operation = new ModelingOperation { Op = op };
        foreach (var (key, value) in fields)
        {
            switch (key)
            {
                case "id": operation.Id = value as string; break;
                case "name": operation.Name = value as string; break;
                case "semanticType": operation.SemanticType = value as string; break;
                case "relationshipType": operation.RelationshipType = value as string; break;
                case "sourceElementId": operation.SourceElementId = value as string; break;
                case "targetElementId": operation.TargetElementId = value as string; break;
                case "properties": operation.Properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(value as string ?? "{}"); break;
                case "removedProperties": operation.RemovedProperties = JsonSerializer.Deserialize<List<string>>(value as string ?? "[]"); break;
                default: throw new ArgumentOutOfRangeException(nameof(fields), key, "Unmapped operation field.");
            }
        }

        return operation;
    }

    private static ModelingModelDto CreateModel()
        => new()
        {
            Id = "property-engine-model",
            Title = "Property engine",
            Notation = "bpmn",
            Elements =
            [
                new() { Id = "task-1", Notation = "bpmn", SemanticType = "Task", Name = "Approve order" },
                new() { Id = "task-2", Notation = "bpmn", SemanticType = "Task", Name = "Ship order" }
            ],
            Relationships =
            [
                new()
                {
                    Id = "flow-1",
                    RelationshipType = "SequenceFlow",
                    SourceElementId = "task-1",
                    TargetElementId = "task-2",
                    Name = "approve"
                }
            ]
        };

    /// <summary>A deliberately small BPMN-ish rule set (the engine must not depend on the real
    /// provider): SequenceFlow between two elements of the same model is fine.</summary>
    private sealed class FlowRules : IModelingRelationshipRulesProvider
    {
        public bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType)
            => true;

        public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
            => ModelingRelationshipRuleResult.Valid;
    }
}
