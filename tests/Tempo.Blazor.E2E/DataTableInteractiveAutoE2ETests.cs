namespace Tempo.Blazor.E2E;

/// <summary>Phase 13 — the same table specs on the InteractiveAuto host (server-first, WASM later), where the render-mode switch could break focus and overlays.</summary>
[TestClass]
public class DataTableInteractiveAutoE2ETests : InteractiveAutoTestBase
{
    [TestMethod, TestCategory("InteractiveAuto")]
    public Task ResultPreview_MatchesRowsAfterApplying() => DataTableSpecs.ResultPreviewAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("InteractiveAuto")]
    public Task ColumnMenu_SortHideGroup_AndKeyboard() => DataTableSpecs.ColumnMenuAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("InteractiveAuto")]
    public Task GridEdit_F2FocusesEditor_EscapeCancels_EnterSaves() => DataTableSpecs.GridEditAsync(Browser, BaseUrl, RegisterContext);
}
