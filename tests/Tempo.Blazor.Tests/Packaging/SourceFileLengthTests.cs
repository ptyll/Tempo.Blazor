using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// N270 / F13 — the 1200-line source-file ceiling for the Tempo repository, guarded fail-closed.
/// Scope: tracked <c>*.cs</c>/<c>*.razor</c> under the repo root, minus build outputs
/// (<c>obj/</c>, <c>bin/</c>) and the <c>src/Tempo.Blazor.Demo*</c> demo-app trees (demo code is
/// not shipped product). Measured 2026-09-23: exactly 47 files exceed the ceiling; that set is
/// FROZEN below — it may only shrink (a file split or trimmed under the ceiling gets its entry
/// deleted), never grow (a new file over the ceiling is a red build).
/// </summary>
public class SourceFileLengthTests
{
    private const int CeilingLines = 1200;

    /// <summary>Frozen on 2026-09-23 at 47 entries — delete entries as files are fixed; never add.</summary>
    private static readonly string[] FrozenOverBudgetSources =
    [
        "src/Tempo.Blazor.DocumentEditor/Components/DocumentEditor/TmDocumentEditor.razor.cs",
        "tests/Tempo.Blazor.Tests/Localization/LocalizationTestBase.cs",
        "tests/Tempo.Blazor.E2E/SpreadsheetE2ETests.cs",
        "src/Tempo.Blazor.DiagramEditor/Components/Diagram/Stencils/BuiltInDiagramStencilProvider.cs",
        "src/Tempo.Blazor.DocumentEditor/Components/DocumentEditor/TmDocumentEditorToolbar.razor",
        "tests/Tempo.Blazor.Tests/Components/DocumentEditor/TmDocumentEditorTests.cs",
        "src/Tempo.Blazor.DocumentFormats/Docx/DocumentDocxImporter.cs",
        "src/Tempo.Blazor.DiagramEditor/Components/Diagram/TmDiagramCanvas.razor.cs",
        "tests/Tempo.Blazor.E2E/DocumentEditorCanvasShapesDrawingsE2ETests.cs",
        "src/Tempo.Blazor.DiagramEditor/Components/Diagram/TmDiagramEditor.razor.cs",
        "src/Tempo.Blazor.Abstractions/Wireframe/BuiltInComponentSchemas.cs",
        "src/Tempo.Blazor/Components/DataTable/TmDataTable.razor.cs",
        "tests/Tempo.Blazor.E2E/DiagramEdgeE2ETests.cs",
        "src/Tempo.Blazor.Abstractions/DocumentEditor/Services/DocumentLayoutEngine.cs",
        "JsonDocumentation/JsonDocumentationGenerator/PackageDocumentationGenerator.cs",
        "tests/Tempo.Blazor.Tests/DocumentEditor/WysiwygPatchApplierTests.cs",
        "src/Tempo.Blazor.Abstractions/DocumentEditor/Services/InMemoryDocumentEditorProvider.cs",
        "src/Tempo.Blazor.Abstractions/DocumentEditor/Services/WysiwygPatchApplier.cs",
        "src/Tempo.Blazor.DocumentFormats/Docx/DocumentDocxExporter.cs",
        "tests/Tempo.Blazor.E2E/NotionCommentsE2ETests.cs",
        "src/Tempo.Blazor/Components/Charts/TmChart.razor",
        "src/Tempo.Blazor.Spreadsheet/Components/Spreadsheet/TmSpreadsheetCanvasGrid.razor.cs",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Page/TmNotionPage.razor.cs",
        "src/Tempo.Blazor.Modeling/Components/Modeling/BuiltInModelingProfiles.cs",
        "src/Tempo.Blazor.DiagramEditor/Components/Diagram/TmDiagramPropertiesPanel.razor.cs",
        "tests/Tempo.Blazor.E2E/DocumentEditorCanvasMathEquationsE2ETests.cs",
        "tests/Tempo.Blazor.Tests/Components/DocumentEditor/CanvasEngine/Model/CanvasModelConverterTests.cs",
        "src/Tempo.ReportServer.Api/ReportServerApiExtensions.cs",
        "tests/Tempo.Blazor.Tests/Theme/CssCascade.cs",
        "src/Tempo.Blazor.Spreadsheet/Components/Spreadsheet/TmSpreadsheet.razor.cs",
        "tests/Tempo.Blazor.Tests/Models/DocumentEditor/DocumentLayoutEngineTests.cs",
        "src/Tempo.Blazor/Components/Scheduler/TmGantt.razor.cs",
        "tests/Tempo.Blazor.Tests/Packaging/ReleaseGateFilterTests.cs",
        "src/Tempo.Blazor.Abstractions/DocumentEditor/Services/DocumentOperationApplier.cs",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/TmNotionBlock.razor.cs",
        "src/Tempo.Blazor/Components/DataDisplay/TmMultiViewList.razor",
        "src/Tempo.Blazor.DocumentEditor/Components/DocumentEditor/TmDocumentEditor.razor",
        "tests/Tempo.Blazor.E2E/DocumentEditorCanvasUxFixE2ETests.cs",
        "src/Tempo.Blazor.Signing/Components/Signing/TmPdfTemplateDesigner.razor.cs",
        "src/Tempo.Blazor/Components/Files/TmDocumentManager.razor.cs",
        "tests/Tempo.Blazor.E2E/DocumentEditorCanvasImageE2ETests.cs",
        "tests/Tempo.Blazor.DocumentFormats.Tests/DocumentDocxFormatTests.cs",
        "src/Tempo.ReportServer.Web.Client/Services/DemoTempoReportServerClient.cs",
        "tests/Tempo.Blazor.Tests/Theme/UnconstrainedClassOwnershipTests.cs",
        "src/Tempo.Blazor.DocumentEditor/Components/DocumentEditor/TmDocumentCanvasEngineHost.razor",
        "src/Tempo.Reporting.Engine/Text/BidiAlgorithm.cs",
        "src/Tempo.Reporting.Abstractions/Definitions/Rdl/RdlReportImporter.cs",
    ];

    /// <summary>
    /// The ceiling itself: every over-budget source file must already be frozen — a NEW file (or a
    /// file pushed over by growth) is a red build, not a quiet append.
    /// </summary>
    [Fact]
    public void NoNewSourceFileExceeds1200Lines()
    {
        var overBudget = EnumerateOverBudgetSources().ToList();

        overBudget.Should().BeSubsetOf(
            FrozenOverBudgetSources,
            "a source file over {0} lines that is not frozen is a NEW ceiling breach — split it or "
            + "shrink it; the frozen list may only shrink (N270/F13). Breaches: {1}",
            CeilingLines,
            string.Join(" | ", overBudget.Except(FrozenOverBudgetSources, StringComparer.Ordinal)));
    }

    /// <summary>
    /// The other direction: a frozen entry whose file no longer exists or already fits the ceiling
    /// must be DELETED from the list — a list that only grows stops describing the code and starts
    /// describing history.
    /// </summary>
    [Fact]
    public void FrozenEntriesThatFitOrAreGone_MustBeRemovedFromTheList()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var stale = FrozenOverBudgetSources
            .Where(relative =>
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                return !File.Exists(path) || File.ReadLines(path).Take(CeilingLines + 1).Count() <= CeilingLines;
            })
            .ToList();

        stale.Should().BeEmpty(
            "a fixed or deleted file leaves the frozen list — keeping it would let the list "
            + "describe history instead of code (N270/F13): {0}",
            string.Join(" | ", stale));
    }

    /// <summary>Population guard: the enumeration must actually see sources — a moved root reads nothing.</summary>
    [Fact]
    public void TheSweepReadsAMeaningfulSourcePopulation()
    {
        EnumerateSourceFiles().Count().Should().BeGreaterThan(
            1000,
            "the repo holds thousands of .cs/.razor files; a fraction of that means the probe reads the wrong root");
    }

    private static IEnumerable<string> EnumerateOverBudgetSources()
    {
        foreach (var relative in EnumerateSourceFiles())
        {
            var path = Path.Combine(
                ThemeCss.RepositoryRoot().FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.ReadLines(path).Take(CeilingLines + 1).Count() > CeilingLines)
            {
                yield return relative;
            }
        }
    }

    /// <summary>
    /// Every <c>*.cs</c>/<c>*.razor</c> under the repository root, repo-relative with '/' separators,
    /// minus build outputs and the <c>src/Tempo.Blazor.Demo*</c> demo-app trees.
    /// </summary>
    private static IEnumerable<string> EnumerateSourceFiles()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.StartsWith("obj/", StringComparison.Ordinal)
                || relative.StartsWith("bin/", StringComparison.Ordinal)
                || relative.StartsWith(".git/", StringComparison.Ordinal)
                || relative.StartsWith("src/Tempo.Blazor.Demo", StringComparison.Ordinal))
            {
                continue;
            }

            if (relative.EndsWith(".cs", StringComparison.Ordinal)
                || relative.EndsWith(".razor", StringComparison.Ordinal))
            {
                yield return relative;
            }
        }
    }
}
