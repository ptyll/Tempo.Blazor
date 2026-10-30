using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 6 (G6): the export, import and column-mapping dialogs ride the core
/// <see cref="TmModal"/> instead of their own <c>.tm-gantt__dialog-overlay</c>. Written BEFORE the
/// implementation: every test fails against the phase-5 dialogs for the reason named in its summary.
/// Culture is pinned through the mock localizer (<c>en</c> / <c>cs</c>): CI runs Linux / UTC / invariant.
/// </summary>
public class TmGanttPhase6DialogTests : LocalizationTestBase
{
    private static IElement Scope(IRenderedComponent<IComponent> cut) => cut.Find(".tm-modal-overlay .tm-focus-scope");

    // ═══════════════════════════════════════════════════════════════
    // Export dialog
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TmGanttExportDialog_Uses_TmModal_With_Dialog_Role_AriaModal_Escape_FocusTrap()
    {
        var closed = 0;
        var cut = Render<TmGanttExportDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.OnClose, () => { closed++; }));

        cut.FindAll(".tm-gantt__dialog-overlay").Should().BeEmpty("the dialog no longer paints its own scrim");
        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("popover").Should().Be("manual", "TmModal promotes to the top layer");
        overlay.ClassList.Should().Contain("tm-gantt__export-dialog", "the per-dialog class rides the overlay root");

        var scope = Scope(cut);
        scope.GetAttribute("role").Should().Be("dialog");
        scope.GetAttribute("aria-modal").Should().Be("true");
        var labelledBy = scope.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        var title = cut.Find($"[id='{labelledBy}']");
        title.ClassList.Should().Contain("tm-modal-title");
        title.TextContent.Trim().Should().Be("Export");
        cut.Find(".tm-modal-close").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        await cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync());

        closed.Should().Be(1, "Escape reaches the host through the shared focus trap");
    }

    [Fact]
    public void TmGanttExportDialog_Footer_Is_Cancel_Then_Export()
    {
        var cut = Render<TmGanttExportDialog>(p => p.Add(x => x.IsOpen, true));

        var buttons = cut.FindAll(".tm-modal-footer button");
        buttons.Select(b => b.TextContent.Trim()).Should().Equal("Cancel", "Export");
        buttons[1].ClassList.Should().Contain("tm-btn-primary");
        cut.FindAll(".tm-gantt__dialog-actions").Should().BeEmpty();
    }

    [Fact]
    public void TmGanttExportDialog_Format_Tiles_Are_Radio_Group()
    {
        var cut = Render<TmGanttExportDialog>(p => p.Add(x => x.IsOpen, true));

        var group = cut.Find(".tm-gantt__export-formats");
        group.GetAttribute("role").Should().Be("radiogroup");
        var labelledBy = group.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty("a radio group needs a name");
        cut.Find($"[id='{labelledBy}']").TextContent.Trim().Should().Be("Export format");

        var radios = group.QuerySelectorAll("input[type=radio]").ToList();
        radios.Should().HaveCount(4);
        radios.Select(r => r.GetAttribute("name")).Distinct().Should().HaveCount(1, "native radios share one name (arrow keys cycle them)");
        radios.Select(r => r.GetAttribute("value")).Should().Equal("Pdf", "Png", "Xlsx", "Xml");
        radios.Count(r => r.HasAttribute("checked")).Should().Be(1);
        radios.Should().OnlyContain(r => r.GetAttribute("class")!.Contains("tm-gantt__export-format-input"),
            "the input is visually hidden, NOT display:none - it must stay in the tab order");

        cut.Find("[data-testid='export-format-xlsx'] input").Change("Xlsx");

        cut.Find("[data-testid='export-format-xlsx']").ClassList.Should().Contain("tm-gantt__export-format-option--selected");
        cut.FindAll(".tm-gantt__export-format-option--selected").Should().ContainSingle();
    }

    [Fact]
    public void TmGanttExportDialog_Offers_IncludeCriticalPath_IncludeToday_IncludeWorkload_Columns()
    {
        GanttExportOptions? exported = null;
        var cut = Render<TmGanttExportDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.OnExport, EventCallback.Factory.Create<GanttExportOptions>(this, o => exported = o)));

        cut.Find("[data-testid='export-include-today'] input").HasAttribute("checked").Should().BeTrue("the today line is on by default");
        cut.Find("[data-testid='export-include-critical-path'] input").HasAttribute("checked").Should().BeFalse();
        cut.Find("[data-testid='export-include-workload'] input").HasAttribute("checked").Should().BeFalse();

        cut.Find("[data-testid='export-include-critical-path'] input").Change(true);
        cut.Find("[data-testid='export-include-workload'] input").Change(true);
        cut.Find("[data-testid='export-include-today'] input").Change(false);
        cut.Find("[data-testid='export-zoom'] select").Change("150");
        cut.Find("[data-testid='export-submit']").Click();

        exported.Should().NotBeNull();
        exported!.Format.Should().Be(GanttExportFormat.Pdf);
        exported.IncludeCriticalPath.Should().BeTrue();
        exported.IncludeWorkload.Should().BeTrue();
        exported.IncludeToday.Should().BeFalse();
        exported.ZoomLevel.Should().Be(150);
    }

    [Fact]
    public void TmGanttExportDialog_Xlsx_Offers_A_Column_Picker_And_Hides_The_Image_Options()
    {
        GanttExportOptions? exported = null;
        var cut = Render<TmGanttExportDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.OnExport, EventCallback.Factory.Create<GanttExportOptions>(this, o => exported = o)));

        cut.FindAll("[data-testid='export-columns']").Should().BeEmpty("columns are an Excel option");
        cut.Find("[data-testid='export-format-xlsx'] input").Change("Xlsx");

        cut.FindAll("[data-testid='export-include-today']").Should().BeEmpty("the image options do not apply to a workbook");
        cut.FindAll("[data-testid='export-paper-size']").Should().BeEmpty();
        var columns = cut.Find("[data-testid='export-columns']");
        columns.GetAttribute("role").Should().Be("group");
        cut.Find("[data-testid='export-column-Title'] input").HasAttribute("checked").Should().BeTrue();
        cut.Find("[data-testid='export-column-Deadline'] input").HasAttribute("checked").Should().BeFalse();

        cut.Find("[data-testid='export-column-Deadline'] input").Change(true);
        cut.Find("[data-testid='export-column-Priority'] input").Change(false);
        cut.Find("[data-testid='export-submit']").Click();

        exported!.Format.Should().Be(GanttExportFormat.Xlsx);
        exported.Columns.Should().NotBeNull();
        exported.Columns.Should().Contain(GanttColumnKey.Deadline).And.NotContain(GanttColumnKey.Priority).And.Contain(GanttColumnKey.Title);
    }

    [Fact]
    public void TmGanttExportDialog_Untouched_Columns_Export_As_Null_So_The_Exporter_Keeps_Its_Defaults()
    {
        GanttExportOptions? exported = null;
        var cut = Render<TmGanttExportDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.OnExport, EventCallback.Factory.Create<GanttExportOptions>(this, o => exported = o)));

        cut.Find("[data-testid='export-format-xlsx'] input").Change("Xlsx");
        cut.Find("[data-testid='export-submit']").Click();

        exported!.Columns.Should().BeNull();
    }

    [Fact]
    public void TmGanttExportDialog_PreviewContent_Slot()
    {
        var placeholder = Render<TmGanttExportDialog>(p => p.Add(x => x.IsOpen, true));
        placeholder.Find("[data-testid='export-preview']").TextContent.Trim().Should().Be("Preview",
            "the placeholder is the default (a live preview is backlog)");

        var cut = Render<TmGanttExportDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.PreviewContent, (RenderFragment)(b => b.AddMarkupContent(0, "<img class=\"host-preview\" alt=\"\" />"))));

        var preview = cut.Find("[data-testid='export-preview']");
        preview.QuerySelector(".host-preview").Should().NotBeNull("the host content replaces the placeholder");
        preview.TextContent.Should().NotContain("Preview");
    }

    // ═══════════════════════════════════════════════════════════════
    // TmGantt wiring: a dialog that confirms closes - it must not toggle back open
    // ═══════════════════════════════════════════════════════════════

    private static List<TmWorkItem> GanttTasks() =>
        [new() { Id = "1", Title = "Design", Start = new DateTime(2024, 6, 3), End = new DateTime(2024, 6, 7) }];

    [Fact]
    public void TmGantt_Export_Closes_The_Dialog_And_Raises_The_Options_Once()
    {
        var exported = new List<GanttExportOptions>();
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, GanttTasks())
            .Add(x => x.OnExportRequested, EventCallback.Factory.Create<GanttExportOptions>(this, o => exported.Add(o))));
        cut.Find("[data-testid='gantt-export-btn']").Click();
        cut.FindAll(".tm-modal-overlay.tm-gantt__export-dialog").Should().ContainSingle();

        cut.Find("[data-testid='export-submit']").Click();

        exported.Should().ContainSingle();
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty("a confirmed export closes the dialog; the second OnClose of the dialog must not toggle it open again");
    }

    [Fact]
    public async Task TmGantt_Escape_Closes_Every_Dialog_And_The_Trigger_Reopens_It()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, GanttTasks()));

        foreach (var trigger in new[] { "gantt-export-btn", "gantt-import-btn" })
        {
            cut.Find($"[data-testid='{trigger}']").Click();
            cut.FindAll(".tm-modal-overlay").Should().ContainSingle();

            await cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync());
            cut.FindAll(".tm-modal-overlay").Should().BeEmpty($"Escape closes the {trigger} dialog");
        }
    }

    [Fact]
    public void TmGantt_Import_Error_Keeps_The_Dialog_Open_So_The_Alert_Is_Seen()
    {
        string? reported = null;
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, GanttTasks())
            .Add(x => x.OnImportError, EventCallback.Factory.Create<string>(this, e => reported = e)));
        cut.Find("[data-testid='gantt-import-btn']").Click();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "tasks.xlsx"));

        cut.Find("[data-testid='import-submit']").Click();

        reported.Should().Contain("Import failed", "the host is told");
        cut.Find("[data-testid='import-error']").TextContent.Should().Contain("Import failed",
            "the dialog stays open, so the user sees why nothing was imported (it used to close and swallow the error)");
    }

    // ═══════════════════════════════════════════════════════════════
    // Import dialog
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGanttImportDialog_Uses_TmModal()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        cut.FindAll(".tm-gantt__dialog-overlay").Should().BeEmpty();
        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("popover").Should().Be("manual");
        overlay.ClassList.Should().Contain("tm-gantt__import-dialog");

        var scope = Scope(cut);
        scope.GetAttribute("role").Should().Be("dialog");
        scope.GetAttribute("aria-modal").Should().Be("true");
        cut.Find($"[id='{scope.GetAttribute("aria-labelledby")}']").TextContent.Trim().Should().Be("Import");
        cut.FindAll(".tm-modal-footer button").Select(b => b.TextContent.Trim()).Should().Equal("Cancel", "Import");
    }

    [Fact]
    public void TmGanttImportDialog_Tabs_Have_Aria_And_ArrowKeys()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        var list = cut.Find(".tm-gantt__import-tabs");
        list.GetAttribute("role").Should().Be("tablist");
        list.GetAttribute("aria-label").Should().Be("Import source");

        IReadOnlyList<IElement> Tabs() => cut.FindAll("[role='tab']");
        Tabs().Should().HaveCount(3);
        Tabs().Select(t => t.GetAttribute("aria-selected")).Should().Equal("true", "false", "false");
        Tabs().Select(t => t.GetAttribute("tabindex")).Should().Equal("0", "-1", "-1");
        Tabs().Should().OnlyContain(t => t.ClassList.Contains("tm-gantt__import-tab"));

        var panel = cut.Find("[role='tabpanel']");
        Tabs()[0].GetAttribute("aria-controls").Should().Be(panel.GetAttribute("id"));
        panel.GetAttribute("aria-labelledby").Should().Be(Tabs()[0].GetAttribute("id"));

        list.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Tabs().Select(t => t.GetAttribute("aria-selected")).Should().Equal("false", "true", "false");
        cut.Find("[role='tabpanel']").GetAttribute("aria-labelledby").Should().Be(Tabs()[1].GetAttribute("id"));
        cut.Find("input[type=file]").GetAttribute("accept").Should().Be(".xml,.mpp");

        cut.Find(".tm-gantt__import-tabs").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Tabs().Select(t => t.GetAttribute("aria-selected")).Should().Equal("false", "false", "true");

        cut.Find(".tm-gantt__import-tabs").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Tabs()[0].GetAttribute("aria-selected").Should().Be("true", "ArrowRight wraps");

        cut.Find(".tm-gantt__import-tabs").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Tabs()[2].GetAttribute("aria-selected").Should().Be("true", "ArrowLeft wraps");

        cut.Find(".tm-gantt__import-tabs").KeyDown(new KeyboardEventArgs { Key = "Home" });
        Tabs()[0].GetAttribute("aria-selected").Should().Be("true");
        cut.Find(".tm-gantt__import-tabs").KeyDown(new KeyboardEventArgs { Key = "End" });
        Tabs()[2].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void TmGanttImportDialog_Arrow_Keys_Move_The_DOM_Focus_With_The_Selection()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));
        var before = JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus");

        cut.Find(".tm-gantt__import-tabs").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus").Should().Be(before + 1,
            "selection and focus move together, so the next arrow key starts from the focused tab");
    }

    [Fact]
    public void TmGanttImportDialog_Reopening_Starts_From_A_Clean_State()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));
        cut.FindAll("[role='tab']")[1].Click();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "plan.xml"));
        cut.Find("[data-testid='import-submit']").Click();
        cut.Find("[data-testid='import-error']").Should().NotBeNull("the failed import leaves an error behind");
        cut.Find(".tm-gantt__import-file-name").TextContent.Trim().Should().Be("plan.xml");

        cut.Render(p => p.Add(x => x.IsOpen, false));
        cut.Render(p => p.Add(x => x.IsOpen, true));

        cut.Find(".tm-gantt__import-file-name").TextContent.Trim().Should().Be("No file chosen", "a reopened dialog does not remember the previous file");
        cut.Find("[data-testid='import-submit']").HasAttribute("disabled").Should().BeTrue("Import needs a file again");
        cut.FindAll("[data-testid='import-error']").Should().BeEmpty("the previous error is gone");
        cut.FindAll("[role='tab']").Select(t => t.GetAttribute("aria-selected")).Should().Equal("true", "false", "false");
        cut.Find("input[type=file]").GetAttribute("accept").Should().Be(".xlsx,.xls", "the Excel tab is selected again");
    }

    [Fact]
    public void TmGanttImportDialog_Wires_The_Tablist_Key_Guard()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/Components/Scheduler/TmGanttImportDialog.razor.js");
        var guard = module.SetupVoid("registerTablistKeys", _ => true).SetVoidResult();
        module.SetupVoid("registerFilePickerTrigger", _ => true).SetVoidResult();

        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        guard.Invocations.Should().NotBeEmpty("Home / End / Arrow keys must not scroll the dialog while they move the tab selection");
        var before = guard.Invocations.Count;
        cut.FindAll("[role='tab']")[2].Click();
        guard.Invocations.Count.Should().BeGreaterThanOrEqualTo(before, "the tablist stays on every tab, including JIRA, which has no file picker");
    }

    [Fact]
    public void TmGanttImportDialog_Remove_File()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        cut.FindAll("[data-testid='import-remove-file']").Should().BeEmpty("nothing to remove yet");
        cut.Find(".tm-gantt__import-file-name").TextContent.Trim().Should().Be("No file chosen");

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "plan.xlsx"));

        cut.Find(".tm-gantt__import-file-name").TextContent.Trim().Should().Be("plan.xlsx");
        var remove = cut.Find("[data-testid='import-remove-file']");
        remove.GetAttribute("aria-label").Should().Be("Remove file");
        remove.GetAttribute("type").Should().Be("button");
        cut.Find("[data-testid='import-submit']").HasAttribute("disabled").Should().BeFalse();

        remove.Click();

        cut.FindAll("[data-testid='import-remove-file']").Should().BeEmpty();
        cut.Find(".tm-gantt__import-file-name").TextContent.Trim().Should().Be("No file chosen");
        cut.Find("[data-testid='import-submit']").HasAttribute("disabled").Should().BeTrue("Import needs a file again");
    }

    [Fact]
    public void TmGanttImportDialog_Switching_Between_File_Tabs_Drops_The_Chosen_File()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "plan.xlsx"));

        cut.FindAll("[role='tab']")[1].Click();

        cut.Find(".tm-gantt__import-file-name").TextContent.Trim().Should().Be("No file chosen",
            "an .xlsx must never be fed to the MS Project importer");
        cut.Find("[data-testid='import-submit']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmGanttImportDialog_Shows_Supported_Formats_Hint()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));

        cut.Find(".tm-gantt__import-hint").TextContent.Trim().Should().Be("Supported formats: .xlsx, .xls");

        cut.FindAll("[role='tab']")[1].Click();
        cut.Find(".tm-gantt__import-hint").TextContent.Trim().Should().Be("Supported formats: .xml, .mpp");

        cut.FindAll("[role='tab']")[2].Click();
        cut.FindAll(".tm-gantt__import-hint").Should().BeEmpty("the JIRA tab imports from a URL, not a file");
    }

    [Fact]
    public void TmGanttImportDialog_Error_Uses_TmAlert()
    {
        var cut = Render<TmGanttImportDialog>(p => p.Add(x => x.IsOpen, true));
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "tasks.xlsx"));
        cut.Find("[data-testid='import-submit']").Click();

        var alert = cut.FindComponent<TmAlert>();
        alert.Instance.Severity.Should().Be(AlertSeverity.Error);
        var root = cut.Find("[data-testid='import-error']");
        root.ClassList.Should().Contain("tm-alert").And.Contain("tm-alert--error").And.Contain("tm-gantt-import__error");
        root.GetAttribute("role").Should().Be("alert");
        root.TextContent.Should().Contain("Import failed");
    }

    // ═══════════════════════════════════════════════════════════════
    // Mapping dialog
    // ═══════════════════════════════════════════════════════════════

    private static string[] FullColumns => ["Task Name", "Start Date", "End Date"];

    private IRenderedComponent<TmGanttImportMappingDialog> RenderMapping(
        IEnumerable<string> columns,
        IReadOnlyList<GanttColumnMapping>? initial = null,
        EventCallback<IReadOnlyList<GanttColumnMapping>>? onImport = null,
        Action? onClose = null) =>
        Render<TmGanttImportMappingDialog>(p =>
        {
            p.Add(x => x.IsOpen, true).Add(x => x.SourceColumns, columns);
            if (initial is not null) p.Add(x => x.InitialMappings, initial);
            if (onImport is { } cb) p.Add(x => x.OnImport, cb);
            if (onClose is not null) p.Add(x => x.OnClose, onClose);
        });

    private static string? Selected(IRenderedComponent<TmGanttImportMappingDialog> cut, string column)
    {
        var value = cut.Find($"[data-testid='mapping-select-{column}'] select").QuerySelector("option[selected]")?.GetAttribute("value");
        return string.IsNullOrEmpty(value) ? null : value;
    }

    [Fact]
    public void TmGanttImportMappingDialog_Uses_TmModal_With_Subtitle()
    {
        var cut = RenderMapping(FullColumns);

        cut.FindAll(".tm-gantt__dialog-overlay").Should().BeEmpty();
        var overlay = cut.Find(".tm-modal-overlay");
        overlay.ClassList.Should().Contain("tm-gantt__import-mapping-dialog");
        var scope = Scope(cut);
        scope.GetAttribute("role").Should().Be("dialog");
        scope.GetAttribute("aria-modal").Should().Be("true");
        cut.Find($"[id='{scope.GetAttribute("aria-labelledby")}']").TextContent.Trim().Should().Be("Column mapping");
        cut.Find(".tm-modal-subtitle").TextContent.Trim().Should().Be("Assign the columns of the imported file to the matching project fields.");
        cut.FindAll(".tm-modal-footer button").Select(b => b.TextContent.Trim()).Should().Equal("Cancel", "Import");
    }

    [Fact]
    public async Task TmGanttImportMappingDialog_Escape_Closes()
    {
        var closed = 0;
        var cut = RenderMapping(FullColumns, onClose: () => closed++);

        await cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync());

        closed.Should().Be(1);
    }

    [Fact]
    public void TmGanttImportMappingDialog_AutoMaps_By_Header_Name()
    {
        // Case and diacritics do not matter; English, Czech and French header names are all recognised.
        var cut = RenderMapping(["NAZEV UKOLU", "Début", "Finish", "Trvání", "Prédécesseurs", "ASSIGNED TO", "wbs", "Priorité", "Stav", "Poznámka"]);

        Selected(cut, "NAZEV UKOLU").Should().Be("Title", "diacritics-insensitive: 'Název úkolu'");
        Selected(cut, "Début").Should().Be("Start", "French");
        Selected(cut, "Finish").Should().Be("End", "English synonym");
        Selected(cut, "Trvání").Should().Be("Duration", "Czech");
        Selected(cut, "Prédécesseurs").Should().Be("Predecessor", "French plural");
        Selected(cut, "ASSIGNED TO").Should().Be("Assignees", "case-insensitive");
        Selected(cut, "wbs").Should().Be("WBS");
        Selected(cut, "Priorité").Should().Be("Priority");
        Selected(cut, "Stav").Should().Be("Status");
        Selected(cut, "Poznámka").Should().BeNull("an unknown header stays unmapped");
    }

    [Fact]
    public void TmGanttImportMappingDialog_AutoMap_Claims_Each_Target_Once()
    {
        var cut = RenderMapping(["Name", "Task Name", "Start", "End"]);

        Selected(cut, "Name").Should().Be("Title");
        Selected(cut, "Task Name").Should().BeNull("the first header wins; the second stays unmapped instead of raising a duplicate warning");
        cut.FindAll(".tm-gantt__mapping-duplicate").Should().BeEmpty();
    }

    [Fact]
    public void TmGanttImportMappingDialog_AutoMaps_The_Names_Of_The_Active_Locale()
    {
        UseCzechLocalization();

        var cut = RenderMapping(["Úkol", "Začátek", "Konec"]);

        Selected(cut, "Úkol").Should().Be("Title");
        Selected(cut, "Začátek").Should().Be("Start");
        Selected(cut, "Konec").Should().Be("End");
    }

    [Fact]
    public void TmGanttImportMappingDialog_InitialMappings_Parameter()
    {
        IReadOnlyList<GanttColumnMapping>? confirmed = null;
        var cut = RenderMapping(
            ["Zeta", "Name", "Start", "End"],
            initial: [new GanttColumnMapping { SourceColumn = "zeta", TargetProperty = GanttColumnKey.Title }],
            onImport: EventCallback.Factory.Create<IReadOnlyList<GanttColumnMapping>>(this, m => confirmed = m));

        Selected(cut, "Zeta").Should().Be("Title", "the host's mapping is the starting point (source column matched case-insensitively)");
        Selected(cut, "Name").Should().BeNull("the host already claimed Title, so auto-mapping does not double-book it");
        Selected(cut, "Start").Should().Be("Start", "the remaining columns are still auto-mapped");

        cut.Find("[data-testid='mapping-submit']").Click();

        confirmed.Should().NotBeNull();
        confirmed!.Should().Contain(m => m.SourceColumn == "Zeta" && m.TargetProperty == GanttColumnKey.Title);
    }

    [Fact]
    public void TmGanttImportMappingDialog_Requires_Title_And_Start()
    {
        var cut = RenderMapping(["Foo", "Bar", "Baz"]);
        bool Disabled() => cut.Find("[data-testid='mapping-submit']").HasAttribute("disabled");

        Disabled().Should().BeTrue("nothing is mapped");
        cut.Find("[data-testid='mapping-validation']").TextContent.Should().Contain("Task").And.Contain("Start").And.Contain("End or Duration");

        cut.Find("[data-testid='mapping-select-Foo'] select").Change("Title");
        cut.Find("[data-testid='mapping-select-Bar'] select").Change("Start");
        Disabled().Should().BeTrue("a finish date or a duration is still missing");
        cut.Find("[data-testid='mapping-validation']").TextContent.Should().Contain("End or Duration").And.NotContain("Task,");

        cut.Find("[data-testid='mapping-select-Baz'] select").Change("Duration");
        Disabled().Should().BeFalse("Title + Start + Duration is enough");
        cut.Find("[data-testid='mapping-validation']").TextContent.Trim().Should().BeEmpty("the region stays, its text is cleared");

        cut.Find("[data-testid='mapping-select-Baz'] select").Change("End");
        Disabled().Should().BeFalse("Title + Start + End is enough too");

        cut.Find("[data-testid='mapping-select-Bar'] select").Change("");
        Disabled().Should().BeTrue("un-mapping Start disables Import again");
    }

    [Fact]
    public void TmGanttImportMappingDialog_Does_Not_Import_While_Invalid()
    {
        var imported = 0;
        var cut = RenderMapping(["Foo"], onImport: EventCallback.Factory.Create<IReadOnlyList<GanttColumnMapping>>(this, _ => imported++));

        cut.Find("[data-testid='mapping-submit']").Click();

        imported.Should().Be(0);
    }

    [Fact]
    public void TmGanttImportMappingDialog_Warns_Duplicate_Target()
    {
        var cut = RenderMapping(["Task Name", "Start Date", "End Date", "Caption"]);
        cut.FindAll("[data-testid='mapping-duplicate']").Should().BeEmpty();

        cut.Find("[data-testid='mapping-select-Caption'] select").Change("Title");

        var warning = cut.Find("[data-testid='mapping-duplicate']");
        warning.ClassList.Should().Contain("tm-alert--warning");
        warning.TextContent.Should().Contain("Task");
        cut.FindAll(".tm-gantt__mapping-row--duplicate").Should().HaveCount(2, "both colliding rows are marked");
        cut.Find("[data-testid='mapping-submit']").HasAttribute("disabled").Should().BeFalse("a duplicate is a warning, not an error");
    }

    [Fact]
    public void TmGanttImportMappingDialog_Duplicate_Source_Columns_Render_One_Row_With_Unique_Ids()
    {
        var cut = RenderMapping(["Task Name", "Start Date", "Task Name", "End Date"]);

        var rows = cut.FindAll(".tm-gantt__mapping-row");
        rows.Should().HaveCount(3, "a repeated header is one mapping target");
        var ids = rows.Select(r => r.QuerySelector("select")!.GetAttribute("id")).ToList();
        ids.Should().OnlyHaveUniqueItems();
        cut.FindAll("[data-testid='mapping-select-Task Name']").Should().ContainSingle();
    }

    [Fact]
    public void TmGanttImportMappingDialog_Validation_Region_Is_Always_Rendered_And_Describes_The_Disabled_Import()
    {
        var cut = RenderMapping(["Foo", "Bar", "Baz"]);

        var region = cut.Find("p.tm-gantt__mapping-validation");
        region.GetAttribute("role").Should().Be("status");
        region.GetAttribute("aria-live").Should().Be("polite");
        region.TextContent.Should().Contain("End or Duration");
        var submit = cut.Find("[data-testid='mapping-submit']");
        submit.HasAttribute("disabled").Should().BeTrue();
        submit.GetAttribute("aria-describedby").Should().Be(region.GetAttribute("id")).And.NotBeNullOrEmpty();

        cut.Find("[data-testid='mapping-select-Foo'] select").Change("Title");
        cut.Find("[data-testid='mapping-select-Bar'] select").Change("Start");
        cut.Find("[data-testid='mapping-select-Baz'] select").Change("Duration");

        var settled = cut.Find("p.tm-gantt__mapping-validation");
        settled.TextContent.Trim().Should().BeEmpty("the live region stays in the DOM so assistive tech registered it; only its text toggles");
        settled.GetAttribute("role").Should().Be("status");
        settled.GetAttribute("aria-live").Should().Be("polite");
        cut.Find("[data-testid='mapping-submit']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void TmGanttImportMappingDialog_Select_Has_Accessible_Name()
    {
        var cut = RenderMapping(FullColumns);

        var rows = cut.FindAll(".tm-gantt__mapping-row");
        rows.Should().HaveCount(3);
        foreach (var row in rows)
        {
            var select = row.QuerySelector("select")!;
            var id = select.GetAttribute("id");
            id.Should().NotBeNullOrEmpty();
            var label = row.QuerySelector($"label[for='{id}']");
            label.Should().NotBeNull("the source column name is the <label for> of its select");
            label!.TextContent.Trim().Should().NotBeNullOrEmpty();
        }

        rows[0].QuerySelector("label")!.TextContent.Trim().Should().Be("Task Name");
    }

    [Fact]
    public void TmGanttImportMappingDialog_Targets_Include_Predecessor_Assignees_WBS()
    {
        var cut = RenderMapping(["Foo"]);

        var values = cut.Find("[data-testid='mapping-select-Foo'] select").QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToList();

        values.Should().Contain(["Predecessor", "Assignees", "WBS", "Title", "Start", "End", "Duration"]);
        values.Should().NotContain("Description", "GanttColumnKey.Description is not introduced");
        values.Should().NotContain(["Comments", "Budget", "ActualCost", "ResourceOverload"]);
    }

    [Fact]
    public void TmGanttImportMappingDialog_Mapping_Round_Trips_Enum_Values()
    {
        IReadOnlyList<GanttColumnMapping>? confirmed = null;
        var closed = 0;
        var cut = RenderMapping(
            ["Name", "Begin", "Length"],
            onImport: EventCallback.Factory.Create<IReadOnlyList<GanttColumnMapping>>(this, m => confirmed = m),
            onClose: () => closed++);

        cut.Find("[data-testid='mapping-select-Length'] select").Change(nameof(GanttColumnKey.Duration));
        cut.Find("[data-testid='mapping-submit']").Click();

        confirmed.Should().BeEquivalentTo(new[]
        {
            new GanttColumnMapping { SourceColumn = "Name", TargetProperty = GanttColumnKey.Title },
            new GanttColumnMapping { SourceColumn = "Begin", TargetProperty = GanttColumnKey.Start },
            new GanttColumnMapping { SourceColumn = "Length", TargetProperty = GanttColumnKey.Duration },
        });
        closed.Should().Be(1);
    }

    [Fact]
    public void TmGanttImportMappingDialog_Reopening_Starts_From_The_Auto_Mapping_Again()
    {
        var cut = RenderMapping(["Name", "Start", "End"]);
        cut.Find("[data-testid='mapping-select-Name'] select").Change("");
        Selected(cut, "Name").Should().BeNull();

        cut.Render(p => p.Add(x => x.IsOpen, false));
        cut.Render(p => p.Add(x => x.IsOpen, true));

        Selected(cut, "Name").Should().Be("Title");
    }
}
