namespace Tempo.Blazor.E2E;

/// <summary>13.8 — Czech smoke: grouping, filter and summary texts, correct plural forms, no English toolbar fallback.</summary>
[TestClass]
public class DataTableLocalizationE2ETests : WasmTestBase
{
    [TestMethod, TestCategory("WASM")]
    public Task Czech_ToolbarGroupingFilterAndPlurals() => DataTableSpecs.CzechAsync(Browser, BaseUrl, RegisterContext);
}
