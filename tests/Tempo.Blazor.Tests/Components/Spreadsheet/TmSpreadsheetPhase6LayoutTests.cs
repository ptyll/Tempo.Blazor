using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// Redesign phase 6 — the RESPONSIVE CHROME. The spreadsheet is embedded in a Notion block or a modal, so
/// the layout is decided by the width of its OWN container (a container query plus the core
/// <c>TmLayoutObserver</c>/<c>TmLayoutMode</c>), never by the viewport: a 390px block inside a 1440px page
/// is a phone.
/// <para>
/// bUnit cannot evaluate container queries or resize a box, so every test here pins
/// <see cref="TmSpreadsheet.LayoutMode"/> — the same parameter a host uses to force a mode — and asserts the
/// MARKUP and MODEL contract that the CSS then paints. The layout itself is verified in the browser
/// (<c>SpreadsheetPhase6ResponsiveE2ETests</c>, screenshots <c>phase6-*</c>).
/// </para>
/// </summary>
[Collection("SpreadsheetClipboard")]
public class TmSpreadsheetPhase6LayoutTests : LocalizationTestBase
{
    private const string AdvancedToolIdsHint =
        "text-to-columns, remove-duplicates, data-validation, name-manager, paste-special, format-painter, " +
        "merge-cells, borders, thousands and the workbook open/download are NOT part of the mobile (G2) set";

    private static readonly string[] MobileForbiddenCommandIds =
    [
        "file-open", "file-download", "text-to-columns", "remove-duplicates", "data-validation",
        "name-manager", "paste-special", "format-painter", "merge-cells", "borders", "custom-sort",
        "insert-row", "delete-row", "insert-column", "delete-column", "grid-lines",
    ];

    // ── 1. LayoutMode forces the mode and the classes ──────────────────────────

    [Fact]
    public void Spreadsheet_LayoutModeParameter_ForcesTabletAndMobileClasses()
    {
        var desktop = Render<TmSpreadsheet>();
        var desktopRoot = desktop.Find(".tm-spreadsheet");
        desktopRoot.GetAttribute("data-layout").Should().Be("desktop",
            "LayoutMode=Auto defaults to the desktop chrome before the first measurement");
        desktopRoot.ClassList.Should().Contain("tm-spreadsheet--layout-desktop");

        var tablet = Render<TmSpreadsheet>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Tablet));
        var tabletRoot = tablet.Find(".tm-spreadsheet");
        tabletRoot.GetAttribute("data-layout").Should().Be("tablet");
        tabletRoot.ClassList.Should().Contain("tm-spreadsheet--layout-tablet");
        tablet.Find(".tm-spreadsheet__layout").GetAttribute("data-layout").Should().Be("tablet",
            "the core layout observer cascades and renders the resolved mode on its own root");

        var mobile = Render<TmSpreadsheet>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile));
        var mobileRoot = mobile.Find(".tm-spreadsheet");
        mobileRoot.GetAttribute("data-layout").Should().Be("mobile");
        mobileRoot.ClassList.Should().Contain("tm-spreadsheet--layout-mobile");
        mobile.Find(".tm-spreadsheet__layout").GetAttribute("data-layout").Should().Be("mobile");
    }

    // ── 2. The tablet model: two rows, the reference's command set ─────────────

    [Fact]
    public void SpreadsheetToolbarLayout_Tablet_TwoRows_GroupsAsDropdowns()
    {
        var rows = SpreadsheetToolbarLayout.Rows(TmLayoutMode.Tablet, showFileCommands: true);

        rows.Should().HaveCount(2, "the tablet chrome is TWO compact rows instead of the tab strip");

        var primary = rows[0];
        primary.Id.Should().Be("primary");
        primary.Commands.Select(c => c.Id).Should().ContainInOrder(
            ["file-open", "undo", "redo", "paste", "cut", "copy", "format-cells", "insert-image", "insert-link"],
            "row 1 is the reference's primary command set in its own order (the download sits behind More: the "
            + "reference's one \"Soubor\" control is documented as a deviation)");
        primary.Commands.Single(c => c.Id == "file-download").Priority.Should()
            .Be(ToolbarButtonPriority.OverflowOnly, "the workbook download lives in More while file is one control");
        primary.Commands.Should().OnlyHaveUniqueItems(c => c.Id, "a command never renders twice");
        primary.Commands.Count(c => c.Priority != ToolbarButtonPriority.OverflowOnly)
            .Should().BeLessThanOrEqualTo(10,
                "the tablet row 1 fits ten commands plus the More trigger of the core overflow toolbar");

        var formatting = rows[1];
        formatting.Id.Should().Be("formatting");
        var formattingIds = formatting.Commands.Select(c => c.Id).ToList();
        formattingIds.Should().Contain([
            "font-family", "font-size", "bold", "italic", "underline", "text-color", "fill-color",
            "align-left", "align-center", "align-right", "number-format", "percent", "thousands",
            "decimals-increase", "decimals-decrease", "borders", "filter", "sort-asc", "sort-desc",
        ]);
        formatting.Commands.Should().OnlyHaveUniqueItems(c => c.Id);

        // The selects and the border split render as toolbar ITEMS (the core TmToolbarItem), i.e. they
        // keep a "More" representation instead of vanishing on a narrow row.
        rows.SelectMany(r => r.Commands)
            .Where(c => c.Kind is SpreadsheetToolbarCommandKind.Select or SpreadsheetToolbarCommandKind.Border)
            .Should().NotBeEmpty();

        // Full editing starts at the tablet: the advanced tools stay reachable, but only through the
        // row's "More" menu (OverflowOnly), never on the row itself.
        string[] tabletOverflowOnly =
        [
            "text-to-columns", "remove-duplicates", "data-validation", "name-manager", "paste-special",
            "format-painter", "merge-cells", "insert-row", "delete-row", "insert-column", "delete-column",
        ];
        var advanced = rows.SelectMany(r => r.Commands)
            .Where(c => tabletOverflowOnly.Contains(c.Id))
            .ToList();
        advanced.Should().NotBeEmpty("the tablet keeps the advanced data tools, full editing starts here");
        advanced.Should().OnlyContain(c => c.Priority == ToolbarButtonPriority.OverflowOnly,
            "an advanced tool is offered in \"More\" on the tablet, not on the compact row");
    }

    // ── 3. The mobile model is the G2 set ─────────────────────────────────────

    [Fact]
    public void SpreadsheetToolbarLayout_Mobile_OnlyBasicCommands()
    {
        var rows = SpreadsheetToolbarLayout.Rows(TmLayoutMode.Mobile, showFileCommands: true);

        rows.Should().HaveCount(1, "the mobile chrome is ONE basic formatting row");
        var ids = rows[0].Commands.Select(c => c.Id).ToList();

        ids.Should().Contain(["bold", "italic", "underline", "number-format"],
            "basic formatting is the G2 mobile set");
        ids.Should().Contain(id => id.StartsWith("align-"),
            "the alignment group is part of the basic mobile formatting set");

        foreach (var forbidden in MobileForbiddenCommandIds)
        {
            ids.Should().NotContain(forbidden,
                $"mobile is viewing plus basic editing (G2): {AdvancedToolIdsHint}");
        }

        rows[0].Commands.Should().OnlyHaveUniqueItems(c => c.Id);

        SpreadsheetToolbarLayout.Rows(TmLayoutMode.Desktop, showFileCommands: true)
            .Should().BeEmpty("the desktop keeps the phase-5 tab strip, not a compact row");
    }

    // ── 4. The mobile action bar ─────────────────────────────────────────────

    [Fact]
    public void Spreadsheet_Mobile_RendersMobileActionBar_InsertFormatImageMore()
    {
        var mobile = Render<TmSpreadsheet>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        var bar = mobile.FindComponent<TmMobileActionBar>();
        bar.Instance.LayoutMode.Should().Be(TmLayoutMode.Mobile,
            "the bar must not re-measure the container the spreadsheet already resolved");

        var actionIds = bar.Instance.Items!.Select(i => i.Id).ToList();
        actionIds.Should().ContainInOrder(["insert", "format", "insert-image"],
            "the reference's three tiles: Vložit / Formát / Vložit obrázek");
        actionIds.Should().Contain(id => id.StartsWith("summary"),
            "hidden status-bar aggregations move into the bar's More menu on a phone");
        bar.Instance.MaxVisible.Should().Be(3, "three tiles stay on the bar, the rest is the More menu");
        actionIds.Count.Should().BeGreaterThan(3, "the More menu carries the sheet/data/zoom groups");

        // The bar is INSIDE the spreadsheet chrome (in flow under the sheet tabs), not a sibling page element.
        mobile.FindAll(".tm-spreadsheet .tm-mobile-action-bar").Count.Should().Be(1);

        var desktop = Render<TmSpreadsheet>();
        desktop.FindComponents<TmMobileActionBar>().Should().BeEmpty(
            "the desktop/tablet layouts keep the status bar and never mount the mobile bar");

        var embedded = Render<TmSpreadsheet>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Mode, SpreadsheetMode.Embedded));
        embedded.FindComponents<TmMobileActionBar>().Should().BeEmpty(
            "an embedded (read-only) spreadsheet at phone width is viewing only, so it offers no action bar");
    }

    // ── 5. Status bar hidden on mobile, aggregations in More ─────────────────

    [Fact]
    public void Spreadsheet_Mobile_HidesStatusBar_AggregationsInMore()
    {
        var mobile = Render<TmSpreadsheet>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        mobile.FindAll(".tm-spreadsheet-statusbar").Should().BeEmpty(
            "the phone chrome has no status bar — the zoom and the aggregations move to the More menu");

        var items = mobile.FindComponent<TmMobileActionBar>().Instance.Items!;
        var summary = items.Where(i => i.Group == "summary").ToList();
        summary.Should().NotBeEmpty("the status-bar aggregations are re-offered under \"Summary\" in More");
        summary.Should().OnlyContain(i => i.Disabled,
            "an aggregation is information, not an action, so its More entry is inert");
        summary.First().Label.Should().StartWith("Count",
            "the status-bar aggregations keep their localized labels in the More menu");

        var zoom = items.Where(i => i.Group == "zoom").Select(i => i.Id).ToList();
        zoom.Should().Contain(["zoom-out", "zoom-in", "zoom-reset"],
            "scroll/zoom is part of the G2 mobile set, so the status-bar zoom controls survive in More");

        var tablet = Render<TmSpreadsheet>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Tablet));
        tablet.FindAll(".tm-spreadsheet-statusbar").Count.Should().Be(1,
            "the tablet keeps the status bar of the reference");
    }

    // ── 6. SP3: 100 % height only in the mobile mode ─────────────────────────

    [Fact]
    public void Spreadsheet_Mobile_HeightIs100Percent_OnlyInMobileMode()
    {
        Render<TmSpreadsheet>().Find(".tm-spreadsheet").GetAttribute("style")
            .Should().Contain("height: 600px", "the desktop chrome keeps its fixed default height");

        Render<TmSpreadsheet>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Tablet))
            .Find(".tm-spreadsheet").GetAttribute("style")
            .Should().Contain("height: 600px", "only the mobile layout fills its container");

        Render<TmSpreadsheet>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile))
            .Find(".tm-spreadsheet").GetAttribute("style")
            .Should().Contain("height: 100%", "SP3: the phone layout fills its container");
    }

    // ── 7. The grid density follows the layout on Auto ───────────────────────

    [Fact]
    public void Spreadsheet_Mobile_AutoDensity_ResolvesToComfortable()
    {
        var mobile = Render<TmSpreadsheet>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile));
        mobile.Find(".tm-spreadsheet-canvas-grid").GetAttribute("data-density")
            .Should().Be("comfortable", "the reference draws taller phone rows, so Auto becomes Comfortable");

        var desktop = Render<TmSpreadsheet>();
        desktop.Find(".tm-spreadsheet-canvas-grid").GetAttribute("data-density")
            .Should().Be("compact", "the desktop keeps the compact rows");

        var pinned = Render<TmSpreadsheet>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Density, SpreadsheetDensity.Compact));
        pinned.Find(".tm-spreadsheet-canvas-grid").GetAttribute("data-density")
            .Should().Be("compact", "an explicit Density always wins over the layout default");
    }

    // ── 8. The filter dropdown becomes a sheet on the mobile layout ──────────

    [Fact]
    public void FilterDropdown_MobilePresentation_RendersAsSheet_WithStickyFooter()
    {
        var sheet = new SpreadsheetSheet();
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "Fruit", DisplayValue = "Fruit", DataType = SpreadsheetDataType.Text };
        sheet.Cells["A2"] = new SpreadsheetCell { Value = "Apple", DisplayValue = "Apple", DataType = SpreadsheetDataType.Text };
        var filter = new SpreadsheetAutoFilter(new SpreadsheetRange(0, 0, 1, 0));

        var desktop = Render<TmSpreadsheetFilterDropdown>(p => p
            .Add(c => c.Sheet, sheet)
            .Add(c => c.Filter, filter)
            .Add(c => c.ColumnIndex, 0)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));
        desktop.FindAll(".tm-spreadsheet-filter-dropdown--sheet").Should().BeEmpty();
        desktop.Find(".tm-spreadsheet-filter-dropdown").ClassList.Should().NotContain("tm-spreadsheet-filter-dropdown--sheet");

        var mobile = Render<TmSpreadsheetFilterDropdown>(p => p
            .Add(c => c.Sheet, sheet)
            .Add(c => c.Filter, filter)
            .Add(c => c.ColumnIndex, 0)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        var panel = mobile.Find(".tm-spreadsheet-filter-dropdown");
        panel.ClassList.Should().Contain("tm-spreadsheet-filter-dropdown--sheet",
            "the header filter is a bottom sheet on a phone");
        mobile.FindAll(".tm-spreadsheet-filter-host").Count.Should().Be(1,
            "the sheet surface is the core TmDrawer, never a package-local sheet");
        mobile.Find(".tm-spreadsheet-filter-dropdown__actions").ClassList
            .Should().Contain("tm-spreadsheet-filter-dropdown__actions--sticky",
                "the sheet keeps Cancel / OK pinned to its bottom edge");
        mobile.FindAll(".tm-spreadsheet-filter-dropdown__item").Should()
            .OnlyContain(item => item.ClassList.Contains("tm-touch-target"),
                "a sheet row is a 44px touch target");
    }

    // ── 9. The grid context menu becomes a sheet on the mobile layout ────────

    [Fact]
    public void ContextMenu_Mobile_RendersAsSheet()
    {
        // Review round: the menu may NOT be rendered inside the grid any more (the grid sits in the scrolling
        // content and in the layout container, whose containment repositions a position:fixed surface). The grid
        // hands the gesture to its host, and TmSpreadsheet renders the sheet as a SIBLING of the container.
        var grid = Render<TmSpreadsheetCanvasGrid>(p => p
            .Add(c => c.Sheet, new SpreadsheetSheet())
            .Add(c => c.RenderContextMenu, false)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        SpreadsheetContextMenuRequest? request = null;
        var host = Render<TmSpreadsheet>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Mode, SpreadsheetMode.Full));
        var gridHost = host.FindComponent<TmSpreadsheetCanvasGrid>();
        gridHost.Instance.RenderContextMenu.Should().BeFalse("the host owns the menu");

        // Drive the grid's handler and forward the request into the host's menu surface.
        host.InvokeAsync(() => gridHost.Instance.OnCanvasContextMenu(10, 10, 10, 10));
        host.Render();

        host.FindAll(".tm-spreadsheet-context-sheet").Count.Should().Be(1,
            "the phone's cell menu is a core TmDrawer sheet rendered OUTSIDE the layout container");
        host.Find(".tm-spreadsheet-context-menu").ClassList.Should().Contain("tm-spreadsheet-context-menu--sheet");
        host.Find(".tm-spreadsheet-context-menu").GetAttribute("role").Should().Be("menu");
        host.FindAll(".tm-spreadsheet-context-menu__item").Should()
            .OnlyContain(item => item.GetAttribute("role") == "menuitem",
                "the sheet's entries are real menuitem buttons, not divs");

        // The sheet is a sibling of the container, never a descendant of it.
        host.FindAll(".tm-spreadsheet__layout .tm-spreadsheet-context-sheet").Should().BeEmpty(
            "a fixed menu inside the container would be positioned against the card instead of the viewport");

        // And the standalone grid no longer renders a menu at all (it raises the request instead).
        grid.Render();
        grid.FindAll(".tm-spreadsheet-context-menu").Should().BeEmpty();
        _ = request;
    }

    // ── Review round: the frozen group contract on the compact rows ──────────

    [Fact]
    public void SpreadsheetToolbarLayout_Tablet_RowsCarryTheFrozenGroupIds()
    {
        var rows = SpreadsheetToolbarLayout.Rows(TmLayoutMode.Tablet, showFileCommands: true);
        var ids = rows.SelectMany(r => r.Commands).Select(c => c.GroupId).Distinct().ToArray();

        ids.Should().Contain(["clipboard", "font", "alignment", "number", "cells", "insert", "data", "view", "file"],
            "the compact chrome uses the SAME frozen data-tm-group set the ribbon's groups carry");
        rows.SelectMany(r => r.Commands).Should().OnlyContain(c => !string.IsNullOrEmpty(c.GroupId));

        SpreadsheetToolbarLayout.Rows(TmLayoutMode.Mobile, showFileCommands: true)
            .SelectMany(r => r.Commands)
            .Should().OnlyContain(c => !string.IsNullOrEmpty(c.GroupId));
    }

    [Fact]
    public void SpreadsheetToolbar_Tablet_RendersCoreToolbarGroupsWithTheDataTmGroupAttribute()
    {
        var cut = Render<TmSpreadsheetToolbar>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Tablet));

        var groups = cut.FindAll(".tm-spreadsheet-compact-toolbar__row [data-tm-group]");
        groups.Should().NotBeEmpty("the compact rows group their commands like the ribbon");
        groups.Select(g => g.GetAttribute("data-tm-group")).Distinct()
            .Should().Contain(["clipboard", "font", "alignment", "number", "cells", "insert"]);
        groups.Should().OnlyContain(g => g.GetAttribute("role") == "group",
            "a labelled core TmToolbarGroup renders role=group and names its More section");
    }

    [Fact]
    public void SpreadsheetToolbar_Mobile_LeadsWithUndoRedo_AndHasOneAlignDropdown()
    {
        var cut = Render<TmSpreadsheetToolbar>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        // The compact header may be absent (no Title / slots), so undo & redo lead the formatting row.
        cut.FindAll("[data-command-id='undo']").Count.Should().Be(1);
        cut.FindAll("[data-command-id='redo']").Count.Should().Be(1);
        cut.FindAll("[data-command-id='align']").Count.Should().Be(1,
            "one Align control, like the reference — not three separate alignment buttons on the row");
        cut.FindAll(".tm-spreadsheet-compact-toolbar__row").Count.Should().Be(1);
    }

    [Fact]
    public void SpreadsheetToolbar_Compact_RendersHostCustomTools_InTheirOwnGroup()
    {
        var tools = new List<SpreadsheetCustomTool>
        {
            new() { Id = "archive", Title = "Archive", IconName = "download", Tab = "Home" },
        };

        var tablet = Render<TmSpreadsheetToolbar>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Tablet)
            .Add(c => c.CustomTools, tools));
        tablet.FindAll("[data-tm-group='custom']").Should().NotBeEmpty(
            "a host tool keeps its data-tm-group=custom entry on the compact rows");
        tablet.FindAll("[data-tool-id='archive']").Count.Should().Be(1);

        var mobile = Render<TmSpreadsheetToolbar>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.CustomTools, tools));
        mobile.FindAll("[data-tm-group='custom']").Should().NotBeEmpty(
            "on a phone the tool is reachable through More only (G2)");
    }

    // ── Review round: only ONE header at any width (ShowHeader + mobile) ─────

    [Fact]
    public void Spreadsheet_Mobile_WithShowHeader_RendersExactlyOneHeader_WithoutOpenDownload()
    {
        var cut = Render<TmSpreadsheet>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.ShowHeader, true)
            .Add(c => c.Title, "Projekt Aurora")
            .Add(c => c.OnOpen, EventCallback.Factory.Create<SpreadsheetOpenEventArgs>(this, _ => { }))
            .Add(c => c.OnDownload, EventCallback.Factory.Create<SpreadsheetDownloadEventArgs>(this, _ => { })));

        cut.FindAll("[data-testid='tm-spreadsheet-header']").Should().BeEmpty(
            "the document header is desktop/tablet only — a phone gets the compact header instead");
        cut.FindAll("[data-testid='tm-spreadsheet-mobile-header']").Count.Should().Be(1);
        cut.FindAll("[data-testid='tm-spreadsheet-header-open']").Should().BeEmpty();
        cut.FindAll("[data-testid='tm-spreadsheet-header-download']").Should().BeEmpty();
        cut.Find(".tm-spreadsheet__mobile-title").TextContent.Should().Be("Projekt Aurora");
    }

    [Fact]
    public void Spreadsheet_Mobile_WithoutHeaderContent_RendersNoEmptyHeaderRow()
    {
        var cut = Render<TmSpreadsheet>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        cut.FindAll("[data-testid='tm-spreadsheet-mobile-header']").Should().BeEmpty(
            "an empty 50px header row above a phone grid is worse than none; undo/redo live in the row");
        cut.FindAll("[data-command-id='undo']").Count.Should().Be(1);
    }

    [Fact]
    public void Spreadsheet_Desktop_WithShowHeader_RendersTheDocumentHeaderOnly()
    {
        var cut = Render<TmSpreadsheet>(p => p
            .Add(c => c.ShowHeader, true)
            .Add(c => c.Title, "Projekt Aurora"));

        cut.FindAll("[data-testid='tm-spreadsheet-header']").Count.Should().Be(1);
        cut.FindAll("[data-testid='tm-spreadsheet-mobile-header']").Should().BeEmpty();
    }

    // ── Review round 2: the resize dialogs are host-owned (outside the container) ──

    [Fact]
    public void Spreadsheet_ResizeDialog_IsRenderedOutsideTheLayoutContainer()
    {
        var cut = Render<TmSpreadsheet>(p => p.Add(c => c.InitialWorkbook, new SpreadsheetWorkbook()));
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        grid.Instance.RenderContextMenu.Should().BeFalse("the host owns the cell menu");

        cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(4, 4, 200, 300));
        cut.Render();
        cut.FindAll(".tm-spreadsheet-context-menu__item")
            .First(item => item.TextContent.Contains("Set column width", StringComparison.Ordinal)).Click();

        cut.FindAll(".tm-spreadsheet-resize-dialog").Count.Should().Be(1,
            "the host renders the column-width dialog");
        cut.FindAll(".tm-spreadsheet__layout .tm-modal-overlay").Should().BeEmpty(
            "a position:fixed dialog inside the container would be positioned against the card");
        // The input is the MODEL width (the sheet default), never the rendered one.
        cut.Find(".tm-spreadsheet-resize-dialog__input").GetAttribute("value").Should().Be("64");

        // Enter applies through the same resize path (model units), then the dialog closes.
        cut.Find(".tm-spreadsheet-resize-dialog__input")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Render();
        cut.FindAll(".tm-spreadsheet-resize-dialog").Should().BeEmpty("Enter applies and closes");
    }

    [Fact]
    public void Spreadsheet_ResizeDialog_AppliesTheTypedModelValue()
    {
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheet>(p => p.Add(c => c.InitialWorkbook, workbook));
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(4, 4, 200, 300));
        cut.Render();
        cut.FindAll(".tm-spreadsheet-context-menu__item")
            .First(item => item.TextContent.Contains("Set row height", StringComparison.Ordinal)).Click();

        var input = cut.Find(".tm-spreadsheet-resize-dialog__input");
        input.Input("33");
        cut.Find(".tm-spreadsheet-resize-dialog__btn--ok").Click();

        cut.Instance.Workbook.ActiveSheet!.Rows.TryGetValue(0, out var row).Should().BeTrue();
        row!.Height.Should().Be(33,
            "the dialog writes MODEL units — the density/zoom conversion happens on the canvas path only");
    }

    [Fact]
    public void SpreadsheetCanvasGrid_Standalone_RendersItsOwnResizeDialogs()
    {
        // A standalone grid (RenderContextMenu defaults true) keeps the in-grid dialogs: nothing else hosts them.
        var cut = Render<TmSpreadsheetCanvasGrid>(p => p.Add(c => c.Sheet, new SpreadsheetSheet()));

        cut.InvokeAsync(() => cut.Instance.OnCanvasContextMenu(60, 60, 10, 10));
        cut.Render();
        cut.FindAll(".tm-spreadsheet-context-menu__item")
            .First(item => item.TextContent.Contains("Set row height", StringComparison.Ordinal)).Click();

        cut.FindAll(".tm-spreadsheet-resize-dialog").Count.Should().Be(1);
    }

    // ── 10. The container, not the viewport, decides the mode ────────────────

    [Fact]
    public void Spreadsheet_Embedded_UsesContainerWidth_NotTheViewport()
    {
        // 1. The default is Auto, so an embed (a 390px Notion block inside a 1440px page) classifies
        //    itself instead of inheriting the page's width.
        Render<TmSpreadsheet>().Instance.LayoutMode.Should().Be(TmLayoutMode.Auto);

        // 2. The markup contract the styles follow: the root carries the resolved mode as BOTH a
        //    data attribute and a modifier class, and the core observer's own root repeats it — a host
        //    (or a spec) never has to measure anything.
        var mobile = Render<TmSpreadsheet>(p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile));
        mobile.Find(".tm-spreadsheet").GetAttribute("data-layout").Should().Be("mobile");
        mobile.Find(".tm-spreadsheet").ClassList.Should().Contain("tm-spreadsheet--layout-mobile");
        mobile.Find(".tm-spreadsheet__layout").GetAttribute("data-layout").Should().Be("mobile");

        // 3. The stylesheet registers the NAMED query container on the same root, and the package has no
        //    viewport LAYOUT query left: every responsive rule is a container query with a TmBreakpoints
        //    width in range syntax.
        var css = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Tempo.Blazor.Spreadsheet", "wwwroot", "css", "components", "_spreadsheet.css"));
        css.Should().Contain("container-type: inline-size");
        css.Should().Contain("container-name: tm-spreadsheet");
        foreach (var sheet in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "Tempo.Blazor.Spreadsheet"),
                     "*.css",
                     SearchOption.AllDirectories).Where(path => path.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}")))
        {
            File.ReadAllText(sheet).Should().NotContain("@media (max-width",
                $"{sheet} must not decide the layout from the viewport width");
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }
}
