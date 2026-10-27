using Bunit;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingModelTreeTests : LocalizationTestBase
{
    private const string ModelingModulePath = "./_content/Tempo.Blazor.Modeling/js/modeling-editor.js";

    [Fact]
    public void Renders_node_for_each_model_element()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(3);
    }

    [Fact]
    public void Filter_narrows_visible_nodes_to_matches()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.Find("[data-testid='modeling-tree-search']").Input("approve");

        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(1);
        cut.Find("[data-testid='modeling-tree-node-task-approve']").TextContent.Should().Contain("Approve request");
    }

    [Fact]
    public void Empty_filter_restores_all_nodes()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.Find("[data-testid='modeling-tree-search']").Input("approve");
        cut.Find("[data-testid='modeling-tree-search']").Input(string.Empty);

        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(3);
    }

    [Fact]
    public void Non_matching_filter_shows_empty_filter_state()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.Find("[data-testid='modeling-tree-search']").Input("xxxx");

        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().BeEmpty();
        cut.Find("[data-testid='modeling-tree-empty-filter']").TextContent.Should().Contain("xxxx");
    }

    [Fact]
    public void Clicking_node_emits_selected_element()
    {
        ModelingElementDto? selected = null;
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnElementSelected, EventCallback.Factory.Create<ModelingElementDto>(this, element => selected = element)));

        cut.Find("[data-testid='modeling-tree-node-task-review']").Click();

        selected.Should().NotBeNull();
        selected!.Id.Should().Be("task-review");
    }

    [Fact]
    public void Enter_on_node_selects_exactly_once()
    {
        // Native <button role="treeitem"> sequence: Enter produces the click on
        // keydown — keydown -> click. A keydown handler that also selected the
        // element fired OnElementSelected twice for one key press.
        var selections = new List<ModelingElementDto>();
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnElementSelected, EventCallback.Factory.Create<ModelingElementDto>(this, element => selections.Add(element))));

        var node = cut.Find("[data-testid='modeling-tree-node-task-review']");
        node.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-testid='modeling-tree-node-task-review']").Click();

        selections.Select(e => e.Id).Should().Equal("task-review");
    }

    [Fact]
    public void Dragstart_exposes_element_id_for_data_transfer_bridge()
    {
        ModelingElementDto? dragged = null;
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnElementDragStarted, EventCallback.Factory.Create<ModelingElementDto>(this, element => dragged = element)));

        var node = cut.Find("[data-testid='modeling-tree-node-task-review']");
        node.GetAttribute("draggable").Should().Be("true");
        node.GetAttribute("data-modeling-drag-element-id").Should().Be("task-review");

        node.TriggerEvent("ondragstart", new Microsoft.AspNetCore.Components.Web.DragEventArgs());

        dragged.Should().NotBeNull();
        dragged!.Id.Should().Be("task-review");
    }

    [Fact]
    public void Drag_bridge_uses_module_import_not_eval()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("registerTreeDragBridge", _ => true).SetVoidResult();

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.WaitForAssertion(() =>
        {
            JSInterop.Invocations
                .Where(invocation => invocation.Identifier == "import")
                .SelectMany(invocation => invocation.Arguments)
                .Select(argument => argument?.ToString())
                .Should().Contain(ModelingModulePath);
        });

        module.Invocations
            .Select(invocation => invocation.Identifier)
            .Should().Contain("registerTreeDragBridge");

        JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "eval")
            .Should().BeEmpty("the drag bridge is registered by the ES module, not by eval");
    }

    [Fact]
    public async Task Keyboard_navigation_focuses_node_without_eval()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusTreeElement", _ => true).SetVoidResult();
        module.SetupVoid("scrollTreeElementIntoView", _ => true).SetVoidResult();

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        // task-approve sorts first (ArrowDown has a next sibling); ArrowDown on the
        // LAST node is a no-op by design (the boundary is clamped).
        cut.Find("[data-testid='modeling-tree-node-task-approve']")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowDown" });

        cut.WaitForAssertion(() => module.Invocations
            .Select(invocation => invocation.Identifier)
            .Should().Contain("focusTreeElement",
                "arrow navigation moves focus through the CSP-safe module"));

        // The query is scoped to the tree root ElementReference — never the document.
        foreach (var invocation in module.Invocations.Where(i =>
                     i.Identifier is "focusTreeElement" or "scrollTreeElementIntoView"))
        {
            invocation.Arguments.Should().HaveCount(2);
            invocation.Arguments[0].Should().BeOfType<ElementReference>(
                "the node query is scoped to the tree root, not the document");
        }

        JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "eval")
            .Should().BeEmpty("keyboard navigation must not fall back to eval");

        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_the_tree_disposes_the_imported_module()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("registerTreeDragBridge", _ => true).SetVoidResult();

        var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        // The first render registers the drag bridge, which creates (and imports through)
        // the module wrapper.
        cut.WaitForAssertion(() => module.Invocations
            .Select(invocation => invocation.Identifier)
            .Should().Contain("registerTreeDragBridge"));

        var wrapper = cut.Instance.TestInteropModule;
        wrapper.Should().NotBeNull("the drag bridge created the module wrapper");

        await cut.Instance.DisposeAsync();

        wrapper!.IsDisposed.Should().BeTrue(
            "@implements IAsyncDisposable must dispose the imported ES module, or the module object leaks");
    }

    [Fact]
    public void Empty_model_shows_message_instead_of_blank_container()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, Array.Empty<ModelingElementDto>()));

        cut.Find("[data-testid='modeling-model-tree']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-tree-empty']").TextContent.Should().Contain("No elements");
    }

    [Fact]
    public void Empty_name_renders_unnamed_fallback()
    {
        var elements = CreateElements().Append(new ModelingElementDto
        {
            Id = "empty-name",
            SemanticType = "task",
            Name = string.Empty
        }).ToArray();

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, elements));

        cut.Find("[data-testid='modeling-tree-node-empty-name']").TextContent.Should().Contain("(Unnamed)");
    }

    [Fact]
    public void Group_header_is_disclosure_with_count_and_aria_expanded()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        var header = cut.Find("[data-testid='modeling-tree-group-header-userTask']");
        header.TagName.Should().Be("BUTTON", "the group header is a real disclosure button");
        header.GetAttribute("aria-expanded").Should().Be("true");
        header.GetAttribute("aria-controls").Should().NotBeNullOrEmpty();
        header.TextContent.Should().Contain("userTask");
        header.TextContent.Should().Contain("2", "the group header shows the element count");
        header.QuerySelector(".tm-modeling-model-tree__group-chevron svg").Should().NotBeNull();

        cut.Find("[data-testid='modeling-tree-group-userTask'] [role='group']").Should().NotBeNull(
            "the node list is an ARIA group owned by the disclosure header");
        cut.Find("[data-testid='modeling-tree-group-userTask']").GetAttribute("data-expanded")
            .Should().Be("true");
    }

    [Fact]
    public void Group_can_be_collapsed_and_expanded_with_keyboard()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();
        cut.Find("[data-testid='modeling-tree-group-header-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-testid='modeling-tree-node-task-approve']").Should().BeEmpty();
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-visible-count").Should().Be("1");

        // ArrowLeft collapses, ArrowRight expands — Enter/Space stay native button activation
        // (docs/keyboard-activation-convention.md: no emulated activation on native activatables).
        cut.Find("[data-testid='modeling-tree-group-header-userTask']").KeyDown("ArrowLeft");
        cut.Find("[data-testid='modeling-tree-group-header-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false");

        cut.Find("[data-testid='modeling-tree-group-header-userTask']").KeyDown("ArrowRight");
        cut.Find("[data-testid='modeling-tree-group-header-userTask']")
            .GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='modeling-tree-node-task-approve']").Should().NotBeNull();
    }

    [Fact]
    public void Filter_expands_collapsed_groups_with_matches()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();
        cut.FindAll("[data-testid='modeling-tree-node-task-approve']").Should().BeEmpty();

        cut.Find("[data-testid='modeling-tree-search']").Input("Approve");

        cut.Find("[data-testid='modeling-tree-node-task-approve']").Should().NotBeNull(
            "a collapsed group whose nodes match the filter is temporarily expanded");
        cut.Find("[data-testid='modeling-tree-group-header-userTask']")
            .GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void Collapsed_state_survives_rerender_and_filter_clear()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();

        cut.Find("[data-testid='modeling-tree-search']").Input("Approve");
        cut.Find("[data-testid='modeling-tree-node-task-approve']").Should().NotBeNull();

        cut.Find("[data-testid='modeling-tree-search']").Input(string.Empty);

        cut.Find("[data-testid='modeling-tree-group-header-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false",
                "clearing the filter restores the user's collapsed state");
        cut.FindAll("[data-testid='modeling-tree-node-task-approve']").Should().BeEmpty();
        cut.Find("[data-testid='modeling-tree-node-system-customer']").Should().NotBeNull(
            "groups the user never collapsed stay expanded");
    }

    [Fact]
    public void Node_renders_type_icon_with_fallback()
    {
        var elements = CreateElements().Append(new ModelingElementDto
        {
            Id = "mystery-element",
            SemanticType = "mysteryType",
            Name = "Mystery"
        }).ToArray();

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, elements));

        cut.Find("[data-testid='modeling-tree-node-task-approve'] .tm-modeling-model-tree__type-icon svg")
            .Should().NotBeNull("each node renders an icon derived from its semantic type");

        var fallback = ModelingSemanticTypeIcons.ForSemanticType("mysteryType");
        ModelingSemanticTypeIcons.ForSemanticType("userTask").Should().NotBe(fallback,
            "a known semantic type maps to its own icon");
        cut.FindComponents<TmIcon>()
            .Select(icon => icon.Instance.Name)
            .Should().Contain(fallback, "an unknown semantic type falls back to the generic icon");
    }

    [Fact]
    public void Drag_handle_is_icon_not_text_glyph()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        var handle = cut.Find("[data-testid='modeling-tree-node-task-approve'] .tm-modeling-model-tree__drag-handle");

        handle.TextContent.Should().NotContain("::");
        handle.QuerySelector("svg").Should().NotBeNull("the drag affordance is an icon, not the '::' text glyph");
    }

    private static ModelingElementDto[] CreateElements() =>
    [
        new()
        {
            Id = "task-approve",
            SourceId = "source/task-approve",
            SourceType = "task",
            SourcePath = "/Requests/Approve",
            SemanticType = "userTask",
            Name = "Approve request",
            Description = "Approve the request.",
            Tags = ["workflow", "approval"]
        },
        new()
        {
            Id = "task-review",
            SourceId = "source/task-review",
            SourceType = "task",
            SourcePath = "/Requests/Review",
            SemanticType = "userTask",
            Name = "Review request",
            Description = "Review the request.",
            Tags = ["workflow", "review"]
        },
        new()
        {
            Id = "system-customer",
            SourceId = "source/system-customer",
            SourceType = "system",
            SourcePath = "/Systems/Customer",
            SemanticType = "applicationComponent",
            Name = "Customer portal",
            Description = "Customer application.",
            Tags = ["portal"]
        }
    ];
}
