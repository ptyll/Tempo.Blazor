using System.Text.Json;
using Bunit;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingInspectorTests : LocalizationTestBase
{
    [Fact]
    public void Header_renders_type_icon_name_and_type()
    {
        var element = CreateElement();

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, element));

        var header = cut.Find(".tm-modeling-inspector__header");
        header.QuerySelector(".tm-modeling-inspector__type-icon svg").Should().NotBeNull(
            "the header leads with the semantic type icon");
        header.TextContent.Should().Contain(element.Name);
        header.TextContent.Should().Contain(element.SemanticType);
    }

    [Fact]
    public void Null_input_renders_empty_state()
    {
        using var cut = Render<TmModelingInspector>();

        cut.Find("[data-testid='modeling-inspector']").GetAttribute("data-kind").Should().Be("empty");
        cut.Find("[data-testid='modeling-inspector-empty']").TextContent.Should().Contain("Select an element");
    }

    [Fact]
    public void Element_with_many_properties_renders_all_rows()
    {
        var element = CreateElement();
        element.Properties = Enumerable.Range(0, 50)
            .ToDictionary(
                index => $"property-{index:00}",
                index => JsonSerializer.SerializeToElement($"Value {index:00}"));

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, element));

        cut.FindAll(".tm-modeling-inspector__properties-table tbody tr").Should().HaveCount(50);
        cut.Find("[data-testid='modeling-inspector-property-property-49']").TextContent.Should().Contain("Value 49");
    }

    [Fact]
    public void Element_with_no_properties_shows_none_message()
    {
        var element = CreateElement();
        element.Properties.Clear();

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, element));

        cut.Find("[data-testid='modeling-inspector-no-properties']").TextContent.Should().Contain("(None)");
    }

    [Fact]
    public void Property_value_with_html_chars_renders_as_text()
    {
        var element = CreateElement();
        element.Properties["html"] = JsonSerializer.SerializeToElement("<b>test</b>");

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, element));

        var property = cut.Find("[data-testid='modeling-inspector-property-html']");
        property.TextContent.Should().Contain("<b>test</b>");
        property.QuerySelector("td b").Should().BeNull();
    }

    [Fact]
    public void Switching_from_element_to_relationship_replaces_content()
    {
        var source = CreateElement(id: "task-a", name: "Approve request");
        var target = CreateElement(id: "task-b", name: "Ship order");
        var relationship = CreateRelationship();

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, source)
            .Add(p => p.Elements, new[] { source, target }));

        cut.Find("[data-testid='modeling-inspector']").GetAttribute("data-kind").Should().Be("element");
        cut.Render(parameters => parameters
            .Add(p => p.Element, null)
            .Add(p => p.Relationship, relationship)
            .Add(p => p.Elements, new[] { source, target }));

        cut.Find("[data-testid='modeling-inspector']").GetAttribute("data-kind").Should().Be("relationship");
        cut.Find("[data-testid='modeling-inspector-selected-relationship']").TextContent.Should().Contain("sequenceFlow");
        cut.FindAll("[data-testid='modeling-inspector-selected-element']").Should().BeEmpty();
    }

    [Fact]
    public void Governance_with_null_values_renders_empty_cells_without_exception()
    {
        var element = CreateElement();
        element.Governance = new ModelingGovernanceDto
        {
            TrustLevel = null!,
            ReviewState = null!,
            SyncState = null!,
            DataSource = null!
        };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, element));

        cut.Find("[data-testid='modeling-inspector-governance']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-inspector-trust'] dd").TextContent.Should().BeEmpty();
        cut.Find("[data-testid='modeling-inspector-review'] dd").TextContent.Should().BeEmpty();
    }

    /// <summary>The summary/properties markup must be plain Razor — elements built through
    /// RenderTreeBuilder in code do NOT receive the Razor compiler's CSS-isolation scope
    /// attribute, so the TmModelingInspector.razor.css rules (<c>.tm-modeling-inspector__summary dt</c>,
    /// <c>.tm-modeling-inspector__properties-table th</c>, …) would never match.</summary>
    [Fact]
    public void Summary_and_table_elements_carry_the_component_css_scope_attribute()
    {
        var element = CreateElement();
        element.Properties = new Dictionary<string, JsonElement>
        {
            ["owner"] = JsonSerializer.SerializeToElement("Sales")
        };

        using var cut = Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, element));

        var scopeAttribute = cut.Find(".tm-modeling-inspector").Attributes
            .Select(attribute => attribute.Name)
            .FirstOrDefault(name => name.StartsWith("b-", StringComparison.Ordinal));
        scopeAttribute.Should().NotBeNullOrWhiteSpace("the inspector root must carry a CSS-isolation scope attribute");

        var scopedElements =
            cut.FindAll(".tm-modeling-inspector__summary")
                .Concat(cut.FindAll(".tm-modeling-inspector__summary dt"))
                .Concat(cut.FindAll(".tm-modeling-inspector__summary dd"))
                .Concat(cut.FindAll(".tm-modeling-inspector__section h4"))
                .Concat(cut.FindAll(".tm-modeling-inspector__properties-table th"))
                .Concat(cut.FindAll(".tm-modeling-inspector__properties-table td"));
        scopedElements.Should().NotBeEmpty();

        foreach (var scopedElement in scopedElements)
        {
            scopedElement.GetAttribute(scopeAttribute!).Should().NotBeNull(
                $"'{scopedElement.TagName}' must carry the '{scopeAttribute}' scope attribute so the scoped CSS rules apply");
        }
    }

    private static ModelingElementDto CreateElement(string id = "task-a", string name = "Approve request") => new()
    {
        Id = id,
        SourceId = $"source/{id}",
        SourceType = "bpmn-task",
        SourcePath = $"/Requests/{id}",
        Notation = "bpmn",
        SemanticType = "userTask",
        Name = name,
        Description = "Approve the request.",
        Properties = new Dictionary<string, JsonElement>
        {
            ["owner"] = JsonSerializer.SerializeToElement("Sales")
        },
        Governance = new ModelingGovernanceDto
        {
            TrustLevel = "High",
            ReviewState = "approved",
            SyncState = "fresh",
            DataSource = "Unit tests"
        }
    };

    private static ModelingRelationshipDto CreateRelationship() => new()
    {
        Id = "rel-a-b",
        SourceId = "source/rel-a-b",
        SourceType = "bpmn-sequence-flow",
        SourceElementId = "task-a",
        TargetElementId = "task-b",
        RelationshipType = "sequenceFlow",
        Name = "Approved request",
        Properties = new Dictionary<string, JsonElement>
        {
            ["condition"] = JsonSerializer.SerializeToElement("approved")
        }
    };
}
