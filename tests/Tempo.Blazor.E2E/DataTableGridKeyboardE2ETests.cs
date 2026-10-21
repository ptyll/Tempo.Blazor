namespace Tempo.Blazor.E2E;

/// <summary>13.2 — grid keyboard on the redesign demo (real key presses, real focus, real scroll position).</summary>
[TestClass]
public class DataTableGridKeyboardE2ETests : WasmTestBase
{
    [TestMethod, TestCategory("WASM")]
    public Task GridKeyboard_TabArrowsEnterSpaceHomeFilterAndGroup() => DataTableSpecs.GridKeyboardAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("WASM")]
    public Task GridEdit_F2FocusesEditor_EscapeCancels_EnterSaves() => DataTableSpecs.GridEditAsync(Browser, BaseUrl, RegisterContext);
}

[TestClass]
public class DataTableGridKeyboardServerE2ETests : ServerTestBase
{
    [TestMethod, TestCategory("Server")]
    public Task GridKeyboard_TabArrowsEnterSpaceHomeFilterAndGroup() => DataTableSpecs.GridKeyboardAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("Server")]
    public Task GridEdit_F2FocusesEditor_EscapeCancels_EnterSaves() => DataTableSpecs.GridEditAsync(Browser, BaseUrl, RegisterContext);
}
