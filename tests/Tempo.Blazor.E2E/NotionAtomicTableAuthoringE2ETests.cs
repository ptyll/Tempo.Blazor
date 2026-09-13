using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class NotionAtomicTableAuthoringE2ETests : NotionE2ETestBase
{
    private static readonly Guid PageId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("F6 validates canonical table rendering, atomic conflict recovery, and responsive light/dark UX.")]
    public async Task F6_AtomicTable_RendersAndRecoversConflictAcrossThemesAndViewports()
    {
        var page = await OpenNotionEditorAsync(390, 844);
        await SeedAtomicTablePageAsync();
        var table = page
            .Locator("[data-block-id='f6000000-0000-0000-0000-000000000010']")
            .First;

        await Assertions.Expect(table).ToContainTextAsync("Atomic authoring");
        await Assertions.Expect(table.Locator(".tm-notion-table-block"))
            .ToHaveAttributeAsync("data-aggregate-enabled", "true");
        await Assertions.Expect(Cell(table, 0, 0)).ToHaveAttributeAsync("colspan", "2");
        await Assertions.Expect(Cell(table, 0, 0)).ToHaveCSSAsync(
            "background-color",
            "rgb(219, 234, 254)");
        await Assertions.Expect(Cell(table, 1, 1)).ToHaveCSSAsync(
            "text-align",
            "center");
        await Assertions.Expect(Cell(table, 0, 0)).ToHaveCSSAsync(
            "border-bottom-width",
            "2px");
        var wrapper = table.Locator(".tm-notion-table-block__wrapper");
        await wrapper.EvaluateAsync("element => element.scrollLeft = 0");
        var scrollWidth = await wrapper.EvaluateAsync<double>("element => element.scrollWidth");
        var clientWidth = await wrapper.EvaluateAsync<double>("element => element.clientWidth");
        Assert.IsTrue(scrollWidth > clientWidth, "Canonical column widths should scroll within the mobile table wrapper.");
        await CaptureAsync(page, table, "normal-light-mobile");

        await page.SetViewportSizeAsync(1366, 900);
        await table.ScrollIntoViewIfNeededAsync();
        await CaptureAsync(page, table, "normal-light-desktop");

        await SetThemeAsync(page, true);
        await CaptureAsync(page, table, "normal-dark-desktop");
        await SetThemeAsync(page, false);

        await AdvanceServerTokenAsync();
        await table.Locator(".tm-notion-table-block__add-col").ClickAsync();

        var conflict = table.Locator("[data-testid='notion-table-conflict']");
        await page.WaitForTimeoutAsync(1000);
        Assert.IsTrue(
            await conflict.IsVisibleAsync(),
            $"Conflict recovery should be visible. Table state: {await table.InnerTextAsync()}");
        await Assertions.Expect(conflict).ToContainTextAsync("Save conflict");
        Assert.AreEqual(
            4,
            await table.Locator("[data-tm-row='1']").CountAsync(),
            "The conflicted local candidate should keep the newly added column.");
        await CaptureAsync(page, table, "conflict-local-candidate");

        await conflict.GetByRole(AriaRole.Button, new() { Name = "Reapply my changes" })
            .ClickAsync();
        await Assertions.Expect(conflict).ToBeHiddenAsync();
        Assert.AreEqual(
            4,
            await table.Locator("[data-tm-row='1']").CountAsync(),
            "Reapply should keep the local multi-block table change.");
        await CaptureAsync(page, table, "conflict-reapplied");
    }

    private static ILocator Cell(ILocator table, int row, int column)
        => table.Locator($"[data-tm-row='{row}'][data-tm-col='{column}']").First;

    private static async Task SetThemeAsync(IPage page, bool dark)
    {
        await page.EvaluateAsync(
            """
            dark => {
                document.documentElement.toggleAttribute('data-theme', dark);
                if (dark) {
                    document.documentElement.setAttribute('data-theme', 'dark');
                    document.body.classList.add('tm-dark');
                } else {
                    document.documentElement.removeAttribute('data-theme');
                    document.body.classList.remove('tm-dark');
                }
            }
            """,
            dark);
        await page.WaitForTimeoutAsync(200);
    }

    private static async Task AdvanceServerTokenAsync()
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler);
        var response = await client.PostAsync(
            $"https://localhost:5100/api/notion/aggregate/e2e/advance-token/{PageId:D}",
            null);
        response.EnsureSuccessStatusCode();
    }

    private async Task CaptureAsync(IPage page, ILocator table, string name)
    {
        var output = BaselineOutput.DirectoryFor(TestContext, "notion",
            "atomic-table");
        Directory.CreateDirectory(output);
        var fullPath = Path.Combine(output, $"{name}.png");
        var regionPath = Path.Combine(output, $"{name}.region.png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            FullPage = true,
            Type = ScreenshotType.Png
        });
        await table.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png
        });
        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
    }
}
