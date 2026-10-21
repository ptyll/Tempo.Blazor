namespace Tempo.Blazor.E2E;

/// <summary>13.7 — "N matching records" equals the rows left after the filter is applied.</summary>
[TestClass]
public class DataTableResultPreviewE2ETests : WasmTestBase
{
    [TestMethod, TestCategory("WASM")]
    public Task ResultPreview_MatchesRowsAfterApplying() => DataTableSpecs.ResultPreviewAsync(Browser, BaseUrl, RegisterContext);
}

[TestClass]
public class DataTableResultPreviewServerE2ETests : ServerTestBase
{
    [TestMethod, TestCategory("Server")]
    public Task ResultPreview_MatchesRowsAfterApplying() => DataTableSpecs.ResultPreviewAsync(Browser, BaseUrl, RegisterContext);
}
