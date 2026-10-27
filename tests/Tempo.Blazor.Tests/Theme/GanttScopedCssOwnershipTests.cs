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
            "tm-gantt__dialog-overlay", "tm-gantt__dialog", "tm-gantt__export-", "tm-gantt__import-",
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
    public void TheSharedDialogShell_IsGlobal_And_ImportedByTheBundleEntry()
    {
        // The overlay / card / header / actions are shared by three sub-components and by the history
        // drawer close button, so they are declared once in a global stylesheet (decision
        // GANTT-DIALOG-SHELL-GLOBAL) instead of being copied into three scoped files.
        var shell = File.ReadAllText(Path.Combine(Root, "src/Tempo.Blazor/wwwroot/css/components/_gantt-dialog.css"));
        foreach (var cls in new[] { "tm-gantt__dialog-overlay", "tm-gantt__dialog", "tm-gantt__dialog-header", "tm-gantt__dialog-title",
                     "tm-gantt__dialog-close", "tm-gantt__dialog-body", "tm-gantt__dialog-actions" })
        {
            shell.Should().Contain("." + cls + " {");
        }

        File.ReadAllText(Path.Combine(Root, "src/Tempo.Blazor/wwwroot/css/tempo-blazor.css"))
            .Should().Contain("components/_gantt-dialog.css");
        File.ReadAllText(Path.Combine(Root, "src/Tempo.Blazor/wwwroot/css/tempo-blazor.bundled.css"))
            .Should().Contain(".tm-gantt__dialog-overlay", "the committed bundle must carry the shell");
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
