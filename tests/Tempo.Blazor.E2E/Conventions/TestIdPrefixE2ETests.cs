using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Conventions;

/// <summary>
/// E2E for the K9 <c>data-testid</c> convention (WASM @ 7106): the /testid-prefix demo renders the same
/// <c>TmFileUploadProgress</c> three times — with <c>TestIdPrefix="k9-alpha"</c>, <c>TestIdPrefix="k9-beta"</c>,
/// and with no prefix. Proves in a real browser that a set prefix namespaces every internal id as
/// <c>{prefix}-{name}</c> (distinct per instance) while a null prefix keeps the bare, backward-compatible id.
/// Screenshot lands in <c>__screenshots__/conventions/</c> for review.
/// </summary>
[TestClass]
public sealed class TestIdPrefixE2ETests : WasmTestBase
{
    private const string Route = "/testid-prefix";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture','en');");
        var page = await context.NewPageAsync();
        page.PageError += (_, e) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + e); };
        page.Console += (_, m) =>
        {
            if (m.Type == "error" && m.Text.Contains("Unhandled exception"))
                lock (_clientErrors) _clientErrors.Add("CONSOLE: " + m.Text);
        };
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}{Route}");

        var section = page.Locator("[data-testid='k9-testid-prefix-section']");
        await section.ScrollIntoViewIfNeededAsync();
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task TestIdPrefix_NamespacesInternalIds_WhileBareStaysBackwardCompatible()
    {
        var page = await OpenAsync();

        // (a) A set prefix namespaces the component's internal root id as "{prefix}-{name}".
        //     Both prefixed instances must be present AND distinct.
        var alphaRoot = page.Locator("[data-testid='k9-alpha-tm-upload-progress']");
        var betaRoot = page.Locator("[data-testid='k9-beta-tm-upload-progress']");
        await Assertions.Expect(alphaRoot).ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await Assertions.Expect(betaRoot).ToHaveCountAsync(1);
        await Assertions.Expect(alphaRoot).ToBeVisibleAsync();
        await Assertions.Expect(betaRoot).ToBeVisibleAsync();

        // Nested internal ids are namespaced too (two upload rows per instance), proving the prefix
        // is applied to EVERY internal data-testid, not just the root.
        await Assertions.Expect(page.Locator("[data-testid='k9-alpha-upload-item']")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("[data-testid='k9-beta-upload-item']")).ToHaveCountAsync(2);

        // (b) The unprefixed instance still exposes the BARE id — an exact-attribute match, so the
        //     prefixed ids ("k9-alpha-tm-upload-progress", …) do not satisfy it: backward compatible.
        var bareRoot = page.Locator("[data-testid='tm-upload-progress']");
        await Assertions.Expect(bareRoot).ToHaveCountAsync(1);
        await Assertions.Expect(bareRoot).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='upload-item']")).ToHaveCountAsync(2);

        await SaveScreenshotAsync(page, "testid-prefix");

        // No unhandled client-side errors on the demo page.
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "conventions");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png"), FullPage = true });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx"))) return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
