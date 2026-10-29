using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// FZE-10: the export dialog's BUILT-IN live preview. With <see cref="TmGanttExportDialog.PreviewTasks"/>
/// supplied it renders a pure SVG/HTML miniature of the chosen format and options (the host's
/// <c>PreviewContent</c> slot keeps winning); without it the neutral placeholder stays. Culture is
/// pinned through the mock localizer (en): CI runs Linux / UTC / invariant.
/// </summary>
public class TmGanttExportDialogTests : LocalizationTestBase
{
    // ═══════════════════════════════════════════════════════════════
    // Fixtures
    // ═══════════════════════════════════════════════════════════════

    private static List<TmWorkItem> Tasks(int count = 3) => Enumerable.Range(1, count)
        .Select(i => new TmWorkItem
        {
            Id = "t" + i,
            Title = "Task " + i,
            Start = new DateTime(2024, 6, 1).AddDays(i),
            End = new DateTime(2024, 6, 5).AddDays(i),
            PercentComplete = (i * 10) % 100,
            IsMilestone = i == 3,
        })
        .ToList();

    private static List<GanttDependency> Dependencies() =>
    [
        new() { FromId = "t1", ToId = "t2", DepType = GanttDependencyType.FinishToStart },
        new() { FromId = "t2", ToId = "t3", DepType = GanttDependencyType.FinishToStart },
    ];

    private IRenderedComponent<TmGanttExportDialog> RenderPreview(
        List<TmWorkItem>? tasks = null,
        Action<ComponentParameterCollectionBuilder<TmGanttExportDialog>>? build = null)
    {
        return Render<TmGanttExportDialog>(p =>
        {
            p.Add(x => x.IsOpen, true);
            if (tasks is not null)
            {
                p.Add(x => x.PreviewTasks, tasks);
                p.Add(x => x.PreviewDependencies, Dependencies());
            }

            build?.Invoke(p);
        });
    }

    /// <summary>Chooses an export format through the dialog's own radio tiles.</summary>
    private static void SelectFormat(IRenderedComponent<TmGanttExportDialog> cut, string format)
        => cut.Find($"[data-testid='export-format-{format}'] input").Change(format);

    // ═══════════════════════════════════════════════════════════════
    // When the built-in preview renders at all
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ExportDialog_BuiltInPreview_Renders_When_PreviewTasks_Supplied()
    {
        var cut = RenderPreview(Tasks());

        cut.Find(".tm-gantt-export-preview").Should().NotBeNull();
        cut.Find("[data-testid='export-preview']").QuerySelector(".tm-gantt-export-preview__page").Should().NotBeNull(
            "the default format is PDF, so the preview is the miniature page");

        var neutral = RenderPreview();
        neutral.FindAll(".tm-gantt-export-preview").Should().BeEmpty("without PreviewTasks the placeholder stays");
        neutral.Find("[data-testid='export-preview']").TextContent.Trim().Should().Be("Preview");

        var suppressed = RenderPreview(Tasks(), p => p.Add(x => x.ShowBuiltInPreview, false));
        suppressed.FindAll(".tm-gantt-export-preview").Should().BeEmpty("a host can turn the built-in preview off");
    }

    [Fact]
    public void ExportDialog_PreviewContent_Slot_Wins_Over_BuiltIn()
    {
        var cut = RenderPreview(Tasks(), p => p.Add(
            x => x.PreviewContent,
            (RenderFragment)(b => b.AddMarkupContent(0, "<img class=\"host-preview\" alt=\"\" />"))));

        cut.Find("[data-testid='export-preview']").QuerySelector(".host-preview").Should().NotBeNull();
        cut.FindAll(".tm-gantt-export-preview").Should().BeEmpty("the slot replaces the built-in preview");
    }

    // ═══════════════════════════════════════════════════════════════
    // The image (PDF / PNG) page
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("A4", "210/297")]
    [InlineData("A3", "297/420")]
    [InlineData("Letter", "216/279")]
    public void ExportDialog_Preview_Pdf_Respects_PaperSize_And_Orientation_AspectRatio(
        string paper, string expectedRatio)
    {
        var cut = RenderPreview(Tasks());

        cut.Find("[data-testid='export-paper-size'] select").Change(paper);

        var page = cut.Find(".tm-gantt-export-preview__page");
        page.GetAttribute("data-paper").Should().Be(paper);
        page.GetAttribute("style").Should().Contain($"aspect-ratio: {expectedRatio}");

        cut.Find("[data-testid='export-landscape'] input").Change(true);

        var landscape = cut.Find(".tm-gantt-export-preview__page");
        landscape.GetAttribute("style").Should().Contain(Flip(expectedRatio),
            "the orientation swaps the page's sides");
    }

    private static string Flip(string ratio)
    {
        var parts = ratio.Split('/');
        return $"{parts[1]}/{parts[0]}";
    }

    [Fact]
    public void ExportDialog_Preview_Toggles_CriticalPath_Today_Workload()
    {
        var cut = RenderPreview(Tasks(), p => p.Add(
            x => x.PreviewCriticalPathIds,
            new[] { "t2" }));

        cut.Find("[data-testid='export-include-today'] input").Change(false);
        cut.FindAll(".tm-gantt-export-preview__today").Should().BeEmpty("the today line is an option, not a given");
        cut.FindAll(".tm-gantt-export-preview__bar--critical").Should().BeEmpty();

        cut.Find("[data-testid='export-include-today'] input").Change(true);
        cut.Find("[data-testid='export-include-critical-path'] input").Change(true);
        cut.Find("[data-testid='export-include-workload'] input").Change(true);

        cut.Find(".tm-gantt-export-preview__today").Should().NotBeNull();
        cut.FindAll(".tm-gantt-export-preview__bar--critical").Should().ContainSingle(
            "only the supplied critical-path task paints the critical colour");
        cut.Find(".tm-gantt-export-preview__workload").Should().NotBeNull();

        cut.Find("[data-testid='export-include-critical-path'] input").Change(false);
        cut.FindAll(".tm-gantt-export-preview__bar--critical").Should().BeEmpty();
    }

    [Fact]
    public void ExportDialog_Preview_Zoom_Changes_Scale()
    {
        var cut = RenderPreview(Tasks());

        cut.Find(".tm-gantt-export-preview").GetAttribute("data-zoom").Should().Be("100",
            "no zoom level chosen means the current zoom, modelled as 100 %");

        cut.Find("[data-testid='export-zoom'] select").Change("150");

        cut.Find(".tm-gantt-export-preview").GetAttribute("data-zoom").Should().Be("150");
    }

    // ═══════════════════════════════════════════════════════════════
    // Excel and XML formats
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ExportDialog_Preview_Xlsx_Shows_Table_With_Selected_Columns_First_20_Rows()
    {
        var cut = RenderPreview(Tasks(4));
        SelectFormat(cut, "xlsx");

        // Down to Title | Start | End: the remaining checkboxes go off, the table follows.
        foreach (var column in new[] { "Progress", "Status", "Priority", "Duration", "Assignees" })
        {
            cut.Find($"[data-testid='export-column-{column}'] input").Change(false);
        }

        // The selected columns come first, in workbook order.
        var headers = cut.FindAll(".tm-gantt-export-preview__table th").Select(th => th.TextContent.Trim()).ToList();
        headers.Should().Equal("Task", "Start", "End");

        var rows = cut.FindAll(".tm-gantt-export-preview__table tbody tr");
        rows.Should().HaveCount(4);
        // The cell values are the exporter's own (shared) formatting.
        rows[0].QuerySelectorAll("td").Select(td => td.TextContent.Trim()).Should().Equal(
            "Task 1", "2024-06-02", "2024-06-06");
    }

    [Fact]
    public void ExportDialog_Preview_Xml_Shows_Summary_Counts()
    {
        var cut = RenderPreview(Tasks());
        SelectFormat(cut, "xml");

        var summary = cut.Find(".tm-gantt-export-preview__summary");
        summary.TextContent.Should().Contain("Tasks").And.Contain("3");
        summary.TextContent.Should().Contain("Dependencies").And.Contain("2");
        summary.TextContent.Should().Contain("Milestones").And.Contain("1");
    }

    [Fact]
    public void ExportDialog_Preview_Caps_Rows_And_Shows_More_Note()
    {
        var cut = RenderPreview(Tasks(26));
        SelectFormat(cut, "xlsx");

        cut.FindAll(".tm-gantt-export-preview__table tbody tr").Should().HaveCount(20,
            "the preview never renders the whole workbook");
        cut.Find("[data-testid='gantt-export-preview-more']").TextContent.Should().Contain("6");
    }

    // ═══════════════════════════════════════════════════════════════
    // Accessibility
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ExportDialog_Preview_Has_Accessible_Name_And_Hidden_Content()
    {
        var cut = RenderPreview(Tasks(2), p =>
        {
            p.Add(x => x.PreviewCriticalPathIds, new[] { "t2" });
        });

        cut.Find("[data-testid='export-landscape'] input").Change(true);

        var root = cut.Find(".tm-gantt-export-preview");
        root.GetAttribute("role").Should().Be("img");
        root.GetAttribute("aria-label").Should().StartWith("Preview:").And.Contain("2 tasks").And.Contain("Landscape");
        cut.Find(".tm-gantt-export-preview__body").GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void ExportDialog_Preview_Collapses_Into_Details_On_Mobile()
    {
        var cut = Render<TmGanttExportPreview>(p => p
            .Add(x => x.Tasks, Tasks())
            .Add(x => x.Options, new GanttExportOptions(GanttExportFormat.Pdf))
            .Add(x => x.Layout, TmLayoutMode.Mobile));

        var details = cut.Find("details.tm-gantt-export-preview");
        details.QuerySelector("summary").TextContent.Trim().Should().Be("Show preview");
        // The collapsed branch must keep the accessible image role the desktop root has.
        var image = details.QuerySelector("[role='img']");
        image.Should().NotBeNull("the <details> branch wraps the body in the role=img element too");
        image!.GetAttribute("aria-label").Should().StartWith("Preview:").And.Contain("3 tasks");
        image.QuerySelector(".tm-gantt-export-preview__body").Should().NotBeNull();
        details.GetAttribute("data-zoom").Should().Be("100");
    }
}
