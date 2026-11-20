using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// HTTPS WASM interaction, theme, and responsive visual coverage for the Sankey demo.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmSankeyChartE2ETests : WasmTestBase
{
    private const string SankeySection = "[data-testid='sankey-chart']";

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task SankeyDemo_IsInteractiveReadableAndResponsive()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && message.Text.Contains("Unhandled exception"))
            {
                errors.Add(message.Text);
            }
        };

        await GotoAppReadyAsync(page, $"{BaseUrl}/charts");

        var section = page.Locator(SankeySection);
        await section.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await section.ScrollIntoViewIfNeededAsync();

        var cashFlow = section.Locator("[data-testid='sankey-cashflow']");
        var customColors = section.Locator("[data-testid='sankey-custom-colors']");
        await Assertions.Expect(section.Locator(".tm-sankey")).ToHaveCountAsync(2);
        await Assertions.Expect(cashFlow.Locator("rect.tm-sankey__node")).ToHaveCountAsync(7);
        await Assertions.Expect(cashFlow.Locator("path.tm-sankey__link")).ToHaveCountAsync(6);
        Assert.AreEqual(
            "#7c3aed",
            await customColors.Locator("rect[data-node-id='budget']").GetAttributeAsync("fill"));

        await page.EvaluateAsync("() => document.fonts.ready");
        var overflowingLabels = await cashFlow.Locator("text.tm-sankey__label")
            .EvaluateAllAsync<string[]>(
                "labels => labels.flatMap(label => { const box = label.getBBox(); const right = box.x + box.width; return box.x < 0 || right > 800 ? [`${label.textContent}: ${box.x.toFixed(1)}..${right.toFixed(1)}`] : []; })");
        Assert.AreEqual(0, overflowingLabels.Length,
            "Every financial label must stay inside the Sankey viewBox: " + string.Join(", ", overflowingLabels));
        await CaptureSectionAsync(cashFlow, "sankey-light.png");

        await cashFlow.Locator("rect[data-node-id='salary']").HoverAsync();
        await Assertions.Expect(cashFlow.Locator(".tm-sankey__node--highlight")).ToHaveCountAsync(2);
        await Assertions.Expect(cashFlow.Locator(".tm-sankey__node--dimmed")).ToHaveCountAsync(5);
        await Assertions.Expect(cashFlow.Locator(".tm-sankey__link--highlight")).ToHaveCountAsync(1);
        await Assertions.Expect(cashFlow.Locator(".tm-sankey__link--dimmed")).ToHaveCountAsync(5);
        await CaptureSectionAsync(cashFlow, "sankey-edge-hover.png");

        await cashFlow.Locator("path[data-link-index='0']").ClickAsync();
        await Assertions.Expect(section.Locator("[data-testid='sankey-clicked']"))
            .ToContainTextAsync("Salary → Budget");

        await page.Locator("button[aria-label='Switch to dark mode']:visible").ClickAsync();
        await page.Locator("[data-theme='dark']").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var darkLabelColor = await cashFlow.Locator("text.tm-sankey__label").First
            .EvaluateAsync<string>("label => getComputedStyle(label).fill");
        Assert.AreNotEqual("rgb(0, 0, 0)", darkLabelColor,
            "Sankey labels must use the dark semantic text token instead of black.");
        await CaptureSectionAsync(cashFlow, "sankey-dark.png");

        await page.SetViewportSizeAsync(390, 844);
        await section.ScrollIntoViewIfNeededAsync();
        var responsiveState = await cashFlow.Locator(".tm-sankey")
            .EvaluateAsync<double[]>(
                "root => { const svg = root.querySelector('svg'); const rootBox = root.getBoundingClientRect(); const svgBox = svg.getBoundingClientRect(); return [rootBox.width, svgBox.width, document.documentElement.scrollWidth, document.documentElement.clientWidth]; }");
        Assert.IsTrue(responsiveState[1] <= responsiveState[0] + 0.5,
            $"The Sankey SVG ({responsiveState[1]}px) must fit its root ({responsiveState[0]}px).");
        Assert.IsTrue(responsiveState[2] <= responsiveState[3] + 1,
            "The responsive Sankey demo must not widen the document.");
        await CaptureSectionAsync(cashFlow, "sankey-narrow.png");

        Assert.AreEqual(0, errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", errors));
    }

    private static async Task CaptureSectionAsync(ILocator locator, string fileName)
    {
        var directory = Path.Combine(
            FindRepoRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "__screenshots__",
            "sankey");
        Directory.CreateDirectory(directory);
        await locator.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, fileName),
            Type = ScreenshotType.Png
        });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
