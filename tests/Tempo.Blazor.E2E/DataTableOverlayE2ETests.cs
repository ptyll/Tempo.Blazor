namespace Tempo.Blazor.E2E;

/// <summary>13.4 — overlays: anchoring, sheet presentation, focus return, export scope and the filter builder.</summary>
[TestClass]
public class DataTableOverlayE2ETests : WasmTestBase
{
    [TestMethod, TestCategory("WASM")]
    public Task ViewManager_Desktop_AnchoredAndFocusRules() => DataTableSpecs.ViewManagerDesktopAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("WASM")]
    public Task ViewManager_Mobile_IsAFullWidthSheet() => DataTableSpecs.ViewManagerMobileSheetAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("WASM")]
    public Task Export_SelectedScope_DisabledWhenEmpty_DownloadsTwoRows() => DataTableSpecs.ExportScopeAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("WASM")]
    public Task FilterBuilder_PopoverAnchored_AutoIsASheetOnMobile() => DataTableSpecs.FilterBuilderPresentationAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("WASM")]
    public Task MultiViewList_GroupPicker_AnchoredAndFocusRules() => MultiViewListSpecs.GroupPickerFocusAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("WASM")]
    public Task DefaultAppearance_GroupZoneAndExport_KeepTheOriginalLook() => DataTableSpecs.DefaultAppearanceGroupZoneAndExportAsync(Browser, BaseUrl, RegisterContext);
}

[TestClass]
public class DataTableOverlayServerE2ETests : ServerTestBase
{
    [TestMethod, TestCategory("Server")]
    public Task ViewManager_Desktop_AnchoredAndFocusRules() => DataTableSpecs.ViewManagerDesktopAsync(Browser, BaseUrl, RegisterContext);

    [TestMethod, TestCategory("Server")]
    public Task Export_SelectedScope_DisabledWhenEmpty_DownloadsTwoRows() => DataTableSpecs.ExportScopeAsync(Browser, BaseUrl, RegisterContext);
}
