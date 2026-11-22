using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

[TestClass]
[TestCategory("WASM")]
public sealed class TmButtonDataTableScopeE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task TmButton_NewVariants_RenderExpectedCssClasses()
    {
        var page = await OpenDemoPageAsync("/buttons", "[data-testid='button-variant-outline-secondary']");

        await ExpectClassAsync(page, "button-variant-outline-secondary", "tm-btn-outline-secondary");
        await ExpectClassAsync(page, "button-variant-warning", "tm-btn-warning");
        await ExpectClassAsync(page, "button-variant-outline-warning", "tm-btn-outline-warning");
        await ExpectClassAsync(page, "button-variant-soft", "tm-btn-soft");
    }

    [TestMethod]
    public async Task TmButton_Pressed_TogglesAriaPressed_AndThePressedClass()
    {
        var page = await OpenDemoPageAsync("/buttons", "[data-testid='button-soft-pressed']");
        var toggle = page.Locator("[data-testid='button-soft-pressed']");

        // null default would render no attribute; the demo binds a real toggle, so it starts false.
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(toggle).ToHaveClassAsync(new Regex(@"^(?:(?!tm-btn--pressed).)*$"));

        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(toggle).ToHaveClassAsync(new Regex("tm-btn--pressed"));

        // Hover-independent state: hover styles must not be the only pressed cue.
        var boxShadow = await toggle.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow");
        StringAssert.Contains(boxShadow, "inset");

        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(toggle).ToHaveClassAsync(new Regex(@"^(?:(?!tm-btn--pressed).)*$"));
    }

    [TestMethod]
    public async Task TmButton_SoftAndPressed_Screenshots_LightAndDark()
    {
        var page = await OpenDemoPageAsync("/buttons", "[data-testid='button-soft-pressed']");
        await page.Locator("[data-testid='button-soft-pressed']").ClickAsync();

        var card = page.Locator("[data-testid='buttons-soft-card']");
        await SaveCardScreenshotAsync(page, card, "buttons-soft/soft-pressed-light");
        await SetDarkAsync(page);
        await SaveCardScreenshotAsync(page, card, "buttons-soft/soft-pressed-dark");
    }

    [TestMethod]
    public async Task TmContextMenu_AsChild_HostButtonCarriesTheMenuButtonSemantics()
    {
        var page = await OpenDemoPageAsync("/feedback", "[data-testid='context-menu-as-child-trigger']");
        var trigger = page.Locator("[data-testid='context-menu-as-child-trigger']");

        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-haspopup", "menu");
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");

        await trigger.ClickAsync();
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(page.Locator("[role='menu']")).ToBeVisibleAsync();

        await trigger.ClickAsync();
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(page.Locator("[role='menu']")).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task TmContextMenu_AsChild_PopoverAnchorsToTheHostTrigger()
    {
        var page = await OpenDemoPageAsync("/feedback", "[data-testid='context-menu-as-child-trigger']");
        var trigger = page.Locator("[data-testid='context-menu-as-child-trigger']");

        await trigger.ClickAsync();
        await Assertions.Expect(page.Locator("[role='menu']")).ToBeVisibleAsync();

        // The wrapper is the anchor in AsChild mode: the menu's top edge never sits above the
        // trigger's bottom edge (Bottom placement) — an anchor to a never-rendered element would
        // push the popover to the viewport origin.
        var gap = await page.EvaluateAsync<int>(@"() => {
            const triggerRect = document.querySelector('[data-testid=context-menu-as-child-trigger]').getBoundingClientRect();
            const menuRect = document.querySelector('[role=menu]').getBoundingClientRect();
            return Math.round(menuRect.top - triggerRect.bottom);
        }");
        Assert.IsTrue(gap >= -2, $"the AsChild menu must open below the host trigger; measured top-gap {gap}px");
    }

    [TestMethod]
    public async Task TmContextMenu_AsChild_ItemActivation_ReturnsFocusToTheHostTrigger()
    {
        var page = await OpenDemoPageAsync("/feedback", "[data-testid='context-menu-as-child-trigger']");
        var trigger = page.Locator("[data-testid='context-menu-as-child-trigger']");

        await trigger.ClickAsync();
        await Assertions.Expect(page.Locator("[role='menu']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[role='menuitem']").First).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[role='menu']")).ToHaveCountAsync(0);

        var activeTestIds = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('data-testid') ?? ''");
        Assert.AreEqual("context-menu-as-child-trigger", activeTestIds,
            "an Enter activation on a menu item must hand focus back to the host's own trigger, not leave it on <body>");
    }

    private static async Task SetDarkAsync(IPage page)
    {
        var toggle = page.Locator("[data-testid='theme-toggle']:visible").First;
        if (await toggle.GetAttributeAsync("aria-pressed") != "true")
        {
            await toggle.ClickAsync();
        }

        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", "dark", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
    }

    private async Task SaveCardScreenshotAsync(IPage page, ILocator card, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__");
        Directory.CreateDirectory(Path.Combine(dir, Path.GetDirectoryName(fileName)!));
        var path = Path.Combine(dir, $"{fileName}.png");
        await card.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        TestContext.AddResultFile(path);
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

    [TestMethod]
    public async Task TmDataTable_RowAttributes_RenderForFlatVirtualizedAndGroupedRows()
    {
        var page = await OpenDemoPageAsync("/data-table", "[data-testid='datatable-rowattrs-flat-alice']");

        await ExpectRowAttributesAsync(page, "datatable-rowattrs-flat-alice", "flat", "Engineering", "91");
        await ExpectRowAttributesAsync(page, "datatable-rowattrs-virtual-bob", "virtual", "Sales", "84");
        await ExpectRowAttributesAsync(page, "datatable-rowattrs-grouped-diana", "grouped", "Engineering", "95");
    }

    private async Task<IPage> OpenDemoPageAsync(string path, string readySelector)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1366, 900);
        await GotoAppReadyAsync(page, $"{BaseUrl}{path}", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(readySelector, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        return page;
    }

    private static async Task ExpectClassAsync(IPage page, string testId, string expectedClass)
    {
        var button = page.Locator($"[data-testid='{testId}']");
        await Assertions.Expect(button).ToHaveClassAsync(new Regex($@"(^|\s){Regex.Escape(expectedClass)}(\s|$)"));
    }

    private static async Task ExpectRowAttributesAsync(IPage page, string testId, string mode, string dept, string score)
    {
        var row = page.Locator($"[data-testid='{testId}']");
        await Assertions.Expect(row).ToHaveAttributeAsync("data-row-mode", mode);
        await Assertions.Expect(row).ToHaveAttributeAsync("data-row-dept", dept);
        await Assertions.Expect(row).ToHaveAttributeAsync("data-row-score", score);
    }
}
