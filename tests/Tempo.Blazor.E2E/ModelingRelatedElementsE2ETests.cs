using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 13: the inspector's Relations tab (related elements) across the three bands — the desktop
/// docked tab (badge + navigation to the counterpart), the tablet inspector side sheet (tap) and
/// the mobile Relations SECTION under the properties (no horizontal overflow at 320×640). The
/// navigation reuses the editor's selection paths: the tree selection and the inspector follow.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingRelatedElementsE2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("1440x900 desktop: the Relations tab sits between Properties and Source, carries the related-count badge and navigating to a counterpart selects it in the tree and the inspector")]
    public async Task Relations_Desktop_TabBadgeAndCounterpartNavigation()
    {
        var page = await OpenLoadedModelingPageAsync(1440, 900, touch: false);
        try
        {
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();
            await ExpectSelectedAsync(page, "bpmn-validate-order");

            // The badge shows the related count (1 outgoing + 2 incoming in the demo model).
            var badge = page.Locator("[data-testid='modeling-tab-relations'] .tm-tab__badge");
            Assert.AreEqual("3", await badge.TextContentAsync(), "the Relations tab badge shows the related-row count");

            await OpenRelationsTabAsync(page);

            // Phase 13 review: the THREE-tab strip fits the docked aside — the Source tab's
            // right edge stays inside the inspector panel at 1440x900.
            var panelBox = await page.Locator("[data-testid='modeling-inspector-panel']").BoundingBoxAsync();
            var sourceBox = await page.Locator("[data-testid='modeling-tab-source']").BoundingBoxAsync();
            Assert.IsNotNull(panelBox);
            Assert.IsNotNull(sourceBox);
            Assert.IsTrue(sourceBox.X + sourceBox.Width <= panelBox.X + panelBox.Width + 0.5,
                $"the Source tab's right edge ({sourceBox.X + sourceBox.Width}) must stay inside the inspector panel ({panelBox.X + panelBox.Width})");

            Assert.AreEqual(1, await page.Locator("[data-testid='modeling-related-outgoing-rel-validate-to-ship']").CountAsync(),
                "the outgoing group lists the validate→ship flow");
            Assert.AreEqual(1, await page.Locator("[data-testid='modeling-related-incoming-rel-start-to-validate']").CountAsync(),
                "the incoming group lists the start→validate flow");
            Assert.AreEqual(1, await page.Locator("[data-testid='modeling-related-incoming-rel-order-service-triggers-validation']").CountAsync());

            // Navigate to the counterpart: the selection lands in the tree and the inspector
            // returns to the Properties tab presenting it.
            await page.Locator("[data-testid='modeling-related-outgoing-counterpart-rel-validate-to-ship']").ClickAsync();
            await ExpectSelectedAsync(page, "bpmn-ship-order");
            Assert.AreEqual("true", await page.Locator("[data-testid='modeling-tree-node-bpmn-ship-order']")
                .GetAttributeAsync("aria-selected"), "the counterpart is selected in the model tree");
            await ExpectVisibleAsync(page, "[data-testid='modeling-inspector'][data-kind='element']");
            await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-name']", "Ship order");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("1024x768 touch: the Relations tab lives inside the inspector side sheet and a tap on a counterpart navigates (the one-open sheet rule respected)")]
    public async Task Relations_Tablet_SheetTapNavigatesToCounterpart()
    {
        var page = await OpenLoadedModelingPageAsync(1024, 768, touch: true);
        try
        {
            // Select through the tree drawer: the tablet rule "a selection opens the inspector
            // sheet" swaps the drawer for the sheet under the one-open rule.
            await page.Locator("[data-testid='modeling-panel-tab-tree']").TapAsync();
            await page.Locator("[data-testid='modeling-tree-drawer']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").TapAsync();
            await ExpectSelectedAsync(page, "bpmn-validate-order");

            var sheet = page.Locator("[data-testid='modeling-inspector-sheet']");
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            await sheet.Locator("[data-testid='modeling-tab-relations']").TapAsync();
            var counterpart = sheet.Locator("[data-testid='modeling-related-outgoing-counterpart-rel-validate-to-ship']");
            await counterpart.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await counterpart.TapAsync();

            await ExpectSelectedAsync(page, "bpmn-ship-order");
            await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-sheet'] [data-testid='modeling-inspector-name']", "Ship order");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("390x844 touch: the mobile Inspector tab renders the Relations SECTION (no extra tab level) and 320x640 introduces no horizontal overflow")]
    public async Task Relations_Mobile_SectionUnderProperties_NoOverflow()
    {
        foreach (var (width, height) in new[] { (390, 844), (320, 640) })
        {
            var page = await OpenLoadedModelingPageAsync(width, height, touch: true);
            try
            {
                await page.Locator("[data-testid='modeling-panel-tab-tree']").TapAsync();
                await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").TapAsync();
                await ExpectSelectedAsync(page, "bpmn-validate-order");

                await page.Locator("[data-testid='modeling-panel-tab-inspector']").TapAsync();
                var inspector = page.Locator("[data-testid='modeling-inspector-panel']");
                await inspector.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

                // No extra tab level on mobile: the relations section renders INSIDE the tab body.
                Assert.AreEqual(0, await page.Locator("[data-testid='modeling-inspector-panel'] [data-testid='modeling-tab-relations']").CountAsync(),
                    "mobile shows a section, never another tab strip inside the inspector");
                var outgoing = inspector.Locator("[data-testid='modeling-related-outgoing-rel-validate-to-ship']");
                await outgoing.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                Assert.IsTrue((await outgoing.TextContentAsync())!.Contains("Ship order"), "the outgoing row names the counterpart");

                Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), $"no horizontal overflow at {width}x{height}");
            }
            finally
            {
                await page.Context.CloseAsync();
            }
        }
    }

    [TestMethod]
    [Description("1440x900 a11y: the inspector tablist has three named tabs and the relation rows carry the localized button names")]
    public async Task Relations_Accessibility_TabNamesAndButtonNames()
    {
        var page = await OpenLoadedModelingPageAsync(1440, 900, touch: false);
        try
        {
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();
            await ExpectSelectedAsync(page, "bpmn-validate-order");
            await OpenRelationsTabAsync(page);

            var tabs = page.Locator("[data-testid='modeling-inspector-panel'] [role='tab']");
            Assert.AreEqual(3, await tabs.CountAsync(), "the inspector tablist has Properties | Relations | Source");
            StringAssert.Contains(await tabs.Nth(0).TextContentAsync(), "Properties");
            StringAssert.Contains(await tabs.Nth(1).TextContentAsync(), "Relations");
            StringAssert.Contains(await tabs.Nth(2).TextContentAsync(), "Source");

            var counterpart = page.Locator("[data-testid='modeling-related-outgoing-counterpart-rel-validate-to-ship']");
            await counterpart.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            StringAssert.Contains(await counterpart.GetAttributeAsync("aria-label") ?? string.Empty,
                "Select element", "the counterpart action names its target");
            var relationshipAction = page.Locator("[data-testid='modeling-related-outgoing-relationship-rel-validate-to-ship']");
            StringAssert.Contains(await relationshipAction.GetAttributeAsync("aria-label") ?? string.Empty,
                "Select relationship", "the relationship-type action names its target");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<IPage> OpenLoadedModelingPageAsync(int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 640,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en'); localStorage.removeItem('tm-demo-theme');");

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}?notation=bpmn&viewpoint=overview");
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        return page;
    }

    private static async Task OpenRelationsTabAsync(IPage page)
    {
        await page.Locator("[data-testid='modeling-tab-relations']").ClickAsync();
        await page.Locator("[data-testid='modeling-related-panel']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    private static async Task ExpectSelectedAsync(IPage page, string elementId)
    {
        await page.Locator($"[data-testid='modeling-editor'][data-selected-element-id='{elementId}']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 5000)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static async Task ExpectTextContainsAsync(IPage page, string selector, string expectedText)
    {
        var text = await page.Locator(selector).TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, expectedText);
    }

    private static Task<bool> HasNoHorizontalOverflowAsync(IPage page) => page.EvaluateAsync<bool>(
        """
        () => {
            const doc = document.documentElement;
            const body = document.body;
            return Math.max(doc.scrollWidth, body ? body.scrollWidth : 0) <= window.innerWidth + 1;
        }
        """);
}
