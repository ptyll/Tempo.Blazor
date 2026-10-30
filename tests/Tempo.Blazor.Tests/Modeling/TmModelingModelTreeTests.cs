using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
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

    // ── Render gate (final code review, Wireframe render-gate pattern) ───────

    [Fact]
    public void Render_gate_skips_a_noop_parent_pass_but_renders_for_every_render_affecting_parameter()
    {
        var elements = CreateElements();
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, elements));

        var afterFirst = cut.Instance.RenderCount;
        afterFirst.Should().BeGreaterThan(0);

        // A no-op parent pass (the SAME Elements reference, like the editor re-rendering for its
        // own state) — the tree's gate diffs the snapshot to "no change" and skips the 200-row
        // re-render.
        cut.Render(parameters => parameters.Add(p => p.Elements, elements));
        cut.Instance.RenderCount.Should().Be(afterFirst, "a parameter-equal pass renders nothing");

        // A render-affecting parameter (the selected element) passes the gate.
        var selection = elements[0];
        cut.Render(parameters => parameters.Add(p => p.SelectedElement, selection));
        cut.WaitForAssertion(() => cut.Instance.RenderCount.Should().BeGreaterThan(afterFirst,
            "a new SelectedElement re-renders the selection state"));
        cut.Find("[data-testid='modeling-tree-node-task-approve']")
            .GetAttribute("aria-selected").Should().Be("true");

        // Internal interactions still render: the filter input narrows the list.
        var beforeFilter = cut.Instance.RenderCount;
        cut.Find("[data-testid='modeling-tree-search']").Input("approve");
        cut.WaitForAssertion(() => cut.Instance.RenderCount.Should().BeGreaterThan(beforeFilter));
        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(1);
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
    public void Group_header_is_apg_treeitem_with_count_and_aria_expanded()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        var treeitem = cut.Find("[data-testid='modeling-tree-group-userTask']");
        treeitem.GetAttribute("role").Should().Be("treeitem", "the group is an APG level-1 treeitem");
        treeitem.TagName.Should().Be("DIV");
        treeitem.GetAttribute("aria-level").Should().Be("1");
        treeitem.GetAttribute("aria-expanded").Should().Be("true");
        treeitem.GetAttribute("aria-controls").Should().NotBeNullOrEmpty();
        treeitem.GetAttribute("aria-setsize").Should().Be("2");
        treeitem.GetAttribute("aria-posinset").Should().Be("2");
        treeitem.GetAttribute("aria-label").Should().Contain("User task");
        cut.FindAll("[role='tree'] > [role='treeitem']")
            .Count(item => item.GetAttribute("tabindex") == "0")
            .Should().Be(1, "the roving tabindex marks exactly ONE active level-1 treeitem");
        cut.FindAll("[role='tree'] [role='treeitem'] [role='treeitem']")
            .Should().OnlyContain(node => node.GetAttribute("tabindex") == "-1",
                "level-2 nodes stay out of the Tab order until the roving tabindex reaches them");

        var header = cut.Find("[data-testid='modeling-tree-group-header-userTask']");
        header.TagName.Should().Be("DIV", "the header row is presentational; the treeitem owns focus");
        header.GetAttribute("title").Should().NotBeNullOrEmpty("the sr-only hint moved into a tooltip");
        header.TextContent.Should().Contain("User task");
        header.TextContent.Should().Contain("2", "the group header shows the element count");
        header.QuerySelector(".tm-modeling-model-tree__group-chevron svg").Should().NotBeNull();

        cut.Find("[data-testid='modeling-tree-group-userTask'] [role='group']").Should().NotBeNull(
            "the node list is an ARIA group owned by the treeitem");
        cut.FindAll("[data-testid='modeling-tree-group-userTask'] [role='treeitem'][aria-level='2']")
            .Should().HaveCount(2, "each element node is a level-2 treeitem with setsize/posinset");
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-visible-count").Should().Be("3");
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-rendered-count").Should().Be("3");
    }

    [Fact]
    public void Group_ids_are_unique_per_instance_and_not_lossy_slugs()
    {
        var firstIds = RenderAndCollectGroupIds();
        var secondIds = RenderAndCollectGroupIds();

        firstIds.Should().NotIntersectWith(secondIds, "two trees on one page must not share content ids");
        firstIds.Should().OnlyContain(id => id.StartsWith("tm-mmt-", StringComparison.Ordinal));
        firstIds.Should().HaveCount(2).And.OnlyContain(id => id.EndsWith("-group-1") || id.EndsWith("-group-2"),
            "content ids use the group position, not a lossy slug of the key");
    }

    private List<string> RenderAndCollectGroupIds()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));
        return cut.FindAll("[role='tree'] [role='treeitem'] [role='group']")
            .Select(node => node.GetAttribute("id") ?? string.Empty)
            .ToList();
    }

    [Fact]
    public void Group_can_be_collapsed_and_expanded_with_keyboard()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-testid='modeling-tree-node-task-approve']").Should().BeEmpty();
        // data-visible-count keeps the MATCHING count contract (M10/M23/M26) — a collapsed
        // group still counts; the RENDERED count drops instead.
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-visible-count").Should().Be("3");
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-rendered-count").Should().Be("1");

        // ArrowLeft collapses (already collapsed: stays), ArrowRight expands — the toggle
        // keys on the treeitem are Enter (keydown) and Space (keyup), the native button split.
        cut.Find("[data-testid='modeling-tree-group-userTask']").KeyDown("ArrowLeft");
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false");

        cut.Find("[data-testid='modeling-tree-group-userTask']").KeyDown("ArrowRight");
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='modeling-tree-node-task-approve']").Should().NotBeNull();
    }

    [Fact]
    public void Enter_and_space_toggle_the_group_treeitem_exactly_once()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        // The group treeitem is a non-native focusable: Enter on keydown (no repeat) and
        // Space on keyup — the native button split (docs/keyboard-activation-convention.md).
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false");

        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", Repeat = true });
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false", "held Enter must not re-toggle");

        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void Single_type_model_with_collapsed_group_never_shows_the_empty_filter_state()
    {
        // The BLOCKER regression: with ONE group and no filter text, collapsing it must not
        // flip the tree into the empty-filter dead end — the header stays, click re-expands.
        var elements = new[]
        {
            new ModelingElementDto { Id = "only-one", SemanticType = "userTask", Name = "Only element" }
        };

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, elements));

        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();

        cut.FindAll("[data-testid='modeling-tree-empty-filter']").Should().BeEmpty(
            "a collapsed single group is a collapsed tree, not an empty filter result");
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false");
        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Should().NotBeNull(
            "the header is still rendered so the group can be re-expanded");
        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().BeEmpty();
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-visible-count").Should().Be("1");
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-rendered-count").Should().Be("0");

        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();
        cut.Find("[data-testid='modeling-tree-node-only-one']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-rendered-count").Should().Be("1");
    }

    [Fact]
    public void Filter_toggle_uses_transient_state_and_keeps_stored_state_untouched()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        // Collapse a group WITHOUT a filter → stored state.
        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();

        // With the filter active, toggling moves the TRANSIENT set only.
        cut.Find("[data-testid='modeling-tree-search']").Input("task");
        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false", "the transient set drives the filtered view");

        // Editing the filter starts a NEW transient session.
        cut.Find("[data-testid='modeling-tree-search']").Input("review");
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("true", "each filter edit clears the transient collapses");

        cut.Find("[data-testid='modeling-tree-search']").Input(string.Empty);
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false", "the stored state was never touched by filter toggles");
    }

    [Fact]
    public async Task Externally_selected_element_auto_reveals_its_collapsed_group()
    {
        var selected = CreateElements().First(element => element.Id == "task-review");

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.SelectedElement, selected));

        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();
        cut.FindAll("[data-testid='modeling-tree-node-task-review']").Should().BeEmpty();

        // A NEW SelectedElement parameter instance inside the collapsed group re-reveals it;
        // the scroll happens once the node is rendered (after the reveal render).
        var next = new ModelingElementDto
        {
            Id = "task-review",
            SourceId = selected.SourceId,
            SourceType = selected.SourceType,
            SourcePath = selected.SourcePath,
            SemanticType = selected.SemanticType,
            Name = selected.Name,
            Description = selected.Description,
            Tags = selected.Tags
        };
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmModelingModelTree.SelectedElement)] = next })));

        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("true", "an external selection reveals its collapsed group");
        cut.Find("[data-testid='modeling-tree-node-task-review']").Should().NotBeNull();
        cut.Instance.TestInteropModule.Should().NotBeNull();
        cut.WaitForAssertion(() => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "scrollTreeElementIntoView")
            .Should().NotBeEmpty("the revealed node is scrolled into view once rendered"));
    }

    [Fact]
    public void Two_way_collapsed_groups_seeds_and_raises()
    {
        var raised = new List<IReadOnlyCollection<string>>();
        var seed = new[] { "userTask" };

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.CollapsedGroups, seed)
            .Add(p => p.CollapsedGroupsChanged, EventCallback.Factory
                .Create<IReadOnlyCollection<string>>(this, value => raised.Add(value))));

        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("false", "the internal state seeds from the parameter");

        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-expanded").Should().Be("true");
        raised.Should().HaveCount(1);
        raised[0].Should().BeEmpty("the re-expansion raises the new snapshot");

        cut.Find("[data-testid='modeling-tree-group-header-applicationComponent']").Click();
        raised.Should().HaveCount(2);
        raised[1].Should().BeEquivalentTo(["applicationComponent"]);
    }

    [Fact]
    public async Task Keyboard_navigation_walks_the_flat_visible_list()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusTreeElement", _ => true).SetVoidResult();
        module.SetupVoid("focusTreeGroupHeader", _ => true).SetVoidResult();

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        // userTask sorts first: its header is the first flat entry; ArrowDown moves to the
        // first NODE (a level-2 treeitem), not another header.
        cut.Find("[data-testid='modeling-tree-group-userTask']").KeyDown("ArrowDown");

        cut.WaitForAssertion(() => module.Invocations
            .Select(invocation => invocation.Identifier)
            .Should().Contain("focusTreeElement", "ArrowDown from a header focuses its first child node"));

        // Left on a node focuses the node's group header (level 1 treeitem).
        cut.Find("[data-testid='modeling-tree-node-task-approve']").KeyDown("ArrowLeft");
        cut.WaitForAssertion(() => module.Invocations
            .Select(invocation => invocation.Identifier)
            .Should().Contain("focusTreeGroupHeader", "ArrowLeft from a node focuses its group treeitem"));

        // End jumps to the LAST visible entry (the applicationComponent node).
        cut.Find("[data-testid='modeling-tree-group-userTask']").KeyDown("End");
        cut.WaitForAssertion(() => module.Invocations
            .Where(invocation => invocation.Identifier == "focusTreeElement")
            .Select(invocation => invocation.Arguments[1])
            .Should().Contain("task-review", "End jumps to the last visible entry"));

        // Right on a node is a no-op: no further focus invocation may appear (flush the
        // dispatcher first — handler continuations record their JS calls asynchronously).
        cut.Find("[data-testid='modeling-tree-node-task-approve']").KeyDown("ArrowRight");
        await cut.InvokeAsync(() => { });
        module.Invocations.Count(invocation => invocation.Identifier == "focusTreeGroupHeader")
            .Should().Be(1, "ArrowRight on a node is a no-op");

        JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "eval")
            .Should().BeEmpty("keyboard navigation must not fall back to eval");

        // Roving tabindex: exactly ONE visible treeitem carries tabindex=0.
        cut.FindAll("[role='tree'] [role='treeitem']")
            .Count(item => item.GetAttribute("tabindex") == "0")
            .Should().BeLessThanOrEqualTo(1);
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
        cut.Find("[data-testid='modeling-tree-group-userTask']")
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

        cut.Find("[data-testid='modeling-tree-group-userTask']")
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

    [Fact]
    public async Task External_selection_syncs_roving_tabindex_and_raises_collapsed_groups()
    {
        var raised = new List<IReadOnlyCollection<string>>();

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.CollapsedGroups, new[] { "userTask" })
            .Add(p => p.CollapsedGroupsChanged, EventCallback.Factory
                .Create<IReadOnlyCollection<string>>(this, value => raised.Add(value))));

        cut.FindAll("[data-testid='modeling-tree-node-task-review']").Should().BeEmpty();

        // A NEW SelectedElement instance (e.g. a canvas click) auto-reveals the collapsed group,
        // moves the roving tabindex onto the selected node and raises the two-way binding with
        // the revealed snapshot (queued out of OnParametersSet, flushed after render).
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmModelingModelTree.SelectedElement)] = CreateElements().First(element => element.Id == "task-review") })));

        cut.Find("[data-testid='modeling-tree-node-task-review']")
            .GetAttribute("tabindex").Should().Be("0", "the roving tabindex follows an external selection");
        cut.WaitForAssertion(() => raised.Should().ContainSingle(
                "the auto-reveal keeps the two-way binding in sync"),
            timeout: TimeSpan.FromSeconds(5));
        raised[0].Should().BeEmpty("userTask was revealed");
    }

    [Fact]
    public void Tree_is_labelled_by_its_title_and_aria_controls_follow_expansion()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        var tree = cut.Find("[role='tree']");
        var titleId = tree.GetAttribute("aria-labelledby");
        titleId.Should().NotBeNullOrWhiteSpace("the tree element is named by the header title");
        cut.Find($"#{titleId}").TextContent.Should().NotBeEmpty();

        var treeitem = cut.Find("[data-testid='modeling-tree-group-userTask']");
        treeitem.GetAttribute("aria-controls").Should().NotBeNullOrWhiteSpace("an expanded group points at its content");

        cut.Find("[data-testid='modeling-tree-group-header-userTask']").Click();
        cut.Find("[data-testid='modeling-tree-group-userTask']")
            .GetAttribute("aria-controls").Should().BeNull("a collapsed group has no content element to point at");
    }

    // ── Non-drag alternative (Fáze 6, WCAG 2.5.7) ────────────────────────────

    [Fact]
    public void Insert_into_preview_action_raises_insert_request()
    {
        // The row-level "Insert into preview" action is the WCAG 2.5.7 non-drag alternative:
        // a keyboard/touch-reachable button that asks the host to reuse the element through
        // the existing drop path (the drop geometry is computed by the hosting editor, which
        // owns the document — the tree raises the request, not the geometry).
        ModelingElementDto? requested = null;
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnInsertIntoPreview, EventCallback.Factory.Create<ModelingElementDto>(this, element => requested = element)));

        cut.Find("[data-testid='modeling-tree-insert-task-review']").Click();

        cut.WaitForAssertion(() => requested.Should().NotBeNull());
        requested!.Id.Should().Be("task-review");
    }

    [Fact]
    public void Insert_action_respects_allow_node_drop()
    {
        // AllowNodeDrop=false hides the action (the host opted out of tree reuse entirely —
        // the same switch the drop zone follows), so no alternative path may appear either.
        var requested = 0;
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.AllowNodeDrop, false)
            .Add(p => p.OnInsertIntoPreview, EventCallback.Factory.Create<ModelingElementDto>(this, _ => requested++)));

        cut.FindAll("[data-testid='modeling-tree-insert-task-review']").Should().BeEmpty();
    }

    [Fact]
    public void Insert_action_requires_a_listener_even_when_node_drop_is_allowed()
    {
        // Fáze 6 review: the button renders only when the host BOTH allows drops AND listens —
        // a "+" with no receiver would be a dead control.
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.AllowNodeDrop, true));

        cut.FindAll("[data-testid^='modeling-tree-insert-']").Should().BeEmpty();
    }

    [Fact]
    public void Insert_action_is_out_of_the_tab_order_and_names_its_row()
    {
        ModelingElementDto? requested = null;
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnInsertIntoPreview, EventCallback.Factory.Create<ModelingElementDto>(this, element => requested = element)));

        // The reveal button is a pointer/touch affordance: the KEYBOARD path is the node
        // treeitem's Insert key (aria-keyshortcuts), so the button itself stays tabindex=-1.
        var button = cut.Find("[data-testid='modeling-tree-insert-task-approve']");
        button.GetAttribute("tabindex").Should().Be("-1");
        button.GetAttribute("aria-label").Should().Be("Insert Approve request into preview",
            "the per-row name carries the element's display name");
        button.GetAttribute("title").Should().Be("Insert Approve request into preview");

        // The keyboard path: Insert on the node treeitem raises the same request.
        var node = cut.Find("[data-testid='modeling-tree-node-task-approve']");
        node.KeyDown(new KeyboardEventArgs { Key = "Insert" });
        cut.WaitForAssertion(() => requested.Should().NotBeNull());
        requested!.Id.Should().Be("task-approve");
        node.GetAttribute("aria-keyshortcuts").Should().Be("Insert Control+Enter",
            "the node names both keyboard insert paths (the gesture twins)");
    }

    [Fact]
    public async Task Ctrl_Enter_on_a_node_raises_the_insert_request_exactly_once()
    {
        var requested = 0;
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnInsertIntoPreview, EventCallback.Factory.Create<ModelingElementDto>(this, _ => requested++)));

        var node = cut.Find("[data-testid='modeling-tree-node-task-approve']");
        await cut.InvokeAsync(() => node.KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true }));
        requested.Should().Be(1, "Ctrl+Enter is the mnemonic twin of Insert");

        // A plain Enter stays the NATIVE selection click (the node is a real button) — the
        // emulated path must not fire on top of it.
        await cut.InvokeAsync(() => node.KeyDown(new KeyboardEventArgs { Key = "Enter" }));
        requested.Should().Be(1);
    }

    [Fact]
    public async Task Repeated_insert_key_does_not_repeat_the_request()
    {
        var requested = 0;
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnInsertIntoPreview, EventCallback.Factory.Create<ModelingElementDto>(this, _ => requested++)));

        await cut.InvokeAsync(() => cut.Find("[data-testid='modeling-tree-node-task-approve']")
            .KeyDown(new KeyboardEventArgs { Key = "Insert", Repeat = true }));
        requested.Should().Be(0, "auto-repeat must not fire the insert path");
    }

    [Fact]
    public void Insert_action_stays_reachable_without_hover_and_names_itself()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnInsertIntoPreview, EventCallback.Factory.Create<ModelingElementDto>(this, _ => { })));

        var button = cut.Find("[data-testid='modeling-tree-insert-task-approve']");
        button.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace(
            "an icon-only action names itself (the localized 'Insert {0} into preview' label)");
        button.GetAttribute("title").Should().NotBeNullOrWhiteSpace();

        // M25 re-review: the NODE treeitem names both keyboard insert paths (Insert is the
        // gesture twin of Ctrl+Enter) — the row button itself is out of the tab order.
        cut.Find("[data-testid='modeling-tree-node-task-approve']")
            .GetAttribute("aria-keyshortcuts").Should().Be("Insert Control+Enter");

        // The reveal contract (docs/responsive-conventions.md) is the CORE class pair: the
        // action hides until hover on a fine pointer only, stays visible on (hover: none) and
        // reveals under :focus-within — nothing component-local may re-implement it.
        button.ClassList.Should().Contain("tm-reveal-on-hover",
            "the action uses the core hover-reveal class (fine pointers only)");
        button.ParentElement!.ClassList.Should().Contain("tm-reveal-host",
            "the row is the reveal host (hover or focus anywhere in the row reveals the action)");

        // Fáze 6 review: on a FINE pointer the action no longer reserves a grid column — the
        // row is a single full-width track (the pre-phase-6 name truncation) and the action
        // floats over its trailing edge (the ONLY component-local media query; the reveal
        // CLASS pair itself stays the core contract). On a coarse pointer it keeps the column
        // and reaches the 44px target both ways. Line endings normalized (item: exact
        // multi-line strings).
        var css = File.ReadAllText(FindTreeCssPath()).Replace("\r\n", "\n", StringComparison.Ordinal);
        css.Should().Contain("@media (pointer: coarse)",
            "the coarse-pointer block pins the 44px targets");
        css.Should().Contain(".tm-modeling-model-tree__node,\n    .tm-modeling-model-tree__insert {\n        min-height: var(--tm-touch-target)",
            "tree nodes and the insert action meet the 44px target on a coarse pointer");
        css.Should().Contain(".tm-modeling-model-tree__insert {\n        min-width: var(--tm-touch-target)",
            "the insert action is 44px WIDE on a coarse pointer too (E2E Tablet_1024_TouchTargets_Measure44px)");
        css.Should().Contain("@media (hover: hover) and (pointer: fine)",
            "the fine-pointer block drops the reserved column");
        css.Should().MatchRegex(
            @"@media \(hover: hover\) and \(pointer: fine\) \{[^}]*tm-modeling-model-tree__node-row \{[^}]*grid-template-columns: minmax\(0, 1fr\);",
            "fine pointers render the row as one full-width track (the pre-phase-6 truncation)");
    }

    // ── Two-way filter text (Fáze 6 review fix) ──────────────────────────────

    [Fact]
    public async Task Filter_text_round_trips_through_a_real_bind()
    {
        using var cut = Render<FilterBindHost>();

        // A user edit filters, updates the host field and comes back through the binding…
        cut.Find("[data-testid='modeling-tree-search']").Input("approve");
        await cut.InvokeAsync(() => { });
        cut.Instance.Filter.Should().Be("approve", "FilterTextChanged carries the edit to the host");
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-visible-count").Should().Be("1");

        // …and the echoed parameter value keeps the state (the input renders it, the filter
        // still applies) — a layout flip remounts the tree, the FIELD lives with the host.
        cut.Find("[data-testid='modeling-tree-search']").GetAttribute("value").Should().Be("approve");
        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(1);
    }

    [Fact]
    public void Filter_text_seeds_from_the_host_parameter()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.FilterText, (string?)"approve"));

        cut.Find("[data-testid='modeling-tree-search']").GetAttribute("value").Should().Be("approve");
        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(1,
            "the seeded filter applies from the first render");
    }

    [Fact]
    public async Task Host_reset_of_the_bound_filter_re_seeds_the_tree()
    {
        using var cut = Render<FilterBindHost>();

        // The user types; the edit round-trips through the binding (the echo of the raise)…
        cut.Find("[data-testid='modeling-tree-search']").Input("approve");
        await cut.InvokeAsync(() => { });
        cut.Instance.Filter.Should().Be("approve");
        cut.Find("[data-testid='modeling-tree-search']").GetAttribute("value").Should().Be("approve");

        // …and the host can still RESET the bound value afterwards (M26 re-review): the echo
        // must not pin the last host-provided value, so a reset to it re-seeds the working
        // copy — the input clears and every row is visible again.
        await cut.Instance.SetFilterAsync(null);
        cut.Find("[data-testid='modeling-tree-search']").GetAttribute("value").Should().Be("", "a reset clears the input");
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-visible-count").Should().Be("3",
            "all rows are visible again after the reset");

        // A host change to a DIFFERENT value re-seeds the same way.
        await cut.Instance.SetFilterAsync("review");
        cut.Find("[data-testid='modeling-tree-search']").GetAttribute("value").Should().Be("review");
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-visible-count").Should().Be("1");
    }

    [Fact]
    public void Tablet_drawer_hides_the_tree_title_but_keeps_the_filter_usable()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.ShowTitle, false));

        cut.FindAll(".tm-modeling-model-tree__title").Should().BeEmpty(
            "the drawer's sheet title already says 'Model tree'");
        cut.FindAll(".tm-modeling-model-tree__count").Should().BeEmpty();

        var tree = cut.Find("[role='tree']");
        tree.GetAttribute("aria-labelledby").Should().BeNull("the named element is not rendered");
        tree.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace("the tree is still named");

        cut.Find("[data-testid='modeling-tree-search']").Should().NotBeNull("the live filter stays usable");
    }

    /// <summary>A minimal host that really binds <c>@bind-FilterText</c> — the field is the
    /// host's, the tree never assigns the parameter (the two-way contract under test).</summary>
    private sealed class FilterBindHost : ComponentBase
    {
        public string? Filter { get; private set; }

        /// <summary>A HOST-driven change (a reset to null, or a new seed): the field is the
        /// host's and the tree only ever observes it through the parameter.</summary>
        public Task SetFilterAsync(string? value)
        {
            Filter = value;
            return InvokeAsync(StateHasChanged);
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmModelingModelTree>(0);
            builder.AddAttribute(1, nameof(TmModelingModelTree.Elements), CreateElements());
            builder.AddAttribute(2, nameof(TmModelingModelTree.FilterText), Filter);
            builder.AddAttribute(3, nameof(TmModelingModelTree.FilterTextChanged),
                EventCallback.Factory.Create<string?>(this, value =>
                {
                    Filter = value;
                    InvokeAsync(StateHasChanged);
                }));
            builder.CloseComponent();
        }
    }

    private static string FindTreeCssPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent!;
        }

        directory.Should().NotBeNull("repo root with TempoBlazor.slnx must be reachable from the test output");
        return Path.Combine(
            directory!.FullName,
            "src", "Tempo.Blazor.Modeling", "Components", "Modeling", "TmModelingModelTree.razor.css");
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
