namespace Tempo.Blazor.E2E;

/// <summary>13.5 — TmMultiViewList: radiogroup, stretched link, column cap, initials and item count; plus the edge cases.</summary>
[TestClass]
public class MultiViewListRedesignE2ETests : WasmTestBase
{
    [TestMethod, TestCategory("WASM")]
    public Task Radiogroup_Cards_Initials_ItemCount_ColumnCap() => MultiViewListSpecs.RadiogroupAndCardsAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("WASM")]
    public Task StretchedLink_OneTabStop_ClickAnywhere_EnterOnce() => MultiViewListSpecs.StretchedLinkAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("WASM")]
    public Task Edge_EmptyAndVeryLongTitles() => MultiViewListSpecs.EdgeAsync(Browser, BaseUrl, RegisterContext);
}

[TestClass]
public class MultiViewListRedesignServerE2ETests : ServerTestBase
{
    [TestMethod, TestCategory("Server")]
    public Task Radiogroup_Cards_Initials_ItemCount_ColumnCap() => MultiViewListSpecs.RadiogroupAndCardsAsync(Browser, BaseUrl, RegisterContext);
}
