namespace Tempo.Blazor.E2E;

/// <summary>13.6 — column menu actions and the keyboard path (Tab → Alt+ArrowDown → arrows → Escape returns focus).</summary>
[TestClass]
public class DataTableColumnMenuPhase13E2ETests : WasmTestBase
{
    [TestMethod, TestCategory("WASM")]
    public Task ColumnMenu_SortHideGroup_AndKeyboard() => DataTableSpecs.ColumnMenuAsync(Browser, BaseUrl, RegisterContext);
}

[TestClass]
public class DataTableColumnMenuPhase13ServerE2ETests : ServerTestBase
{
    [TestMethod, TestCategory("Server")]
    public Task ColumnMenu_SortHideGroup_AndKeyboard() => DataTableSpecs.ColumnMenuAsync(Browser, BaseUrl, RegisterContext);
}
