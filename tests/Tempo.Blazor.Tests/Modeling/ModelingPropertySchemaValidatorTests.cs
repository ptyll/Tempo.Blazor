using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 5 / decision <c>MODELING-PROPERTY-SCHEMA</c>: the SHARED schema validator
/// (<see cref="ModelingPropertySchemaValidator"/>). It is the one rule set the inspector, the
/// editor's save gate and <see cref="ModelingOperationEngine"/> all call, so MCP and the UI reject
/// the identical value with the identical issue (mandatory rule "one semantics, same errors").
/// <para>
/// Every issue carries a MACHINE-READABLE identity: <c>Id = "&lt;code&gt;:&lt;path&gt;"</c> where the
/// code is <c>required</c>, <c>type</c>, <c>range</c>, <c>length</c>, <c>pattern</c>, <c>enum</c> or
/// <c>reference</c> for errors and <c>unknown</c> for an Info-level host-added key. The path is the
/// property key, or <c>key[index].itemKey</c> inside a structured list.
/// </para>
/// </summary>
public sealed class ModelingPropertySchemaValidatorTests
{
    private static readonly ModelingPropertySchemaValidator Validator = new();

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static ModelingPropertyFieldDto Field(
        string key,
        ModelingPropertyFieldType type = ModelingPropertyFieldType.String,
        bool required = false,
        string? label = null,
        double? min = null,
        double? max = null,
        int? maxLength = null,
        string? pattern = null,
        List<ModelingPropertyOptionDto>? options = null,
        List<ModelingPropertyFieldDto>? itemFields = null)
        => new()
        {
            Key = key,
            Label = label ?? key,
            Type = type,
            Required = required,
            Min = min,
            Max = max,
            MaxLength = maxLength,
            Pattern = pattern,
            Options = options ?? [],
            ItemFields = itemFields ?? []
        };

    private static ModelingPropertySchemaDto Schema(params ModelingPropertyFieldDto[] fields)
        => new()
        {
            SchemaId = "test-schema",
            Version = "1",
            Notation = "bpmn",
            SemanticType = "Task",
            Sections =
            [
                new ModelingPropertySectionDto
                {
                    Key = "main",
                    Label = "Main",
                    Fields = [.. fields]
                }
            ]
        };

    private static Dictionary<string, JsonElement> Properties(params (string Key, object? Value)[] values)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            properties[key] = value is JsonElement element
                ? element
                : JsonSerializer.SerializeToElement(value);
        }

        return properties;
    }

    private static void HasSingleIssue(
        IReadOnlyList<ModelingIssueDto> issues,
        string expectedId,
        ModelingIssueSeverity severity = ModelingIssueSeverity.Error)
    {
        issues.Should().ContainSingle();
        issues[0].Id.Should().Be(expectedId);
        issues[0].Severity.Should().Be(severity);
        issues[0].Category.Should().Be("property");
    }

    // ── Required / type ──────────────────────────────────────────────────────

    [Fact]
    public void Required_missing_field_is_an_error()
    {
        var schema = Schema(Field("sla", ModelingPropertyFieldType.Integer, required: true, label: "SLA"));

        var issues = Validator.Validate(schema, Properties());

        HasSingleIssue(issues, "required:sla");
        issues[0].SuggestedFix.Should().Be("SLA");
        issues[0].Message.Should().Contain("SLA").And.Contain("required");
    }

    [Fact]
    public void Required_null_and_empty_string_are_missing_but_a_real_value_passes()
    {
        var schema = Schema(Field("instructions", required: true));

        Validator.Validate(schema, Properties(("instructions", (string?)null)))
            .Should().ContainSingle(issue => issue.Id == "required:instructions");
        Validator.Validate(schema, Properties(("instructions", "   ")))
            .Should().ContainSingle(issue => issue.Id == "required:instructions");
        Validator.Validate(schema, Properties(("instructions", "Do the thing")))
            .Should().BeEmpty();
    }

    [Fact]
    public void Optional_missing_field_is_fine()
    {
        var schema = Schema(Field("sla", ModelingPropertyFieldType.Integer));

        Validator.Validate(schema, Properties()).Should().BeEmpty();
    }

    [Fact]
    public void Integer_rejects_a_fraction_but_number_accepts_it()
    {
        var schema = Schema(
            Field("sla", ModelingPropertyFieldType.Integer),
            Field("weight", ModelingPropertyFieldType.Number));

        Validator.Validate(schema, Properties(("sla", 1.5), ("weight", 1.5)))
            .Should().ContainSingle(issue => issue.Id == "type:sla");
        Validator.Validate(schema, Properties(("sla", 3), ("weight", 3)))
            .Should().BeEmpty();
    }

    [Fact]
    public void String_field_rejects_a_number_and_boolean_field_rejects_a_string()
    {
        var schema = Schema(
            Field("name", ModelingPropertyFieldType.String),
            Field("automatic", ModelingPropertyFieldType.Boolean));

        var issues = Validator.Validate(schema, Properties(("name", 42), ("automatic", "yes")));

        issues.Select(issue => issue.Id).Should().BeEquivalentTo(["type:name", "type:automatic"]);
    }

    [Fact]
    public void Boolean_accepts_true_and_false_json()
    {
        var schema = Schema(Field("automatic", ModelingPropertyFieldType.Boolean));

        Validator.Validate(schema, Properties(("automatic", true))).Should().BeEmpty();
        Validator.Validate(schema, Properties(("automatic", false))).Should().BeEmpty();
    }

    // ── Range / length / pattern / enum ──────────────────────────────────────

    [Fact]
    public void Numeric_min_and_max_are_enforced_with_a_range_issue()
    {
        var schema = Schema(Field("sla", ModelingPropertyFieldType.Integer, min: 15, max: 240, label: "SLA"));

        Validator.Validate(schema, Properties(("sla", 10))).Should().ContainSingle(issue => issue.Id == "range:sla");
        Validator.Validate(schema, Properties(("sla", 241))).Should().ContainSingle(issue => issue.Id == "range:sla");
        Validator.Validate(schema, Properties(("sla", 60))).Should().BeEmpty();
    }

    [Fact]
    public void Max_length_is_enforced_for_text_fields()
    {
        var schema = Schema(Field("code", maxLength: 3));

        Validator.Validate(schema, Properties(("code", "ab"))).Should().BeEmpty();
        Validator.Validate(schema, Properties(("code", "abcd")))
            .Should().ContainSingle(issue => issue.Id == "length:code");
    }

    [Fact]
    public void Pattern_mismatch_is_a_pattern_issue_and_a_broken_host_pattern_is_ignored()
    {
        var schema = Schema(Field("code", pattern: "^[A-Z]{3}$"));

        Validator.Validate(schema, Properties(("code", "ABC"))).Should().BeEmpty();
        Validator.Validate(schema, Properties(("code", "abc")))
            .Should().ContainSingle(issue => issue.Id == "pattern:code");

        var broken = Schema(Field("code", pattern: "(["));
        broken.Sections[0].Fields[0].Pattern.Should().Be("([");
        Validator.Validate(broken, Properties(("code", "abc"))).Should().BeEmpty();
    }

    [Fact]
    public void Enum_accepts_only_an_offered_option_value()
    {
        var schema = Schema(Field(
            "priority",
            ModelingPropertyFieldType.Enum,
            options:
            [
                new() { Value = "low", Label = "Low" },
                new() { Value = "high", Label = "High" }
            ]));

        Validator.Validate(schema, Properties(("priority", "low"))).Should().BeEmpty();
        Validator.Validate(schema, Properties(("priority", "urgent")))
            .Should().ContainSingle(issue => issue.Id == "enum:priority");
        Validator.Validate(schema, Properties(("priority", 1)))
            .Should().ContainSingle(issue => issue.Id == "enum:priority");
    }

    // ── Dates / duration / url ───────────────────────────────────────────────

    [Fact]
    public void Date_and_date_time_require_their_round_trip_wire_formats()
    {
        var schema = Schema(
            Field("due", ModelingPropertyFieldType.Date),
            Field("started", ModelingPropertyFieldType.DateTime));

        Validator.Validate(schema, Properties(("due", "2026-11-06"), ("started", "2026-11-06T09:24:33+01:00")))
            .Should().BeEmpty();
        Validator.Validate(schema, Properties(("due", "06.11.2026")))
            .Should().ContainSingle(issue => issue.Id == "type:due");
        Validator.Validate(schema, Properties(("started", "not-a-date")))
            .Should().ContainSingle(issue => issue.Id == "type:started");
    }

    [Theory]
    [InlineData("2026-11-06T09:24:33Z", true)]
    [InlineData("2026-11-06T09:24:33+01:00", true)]
    [InlineData("2026-11-06T09:24:33-05:00", true)]
    [InlineData("2026-11-06T09:24:33.1234567+01:00", true)]
    [InlineData("2026-11-06T09:24:33", false)]
    [InlineData("2026-11-06T09:24:33.1234567", false)]
    [InlineData("2026-11-06", false)]
    [InlineData("11/06/2026 9:00", false)]
    [InlineData("2026-11-06 09:24:33Z", false)]
    public void A_date_time_requires_an_iso_literal_with_an_explicit_offset(string value, bool accepted)
    {
        // Review A13: anchored ISO only, and the offset is mandatory (Z or ±hh:mm) — a value without
        // a zone would be re-interpreted in the reader's own time zone.
        var schema = Schema(Field("started", ModelingPropertyFieldType.DateTime));

        var issues = Validator.Validate(schema, Properties(("started", value)));

        if (accepted)
        {
            issues.Should().BeEmpty();
        }
        else
        {
            issues.Should().ContainSingle(issue => issue.Id == "type:started");
        }
    }

    [Fact]
    public void Duration_is_stored_as_integer_minutes()
    {
        var schema = Schema(Field("sla", ModelingPropertyFieldType.Duration, min: 15, max: 480, label: "SLA"));

        Validator.Validate(schema, Properties(("sla", 240))).Should().BeEmpty();
        Validator.Validate(schema, Properties(("sla", 1.5))).Should().ContainSingle(issue => issue.Id == "type:sla");
        Validator.Validate(schema, Properties(("sla", 5))).Should().ContainSingle(issue => issue.Id == "range:sla");
    }

    [Fact]
    public void Url_requires_an_absolute_http_uri()
    {
        var schema = Schema(Field("link", ModelingPropertyFieldType.Url));

        Validator.Validate(schema, Properties(("link", "https://example.com/x"))).Should().BeEmpty();
        Validator.Validate(schema, Properties(("link", "example.com/x")))
            .Should().ContainSingle(issue => issue.Id == "pattern:link");
    }

    // ── Entity references (§5.4) ─────────────────────────────────────────────

    [Fact]
    public void Entity_reference_requires_type_and_id_strings()
    {
        var schema = Schema(Field("role", ModelingPropertyFieldType.EntityReference, label: "Role"));

        Validator.Validate(
                schema,
                Properties(("role", JsonSerializer.SerializeToElement(new { type = "role", id = "role-42", display = "Risk owner" }))))
            .Should().BeEmpty();

        Validator.Validate(schema, Properties(("role", JsonSerializer.SerializeToElement(new { type = "role" }))))
            .Should().ContainSingle(issue => issue.Id == "reference:role");
        Validator.Validate(schema, Properties(("role", "role-42")))
            .Should().ContainSingle(issue => issue.Id == "reference:role");
    }

    [Fact]
    public void Entity_reference_list_validates_every_item()
    {
        var schema = Schema(Field("systems", ModelingPropertyFieldType.EntityReferenceList));

        Validator.Validate(
                schema,
                Properties((
                    "systems",
                    JsonSerializer.SerializeToElement(new object[]
                    {
                        new { type = "system", id = "erp", display = "ERP" },
                        new { type = "system", id = "wms", display = "WMS" }
                    }))))
            .Should().BeEmpty();

        var issues = Validator.Validate(
            schema,
            Properties((
                "systems",
                JsonSerializer.SerializeToElement(new object[]
                {
                    new { type = "system", id = "erp" },
                    new { type = "system" }
                }))));

        issues.Select(issue => issue.Id).Should().Equal(["reference:systems[1]"]);
    }

    [Fact]
    public void String_list_accepts_only_strings()
    {
        var schema = Schema(Field("tags", ModelingPropertyFieldType.StringList));

        Validator.Validate(schema, Properties(("tags", JsonSerializer.SerializeToElement(new[] { "a", "b" }))))
            .Should().BeEmpty();
        Validator.Validate(schema, Properties(("tags", JsonSerializer.SerializeToElement(new object[] { "a", 3 }))))
            .Should().ContainSingle(issue => issue.Id == "type:tags[1]");
        Validator.Validate(schema, Properties(("tags", "a")))
            .Should().ContainSingle(issue => issue.Id == "type:tags");
    }

    // ── Structured list (§5.2) ───────────────────────────────────────────────

    [Fact]
    public void Structured_list_validates_each_item_against_its_item_fields()
    {
        var schema = Schema(Field(
            "exceptions",
            ModelingPropertyFieldType.StructuredList,
            label: "Exceptions",
            itemFields:
            [
                Field("code", required: true, label: "Code"),
                Field("severity", ModelingPropertyFieldType.Enum, options: [new() { Value = "warning", Label = "Warning" }])
            ]));

        Validator.Validate(
                schema,
                Properties((
                    "exceptions",
                    JsonSerializer.SerializeToElement(new object[] { new { code = "E-1", severity = "warning" } }))))
            .Should().BeEmpty();

        var issues = Validator.Validate(
            schema,
            Properties((
                "exceptions",
                JsonSerializer.SerializeToElement(new object[] { new { severity = "warning" }, new { code = "E-2", severity = "blocker" } }))));

        issues.Select(issue => issue.Id).Should().Equal(["required:exceptions[0].code", "enum:exceptions[1].severity"]);
        issues[0].SuggestedFix.Should().Be("Exceptions");
        issues[0].Message.Should().Contain("Code");
    }

    [Fact]
    public void Structured_list_rejects_a_non_array_value_and_non_object_items()
    {
        var schema = Schema(Field(
            "exceptions",
            ModelingPropertyFieldType.StructuredList,
            itemFields: [Field("code", required: true, label: "Code")]));

        Validator.Validate(schema, Properties(("exceptions", "nope")))
            .Should().ContainSingle(issue => issue.Id == "type:exceptions");
        Validator.Validate(schema, Properties(("exceptions", JsonSerializer.SerializeToElement(new[] { "nope" }))))
            .Should().ContainSingle(issue => issue.Id == "type:exceptions[0]");
    }

    [Fact]
    public void Required_list_treats_an_empty_array_as_missing()
    {
        var schema = Schema(Field("systems", ModelingPropertyFieldType.EntityReferenceList, required: true, label: "Systems"));

        Validator.Validate(schema, Properties(("systems", JsonSerializer.SerializeToElement(Array.Empty<object>()))))
            .Should().ContainSingle(issue => issue.Id == "required:systems");
    }

    // ── Unknown keys (host-added properties stay allowed) ────────────────────

    [Fact]
    public void Unknown_properties_are_reported_as_info_not_as_errors()
    {
        var schema = Schema(Field("instructions"));

        var issues = Validator.Validate(schema, Properties(("instructions", "x"), ("stencilId", "bpmn2.task.user")));

        HasSingleIssue(issues, "unknown:stencilId", ModelingIssueSeverity.Info);
        issues[0].SuggestedFix.Should().Be("stencilId");
    }

    [Fact]
    public void Unknown_issues_can_be_suppressed_when_the_caller_only_cares_about_its_own_keys()
    {
        var schema = Schema(Field("instructions"));

        // The editor's field-level pass only wants the touched keys; an info row per host key on
        // every selection would drown the inspector. The public API is fixed at the doc's shape,
        // so the suppression is the caller's job: filter by Severity.
        var errors = Validator
            .Validate(schema, Properties(("stencilId", "x"), ("tags", JsonSerializer.SerializeToElement(Array.Empty<string>()))))
            .Where(issue => issue.Severity == ModelingIssueSeverity.Error)
            .ToArray();

        errors.Should().BeEmpty();
    }

    // ── Schema shape robustness ──────────────────────────────────────────────

    [Fact]
    public void An_empty_schema_reports_only_unknown_keys()
    {
        var schema = new ModelingPropertySchemaDto { SchemaId = "empty", Version = "1" };

        Validator.Validate(schema, Properties()).Should().BeEmpty();
        Validator.Validate(schema, Properties(("custom", "value")))
            .Should().ContainSingle(issue => issue.Id == "unknown:custom");
    }

    [Fact]
    public void Field_order_and_duplicate_keys_never_produce_duplicate_required_issues()
    {
        var schema = Schema(Field("sla", ModelingPropertyFieldType.Integer, required: true, label: "SLA"));
        schema.Sections.Add(new ModelingPropertySectionDto
        {
            Key = "second",
            Label = "Second",
            Fields = [Field("sla", ModelingPropertyFieldType.Integer, required: true, label: "SLA")]
        });

        Validator.Validate(schema, Properties()).Should().ContainSingle();
    }

    [Fact]
    public void A_phrase_message_names_the_field_label_not_the_key()
    {
        var schema = Schema(Field("slaMinutes", ModelingPropertyFieldType.Integer, min: 15, max: 240, label: "SLA"));

        var issues = Validator.Validate(schema, Properties(("slaMinutes", 1)));

        issues.Should().ContainSingle();
        issues[0].Message.Should().Contain("SLA").And.NotContain("slaMinutes");
    }
}
