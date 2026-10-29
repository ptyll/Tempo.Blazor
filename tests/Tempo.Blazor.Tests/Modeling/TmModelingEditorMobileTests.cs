using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// The mobile presentation of <see cref="TmModelingEditor"/> (Fáze 7, G2 — viewing + light edits),
/// driven with a forced <c>LayoutMode=Mobile</c> (no CSS, no DOM measurement): a compact header
/// (title + state badge + "⋯" overflow), ONE panel at a time behind a real
/// <c>role="tablist"</c> strip ordered Preview / Tree / Inspector / Issues, a status card with
/// the four count tiles and the loaded time, and the in-flow F5 action bar (Reload / Change
/// view / Issues + More with Generate diagram and Open in editor — never a "Settings" item).
/// </summary>
public sealed class TmModelingEditorMobileTests : LocalizationTestBase
{
    public TmModelingEditorMobileTests()
    {
        Services.AddTempoBlazorModeling();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));
    }

    private IRenderedComponent<TmModelingEditor> RenderMobileEditor(
        Action<ComponentParameterCollectionBuilder<TmModelingEditor>>? configure = null)
        => Render<TmModelingEditor>(parameters =>
        {
            parameters.Add(p => p.LayoutMode, TmLayoutMode.Mobile);
            parameters.Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key);
            configure?.Invoke(parameters);
        });

    /// <summary>Like <see cref="RenderMobileEditor"/> but for tests that replace the provider:
    /// the caller's builder adds its own ProviderKey LAST (bUnit rejects duplicates).</summary>
    private IRenderedComponent<TmModelingEditor> RenderMobileEditorWithProvider(
        string providerKey,
        Action<ComponentParameterCollectionBuilder<TmModelingEditor>>? extra = null)
        => Render<TmModelingEditor>(parameters =>
        {
            parameters.Add(p => p.LayoutMode, TmLayoutMode.Mobile);
            extra?.Invoke(parameters);
            parameters.Add(p => p.ProviderKey, providerKey);
        });

    private static void WaitForLoaded(IRenderedComponent<TmModelingEditor> cut)
        => cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

    // ── Tablist semantics ────────────────────────────────────────────────────

    [Fact]
    public void Panel_tabs_expose_tablist_semantics()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        var tablist = cut.Find("[data-testid='modeling-panel-tabs'] [role='tablist']");
        tablist.Should().NotBeNull("the compact panel strip is a real tablist");

        var tabs = tablist.QuerySelectorAll("[role='tab']");
        tabs.Should().HaveCount(4, "Preview, Tree, Inspector and Issues are the four panels");

        var preview = tabs[0];
        preview.GetAttribute("aria-selected").Should().Be("true", "the preview is the initial panel");
        preview.GetAttribute("tabindex").Should().Be("0", "the selected tab owns the roving tab stop");
        preview.GetAttribute("aria-controls").Should().NotBeNullOrEmpty();

        // Every tab CONTROLS its panel through aria-controls; the ACTIVE tab's panel exists and
        // is the labelled tabpanel (TmTabs renders only the active body — the M26 budget).
        var activeTab = tabs[0];
        var panelId = activeTab.GetAttribute("aria-controls")!;
        var panel = cut.Find($"[data-testid='modeling-panel-tabs'] #{panelId}");
        panel.GetAttribute("role").Should().Be("tabpanel");
        panel.GetAttribute("aria-labelledby").Should().Be(activeTab.GetAttribute("id"));

        // Arrow keys move the selection (the APG tablist contract the compact strip used to lack).
        tabs[0].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-panel-tab-tree']").GetAttribute("aria-selected").Should().Be("true");
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-active-panel").Should().Be("tree");
        });

        tabs = tablist.QuerySelectorAll("[role='tab']");
        tabs[1].KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-panel-tab-preview']").GetAttribute("aria-selected").Should().Be("true"));
    }

    [Fact]
    public void Panel_tabs_order_starts_with_preview_and_includes_issues_with_badge()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new IssueMobileModelingModelProvider()));

        using var cut = RenderMobileEditorWithProvider(IssueMobileModelingModelProvider.Key);
        WaitForLoaded(cut);

        var labels = cut.FindAll("[data-testid='modeling-panel-tabs'] [role='tab']")
            .Select(tab => tab.TextContent.Trim())
            .ToList();

        labels[0].Should().Be("Preview", "the preview is the FIRST tab on mobile");
        labels.Should().ContainSingle(label => label.Contains("Tree"), "the tree tab uses the short mobile label");
        labels.Should().Contain(label => label.Contains("Inspector"));
        labels[^1].Should().StartWith("Issues", "the issues tab exists on mobile and is ordered last");

        // The issues tab carries the issue count as a visible badge (the shared model generates
        // one validation issue, the provider adds one more).
        var badge = cut.Find("[data-testid='modeling-panel-tab-issues'] .tm-tab__badge");
        int.Parse(badge.TextContent.Trim(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThanOrEqualTo(1, "the badge is the live issue count");
    }

    [Fact]
    public void Issues_tab_badge_hidden_when_there_are_no_issues()
    {
        // A model whose generation yields NO issues (no elements -> nothing to validate) must not
        // paint a bare "0" on the tab.
        using var cut = RenderMobileEditor(parameters => parameters
            .Add(p => p.ModelLoaderOverride, (ModelingModelRequest _, CancellationToken _) =>
                Task.FromResult(new ModelingModelDto
                {
                    Id = "issue-free",
                    Title = "Issue free"
                })));
        WaitForLoaded(cut);

        cut.FindAll("[data-testid='modeling-panel-tab-issues'] .tm-tab__badge")
            .Should().BeEmpty("a zero count hides the badge instead of showing a bare 0");
    }

    [Fact]
    public void Only_one_panel_visible_on_mobile()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        cut.FindAll(".tm-modeling-editor__panel").Should().ContainSingle(
            "the mobile layout renders exactly one panel body (the active tab)");

        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-modeling-editor__panel").Should().ContainSingle());

        cut.Find("[data-testid='modeling-panel-tab-issues']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-modeling-editor__panel").Should().ContainSingle();
            cut.Find("[data-testid='modeling-issues-panel']").Should().NotBeNull(
                "the issues tab shows the whole issue panel as the tab content");
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-active-panel").Should().Be("issues");
        });
    }

    // ── Compact header ───────────────────────────────────────────────────────

    [Fact]
    public void Mobile_header_is_compact_with_state_badge_and_overflow()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        var header = cut.Find("[data-testid='modeling-editor-toolbar']");
        header.QuerySelector(".tm-modeling-editor__title").Should().NotBeNull("the title stays in the header");
        header.QuerySelector(".tm-modeling-editor__state .tm-badge").Should().NotBeNull(
            "the state badge sits next to the title");

        // The "⋯" overflow exists and is a menu trigger.
        var overflow = header.QuerySelector(".tm-modeling-editor__more .tm-dropdown-trigger");
        overflow.Should().NotBeNull("the compact header carries the ⋯ overflow");
        overflow!.GetAttribute("aria-haspopup").Should().Be("true");

        // The provider/notation/viewpoint selects are NOT in the header anymore — they live in
        // the "Change view" sheet.
        header.QuerySelectorAll("select").Should().BeEmpty("the compact header drops the selects");

        // And the header is the compact single-row presentation (no stacked context group).
        header.QuerySelector(".tm-modeling-editor__context").Should().BeNull();
    }

    [Fact]
    public void Mobile_header_overflow_carries_change_view_generate_and_open_editor()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        cut.Find("[data-testid='modeling-editor-toolbar'] .tm-modeling-editor__more .tm-dropdown-trigger").Click();

        cut.WaitForAssertion(() =>
        {
            var menu = cut.Find(".tm-modeling-editor__more");
            menu.InnerHtml.Should().Contain("Change view", "the ⋯ menu opens the change-view sheet");
            menu.InnerHtml.Should().Contain("Generate diagram");
            menu.InnerHtml.Should().Contain("Open in editor");
            menu.InnerHtml.Should().NotContain("Settings", "the mockup's [E] 'Settings' entry is never rendered");
        });
    }

    // ── Change view sheet ────────────────────────────────────────────────────

    [Fact]
    public void Change_view_action_opens_sheet_with_provider_notation_viewpoint_selects()
    {
        using var cut = RenderMobileEditor(parameters => parameters.Add(p => p.ShowProviderSelector, true));
        WaitForLoaded(cut);

        cut.FindAll("[data-testid='modeling-change-view-sheet']").Should().BeEmpty("the sheet starts closed");

        cut.Find("[data-action-id='change-view']").Click();
        cut.WaitForAssertion(() =>
        {
            var sheet = cut.Find("[data-testid='modeling-change-view-sheet']");
            sheet.Should().NotBeNull("the action-bar 'Change view' item opens the sheet");
            sheet.QuerySelector("[data-testid='modeling-provider-select']").Should().NotBeNull();
            sheet.QuerySelector("[data-testid='modeling-notation-select']").Should().NotBeNull();
            sheet.QuerySelector("[data-testid='modeling-viewpoint-select'], [data-testid='modeling-viewpoint-empty']")
                .Should().NotBeNull("the viewpoint slot renders (select or the disabled placeholder)");
        });

        // Changing the notation regenerates the diagram and closes the sheet.
        cut.Find("[data-testid='modeling-change-view-sheet'] [data-testid='modeling-notation-select']")
            .Change("uml25");
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-notation").Should().Be("uml25");
            cut.FindAll("[data-testid='modeling-change-view-sheet']").Should().BeEmpty("a picked value closes the sheet");
        });
    }

    // ── Action bar ───────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_action_bar_reload_invokes_provider_reload()
    {
        var provider = new CountingModelingModelProvider();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(provider));

        using var cut = RenderMobileEditorWithProvider(CountingModelingModelProvider.Key);
        WaitForLoaded(cut);
        provider.LoadCount.Should().Be(1);

        cut.Find("[data-action-id='reload']").Click();

        cut.WaitForAssertion(() => provider.LoadCount.Should().Be(2, "the bar's Reload runs the provider reload"));
    }

    [Fact]
    public void Mobile_action_bar_more_contains_generate_and_open_in_editor()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        cut.Find(".tm-mobile-action-bar__more").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-action-generate']").Should().NotBeNull(
                "the More menu carries Generate diagram");
            cut.Find("[data-testid='modeling-action-open-editor']").Should().NotBeNull(
                "the More menu carries Open in editor");
        });
    }

    [Fact]
    public void Mobile_action_bar_has_no_settings_item()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        cut.FindAll("[data-action-id='settings']").Should().BeEmpty();
        cut.Find(".tm-mobile-action-bar__bar").TextContent.Should().NotContain("Settings");
    }

    [Fact]
    public void Mobile_action_bar_opens_editor_dialog_from_more()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-action-open-editor']").Should().NotBeNull());
        cut.Find("[data-testid='modeling-action-open-editor']").Click();

        cut.WaitForAssertion(() =>
        {
            var overlay = cut.Find("[data-testid='modeling-open-diagram-editor']");
            overlay.Should().NotBeNull("the More entry opens the open-editor dialog");
            overlay.QuerySelector(".tm-diagram-editor").Should().NotBeNull();
        });
    }

    // ── Status card ──────────────────────────────────────────────────────────

    [Fact]
    public void Status_card_renders_four_tiles_and_loaded_time()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new TimestampedModelingModelProvider()));

        using var cut = RenderMobileEditorWithProvider(TimestampedModelingModelProvider.Key);
        WaitForLoaded(cut);

        var card = cut.Find("[data-testid='modeling-status-card']");
        var tiles = card.QuerySelectorAll(".tm-modeling-editor__status-tile");
        tiles.Should().HaveCount(4, "Elements, Relationships, Nodes and Edges are the four tiles");

        var tileLabels = tiles.Select(tile => tile.TextContent).ToList();
        tileLabels.Should().Contain(text => text.Contains("Elements"));
        tileLabels.Should().Contain(text => text.Contains("Relationships"));
        tileLabels.Should().Contain(text => text.Contains("Nodes"), "the mobile tiles use the short Nodes label");
        tileLabels.Should().Contain(text => text.Contains("Edges"), "the mobile tiles use the short Edges label");
        // Nodes and Edges must stay distinguishable — no shared prefix collapsing them.
        tileLabels.Count(text => text.Contains("Nodes")).Should().Be(1);
        tileLabels.Count(text => text.Contains("Edges")).Should().Be(1);

        card.TextContent.Should().Contain("Last loaded:", "the card shows the loaded time");

        // The mobile layout replaces the desktop status strip — no duplicate count surfaces.
        cut.FindAll("[data-testid='modeling-status-strip']").Should().BeEmpty();
    }

    // ── Fáze 7 review: the empty / error / loading screens keep the mobile chrome ──

    [Fact]
    public void Mobile_empty_state_keeps_change_view_reachable()
    {
        using var cut = RenderMobileEditorWithProvider("tempo.tests.modeling.missing");
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("empty"));

        // The mobile shell is the always-present chrome: header + reduced action bar, with the
        // single-state screen where the tabs would be (no tab strip, no status card).
        cut.Find("[data-testid='modeling-editor-empty']").Should().NotBeNull();
        cut.Find(".tm-modeling-editor__mobile-shell").Should().NotBeNull("the empty screen keeps the mobile chrome");
        cut.Find("[data-testid='modeling-editor-toolbar']").Should().NotBeNull();
        cut.FindAll("[data-testid='modeling-panel-tabs']").Should().BeEmpty("no tab strip on the empty screen");
        cut.FindAll("[data-testid='modeling-status-card']").Should().BeEmpty("no status card on the empty screen");

        var actionIds = cut.FindAll(".tm-mobile-action-bar__bar [data-action-id]")
            .Select(element => element.GetAttribute("data-action-id"))
            .ToList();
        actionIds.Should().BeEquivalentTo(["reload", "change-view"],
            "the reduced bar carries only the recovery entries");
        cut.FindAll("[data-action-id='generate']").Should().BeEmpty();
        cut.FindAll("[data-action-id='open-editor']").Should().BeEmpty();

        // The recovery path: the "Change view" sheet opens from the reduced bar.
        cut.Find("[data-action-id='change-view']").Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-change-view-sheet']").Should().NotBeNull());
    }

    [Fact]
    public void Mobile_error_state_offers_reload()
    {
        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.ProviderKey, "tempo.tests.modeling.throwing")
            .Add(p => p.ModelLoaderOverride, (ModelingModelRequest _, CancellationToken _) =>
                throw new InvalidOperationException("Provider failure")));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("error");
            cut.Find("[data-testid='modeling-editor-error']").TextContent.Should().Contain("Provider failure");
        });

        cut.Find(".tm-modeling-editor__mobile-shell").Should().NotBeNull("the error screen keeps the mobile chrome");
        cut.Find("[data-action-id='reload']").Should().NotBeNull("the error screen offers the reload");
        cut.FindAll("[data-testid='modeling-panel-tabs']").Should().BeEmpty("no tab strip on the error screen");
        cut.FindAll("[data-testid='modeling-status-card']").Should().BeEmpty("no status card on the error screen");
    }

    [Fact]
    public void Mobile_issue_selection_switches_to_inspector_tab()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new NavigableIssueMobileModelingModelProvider()));

        using var cut = RenderMobileEditorWithProvider(NavigableIssueMobileModelingModelProvider.Key);
        WaitForLoaded(cut);

        cut.Find("[data-testid='modeling-panel-tab-issues']").Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-active-panel").Should().Be("issues"));

        cut.Find("[data-testid='modeling-issues-panel'] [data-testid='modeling-issue-0']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-active-panel").Should().Be("inspector",
                "an issue tap is an inspector intent on mobile too");
            cut.Find("[data-testid='modeling-inspector-panel']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-inspector-panel']").TextContent.Should().Contain("Approve request",
                "the inspector shows the selected element");
        });
    }

    // ── G2 scope: no drag on mobile ──────────────────────────────────────────

    [Fact]
    public void Drag_handles_and_drop_hint_not_rendered_on_mobile()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-model-tree-panel']").Should().NotBeNull());

        var node = cut.Find("[data-testid='modeling-tree-node-task-a']");
        node.GetAttribute("draggable").Should().BeNull("drag from the tree is not a mobile affordance");
        cut.FindAll(".tm-modeling-model-tree__drag-handle").Should().BeEmpty("no drag handle renders");
        cut.FindAll("[data-testid='modeling-tree-insert-task-a']").Should().BeEmpty(
            "the insert (drag alternative) action follows AllowNodeDrop=false on mobile");

        // The preview is not a drop target on mobile and never paints the drop hint.
        cut.Find("[data-testid='modeling-panel-tab-preview']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-preview-panel'] [data-testid='modeling-diagram-preview']")
                .GetAttribute("data-drop-enabled").Should().Be("false");
            cut.FindAll("[data-testid='modeling-diagram-preview-drop-hint']").Should().BeEmpty();
        });
    }

    // ── Fáze 7 review: mobile chrome details ─────────────────────────────────

    [Fact]
    public void Mobile_issues_tab_hides_the_collapse_toggle()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        cut.Find("[data-testid='modeling-panel-tab-issues']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-issues-panel'] [data-testid='modeling-issue-panel']").Should().NotBeNull();
            cut.FindAll("[data-testid='modeling-issues-panel'] [data-testid='modeling-issues-toggle']")
                .Should().BeEmpty("the tab body is the whole scroll surface — the header is a plain heading");
        });
    }

    [Fact]
    public void Mobile_header_overflow_sheet_is_titled_more_options()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        cut.Find("[data-testid='modeling-editor-toolbar'] .tm-modeling-editor__more .tm-dropdown-trigger").Click();

        cut.WaitForAssertion(() =>
            cut.Find(".tm-modeling-editor__more .tm-overlay-panel-sheet__title").TextContent.Should().Be("More options",
                "the icon-only ⋯ names its mobile sheet through the AriaLabel"));
    }

    // ── Open-in-editor dialog presentation ───────────────────────────────────

    [Fact]
    public void Open_editor_dialog_uses_fullscreen_sheet_presentation_on_mobile()
    {
        using var cut = RenderMobileEditor();
        WaitForLoaded(cut);

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-action-open-editor']").Should().NotBeNull());
        cut.Find("[data-testid='modeling-action-open-editor']").Click();

        cut.WaitForAssertion(() =>
        {
            var overlay = cut.Find("[data-testid='modeling-open-diagram-editor']");
            overlay.ClassList.Should().Contain("tm-modal--fullscreen-presentation",
                "the mobile dialog is the TmModal fullscreen presentation");
            // The embedded diagram editor gets a full-height body container.
            overlay.QuerySelector(".tm-modeling-editor__open-editor-body").Should().NotBeNull();
        });
    }
}

/// <summary>Provider serving the shared model with one sourceless issue, for the tab badge.</summary>
internal sealed class IssueMobileModelingModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.mobile-issue";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = ModelingEditorTestModels.CreateModel();
        model.Issues.Add(new ModelingIssueDto
        {
            Severity = ModelingIssueSeverity.Warning,
            Message = "Mobile badge issue"
        });
        return Task.FromResult(model);
    }
}

/// <summary>Provider serving the shared model with a NAVIGABLE issue (points at an element).</summary>
internal sealed class NavigableIssueMobileModelingModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.mobile-navigable-issue";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = ModelingEditorTestModels.CreateModel();
        model.Issues.Add(new ModelingIssueDto
        {
            Severity = ModelingIssueSeverity.Warning,
            Message = "Navigable mobile issue",
            SourceElementId = "task-a"
        });
        return Task.FromResult(model);
    }
}

/// <summary>Provider serving the shared model with a metadata loaded timestamp.</summary>
internal sealed class TimestampedModelingModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.timestamped";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = ModelingEditorTestModels.CreateModel();
        model.Metadata.LoadedAt = DateTimeOffset.Now;
        return Task.FromResult(model);
    }
}
