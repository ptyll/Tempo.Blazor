using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Browser-free coverage of the E2E boot-readiness infrastructure (Fáze 1 of the follow-up plan):
/// the configurable readiness budget (<c>TM_E2E_READY_TIMEOUT_MS</c>), the ONE controlled reload on a
/// cold boot, and the guard that keeps that recovery in <see cref="PlaywrightTestBase"/> instead of
/// being copy-pasted into individual specs again. No Playwright browser and no demo host is needed, so
/// these run in the unit lane of the E2E assembly in milliseconds.
/// </summary>
[TestClass]
public class ReadinessInfrastructureTests
{
    // ── configurable readiness budget ───────────────────────────────────────────────────────

    [TestMethod]
    public void ReadyTimeout_DefaultsToMoreThanTheFormerSixtySeconds()
    {
        var timeouts = ReadinessTimeouts.Resolve(null);

        Assert.IsTrue(timeouts.ReadyMs > 60_000,
            $"the authoritative data-blazor-ready gate must be longer than the former 60 s, was {timeouts.ReadyMs} ms");
        Assert.IsTrue(timeouts.DomContentLoadedMs > 60_000,
            $"the DOMContentLoaded wait must be longer than the former 60 s, was {timeouts.DomContentLoadedMs} ms");
        Assert.IsTrue(timeouts.SelectorMs > 15_000,
            $"the app-shell selector wait must be longer than the former 15 s, was {timeouts.SelectorMs} ms");
        Assert.IsTrue(timeouts.FallbackMs > 45_000,
            $"the text-content fallback wait must be longer than the former 45 s, was {timeouts.FallbackMs} ms");
    }

    [TestMethod]
    public void ReadyTimeout_EnvironmentOverride_ScalesEveryPhase()
    {
        var timeouts = ReadinessTimeouts.Resolve("200000");

        Assert.AreEqual(200_000, timeouts.ReadyMs, "TM_E2E_READY_TIMEOUT_MS sets the data-blazor-ready cap");
        Assert.IsTrue(timeouts.DomContentLoadedMs >= 200_000, "the DOMContentLoaded wait follows the override");
        Assert.IsTrue(timeouts.SelectorMs > ReadinessTimeouts.Resolve(null).SelectorMs,
            "the shell selector wait scales with the override");
        Assert.IsTrue(timeouts.SelectorMs < timeouts.ReadyMs && timeouts.FallbackMs < timeouts.ReadyMs,
            "the pre-gates stay inside the authoritative gate budget");
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("abc")]
    [DataRow("0")]
    [DataRow("-5")]
    [DataRow("999999999999")]
    public void ReadyTimeout_InvalidOverride_FallsBackToTheDefault(string raw)
    {
        Assert.AreEqual(ReadinessTimeouts.Resolve(null), ReadinessTimeouts.Resolve(raw),
            $"an unusable TM_E2E_READY_TIMEOUT_MS ('{raw}') must not disable or zero the readiness gate");
    }

    // ── one controlled reload on a cold boot ────────────────────────────────────────────────

    [TestMethod]
    public async Task ColdBoot_ReadyOnTheFirstAttempt_NeverReloads()
    {
        int ready = 0, reloads = 0;

        await ColdBootRecovery.RunAsync(
            () => { ready++; return Task.CompletedTask; },
            () => { reloads++; return Task.CompletedTask; });

        Assert.AreEqual(1, ready);
        Assert.AreEqual(0, reloads, "a healthy boot must not pay a reload");
    }

    [TestMethod]
    public async Task ColdBoot_FirstAttemptTimesOut_ReloadsExactlyOnceAndSucceeds()
    {
        int ready = 0, reloads = 0;

        await ColdBootRecovery.RunAsync(
            () =>
            {
                ready++;
                return ready == 1 ? Task.FromException(new TimeoutException("cold boot")) : Task.CompletedTask;
            },
            () => { reloads++; return Task.CompletedTask; });

        Assert.AreEqual(2, ready, "the readiness gate runs again after the reload");
        Assert.AreEqual(1, reloads, "exactly one controlled reload");
    }

    [TestMethod]
    public async Task ColdBoot_TimesOutTwice_PropagatesAndDoesNotReloadAgain()
    {
        int ready = 0, reloads = 0;

        await Assert.ThrowsExceptionAsync<TimeoutException>(() => ColdBootRecovery.RunAsync(
            () => { ready++; return Task.FromException(new TimeoutException("still booting")); },
            () => { reloads++; return Task.CompletedTask; }));

        Assert.AreEqual(2, ready);
        Assert.AreEqual(1, reloads, "a second timeout is a real failure, not another reload");
    }

    [TestMethod]
    public async Task ColdBoot_NonTimeoutFailure_IsNotRetried()
    {
        int ready = 0, reloads = 0;

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => ColdBootRecovery.RunAsync(
            () => { ready++; return Task.FromException(new InvalidOperationException("assertion-like failure")); },
            () => { reloads++; return Task.CompletedTask; }));

        Assert.AreEqual(1, ready);
        Assert.AreEqual(0, reloads, "only a readiness TIMEOUT is a cold boot; anything else must surface immediately");
    }

    [TestMethod]
    public async Task ColdBoot_PlaywrightTimeout_CountsAsColdBoot()
    {
        int reloads = 0;
        int ready = 0;

        await ColdBootRecovery.RunAsync(
            () =>
            {
                ready++;
                return ready == 1
                    ? Task.FromException(new Microsoft.Playwright.PlaywrightException("Timeout 60000ms exceeded."))
                    : Task.CompletedTask;
            },
            () => { reloads++; return Task.CompletedTask; });

        Assert.AreEqual(1, reloads, "Playwright reports wait timeouts as PlaywrightException('Timeout …ms exceeded')");
    }

    // ── recovery lives in the base, not in the specs ────────────────────────────────────────

    // Matches the hand-rolled cold-boot recoveries that used to be copy-pasted into individual specs (Email F8/F9/F10,
    // Wireframe, Audit log, charts …). The retry belongs to PlaywrightTestBase.GotoAppReadyAsync. Two shapes, checked
    // only in files that call WaitForAppReadyAsync:
    //   1. catch (TimeoutException | PlaywrightException | Exception …) { … ReloadAsync }   (reload-in-catch)
    //   2. catch (TimeoutException | PlaywrightException | Exception …) when (… attempt …)  (attempt-loop retry)
    private const string CaughtType = @"(?:System\.|Microsoft\.Playwright\.)?(?:Timeout|Playwright)?Exception";

    private static readonly Regex LocalRecovery = new(
        @"catch\s*\(\s*" + CaughtType + @"\b[^)]*\)[^{]*\{[^}]*?ReloadAsync"
        + @"|catch\s*\(\s*" + CaughtType + @"\b[^)]*\)\s*when\s*\([^)]*\battempt\b",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [TestMethod]
    public void NoSpecKeepsALocalColdBootRecoveryCopy()
    {
        var root = FindE2EDirectory();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || string.Equals(Path.GetFileName(file), "ReadinessInfrastructureTests.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (!text.Contains("WaitForAppReadyAsync", StringComparison.Ordinal)) continue;
            if (LocalRecovery.IsMatch(text)) offenders.Add(relative);
        }

        Assert.AreEqual(0, offenders.Count,
            "cold-boot recovery must be centralized in PlaywrightTestBase.GotoAppReadyAsync; local copies in: "
            + string.Join(", ", offenders));
    }

    // ── a spec must open its contexts and boot its pages through the central helpers ───────
    //
    // 2026-11-20 (targeted final run): Scheduler_Touch_SwipeFromEventCentre_Scrolls_AndDoesNotResize (768) sat 120 s in
    // WaitForAppReadyAsync because the WASM app never rendered `main` (a stalled/aborted boot fetch leaves the Blazor
    // loader on "Loading components..." for good; ONE reload boots in ~5 s). PointerDragE2ETests built its context with
    // Browser.NewContextAsync and its page with GotoAsync + WaitForAppReadyAsync, so it had neither the controlled
    // reload of GotoAppReadyAsync/ReloadAppReadyAsync nor the failure trace of CreateContextAsync. The same shape is in
    // ~370 other files, so the guard is general and ratcheted: a context comes from PlaywrightTestBase.CreateContextAsync,
    // a navigation from GotoAppReadyAsync / ReloadAppReadyAsync. Existing offenders are listed below with their count;
    // converting one LOWERS its count (update the entry, delete it at zero), a new offender - or a growing count - fails.

    private const string Debt = "debt: hand-rolled context/boot, convert to CreateContextAsync + GotoAppReadyAsync";
    private const string FirefoxLeg = "permanent: the shared browser is Chromium, this Firefox leg launches its own Playwright + browser";
    private const string OwnHost = "permanent: starts its own dotnet host (not the shared demo hosts), so it has no shared browser or boot recovery";

    private static readonly (string Id, Regex Pattern, string Hint)[] BootRules =
    [
        ("R1 NewContextAsync", new Regex(@"\.NewContextAsync\(", RegexOptions.Compiled),
            "create the context with PlaywrightTestBase.CreateContextAsync (trace, bounded relaunch, cleanup registration)"),
        ("R2 Goto/Reload + WaitForAppReadyAsync", new Regex(@"\.(?:Goto|Reload)Async\([^;]*\);\s*await\s+WaitForAppReadyAsync\(", RegexOptions.Compiled | RegexOptions.Singleline),
            "navigate with GotoAppReadyAsync / ReloadAppReadyAsync (the ONE controlled cold-boot reload)"),
        ("R3 WaitForFunction(data-blazor-ready)", new Regex(@"WaitForFunctionAsync\(\s*""[^""]*data-blazor-ready", RegexOptions.Compiled | RegexOptions.Singleline),
            "a hand-written data-blazor-ready wait skips the recovery; use GotoAppReadyAsync / ReloadAppReadyAsync"),
        ("R4 RegisterContext", new Regex(@"\bRegisterContext\(", RegexOptions.Compiled),
            "a context that needs RegisterContext was not made by CreateContextAsync"),
    ];

    /// <summary>
    /// The ratchet: per file (relative to the E2E project, forward slashes) the number of rule hits tolerated today and
    /// why. Computed by running <see cref="BootRules"/> over every comment-stripped <c>*.cs</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (int Count, string Reason)> KnownOffenders =
        new Dictionary<string, (int Count, string Reason)>(StringComparer.Ordinal)
        {
        ["Accessibility/ComponentAccessibilityE2ETests.cs"] = (1, Debt),
        ["Accessibility/ContrastAuditE2ETests.cs"] = (3, Debt),
        ["Accessibility/FocusTrapActivationE2ETests.cs"] = (2, Debt),
        ["Accessibility/SchedulerAccessibilityE2ETests.cs"] = (1, Debt),
        ["ActivityCommentsActionsE2ETests.cs"] = (3, Debt),
        ["ActivityCommentsComposerE2ETests.cs"] = (3, Debt),
        ["ActivityCommentsDraftE2ETests.cs"] = (1, Debt),
        ["ActivityCommentsFailureE2ETests.cs"] = (3, Debt),
        ["ActivityCommentsVisualE2ETests.cs"] = (3, Debt),
        ["AdditiveExtensionsE2ETests.cs"] = (3, Debt),
        ["AgentPlanE2ETests.cs"] = (2, Debt),
        ["AgentRunE2ETests.cs"] = (2, Debt),
        ["AiAgentsE2EBase.cs"] = (2, Debt),
        ["AiAgentsE2ETests.Visual.cs"] = (2, Debt),
        ["AiAgentVisualizationsPageBase.cs"] = (2, Debt),
        ["AnalyticsKitE2ETests.cs"] = (2, Debt),
        ["AppShellE2ETests.cs"] = (3, Debt),
        ["AriaWiringE2ETests.cs"] = (2, Debt),
        ["CalendarRangeFocusRingE2ETests.cs"] = (1, Debt),
        ["ColorGradientE2ETests.cs"] = (3, Debt),
        ["CommandPaletteE2ETests.cs"] = (2, Debt),
        ["ConditionBuilderE2ETests.cs"] = (3, Debt),
        ["ContextUsageE2ETests.cs"] = (2, Debt),
        ["Conventions/TestIdPrefixE2ETests.cs"] = (1, Debt),
        ["DashboardF2ScreenshotTests.cs"] = (1, Debt),
        ["DashboardF3ResponsiveE2ETests.cs"] = (4, Debt),
        ["DashboardF4ReorderE2ETests.cs"] = (3, Debt),
        ["DashboardF5PointerDragE2ETests.cs"] = (3, Debt),
        ["DashboardF6SelectorE2ETests.cs"] = (3, Debt),
        ["DashboardF7DialogsE2ETests.cs"] = (3, Debt),
        ["DashboardTG009ChromelessE2ETests.cs"] = (1, Debt),
        ["DataDisplay/TmMultiViewListStretchedLinkE2ETests.cs"] = (1, Debt),
        ["DataDisplayKpiE2ETests.cs"] = (2, Debt),
        ["DataDisplayTrendBreakdownE2ETests.cs"] = (2, Debt),
        ["DataTableColumnResizeE2ETests.cs"] = (3, Debt),
        ["DataTableE2EKit.cs"] = (2, Debt),
        ["DataTableErgonomicsE2ETests.cs"] = (1, Debt),
        ["DataTableRedesignDemoE2ETests.cs"] = (5, Debt),
        ["DebtTokenBaselineScreenshots.cs"] = (1, Debt),
        ["DecimalInputE2ETests.cs"] = (1, Debt),
        ["DemoCrossLinksPhase9E2ETests.cs"] = (5, Debt),
        ["DensityTG007E2ETests.cs"] = (1, Debt),
        ["Diagram/TmDiagramThemeE2ETests.cs"] = (3, Debt),
        ["DiagramArchimate3Phase7E2ETests.cs"] = (1, Debt),
        ["DiagramBaselineScreenshots.cs"] = (1, Debt),
        ["DiagramBpmn2Phase6E2ETests.cs"] = (1, Debt),
        ["DiagramEdgeE2ETests.cs"] = (4, Debt),
        ["DiagramEdgeStencilPhase4E2ETests.cs"] = (1, Debt),
        ["DiagramExtendedStencilPhase8E2ETests.cs"] = (1, Debt),
        ["DiagramF3E2ETests.cs"] = (1, Debt),
        ["DiagramF4E2ETests.cs"] = (1, Debt),
        ["DiagramF5E2ETests.cs"] = (1, Debt),
        ["DiagramF6E2ETests.cs"] = (1, Debt),
        ["DiagramInsertE2ETests.cs"] = (1, Debt),
        ["DiagramMobileTouchE2ETests.cs"] = (6, Debt),
        ["DiagramPhase12TemplateGalleryE2ETests.cs"] = (1, Debt),
        ["DiagramPhase2ImportDialogsE2ETests.cs"] = (1, Debt),
        ["DiagramResponsiveScreenshotTests.cs"] = (3, Debt),
        ["DiagramTabletTouchE2ETests.cs"] = (6, Debt),
        ["DiagramToolboxPhase3E2ETests.cs"] = (1, Debt),
        ["DiagramTransformE2ETests.cs"] = (1, Debt),
        ["DiagramUml25Phase5E2ETests.cs"] = (2, Debt),
        ["DiagramWorkflowPhase11E2ETests.cs"] = (1, Debt),
        ["DocumentEditorCanvasAccessibilityE2ETests.cs"] = (3, Debt),
        ["DocumentEditorCanvasRenderE2ETests.cs"] = (1, Debt),
        ["DocumentEditorCanvasShapesDrawingsE2ETests.cs"] = (1, Debt),
        ["DocumentEditorCanvasUxGalleryE2ETests.cs"] = (1, Debt),
        ["DocumentEditorHeadlessExportParityE2ETests.cs"] = (1, Debt),
        ["DocumentEditorPdfExportE2ETests.cs"] = (1, Debt),
        ["DocumentEditorSigningBridgeE2ETests.cs"] = (3, Debt),
        ["DocumentLibraryE2ETests.cs"] = (1, Debt),
        ["DocumentLibraryMobileE2ETests.cs"] = (3, Debt),
        ["DocumentLibraryNotionE2ETests.cs"] = (3, Debt),
        ["DocumentLibraryRedesignE2ETests.cs"] = (3, Debt),
        ["DocumentLibraryScopeE2ETests.cs"] = (3, Debt),
        ["DrawerPositionE2ETests.cs"] = (2, Debt),
        ["DropdownDialogE2ETests.cs"] = (1, Debt),
        ["EditorShellE2ETests.cs"] = (43, Debt),
        ["EditorShellE2ETests.Resizer.cs"] = (6, Debt),
        ["EditorShellE2ETests.SheetGesture.cs"] = (2, Debt),
        ["EditorShellPhase15E2ETests.cs"] = (7, Debt),
        ["EditorShellRailE2ETests.cs"] = (3, Debt),
        ["EditorShellTabletOverlayE2ETests.cs"] = (3, Debt),
        ["EmailEditorCanvasReviewFixE2ETests.cs"] = (5, Debt),
        ["EmailEditorCanvasWysiwygF9AE2ETests.cs"] = (2, Debt),
        ["EmailEditorImportExportF10E2ETests.cs"] = (2, Debt),
        ["EmailEditorItemDialogF9E2ETests.cs"] = (2, Debt),
        ["EmailEditorKeystrokePerformanceE2ETests.cs"] = (2, Debt),
        ["EmailEditorResponsiveF8E2ETests.cs"] = (2, Debt),
        ["EmailEditorUxPhase8E2ETests.cs"] = (2, Debt),
        ["EmailTemplateF11DemoE2ETests.cs"] = (1, Debt),
        ["EmailTemplateReferenceShotsE2ETests.cs"] = (2, Debt),
        ["EmailTemplateRenderModesE2ETests.cs"] = (2, Debt),
        ["EmailTemplatesE2ETests.cs"] = (1, Debt),
        ["FacetFilterE2ETests.cs"] = (2, Debt),
        ["Feedback/BottomSheetE2ETests.cs"] = (3, Debt),
        ["Feedback/DialogF2DebtE2ETests.cs"] = (3, Debt),
        ["Feedback/ModalFocusRestoreE2ETests.cs"] = (3, Debt),
        ["Feedback/ModalStackedInOrderE2ETests.cs"] = (3, Debt),
        ["Feedback/OverlayInitialFocusE2ETests.cs"] = (4, Debt),
        ["Feedback/OverlayRound5E2ETests.cs"] = (3, Debt),
        ["Feedback/OverlayRound6E2ETests.cs"] = (3, Debt),
        ["Feedback/SheetReviewE2ETests.cs"] = (3, Debt),
        ["Feedback/SheetRound2E2ETests.cs"] = (3, Debt),
        ["Feedback/SheetRound3E2ETests.cs"] = (6, Debt),
        ["Feedback/SheetRound4E2ETests.cs"] = (3, Debt),
        ["Files/FileManagerK4E2ETests.cs"] = (1, Debt),
        ["FilterableDropdownE2ETests.cs"] = (3, FirefoxLeg),
        ["FocusTokenDerivationE2ETests.cs"] = (1, Debt),
        ["FormActionBarResponsiveE2ETests.cs"] = (1, Debt),
        ["FormulaBuilderE2ETests.cs"] = (3, Debt),
        ["Fze12TouchEmulationSweepE2ETests.cs"] = (2, Debt),
        ["GalleryLightboxE2ETests.cs"] = (21, Debt),
        ["GanttCalendarModeE2ETests.cs"] = (1, Debt),
        ["GanttDesktopStructureE2ETests.cs"] = (3, Debt),
        ["GanttDialogOverlayE2ETests.cs"] = (3, Debt),
        ["GanttInteractionE2ETests.cs"] = (7, Debt),
        ["GanttPhase10DataE2ETests.cs"] = (3, Debt),
        ["GanttPhase11SummaryE2ETests.cs"] = (3, Debt),
        ["GanttPhase1E2ETests.cs"] = (1, Debt),
        ["GanttPhase5E2ETests.cs"] = (3, Debt),
        ["GanttPhase6DialogsE2ETests.cs"] = (3, Debt),
        ["GanttPhase7TabletE2ETests.cs"] = (3, Debt),
        ["GanttPhase8MobileE2ETests.cs"] = (3, Debt),
        ["GanttTaskPanelE2ETests.cs"] = (6, Debt),
        ["GanttThemeE2ETests.cs"] = (3, Debt),
        ["GanttVirtualizationE2ETests.cs"] = (3, Debt),
        ["GanttVisualBaselineE2ETests.cs"] = (3, Debt),
        ["Charts/TmCalendarHeatmapE2ETests.cs"] = (1, Debt),
        ["Charts/TmChartColorsE2ETests.cs"] = (1, Debt),
        ["Charts/TmChartFinalVisualE2ETests.cs"] = (1, Debt),
        ["Charts/TmChartIndexTooltipE2ETests.cs"] = (2, Debt),
        ["Charts/TmChartResponsiveE2ETests.cs"] = (1, Debt),
        ["Charts/TmChartStackedBarE2ETests.cs"] = (1, Debt),
        ["Charts/TmChartThemeE2ETests.cs"] = (3, Debt),
        ["Charts/TmChartTimeAxisE2ETests.cs"] = (1, Debt),
        ["Charts/TmSankeyChartE2ETests.cs"] = (1, Debt),
        ["Chat/ChatA11yE2ETests.cs"] = (2, Debt),
        ["Chat/ChatAutoAttachServerE2ETests.cs"] = (3, Debt),
        ["Chat/ChatCollaborationE2ETests.cs"] = (1, Debt),
        ["Chat/ChatCustomTemplatesE2ETests.cs"] = (6, Debt),
        ["Chat/ChatLayoutMobileE2ETests.cs"] = (6, Debt),
        ["Chat/ChatScrollE2ETests.cs"] = (5, Debt),
        ["Chat/ChatTypedE2ETests.cs"] = (3, Debt),
        ["K10/K10FeaturesE2ETests.cs"] = (1, Debt),
        ["KanbanCollapsedDropE2ETests.cs"] = (1, Debt),
        ["KanbanCollapsedLayoutE2ETests.cs"] = (1, Debt),
        ["KanbanCollapseE2ETests.cs"] = (1, Debt),
        ["KanbanDropZonesE2ETests.cs"] = (1, Debt),
        ["KanbanMoveE2ETests.cs"] = (1, Debt),
        ["KanbanReorderE2ETests.cs"] = (1, Debt),
        ["KanbanSwimlaneE2ETests.cs"] = (1, Debt),
        ["KanbanTouchDragE2ETests.cs"] = (1, Debt),
        ["KanbanVisualE2ETests.cs"] = (1, Debt),
        ["LayoutChromeE2ETests.cs"] = (3, Debt),
        ["Localization/CzechUiE2ETests.cs"] = (5, Debt),
        ["Localization/LocalizationPluralsE2ETests.cs"] = (3, Debt),
        ["MapsE2ETests.cs"] = (3, Debt),
        ["Mcp/NotionAuthoringContractE2ETests.cs"] = (2, Debt),
        ["McpAgentScreenshotE2ETests.cs"] = (1, Debt),
        ["MobileActionBarE2ETests.cs"] = (35, Debt),
        ["MobilePresentationOptInE2ETests.cs"] = (3, Debt),
        ["ModelingAccessibilityM25E2ETests.cs"] = (3, Debt),
        ["ModelingArchimate32ProfileM19E2ETests.cs"] = (1, Debt),
        ["ModelingArchimateRelationshipsM20E2ETests.cs"] = (2, Debt),
        ["ModelingArchimateViewpointsM21E2ETests.cs"] = (1, Debt),
        ["ModelingAuthoringPhase3E2ETests.cs"] = (3, Debt),
        ["ModelingBpmnDiRenderingE2ETests.cs"] = (3, Debt),
        ["ModelingBpmnProfileM17E2ETests.cs"] = (1, Debt),
        ["ModelingBpmnServerE2ETests.cs"] = (1, Debt),
        ["ModelingBpmnXmlConverterE2ETests.cs"] = (1, Debt),
        ["ModelingDemoPageM23E2ETests.cs"] = (7, Debt),
        ["ModelingDiagramPreviewM12E2ETests.cs"] = (4, Debt),
        ["ModelingDragDropM16E2ETests.cs"] = (1, Debt),
        ["ModelingEditingF14E2ETests.cs"] = (3, Debt),
        ["ModelingEditorM8E2ETests.cs"] = (1, Debt),
        ["ModelingErdProfileM22E2ETests.cs"] = (1, Debt),
        ["ModelingFinalReviewE2ETests.cs"] = (2, Debt),
        ["ModelingInspectorM14E2ETests.cs"] = (1, Debt),
        ["ModelingIssueFilterPhase11E2ETests.cs"] = (3, Debt),
        ["ModelingIssuePanelM13E2ETests.cs"] = (1, Debt),
        ["ModelingIssuesDockPhase5E2ETests.cs"] = (1, Debt),
        ["ModelingLayoutPhase4E2ETests.cs"] = (3, Debt),
        ["ModelingLocalizationM15E2ETests.cs"] = (1, Debt),
        ["ModelingMobilePhase7E2ETests.cs"] = (3, Debt),
        ["ModelingModelTreeM10E2ETests.cs"] = (1, Debt),
        ["ModelingOpenInEditorDialogE2ETests.cs"] = (1, Debt),
        ["ModelingPerformanceM26E2ETests.cs"] = (1, Debt),
        ["ModelingPhase15BaselineScreenshots.cs"] = (2, Debt),
        ["ModelingPhase5ReviewE2ETests.cs"] = (1, Debt),
        ["ModelingPropertySchemaPhase5E2ETests.cs"] = (3, Debt),
        ["ModelingPropertySchemaPhase5Screenshots.cs"] = (2, Debt),
        ["ModelingProviderSelectorE2ETests.cs"] = (2, Debt),
        ["ModelingRedesignBaselineScreenshots.cs"] = (6, Debt),
        ["ModelingRegressionM24E2ETests.cs"] = (1, Debt),
        ["ModelingRelatedElementsE2ETests.cs"] = (2, Debt),
        ["ModelingTabletTouchE2ETests.cs"] = (2, Debt),
        ["ModelingTreeCollapseE2ETests.cs"] = (1, Debt),
        ["ModelingTreeHierarchyPhase12E2ETests.cs"] = (1, Debt),
        ["ModelingUmlProfileM18E2ETests.cs"] = (1, Debt),
        ["ModelingViewerPhase6E2ETests.cs"] = (3, Debt),
        ["ModelingViewerPhase6Screenshots.cs"] = (3, Debt),
        ["ModelingViewSelectorM11E2ETests.cs"] = (1, Debt),
        ["ModelPickerE2ETests.cs"] = (2, Debt),
        ["MultiColumnComboBoxE2ETests.cs"] = (1, Debt),
        ["NotificationBellKeyboardE2ETests.cs"] = (1, Debt),
        ["Notifications/NotificationsE2ETests.cs"] = (1, Debt),
        ["NotionActionItemsE2ETests.cs"] = (1, Debt),
        ["NotionAnalyticsE2ETests.cs"] = (1, Debt),
        ["NotionBlockEditingE2ETests.cs"] = (1, Debt),
        ["NotionBlockOpsE2ETests.cs"] = (1, Debt),
        ["NotionClipboardPasteE2ETests.cs"] = (1, Debt),
        ["NotionCommentDialogsE2ETests.cs"] = (3, Debt),
        ["NotionCommentsE2ETests.cs"] = (1, Debt),
        ["NotionDatabaseAdvancedE2ETests.cs"] = (1, Debt),
        ["NotionDatabaseBasicE2ETests.cs"] = (1, Debt),
        ["NotionDatabasePhase8E2ETests.cs"] = (1, Debt),
        ["NotionDatabasePhase9E2ETests.cs"] = (1, Debt),
        ["NotionDbMobileSheetE2ETests.cs"] = (3, Debt),
        ["NotionDragDropE2ETests.cs"] = (1, Debt),
        ["NotionDragDropMediaE2ETests.cs"] = (1, Debt),
        ["NotionE2ETestBase.cs"] = (2, Debt),
        ["NotionEditPersistenceE2ETests.cs"] = (3, Debt),
        ["NotionFormattingE2ETests.cs"] = (2, Debt),
        ["NotionKeyboardE2ETests.cs"] = (1, Debt),
        ["NotionLayoutE2ETests.cs"] = (1, Debt),
        ["NotionMarkdownShortcutsE2ETests.cs"] = (2, Debt),
        ["NotionMediaBlocksE2ETests.cs"] = (1, Debt),
        ["NotionMentionMenuE2ETests.cs"] = (1, Debt),
        ["NotionMyTasksE2ETests.cs"] = (1, Debt),
        ["NotionNavBlocksE2ETests.cs"] = (1, Debt),
        ["NotionNotificationCenterE2ETests.cs"] = (2, Debt),
        ["NotionOverlayInModalE2ETests.cs"] = (2, FirefoxLeg),
        ["NotionPageHistoryE2ETests.cs"] = (1, Debt),
        ["NotionPageSettingsE2ETests.cs"] = (3, Debt),
        ["NotionPhase1EvidenceScreenshots.cs"] = (7, Debt),
        ["NotionPhase2ThemeScreenshots.cs"] = (1, Debt),
        ["NotionPhase3ShellScreenshots.cs"] = (2, Debt),
        ["NotionPhase4DialogScreenshots.cs"] = (3, Debt),
        ["NotionPhase5PagePanelsE2ETests.cs"] = (2, Debt),
        ["NotionPhase6MenusE2ETests.cs"] = (1, Debt),
        ["NotionPhase7SurfacesE2ETests.cs"] = (1, Debt),
        ["NotionPublicShareE2ETests.cs"] = (3, Debt),
        ["NotionResponsiveShellE2ETests.cs"] = (2, Debt),
        ["NotionRestrictionsE2ETests.cs"] = (1, Debt),
        ["NotionSidebarE2ETests.cs"] = (1, Debt),
        ["NotionSinglePageModeE2ETests.cs"] = (1, Debt),
        ["NotionSlashMenuE2ETests.cs"] = (2, Debt),
        ["NotionSpecialBlocksE2ETests.cs"] = (1, Debt),
        ["NotionStructuralOperationsE2ETests.cs"] = (1, Debt),
        ["NotionTableE2ETests.cs"] = (2, Debt),
        ["NotionTempoBlocksE2ETests.cs"] = (1, Debt),
        ["NotionTokenDropdownE2ETests.cs"] = (2, Debt),
        ["NotionWatchNotificationsE2ETests.cs"] = (1, Debt),
        ["NumberInputDarkModeE2ETests.cs"] = (1, Debt),
        ["OtpInputE2ETests.cs"] = (4, Debt),
        ["OverlayMobilePresentationE2ETests.cs"] = (3, Debt),
        ["OverlayPanelE2ETests.cs"] = (4, FirefoxLeg),
        ["OverlayRound1FixE2ETests.cs"] = (3, Debt),
        ["OverlayRound2FixE2ETests.cs"] = (3, Debt),
        ["OverlayRound3FixE2ETests.cs"] = (3, Debt),
        ["OverlayTopLayerRenderModesE2ETests.cs"] = (6, Debt),
        ["PdfAnnotatorE2ETests.cs"] = (2, Debt),
        ["PdfAnnotatorResponsiveE2ETests.cs"] = (5, Debt),
        ["PdfAnnotatorUndoE2ETests.cs"] = (1, Debt),
        ["PdfRedesignThemeScreenshotTests.cs"] = (1, Debt),
        ["PdfTemplateDesignerE2ETests.cs"] = (4, Debt),
        ["PdfViewerAnnotationsE2ETests.cs"] = (4, Debt),
        ["PdfViewerContinuousE2ETests.cs"] = (1, Debt),
        ["PdfViewerResponsiveE2ETests.cs"] = (3, Debt),
        ["PdfViewerTextLayerRotationE2ETests.cs"] = (1, Debt),
        ["PdfViewerToolbarE2ETests.cs"] = (2, Debt),
        ["PdfViewerTouchE2ETests.cs"] = (3, Debt),
        ["PinOnTopE2ETests.cs"] = (2, Debt),
        ["QueryInputE2ETests.cs"] = (1, Debt),
        ["RangeSliderE2ETests.cs"] = (3, Debt),
        ["RecipientRoleEditorE2ETests.cs"] = (5, Debt),
        ["RelativeTimeE2ETests.cs"] = (3, Debt),
        ["ReportingF0FidelityE2ETests.cs"] = (1, OwnHost),
        ["ReportingF12ReportServerWebE2ETests.cs"] = (1, OwnHost),
        ["ReportingF13ReportDesignerE2ETests.cs"] = (1, OwnHost),
        ["ReportingF14ChartsE2ETests.cs"] = (1, OwnHost),
        ["ReportingF15ExportE2ETests.cs"] = (1, OwnHost),
        ["ReportingF16SchedulingE2ETests.cs"] = (1, OwnHost),
        ["ReportingF17EmbeddingE2ETests.cs"] = (1, OwnHost),
        ["ReportingF5TextLayoutE2ETests.cs"] = (1, OwnHost),
        ["ReportingF6InvoicePaginationE2ETests.cs"] = (1, OwnHost),
        ["ReportingF7TablixE2ETests.cs"] = (1, OwnHost),
        ["ReportingF8PdfRendererE2ETests.cs"] = (1, OwnHost),
        ["ReportingF9ReportViewerE2ETests.cs"] = (1, OwnHost),
        ["ResponsiveConventionsE2ETests.cs"] = (3, Debt),
        ["RteDialogsTopLayerE2ETests.cs"] = (3, Debt),
        ["SegmentedControlE2ETests.cs"] = (2, Debt),
        ["Scheduler/SchedulerNavigationE2ETests.cs"] = (1, Debt),
        ["Scheduler/SchedulerPhase5ReviewE2ETests.cs"] = (1, Debt),
        ["Scheduler/SchedulerRangeE2ETests.cs"] = (1, Debt),
        ["Scheduler/SchedulerResponsiveE2ETests.cs"] = (3, Debt),
        ["Scheduler/SchedulerSidePanelE2ETests.cs"] = (1, Debt),
        ["Scheduler/SchedulerTimezoneE2ETests.cs"] = (1, Debt),
        ["Scheduler/SchedulerVisualE2ETests.cs"] = (2, Debt),
        ["Scheduler/TmSchedulerMonthTemplateE2ETests.cs"] = (1, Debt),
        ["SidePanelSheetHeight10dE2ETests.cs"] = (2, Debt),
        ["SignatureCaptureE2ETests.cs"] = (6, Debt),
        ["SigningContainerQueryE2ETests.cs"] = (3, Debt),
        ["SigningDocumentCommentsE2ETests.cs"] = (9, Debt),
        ["SigningDocumentPageViewerE2ETests.cs"] = (2, Debt),
        ["SigningFieldEditorPanelE2ETests.cs"] = (8, Debt),
        ["SigningFieldOverlayE2ETests.cs"] = (4, Debt),
        ["SigningFormRunnerE2ETests.cs"] = (1, Debt),
        ["SigningLocalizationE2ETests.cs"] = (1, Debt),
        ["SigningProductPanelsE2ETests.cs"] = (1, Debt),
        ["SigningQualityE2ETests.cs"] = (1, Debt),
        ["SigningRedesignBaselineE2ETests.cs"] = (7, Debt),
        ["SigningRedesignPhase10E2ETests.cs"] = (1, Debt),
        ["SigningRedesignPhase2E2ETests.cs"] = (3, Debt),
        ["SigningRedesignPhase3E2ETests.cs"] = (6, Debt),
        ["SigningRedesignPhase4E2ETests.cs"] = (4, Debt),
        ["SigningRedesignPhase5E2ETests.cs"] = (1, Debt),
        ["SigningRedesignPhase6E2ETests.cs"] = (1, Debt),
        ["SigningRedesignPhase7E2ETests.cs"] = (1, Debt),
        ["SigningRedesignPhase8E2ETests.cs"] = (1, Debt),
        ["SigningRedesignPhase9E2ETests.cs"] = (1, Debt),
        ["SigningStepsE2ETests.cs"] = (1, Debt),
        ["SmallGaps10cE2ETests.cs"] = (2, Debt),
        ["SmokeLaneE2ETests.cs"] = (1, Debt),
        ["SortableListE2ETests.cs"] = (2, Debt),
        ["SplitterE2ETests.cs"] = (3, Debt),
        ["SpreadsheetBlockE2ETests.cs"] = (1, Debt),
        ["SpreadsheetE2ETests.cs"] = (119, Debt),
        ["SpreadsheetPhase2E2ETests.cs"] = (1, Debt),
        ["SpreadsheetPhase2VisualE2ETests.cs"] = (2, Debt),
        ["SpreadsheetPhase3E2ETests.cs"] = (1, Debt),
        ["SpreadsheetPhase4E2ETests.cs"] = (1, Debt),
        ["SpreadsheetPhase5E2ETests.cs"] = (1, Debt),
        ["SpreadsheetPhase6BaselineScreenshots.cs"] = (4, Debt),
        ["SpreadsheetPhase6E2ETests.cs"] = (5, Debt),
        ["SpreadsheetPhase6ResponsiveE2ETests.cs"] = (1, Debt),
        ["ThemeMatrixScreenshotTests.cs"] = (4, Debt),
        ["ThemeTokenBaselineScreenshots.cs"] = (1, Debt),
        ["TmBottomNavigationE2ETests.cs"] = (3, Debt),
        ["TmButtonDataTableScopeE2ETests.cs"] = (1, Debt),
        ["TmColorPaletteE2ETests.cs"] = (1, Debt),
        ["TmDataTableColumnMenuE2ETests.cs"] = (4, Debt),
        ["TmDataTableSortButtonE2ETests.cs"] = (2, FirefoxLeg),
        ["TmDataTableSortButtonVisualE2ETests.cs"] = (2, FirefoxLeg),
        ["TmInputAnnounceErrorE2ETests.cs"] = (2, Debt),
        ["TmModalFilterChipStyleE2ETests.cs"] = (2, Debt),
        ["TmQRCodeE2ETests.cs"] = (3, Debt),
        ["TmSelectNullableE2ETests.cs"] = (3, Debt),
        ["TmTabsHeaderSlotGeometryE2ETests.cs"] = (1, Debt),
        ["TmTabsKeyboardFocusE2ETests.cs"] = (1, Debt),
        ["TmTabsOverflowStickyE2ETests.cs"] = (1, Debt),
        ["TmTopBarNarrowE2ETests.cs"] = (3, Debt),
        ["ToastE2ETests.cs"] = (2, Debt),
        ["ToolbarFileButtonE2ETests.cs"] = (5, Debt),
        ["ToolbarItemsE2ETests.cs"] = (3, Debt),
        ["ToolbarOverflowE2ETests.cs"] = (9, Debt),
        ["TraceWaterfallE2ETests.cs"] = (2, Debt),
        ["TraceWaterfallPageE2ETests.cs"] = (2, Debt),
        ["TreeListGanttRecurrenceE2ETests.cs"] = (10, Debt),
        ["UsageMeterE2ETests.cs"] = (2, Debt),
        ["UserTimeZoneServerE2ETests.cs"] = (3, Debt),
        ["UserTimeZoneSchedulerGanttServerE2ETests.cs"] = (3, Debt),
        ["WireframeEditorE2ETests.cs"] = (1, Debt),
        ["WireframeLayersPanelE2ETests.cs"] = (3, Debt),
        ["WireframeMobileMouseE2ETests.cs"] = (2, Debt),
        ["WireframePhase2ThemeE2ETests.cs"] = (3, Debt),
        ["WireframePhase4DesktopE2ETests.cs"] = (3, Debt),
        ["WireframePhase5TouchE2ETests.cs"] = (2, Debt),
        ["WireframePhase6TabletE2ETests.cs"] = (2, Debt),
        ["WireframePhase6ZoomThemeE2ETests.cs"] = (2, Debt),
        ["WireframePhase7MobileE2ETests.cs"] = (2, Debt),
        ["WireframeResponsiveScreenshotTests.cs"] = (3, Debt),
        ["WireframeStencilGalleryPhase12E2ETests.cs"] = (1, Debt),
        ["WireframeStencilGalleryPhase8E2ETests.cs"] = (1, Debt),
        ["WireframeStencilPackSweepE2ETests.cs"] = (1, Debt),
        ["WorkflowDesignerTouchE2ETests.cs"] = (3, Debt),
        };

    [TestMethod]
    public void NoSpecOpensItsOwnContextOrBootsPastTheCentralRecovery_RatchetedByKnownOffenders()
    {
        var root = FindE2EDirectory();
        var actual = new SortedDictionary<string, (int Count, string Detail)>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("bin/", StringComparison.Ordinal)
                || relative.StartsWith("obj/", StringComparison.Ordinal)
                || relative is "ReadinessInfrastructureTests.cs"
                // PlaywrightTestBase is a partial class split across PlaywrightTestBase.cs and
                // PlaywrightTestBase.<Part>.cs (source-file ceiling); every part is the central harness.
                || Regex.IsMatch(relative, @"^PlaywrightTestBase(\.[A-Za-z]+)?\.cs$"))
            {
                continue;
            }

            var text = StripComments(File.ReadAllText(file));
            var total = 0;
            var rules = new List<string>();
            foreach (var (id, pattern, _) in BootRules)
            {
                var hits = pattern.Matches(text).Count;
                if (hits > 0)
                {
                    total += hits;
                    rules.Add($"{id} x{hits}");
                }
            }

            if (total > 0)
            {
                actual[relative] = (total, string.Join(", ", rules));
            }
        }

        var problems = new List<string>();
        foreach (var (file, (count, detail)) in actual)
        {
            if (!KnownOffenders.TryGetValue(file, out var known))
            {
                problems.Add($"NEW offender {file}: {count} hit(s) [{detail}]. " + string.Join(" | ", BootRules.Select(r => $"{r.Id}: {r.Hint}")));
            }
            else if (count > known.Count)
            {
                problems.Add($"GREW {file}: {known.Count} -> {count} [{detail}]. New code must use the central helpers.");
            }
            else if (count < known.Count)
            {
                problems.Add($"SHRANK {file}: {known.Count} -> {count} [{detail}]. Lower its KnownOffenders entry to {count} (delete it at 0) so the gain cannot regress.");
            }
        }

        foreach (var (file, known) in KnownOffenders)
        {
            if (!actual.ContainsKey(file))
            {
                problems.Add(File.Exists(Path.Combine(root, file))
                    ? $"CONVERTED {file}: no hit left (was {known.Count}). Delete its KnownOffenders entry."
                    : $"GONE {file}: the file no longer exists. Delete its KnownOffenders entry.");
            }
        }

        Assert.AreEqual(0, problems.Count,
            "context creation / boot readiness must go through PlaywrightTestBase (docs/e2e-test-lanes.md \"Boot readiness and cold-boot recovery\"):\n  "
            + string.Join("\n  ", problems));
    }

    /// <summary>
    /// Removes <c>//</c> and <c>/* */</c> comments but leaves string, char, verbatim and raw-string literals intact
    /// (a <c>//</c> inside a URL or a JS snippet is not a comment), so a rule never matches prose in a comment.
    /// </summary>
    internal static string StripComments(string source)
    {
        var sb = new System.Text.StringBuilder(source.Length);
        int i = 0, n = source.Length;
        while (i < n)
        {
            var c = source[i];
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                while (i < n && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    if (source[i] == '\n')
                    {
                        sb.Append('\n');
                    }

                    i++;
                }

                i = Math.Min(n, i + 2);
                sb.Append(' ');
                continue;
            }

            if (c == '"' && i + 2 < n && source[i + 1] == '"' && source[i + 2] == '"')
            {
                var quotes = 0;
                while (i + quotes < n && source[i + quotes] == '"')
                {
                    quotes++;
                }

                var start = i;
                i += quotes;
                var end = source.IndexOf(new string('"', quotes), i, StringComparison.Ordinal);
                end = end < 0 ? n : end + quotes;
                sb.Append(source, start, end - start);
                i = end;
                continue;
            }

            if (c == '"')
            {
                var verbatim = (i > 0 && source[i - 1] == '@') || (i > 1 && source[i - 1] == '$' && source[i - 2] == '@');
                var start = i++;
                while (i < n)
                {
                    if (verbatim)
                    {
                        if (source[i] == '"' && i + 1 < n && source[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }

                        if (source[i] == '"')
                        {
                            i++;
                            break;
                        }
                    }
                    else
                    {
                        if (source[i] == '\\')
                        {
                            i += 2;
                            continue;
                        }

                        if (source[i] == '"' || source[i] == '\n')
                        {
                            i++;
                            break;
                        }
                    }

                    i++;
                }

                i = Math.Min(i, n);
                sb.Append(source, start, i - start);
                continue;
            }

            if (c == '\'')
            {
                var start = i++;
                while (i < n)
                {
                    if (source[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (source[i] == '\'' || source[i] == '\n')
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                i = Math.Min(i, n);
                sb.Append(source, start, i - start);
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }
    private static string FindE2EDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return Path.Combine(current.FullName, "tests", "Tempo.Blazor.E2E");
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from the test output directory.");
    }
}
