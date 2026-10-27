using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 1 (B21-B23, B15, GA3 + minimal a11y). Written BEFORE the fixes:
/// every test fails against the pre-phase component for the reason named in its summary.
/// </summary>
public class TmGanttPhase1FixesTests : LocalizationTestBase
{
    private static TmWorkItem Task(string id, string? parentId = null, DateTime? start = null, DateTime? end = null) => new()
    {
        Id = id,
        Title = $"Task {id}",
        ParentId = parentId,
        Start = start ?? new DateTime(2024, 6, 3),
        End = end ?? new DateTime(2024, 6, 10),
    };

    private static List<TmWorkItem> Tasks() => [Task("1"), Task("2", start: new DateTime(2024, 6, 11), end: new DateTime(2024, 6, 20))];

    // ═══════════════════════════════════════════════════════════════
    // B22 – Portfolio
    // ═══════════════════════════════════════════════════════════════

    private static List<GanttPortfolioProject> Projects() =>
    [
        new()
        {
            Id = "p1",
            Name = "Alpha",
            Tasks = Tasks(),
            Dependencies = [new GanttDependency { Id = "d1", FromId = "1", ToId = "2" }],
        },
    ];

    [Fact]
    public void TmGanttPortfolio_Renders_Task_Rows_From_Project_Tasks()
    {
        // B22: the portfolio passed Data=/Dependencies= (parameters that do not exist), so every
        // embedded Gantt rendered empty.
        var cut = Render<TmGanttPortfolio>(p => p.Add(x => x.Projects, Projects()));

        cut.FindAll(".tm-gantt__tree-row").Should().HaveCount(2, "one tree row per project task");
        cut.FindAll("[data-testid*='task-bar-']").Should().HaveCount(2, "one bar per project task");
    }

    [Fact]
    public void TmGanttPortfolio_Forwards_Dependencies()
    {
        var cut = Render<TmGanttPortfolio>(p => p.Add(x => x.Projects, Projects()));

        var gantt = cut.FindComponent<TmGantt>().Instance;
        gantt.Items.Should().HaveCount(2);
        gantt.DependencyItems.Should().ContainSingle(d => d.Id == "d1");
        cut.FindAll("[data-testid*='dep-line-']").Should().NotBeEmpty("the dependency is drawn between the two bars");
    }

    [Fact]
    public void TmGanttPortfolio_Supports_Class_And_TestId()
    {
        var cut = Render<TmGanttPortfolio>(p => p
            .Add(x => x.Projects, Projects())
            .Add(x => x.Class, "host-class")
            .Add(x => x.DataTestId, "my-portfolio"));

        var root = cut.Find(".tm-gantt-portfolio");
        root.ClassList.Should().Contain("host-class");
        root.GetAttribute("data-testid").Should().Be("my-portfolio");
        cut.Instance.Should().BeAssignableTo<Tempo.Blazor.Components.TmComponentBase>();
    }

    // ═══════════════════════════════════════════════════════════════
    // B23 – task panel forwarding
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Forwards_Timer_Parameters_To_TaskPanel()
    {
        var task = Task("1");
        string? started = null;
        (string TaskId, GanttTimeLogEntry Entry)? stopped = null;

        var idle = Render<TmGantt>(p => p
            .Add(x => x.Items, [task])
            .Add(x => x.SelectedTask, task)
            .Add(x => x.ShowTaskPanel, true)
            .Add(x => x.OnTimerStarted, EventCallback.Factory.Create<string>(this, id => started = id)));

        idle.Find("[data-testid='timer-start-btn']").Click();
        started.Should().Be("1", "OnTimerStarted reaches the host");

        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, [task])
            .Add(x => x.SelectedTask, task)
            .Add(x => x.ShowTaskPanel, true)
            .Add(x => x.ActiveTimerTaskId, "1")
            .Add(x => x.OnTimerStopped, EventCallback.Factory.Create<(string TaskId, GanttTimeLogEntry Entry)>(this, a => stopped = a)));

        cut.Find("[data-testid='timer-stop-btn']").Click();
        stopped.Should().NotBeNull("ActiveTimerTaskId and OnTimerStopped reach the task panel");
        stopped!.Value.TaskId.Should().Be("1");
    }

    [Fact]
    public void TmGantt_Forwards_Attachment_And_Description_Callbacks_To_TaskPanel()
    {
        var task = Task("1");
        task.Attachments.Add(new TmAttachment { Id = "a1", FileName = "spec.pdf", Url = "https://example.test/spec.pdf" });
        string? description = null;
        string? removed = null;
        string? uploaded = null;

        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, [task])
            .Add(x => x.SelectedTask, task)
            .Add(x => x.ShowTaskPanel, true)
            .Add(x => x.OnDescriptionChanged, EventCallback.Factory.Create<string?>(this, d => description = d))
            .Add(x => x.OnAttachmentRemoved, EventCallback.Factory.Create<string>(this, id => removed = id))
            .Add(x => x.OnAttachmentUpload, EventCallback.Factory.Create<IBrowserFile>(this, f => uploaded = f.Name)));

        cut.Find("[data-testid='task-description'] textarea").Change("Hello");
        description.Should().Be("Hello");

        cut.Find("[data-testid='attachment-remove-a1']").Click();
        removed.Should().Be("a1");

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("x", "notes.txt"));
        uploaded.Should().Be("notes.txt");
    }

    // ═══════════════════════════════════════════════════════════════
    // B23 – localization
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGanttFilterPanel_Operator_Options_Are_Localized()
    {
        UseCzechLocalization();

        var cut = Render<TmGanttFilterPanel>(p => p
            .Add(x => x.Filters, [new GanttFilter("Title", GanttFilterOperator.Contains, "x")]));

        var options = cut.FindAll("select.tm-gantt__filter-op option").Select(o => o.TextContent.Trim()).ToList();
        options.Should().HaveCount(Enum.GetValues<GanttFilterOperator>().Length);
        options.Should().Contain("obsahuje");
        options.Should().NotContain(Enum.GetNames<GanttFilterOperator>(), "enum names are never shown to the user");
    }

    [Fact]
    public void TmGanttFilterPanel_Operator_Values_Round_Trip_As_Enum_Names()
    {
        IReadOnlyList<GanttFilter>? applied = null;
        var cut = Render<TmGanttFilterPanel>(p => p
            .Add(x => x.Filters, [new GanttFilter("Title", GanttFilterOperator.Contains, "x")])
            .Add(x => x.OnFiltersChanged, EventCallback.Factory.Create<IReadOnlyList<GanttFilter>>(this, f => applied = f)));

        cut.Find("select.tm-gantt__filter-op").Change(nameof(GanttFilterOperator.NotEquals));
        cut.Find("[data-testid='filter-apply']").Click();

        applied.Should().ContainSingle().Which.Operator.Should().Be(GanttFilterOperator.NotEquals);
    }

    [Fact]
    public void TmGanttFilterPanel_Icon_Buttons_Have_AriaLabel()
    {
        var cut = Render<TmGanttFilterPanel>(p => p
            .Add(x => x.Filters, [new GanttFilter("Title", GanttFilterOperator.Contains, "x")]));

        cut.Find(".tm-gantt__filter-panel-close").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        cut.Find(".tm-gantt__filter-remove").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TmGanttImportMappingDialog_Target_Options_Are_Localized()
    {
        UseCzechLocalization();

        var cut = Render<TmGanttImportMappingDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.SourceColumns, ["Name"]));

        var options = cut.FindAll("select.tm-gantt__mapping-select option").Select(o => o.TextContent.Trim()).ToList();
        options.Should().Contain(["Úkol", "Začátek", "Konec", "Stav", "Délka", "Priorita", "Průběh", "Termín"]);
        options.Should().NotContain(["Title", "Start", "End", "Progress", "Status", "Priority", "Duration", "Deadline", "Estimation", "TimeLog"]);
    }

    [Fact]
    public void TmGanttImportMappingDialog_Mapping_Still_Round_Trips_Enum_Values()
    {
        IReadOnlyList<GanttColumnMapping>? confirmed = null;
        var cut = Render<TmGanttImportMappingDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.SourceColumns, ["Name"])
            .Add(x => x.OnImport, EventCallback.Factory.Create<IReadOnlyList<GanttColumnMapping>>(this, m => confirmed = m)));

        cut.Find("[data-testid='mapping-select-Name']").Change(nameof(GanttColumnKey.Duration));
        cut.FindAll(".tm-gantt__dialog-actions button")[0].Click();

        confirmed.Should().ContainSingle().Which.TargetProperty.Should().Be(GanttColumnKey.Duration);
    }

    [Fact]
    public void TmGanttHistoryDrawer_Empty_Shows_Empty_Text()
    {
        // Pre-phase the empty state repeated the drawer title ("History") instead of TmGantt_HistoryEmpty.
        var cut = Render<TmGanttHistoryDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.History, []));

        cut.Find(".tm-gantt__history-empty").TextContent.Trim().Should().Be("No history yet.");
        cut.Find(".tm-gantt__dialog-close").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TmGanttHistoryDrawer_Timestamp_Uses_The_Localizer_Culture()
    {
        var timestamp = new DateTimeOffset(2026, 3, 9, 14, 5, 0, TimeSpan.Zero);
        var cut = Render<TmGanttHistoryDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.History, [new TmActivityEntry { Id = "e1", Action = "TaskChanged", Timestamp = timestamp }]));

        var expected = timestamp.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("en"));
        cut.Find(".tm-gantt__history-entry-time").TextContent.Trim().Should().Be(expected);
    }

    [Fact]
    public void TmGantt_SidebarHistory_Uses_Localized_Change_Type()
    {
        var history = new[]
        {
            new TmActivityEntry
            {
                Id = "e1",
                Action = "StatusChanged",
                EntityRef = new TmEntityRef { EntityType = "task", EntityId = "1" },
            },
        };

        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.History, history));

        cut.Find("[data-testid='sidebar-btn-history']").Click();

        cut.Find(".tm-gantt__sidebar-history-action").TextContent.Trim().Should().Be("Status changed");
    }

    [Fact]
    public void GanttLabels_ShortDayMonth_Drops_The_Year_From_The_Culture_Pattern()
    {
        var date = new DateTime(2026, 3, 9);

        GanttLabels.ShortDayMonth(date, CultureInfo.GetCultureInfo("en-US")).Should().Be("3/9");
        GanttLabels.ShortDayMonth(date, CultureInfo.GetCultureInfo("cs-CZ")).Should().MatchRegex(@"^0?9\.\s?0?3\.?$");
        GanttLabels.ShortDayMonth(date, CultureInfo.GetCultureInfo("sv-SE")).Should().Be("03-09");
    }

    // ═══════════════════════════════════════════════════════════════
    // GA3 – reports panel
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGanttReportsPanel_NewReport_Hidden_Without_Callback()
    {
        var cut = Render<TmGanttReportsPanel>();

        cut.FindAll("[data-testid='reports-new-btn']").Should().BeEmpty("a button without a handler is dead UI");
    }

    [Fact]
    public void TmGanttReportsPanel_NewReport_Fires_OnNewReportRequested()
    {
        var fired = 0;
        var cut = Render<TmGanttReportsPanel>(p => p
            .Add(x => x.OnNewReportRequested, () => { fired++; }));

        cut.Find("[data-testid='reports-new-btn']").Click();

        fired.Should().Be(1);
    }

    [Fact]
    public void TmGantt_Forwards_OnNewReportRequested_To_ReportsPanel()
    {
        var fired = 0;
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.OnNewReportRequested, () => { fired++; }));

        cut.Find("[data-testid='sidebar-btn-reports']").Click();
        cut.Find("[data-testid='reports-new-btn']").Click();

        fired.Should().Be(1);
    }

    [Fact]
    public void TmGantt_Reports_Panel_Without_Callback_Has_No_NewReport_Button()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        cut.Find("[data-testid='sidebar-btn-reports']").Click();

        cut.FindAll("[data-testid='reports-new-btn']").Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // Import – disabled until there is something to import
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGanttImportDialog_Import_Disabled_Without_File()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        cut.Find("[data-testid='import-submit']").HasAttribute("disabled").Should().BeTrue();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "tasks.xlsx"));

        cut.Find("[data-testid='import-submit']").HasAttribute("disabled").Should().BeFalse("a file is chosen");
    }

    [Fact]
    public void TmGanttImportDialog_Import_Disabled_Until_Jira_Fields_Are_Filled()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));
        cut.FindAll(".tm-gantt__import-tab")[2].Click();

        cut.Find("[data-testid='import-submit']").HasAttribute("disabled").Should().BeTrue();

        var inputs = cut.FindAll(".tm-gantt__import-jira-form input");
        inputs[0].Change("https://jira.example.test");
        inputs[1].Change("token");
        cut.Find("[data-testid='import-submit']").HasAttribute("disabled").Should().BeTrue("the project key is still empty");

        cut.FindAll(".tm-gantt__import-jira-form input")[2].Change("PROJ");
        cut.Find("[data-testid='import-submit']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void TmGanttImportDialog_Error_Surface_Has_Its_Own_Class()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "tasks.xlsx"));
        cut.Find("[data-testid='import-submit']").Click();

        cut.Find("[data-testid='import-error']").ClassList.Should().Contain("tm-gantt-import__error");
        cut.FindAll(".tm-gantt-task-panel__error").Should().BeEmpty("the import dialog no longer borrows the task panel's class");
    }

    // ═══════════════════════════════════════════════════════════════
    // B15 – FitToScreen
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_FitToScreen_Uses_ElementReference()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<double>("tmGantt.getClientWidth", _ => true).SetResult(5000);

        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));
        cut.Find("[data-testid='gantt-fit-to-screen']").Click();

        JSInterop.Invocations.Select(i => i.Identifier).Should().Contain("tmGantt.getClientWidth");
        JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain("eval", "eval breaks under a CSP without unsafe-eval");
        cut.Find(".tm-gantt__zoom-level").TextContent.Trim().Should().Be("200%", "5000px is wider than the whole timeline, so it zooms to the 200% cap");
    }

    [Fact]
    public void TmGantt_FitToScreen_Uses_ContainerWidth_When_Set()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<double>("tmGantt.getClientWidth", _ => true).SetResult(5000);

        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.ContainerWidth, 1));
        cut.Find("[data-testid='gantt-fit-to-screen']").Click();

        JSInterop.Invocations.Select(i => i.Identifier).Should().NotContain("tmGantt.getClientWidth", "ContainerWidth overrides the measurement");
        cut.Find(".tm-gantt__zoom-level").TextContent.Trim().Should().Be("50%", "a 1px container zooms to the 50% floor");
    }

    // ═══════════════════════════════════════════════════════════════
    // Minimal a11y
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_ViewSwitch_Has_AriaPressed()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        cut.Find("[data-testid='gantt-view-week']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='gantt-view-day']").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find("[data-testid='gantt-view-week']").ClassList.Should().Contain("tm-btn-secondary", "the active view is visually distinct");

        cut.Find("[data-testid='gantt-view-day']").Click();

        cut.Find("[data-testid='gantt-view-day']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='gantt-view-week']").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find("[data-testid='gantt-view-day']").ClassList.Should().Contain("tm-btn-secondary");
        cut.Find("[data-testid='gantt-view-week']").ClassList.Should().Contain("tm-btn-ghost");
    }

    [Fact]
    public void TmGantt_IconButtons_Have_AriaLabel()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        var iconOnly = cut.FindAll(".tm-gantt__toolbar button, .tm-gantt__sidebar button")
            .Where(b => string.IsNullOrWhiteSpace(b.TextContent))
            .ToList();

        iconOnly.Should().NotBeEmpty();
        iconOnly.Where(b => string.IsNullOrWhiteSpace(b.GetAttribute("aria-label")))
            .Select(b => b.GetAttribute("data-testid") ?? b.OuterHtml)
            .Should().BeEmpty("every icon-only button needs a localized accessible name");
        cut.Find("[data-testid='gantt-zoom-slider']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TmGantt_Tree_Expand_Button_Has_Name_And_State()
    {
        var parent = Task("1");
        var child = Task("2", parentId: "1");
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, [parent, child]));

        var button = cut.Find(".tm-gantt__expand-btn");
        button.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        button.GetAttribute("aria-expanded").Should().NotBeNull();
    }

    [Fact]
    public void TmGanttTaskPanel_Icon_Buttons_Have_AriaLabel()
    {
        var task = Task("1");
        task.Attachments.Add(new TmAttachment { Id = "a1", FileName = "spec.pdf" });

        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(x => x.Task, task)
            .Add(x => x.AllTasks, [task])
            .Add(x => x.OnClose, () => { }));

        cut.Find("[data-testid='task-panel-close']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        cut.Find("[data-testid='attachment-remove-a1']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TmGanttExportDialog_Has_Dialog_Role_And_Escape_Closes()
    {
        var closed = 0;
        var cut = Render<TmGanttExportDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.OnClose, () => { closed++; }));

        var dialog = cut.Find(".tm-gantt__dialog");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");
        var labelledBy = dialog.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        cut.Find($"#{labelledBy}").TextContent.Trim().Should().Be("Export");
        cut.Find(".tm-gantt__dialog-close").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync());

        closed.Should().Be(1, "Escape closes the dialog");
    }

    [Fact]
    public void TmGanttImportMappingDialog_Has_Dialog_Role_And_Escape_Closes()
    {
        var closed = 0;
        var cut = Render<TmGanttImportMappingDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.SourceColumns, ["Name"])
            .Add(x => x.OnClose, () => { closed++; }));

        var dialog = cut.Find(".tm-gantt__import-mapping-dialog");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");
        var labelledBy = dialog.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        cut.Find($"#{labelledBy}").TextContent.Trim().Should().Be("Column mapping");
        cut.Find(".tm-gantt__dialog-close").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync());

        closed.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════
    // Review round 1
    // ═══════════════════════════════════════════════════════════════

    private static double TimelineWidth(IRenderedComponent<TmGantt> cut)
    {
        var style = cut.Find(".tm-gantt__timeline-content").GetAttribute("style")!;
        return double.Parse(style.Split("width:")[1].Split("px")[0].Trim(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TmGantt_FitToScreen_Is_Idempotent_At_200_Percent()
    {
        // The timeline width already includes the zoom, so the new zoom is current * container / width.
        var probe = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));
        for (var i = 0; i < 4; i++)
        {
            probe.Find("[data-testid='gantt-zoom-in']").Click();
        }

        probe.Find(".tm-gantt__zoom-level").TextContent.Trim().Should().Be("200%");
        var widthAt200 = TimelineWidth(probe);

        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.ContainerWidth, widthAt200 * 0.6));
        for (var i = 0; i < 4; i++)
        {
            cut.Find("[data-testid='gantt-zoom-in']").Click();
        }

        cut.Find("[data-testid='gantt-fit-to-screen']").Click();
        var first = cut.Find(".tm-gantt__zoom-level").TextContent.Trim();
        cut.Find("[data-testid='gantt-fit-to-screen']").Click();
        var second = cut.Find(".tm-gantt__zoom-level").TextContent.Trim();

        first.Should().Be("100%", "60% of the 200% width is 120%, floored to the 25% step");
        second.Should().Be(first, "fitting an already fitted timeline is a no-op");
    }
    [Fact]
    public void TmGantt_Toolbar_Toggles_Expose_Expanded_State()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        foreach (var id in new[] { "gantt-filter-btn", "gantt-history-btn", "gantt-view-settings-btn" })
        {
            var button = cut.Find($"[data-testid='{id}']");
            button.GetAttribute("aria-expanded").Should().Be("false", id);
            button.GetAttribute("aria-haspopup").Should().Be("true", id);
        }

        cut.Find("[data-testid='gantt-filter-btn']").Click();
        cut.Find("[data-testid='gantt-filter-btn']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='gantt-view-settings-btn']").Click();
        cut.Find("[data-testid='gantt-view-settings-btn']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='gantt-history-btn']").Click();
        cut.Find("[data-testid='gantt-history-btn']").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void TmGantt_TaskPanel_And_Sidebar_Toggles_Expose_Pressed_State()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        cut.Find("[data-testid='gantt-toggle-task-panel']").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find("[data-testid='gantt-toggle-task-panel']").Click();
        cut.Find("[data-testid='gantt-toggle-task-panel']").GetAttribute("aria-pressed").Should().Be("true");

        foreach (var panel in new[] { "tasks", "workload", "history", "notifications", "reports" })
        {
            cut.Find($"[data-testid='sidebar-btn-{panel}']").GetAttribute("aria-pressed").Should().NotBeNull(panel);
        }

        cut.Find("[data-testid='sidebar-btn-history']").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find("[data-testid='sidebar-btn-history']").Click();
        cut.Find("[data-testid='sidebar-btn-history']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='sidebar-btn-workload']").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void TmGanttTaskPanel_RemoveDependency_Button_Names_The_Dependency()
    {
        var task = Task("1");
        var from = Task("2");
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(x => x.Task, task)
            .Add(x => x.AllTasks, [task, from])
            .Add(x => x.Dependencies, [new GanttDependency { Id = "d1", FromId = "2", ToId = "1", Type = 0 }]));

        cut.Find("[data-testid='task-dep-remove-d1']").GetAttribute("aria-label")
            .Should().Be("Remove dependency: Task 2 (FS)");
    }

    [Fact]
    public void TmGanttTaskPanel_RemoveDependency_Button_Is_Localized()
    {
        UseCzechLocalization();
        var task = Task("1");
        var from = Task("2");
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(x => x.Task, task)
            .Add(x => x.AllTasks, [task, from])
            .Add(x => x.Dependencies, [new GanttDependency { Id = "d1", FromId = "2", ToId = "1", Type = 0 }]));

        cut.Find("[data-testid='task-dep-remove-d1']").GetAttribute("aria-label")
            .Should().Be("Odebrat závislost: Task 2 (FS)");
    }

    [Fact]
    public void TmGantt_Tree_Expander_Label_Names_The_Task()
    {
        var parent = Task("1");
        var child = Task("2", parentId: "1");
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, [parent, child]));

        var button = cut.Find(".tm-gantt__expand-btn");
        var expanded = button.GetAttribute("aria-expanded") == "true";
        button.GetAttribute("aria-label").Should().Be(expanded ? "Collapse Task 1" : "Expand Task 1");

        button.Click();

        var after = cut.Find(".tm-gantt__expand-btn");
        (after.GetAttribute("aria-expanded") == "true").Should().Be(!expanded);
        after.GetAttribute("aria-label").Should().Be(expanded ? "Expand Task 1" : "Collapse Task 1");
    }

    [Fact]
    public void TmGantt_Tree_Expander_Label_Is_Localized()
    {
        UseCzechLocalization();
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, [Task("1"), Task("2", parentId: "1")]));

        cut.Find(".tm-gantt__expand-btn").GetAttribute("aria-label")
            .Should().MatchRegex(@"^(Sbalit|Rozbalit) Task 1$");
    }

    [Fact]
    public void TmGanttHistoryDrawer_Timestamp_Uses_The_TimeZone_Parameter()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("tm-test-plus-0545", TimeSpan.FromMinutes(345), "Test", "Test");
        var timestamp = new DateTimeOffset(2026, 3, 9, 22, 30, 0, TimeSpan.Zero);
        var cut = Render<TmGanttHistoryDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.TimeZone, zone)
            .Add(x => x.History, [new TmActivityEntry { Id = "e1", Action = "TaskChanged", Timestamp = timestamp }]));

        var expected = TimeZoneInfo.ConvertTime(timestamp, zone).ToString("g", CultureInfo.GetCultureInfo("en"));
        cut.Find(".tm-gantt__history-entry-time").TextContent.Trim().Should().Be(expected);
    }

    [Fact]
    public void TmGanttPortfolio_Namespaces_Each_Nested_Gantt_Test_Ids()
    {
        var projects = new List<GanttPortfolioProject>
        {
            new() { Id = "p1", Name = "Alpha", Tasks = Tasks(), Dependencies = [] },
            new() { Id = "p2", Name = "Beta", Tasks = Tasks(), Dependencies = [] },
        };

        var cut = Render<TmGanttPortfolio>(p => p.Add(x => x.Projects, projects));

        var ids = cut.FindAll("[data-testid*='task-bar-']").Select(e => e.GetAttribute("data-testid")).ToList();
        ids.Should().OnlyHaveUniqueItems("two projects with the same task ids must not collide");
        ids.Should().HaveCount(4);
        cut.FindAll("[data-testid^='gantt-p1-']").Should().NotBeEmpty();
        cut.FindAll("[data-testid^='gantt-p2-']").Should().NotBeEmpty();
        cut.FindAll("[data-testid^='portfolio-project-']").Should().HaveCount(2, "the nested prefix never matches a project card");
    }

    [Fact]
    public void GanttLabels_ShortDayMonth_Keeps_The_Trailing_Dot_For_Slovak_And_Czech()
    {
        var date = new DateTime(2026, 3, 9);

        GanttLabels.ShortDayMonth(date, CultureInfo.GetCultureInfo("sk-SK")).Should().Be("9. 3.");
        GanttLabels.ShortDayMonth(date, CultureInfo.GetCultureInfo("cs-CZ")).Should().MatchRegex(@"^0?9\. ?0?3\.$");
    }}
