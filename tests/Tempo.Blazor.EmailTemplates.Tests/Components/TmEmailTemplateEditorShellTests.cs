using System.Text.RegularExpressions;
using Bunit;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 8 — the email editor renders the CORE <see cref="TmEditorShell"/> and owns no layout of its own.
/// The editor only forwards parameters (D7/D10/D20) and keys its own rules on the shell's
/// <c>data-layout</c>; breakpoints are the shell's decision (768/1200 for an editor whose panels need
/// the room), never a local <c>@container</c>/<c>@media</c> width literal.
/// </summary>
public class TmEmailTemplateEditorShellTests : EmailEditorTestBase
{
    private static TmEditorShell ShellOf(IRenderedComponent<TmEmailTemplateEditor> cut)
        => cut.FindComponent<TmEditorShell>().Instance;

    private IRenderedComponent<TmEmailTemplateEditor> RenderWith(
        EmailTemplateDocument? document = null,
        TmLayoutMode layoutMode = TmLayoutMode.Auto,
        MobilePanelPresentation presentation = MobilePanelPresentation.Sheet,
        EditorShellPanel activeMobilePanel = EditorShellPanel.None,
        bool toolboxCollapsed = false,
        Action<bool>? toolboxCollapsedChanged = null,
        EmailSaveResult? saveResult = null)
        => Render<TmEmailTemplateEditor>(p =>
        {
            p.Add(c => c.Document, document ?? DocWithTextBlock(out _));
            p.Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(saveResult ?? EmailSaveResult.Saved()));
            p.Add(c => c.LayoutMode, layoutMode);
            p.Add(c => c.MobilePanelPresentation, presentation);
            p.Add(c => c.ActiveMobilePanel, activeMobilePanel);
            p.Add(c => c.ToolboxCollapsed, toolboxCollapsed);
            p.Add(c => c.ToolbarStart, (Microsoft.AspNetCore.Components.RenderFragment)(b =>
                b.AddMarkupContent(0, "<span data-tm-host-start>Back</span>")));
            p.Add(c => c.ToolbarEnd, (Microsoft.AspNetCore.Components.RenderFragment)(b =>
                b.AddMarkupContent(0, "<span data-tm-host-end>Send test</span>")));
            if (toolboxCollapsedChanged is not null)
            {
                p.Add(c => c.ToolboxCollapsedChanged,
                    Microsoft.AspNetCore.Components.EventCallback.Factory.Create<bool>(
                        this, collapsed => toolboxCollapsedChanged(collapsed)));
            }
        });

    // ── 1. The shell replaces the local splitter ────────────────────────────────────────────

    [Fact]
    public void Editor_renders_the_core_editor_shell_and_drops_the_local_splitter()
    {
        var cut = RenderWith();

        cut.FindAll(".tm-splitter").Should().BeEmpty(
            "Fáze 8 removes the local 20/60/20 splitter — the shell owns the layout");
        cut.FindAll(".tm-editor-shell").Should().ContainSingle();

        var shell = cut.FindComponent<TmEditorShell>().Instance;
        shell.Left.Should().NotBeNull("the toolbox is the Left slot");
        shell.LeftRail.Should().NotBeNull("the collapsed toolbox is an icon rail");
        shell.Canvas.Should().NotBeNull();
        shell.Right.Should().NotBeNull("the inspector is the Right slot");
        shell.Toolbar.Should().NotBeNull();
        shell.StatusBar.Should().NotBeNull("the save status lives in the status bar");
        shell.MobileActions.Should().NotBeNull();
        shell.Extra.Should().BeNull("the email editor has exactly two panels");
    }

    [Fact]
    public void Every_shell_region_carries_its_stable_data_region_hook()
    {
        var cut = RenderWith();

        foreach (var region in new[] { "toolbar", "left", "right", "canvas", "status-bar" })
        {
            cut.FindAll($"[data-region='{region}']").Should().ContainSingle($"the {region} region must render once");
        }
    }

    // ── 2. Parameters are forwarded, never re-implemented ───────────────────────────────────

    [Fact]
    public void Editor_forwards_layout_parameters_to_the_shell()
    {
        var cut = RenderWith(layoutMode: TmLayoutMode.Tablet,
            presentation: MobilePanelPresentation.Tabs,
            activeMobilePanel: EditorShellPanel.Right);

        var shell = ShellOf(cut);
        shell.LayoutMode.Should().Be(TmLayoutMode.Tablet, "the editor never keeps a second copy of the layout decision");
        shell.InitialMode.Should().Be(TmLayoutMode.Desktop);
        shell.MobilePanelPresentation.Should().Be(MobilePanelPresentation.Tabs);
        shell.ActiveMobilePanel.Should().Be(EditorShellPanel.Right);
    }

    [Fact]
    public void Default_breakpoints_are_the_editor_pair_768_and_1200()
    {
        // D7: null means 768/1200 (the DiagramEditor pair) — NOT the shared 640/1024 default: the
        // toolbox and the inspector need the wider canvas before the editor can drop a panel.
        ShellOf(RenderWith()).Breakpoints.Should().Be(new TmLayoutBreakpoints(768, 1200));
    }

    [Fact]
    public void A_host_breakpoint_pair_is_forwarded_unchanged()
    {
        var custom = new TmLayoutBreakpoints(720, 1440);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.Breakpoints, custom));

        ShellOf(cut).Breakpoints.Should().Be(custom);
    }

    [Fact]
    public void Resolved_layout_changes_are_forwarded_to_the_host()
    {
        TmLayoutMode reported = TmLayoutMode.Auto;
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.ResolvedLayoutChanged,
                Microsoft.AspNetCore.Components.EventCallback.Factory.Create<TmLayoutMode>(this, mode => reported = mode)));

        cut.InvokeAsync(() => cut.FindComponent<TmEditorShell>().Instance.ResolvedLayoutChanged.InvokeAsync(TmLayoutMode.Mobile));

        reported.Should().Be(TmLayoutMode.Mobile, "ResolvedLayoutChanged is the host's only view of the shell decision");
    }

    // ── 3. ToolboxCollapsed is the D10 mapping onto CollapsedPanels.Left ─────────────────────

    [Fact]
    public void ToolboxCollapsed_is_the_shell_left_panel_state()
    {
        ShellOf(RenderWith(toolboxCollapsed: true)).CollapsedPanels
            .Should().HaveFlag(EditorShellPanel.Left, "D10: ToolboxCollapsed maps onto CollapsedPanels.Left");

        ShellOf(RenderWith(toolboxCollapsed: false)).CollapsedPanels
            .Should().NotHaveFlag(EditorShellPanel.Left);
    }

    [Fact]
    public void Expanding_the_shell_rail_raises_ToolboxCollapsedChanged()
    {
        var raised = new List<bool>();
        var cut = RenderWith(toolboxCollapsed: true, toolboxCollapsedChanged: raised.Add);

        // The shell renders the collapsed palette as a rail with ONE expand control; the editor's own
        // palette arrow is off (ShowCollapseToggle=false), so this is the only way back.
        cut.Find("[data-region='rail-left'] button.tm-editor-shell__panel-toggle").Click();

        raised.Should().ContainSingle().Which.Should().BeFalse("expanding the rail is ToolboxCollapsed=false");
    }

    [Fact]
    public void Expanding_the_rail_without_a_binding_expands_the_palette()
    {
        // Uncontrolled: the editor owns the state it renders (a host that binds nothing still gets a
        // working toggle instead of a rail that never opens).
        var cut = RenderWith(toolboxCollapsed: true);

        cut.Find("[data-region='rail-left'] button.tm-editor-shell__panel-toggle").Click();

        cut.FindAll("[data-region='rail-left']").Should().BeEmpty();
        cut.FindAll("aside.tm-editor-shell__panel--left [data-tm-toolbox]").Should().ContainSingle();
    }

    [Fact]
    public void Collapsing_the_docked_toolbox_raises_ToolboxCollapsedChanged()
    {
        var raised = new List<bool>();
        var cut = RenderWith(toolboxCollapsedChanged: raised.Add);

        cut.Find("[data-region='left'] ~ button.tm-editor-shell__panel-toggle--left").Click();

        raised.Should().ContainSingle().Which.Should().BeTrue();
    }

    // ── 4. The palette/chrome never duplicates the shell's own controls ──────────────────────

    [Fact]
    public void The_palette_loses_its_own_collapse_arrow()
    {
        var cut = RenderWith();

        cut.FindAll("[data-tm-toolbox]").Should().ContainSingle();
        cut.FindAll("[data-tm-toolbox-collapse]").Should().BeEmpty(
            "the shell's panel toggle is the one control for that state (ShowCollapseToggle=false)");
    }

    // ── 5. Desktop and tablet ───────────────────────────────────────────────────────────────

    [Fact]
    public void Desktop_renders_the_toolbar_row_the_docked_panels_and_the_status_bar()
    {
        var cut = RenderWith();

        cut.FindAll("[data-region='toolbar'] [data-tm-group]").Should().HaveCountGreaterThanOrEqualTo(4);
        cut.Find("[data-region='header']").TextContent.Trim().Should().BeEmpty(
            "the header slot is supplied at every layout but renders nothing above the mobile breakpoint");
        cut.Find("[data-region='header']").TextContent.Trim().Should().BeEmpty(
            "the header slot is supplied at every layout but renders nothing above the mobile breakpoint");
        cut.FindAll("[data-tm-toolbar-start]").Should().ContainSingle();
        cut.FindAll("[data-tm-save]").Should().ContainSingle("the primary Save stays on the command row");
        cut.Find("[data-tm-block='text']").Click();
        cut.FindAll("[data-region='status-bar'] [data-tm-save-badge]").Should().ContainSingle(
            "the save status is chrome of the status bar, not of the command row");
        cut.FindAll("[data-region='left'] [data-tm-toolbox]").Should().ContainSingle();
        cut.FindAll("[data-region='right'] [data-tm-properties]").Should().ContainSingle();
        cut.FindAll("[data-region='canvas'] [data-tm-canvas]").Should().ContainSingle();
        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(2);
    }

    [Fact]
    public void Tablet_keeps_the_docked_panels_and_rails_the_toolbox()
    {
        // F6-TABLET-DOCKED-PANELS: the same docked chrome as desktop, at most one expanded panel — the
        // shell rails the panel that did not win, so the editor carries no tablet logic at all.
        var cut = RenderWith(layoutMode: TmLayoutMode.Tablet);

        cut.FindAll("[data-region='rail-left']").Should().ContainSingle();
        cut.FindAll("aside.tm-editor-shell__panel--right").Should().ContainSingle();
        cut.FindAll("[data-region='right'] [data-tm-properties]").Should().ContainSingle();
        cut.FindAll(".tm-side-panel").Should().BeEmpty("tablet is docked, never an overlay panel");
        cut.FindAll("[data-region='toolbar']").Should().ContainSingle("tablet keeps the command row");
    }

    [Fact]
    public void Tablet_expanding_the_rail_swaps_the_expanded_panel_to_the_palette()
    {
        // The browser behaviour the tablet E2E depends on: expanding the railed palette must dock it and
        // rail the INSPECTOR instead (never both expanded, never the palette back in the rail).
        var cut = RenderWith(layoutMode: TmLayoutMode.Tablet);

        cut.FindAll("[data-region='rail-left']").Should().ContainSingle();
        cut.FindAll("[data-region='left'] [data-tm-toolbox]").Should().BeEmpty();

        cut.Find("[data-region='rail-left'] button.tm-editor-shell__panel-toggle").Click();

        cut.FindAll("[data-region='left'] [data-tm-toolbox]").Should().ContainSingle(
            "expanding the palette docks it");
        cut.FindAll("aside.tm-editor-shell__panel--right").Should().BeEmpty(
            "the inspector is railed instead — at most one expanded panel on a tablet");
        cut.FindAll("[data-region='rail-right']").Should().ContainSingle();
    }

    [Fact]
    public void The_left_rail_offers_the_block_icons_as_touch_targets()
    {
        var cut = RenderWith(toolboxCollapsed: true);

        var rail = cut.Find("[data-region='rail-left']");
        var tiles = rail.QuerySelectorAll("[data-tm-rail-block]");
        tiles.Length.Should().BeGreaterThan(0, "the collapsed toolbox keeps blocks reachable as icons");

        foreach (var tile in tiles)
        {
            tile.GetAttribute("title").Should().NotBeNullOrWhiteSpace();
        }
    }

    // ── 6. Mobile ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_renders_a_compact_header_with_the_primary_save()
    {
        var cut = RenderWith(layoutMode: TmLayoutMode.Mobile);

        var header = cut.Find("[data-region='header']");
        header.QuerySelector("[data-tm-save]").Should().NotBeNull("D20: Save lives in the mobile header");
        header.QuerySelector("[data-tm-toolbar-start]").Should().NotBeNull();
        cut.FindAll("[data-region='toolbar']").Should().BeEmpty("the shell drops the Toolbar slot on mobile");
        cut.FindAll("[data-region='header'] [data-tm-mobile-overflow]").Should().ContainSingle();
    }

    [Fact]
    public void Mobile_overflow_carries_import_export_validate_and_shortcuts()
    {
        var cut = RenderWith(layoutMode: TmLayoutMode.Mobile);

        cut.Find("[data-tm-mobile-overflow] .tm-popover__trigger").Click();

        var panel = cut.Find("[data-region='header'] [data-tm-mobile-overflow-panel]");
        panel.QuerySelector("[data-tm-import-btn]").Should().NotBeNull();
        panel.QuerySelector("[data-tm-mjml-btn]").Should().NotBeNull();
        panel.QuerySelector("[data-tm-validate-btn]").Should().NotBeNull();
        panel.QuerySelector("[data-tm-help-btn]").Should().NotBeNull();
        cut.FindAll("[data-region='header'] [data-tm-mjml-btn]").Should().ContainSingle(
            "the export action is rendered exactly once — the desktop toolbar is not rendered on mobile");
    }

    [Fact]
    public void Mobile_action_bar_offers_only_undo_redo_preview_and_validate()
    {
        var cut = RenderWith(layoutMode: TmLayoutMode.Mobile);

        var items = ShellOf(cut).MobileActions!;
        items.Select(item => item.Id).Should().Equal("undo", "redo", "preview", "validate");
        items.Should().NotContain(item => item.Id == "save",
            "D20: Save is in the header, never in the action bar");

        var bar = cut.FindComponent<TmMobileActionBar>();
        bar.Markup.Should().NotContain("data-tm-save\"");
    }

    [Fact]
    public void Mobile_sheet_presents_blocks_and_properties_as_tabs()
    {
        var cut = RenderWith(layoutMode: TmLayoutMode.Mobile);

        var sheet = cut.Find("[data-region='main'] .tm-editor-shell__sheet");
        var tabs = sheet.QuerySelectorAll(".tm-editor-shell__tabs [role='tab']").Select(t => t.TextContent.Trim()).ToArray();
        tabs.Should().Equal("EmailEditor_Panel_Blocks", "EmailEditor_Panel_Properties");
        cut.FindAll("[data-region='left'] [data-tm-toolbox]").Should().ContainSingle();
        cut.FindAll("[data-region='right'] [data-tm-properties]").Should().ContainSingle();

        // The sheet opens high enough to show the palette, not just its search box: the default half snap
        // hid the first block tile on a phone that has its own page chrome above the editor.
        ShellOf(cut).MobileSheetSnapPoints.Should().NotBeNull().And.Contain(0.65);
        ShellOf(cut).MobileSheetSnapIndex.Should().Be(0);
    }

    [Fact]
    public void Selecting_a_block_switches_the_mobile_sheet_to_properties()
    {
        var document = DocWithTextBlock(out var blockId);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        cut.Find($"[data-tm-block-id='{blockId}']").Click();

        cut.FindAll(".tm-editor-shell__tabs [role='tab'][aria-selected='true']").Select(t => t.TextContent.Trim())
            .Should().ContainSingle().Which.Should().Be("EmailEditor_Panel_Properties",
                "a selected block is edited in the properties panel — the mobile sheet must follow the selection");
    }

    [Fact]
    public void Selecting_a_block_reports_the_mobile_panel_to_the_host()
    {
        EditorShellPanel reported = EditorShellPanel.None;
        var document = DocWithTextBlock(out var blockId);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.ActiveMobilePanelChanged,
                Microsoft.AspNetCore.Components.EventCallback.Factory.Create<EditorShellPanel>(this, panel => reported = panel)));

        cut.Find($"[data-tm-block-id='{blockId}']").Click();

        reported.Should().Be(EditorShellPanel.Right,
            "the host can bind ActiveMobilePanel, so the editor reports the switch it made");
    }

    [Fact]
    public void Adding_a_block_collapses_the_mobile_sheet()
    {
        var cut = RenderWith(layoutMode: TmLayoutMode.Mobile);
        cut.FindAll("[data-region='main'] .tm-editor-shell__sheet").Should().ContainSingle();

        cut.Find("[data-tm-block='text']").Click();

        cut.FindAll("[data-region='main'] .tm-editor-shell__sheet").Should().BeEmpty(
            "the new block has to be visible on the canvas, so the sheet gets out of the way");
        cut.FindAll(".tm-editor-shell__panel-toggle--mobile").Should().ContainSingle(
            "the collapsed bar is the one way back into the panels");
    }

    [Fact]
    public void Mobile_tabs_presentation_renders_a_left_canvas_right_tablist()
    {
        var cut = RenderWith(layoutMode: TmLayoutMode.Mobile, presentation: MobilePanelPresentation.Tabs);

        cut.FindAll("[data-region='main'] .tm-editor-shell__sheet").Should().BeEmpty("Tabs has no sheet");
        var strip = cut.Find(".tm-editor-shell__tabs--region");
        strip.QuerySelectorAll("[role='tab']").Select(t => t.TextContent.Trim()).Should().Equal(
            "EmailEditor_Panel_Blocks", "EmailEditor_Panel_Canvas", "EmailEditor_Panel_Properties");
    }

    // ── 7. Failure/conflict stays readable in the shell ─────────────────────────────────────

    [Fact]
    public void Failed_save_alert_renders_inside_the_status_bar()
    {
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Failed("Server said no"))));

        cut.Find("[data-tm-save]").Click();

        var alert = cut.Find("[data-region='status-bar'] [data-tm-save-alert]");
        alert.GetAttribute("data-tm-save-alert").Should().Be("failed");
        alert.TextContent.Should().Contain("Server said no",
            "the host's reason must be readable without hovering a truncated pill");
        cut.FindAll("[data-region='status-bar'] [data-tm-save-retry]").Should().ContainSingle();
    }

    [Fact]
    public void ReadOnly_editor_shows_its_badge_in_the_status_bar_and_no_save_button()
    {
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.ReadOnly, true));

        cut.FindAll("[data-region='status-bar'] [data-tm-readonly-badge]").Should().ContainSingle();
        cut.FindAll("[data-tm-save]").Should().BeEmpty();
    }

    // ── 8. No local breakpoints in email CSS ───────────────────────────────────────────────

    [Fact]
    public void Email_stylesheets_declare_no_container_or_media_width_literals()
    {
        // F6-SHELL-BREAKPOINTS: the shell decides the layout from the CONTAINER width; a local
        // @container/@media width step with 640/1024 would contradict the markup branch, so the email
        // rules key on the shell's data-layout attribute instead.
        var directory = Path.Combine(RepositoryRoot(), "src", "Tempo.Blazor.EmailTemplates", "Components");
        var container = new Regex(@"@container\b");
        var widthMedia = new Regex(@"@media[^{]*(min|max)-width", RegexOptions.IgnoreCase);
        var offenders = new List<string>();

        var comment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline);

        foreach (var path in Directory.GetFiles(directory, "*.razor.css"))
        {
            // Comments mention the forbidden at-rules by name; only real rules count.
            var text = comment.Replace(File.ReadAllText(path), string.Empty);
            offenders.AddRange(container.Matches(text).Select(_ => $"{Path.GetFileName(path)}: @container"));
            offenders.AddRange(widthMedia.Matches(text).Select(m => $"{Path.GetFileName(path)}: {m.Value.Trim()}"));
        }

        offenders.Should().BeEmpty("email CSS keys on the shell's data-layout, never on a width literal");
    }

    [Fact]
    public void The_editor_stylesheet_targets_the_shell_layout_attribute()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Tempo.Blazor.EmailTemplates",
            "Components", "TmEmailTemplateEditor.razor.css"));

        css.Should().Contain("[data-layout=\"mobile\"]", "layout-dependent email rules follow the shell's decision");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
