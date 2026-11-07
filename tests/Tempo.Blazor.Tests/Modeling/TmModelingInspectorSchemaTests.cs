using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 5 / decision <c>MODELING-PROPERTY-SCHEMA</c> §5.6: the schema-driven inspector surface.
/// The schema provider resolves per provider key in the EDITOR; this component only renders the
/// schema it was handed, commits typed values through the very same
/// <see cref="ModelingElementChange"/> / <see cref="ModelingRelationshipChange"/> contract and shows
/// the SHARED validator's issues inline.
/// <para>
/// The backward-compatibility contract is the first test: without <c>PropertySchema</c> the surface is
/// exactly the 2.10.0 one (scalars editable, complex values read-only).
/// </para>
/// </summary>
public sealed class TmModelingInspectorSchemaTests : LocalizationTestBase
{
    // ── Backward compatibility ───────────────────────────────────────────────

    [Fact]
    public void Without_a_schema_the_inspector_renders_the_legacy_surface()
    {
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false));

        cut.FindAll("[data-schema-section]").Should().BeEmpty("no schema registered = the 2.10.0 surface");
        cut.Find("[data-testid='modeling-inspector-property-lane']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-inspector-property-shape']")
            .QuerySelector("input, textarea, [role='switch']")
            .Should().BeNull("a complex JSON value stays read-only without a schema");
        cut.Find("[data-testid='modeling-inspector-properties'] h4").TextContent.Trim()
            .Should().Be("Properties", "the header keeps its 2.10.0 label");
    }

    [Fact]
    public void An_empty_schema_leaves_the_legacy_surface_intact()
    {
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, new ModelingPropertySchemaDto { SchemaId = "empty", Version = "1" }));

        cut.FindAll("[data-schema-section]").Should().BeEmpty();
        cut.Find("[data-testid='modeling-inspector-property-lane']").Should().NotBeNull();
    }

    // ── Sections and typed fields ────────────────────────────────────────────

    [Fact]
    public void Schema_sections_and_fields_render_with_their_labels_and_test_ids()
    {
        using var cut = RenderSchemaEditor(ReadOnly: false);

        cut.FindAll("[data-testid='modeling-inspector-schema-section-ownership']").Should().ContainSingle();
        cut.Find("[data-testid='modeling-inspector-schema-section-ownership'] h4").TextContent.Trim()
            .Should().Be("Ownership");

        var role = cut.Find("[data-testid='modeling-inspector-field-role']");
        role.GetAttribute("data-field-key").Should().Be("role");
        role.GetAttribute("data-field-type").Should().Be("entityreference");
        role.GetAttribute("data-required").Should().Be("true");

        // The typed controls: text (TmTextInput), enum (TmSelect), duration (text + unit select),
        // boolean (TmToggle), markdown (the Tempo markdown editor) and the entity reference
        // (TmMultiSelect — the APG combobox with the async lookup).
        cut.Find("#modeling-inspector-field-instructions .tm-markdown-editor").Should().NotBeNull();
        cut.Find("#modeling-inspector-field-priority").TagName.Should().Be("SELECT");
        cut.Find("#modeling-inspector-field-active").GetAttribute("role").Should().Be("switch");
        cut.Find("#modeling-inspector-field-sla").TagName.Should().Be("INPUT");
        cut.FindAll("[data-testid='modeling-inspector-field-sla'] .tm-modeling-inspector__duration-unit").Should().ContainSingle();
        cut.Find("[data-testid='modeling-inspector-field-role'] .tm-multiselect").Should().NotBeNull();
    }

    [Fact]
    public void Keys_outside_the_schema_move_to_the_other_properties_table()
    {
        using var cut = RenderSchemaEditor(ReadOnly: false);

        cut.Find("[data-testid='modeling-inspector-properties'] h4").TextContent.Trim().Should().Be("Other properties");
        cut.Find("[data-testid='modeling-inspector-property-lane']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-inspector-property-shape']")
            .QuerySelector("input, textarea, [role='switch']")
            .Should().BeNull("a non-schema complex value stays read-only");
    }

    [Fact]
    public void Read_only_host_renders_the_schema_as_formatted_values_without_inputs()
    {
        using var cut = RenderSchemaEditor(ReadOnly: true);

        cut.FindAll("[data-schema-section]").Should().NotBeEmpty("a read-only host still gets the schema labels");
        cut.Find("[data-testid='modeling-inspector-field-role']")
            .QuerySelector("input, select")
            .Should().BeNull();
        cut.Find("[data-testid='modeling-inspector-field-role-value']").TextContent.Trim().Should().Be("Sales");
        cut.Find("[data-testid='modeling-inspector-field-sla-value']").TextContent.Trim().Should().Be("4 h");
        cut.Find("[data-testid='modeling-inspector-field-priority-value']").TextContent.Trim().Should().Be("High");
        cut.Find("[data-testid='modeling-inspector-field-active-value']").TextContent.Trim().Should().Be("Yes");
        cut.Find("[data-testid='modeling-inspector-field-systems-value']").TextContent
            .Should().Contain("ERP").And.Contain("CRM");
    }

    [Fact]
    public void A_reference_field_without_a_lookup_provider_is_read_only()
    {
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema()));

        cut.Find("[data-testid='modeling-inspector-field-role']").QuerySelector(".tm-multiselect").Should().BeNull(
            "a reference cannot be picked without a catalogue");
        cut.Find("[data-testid='modeling-inspector-field-role-value']").Should().NotBeNull();
    }

    // ── Commits ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_text_field_commits_its_json_string_on_blur()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, changes: changes);

        // The Tempo markdown editor reads the contenteditable's markdown through its JS module
        // (js/activity/tm-markdown-editor.js) — the mock returns the typed text.
        JSInterop.Setup<string>("tmMarkdownEditor.getValue", _ => true).SetResult("Updated instructions");
        JSInterop.Setup<string[]>("tmMarkdownEditor.getActiveFormats", _ => true).SetResult([]);

        var editor = cut.Find("#modeling-inspector-field-instructions .tm-markdown-editor");
        var content = editor.QuerySelector("[contenteditable]");
        content.Should().NotBeNull("the markdown field renders the Tempo markdown editor");
        content!.Input("Updated instructions");
        // The markdown editor has no blur callback of its own: the commit rides the root's bubbling
        // focusout (leaving the editor commits, exactly like the text fields).
        editor.FocusOut();

        changes.Should().ContainSingle();
        changes[0].SetProperties!["instructions"].GetString().Should().Be("Updated instructions");
    }

    [Fact]
    public void A_number_field_commits_a_json_number_and_a_cleared_field_removes_the_property()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, element: CreateElement(("count", 3)), changes: changes);

        var count = cut.Find("#modeling-inspector-field-count");
        count.Input("42");
        count.Blur();

        changes.Should().ContainSingle();
        changes[0].SetProperties!["count"].GetInt32().Should().Be(42);
        changes.Clear();

        count.Input(string.Empty);
        count.Blur();

        changes.Should().ContainSingle();
        changes[0].RemovedProperties.Should().Equal("count");
    }

    [Fact]
    public void An_enum_field_commits_the_option_value()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, changes: changes);

        cut.Find("#modeling-inspector-field-priority").Change("low");

        changes.Should().ContainSingle();
        changes[0].SetProperties!["priority"].GetString().Should().Be("low");
    }

    [Fact]
    public void A_boolean_field_commits_the_json_boolean()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, changes: changes);

        cut.Find("#modeling-inspector-field-active").Change(false);

        changes.Should().ContainSingle();
        changes[0].SetProperties!["active"].GetBoolean().Should().BeFalse("the stored value was true");
    }

    [Fact]
    public void A_duration_stores_integer_minutes_whatever_unit_is_displayed()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, element: CreateElement(("sla", 240)), changes: changes);

        // The default unit is minutes: 240 stays 240.
        cut.FindAll("[data-testid='modeling-inspector-field-sla'] option")
            .Select(option => option.GetAttribute("value"))
            .Should().Equal("minutes", "hours", "days");

        // Switch the display unit to hours — the field then shows 4 and commits 4 h as 240 minutes.
        cut.Find("[data-testid='modeling-inspector-field-sla'] .tm-modeling-inspector__duration-unit")
            .Change("hours");
        var sla = cut.Find("#modeling-inspector-field-sla");
        sla.GetAttribute("value").Should().Be("4");
        sla.Input("2");
        sla.Blur();

        changes.Should().ContainSingle();
        changes[0].SetProperties!["sla"].GetInt64().Should().Be(120);
    }

    [Fact]
    public void A_structured_list_adds_and_removes_items_as_a_whole_array()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, changes: changes);

        cut.Find("[data-testid='modeling-inspector-field-exceptions-add']").Click();

        changes.Should().ContainSingle();
        var added = changes[0].SetProperties!["exceptions"];
        added.ValueKind.Should().Be(JsonValueKind.Array);
        added.GetArrayLength().Should().Be(2, "the fixture already carries one exception");
        added[1].TryGetProperty("code", out var code).Should().BeTrue();
        code.GetString().Should().BeEmpty("the new item starts with the item fields' defaults");
        changes.Clear();

        cut.Find("[data-testid='modeling-inspector-field-exceptions-remove-0']").Click();

        changes.Should().ContainSingle();
        changes[0].SetProperties!["exceptions"].GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void A_structured_item_field_commits_the_whole_list_with_the_edited_item()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, changes: changes);

        var itemField = cut.Find("#modeling-inspector-field-exceptions-0-code");
        itemField.Input("E-9");
        itemField.Blur();

        changes.Should().ContainSingle();
        var list = changes[0].SetProperties!["exceptions"];
        list.GetArrayLength().Should().Be(1);
        list[0].GetProperty("code").GetString().Should().Be("E-9");
        list[0].GetProperty("description").GetString().Should().Be("Stock is not available");
    }

    [Fact]
    public void A_string_list_adds_rows()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, changes: changes);

        cut.Find("[data-testid='modeling-inspector-field-tags-add']").Click();

        changes.Should().ContainSingle();
        changes[0].SetProperties!["tags"].GetArrayLength().Should().Be(2);
    }

    // ── Inline errors (shared validator + writer rejection) ──────────────────

    [Fact]
    public void A_required_issue_renders_inline_and_marks_the_input_invalid()
    {
        var issues = new List<ModelingIssueDto>
        {
            new()
            {
                Id = "required:instructions",
                Severity = ModelingIssueSeverity.Error,
                Category = "property",
                SourceElementId = "element-1",
                SuggestedFix = "Instructions",
                Message = "Property 'Instructions' is required."
            }
        };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.PropertyIssues, issues));

        var field = cut.Find("[data-testid='modeling-inspector-field-instructions']");
        field.GetAttribute("data-schema-error").Should().Be("true");

        // Review U7: an error that was ALREADY on the value when the inspector seeded renders as a
        // described message — role=alert is reserved for a field the user committed.
        var message = field.QuerySelector("#modeling-inspector-field-instructions-error");
        message.Should().NotBeNull("the error is rendered");
        message!.GetAttribute("role").Should().BeNull("a seeded error does not shout on load");
        message.TextContent.Should().Contain("Instructions").And.Contain("required");
        message.TextContent.Should().NotContain("instructions", "the localized message uses the field LABEL");
        field.QuerySelector(".tm-mde-content")!.GetAttribute("aria-describedby")
            .Should().Contain("modeling-inspector-field-instructions-error",
                "the error is reachable through the editor's described-by instead");
    }

    [Fact]
    public void An_issue_id_with_an_owner_suffix_still_matches_its_field()
    {
        // Review A12: the editor stamps the owner onto the id (code:path@ownerId); the inspector must
        // keep matching on the PATH only.
        var issues = new List<ModelingIssueDto>
        {
            new()
            {
                Id = "required:instructions@element-1",
                Severity = ModelingIssueSeverity.Error,
                Category = "property",
                SourceElementId = "element-1",
                SuggestedFix = "Instructions",
                Message = "Property 'Instructions' is required."
            }
        };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.PropertyIssues, issues));

        cut.Find("[data-testid='modeling-inspector-field-instructions']")
            .QuerySelector("#modeling-inspector-field-instructions-error")
            .Should().NotBeNull();
    }

    [Fact]
    public void Committing_an_invalid_field_turns_its_error_into_an_alert()
    {
        // Review U7: once the user commits the field the message becomes assertive.
        var issues = new List<ModelingIssueDto>
        {
            new()
            {
                Id = "length:instructions@element-1",
                Severity = ModelingIssueSeverity.Error,
                Category = "property",
                SourceElementId = "element-1",
                SuggestedFix = "Instructions",
                Message = "Property 'Instructions' must be at most 4000 characters long."
            }
        };

        var changes = new List<ModelingElementChange>();
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.PropertyIssues, issues)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, changes.Add)));

        JSInterop.Setup<string>("tmMarkdownEditor.getValue", _ => true).SetResult("Typed");
        JSInterop.Setup<string[]>("tmMarkdownEditor.getActiveFormats", _ => true).SetResult([]);

        var editor = cut.Find("#modeling-inspector-field-instructions .tm-markdown-editor");
        editor.QuerySelector("[contenteditable]")!.Input("Typed");
        editor.FocusOut();

        changes.Should().ContainSingle();
        cut.Find("[data-testid='modeling-inspector-field-instructions']")
            .QuerySelector("#modeling-inspector-field-instructions-error")!
            .GetAttribute("role").Should().Be("alert");
    }

    [Fact]
    public void A_user_caused_error_keeps_announcing_after_the_parent_applies_the_change()
    {
        // Review U7: the editor answers a commit with a NEW element DTO (and a fresh issue list). The
        // reseed that follows must not forget that the user committed this field — otherwise their own
        // error would go quiet again the instant the model updates, exactly when they need to hear it.
        var issues = new List<ModelingIssueDto>
        {
            new()
            {
                Id = "required:instructions@element-1",
                Severity = ModelingIssueSeverity.Error,
                Category = "property",
                SourceElementId = "element-1",
                SuggestedFix = "Instructions",
                Message = "Property 'Instructions' is required."
            }
        };

        var changes = new List<ModelingElementChange>();
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.PropertyIssues, issues)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, changes.Add)));

        cut.Find("#modeling-inspector-field-instructions-error").GetAttribute("role").Should().BeNull(
            "the seeded error is quiet until the user touches the field");

        JSInterop.Setup<string>("tmMarkdownEditor.getValue", _ => true).SetResult(string.Empty);
        JSInterop.Setup<string[]>("tmMarkdownEditor.getActiveFormats", _ => true).SetResult([]);

        var editor = cut.Find("#modeling-inspector-field-instructions .tm-markdown-editor");
        editor.QuerySelector("[contenteditable]")!.Input(string.Empty);
        editor.FocusOut();

        changes.Should().ContainSingle();
        changes[0].RemovedProperties.Should().Contain("instructions");

        // The parent applied the removal: a NEW DTO instance for the SAME selection and schema.
        var updated = CreateElement();
        updated.Properties.Remove("instructions");
        cut.Render(parameters => parameters
            .Add(p => p.Element, updated)
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.PropertyIssues, issues)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, changes.Add)));

        cut.Find("[data-testid='modeling-inspector-field-instructions']")
            .QuerySelector("#modeling-inspector-field-instructions-error")!
            .GetAttribute("role").Should().Be("alert",
                "the user's own commit keeps its assertive announcement across the reseed");
    }

    [Fact]
    public void A_writer_rejection_issue_renders_the_host_message_verbatim()
    {
        var issues = new List<ModelingIssueDto>
        {
            new()
            {
                Id = "demo-modeling-reference-role",
                Severity = ModelingIssueSeverity.Error,
                Category = "property",
                SourceElementId = "element-1",
                SuggestedFix = "role",
                Message = "The reference stored in 'role' no longer exists in the catalogue."
            }
        };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.PropertyIssues, issues));

        var field = cut.Find("[data-testid='modeling-inspector-field-role']");
        field.TextContent.Should().Contain("no longer exists", "a host-authored issue keeps its own wording");
    }

    [Fact]
    public void An_issue_of_another_element_is_not_rendered_on_this_one()
    {
        var issues = new List<ModelingIssueDto>
        {
            new()
            {
                Id = "required:instructions",
                Severity = ModelingIssueSeverity.Error,
                Category = "property",
                SourceElementId = "element-2",
                SuggestedFix = "Instructions",
                Message = "Property 'Instructions' is required."
            }
        };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.PropertyIssues, issues));

        cut.Find("[data-testid='modeling-inspector-field-instructions']").QuerySelector("[role='alert']")
            .Should().BeNull();
    }

    [Fact]
    public void A_structured_item_issue_is_rendered_on_the_item_not_on_the_list()
    {
        // Review U6: the item path ({key}[{i}].{itemKey}) belongs to THAT item field; the list field
        // only shows an issue whose path is the list itself.
        var issues = new List<ModelingIssueDto>
        {
            new()
            {
                Id = "required:exceptions[0].code",
                Severity = ModelingIssueSeverity.Error,
                Category = "property",
                SourceElementId = "element-1",
                SuggestedFix = "Exceptions",
                Message = "Property 'Exceptions / Code' is required."
            }
        };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.PropertyIssues, issues));

        var field = cut.Find("[data-testid='modeling-inspector-field-exceptions']");
        field.QuerySelector("#modeling-inspector-field-exceptions-error")
            .Should().BeNull("the list itself is not the invalid value");
        var item = cut.Find("#modeling-inspector-field-exceptions-0-code");
        item.GetAttribute("aria-invalid").Should().Be("true");
        item.GetAttribute("aria-describedby").Should().Be("modeling-inspector-field-exceptions-0-code-error");
        cut.Find("#modeling-inspector-field-exceptions-0-code-error").TextContent.Should().Contain("Code");
    }

    // ── A1: label re-resolution ──────────────────────────────────────────────

    [Fact]
    public async Task A_resolved_label_replaces_the_displayed_snapshot_without_committing()
    {
        var changes = new List<ModelingElementChange>();
        var lookup = new StubLookupProvider
        {
            Resolve = request => new ModelingLookupResultDto
            {
                Items = [.. request.References.Select(reference => new ModelingLookupItemDto
                {
                    RefType = reference.RefType,
                    RefId = reference.RefId,
                    Display = reference.RefId == "role-sales" ? "Sales (renamed)" : reference.Display
                })]
            }
        };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.ModelId, "model-1")
            .Add(p => p.LookupProvider, lookup)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, changes.Add)));

        await cut.InvokeAsync(() => cut.Instance.TestReferenceResolution);

        cut.WaitForAssertion(
            () => cut.Find("[data-testid='modeling-inspector-field-role']").TextContent.Should().Contain("Sales (renamed)"),
            TimeSpan.FromSeconds(5));
        changes.Should().BeEmpty("a fresh label is display-only — the stored value is never rewritten");
        lookup.ResolveCalls.Should().Be(2, "ONE resolve call per reference field (role + systems)");
    }

    [Fact]
    public async Task An_unknown_reference_renders_the_stale_hint_and_never_an_error()
    {
        var changes = new List<ModelingElementChange>();
        var lookup = new StubLookupProvider { Resolve = _ => new ModelingLookupResultDto() };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.ModelId, "model-1")
            .Add(p => p.LookupProvider, lookup)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, changes.Add)));

        await cut.InvokeAsync(() => cut.Instance.TestReferenceResolution);

        cut.WaitForAssertion(
            () => cut.Find("#modeling-inspector-field-role-stale").TextContent
                .Should().Contain("stale"),
            TimeSpan.FromSeconds(5));

        var trigger = cut.Find("[data-testid='modeling-inspector-field-role'] .tm-multiselect");
        trigger.GetAttribute("aria-describedby").Should().Contain("modeling-inspector-field-role-stale");
        trigger.GetAttribute("aria-invalid").Should().BeNull("a stale hint is not an error");
        cut.Find("[data-testid='modeling-inspector-field-role']").QuerySelector("[role='alert']")
            .Should().BeNull("a stale reference never blocks the save");
        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_provider_without_resolve_support_renders_no_hint()
    {
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.ModelId, "model-1")
            .Add(p => p.LookupProvider, new LookupOnlyProvider()));

        await cut.InvokeAsync(() => cut.Instance.TestReferenceResolution);

        cut.FindAll("[id$='-stale']").Should().BeEmpty("null = re-resolution unsupported, not stale");
        cut.Find("[data-testid='modeling-inspector-field-role']").TextContent.Should().Contain("Sales");
    }

    // ── A11: collapsible sections ────────────────────────────────────────────

    [Fact]
    public void A_collapsed_section_is_a_disclosure_and_a_field_error_forces_it_open()
    {
        using var cut = RenderSchemaEditor(ReadOnly: false);

        var section = cut.Find("[data-testid='modeling-inspector-schema-section-advanced']");
        var toggle = section.QuerySelector("button[aria-expanded]");
        toggle.Should().NotBeNull("a CollapsedByDefault section is a real disclosure");
        toggle!.GetAttribute("aria-expanded").Should().Be("false");
        var bodyId = toggle.GetAttribute("aria-controls");
        section.QuerySelector($"#{bodyId}")!.HasAttribute("hidden").Should().BeTrue();

        toggle.Click();
        cut.Find("[data-testid='modeling-inspector-schema-section-advanced'] button[aria-expanded]")
            .GetAttribute("aria-expanded").Should().Be("true");

        // A field error FORCES the section open even when the user collapsed it again.
        cut.Find("[data-testid='modeling-inspector-schema-section-advanced'] button[aria-expanded]").Click();
        cut.Render(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.PropertyIssues, new List<ModelingIssueDto>
            {
                new()
                {
                    Id = "required:note",
                    Severity = ModelingIssueSeverity.Error,
                    Category = "property",
                    SourceElementId = "element-1",
                    SuggestedFix = "Note",
                    Message = "Property 'Note' is required."
                }
            }));

        cut.Find("[data-testid='modeling-inspector-schema-section-advanced'] button[aria-expanded]")
            .GetAttribute("aria-expanded").Should().Be("true", "an error inside the section forces it open");
    }

    // ── A14: numbers ────────────────────────────────────────────────────────

    [Fact]
    public void An_integer_field_refuses_a_fractional_value_and_commits_nothing()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, element: CreateElement(("count", 3)), changes: changes);

        var count = cut.Find("#modeling-inspector-field-count");
        count.Input("1.5");
        count.Blur();

        changes.Should().BeEmpty("1.5 is NOT silently rounded into 2");
        cut.Find("[data-testid='modeling-inspector-field-count']")
            .QuerySelector("#modeling-inspector-field-count-error")!.TextContent
            .Should().Contain("whole number");
        count.HasAttribute("aria-invalid").Should().BeTrue();
    }

    [Fact]
    public void A_structured_item_number_that_does_not_parse_keeps_the_text_and_shows_an_item_error()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, changes: changes);

        var amount = cut.Find("#modeling-inspector-field-exceptions-0-limit");
        amount.Input("many");
        amount.Blur();

        changes.Should().BeEmpty("nothing is committed for an unparsable number");
        amount.GetAttribute("value").Should().Be("many", "the typed text stays on screen");
        cut.Find("#modeling-inspector-field-exceptions-0-limit-error").TextContent
            .Should().Contain("Limit");
    }

    // ── A15: reference snapshots ────────────────────────────────────────────

    [Fact]
    public async Task Reselecting_a_stored_reference_keeps_its_original_snapshot()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = RenderSchemaEditor(ReadOnly: false, changes: changes);

        // The fixture stores systems with an OLD snapshot; picking one more keeps those two intact.
        await cut.InvokeAsync(async () =>
        {
            await cut.Instance.TestLoadReferenceOptionsAsync("systems");
            await cut.Instance.TestSelectReferencesAsync("systems", ["sys-erp", "sys-crm", "sys-wms"]);
        });

        changes.Should().ContainSingle();
        var list = changes[0].SetProperties!["systems"];
        list.GetArrayLength().Should().Be(3);
        list[0].GetProperty("snapshotAt").GetString().Should().Be("2020-01-01T00:00:00Z");
        list[1].GetProperty("snapshotAt").GetString().Should().Be("2020-01-01T00:00:00Z");
        list[2].GetProperty("snapshotAt").GetString().Should().NotBe("2020-01-01T00:00:00Z",
            "a NEWLY picked reference gets a fresh snapshot");
    }

    // ── U1 / U2 / U3 / U4 / U5: the field controls ──────────────────────────

    [Fact]
    public void A_date_field_names_the_picker_trigger_from_its_label_only()
    {
        using var cut = RenderSchemaEditor(ReadOnly: false);

        var field = cut.Find("[data-testid='modeling-inspector-field-due']");
        field.QuerySelectorAll(".tm-input-label").Should().BeEmpty("the picker renders its own label");
        var label = field.QuerySelector(".tm-picker-label");
        label.Should().NotBeNull();
        label!.TextContent.Trim().Should().Be("Due");

        var trigger = field.QuerySelector(".tm-date-picker-trigger")!;
        trigger.GetAttribute("aria-label").Should().BeNull("the label names the trigger instead");
        trigger.GetAttribute("aria-labelledby").Should().Contain(label.GetAttribute("id"));
    }

    [Fact]
    public void The_markdown_editor_is_a_textbox_named_and_described_by_the_field()
    {
        using var cut = RenderSchemaEditor(ReadOnly: false);

        var content = cut.Find("#modeling-inspector-field-instructions .tm-mde-content");
        content.GetAttribute("role").Should().Be("textbox");
        content.GetAttribute("aria-multiline").Should().Be("true");
        content.GetAttribute("aria-labelledby").Should().Be("modeling-inspector-field-instructions-label");
        content.GetAttribute("aria-required").Should().Be("true");
        content.GetAttribute("aria-invalid").Should().BeNull("a valid value carries no aria-invalid");
    }

    [Fact]
    public void Every_custom_label_carries_the_required_mark()
    {
        using var cut = RenderSchemaEditor(ReadOnly: false);

        var mark = cut.Find("[data-testid='modeling-inspector-field-instructions'] .tm-input-required");
        mark.TextContent.Should().Be("*");
        mark.GetAttribute("aria-hidden").Should().Be("true");
        cut.Find("[data-testid='modeling-inspector-field-instructions'] .tm-input-label")
            .TextContent.Should().StartWith("Instructions");
    }

    [Fact]
    public void A_boolean_field_renders_its_error_block_after_the_toggle()
    {
        var issues = new List<ModelingIssueDto>
        {
            new()
            {
                Id = "type:active",
                Severity = ModelingIssueSeverity.Error,
                Category = "property",
                SourceElementId = "element-1",
                SuggestedFix = "Active",
                Message = "Property 'Active' must be true or false."
            }
        };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.PropertyIssues, issues));

        var field = cut.Find("[data-testid='modeling-inspector-field-active']");
        var error = field.QuerySelector("#modeling-inspector-field-active-error");
        error.Should().NotBeNull();
        error!.TextContent.Should().Contain("Active");
        field.QuerySelector("[role='switch']")!.GetAttribute("aria-describedby")
            .Should().Be("modeling-inspector-field-active-error");
    }

    [Fact]
    public void A_duration_field_keeps_the_unit_out_of_its_label()
    {
        using var cut = RenderSchemaEditor(ReadOnly: false);

        var field = cut.Find("[data-testid='modeling-inspector-field-sla']");
        field.QuerySelector(".tm-input-label")!.TextContent.Trim().Should().Be("SLA",
            "the unit lives in the select, not in the label");

        var unit = field.QuerySelector(".tm-modeling-inspector__duration-unit")!;
        unit.GetAttribute("aria-label").Should().Be("SLA: minutes");
    }

    // ── U9: the list copy ───────────────────────────────────────────────────

    [Fact]
    public void The_list_actions_carry_the_reviewed_copy()
    {
        using var cut = RenderSchemaEditor(ReadOnly: false);

        var add = cut.Find("[data-testid='modeling-inspector-field-exceptions-add']");
        add.TextContent.Trim().Should().Be("Add item");
        add.GetAttribute("aria-label").Should().Be("Add item to Exceptions");

        cut.Find("[data-testid='modeling-inspector-field-exceptions-remove-0']")
            .GetAttribute("aria-label").Should().Be("Remove item 1 from Exceptions");

        cut.Find("[data-testid='modeling-inspector-field-role'] .tm-multiselect").Click();
        cut.WaitForAssertion(
            () => cut.Find(".tm-multiselect__filter-input").GetAttribute("placeholder").Should().Be("Search…"),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_read_only_empty_value_reads_not_set()
    {
        using var cut = RenderSchemaEditor(ReadOnly: true);

        cut.Find("[data-testid='modeling-inspector-field-due-value']").TextContent.Trim().Should().Be("Not set");
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private IRenderedComponent<TmModelingInspector> RenderSchemaEditor(
        bool ReadOnly,
        ModelingElementDto? element = null,
        List<ModelingElementChange>? changes = null)
    {
        element ??= CreateElement();
        return Render<TmModelingInspector>(parameters =>
        {
            parameters
                .Add(p => p.Element, element)
                .Add(p => p.ReadOnly, ReadOnly)
                .Add(p => p.PropertySchema, CreateSchema())
                .Add(p => p.ModelId, "model-1")
                .Add(p => p.LookupProvider, new StubLookupProvider());

            if (changes is not null)
            {
                parameters.Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, changes.Add));
            }
        });
    }

    private static ModelingElementDto CreateElement(params (string Key, object Value)[] extra)
    {
        var element = new ModelingElementDto
        {
            Id = "element-1",
            SourceId = "source-1",
            Notation = "bpmn",
            SemanticType = "userTask",
            Name = "Approve request",
            Properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["lane"] = JsonSerializer.SerializeToElement("Sales"),
                ["shape"] = JsonSerializer.SerializeToElement(new { a = 1 }),
                ["instructions"] = JsonSerializer.SerializeToElement("Do the thing."),
                ["priority"] = JsonSerializer.SerializeToElement("high"),
                ["active"] = JsonSerializer.SerializeToElement(true),
                ["sla"] = JsonSerializer.SerializeToElement(240),
                ["role"] = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement>
                {
                    ["type"] = JsonSerializer.SerializeToElement("role"),
                    ["id"] = JsonSerializer.SerializeToElement("role-sales"),
                    ["display"] = JsonSerializer.SerializeToElement("Sales"),
                    ["snapshotAt"] = JsonSerializer.SerializeToElement("2026-11-06T09:24:33Z")
                }),
                ["systems"] = JsonSerializer.SerializeToElement(new object[]
                {
                    new { type = "system", id = "sys-erp", display = "ERP", snapshotAt = "2020-01-01T00:00:00Z" },
                    new { type = "system", id = "sys-crm", display = "CRM", snapshotAt = "2020-01-01T00:00:00Z" }
                }),
                ["tags"] = JsonSerializer.SerializeToElement(new[] { "bpmn" }),
                ["exceptions"] = JsonSerializer.SerializeToElement(new object[]
                {
                    new { code = "E-STOCK", description = "Stock is not available", severity = "warning" }
                })
            }
        };

        foreach (var (key, value) in extra)
        {
            element.Properties[key] = JsonSerializer.SerializeToElement(value);
        }

        return element;
    }

    /// <summary>The phase-5 demo schema shape in miniature: markdown, enum, boolean, duration,
    /// entity reference (single + list), structured list and a string list.</summary>
    private static ModelingPropertySchemaDto CreateSchema() => new()
    {
        SchemaId = "tests.bpmn.step",
        Version = "1",
        Notation = "bpmn",
        SemanticType = "userTask",
        Sections =
        [
            new ModelingPropertySectionDto
            {
                Key = "ownership",
                Label = "Ownership",
                Order = 0,
                Fields =
                [
                    new ModelingPropertyFieldDto
                    {
                        Key = "role",
                        Label = "Role",
                        Type = ModelingPropertyFieldType.EntityReference,
                        Required = true,
                        Lookup = new ModelingLookupDescriptor { LookupType = "role" }
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "systems",
                        Label = "Systems",
                        Type = ModelingPropertyFieldType.EntityReferenceList,
                        Lookup = new ModelingLookupDescriptor { LookupType = "system" }
                    }
                ]
            },
            new ModelingPropertySectionDto
            {
                Key = "execution",
                Label = "Execution",
                Order = 1,
                Fields =
                [
                    new ModelingPropertyFieldDto
                    {
                        Key = "instructions",
                        Label = "Instructions",
                        Type = ModelingPropertyFieldType.Markdown,
                        Required = true
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
                        Key = "active",
                        Label = "Active",
                        Type = ModelingPropertyFieldType.Boolean
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "count",
                        Label = "Count",
                        Type = ModelingPropertyFieldType.Integer
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "sla",
                        Label = "SLA",
                        Type = ModelingPropertyFieldType.Duration,
                        Min = 15,
                        Max = 10080,
                        Unit = "min"
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "tags",
                        Label = "Tags",
                        Type = ModelingPropertyFieldType.StringList
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "exceptions",
                        Label = "Exceptions",
                        Type = ModelingPropertyFieldType.StructuredList,
                        ItemFields =
                        [
                            new() { Key = "code", Label = "Code", Type = ModelingPropertyFieldType.String, Required = true },
                            new() { Key = "description", Label = "Description", Type = ModelingPropertyFieldType.MultilineText },
                            new() { Key = "limit", Label = "Limit", Type = ModelingPropertyFieldType.Integer, Order = 2 }
                        ]
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "due",
                        Label = "Due",
                        Type = ModelingPropertyFieldType.Date
                    }
                ]
            },
            new ModelingPropertySectionDto
            {
                Key = "advanced",
                Label = "Advanced",
                Order = 2,
                CollapsedByDefault = true,
                Fields =
                [
                    new ModelingPropertyFieldDto
                    {
                        Key = "note",
                        Label = "Note",
                        Type = ModelingPropertyFieldType.String
                    }
                ]
            }
        ]
    };

    private sealed class StubLookupProvider : IModelingLookupProvider
    {
        public string ProviderKey => "tempo.tests.modeling.schema";

        /// <summary>The re-resolution answer; null leaves the default "unsupported" answer.</summary>
        public Func<ModelingLookupResolveRequest, ModelingLookupResultDto?>? Resolve { get; set; }

        public int ResolveCalls { get; private set; }

        public Task<ModelingLookupResultDto> LookupAsync(ModelingLookupRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ModelingLookupResultDto
            {
                Items =
                [
                    new ModelingLookupItemDto { RefType = request.LookupType, RefId = "role-risk", Display = "Risk owner" },
                    new ModelingLookupItemDto { RefType = request.LookupType, RefId = "sys-wms", Display = "WMS" }
                ],
                TotalCount = 2
            });

        public Task<ModelingLookupResultDto?> ResolveAsync(ModelingLookupResolveRequest request, CancellationToken cancellationToken)
        {
            ResolveCalls++;
            return Resolve is null
                ? Task.FromResult<ModelingLookupResultDto?>(null)
                : Task.FromResult(Resolve(request));
        }
    }

    /// <summary>A provider that only implements the REQUIRED member (no label re-resolution).</summary>
    private sealed class LookupOnlyProvider : IModelingLookupProvider
    {
        public string ProviderKey => "tempo.tests.modeling.schema";

        public Task<ModelingLookupResultDto> LookupAsync(ModelingLookupRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ModelingLookupResultDto());
    }
}
