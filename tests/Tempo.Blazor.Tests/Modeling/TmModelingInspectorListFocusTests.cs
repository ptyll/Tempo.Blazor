using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase-5 review (U8): the schema list actions own their focus movement and their announcement. Add
/// moves focus into the NEW row's first input, remove moves it to the next row's remove button (else
/// back to the add button), and one polite live region says what happened in the user's language —
/// so a keyboard or screen-reader user never loses their place when a row appears or disappears under
/// them. The move rides the shared <c>modeling-editor.js</c> <c>focusWithin</c> export.
/// </summary>
public sealed class TmModelingInspectorListFocusTests : LocalizationTestBase
{
    private const string FocusWithinSelectorPrefix = "#modeling-inspector-field-";

    [Fact]
    public void Adding_a_string_list_item_announces_it_and_focuses_the_new_row()
    {
        var module = SetupFocusModule();
        using var cut = RenderSchemaEditor();

        cut.Find("[data-testid='modeling-inspector-field-tags-add']").Click();

        cut.WaitForAssertion(() =>
        {
            Announcement(cut).Should().Be("Item added to Tags");
            FocusSelectors(module).Should().Contain($"{FocusWithinSelectorPrefix}tags-2",
                "focus lands on the input of the row the action just created (the third of three)");
        });
    }

    [Fact]
    public void Adding_a_structured_list_item_focuses_its_first_input()
    {
        var module = SetupFocusModule();
        using var cut = RenderSchemaEditor();

        cut.Find("[data-testid='modeling-inspector-field-exceptions-add']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='modeling-inspector-field-exceptions-item-1']").Should().HaveCount(1);
            FocusSelectors(module).Should().Contain($"{FocusWithinSelectorPrefix}exceptions-1-code",
                "the first item field of the appended item receives focus");
        });
    }

    [Fact]
    public void Removing_an_item_focuses_the_next_remove_button()
    {
        var module = SetupFocusModule();
        using var cut = RenderSchemaEditor();

        cut.Find("[data-testid='modeling-inspector-field-tags-remove-0']").Click();

        cut.WaitForAssertion(() =>
        {
            Announcement(cut).Should().Be("Item removed from Tags");
            FocusSelectors(module).Should().Contain($"{FocusWithinSelectorPrefix}tags-remove-0",
                "the row that took the removed row's place offers its remove button to the keyboard");
        });
    }

    [Fact]
    public void Removing_the_last_item_focuses_the_add_button()
    {
        var module = SetupFocusModule();
        using var cut = RenderSchemaEditor(tags: ["only"]);

        cut.Find("[data-testid='modeling-inspector-field-tags-remove-0']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='modeling-inspector-field-tags-row-0']").Should().BeEmpty("the list is empty now");
            FocusSelectors(module).Should().Contain($"{FocusWithinSelectorPrefix}tags-add",
                "with no row left the add button is the next sensible stop");
        });
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private BunitJSModuleInterop SetupFocusModule()
    {
        var module = JSInterop.SetupModule(ModelingInteropModule.InteropModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();
        return module;
    }

    private static IReadOnlyList<string> FocusSelectors(BunitJSModuleInterop module)
        => module.Invocations
            .Where(invocation => invocation.Identifier == "focusWithin")
            .Select(invocation => (string)invocation.Arguments[1]!)
            .ToArray();

    private static string Announcement(IRenderedComponent<TmModelingInspector> cut)
        => cut.Find("[data-testid='modeling-inspector-item-announcement']").TextContent.Trim();

    private IRenderedComponent<TmModelingInspector> RenderSchemaEditor(string[]? tags = null)
        => Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement(tags ?? ["bpmn", "erp"]))
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema())
            .Add(p => p.ModelId, "model-1"));

    private static ModelingElementDto CreateElement(IReadOnlyList<string> tags) => new()
    {
        Id = "element-1",
        SourceId = "source-1",
        Notation = "bpmn",
        SemanticType = "userTask",
        Name = "Approve request",
        Properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["tags"] = JsonSerializer.SerializeToElement(tags),
            ["exceptions"] = JsonSerializer.SerializeToElement(new object[]
            {
                new { code = "E-STOCK", description = "Stock is not available" }
            })
        }
    };

    private static ModelingPropertySchemaDto CreateSchema() => new()
    {
        SchemaId = "tests.bpmn.list-focus",
        Version = "1",
        Notation = "bpmn",
        SemanticType = "userTask",
        Sections =
        [
            new ModelingPropertySectionDto
            {
                Key = "lists",
                Label = "Lists",
                Order = 0,
                Fields =
                [
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
                            new() { Key = "description", Label = "Description", Type = ModelingPropertyFieldType.MultilineText }
                        ]
                    }
                ]
            }
        ]
    };
}
