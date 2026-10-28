using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 6: the filter. Typed value editors (an enum select for Status / Priority, a date input
/// for Start / End / Deadline), operators limited by the field type, a transactional footer
/// (Clear all | Cancel | Apply), the opt-in dialog presentation (<c>FilterPanelMode</c>) and the chips of the
/// active filters under the toolbar. Written BEFORE the implementation.
/// </summary>
public class TmGanttPhase6FilterTests : LocalizationTestBase
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    private static List<TmWorkItem> Tasks() =>
    [
        new() { Id = "1", Title = "Design", Start = new DateTime(2024, 6, 3), End = new DateTime(2024, 6, 7), Status = TmWorkItemStatus.Done, Priority = TmWorkItemPriority.High },
        new() { Id = "2", Title = "Build", Start = new DateTime(2024, 6, 10), End = new DateTime(2024, 6, 21), Status = TmWorkItemStatus.InProgress, Priority = TmWorkItemPriority.Medium, DueDate = new DateTime(2024, 6, 25) },
        new() { Id = "3", Title = "Ship", Start = new DateTime(2024, 6, 24), End = new DateTime(2024, 6, 28), Status = TmWorkItemStatus.Open, Priority = TmWorkItemPriority.Low },
    ];

    private IRenderedComponent<TmGanttFilterPanel> RenderPanel(
        IEnumerable<GanttFilter>? filters = null,
        Action<IReadOnlyList<GanttFilter>>? onChanged = null,
        Action? onClose = null,
        bool asDialog = false) =>
        Render<TmGanttFilterPanel>(p =>
        {
            p.Add(x => x.Filters, (filters ?? []).ToList());
            if (onChanged is not null) p.Add(x => x.OnFiltersChanged, EventCallback.Factory.Create<IReadOnlyList<GanttFilter>>(this, onChanged));
            if (onClose is not null) p.Add(x => x.OnClose, EventCallback.Factory.Create(this, onClose));
            if (asDialog) p.Add(x => x.AsDialog, true);
        });

    private static IReadOnlyList<string?> OptionValues(IElement select) =>
        select.QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToList();

    private static IReadOnlyList<string> OptionTexts(IElement select) =>
        select.QuerySelectorAll("option").Select(o => o.TextContent.Trim()).ToList();

    // ═══════════════════════════════════════════════════════════════
    // Typed value editors
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGanttFilterPanel_Status_And_Priority_Use_Select()
    {
        IReadOnlyList<GanttFilter>? applied = null;
        var cut = RenderPanel([new GanttFilter("Status", GanttFilterOperator.Equals, "Done")], f => applied = f);

        cut.FindAll("input[type=text].tm-gantt__filter-value").Should().BeEmpty("an enum is picked, never typed");
        var value = cut.Find("select.tm-gantt__filter-value");
        OptionValues(value).Should().Equal("Open", "InProgress", "Done", "Closed");
        OptionTexts(value).Should().Equal("Open", "In Progress", "Done", "Closed");
        value.QuerySelector("option[selected]")!.GetAttribute("value").Should().Be("Done");

        cut.Find("select.tm-gantt__filter-field").Change("Priority");

        value = cut.Find("select.tm-gantt__filter-value");
        OptionValues(value).Should().Equal("Highest", "High", "Medium", "Low", "Lowest");
        OptionTexts(value).Should().Equal("Highest", "High", "Medium", "Low", "Lowest");
        value.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        value.Change("Low");
        cut.Find("[data-testid='filter-apply']").Click();

        applied.Should().ContainSingle().Which.Should().Be(new GanttFilter("Priority", GanttFilterOperator.Equals, "Low"));
    }

    [Fact]
    public void TmGanttFilterPanel_A_New_Enum_Row_Starts_On_The_First_Value_So_Apply_Never_Sends_Nothing()
    {
        IReadOnlyList<GanttFilter>? applied = null;
        var cut = RenderPanel(onChanged: f => applied = f);
        cut.Find("[data-testid='filter-add']").Click();
        cut.Find("select.tm-gantt__filter-field").Change("Status");

        cut.Find("[data-testid='filter-apply']").Click();

        applied.Should().ContainSingle().Which.Should().Be(new GanttFilter("Status", GanttFilterOperator.Equals, "Open"));
    }

    [Fact]
    public void TmGanttFilterPanel_Date_Fields_Use_Date_Input_With_Before_After()
    {
        IReadOnlyList<GanttFilter>? applied = null;
        var cut = RenderPanel([new GanttFilter("Title", GanttFilterOperator.Contains, "x")], f => applied = f);

        cut.Find("select.tm-gantt__filter-field").Change("Start");

        cut.FindAll("input[type=text].tm-gantt__filter-value").Should().BeEmpty();
        var date = cut.Find("input[type=date].tm-gantt__filter-value");
        date.ClassList.Should().Contain("tm-input", "the native date input is styled as a Tm input");
        date.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        OptionValues(cut.Find("select.tm-gantt__filter-op")).Should().Equal("Equals", "Before", "After");
        OptionTexts(cut.Find("select.tm-gantt__filter-op")).Should().Equal("equals", "is before", "is after");

        cut.Find("select.tm-gantt__filter-op").Change("Before");
        cut.Find("input[type=date].tm-gantt__filter-value").Change("2024-06-12");
        cut.Find("[data-testid='filter-apply']").Click();

        applied.Should().ContainSingle().Which.Should().Be(new GanttFilter("Start", GanttFilterOperator.Before, "2024-06-12"));
    }

    [Theory]
    [InlineData("End")]
    [InlineData("Deadline")]
    public void TmGanttFilterPanel_Every_Date_Field_Gets_A_Date_Input(string field)
    {
        var cut = RenderPanel([new GanttFilter(field, GanttFilterOperator.After, "2024-06-01")]);

        cut.Find("input[type=date].tm-gantt__filter-value").GetAttribute("value").Should().Be("2024-06-01");
        cut.Find("select.tm-gantt__filter-field").GetAttribute("value").Should().Be(field);
    }

    [Fact]
    public void TmGanttFilterPanel_Operators_Limited_By_Field_Type()
    {
        var cut = RenderPanel([new GanttFilter("Title", GanttFilterOperator.Contains, "x")]);
        IReadOnlyList<string?> Ops() => OptionValues(cut.Find("select.tm-gantt__filter-op"));

        Ops().Should().Equal("Contains", "Equals", "NotEquals", "IsEmpty", "IsNotEmpty");

        cut.Find("select.tm-gantt__filter-field").Change("Status");
        Ops().Should().Equal("Equals", "NotEquals");

        cut.Find("select.tm-gantt__filter-field").Change("Priority");
        Ops().Should().Equal("Equals", "NotEquals");

        cut.Find("select.tm-gantt__filter-field").Change("End");
        Ops().Should().Equal("Equals", "Before", "After");

        cut.Find("select.tm-gantt__filter-field").Change("Deadline");
        Ops().Should().Equal("Equals", "Before", "After", "IsEmpty", "IsNotEmpty");
    }

    [Fact]
    public void TmGanttFilterPanel_Changing_The_Field_Moves_The_Operator_And_Value_To_What_The_Field_Allows()
    {
        IReadOnlyList<GanttFilter>? applied = null;
        var cut = RenderPanel([new GanttFilter("Title", GanttFilterOperator.Contains, "design")], f => applied = f);

        cut.Find("select.tm-gantt__filter-field").Change("Start");

        cut.Find("select.tm-gantt__filter-op").GetAttribute("value").Should().Be("Equals", "Contains is not a date operator");
        cut.Find("input[type=date].tm-gantt__filter-value").GetAttribute("value").Should().BeNullOrEmpty("the title text is not a date");

        cut.Find("[data-testid='filter-apply']").Click();
        applied.Should().BeEmpty("a date rule without a date is dropped, not applied with a null value");
    }

    [Fact]
    public void TmGanttFilterPanel_IsEmpty_Operators_Hide_The_Value_Editor()
    {
        var cut = RenderPanel([new GanttFilter("Deadline", GanttFilterOperator.IsEmpty, null)]);

        cut.FindAll(".tm-gantt__filter-value").Should().BeEmpty();

        cut.Find("select.tm-gantt__filter-op").Change("Before");
        cut.FindAll("input[type=date].tm-gantt__filter-value").Should().HaveCount(1);
    }

    [Fact]
    public void TmGanttFilterPanel_Keeps_A_Loaded_Operator_The_Field_Type_Would_Not_Offer()
    {
        // A host-supplied Status/Contains filter must not be silently rewritten to Equals just by opening the panel.
        var cut = RenderPanel([new GanttFilter("Status", GanttFilterOperator.Contains, "Prog")]);

        cut.Find("select.tm-gantt__filter-op").GetAttribute("value").Should().Be("Contains");
        OptionValues(cut.Find("select.tm-gantt__filter-op")).Should().Contain("Contains");
    }

    [Fact]
    public void TmGanttFilterPanel_Row_Labels_Are_Localized_And_Fields_Are_Listed()
    {
        var cut = RenderPanel([new GanttFilter("Title", GanttFilterOperator.Contains, "x")]);

        OptionValues(cut.Find("select.tm-gantt__filter-field")).Should().Equal("Title", "Status", "Priority", "Start", "End", "Deadline");
        OptionTexts(cut.Find("select.tm-gantt__filter-field")).Should().Equal("Title", "Status", "Priority", "Start", "End", "Deadline");
    }

    // ═══════════════════════════════════════════════════════════════
    // Transactional footer
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGanttFilterPanel_Cancel_Discards_Changes()
    {
        var applied = 0;
        var closed = 0;
        var cut = RenderPanel([new GanttFilter("Title", GanttFilterOperator.Contains, "x")], _ => applied++, () => closed++);

        cut.Find("[data-testid='filter-add']").Click();
        cut.FindAll(".tm-gantt__filter-row").Should().HaveCount(2);

        cut.Find("[data-testid='filter-cancel']").Click();

        applied.Should().Be(0, "Cancel never applies");
        closed.Should().Be(1);
        cut.FindAll(".tm-gantt__filter-row").Should().HaveCount(1, "the unapplied row is gone: the edit state is back at the applied filters");
    }

    [Fact]
    public void TmGanttFilterPanel_Footer_Order_Is_ClearAll_Cancel_Apply()
    {
        var cut = RenderPanel([new GanttFilter("Title", GanttFilterOperator.Contains, "x")]);

        var footer = cut.Find(".tm-gantt__filter-panel-actions");
        footer.QuerySelectorAll("button").Select(b => b.GetAttribute("data-testid"))
            .Should().Equal("filter-clear", "filter-cancel", "filter-apply");
        cut.Find("[data-testid='filter-apply']").ClassList.Should().Contain("tm-btn-primary");
    }

    [Fact]
    public void TmGanttFilterPanel_ClearAll_Link()
    {
        var applied = 0;
        var cut = RenderPanel(onChanged: _ => applied++);
        cut.FindAll("[data-testid='filter-clear']").Should().BeEmpty("nothing to clear");

        cut.Find("[data-testid='filter-add']").Click();
        var clear = cut.Find("[data-testid='filter-clear']");
        clear.ClassList.Should().Contain("tm-btn-link", "it is a link, not a button-shaped action");
        clear.TextContent.Trim().Should().Be("Clear all filters");

        clear.Click();

        cut.FindAll(".tm-gantt__filter-row").Should().BeEmpty();
        applied.Should().Be(0, "the footer is transactional: nothing is applied until Apply");

        cut.Find("[data-testid='filter-apply']").Click();
        applied.Should().Be(1);
    }

    [Fact]
    public void TmGanttFilterPanel_Apply_Raises_The_Full_Set_And_Keeps_An_Inline_Panel_Open()
    {
        IReadOnlyList<GanttFilter>? applied = null;
        var closed = 0;
        var cut = RenderPanel([new GanttFilter("Title", GanttFilterOperator.Contains, "x")], f => applied = f, () => closed++);
        cut.Find("[data-testid='filter-add']").Click();
        cut.FindAll("input.tm-gantt__filter-value")[1].Change("y");

        cut.Find("[data-testid='filter-apply']").Click();

        applied.Should().HaveCount(2);
        closed.Should().Be(0, "the inline panel stays where it was (today's behaviour)");
    }

    [Fact]
    public void TmGanttFilterPanel_Apply_Closes_The_Dialog_Presentation()
    {
        var closed = 0;
        var cut = RenderPanel([new GanttFilter("Title", GanttFilterOperator.Contains, "x")], onClose: () => closed++, asDialog: true);

        cut.Find("[data-testid='filter-apply']").Click();

        closed.Should().Be(1);
    }

    [Fact]
    public void TmGanttFilterPanel_Edits_Survive_A_Parent_Re_Render_With_The_Same_Filters()
    {
        var cut = RenderPanel([new GanttFilter("Title", GanttFilterOperator.Contains, "x")]);
        cut.Find("[data-testid='filter-add']").Click();

        // TmGantt hands over a fresh list instance on every render; content is unchanged.
        cut.Render(p => p.Add(x => x.Filters, [new GanttFilter("Title", GanttFilterOperator.Contains, "x")]));

        cut.FindAll(".tm-gantt__filter-row").Should().HaveCount(2, "an in-progress edit is not thrown away by an unrelated parent render");
    }

    [Fact]
    public void TmGanttFilterPanel_Dialog_Presentation_Is_A_TmModal_Without_Its_Own_Header()
    {
        var cut = RenderPanel([new GanttFilter("Title", GanttFilterOperator.Contains, "x")], asDialog: true);

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.ClassList.Should().Contain("tm-gantt__filter-dialog");
        cut.Find(".tm-focus-scope").GetAttribute("role").Should().Be("dialog");
        cut.Find(".tm-modal-title").TextContent.Trim().Should().Be("Filter");
        cut.FindAll(".tm-gantt__filter-panel-header").Should().BeEmpty("TmModal owns the header");
        cut.FindAll(".tm-gantt__filter-panel-close").Should().BeEmpty();
        cut.FindAll(".tm-modal-footer .tm-gantt__filter-panel-actions, .tm-modal-footer [data-testid='filter-apply']").Should().NotBeEmpty("the footer rides the TmModal footer");
    }

    [Fact]
    public void FilterRow_Css_Sizes_The_Controls_Explicitly_Because_TmInput_Is_Full_Width()
    {
        // UX review (1440 screenshot): .tm-input is width:100%, so field / operator / value stacked as three full-width rows.
        const string file = "TmGanttFilterPanel.razor.css";
        GanttCss.Property(file, ".tm-gantt__filter-row .tm-gantt__filter-field", "flex").Should().Be("0 0 9rem");
        GanttCss.Property(file, ".tm-gantt__filter-row .tm-gantt__filter-field", "width").Should().Be("auto");
        GanttCss.Property(file, ".tm-gantt__filter-row .tm-gantt__filter-value", "flex").Should().Be("1 1 10rem");
    }

    // ═══════════════════════════════════════════════════════════════
    // TmGantt: FilterPanelMode and the chips
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGanttFilterPanel_Apply_Drops_Rules_That_Need_A_Value_But_Have_None()
    {
        IReadOnlyList<GanttFilter>? applied = null;
        var cut = RenderPanel(onChanged: f => applied = f);
        cut.Find("[data-testid='filter-add']").Click();   // Title contains <empty>
        cut.Find("[data-testid='filter-add']").Click();
        cut.FindAll("select.tm-gantt__filter-field")[1].Change("Deadline");
        cut.FindAll("select.tm-gantt__filter-op")[1].Change("IsEmpty");   // needs no value: kept
        cut.Find("[data-testid='filter-add']").Click();
        cut.FindAll("input.tm-gantt__filter-value")[1].Change("   ");      // whitespace only: dropped

        cut.Find("[data-testid='filter-apply']").Click();

        applied.Should().ContainSingle().Which.Should().Be(new GanttFilter("Deadline", GanttFilterOperator.IsEmpty, null));
    }

    [Fact]
    public void TmGantt_FilterPanelMode_Dialog_Keeps_The_TmModal_Mounted_And_Toggles_Show()
    {
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.FilterPanelMode, GanttFilterPanelMode.Dialog));

        cut.FindComponents<TmGanttFilterPanel>().Should().ContainSingle("the panel is mounted while closed, so the modal can return focus to the trigger");
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty();

        cut.Find("[data-testid='gantt-filter-btn']").Click();
        cut.FindAll(".tm-modal-overlay").Should().ContainSingle();
        cut.FindAll("[data-testid='gantt-filter-panel']").Should().ContainSingle();

        cut.Find("[data-testid='filter-cancel']").Click();
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty();
        cut.FindComponents<TmGanttFilterPanel>().Should().ContainSingle();
    }

    [Fact]
    public void TmGantt_FilterPanelMode_Default_Inline_Unchanged()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));
        cut.Instance.FilterPanelMode.Should().Be(GanttFilterPanelMode.Inline);

        cut.Find("[data-testid='gantt-filter-btn']").Click();

        var wrapper = cut.Find(".tm-gantt__filter-panel[data-testid='gantt-filter-panel']");
        wrapper.QuerySelector(".tm-gantt__filter-panel-header").Should().NotBeNull("the inline panel keeps its own header");
        wrapper.QuerySelector(".tm-gantt__filter-panel-close").Should().NotBeNull();
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_FilterPanelMode_Auto_Is_Inline_Until_The_Container_Layout_Lands()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()).Add(x => x.FilterPanelMode, GanttFilterPanelMode.Auto));

        cut.Find("[data-testid='gantt-filter-btn']").Click();

        cut.FindAll(".tm-gantt__filter-panel").Should().ContainSingle();
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_FilterPanelMode_Dialog_Renders_TmModal()
    {
        IReadOnlyList<GanttFilter>? changed = null;
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.FilterPanelMode, GanttFilterPanelMode.Dialog)
            .Add(x => x.OnFiltersChanged, EventCallback.Factory.Create<IReadOnlyList<GanttFilter>>(this, f => changed = f)));
        cut.FindAll("[data-testid^='tree-row-']").Should().HaveCount(3);

        cut.Find("[data-testid='gantt-filter-btn']").Click();

        cut.FindAll(".tm-gantt__filter-panel").Should().BeEmpty("no inline wrapper in the dialog presentation");
        var overlay = cut.Find(".tm-modal-overlay.tm-gantt__filter-dialog");
        overlay.GetAttribute("data-testid").Should().BeNull("the test id rides the inner panel, not the overlay root");
        cut.Find(".tm-gantt__filter-panel-inner--dialog").GetAttribute("data-testid").Should().Be("gantt-filter-panel");
        cut.Find("button[data-testid='gantt-filter-btn']").GetAttribute("aria-expanded").Should().Be("true");

        cut.Find("[data-testid='filter-add']").Click();
        cut.Find("select.tm-gantt__filter-field").Change("Status");
        cut.Find("select.tm-gantt__filter-value").Change("Done");
        cut.Find("[data-testid='filter-apply']").Click();

        cut.FindAll(".tm-modal-overlay").Should().BeEmpty("Apply closes the dialog");
        changed.Should().ContainSingle().Which.Should().Be(new GanttFilter("Status", GanttFilterOperator.Equals, "Done"));
        cut.FindAll("[data-testid^='tree-row-']").Should().HaveCount(1);
        cut.Find("button[data-testid='gantt-filter-btn']").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void TmGantt_FilterPanelMode_Dialog_Cancel_And_Close_Leave_The_Filters_Alone()
    {
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.FilterPanelMode, GanttFilterPanelMode.Dialog));
        cut.Find("[data-testid='gantt-filter-btn']").Click();
        cut.Find("[data-testid='filter-add']").Click();

        cut.Find("[data-testid='filter-cancel']").Click();

        cut.FindAll(".tm-modal-overlay").Should().BeEmpty();
        cut.FindAll("[data-testid^='tree-row-']").Should().HaveCount(3);
        cut.FindAll(".tm-gantt__filter-chips").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_Active_Filter_Chips_Below_Toolbar()
    {
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.Filters,
            [
                new GanttFilter("Status", GanttFilterOperator.Equals, "InProgress"),
                new GanttFilter("Start", GanttFilterOperator.After, "2024-06-05"),
                new GanttFilter("Title", GanttFilterOperator.Contains, "ui"),
            ]));

        var strip = cut.Find(".tm-gantt__filter-chips");
        strip.PreviousElementSibling!.ClassList.Should().Contain("tm-gantt__toolbar", "the chips sit directly below the toolbar");
        strip.GetAttribute("role").Should().Be("group");
        strip.GetAttribute("aria-label").Should().Be("Active filters");

        var chips = strip.QuerySelectorAll(".tm-filter-chip").ToList();
        chips.Should().HaveCount(3);
        chips[0].QuerySelector(".tm-filter-chip-field")!.TextContent.Trim().Should().Be("Status:");
        chips[0].QuerySelector(".tm-filter-chip-value")!.TextContent.Trim().Should().Be("In Progress", "an Equals filter reads as Field: Value");
        chips[1].QuerySelector(".tm-filter-chip-field")!.TextContent.Trim().Should().Be("Start:");
        chips[1].QuerySelector(".tm-filter-chip-value")!.TextContent.Trim().Should().Be("is after " + new DateTime(2024, 6, 5).ToString("d", En));
        chips[2].QuerySelector(".tm-filter-chip-value")!.TextContent.Trim().Should().Be("contains ui");
    }

    [Fact]
    public void TmGantt_No_Chip_Strip_Without_Filters()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        cut.FindAll(".tm-gantt__filter-chips").Should().BeEmpty("the default markup is unchanged");
    }

    [Fact]
    public void TmGantt_Chip_Remove_Drops_That_Filter_Only()
    {
        IReadOnlyList<GanttFilter>? changed = null;
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.Filters, [new GanttFilter("Status", GanttFilterOperator.NotEquals, "Closed"), new GanttFilter("Title", GanttFilterOperator.Contains, "Bu")])
            .Add(x => x.OnFiltersChanged, EventCallback.Factory.Create<IReadOnlyList<GanttFilter>>(this, f => changed = f)));
        cut.FindAll("[data-testid^='tree-row-']").Should().HaveCount(1);

        cut.FindAll(".tm-gantt__filter-chips .tm-filter-chip-remove")[1].Click();

        changed.Should().ContainSingle().Which.Should().Be(new GanttFilter("Status", GanttFilterOperator.NotEquals, "Closed"));
        cut.FindAll(".tm-gantt__filter-chips .tm-filter-chip").Should().HaveCount(1);
        cut.FindAll("[data-testid^='tree-row-']").Should().HaveCount(3);
    }

    [Fact]
    public void TmGantt_Chip_Click_Opens_The_Filter_Panel()
    {
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.Filters, [new GanttFilter("Title", GanttFilterOperator.Contains, "Bu")]));
        cut.FindAll("[data-testid='gantt-filter-panel']").Should().BeEmpty();

        cut.Find(".tm-gantt__filter-chips .tm-filter-chip").Click();

        cut.Find("[data-testid='gantt-filter-panel']").Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // GanttHelper: the new filterable fields
    // ═══════════════════════════════════════════════════════════════

    private static List<TmWorkItem> Filtered(params GanttFilter[] filters) =>
        GanttHelper.ApplyFilters(GanttHelper.BuildTree(Tasks()), filters).Select(n => n.Task).ToList();

    [Fact]
    public void GanttHelper_Start_Before_And_After_Compare_Dates()
    {
        Filtered(new GanttFilter("Start", GanttFilterOperator.Before, "2024-06-10")).Select(t => t.Id).Should().Equal("1");
        Filtered(new GanttFilter("Start", GanttFilterOperator.After, "2024-06-10")).Select(t => t.Id).Should().Equal("3");
        Filtered(new GanttFilter("Start", GanttFilterOperator.Equals, "2024-06-10")).Select(t => t.Id).Should().Equal("2");
        Filtered(new GanttFilter("End", GanttFilterOperator.After, "2024-06-20")).Select(t => t.Id).Should().Equal("2", "3");
    }

    [Fact]
    public void GanttHelper_Deadline_Handles_Missing_Dates()
    {
        Filtered(new GanttFilter("Deadline", GanttFilterOperator.IsEmpty, null)).Select(t => t.Id).Should().Equal("1", "3");
        Filtered(new GanttFilter("Deadline", GanttFilterOperator.IsNotEmpty, null)).Select(t => t.Id).Should().Equal("2");
        Filtered(new GanttFilter("Deadline", GanttFilterOperator.Before, "2024-07-01")).Select(t => t.Id).Should().Equal(["2"], "a task without a deadline is neither before nor after");
        Filtered(new GanttFilter("Deadline", GanttFilterOperator.NotEquals, "2024-06-25")).Select(t => t.Id).Should().Equal("1", "3");
    }

    [Fact]
    public void GanttHelper_An_Unparseable_Date_Value_Does_Not_Filter()
    {
        Filtered(new GanttFilter("Start", GanttFilterOperator.After, "not a date")).Should().HaveCount(3, "an unfinished row must not hide the whole chart");
        Filtered(new GanttFilter("Start", GanttFilterOperator.After, null)).Should().HaveCount(3);
    }

    [Fact]
    public void GanttHelper_Date_Values_Parse_Culture_Invariantly()
    {
        using var _ = CultureScope.Use("cs-CZ");

        Filtered(new GanttFilter("Start", GanttFilterOperator.Before, "2024-06-10")).Select(t => t.Id).Should().Equal("1");
    }
}
