using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Fáze 14: the OPT-IN editable inspector. <see cref="TmModelingInspector.ReadOnly"/> defaults to
/// true (MO2 regression) — only when the editor turns editing on do the name, description and
/// SCALAR properties become inputs; semantic/source/governance metadata and complex JSON values
/// stay read-only forever, an empty name blocks the commit and Escape reverts.
/// </summary>
public sealed class TmModelingInspectorEditingTests : LocalizationTestBase
{
    [Fact]
    public void ReadOnly_default_renders_no_inputs()
    {
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement()));

        cut.FindAll(".tm-modeling-inspector input, .tm-modeling-inspector textarea, .tm-modeling-inspector [role='switch']")
            .Should().BeEmpty("MO2: without the editor's opt-in the inspector stays read-only");
    }

    [Fact]
    public void Editable_element_renders_name_description_and_scalar_property_inputs()
    {
        var element = CreateElement();

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, element)
            .Add(p => p.ReadOnly, false));

        cut.Find("#modeling-inspector-name").Should().NotBeNull();
        cut.Find("#modeling-inspector-description").Should().NotBeNull();
        // Scalar properties become inputs keyed by the property name…
        cut.Find("[data-testid='modeling-inspector-property-lane']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-inspector-property-priority']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-inspector-property-active']").Should().NotBeNull();
        // …while the summary (source metadata) and governance stay plain values.
        cut.Find("[data-testid='modeling-inspector-source-id']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-inspector-governance']").Should().NotBeNull();
    }

    [Fact]
    public void Source_backed_fields_and_governance_stay_read_only()
    {
        var element = CreateElement();
        element.SourceId = "src-1";
        element.SourceType = "bpmn-task";
        element.SourcePath = "/Order/Approve";
        element.Governance = new ModelingGovernanceDto { TrustLevel = "demo", ReviewState = "approved" };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, element)
            .Add(p => p.ReadOnly, false));

        var summary = cut.Find("[data-testid='modeling-inspector-selected-element']");
        // Semantic type, notation, source triple and governance never become editable.
        summary.QuerySelectorAll("input, textarea, [role='switch']")
            .Where(input => input.Id != "modeling-inspector-name"
                && input.Id != "modeling-inspector-description"
                && !input.Id.StartsWith("modeling-inspector-property-", StringComparison.Ordinal))
            .Should().BeEmpty("source-backed fields and governance are never editable");
        cut.Find("[data-testid='modeling-inspector-source-id']").TextContent.Should().Contain("src-1");
        cut.Find("[data-testid='modeling-inspector-governance']").TextContent.Should().Contain("approved");
    }

    [Fact]
    public void Complex_json_property_is_read_only()
    {
        var element = CreateElement();
        element.Properties["shape"] = JsonSerializer.SerializeToElement(new { a = 1 });

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, element)
            .Add(p => p.ReadOnly, false));

        var row = cut.Find("[data-testid='modeling-inspector-property-shape']");
        row.QuerySelector("input, textarea, [role='switch']").Should().BeNull(
            "an object/array property value stays a read-only raw-text row");
        row.TextContent.Should().Contain("\"a\":1");
    }

    [Fact]
    public void Commit_on_blur_or_enter_raises_OnElementEdited_with_only_changed_field()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, changes.Add)));

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Renamed task");
        name.KeyDown("Enter");

        changes.Should().ContainSingle("Enter commits the field");
        changes[0].ElementId.Should().Be("element-1");
        changes[0].Name.Should().Be("Renamed task");
        changes[0].Description.Should().BeNull("only the changed field is reported");
        changes[0].SetProperties.Should().BeNull();
        changes[0].RemovedProperties.Should().BeNull();
        changes.Clear();

        var description = cut.Find("#modeling-inspector-description");
        description.Change("Blur description");
        description.Blur();

        changes.Should().ContainSingle("blur commits the field too");
        changes[0].Description.Should().Be("Blur description");
        changes[0].Name.Should().BeNull();
    }

    [Fact]
    public void Escape_reverts_field_without_event()
    {
        ModelingElementChange? raised = null;
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, change => raised = change)));

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Temporary value");
        name.KeyDown("Escape");

        name.GetAttribute("value").Should().Be("Approve request", "Escape restores the committed value");
        raised.Should().BeNull("Escape never commits");
    }

    [Fact]
    public void Empty_name_shows_validation_and_does_not_commit()
    {
        ModelingElementChange? raised = null;
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, change => raised = change)));

        var name = cut.Find("#modeling-inspector-name");
        name.Input("   ");
        name.KeyDown("Enter");

        cut.Find(".tm-modeling-inspector").TextContent.Should().Contain("required");
        raised.Should().BeNull("an empty name is never committed");
        name.GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void Number_and_bool_properties_keep_json_kind()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, changes.Add)));

        var priority = cut.Find("[data-testid='modeling-inspector-property-priority'] input");
        priority.Change("7");
        priority.Blur();
        changes.Should().ContainSingle();
        changes[0].SetProperties.Should().NotBeNull();
        changes[0].SetProperties!["priority"].ValueKind.Should().Be(JsonValueKind.Number);
        changes.Clear();

        var active = cut.Find("[data-testid='modeling-inspector-property-active'] input");
        active.Change(false);
        changes.Should().ContainSingle();
        changes[0].SetProperties!["active"].ValueKind.Should().Be(JsonValueKind.False);
    }

    [Fact]
    public void Inputs_have_labels_and_44px_on_coarse_pointer()
    {
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false));

        cut.Find("label[for='modeling-inspector-name']").TextContent.Should().Contain("Name");
        cut.Find("label[for='modeling-inspector-description']").TextContent.Should().Contain("Description");
        cut.Find("label[for='modeling-inspector-property-lane']").TextContent.Should().Contain("lane");

        var css = ReadPackageCss("TmModelingInspector.razor.css");
        css.Should().Contain("@media (pointer: coarse)")
            .And.Contain("--tm-touch-target");
    }

    [Fact]
    public void Editable_relationship_renders_name_input_and_raises_OnRelationshipEdited()
    {
        var relationship = CreateRelationship();
        ModelingRelationshipChange? raised = null;
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Relationship, relationship)
            .Add(p => p.ReadOnly, false)
            .Add(p => p.OnRelationshipEdited, EventCallback.Factory.Create<ModelingRelationshipChange>(this, change => raised = change)));

        var name = cut.Find("#modeling-inspector-name");
        name.Input("New flow name");
        name.KeyDown("Enter");

        raised.Should().NotBeNull();
        raised!.RelationshipId.Should().Be("rel-1");
        raised.Name.Should().Be("New flow name");
    }

    [Fact]
    public void Emptying_a_scalar_property_raises_removed_properties()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, changes.Add)));

        var lane = cut.Find("[data-testid='modeling-inspector-property-lane'] input");
        lane.Input(string.Empty);
        lane.Blur();

        changes.Should().ContainSingle();
        changes[0].RemovedProperties.Should().Contain("lane");
    }

    // ── Review-fix tests (phase 14 follow-up) ────────────────────────────────

    [Fact]
    public void Name_edit_field_renders_above_and_outside_the_summary_list()
    {
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false));

        var field = cut.Find("[data-testid='modeling-inspector-name-field']");
        field.TagName.Should().Be("DIV");
        field.ParentElement!.GetAttribute("data-testid").Should().Be("modeling-inspector-selected-element",
            "the name field is a block ABOVE the summary list, not a row inside the <dl>");
        field.NextElementSibling!.TagName.Should().Be("DL");
    }

    [Fact]
    public void Ctrl_Z_with_an_active_draft_lets_the_native_text_undo_win()
    {
        var undoRequests = 0;
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.OnUndoRequested, EventCallback.Factory.Create(this, () => undoRequests++)));

        var name = cut.Find("#modeling-inspector-name");
        name.Input("Uncommitted typing");

        cut.Find(".tm-modeling-inspector").KeyDown(new KeyboardEventArgs { Key = "z", CtrlKey = true });
        undoRequests.Should().Be(0, "a field with uncommitted typing owns Ctrl+Z natively");

        // Once the draft matches the committed value again the editor stack takes over.
        name.KeyDown("Escape");
        cut.Find(".tm-modeling-inspector").KeyDown(new KeyboardEventArgs { Key = "z", CtrlKey = true });
        undoRequests.Should().Be(1);
    }

    [Fact]
    public void Empty_relationship_name_shows_validation_and_does_not_commit()
    {
        var raised = new List<ModelingRelationshipChange>();
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Relationship, CreateRelationship())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.OnRelationshipEdited, EventCallback.Factory.Create<ModelingRelationshipChange>(this, raised.Add)));

        var name = cut.Find("#modeling-inspector-name");
        name.Input("   ");
        name.KeyDown("Enter");

        cut.Find(".tm-modeling-inspector").TextContent.Should().Contain("required");
        raised.Should().BeEmpty("an empty relationship name is never committed");
        name.GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void Escape_reverts_the_description_displayed_value()
    {
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false));

        var description = cut.Find("#modeling-inspector-description");
        description.Input("Typed draft");
        description.KeyDown("Escape");

        cut.Find("#modeling-inspector-description").TextContent.Should().Be("Original description",
            "the textarea is a controlled component — the re-render pushes the committed value back");
    }

    [Fact]
    public void Escape_on_a_number_property_reverts_the_draft_before_the_commit()
    {
        var changes = new List<ModelingElementChange>();
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.OnElementEdited, EventCallback.Factory.Create<ModelingElementChange>(this, changes.Add)));

        var priority = cut.Find("[data-testid='modeling-inspector-property-priority'] input");
        priority.Change("9");
        priority.KeyDown("Escape");
        priority.Blur();

        changes.Should().BeEmpty("Escape reverted the draft, so the blur commits nothing");
        // The render key bumped with the revert: the input remounts onto the committed value.
        priority.GetAttribute("value").Should().Be("3");
    }

    [Fact]
    public void Saving_disables_the_editing_inputs()
    {
        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.Saving, true));

        cut.Find("#modeling-inspector-name").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#modeling-inspector-description").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='modeling-inspector-property-priority'] input").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='modeling-inspector-property-active'] input").HasAttribute("disabled").Should().BeTrue();
    }

    private static string ReadPackageCss(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent!;
        }

        directory.Should().NotBeNull("repo root with TempoBlazor.slnx must be reachable from the test output");
        return File.ReadAllText(Path.Combine(
            directory!.FullName,
            "src", "Tempo.Blazor.Modeling", "Components", "Modeling", fileName));
    }

    private static ModelingElementDto CreateElement() => new()
    {
        Id = "element-1",
        SourceId = "source-1",
        SourceType = "bpmn-task",
        SourcePath = "/Order/Approve",
        Notation = "bpmn",
        SemanticType = "userTask",
        Name = "Approve request",
        Description = "Original description",
        Properties = new Dictionary<string, JsonElement>
        {
            ["lane"] = JsonSerializer.SerializeToElement("Sales"),
            ["priority"] = JsonSerializer.SerializeToElement(3),
            ["active"] = JsonSerializer.SerializeToElement(true),
            ["ratio"] = JsonSerializer.SerializeToElement(0.5)
        },
        Governance = new ModelingGovernanceDto { TrustLevel = "demo", ReviewState = "approved" }
    };

    private static ModelingRelationshipDto CreateRelationship() => new()
    {
        Id = "rel-1",
        SourceElementId = "element-1",
        TargetElementId = "element-2",
        RelationshipType = "sequenceFlow",
        Name = "Original flow name",
        Properties = new Dictionary<string, JsonElement>
        {
            ["priority"] = JsonSerializer.SerializeToElement(3)
        }
    };
}
