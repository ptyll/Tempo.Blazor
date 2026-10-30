using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// TmGantt redesign, phase 1 (B21). A scoped <c>.razor.css</c> rewrites every selector to carry the
/// OWNING component's scope attribute, so a rule for a class that a child component renders never
/// applies. The Gantt's export / import / mapping dialogs, history drawer, filter panel, workload
/// grid and portfolio were all styled from <c>TmGantt.razor.css</c> and therefore unstyled.
/// <para>
/// The global <see cref="ScopedCssOwnershipTests"/> freezes a baseline that may only shrink; this
/// fixture pins the Gantt share of it at ZERO and additionally guards the move itself (the
/// sub-component stylesheets exist and own their classes).
/// </para>
/// </summary>
public sealed class GanttScopedCssOwnershipTests
{
    private const string SchedulerDir = "src/Tempo.Blazor/Components/Scheduler";

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    [Fact]
    public void NoGanttStylesheet_Styles_A_Class_Another_Component_Renders()
    {
        var findings = ScopedCssOwnership.Scan(Root)
            .Where(finding => finding.Key.Contains("Scheduler/TmGantt", StringComparison.Ordinal))
            .Select(finding => finding.Key)
            .ToList();

        findings.Should().BeEmpty(
            "every Gantt rule must live in the stylesheet of the component that renders the class "
            + "(or use ::deep deliberately). Found: " + string.Join(", ", findings));
    }

    [Fact]
    public void TheOwnershipBaseline_HasNoGanttRowsLeft()
    {
        var baseline = ScopedCssOwnership.Baseline(Root);

        baseline.Where(row => row.Contains("TmGantt", StringComparison.Ordinal)).Should().BeEmpty(
            "the Gantt rows were frozen by F0 for this plan to remove - drop them from "
            + "tests/Tempo.Blazor.Tests/Theme/scoped-css-ownership-baseline.txt");
    }

    [Theory]
    [InlineData("TmGanttExportDialog", "tm-gantt__export-formats")]
    [InlineData("TmGanttImportDialog", "tm-gantt__import-tabs")]
    [InlineData("TmGanttImportDialog", "tm-gantt-import__error")]
    [InlineData("TmGanttImportMappingDialog", "tm-gantt__mapping-row")]
    [InlineData("TmGanttHistoryDrawer", "tm-gantt__history-drawer")]
    [InlineData("TmGanttFilterPanel", "tm-gantt__filter-row")]
    [InlineData("TmGanttWorkloadView", "tm-gantt__workload-grid")]
    [InlineData("TmGanttPortfolio", "tm-gantt-portfolio__project-header")]
    [InlineData("TmGanttTaskPanel", "tm-gantt__panel-color-swatch")]
    public void EachSubComponent_OwnsItsStylesheet(string component, string expectedClass)
    {
        var path = Path.Combine(Root, SchedulerDir, component + ".razor.css");

        File.Exists(path).Should().BeTrue($"{component}.razor.css must exist");
        File.ReadAllText(path).Should().Contain("." + expectedClass);
    }

    [Fact]
    public void TmGanttStylesheet_NoLongerContainsTheMovedSubComponentClasses()
    {
        var css = File.ReadAllText(Path.Combine(Root, SchedulerDir, "TmGantt.razor.css"));

        string[] moved =
        [
            "tm-gantt__dialog-overlay", "tm-gantt__dialog-actions", "tm-gantt__export-", "tm-gantt__import-",
            "tm-gantt__mapping-", "tm-gantt__history-drawer", "tm-gantt__history-entry", "tm-gantt__history-list",
            "tm-gantt__history-empty", "tm-gantt__history-old", "tm-gantt__history-new", "tm-gantt__filter-row",
            "tm-gantt__filter-field", "tm-gantt__filter-op", "tm-gantt__filter-value", "tm-gantt__filter-remove",
            "tm-gantt__filter-panel-",
            "tm-gantt__workload-", "tm-gantt__workload ", "tm-gantt-portfolio", "tm-gantt__panel-color-swatch",
        ];

        foreach (var stem in moved)
        {
            // The sidebar drawers keep their own tm-gantt__sidebar-workload-* / sidebar-history-* rows.
            Regex.IsMatch(css, @"\." + Regex.Escape(stem), RegexOptions.None, TimeSpan.FromSeconds(5))
                .Should().BeFalse($"'.{stem}' belongs to a sub-component stylesheet now");
        }
    }

    [Fact]
    public void TheDialogShell_IsGone_The_Dialogs_Ride_TmModal()
    {
        // Phase 6 (G6): the export / import / mapping dialogs paint no overlay, card, header or footer of their own,
        // so the global shell sheet of phase 1 (decision GANTT-DIALOG-SHELL-GLOBAL) is removed.
        File.Exists(Path.Combine(Root, "src/Tempo.Blazor/wwwroot/css/components/_gantt-dialog.css")).Should().BeFalse();
        File.ReadAllText(Path.Combine(Root, "src/Tempo.Blazor/wwwroot/css/tempo-blazor.css"))
            .Should().NotContain("_gantt-dialog.css");
        File.ReadAllText(Path.Combine(Root, "src/Tempo.Blazor/wwwroot/css/tempo-blazor.bundled.css"))
            .Should().NotContain(".tm-gantt__dialog-overlay", "the committed bundle carries no dialog overlay any more");

        foreach (var component in new[] { "TmGanttExportDialog", "TmGanttImportDialog", "TmGanttImportMappingDialog" })
        {
            var markup = File.ReadAllText(Path.Combine(Root, SchedulerDir, component + ".razor"));
            markup.Should().Contain("<TmModal", $"{component} is a TmModal");
            markup.Should().NotContain("tm-gantt__dialog", $"{component} renders no own dialog shell");
        }
    }

    [Fact]
    public void NoGanttSource_Paints_Its_Own_Backdrop_Or_Fixed_Surface()
    {
        // The DoD line "0 own overlays in the Gantt": a grep over the whole Gantt source for the three tells.
        var offences = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Root, SchedulerDir), "TmGantt*.*")
                     .Where(p => p.EndsWith(".razor", StringComparison.Ordinal) || p.EndsWith(".razor.css", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(path);
            if (text.Contains("dialog-overlay", StringComparison.Ordinal)) offences.Add(Path.GetFileName(path) + ": dialog-overlay");
            if (text.Contains("__backdrop", StringComparison.Ordinal)) offences.Add(Path.GetFileName(path) + ": __backdrop");
            // Recorded fixed placements: the history drawer's opt-in Viewport placement, and the transparent
            // click-away scrims of the menus (TmGantt.razor.css context menu, TmGanttViewSettings.razor.css view
            // settings - the settings list moved there in phase 8, its scrim with it, and TmGanttMobileOverlays.razor.css
            // mobile long-press menu) - menus, not dialogs; they paint no backdrop.
            if (Regex.IsMatch(text, @"position:\s*fixed", RegexOptions.None, TimeSpan.FromSeconds(5))
                && !path.EndsWith("TmGanttHistoryDrawer.razor.css", StringComparison.Ordinal)
                && !path.EndsWith("TmGantt.razor.css", StringComparison.Ordinal)
                && !path.EndsWith("TmGanttViewSettings.razor.css", StringComparison.Ordinal)
                && !path.EndsWith("TmGanttMobileOverlays.razor.css", StringComparison.Ordinal))
            {
                offences.Add(Path.GetFileName(path) + ": position: fixed");
            }
        }

        offences.Should().BeEmpty("the dialogs are TmModal, the history drawer (HistoryDrawerPlacement.Viewport) and the two menu click-away scrims are the only recorded fixed placements");
    }
    [Fact]
    public void EveryClassTheMovedRulesStyle_IsStillRenderedByTheirOwner()
    {
        // The reverse of the move: a stylesheet that owns a class nobody renders is dead CSS.
        var scan = ScopedCssOwnership.Scan(Root);

        scan.Should().NotContain(finding => finding.Key.Contains("Scheduler/TmGantt"));
    }

    [Fact]
    public void EveryLocKeyUsedByAGanttComponent_ExistsInEveryResourceTable()
    {
        var schedulerDir = Path.Combine(Root, SchedulerDir);
        var literal = new Regex(@"Loc\[\s*""(?<key>[A-Za-z0-9_]+)""", RegexOptions.None, TimeSpan.FromSeconds(5));

        var used = Directory
            .EnumerateFiles(schedulerDir, "TmGantt*.*")
            .Where(path => path.EndsWith(".razor", StringComparison.Ordinal) || path.EndsWith(".razor.cs", StringComparison.Ordinal))
            .SelectMany(path => literal.Matches(File.ReadAllText(path)).Select(match => match.Groups["key"].Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        used.Should().NotBeEmpty();

        var resourceDir = Path.Combine(Root, "src", "Tempo.Blazor", "Resources");
        foreach (var file in new[] { "TmResources.json", "TmResources.cs.json", "TmResources.sk.json", "TmResources.fr.json" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(resourceDir, file)));
            var keys = doc.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            used.Where(key => !keys.Contains(key)).Should().BeEmpty($"{file} must define every key a TmGantt* component reads");
        }
    }

    [Fact]
    public void NoGanttSource_Uses_Eval_Or_ToStringOptionLabels()
    {
        var schedulerDir = Path.Combine(Root, SchedulerDir);
        foreach (var path in Directory.EnumerateFiles(schedulerDir, "TmGantt*.*")
                     .Where(p => p.EndsWith(".razor", StringComparison.Ordinal) || p.EndsWith(".razor.cs", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(path);
            text.Should().NotContain("\"eval\"", $"{Path.GetFileName(path)}: eval breaks under a CSP without unsafe-eval");
            text.Should().NotContain(">@op<", $"{Path.GetFileName(path)}: enum names are never shown");
            text.Should().NotContain(">@key.ToString()<", $"{Path.GetFileName(path)}: enum names are never shown");
        }
    }
}
