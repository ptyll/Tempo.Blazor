using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Localization;

/// <summary>
/// The demo-only localization keys (<c>TmDemo_*</c> and the <c>*Demo_*</c> page families) moved out
/// of the library's <c>TmResources</c> into the demo's own <c>DemoResources</c> tables served by the
/// <c>DemoTmLocalizer</c> decorator. This smoke proves the demo pages still speak: the modal-dialog
/// page (whose buttons all use moved keys) renders the localized strings — not raw resource keys —
/// on every demo host: WASM (7106), Server (7107) and InteractiveAuto (7108, whose WebAssembly side
/// registers the same decorator in its client Program.cs).
/// </summary>
internal static class DemoLocalizerSmoke
{
    internal const string ModalDialogPage = "/modal-dialog";

    /// <summary>Asserts the moved-key strings render localized (never as raw keys) on an open page.</summary>
    internal static async Task AssertModalDialogRendersMovedDemoStringsAsync(IPage page, string baseUrl, Action<IPage> readyWait)
    {
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{baseUrl}{ModalDialogPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        readyWait(page);

        var openSheet = page.Locator("[data-testid='open-sheet']");
        await openSheet.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // The moved key resolves through the demo table to its English string…
        await Assertions.Expect(openSheet).ToHaveTextAsync("Open sheet");
        await Assertions.Expect(page.Locator("[data-testid='open-stacked']")).ToHaveTextAsync("Stacked footer");

        // …and no raw key leaks anywhere into the page.
        var body = (await page.Locator("body").TextContentAsync())!;
        body.Should().NotContain("TmDemo_",
            "a raw resource key on a demo page means the demo localizer did not serve the moved key");
    }
}

[TestClass]
[TestCategory("Smoke")]
[TestCategory("WASM")]
public class DemoLocalizerWasmE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("WASM: /modal-dialog renders the moved demo keys localized (DemoTmLocalizer serves them)")]
    public async Task ModalDialog_RendersMovedDemoKeys()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await DemoLocalizerSmoke.AssertModalDialogRendersMovedDemoStringsAsync(page, BaseUrl, p => WaitForAppReadyAsync(p));
    }
}

[TestClass]
[TestCategory("Smoke")]
[TestCategory("Server")]
public class DemoLocalizerServerE2ETests : ServerTestBase
{
    [TestMethod]
    [Description("Server: /modal-dialog renders the moved demo keys localized (DemoTmLocalizer serves them)")]
    public async Task ModalDialog_RendersMovedDemoKeys()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await DemoLocalizerSmoke.AssertModalDialogRendersMovedDemoStringsAsync(page, BaseUrl, p => WaitForAppReadyAsync(p));
    }
}

[TestClass]
[TestCategory("Smoke")]
[TestCategory("InteractiveAuto")]
public class DemoLocalizerInteractiveAutoE2ETests : InteractiveAutoTestBase
{
    [TestMethod]
    [Description("InteractiveAuto: /modal-dialog renders the moved demo keys localized on the auto render mode")]
    public async Task ModalDialog_RendersMovedDemoKeys()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await DemoLocalizerSmoke.AssertModalDialogRendersMovedDemoStringsAsync(page, BaseUrl, p => WaitForAppReadyAsync(p));
    }
}
