using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// The tablet presentation of <see cref="TmModelingEditor"/> (Fáze 6): with a forced
/// <c>LayoutMode=Tablet</c> (no CSS, no DOM measurement — the same pattern the wireframe
/// editor's tablet tests use) the docked workspace renders through the core TmEditorShell's
/// overlay presentation — the model tree as an overlay drawer, the inspector as a non-modal
/// side sheet, at most ONE open at a time — and the "Panels" tab row drives their visibility.
/// The non-drag alternative (WCAG 2.5.7) is covered at the end: the tree's "Insert into
/// preview" action goes through the preview's existing drop path at the viewport center.
/// </summary>
public sealed class TmModelingEditorTabletTests : LocalizationTestBase
{
    public TmModelingEditorTabletTests()
    {
        Services.AddTempoBlazorModeling();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));
    }

    private IRenderedComponent<TmModelingEditor> RenderTabletEditor(
        Action<ComponentParameterCollectionBuilder<TmModelingEditor>>? configure = null)
        => Render<TmModelingEditor>(parameters =>
        {
            parameters.Add(p => p.LayoutMode, TmLayoutMode.Tablet);
            parameters.Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key);
            configure?.Invoke(parameters);
        });

    private static void WaitForLoaded(IRenderedComponent<TmModelingEditor> cut)
        => cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

    // ── At most one side panel ───────────────────────────────────────────────

    [Fact]
    public void Tablet_renders_preview_with_at_most_one_side_panel()
    {
        using var cut = RenderTabletEditor();
        WaitForLoaded(cut);

        // The preview is the persistent surface of the tablet layout…
        cut.Find("[data-testid='modeling-preview-panel']").Should().NotBeNull();

        // …and at most ONE side panel exists: the overlay drawer (tree) and the side sheet
        // (inspector) never render together. With both flags open the shell keeps the most
        // recently opened one (the inspector) and tells the editor to close the other.
        var drawerCount = cut.FindAll("[data-testid='modeling-tree-drawer']").Count;
        var sheetCount = cut.FindAll("[data-testid='modeling-inspector-sheet']").Count;
        (drawerCount + sheetCount).Should().BeLessThanOrEqualTo(1,
            "the tablet layout shows the preview plus at most one side panel");

        // The docked presentation is gone: neither side renders as a shell column/rail.
        cut.FindAll(".tm-editor-shell__panel").Should().BeEmpty(
            "on a tablet the side panels are overlays, not docked columns");
        cut.FindAll(".tm-editor-shell__rail").Should().BeEmpty();
    }

    [Fact]
    public void Tablet_opening_tree_closes_inspector_sheet()
    {
        using var cut = RenderTabletEditor();
        WaitForLoaded(cut);

        // Opening the tree drawer closes the inspector sheet (the editor enforces the
        // one-sheet rule itself, the same way the wireframe editor does).
        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-tree-drawer']").Should().NotBeNull();
            cut.FindAll("[data-testid='modeling-inspector-sheet']").Should().BeEmpty(
                "at most one side panel is open");
        });

        cut.Find("[data-testid='modeling-panel-tab-inspector']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-inspector-sheet']").Should().NotBeNull();
            cut.FindAll("[data-testid='modeling-tree-drawer']").Should().BeEmpty();
        });
    }

    [Fact]
    public void Panel_tabs_toggle_side_panel_visibility_on_tablet()
    {
        using var cut = RenderTabletEditor();
        WaitForLoaded(cut);

        // The compact panel-tab row doubles as the tablet visibility switch.
        var tabs = cut.Find("[data-testid='modeling-panel-tabs']");
        tabs.Should().NotBeNull("the tablet layout keeps the 'Panels' tabs as the visibility switch");

        // Tree tab toggles the drawer: open…
        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-tree-drawer']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-panel-tab-tree']")
                .GetAttribute("aria-expanded").Should().Be("true");
            cut.Find("[data-testid='modeling-panel-tab-tree']").ClassList
                .Should().Contain("tm-modeling-editor__panel-tab--active",
                    "the tablet tab's visual state follows the sheet flags, not the compact active panel");
            cut.Find("[data-testid='modeling-panel-tab-inspector']").ClassList
                .Should().NotContain("tm-modeling-editor__panel-tab--active");
        });

        // …and closed again by the same tab (a toggle, not a one-way switch).
        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='modeling-tree-drawer']").Should().BeEmpty();
            cut.Find("[data-testid='modeling-panel-tab-tree']")
                .GetAttribute("aria-expanded").Should().Be("false");
        });

        // The Preview tab closes every sheet: preview only (and it is the active tab then).
        cut.Find("[data-testid='modeling-panel-tab-inspector']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-inspector-sheet']").Should().NotBeNull());
        cut.Find("[data-testid='modeling-panel-tab-preview']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='modeling-inspector-sheet']").Should().BeEmpty();
            cut.FindAll("[data-testid='modeling-tree-drawer']").Should().BeEmpty();
            cut.Find("[data-testid='modeling-preview-panel']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-panel-tab-preview']").ClassList
                .Should().Contain("tm-modeling-editor__panel-tab--active", "the preview-only state is the active one");
        });
    }

    [Fact]
    public void Selecting_tree_element_opens_inspector_when_tablet()
    {
        using var cut = RenderTabletEditor();
        WaitForLoaded(cut);

        // Open the drawer, select an element inside it: the selection is an editing intent,
        // so the inspector sheet opens and the drawer yields (one sheet at a time).
        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-tree-drawer']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-tree-node-task-a']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-inspector-sheet']").Should().NotBeNull(
                "a tree selection opens the inspector sheet on a tablet");
            cut.FindAll("[data-testid='modeling-tree-drawer']").Should().BeEmpty();
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-selected-element-id").Should().Be("task-a");
        });
    }

    [Fact]
    public void Tablet_sheet_close_buttons_close_their_panel()
    {
        using var cut = RenderTabletEditor();
        WaitForLoaded(cut);

        // The sheet's own × (the shell's overlay close) closes it through the editor state.
        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-tree-drawer']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-panel-tab-inspector']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-inspector-sheet']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-sheet-close']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-inspector-sheet']").Should().BeEmpty());
    }

    // ── Sheets start closed (Fáze 6 review fix) ──────────────────────────────

    [Fact]
    public void Tablet_sheets_start_closed_and_issues_dock_collapsed()
    {
        using var cut = RenderTabletEditor();
        WaitForLoaded(cut);

        // Both sheets start closed: the preview is alone until the user opens one (the same
        // defaults the wireframe editor uses). The compact `_activePanel` is a different machine.
        cut.FindAll("[data-testid='modeling-tree-drawer']").Should().BeEmpty();
        cut.FindAll("[data-testid='modeling-inspector-sheet']").Should().BeEmpty();
        cut.Find("[data-testid='modeling-preview-panel']").Should().NotBeNull();

        // The tabs agree: both disclosures closed, Preview aria-pressed (the preview-only state).
        cut.Find("[data-testid='modeling-panel-tab-tree']").GetAttribute("aria-expanded").Should().Be("false");
        cut.Find("[data-testid='modeling-panel-tab-inspector']").GetAttribute("aria-expanded").Should().Be("false");
        cut.Find("[data-testid='modeling-panel-tab-preview']").GetAttribute("aria-pressed").Should().Be("true");

        // The issues dock starts collapsed on the tablet band (its own tablet flag).
        cut.Find("[data-testid='modeling-issues-toggle']").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public async Task Leaving_the_tablet_never_resets_the_desktop_dock_flags()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/layout-observer.js");
        module.SetupVoid("observe", invocation => true);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));
        WaitForLoaded(cut);

        var observer = () => cut.FindComponents<TmLayoutObserver>()
            .Single(component => component.Instance.Class == "tm-modeling-editor__layout");

        // The user collapsed the desktop tree dock BEFORE touching the band (the shell's own
        // collapse toggle rails it).
        await cut.InvokeAsync(() => observer().Instance.OnLayoutModeChanged("desktop"));
        cut.Find(".tm-editor-shell__panel-toggle--left").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-editor-shell__rail--left").Should().NotBeNull());

        // Tablet: its sheets start closed and the docked chrome is gone.
        await cut.InvokeAsync(() => observer().Instance.OnLayoutModeChanged("tablet"));
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='modeling-tree-drawer']").Should().BeEmpty();
            cut.FindAll(".tm-editor-shell__rail").Should().BeEmpty("the tablet band replaces the dock with overlays");
        });

        // Back to desktop: the pre-tablet dock state survives — the collapse is NOT reset to open.
        await cut.InvokeAsync(() => observer().Instance.OnLayoutModeChanged("desktop"));
        cut.WaitForAssertion(() => cut.Find(".tm-editor-shell__rail--left").Should().NotBeNull(
            "leaving the tablet must hand the dock back exactly as the user left it"));
        cut.FindAll("[data-testid='modeling-tree-drawer']").Should().BeEmpty();
    }

    // ── Resolved-layout tracking (Fáze 6 review fix: the editor's OWN observer) ──

    [Fact]
    public async Task Auto_editor_tracks_its_own_container_and_desktop_selection_stays_docked()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/layout-observer.js");
        module.SetupVoid("observe", invocation => true);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));
        WaitForLoaded(cut);

        var observer = () => cut.FindComponents<TmLayoutObserver>()
            .Single(component => component.Instance.Class == "tm-modeling-editor__layout");

        // Tablet → mobile → desktop: the editor must follow its OWN observer (the shell's raise
        // is only an idempotent duplicate), not run a raise behind.
        await cut.InvokeAsync(() => observer().Instance.OnLayoutModeChanged("tablet"));
        await cut.InvokeAsync(() => observer().Instance.OnLayoutModeChanged("mobile"));
        await cut.InvokeAsync(() => observer().Instance.OnLayoutModeChanged("desktop"));

        cut.WaitForAssertion(() =>
        {
            // The docked workspace is back; selecting a tree node keeps the tree DOCKED (no
            // tablet sheet may open from a desktop selection — a stale _resolvedLayout would).
            cut.Find("[data-testid='modeling-model-tree-panel']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-tree-node-task-a']").Click();
        });

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-selected-element-id").Should().Be("task-a");
            cut.Find("[data-testid='modeling-model-tree-panel']").Should().NotBeNull("the tree stays docked on desktop");
            cut.FindAll("[data-testid='modeling-tree-drawer']").Should().BeEmpty();
            cut.FindAll("[data-testid='modeling-inspector-sheet']").Should().BeEmpty();
        });

        // The open-editor dialog body decides the embedded editor's layout — it is NOT pinned to
        // the editor's resolved mode, so a desktop dialog body must never report tablet.
        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());
        cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor[data-editor-layout]")
            .GetAttribute("data-editor-layout").Should().NotBe("tablet",
                "the dialog DE resolves from the dialog body (Auto + 768/1100), not from the editor's resolved mode");
    }

    // ── Non-drag alternative (WCAG 2.5.7) ────────────────────────────────────

    [Fact]
    public async Task Insert_into_preview_action_raises_drop_at_viewport_center()
    {
        ModelingNodeDroppedEventArgs? dropped = null;
        using var cut = RenderTabletEditor(parameters => parameters
            .Add(p => p.OnNodeDropped, EventCallback.Factory.Create<ModelingNodeDroppedEventArgs>(this, args => dropped = args)));
        WaitForLoaded(cut);

        // The action lives in the tree rows — open the drawer first.
        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-tree-drawer']").Should().NotBeNull());

        var before = int.Parse(cut.Find("[data-testid='modeling-editor']").GetAttribute("data-diagram-node-count")!, System.Globalization.CultureInfo.InvariantCulture);

        // The tree row's "Insert into preview" action reuses the existing drop path.
        cut.Find("[data-testid='modeling-tree-insert-task-a']").Click();

        cut.WaitForAssertion(() =>
        {
            dropped.Should().NotBeNull("the insert action routes through OnNodeDropped");
            dropped!.Element.Id.Should().Be("task-a");
            dropped.NodeId.Should().StartWith("drop-");

            // The viewport center: the generated page is the 3000x2000 default canvas and the
            // fit view centers it, so the insert lands at its middle (the template half-size
            // slides the point by ≤90px).
            dropped.Point.X.Should().BeInRange(1300d, 1600d, "the drop lands around the page center");
            dropped.Point.Y.Should().BeInRange(900d, 1100d, "the drop lands around the page center");
        });

        cut.WaitForAssertion(() =>
        {
            var after = int.Parse(cut.Find("[data-testid='modeling-editor']").GetAttribute("data-diagram-node-count")!, System.Globalization.CultureInfo.InvariantCulture);
            after.Should().Be(before + 1, "the insert created a preview node");
        });
    }

    [Fact]
    public void Insert_into_preview_respects_allow_node_drop()
    {
        ModelingNodeDroppedEventArgs? dropped = null;
        using var cut = RenderTabletEditor(parameters => parameters
            .Add(p => p.AllowNodeDrop, false)
            .Add(p => p.OnNodeDropped, EventCallback.Factory.Create<ModelingNodeDroppedEventArgs>(this, args => dropped = args)));
        WaitForLoaded(cut);

        cut.FindAll("[data-testid='modeling-tree-insert-task-a']").Should().BeEmpty(
            "the non-drag alternative follows the AllowNodeDrop switch");
        dropped.Should().BeNull("no insert path may run while node drops are disabled");
    }

    // ── Touch targets (WCAG 2.5.8 / the phase contract) ──────────────────────

    [Fact]
    public void Touch_targets_reach_44px_on_coarse_pointer()
    {
        // bUnit cannot evaluate media queries: the contract is the stylesheet itself — every
        // control the phase names gets --tm-touch-target under (pointer: coarse).
        var editorCss = ReadPackageCss("TmModelingEditor.razor.css");
        editorCss.Should().Contain("@media (pointer: coarse)",
            "the editor pins its coarse-pointer targets");
        editorCss.Should().Contain("tm-modeling-editor__panel-tab", "the 'Panels' tabs are 44px tall on a coarse pointer");
        editorCss.Should().Contain("tm-modeling-editor__field select", "the header selects are 44px tall on a coarse pointer");
        editorCss.Should().Contain("tm-modeling-view-selector__field select", "the notation/viewpoint selects are 44px tall on a coarse pointer");

        var treeCss = ReadPackageCss("TmModelingModelTree.razor.css");
        treeCss.Should().Contain("@media (pointer: coarse)").And.Contain("tm-modeling-model-tree__node",
            "tree nodes reach the 44px target on a coarse pointer");

        var sourceCss = ReadPackageCss("TmModelingSourcePanel.razor.css");
        sourceCss.Should().Contain("@media (pointer: coarse)").And.Contain("tm-modeling-source-panel__load",
            "the 'Load model' button reaches the 44px target on a coarse pointer");

        var issueCss = ReadPackageCss("TmModelingIssuePanel.razor.css");
        issueCss.Should().Contain("@media (pointer: coarse)").And.Contain("tm-modeling-issue-panel__item",
            "issue rows reach the 44px target on a coarse pointer");
    }

    [Fact]
    public void Editor_css_resolves_the_dialog_presentation_from_the_editor_container()
    {
        // Fáze 6: the open-editor dialog is sized by the EDITOR's container, not the viewport —
        // the viewport @media is gone; a container query on the editor's own container decides.
        var css = ReadPackageCss("TmModelingEditor.razor.css");
        css.Should().Contain("@container tm-modeling-editor (width < 768px)",
            "the near-fullscreen dialog footprint keys off the editor container");
        css.Should().NotContain("@media (max-width: 48rem)",
            "the editor must not resolve its presentation from the viewport (responsive-conventions.md)");
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
}
