namespace Tempo.Blazor.E2E;

/// <summary>13.9 — empty / no results / loading / very long text / 15 columns / 200% zoom / RTL / coarse pointer.</summary>
[TestClass]
public class DataTableEdgeCaseE2ETests : WasmTestBase
{
    [TestMethod, TestCategory("WASM")]
    public Task EmptyNoResultsLoadingLongTextAndFifteenColumns() => DataTableSpecs.EdgeStatesAsync(this);

    [TestMethod, TestCategory("WASM")]
    public Task Zoom200_Rtl_AndCoarsePointerTargets() => DataTableSpecs.ZoomRtlAndTouchAsync(this);
}
