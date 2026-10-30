using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Fáze 13: the relations tab body — <see cref="TmModelingRelatedElementsPanel"/> lists the
/// selected element's outgoing/incoming relationships (relationship type + counterpart with the
/// semantic-type icon) and the selected relationship's source/target as navigable rows. Two
/// NATIVE buttons per element-view row (no nesting — docs/keyboard-activation-convention.md),
/// missing counterparts and self-references render the localized fallback non-interactively, and
/// the by-element relationship index is built ONCE per <c>Relationships</c> reference (M26).
/// </summary>
public sealed class TmModelingRelatedElementsPanelTests : LocalizationTestBase
{
    private readonly ModelingEditorTestClock _clock = new();

    public TmModelingRelatedElementsPanelTests()
    {
        Services.AddTempoBlazorModeling();
        // The editor's minimum-loading delay must not arm real timers (see TmModelingEditorTests).
        Services.AddSingleton<TimeProvider>(_clock);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new RelatedElementsModelingModelProvider()));
    }

    private static ModelingElementDto Element(string id, string name, string semanticType = "userTask") => new()
    {
        Id = id,
        Notation = "bpmn",
        SemanticType = semanticType,
        Name = name
    };

    private static ModelingRelationshipDto Relationship(string id, string sourceId, string targetId, string type = "sequenceFlow", string name = "") => new()
    {
        Id = id,
        SourceElementId = sourceId,
        TargetElementId = targetId,
        RelationshipType = type,
        Name = name
    };

    private static ModelingElementDto Approve { get; } = Element("task-approve", "Approve request");
    private static ModelingElementDto Ship { get; } = Element("task-ship", "Ship order", "serviceTask");
    private static ModelingElementDto Start { get; } = Element("task-start", "Order received", "startEvent");

    private static IReadOnlyList<ModelingElementDto> Elements { get; } = [Approve, Ship, Start];

    private static IReadOnlyList<ModelingRelationshipDto> Relationships { get; } =
    [
        Relationship("rel-approve-ship", "task-approve", "task-ship", name: "Validated order"),
        Relationship("rel-start-approve", "task-start", "task-approve", name: "Start validation"),
        Relationship("rel-start-ship", "task-start", "task-ship")
    ];

    [Fact]
    public void Element_lists_outgoing_and_incoming_groups_with_counts()
    {
        using var cut = Render<TmModelingRelatedElementsPanel>(parameters => parameters
            .Add(p => p.Element, Approve)
            .Add(p => p.Elements, Elements)
            .Add(p => p.Relationships, Relationships));

        cut.Find("[data-testid='modeling-related-panel']").GetAttribute("data-kind").Should().Be("element");

        // Approve has ONE outgoing and ONE incoming relationship; the third one is unrelated.
        cut.FindAll("[data-testid='modeling-related-outgoing-rel-approve-ship']").Should().HaveCount(1);
        cut.FindAll("[data-testid='modeling-related-incoming-rel-start-approve']").Should().HaveCount(1);
        cut.FindAll("[data-testid^='modeling-related-outgoing-']").Should().HaveCount(1);
        cut.FindAll("[data-testid^='modeling-related-incoming-']").Should().HaveCount(1);

        var headings = cut.FindAll(".tm-modeling-related__group-title");
        headings.Should().HaveCount(2);
        headings[0].TextContent.Should().Contain("Outgoing").And.Contain("1");
        headings[1].TextContent.Should().Contain("Incoming").And.Contain("1");
    }

    [Fact]
    public void Row_shows_relationship_type_counterpart_name_and_type_icon()
    {
        using var cut = Render<TmModelingRelatedElementsPanel>(parameters => parameters
            .Add(p => p.Element, Approve)
            .Add(p => p.Elements, Elements)
            .Add(p => p.Relationships, Relationships));

        var row = cut.Find("[data-testid='modeling-related-outgoing-rel-approve-ship']");
        row.TextContent.Should().Contain("sequenceFlow", "the row leads with the relationship type");
        row.TextContent.Should().Contain("Ship order", "the counterpart is named");

        var counterpart = row.QuerySelector("[data-testid='modeling-related-counterpart-rel-approve-ship']");
        counterpart.Should().NotBeNull("the counterpart action is one of the row's two buttons");
        counterpart!.QuerySelector("svg").Should().NotBeNull("the counterpart carries its semantic-type icon");
        counterpart.GetAttribute("aria-label").Should().Be("Select element Ship order");

        var relationshipAction = row.QuerySelector("[data-testid='modeling-related-relationship-rel-approve-ship']");
        relationshipAction.Should().NotBeNull("the relationship type is the row's second action");
        relationshipAction!.GetAttribute("aria-label").Should().Be("Select relationship sequenceFlow");
    }

    [Fact]
    public void Activating_counterpart_raises_OnElementSelected()
    {
        ModelingElementDto? raised = null;
        using var cut = Render<TmModelingRelatedElementsPanel>(parameters => parameters
            .Add(p => p.Element, Approve)
            .Add(p => p.Elements, Elements)
            .Add(p => p.Relationships, Relationships)
            .Add(p => p.OnElementSelected, EventCallback.Factory.Create<ModelingElementDto>(this, element => raised = element)));

        cut.Find("[data-testid='modeling-related-counterpart-rel-approve-ship']").Click();

        raised.Should().NotBeNull("activating the counterpart navigates to that element");
        raised!.Id.Should().Be("task-ship");
    }

    [Fact]
    public void Activating_relationship_type_raises_OnRelationshipSelected()
    {
        ModelingRelationshipDto? raised = null;
        using var cut = Render<TmModelingRelatedElementsPanel>(parameters => parameters
            .Add(p => p.Element, Approve)
            .Add(p => p.Elements, Elements)
            .Add(p => p.Relationships, Relationships)
            .Add(p => p.OnRelationshipSelected, EventCallback.Factory.Create<ModelingRelationshipDto>(this, relationship => raised = relationship)));

        cut.Find("[data-testid='modeling-related-relationship-rel-approve-ship']").Click();

        raised.Should().NotBeNull("activating the relationship type navigates to that relationship");
        raised!.Id.Should().Be("rel-approve-ship");
    }

    [Fact]
    public void Relationship_selection_lists_source_and_target_rows()
    {
        var selected = Relationships[0];
        ModelingElementDto? raised = null;
        using var cut = Render<TmModelingRelatedElementsPanel>(parameters => parameters
            .Add(p => p.Relationship, selected)
            .Add(p => p.Elements, Elements)
            .Add(p => p.Relationships, Relationships)
            .Add(p => p.OnElementSelected, EventCallback.Factory.Create<ModelingElementDto>(this, element => raised = element)));

        cut.Find("[data-testid='modeling-related-panel']").GetAttribute("data-kind").Should().Be("relationship");

        cut.Find("[data-testid='modeling-related-source-rel-approve-ship']").TextContent.Should().Contain("Approve request");
        cut.Find("[data-testid='modeling-related-target-rel-approve-ship']").TextContent.Should().Contain("Ship order");

        cut.Find("[data-testid='modeling-related-target-rel-approve-ship']").Click();
        raised.Should().NotBeNull("the target row navigates to its element");
        raised!.Id.Should().Be("task-ship");
    }

    [Fact]
    public void Self_reference_and_missing_counterpart_render_safely()
    {
        var relationships = new[]
        {
            Relationship("rel-self", "task-approve", "task-approve"),
            Relationship("rel-ghost", "task-approve", "ghost-element")
        };

        using var cut = Render<TmModelingRelatedElementsPanel>(parameters => parameters
            .Add(p => p.Element, Approve)
            .Add(p => p.Elements, Elements)
            .Add(p => p.Relationships, relationships));

        // Self-reference: both directions render the row and the counterpart stays the element itself.
        cut.Find("[data-testid='modeling-related-outgoing-rel-self']").TextContent.Should().Contain("Approve request");
        cut.Find("[data-testid='modeling-related-incoming-rel-self']").TextContent.Should().Contain("Approve request");

        // Missing counterpart: the localized Unknown fallback renders NON-interactively (no button).
        var ghostRow = cut.Find("[data-testid='modeling-related-outgoing-rel-ghost']");
        ghostRow.TextContent.Should().Contain("Unknown");
        ghostRow.QuerySelector("button[data-testid='modeling-related-counterpart-rel-ghost']").Should().BeNull(
            "a counterpart that does not resolve must not be a navigation button");
    }

    [Fact]
    public void Empty_state_is_localized()
    {
        using var nothing = Render<TmModelingRelatedElementsPanel>();
        nothing.Find("[data-testid='modeling-related-panel']").GetAttribute("data-kind").Should().Be("empty");
        nothing.Find("[data-testid='modeling-related-empty']").TextContent.Should().Contain("No related elements");

        // An element without any relationship shows the same localized empty state.
        using var solo = Render<TmModelingRelatedElementsPanel>(parameters => parameters
            .Add(p => p.Element, Element("solo", "Solo element"))
            .Add(p => p.Elements, [Element("solo", "Solo element")])
            .Add(p => p.Relationships, []));
        solo.Find("[data-testid='modeling-related-empty']").TextContent.Should().Contain("No related elements");
    }

    [Fact]
    public void Relationships_index_memoized()
    {
        var relationships = Enumerable.Range(0, 500)
            .Select(index => Relationship($"rel-{index:000}", index % 2 == 0 ? "task-approve" : "task-start", "task-ship"))
            .ToList();

        using var cut = Render<TmModelingRelatedElementsPanel>(parameters => parameters
            .Add(p => p.Element, Approve)
            .Add(p => p.Elements, Elements)
            .Add(p => p.Relationships, relationships));

        cut.Instance.IndexBuildCount.Should().Be(1, "the index is built once for the first render");

        // An unrelated parameter change re-renders without rebuilding the index.
        cut.Render(parameters => parameters.Add(p => p.Class, "re-rendered"));
        cut.Instance.IndexBuildCount.Should().Be(1, "an unrelated re-render must not rebuild the index");

        // Switching the selected element reuses the SAME memoized index.
        cut.Render(parameters => parameters.Add(p => p.Element, Ship));
        cut.Instance.IndexBuildCount.Should().Be(1, "a selection change reuses the memoized index");

        // A NEW Relationships reference is the only thing that rebuilds it.
        cut.Render(parameters => parameters.Add(p => p.Relationships, relationships.ToList()));
        cut.Instance.IndexBuildCount.Should().Be(2, "a new collection reference rebuilds the index once");
    }

    // ── Editor wiring (the tab, the badge, the navigation and the mobile section) ──────────

    private IRenderedComponent<TmModelingEditor> RenderEditor(TmLayoutMode mode) => Render<TmModelingEditor>(parameters => parameters
        .Add(p => p.LayoutMode, mode)
        .Add(p => p.ProviderKey, RelatedElementsModelingModelProvider.Key));

    private static void WaitForLoaded(IRenderedComponent<TmModelingEditor> cut)
        => cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

    [Fact]
    public void Related_navigation_selects_tree_element_and_highlights_preview()
    {
        // Reusing the SAME selection paths as an issue/edge pick: activating a counterpart from
        // the Relations tab selects the element (the tree's aria-selected + the editor's frozen
        // data-selected-element-id contract the preview flow reports) and swaps the inspector.
        using var cut = RenderEditor(TmLayoutMode.Desktop);
        WaitForLoaded(cut);

        // Select "Approve request" first — the Relations tab always presents the CURRENT selection.
        cut.Find("[data-testid='modeling-tree-node-task-approve']").Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-selected-element-id").Should().Be("task-approve"));

        cut.Find("[data-testid='modeling-tab-relations']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-related-panel']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-related-counterpart-rel-approve-ship']").Click();

        cut.WaitForAssertion(() =>
        {
            var editor = cut.Find("[data-testid='modeling-editor']");
            editor.GetAttribute("data-selected-element-id").Should().Be("task-ship");
            editor.GetAttribute("data-selected-relationship-id").Should().BeEmpty();
            cut.Find("[data-testid='modeling-tree-node-task-ship']").GetAttribute("aria-selected").Should().Be("true");
            cut.Find("[data-testid='modeling-inspector']").GetAttribute("data-kind").Should().Be("element");
            cut.Find("[data-testid='modeling-inspector-name']").TextContent.Should().Contain("Ship order");
        });
    }

    [Fact]
    public void Relations_tab_badge_shows_relationship_count()
    {
        using var cut = RenderEditor(TmLayoutMode.Desktop);
        WaitForLoaded(cut);

        // No selection → no badge; after selecting an element the badge shows its related count.
        cut.FindAll("[data-testid='modeling-tab-relations'] .tm-tab__badge").Should().BeEmpty();

        cut.Find("[data-testid='modeling-tree-node-task-approve']").Click();
        cut.WaitForAssertion(() =>
        {
            var badge = cut.Find("[data-testid='modeling-tab-relations'] .tm-tab__badge");
            badge.TextContent.Should().Be("2", "Approve request has one outgoing and one incoming relationship");
        });

        // A relationship selection lists exactly its source and target rows — open the Relations
        // tab first (the navigation returns the docked inspector to Properties), activate the
        // incoming relationship TYPE, and the editor selects that relationship.
        cut.Find("[data-testid='modeling-tab-relations']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-related-relationship-rel-start-approve']").Should().NotBeNull());
        cut.Find("[data-testid='modeling-related-relationship-rel-start-approve']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-selected-relationship-id")
                .Should().Be("rel-start-approve");
            cut.Find("[data-testid='modeling-tab-relations'] .tm-tab__badge").TextContent.Should().Be("2");
        });
    }

    [Fact]
    public void Mobile_inspector_tab_renders_relations_section()
    {
        // Mobile (G2): no extra tab level — the Inspector TAB body shows the Relations SECTION
        // under the properties (the selection made on the tree tab persists across the switch).
        using var cut = RenderEditor(TmLayoutMode.Mobile);
        WaitForLoaded(cut);

        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-tree-node-task-approve']").Should().NotBeNull());
        cut.Find("[data-testid='modeling-tree-node-task-approve']").Click();

        cut.Find("[data-testid='modeling-panel-tab-inspector']").Click();
        cut.WaitForAssertion(() =>
        {
            var inspector = cut.Find("[data-testid='modeling-inspector-panel']");
            inspector.TextContent.Should().Contain("Approve request");
            inspector.QuerySelector("[data-testid='modeling-related-panel']").Should().NotBeNull(
                "the Relations section renders inside the mobile Inspector tab");
            inspector.QuerySelectorAll("[data-testid='modeling-related-outgoing-rel-approve-ship']").Should().HaveCount(1);
        });
    }
}
