namespace Tempo.Blazor.E2E;

/// <summary>13.3 — 390×844 touch: cards, sort dropdown, filter sheet, pinned Scroll column, pager, tablet 1024.</summary>
[TestClass]
public class DataTableMobileE2ETests : WasmTestBase
{
    [TestMethod, TestCategory("WASM")]
    public Task Cards_HeaderGone_SortDropdown_FilterSheet() => DataTableSpecs.MobileCardsAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("WASM")]
    public Task ScrollPinnedColumn_Pager_TabletCellsDoNotOverlap() => DataTableSpecs.ScrollPinnedAndPagerAsync(Browser, BaseUrl, RegisterContext);
}

[TestClass]
public class DataTableMobileServerE2ETests : ServerTestBase
{
    [TestMethod, TestCategory("Server")]
    public Task Cards_HeaderGone_SortDropdown_FilterSheet() => DataTableSpecs.MobileCardsAsync(Browser, BaseUrl, RegisterContext);
}
