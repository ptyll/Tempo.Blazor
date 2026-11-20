namespace Tempo.Blazor.E2E;

/// <summary>13.4 — overlays: anchoring, sheet presentation, focus return, export scope and the filter builder.</summary>
[TestClass]
public class DataTableOverlayE2ETests : WasmTestBase
{
    [TestMethod, TestCategory("WASM")]
    public Task ViewManager_Desktop_AnchoredAndFocusRules() => DataTableSpecs.ViewManagerDesktopAsync(this);

    [TestMethod, TestCategory("WASM")]
    public Task ViewManager_Mobile_IsAFullWidthSheet() => DataTableSpecs.ViewManagerMobileSheetAsync(this);

    [TestMethod, TestCategory("WASM")]
    public Task Export_SelectedScope_DisabledWhenEmpty_DownloadsTwoRows() => DataTableSpecs.ExportScopeAsync(this);

    [TestMethod, TestCategory("WASM")]
    public Task FilterBuilder_PopoverAnchored_AutoIsASheetOnMobile() => DataTableSpecs.FilterBuilderPresentationAsync(this);

    [TestMethod, TestCategory("WASM")]
    public Task MultiViewList_GroupPicker_AnchoredAndFocusRules() => MultiViewListSpecs.GroupPickerFocusAsync(this);

    [TestMethod, TestCategory("WASM")]
    public Task DefaultAppearance_GroupZoneAndExport_KeepTheOriginalLook() => DataTableSpecs.DefaultAppearanceGroupZoneAndExportAsync(this);
}

[TestClass]
public class DataTableOverlayServerE2ETests : ServerTestBase
{
    [TestMethod, TestCategory("Server")]
    public Task ViewManager_Desktop_AnchoredAndFocusRules() => DataTableSpecs.ViewManagerDesktopAsync(this);

    [TestMethod, TestCategory("Server")]
    public Task Export_SelectedScope_DisabledWhenEmpty_DownloadsTwoRows() => DataTableSpecs.ExportScopeAsync(this);
}
