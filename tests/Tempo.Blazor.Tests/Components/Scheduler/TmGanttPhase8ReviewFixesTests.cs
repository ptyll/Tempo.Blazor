using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// Review fixes of the phase 8 (mobile read-mostly) round: one mobile sheet at a time
/// (<see cref="TmGantt.CloseMobileSurfaces"/>), the layout flip closes every surface and the inline
/// edit, the mobile zoom keeps the Day | Week | Month segment working through the zoom override, the
/// culture-aware range line, the "More" sheet as a plain button list (menu roles dropped, the
/// task-panel entry disabled without a selection), the long-press menu as a real
/// <c>role="menu"</c> with Escape and a roving tab stop, the focus restore of the mobile sheets
/// (stable action-bar tile ids + <c>RestoreFocusTargetId</c>), the density group hidden inside the
/// sheet, the percent clamp re-sync and the separate invalid-end message.
/// A bUnit render cannot run container queries, so every layout is FORCED with <c>LayoutMode</c>.
/// </summary>
public class TmGanttPhase8ReviewFixesTests : LocalizationTestBase
{
    public TmGanttPhase8ReviewFixesTests()
    {
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2024, 6, 12, 10, 0, 0, TimeSpan.Zero)));
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc));
        JSInterop.Setup<bool>("tmGantt.attachTimeline", _ => true).SetResult(true);
        JSInterop.Setup<bool>("tmGantt.attachTree", _ => true).SetResult(true);
    }

    private static List<TmWorkItem> Tasks() =>
    [
        new() { Id = "1", Title = "Project", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30), IsExpanded = true, PercentComplete = 40 },
        new() { Id = "2", Title = "Design", ParentId = "1", Start = new DateTime(2024, 6, 3), End = new DateTime(2024, 6, 6), PercentComplete = 100 },
        new() { Id = "3", Title = "Development", ParentId = "1", Start = new DateTime(2024, 6, 11), End = new DateTime(2024, 6, 25), PercentComplete = 30 },
    ];

    private IRenderedComponent<TmGantt> RenderGantt(TmLayoutMode mode, Action<ComponentParameterCollectionBuilder<TmGantt>>? configure = null) =>
        Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks());
            p.Add(c => c.LayoutMode, mode);
            configure?.Invoke(p);
        });

    /// <summary>A touch tap in bUnit: the row's pointerup (names the pointer type) followed by its click.</summary>
    private static void TouchTap(AngleSharp.Dom.IElement row)
    {
        row.TriggerEvent("onpointerup", new PointerEventArgs { PointerType = "touch" });
        row.TriggerEvent("onclick", new MouseEventArgs());
    }

    private static TmSidePanel Sheet(IRenderedComponent<TmGantt> cut, string testId) =>
        cut.FindComponents<TmSidePanel>().Single(s => s.Instance.DataTestId == testId).Instance;

    /// <summary>Wraps a localizer with another culture (the strings stay English; only the culture is pinned).</summary>
    private sealed class CultureLocalizer(ITmLocalizer inner, CultureInfo culture) : ITmLocalizer
    {
        public CultureInfo Culture { get; } = culture;

        public bool TryGetString(string key, out string value) => inner.TryGetString(key, out value!);

        public string this[string key] => inner[key];

        public string this[string key, params object[] arguments] => inner[key, arguments];
    }

    // ── 1. One mobile sheet at a time ───────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_Opening_A_Surface_Closes_The_Others()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);

        // The More sheet replaces the add sheet.
        cut.Find("[data-action-id='gantt-mobile-add']").Click();
        cut.FindAll("[data-testid='gantt-add-sheet']").Should().ContainSingle();
        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        cut.FindAll("[data-testid='gantt-add-sheet']").Should().BeEmpty("the More sheet is the one surface");
        cut.FindAll("[data-testid='gantt-more-sheet']").Should().ContainSingle();

        // History replaces the More sheet.
        cut.Find("[data-testid='gantt-more-history']").Click();
        cut.FindAll("[data-testid='gantt-more-sheet']").Should().BeEmpty();
        cut.FindAll("[data-testid='gantt-history-sheet']").Should().ContainSingle();

        // Reports replace history (opened through the More sheet again).
        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        cut.Find("[data-testid='gantt-more-reports']").Click();
        cut.FindAll("[data-testid='gantt-history-sheet']").Should().BeEmpty();
        cut.FindAll("[data-testid='gantt-reports-sheet']").Should().ContainSingle();

        // The long-press menu closes when its entry opens the task sheet.
        cut.Find("[data-testid='gantt-reports-sheet']").QuerySelector(".tm-drawer__close")!.Click();
        cut.InvokeAsync(() => cut.Instance.OnLongPressContextMenu("3", 100, 100));
        cut.FindAll(".tm-gantt__mobile-menu").Should().ContainSingle();
        cut.Find("[data-testid='gantt-context-status']").Click();
        cut.FindAll(".tm-gantt__mobile-menu").Should().BeEmpty();
        cut.FindAll(".tm-drawer.tm-gantt__sheet").Should().ContainSingle("the task sheet is the one surface");

        // The filter chip closes the task sheet: the dialog is the one surface.
        cut.Find("[data-testid='gantt-filter-btn']").Click();
        cut.FindAll(".tm-modal-overlay.tm-gantt__filter-dialog").Should().ContainSingle();
        cut.FindAll(".tm-drawer.tm-gantt__sheet").Should().BeEmpty("the filter dialog replaced the task sheet");
    }

    // ── 2. The layout flip closes every surface ─────────────────────────────────────────────────

    [Fact]
    public void Layout_Flip_Closes_Surfaces_And_Inline_Edit_Both_Directions()
    {
        // The layout is NOT forced: the flip is the case tested (the observer reports, ApplyLayout reconciles).
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks()));
        var observer = cut.FindComponent<TmLayoutObserver>();

        cut.InvokeAsync(() => cut.Instance.ShowContextMenu(new MouseEventArgs { ClientX = 10, ClientY = 10 }, Tasks()[1]));
        cut.InvokeAsync(() => cut.Instance.OpenFilterPanel());
        cut.InvokeAsync(() => cut.Instance.ToggleViewSettings());
        cut.InvokeAsync(() => cut.Instance.StartCustomFieldInlineEdit("2", "cf1", "v"));
        cut.Instance.ContextMenuVisible.Should().BeTrue();
        cut.Instance._inlineEditCustomFieldId.Should().Be("cf1");

        cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("mobile"));

        cut.Instance.ContextMenuVisible.Should().BeFalse("the flip to mobile closed the context menu");
        cut.Instance._filterPanelOpen.Should().BeFalse();
        cut.Instance._viewSettingsOpen.Should().BeFalse();
        cut.Instance._inlineEditTaskId.Should().BeNull();
        cut.Instance._inlineEditColumn.Should().BeNull();
        cut.Instance._inlineEditCustomFieldId.Should().BeNull("the flip cancelled the inline edit");
        cut.Find(".tm-gantt").GetAttribute("data-density").Should().Be("mobile");

        // Back up: a flip to desktop closes the mobile surfaces too.
        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        cut.FindAll("[data-testid='gantt-more-sheet']").Should().ContainSingle();
        cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("desktop"));
        cut.FindAll("[data-testid='gantt-more-sheet']").Should().BeEmpty();
        cut.Find(".tm-gantt").GetAttribute("data-density").Should().BeNull();
    }

    // ── 3. The mobile zoom override keeps the segment working ───────────────────────────────────

    [Fact]
    public async Task Mobile_Segment_Sets_The_Zoom_Override()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);

        cut.Instance.GetEffectiveZoomPreset().Should().Be(GanttZoomPreset.Days, "the read-mostly default");

        await cut.InvokeAsync(() => cut.Instance.SetViewAsync(GanttView.Month));
        cut.Instance.GetEffectiveZoomPreset().Should().Be(GanttZoomPreset.Months, "the Month segment choice sticks on mobile");

        await cut.InvokeAsync(() => cut.Instance.SetViewAsync(GanttView.Week));
        cut.Instance.GetEffectiveZoomPreset().Should().Be(GanttZoomPreset.Weeks);

        await cut.InvokeAsync(() => cut.Instance.SetViewAsync(GanttView.Day));
        cut.Instance.GetEffectiveZoomPreset().Should().Be(GanttZoomPreset.Days);
    }

    [Fact]
    public void Desktop_Keeps_The_ZoomPreset_Parameter_And_A_Set_Override_Survives_The_Flip()
    {
        var desktop = RenderGantt(TmLayoutMode.Desktop);
        desktop.Instance.GetEffectiveZoomPreset().Should().Be(desktop.Instance.ZoomPreset, "no override is set yet");

        // A choice made before the flip is not reset by it.
        desktop.InvokeAsync(() => desktop.Instance.SetViewAsync(GanttView.Month));
        desktop.Instance.GetEffectiveZoomPreset().Should().Be(GanttZoomPreset.Months);
        var observer = desktop.FindComponent<TmLayoutObserver>();
        observer.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("mobile"));
        desktop.Instance.GetEffectiveZoomPreset().Should().Be(GanttZoomPreset.Months, "the explicit override wins on mobile too");
    }

    // ── 4. The range line follows the culture ───────────────────────────────────────────────────

    [Fact]
    public void Mobile_Cell_Range_Follows_The_Culture()
    {
        var csCulture = CultureInfo.GetCultureInfo("cs-CZ");
        Services.AddSingleton<ITmLocalizer>(new CultureLocalizer(BuildEnglishLocalizer(), csCulture));
        var cut = RenderGantt(TmLayoutMode.Mobile);

        // "d. MMM" is the cs MonthDayPattern — read from the culture, not the hard-coded "d. M.".
        cut.FindAll(".tm-gantt__mobile-cell-range").Select(r => r.TextContent.Trim()).Should().Equal([
            "1. června – 30. června", "3. června – 6. června", "11. června – 25. června",
        ]);
    }

    [Fact]
    public void Mobile_Cell_Range_Falls_Back_To_Short_Date_Without_A_MonthDayPattern()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.DateTimeFormat.MonthDayPattern = " ";
        Services.AddSingleton<ITmLocalizer>(new CultureLocalizer(BuildEnglishLocalizer(), culture));
        var cut = RenderGantt(TmLayoutMode.Mobile);

        cut.FindAll(".tm-gantt__mobile-cell-range").First().TextContent.Trim().Should().Be("6/1/2024 – 6/30/2024");
    }

    // ── 5. + 8. The More sheet is a plain button list ───────────────────────────────────────────

    [Fact]
    public void Mobile_More_Is_A_Plain_Button_List_And_Task_Panel_Needs_A_Selection()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);
        cut.Find("[data-action-id='gantt-mobile-more']").Click();

        var sheet = cut.Find("[data-testid='gantt-more-sheet']");
        sheet.QuerySelectorAll("[role='menu'], [role='menuitem']")
            .Should().BeEmpty("a plain list of buttons — the menu roles are gone");

        var taskPanel = sheet.QuerySelector("[data-testid='gantt-more-task-panel']")!;
        taskPanel.HasAttribute("disabled").Should().BeTrue("no task is selected");
        taskPanel.GetAttribute("aria-pressed").Should().Be("false", "the toggle state stays announced");
        sheet.QuerySelector("[data-testid='gantt-more-settings']")!.HasAttribute("aria-expanded")
            .Should().BeFalse("the constant aria-expanded is gone");

        // With a selection the entry is enabled and the pressed state follows the panel.
        // (The programmatic paths raise no event, so the test renders once to flush them.)
        cut.InvokeAsync(() => cut.Instance.SelectTaskAsync(Tasks()[1]));
        cut.Render();
        var sheet2 = cut.Find("[data-testid='gantt-more-sheet']");
        sheet2.QuerySelector("[data-testid='gantt-more-task-panel']")!.HasAttribute("disabled").Should().BeFalse();
        sheet2.QuerySelector("[data-testid='gantt-more-task-panel']")!.GetAttribute("aria-pressed").Should().Be("false");

        // The entry opens the task panel as THE surface: the More sheet yields (and with it the
        // pressed state returns to "false" the next time it is opened over a closed panel).
        cut.InvokeAsync(() => cut.Instance.MobileMoreTaskPanel());
        cut.Render();
        cut.FindAll("[data-testid='gantt-more-sheet']").Should().BeEmpty("the entry closed the More sheet");
        cut.FindAll(".tm-drawer.tm-gantt__sheet").Should().ContainSingle("the task sheet opened");
    }

    // ── 6. The long-press menu is a menu ────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_LongPress_Menu_Is_A_Menu_With_A_Roving_Tab_Stop_And_Escape()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);
        cut.InvokeAsync(() => cut.Instance.OnLongPressContextMenu("3", 100, 100));

        var menu = cut.Find(".tm-gantt__mobile-menu");
        menu.GetAttribute("role").Should().Be("menu");
        menu.QuerySelectorAll("[role='menuitem']").Select(i => i.GetAttribute("data-testid")).Should().Equal(
            ["gantt-context-status", "gantt-context-percent", "gantt-context-add"]);
        menu.QuerySelectorAll("[role='menuitem']").Select(i => i.GetAttribute("tabindex")).Should().Equal(
            ["0", "-1", "-1"], "the first item holds the roving tab stop");

        var part = cut.FindComponent<TmGanttMobileOverlays>();
        menu.KeyDown("ArrowDown");
        part.Instance.MobileMenuIndex.Should().Be(1);
        menu = cut.Find(".tm-gantt__mobile-menu");
        menu.QuerySelectorAll("[role='menuitem']").Select(i => i.GetAttribute("tabindex")).Should().Equal(
            ["-1", "0", "-1"]);

        menu.KeyDown("ArrowUp");
        menu.KeyDown("ArrowUp");
        part.Instance.MobileMenuIndex.Should().Be(2, "the roving stop wraps around");
        cut.FindAll(".tm-gantt__mobile-menu").Should().ContainSingle("the arrows navigate, they never close");

        cut.Find(".tm-gantt__mobile-menu").KeyDown("Escape");
        cut.FindAll(".tm-gantt__mobile-menu").Should().BeEmpty("Escape closes the menu");
        cut.Instance.FocusRequest!.Value.Target.Should().Be(GanttFocusTarget.Row);
        cut.Instance.FocusRequest!.Value.TaskId.Should().Be("3", "the focus returns to the row of the task");
    }

    // ── 7. Focus restore of the mobile sheets ───────────────────────────────────────────────────

    [Fact]
    public void Mobile_Action_Bar_Tiles_Carry_Stable_Ids_And_Sheets_Restore_Focus_To_Them()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile, p => p.Add(c => c.TestIdPrefix, "p1"));

        cut.Find("[data-action-id='gantt-mobile-more']").GetAttribute("id").Should().Be("p1-gantt-mobile-more");
        cut.Find("[data-action-id='gantt-mobile-add']").GetAttribute("id").Should().Be("p1-gantt-mobile-add");

        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        Sheet(cut, "p1-gantt-more-sheet").RestoreFocusTargetId.Should().Be("p1-gantt-mobile-more");

        cut.Find("[data-action-id='gantt-mobile-add']").Click();
        Sheet(cut, "p1-gantt-add-sheet").RestoreFocusTargetId.Should().Be("p1-gantt-mobile-add");

        cut.Find("[data-testid='p1-gantt-add-cancel']").Click();
        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        cut.Find("[data-testid='p1-gantt-more-history']").Click();
        Sheet(cut, "p1-gantt-history-sheet").RestoreFocusTargetId.Should().Be("p1-gantt-mobile-more");

        cut.Find("[data-testid='p1-gantt-history-sheet']").QuerySelector(".tm-drawer__close")!.Click();
        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        cut.Find("[data-testid='p1-gantt-more-reports']").Click();
        Sheet(cut, "p1-gantt-reports-sheet").RestoreFocusTargetId.Should().Be("p1-gantt-mobile-more");

        // Without a prefix the ids are still stable (and unique on a single-Gantt page).
        var plain = RenderGantt(TmLayoutMode.Mobile);
        plain.Find("[data-action-id='gantt-mobile-more']").GetAttribute("id").Should().Be("gantt-mobile-more");
        plain.Find("[data-action-id='gantt-mobile-more']").Click();
        Sheet(plain, "gantt-more-sheet").RestoreFocusTargetId.Should().Be("gantt-mobile-more");
    }

    [Fact]
    public void Task_Sheet_Close_Returns_Focus_To_The_Row_And_Tablet_Reports_Sheet_Stays_Null()
    {
        var cut = RenderGantt(TmLayoutMode.Mobile);
        TouchTap(cut.FindAll(".tm-gantt__tree-row")[1]);
        cut.FindAll(".tm-drawer.tm-gantt__sheet").Should().ContainSingle();

        cut.Find("[data-testid='task-mobile-cancel']").Click();
        cut.FindAll(".tm-drawer.tm-gantt__sheet").Should().BeEmpty();
        cut.Instance.FocusRequest!.Value.Target.Should().Be(GanttFocusTarget.Row);
        cut.Instance.FocusRequest!.Value.TaskId.Should().Be("2");

        // A non-mobile SHEET (TaskPanelMode Overlay on a desktop container) is not a mobile surface:
        // it keeps the default restore.
        var overlay = RenderGantt(TmLayoutMode.Desktop, p => p
            .Add(c => c.TestIdPrefix, "p1")
            .Add(c => c.TaskPanelMode, GanttTaskPanelMode.Overlay));
        overlay.InvokeAsync(() => overlay.Instance.ToggleSidebarPanel(GanttSidebarPanel.Reports));
        overlay.Render();
        Sheet(overlay, "p1-gantt-reports-sheet").RestoreFocusTargetId.Should().BeNull();
    }

    // ── 9. The density group is hidden inside the sheet ─────────────────────────────────────────

    [Fact]
    public void View_Settings_Hide_The_Density_Group_In_The_Sheet_And_On_Mobile()
    {
        // The mobile More sheet renders the list without the density group (the mobile density is pinned).
        var cut = RenderGantt(TmLayoutMode.Mobile);
        cut.Find("[data-action-id='gantt-mobile-more']").Click();
        cut.Find("[data-testid='gantt-more-settings']").Click();

        var sheet = cut.Find("[data-testid='gantt-more-sheet']");
        sheet.QuerySelector("[data-testid='vset-show-today']").Should().NotBeNull("the toggles stay");
        sheet.QuerySelector("input[value='Compact']").Should().BeNull("the density group is a desktop / tablet setting");
        sheet.TextContent.Should().NotContain("Row density");

        // The desktop popover keeps it (opened through the toolbar button, an event path).
        var desktop = RenderGantt(TmLayoutMode.Desktop);
        desktop.Find("[data-testid='gantt-view-settings-btn']").Click();
        desktop.Find("input[value='Compact']").Should().NotBeNull();
    }

    // ── 10. The percent clamp re-syncs the input ────────────────────────────────────────────────

    [Fact]
    public void Mobile_Task_Panel_Percent_Clamp_ReSyncs_The_Input()
    {
        var updated = new List<TmWorkItem>();
        var cut = RenderGantt(TmLayoutMode.Mobile, p => p
            .Add(c => c.OnTaskUpdated, EventCallback.Factory.Create<TmWorkItem>(this, t => updated.Add(t))));

        // "Design" is already at 100: typing 150 clamps to the SAME value the field showed.
        TouchTap(cut.FindAll(".tm-gantt__tree-row")[1]);

        var input = cut.Find("input[data-testid='task-mobile-percent']");
        input.Change("150");

        var resynced = cut.Find("input[data-testid='task-mobile-percent']");
        resynced.GetAttribute("value").Should().Be("100", "the field shows the clamped value again");
        resynced.TextContent.Should().NotContain("150");

        cut.Find("input[data-testid='task-mobile-percent']").Change("-5");
        cut.Find("input[data-testid='task-mobile-percent']").GetAttribute("value").Should().Be("0");

        cut.Find("[data-testid='task-mobile-save']").Click();
        updated.Should().ContainSingle(t => t.Id == "2" && t.PercentComplete == 0);
    }

    // ── 11. The invalid end has its own message ─────────────────────────────────────────────────

    [Fact]
    public void Mobile_Add_Sheet_Reports_An_Invalid_End_Separately()
    {
        var added = new List<TmWorkItem>();
        var cut = RenderGantt(TmLayoutMode.Mobile, p =>
            p.Add(c => c.OnTaskAdded, EventCallback.Factory.Create<TmWorkItem>(this, t => added.Add(t))));

        cut.Find("[data-action-id='gantt-mobile-add']").Click();
        cut.Find("[data-testid='gantt-add-name']").Input("Review");
        cut.Find("[data-testid='gantt-add-end']").Change("30/6");
        cut.Find("[data-testid='gantt-add-submit']").Click();

        added.Should().BeEmpty();
        cut.Find("[data-testid='gantt-add-error']").TextContent.Should().Be("Invalid end date.");
        cut.FindAll("[data-testid='gantt-add-sheet']").Should().ContainSingle("the sheet stays open");

        cut.Find("[data-testid='gantt-add-start']").Change("not-a-date");
        cut.Find("[data-testid='gantt-add-submit']").Click();
        cut.Find("[data-testid='gantt-add-error']").TextContent.Should().Be("Invalid start date.");
    }
}
