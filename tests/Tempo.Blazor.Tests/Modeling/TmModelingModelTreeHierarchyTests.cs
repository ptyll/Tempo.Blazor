using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>Fáze 12 bUnit contract of the model tree's ParentId hierarchy: the nested APG
/// treeitems, disclosure parents, keyboard navigation, the ancestor-keeping filter, cycle
/// safety, the two-way CollapsedElementIds and the M26 filter budget on the hierarchy
/// projection. The flat SemanticType view and its render gate live in
/// TmModelingModelTreeTests.</summary>
public sealed class TmModelingModelTreeHierarchyTests : LocalizationTestBase
{
    private const string ModelingModulePath = "./_content/Tempo.Blazor.Modeling/js/modeling-editor.js";

    // ── M26 filter budget ────────────────────────────────────────────────────

    [Fact]
    public void Filter_narrows_a_200_row_tree_under_200ms()
    {
        // M26: the tree filter must stay under 200ms on a 200-element model — the render gate
        // keeps parent passes from re-rendering the rows and the filter edit itself touches
        // only the matching subset.
        var elements = Enumerable.Range(0, 200)
            .Select(index => new ModelingElementDto
            {
                Id = $"task-{index}",
                SourceId = $"source/task-{index}",
                SourceType = "task",
                SourcePath = $"/Requests/Task{index}",
                SemanticType = "userTask",
                Name = $"Task {index}",
                Description = $"Generated row {index}.",
                Tags = ["bulk"]
            })
            .ToArray();

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, elements));

        // Warm the filter path up first: the FIRST edit pays the JIT/render-pipeline cost of the
        // whole test host, which is not what the M26 budget measures. A warm-up edit + reset
        // leaves the MEASURED edit to the tree's own work (the subset re-render).
        cut.Find("[data-testid='modeling-tree-search']").Input("Task 1");
        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().NotBeEmpty();
        cut.Find("[data-testid='modeling-tree-search']").Input(string.Empty);
        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(200);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        cut.Find("[data-testid='modeling-tree-search']").Input("Task 199");
        stopwatch.Stop();

        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(1,
            "the filter narrows the 200 rows to the single match");

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200),
            "the M26 tree-filter budget is 200ms on a 200-row model");
    }

    [Fact]
    public void Filter_narrows_a_200_row_hierarchy_under_200ms()
    {
        // M26 phase 12: the budget holds for the HIERARCHY projection too — 200 elements in
        // 6-level chains must filter under 200ms with the ancestors kept visible.
        const int chainLength = 6;
        var elements = new List<ModelingElementDto>(200);
        for (var index = 0; index < 200; index++)
        {
            var position = index % chainLength;
            elements.Add(new ModelingElementDto
            {
                Id = $"task-{index}",
                SourceId = $"source/task-{index}",
                SourceType = "task",
                SourcePath = $"/Requests/Task{index}",
                SemanticType = "userTask",
                Name = $"Task {index}",
                Description = $"Generated row {index}.",
                Tags = ["bulk"],
                ParentId = position == 0 ? string.Empty : $"task-{index - 1}"
            });
        }

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, (IReadOnlyList<ModelingElementDto>)elements));

        cut.FindAll("[role='tree'] [role='treeitem']").Should().HaveCount(200);
        cut.Find("[data-testid='modeling-tree-node-task-197']").GetAttribute("aria-level").Should().Be("6");

        // Warm-up edit + reset (the first edit pays the JIT cost of the test host, not the budget).
        cut.Find("[data-testid='modeling-tree-search']").Input("Task 1");
        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().NotBeEmpty();
        cut.Find("[data-testid='modeling-tree-search']").Input(string.Empty);
        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(200);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        cut.Find("[data-testid='modeling-tree-search']").Input("Task 197");
        stopwatch.Stop();

        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(6,
            "the match plus its five ancestors are the only visible rows");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200),
            "the M26 hierarchy filter budget is 200ms on a 200-row / 6-level model");
    }

    // ── Phase 12: tree hierarchy by ParentId ──────────────────────────────────

    [Fact]
    public void Auto_grouping_without_parents_keeps_semantic_type_groups()
    {
        // Auto (the default) must be a NO-OP for models without parents: today's group DOM.
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateFlatElements()));

        cut.FindAll("[role='tree'] > [role='treeitem']").Should().HaveCount(2,
            "semantic-type groups stay the level-1 treeitems");
        cut.FindAll("[role='tree'] > [role='treeitem'][data-group-key]").Should().HaveCount(2);
        cut.Find("[data-testid='modeling-tree-node-task-approve']")
            .GetAttribute("aria-level").Should().Be("2");
    }

    [Fact]
    public void Hierarchy_renders_nested_treeitems_with_aria_level_setsize_posinset()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateHierarchyElements()));

        var treeitems = cut.FindAll("[role='tree'] [role='treeitem']");
        treeitems.Should().HaveCount(6, "every element is exactly one treeitem (flat markup, nested levels)");

        var process = cut.Find("[data-testid='modeling-tree-node-process-1']");
        process.GetAttribute("aria-level").Should().Be("1");
        process.GetAttribute("aria-posinset").Should().Be("1");
        process.GetAttribute("aria-setsize").Should().Be("2", "process-1 and system-customer are the roots");

        var approve = cut.Find("[data-testid='modeling-tree-node-task-approve']");
        approve.GetAttribute("aria-level").Should().Be("2");
        approve.GetAttribute("aria-posinset").Should().Be("1");
        approve.GetAttribute("aria-setsize").Should().Be("2", "approve and review are process-1's children");

        var deep = cut.Find("[data-testid='modeling-tree-node-task-deep']");
        deep.GetAttribute("aria-level").Should().Be("4", "the hierarchy nests to n levels");
        deep.GetAttribute("aria-expanded").Should().BeNull("a childless node is not a disclosure");

        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-visible-count").Should().Be("6");
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-rendered-count").Should().Be("6");
    }

    [Fact]
    public void Parent_node_is_disclosure_with_aria_expanded_and_child_count()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateHierarchyElements()));

        var parent = cut.Find("[data-testid='modeling-tree-node-process-1']");
        parent.GetAttribute("aria-expanded").Should().Be("true");
        parent.GetAttribute("aria-controls").Should().NotBeNullOrEmpty();
        parent.QuerySelector(".tm-modeling-model-tree__type-chevron svg").Should().NotBeNull(
            "a parent node carries a chevron");
        parent.QuerySelector(".tm-modeling-model-tree__child-count")!.TextContent.Should().Be("2",
            "the parent names its direct child count");
        parent.GetAttribute("aria-label").Should().Contain("2 children",
            "the localized child count rides the node label");
        parent.QuerySelector(".tm-modeling-model-tree__type-chevron")!.GetAttribute("title")
            .Should().Contain("Order fulfillment", "the disclosure tooltip carries the localized node name");

        // Indentation rides the level through a --tm-space-* driven CSS variable.
        parent.GetAttribute("style").Should().Contain("--tm-modeling-tree-level: 1");
        cut.Find("[data-testid='modeling-tree-node-task-approve']").GetAttribute("style")
            .Should().Contain("--tm-modeling-tree-level: 2");
        cut.Find("[data-testid='modeling-tree-node-task-deep']").GetAttribute("style")
            .Should().Contain("--tm-modeling-tree-level: 4");

        var css = File.ReadAllText(FindTreeCssPath()).Replace("\r\n", "\n", StringComparison.Ordinal);
        css.Should().Contain("var(--tm-modeling-tree-level",
            "the indentation is token-driven, not hard-coded pixels");
    }

    [Fact]
    public async Task Right_expands_left_collapses_or_moves_to_parent()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusTreeElement", _ => true).SetVoidResult();

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateHierarchyElements()));

        // Collapse the deepest parent first (sub-1 owns task-deep).
        cut.Find("[data-testid='modeling-tree-node-sub-1'] .tm-modeling-model-tree__type-chevron").Click();
        cut.FindAll("[data-testid='modeling-tree-node-task-deep']").Should().BeEmpty();

        // Right on the COLLAPSED parent expands it.
        cut.Find("[data-testid='modeling-tree-node-sub-1']").KeyDown("ArrowRight");
        cut.Find("[data-testid='modeling-tree-node-sub-1']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='modeling-tree-node-task-deep']").Should().NotBeNull();

        // Right on an EXPANDED parent moves focus to its first child (APG).
        cut.Find("[data-testid='modeling-tree-node-sub-1']").KeyDown("ArrowRight");
        cut.WaitForAssertion(() => module.Invocations
            .Where(invocation => invocation.Identifier == "focusTreeElement")
            .Select(invocation => invocation.Arguments[1])
            .Should().Contain("task-deep", "Right on an expanded parent moves to the first child"));

        // Left on a CHILDLESS node moves to its parent (focus, not collapse).
        cut.Find("[data-testid='modeling-tree-node-task-deep']").KeyDown("ArrowLeft");
        cut.WaitForAssertion(() => module.Invocations
            .Where(invocation => invocation.Identifier == "focusTreeElement")
            .Select(invocation => invocation.Arguments[1])
            .Should().Contain("sub-1", "Left on a childless node moves to its parent"));
        cut.Find("[data-testid='modeling-tree-node-sub-1']").GetAttribute("aria-expanded").Should().Be("true");

        // Left on an EXPANDED parent collapses it.
        cut.Find("[data-testid='modeling-tree-node-sub-1']").KeyDown("ArrowLeft");
        cut.Find("[data-testid='modeling-tree-node-sub-1']").GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-testid='modeling-tree-node-task-deep']").Should().BeEmpty();

        JSInterop.Invocations.Where(invocation => invocation.Identifier == "eval").Should().BeEmpty();
        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public void Filter_keeps_ancestors_of_matches_visible_and_expanded()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateHierarchyElements()));

        cut.Find("[data-testid='modeling-tree-search']").Input("Deep");

        cut.Find("[data-testid='modeling-tree-node-task-deep']").Should().NotBeNull("the match renders");
        cut.Find("[data-testid='modeling-tree-node-sub-1']").Should().NotBeNull("the parent stays visible");
        cut.Find("[data-testid='modeling-tree-node-sub-1']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='modeling-tree-node-process-1']").GetAttribute("aria-expanded").Should().Be("true",
            "every ancestor of a match is expanded");
        cut.FindAll("[data-testid='modeling-tree-node-task-review']").Should().BeEmpty(
            "non-matching siblings are still filtered out");
        cut.Find("[data-testid='modeling-model-tree']").GetAttribute("data-visible-count").Should().Be("1",
            "data-visible-count keeps the MATCHING count in hierarchy mode too");
    }

    [Fact]
    public void Missing_parent_renders_element_at_root()
    {
        var elements = new[]
        {
            new ModelingElementDto { Id = "orphan", SemanticType = "userTask", Name = "Orphan", ParentId = "ghost" },
            new ModelingElementDto { Id = "rooted", SemanticType = "userTask", Name = "Rooted" }
        };

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, elements)
            .Add(p => p.Grouping, ModelingTreeGrouping.Hierarchy));

        var orphan = cut.Find("[data-testid='modeling-tree-node-orphan']");
        orphan.GetAttribute("aria-level").Should().Be("1", "an unresolvable parent falls back to the root");
        orphan.GetAttribute("aria-setsize").Should().Be("2");
        cut.Find("[data-testid='modeling-tree-node-rooted']").GetAttribute("aria-level").Should().Be("1");
    }

    [Fact]
    public void Parent_cycle_is_broken_deterministically_without_stack_overflow()
    {
        // a → b → c → a plus d → a: the cycle members (and anything hanging off them) cannot
        // resolve to a root, so they render AT THE ROOT — deterministically, whatever the input
        // order, without a stack overflow.
        ModelingElementDto[] Cycle() =>
        [
            new() { Id = "c", SemanticType = "userTask", Name = "C", ParentId = "a" },
            new() { Id = "b", SemanticType = "userTask", Name = "B", ParentId = "c" },
            new() { Id = "a", SemanticType = "subProcess", Name = "A", ParentId = "b" },
            new() { Id = "d", SemanticType = "userTask", Name = "D", ParentId = "a" }
        ];

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, Cycle()));

        cut.FindAll("[role='tree'] [role='treeitem']").Should().HaveCount(4,
            "every element renders exactly once — no infinite recursion");
        foreach (var id in new[] { "a", "b", "c", "d" })
        {
            cut.Find($"[data-testid='modeling-tree-node-{id}']").GetAttribute("aria-level")
                .Should().Be("1", $"cycle member {id} is deterministically broken to the root");
        }
    }

    [Fact]
    public async Task External_selection_reveals_all_collapsed_ancestors()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusTreeElement", _ => true).SetVoidResult();
        module.SetupVoid("scrollTreeElementIntoView", _ => true).SetVoidResult();

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateHierarchyElements()));

        cut.Find("[data-testid='modeling-tree-node-sub-1'] .tm-modeling-model-tree__type-chevron").Click();
        cut.Find("[data-testid='modeling-tree-node-process-1'] .tm-modeling-model-tree__type-chevron").Click();
        cut.FindAll("[data-testid='modeling-tree-node-task-deep']").Should().BeEmpty();

        var selected = CreateHierarchyElements().First(element => element.Id == "task-deep");
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmModelingModelTree.SelectedElement)] = selected })));

        cut.Find("[data-testid='modeling-tree-node-task-deep']").Should().NotBeNull(
            "the selection reveals the whole ancestor chain");
        cut.WaitForAssertion(() => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "scrollTreeElementIntoView")
            .Should().NotBeEmpty());
        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public async Task CollapsedElementIds_two_way_binding_is_separate_from_CollapsedGroups()
    {
        var raised = new List<IReadOnlyCollection<string>>();
        var seed = new[] { "process-1" };

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateHierarchyElements())
            .Add(p => p.CollapsedElementIds, seed)
            .Add(p => p.CollapsedElementIdsChanged, EventCallback.Factory
                .Create<IReadOnlyCollection<string>>(this, value => raised.Add(value))));

        cut.FindAll("[data-testid='modeling-tree-node-task-approve']").Should().BeEmpty(
            "the seeded element collapse applies from the first render");
        cut.Find("[data-testid='modeling-tree-node-process-1']").GetAttribute("aria-expanded").Should().Be("false");

        // The chevron toggles expansion without selecting the node.
        cut.Find("[data-testid='modeling-tree-node-process-1'] .tm-modeling-model-tree__type-chevron").Click();
        cut.Find("[data-testid='modeling-tree-node-task-approve']").Should().NotBeNull();
        raised.Should().ContainSingle();
        raised[0].Should().BeEmpty("the re-expansion raises the fresh snapshot");
        raised[0].Should().BeEquivalentTo(Array.Empty<string>());

        cut.Find("[data-testid='modeling-tree-node-task-approve']").Click();
        cut.WaitForAssertion(() => cut.Instance.ToString().Should().NotBeNull()); // flush raises
        cut.Find("[data-testid='modeling-tree-node-task-approve']").GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void Insert_button_and_drag_handle_on_every_hierarchy_node()
    {
        ModelingElementDto? requested = null;
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateHierarchyElements())
            .Add(p => p.OnInsertIntoPreview, EventCallback.Factory
                .Create<ModelingElementDto>(this, element => requested = element)));

        foreach (var id in new[] { "process-1", "task-approve", "sub-1", "task-deep", "system-customer" })
        {
            var row = cut.Find($"[data-testid='modeling-tree-node-{id}']");
            row.GetAttribute("draggable").Should().Be("true", $"every hierarchy node drags ({id})");
            row.QuerySelector(".tm-modeling-model-tree__drag-handle").Should().NotBeNull($"({id})");
        }

        // The insert action exists on EVERY row — including a collapsed parent.
        cut.Find("[data-testid='modeling-tree-node-sub-1'] .tm-modeling-model-tree__type-chevron").Click();
        cut.Find("[data-testid='modeling-tree-insert-sub-1']").Click();
        cut.WaitForAssertion(() => requested.Should().NotBeNull());
        requested!.Id.Should().Be("sub-1");
    }

    [Fact]
    public void Render_gate_re_renders_when_hierarchy_parameters_change()
    {
        var elements = CreateHierarchyElements();
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, elements));

        var afterFirst = cut.Instance.RenderCount;

        // A new CollapsedElementIds collection is a render-affecting seed.
        cut.Render(parameters => parameters.Add(p => p.CollapsedElementIds, new[] { "process-1" }));
        cut.WaitForAssertion(() => cut.Instance.RenderCount.Should().BeGreaterThan(afterFirst));
        cut.FindAll("[data-testid='modeling-tree-node-task-approve']").Should().BeEmpty();

        // Grouping is render-affecting too: pinning SemanticType brings the flat groups back.
        cut.Render(parameters => parameters
            .Add(p => p.CollapsedElementIds, (IReadOnlyCollection<string>?)null)
            .Add(p => p.Grouping, ModelingTreeGrouping.SemanticType));
        cut.WaitForAssertion(() => cut.Instance.RenderCount.Should().BeGreaterThan(afterFirst + 1));
        cut.FindAll("[role='tree'] > [role='treeitem'][data-group-key]").Should().NotBeEmpty();
    }


    /// <summary>A flat model (no ParentId) — mirrors the flat fixture of TmModelingModelTreeTests.
    /// </summary>
    private static ModelingElementDto[] CreateFlatElements() =>
    [
        new() { Id = "task-approve", SourceId = "source/task-approve", SourceType = "task", SourcePath = "/Requests/Approve", SemanticType = "userTask", Name = "Approve request", Description = "Approve the request.", Tags = ["workflow", "approval"] },
        new() { Id = "task-review", SourceId = "source/task-review", SourceType = "task", SourcePath = "/Requests/Review", SemanticType = "userTask", Name = "Review request", Description = "Review the request.", Tags = ["workflow", "review"] },
        new() { Id = "system-customer", SourceId = "source/system-customer", SourceType = "system", SourcePath = "/Systems/Customer", SemanticType = "applicationComponent", Name = "Customer portal", Description = "Customer application.", Tags = ["portal"] }
    ];

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

    private static ModelingElementDto[] CreateHierarchyElements() =>
    [
        new() { Id = "process-1", SemanticType = "subProcess", Name = "Order fulfillment" },
        new() { Id = "task-approve", SemanticType = "userTask", Name = "Approve request", ParentId = "process-1" },
        new() { Id = "task-review", SemanticType = "userTask", Name = "Review request", ParentId = "process-1" },
        new() { Id = "sub-1", SemanticType = "subProcess", Name = "Inner subprocess", ParentId = "task-approve" },
        new() { Id = "task-deep", SemanticType = "serviceTask", Name = "Deep task", ParentId = "sub-1" },
        new() { Id = "system-customer", SemanticType = "applicationComponent", Name = "Customer portal" }
    ];
}

