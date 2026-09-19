using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for the <c>.tm-th-sort</c> sort button contract, run in BOTH engines the library supports:
/// the accessible name a screen reader announces, single-path keyboard activation, and the
/// library-owned hover/focus styling — the pieces that cannot regress behind a passing bUnit
/// render because they live in real layout, a real accessibility tree and a real cascade.
/// The component-side parameter coverage (<c>SortLabel</c>, fallbacks) is bUnit's job in
/// <c>TmDataTableSortButtonNameTests</c>; here the browser proves the name resolves, the Enter
/// press sorts once, and focus/hover paint the documented styles — in Chromium AND in Firefox,
/// because an accessibility-tree answer that is only true in one engine is not an answer.
/// </summary>
[TestClass]
public class TmDataTableSortButtonE2ETests : WasmTestBase
{
    private const string DataTablePage = "/data-table";

    [TestMethod]
    [TestCategory("WASM")]
    public async Task SortButton_AccessibleName_Keyboard_And_HoverFocus_Chromium()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenDataTableAsync(page);
        await AssertSortButtonContractAsync(page);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task SortButton_AccessibleName_Keyboard_And_HoverFocus_Firefox()
    {
        // The shared browser of the base class is Chromium; Firefox is launched per-test so the
        // same contract runs against the second engine the matrix claims.
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1100 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        await OpenDataTableAsync(page);
        await AssertSortButtonContractAsync(page);
    }

    private async Task OpenDataTableAsync(IPage page)
    {
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}{DataTablePage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='dt-ergonomics-section']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
    }

    private static async Task AssertSortButtonContractAsync(IPage page)
    {
        var section = page.Locator("[data-testid='dt-ergonomics-section']");
        var buttons = section.Locator("button.tm-th-sort");
        var count = await buttons.CountAsync();
        Assert.IsTrue(count > 0, "the ergonomics table renders sortable columns");

        // ── Accessible name: every sort button resolves a non-empty name in the AX tree ──
        for (var i = 0; i < count; i++)
        {
            var label = await buttons.Nth(i).GetAttributeAsync("aria-label");
            Assert.IsFalse(string.IsNullOrWhiteSpace(label),
                $"sort button #{i} must announce a name — an aria-hidden icon alone says nothing");
            var named = section.GetByRole(AriaRole.Button,
                new LocatorGetByRoleOptions { Name = label, Exact = true });
            Assert.IsTrue(await named.CountAsync() >= 1,
                $"accessible name '{label}' must resolve through the browser's AX tree");
        }

        // ── Hover: the header label takes the primary ink, not the resting secondary ──
        var first = buttons.First;
        var restColor = await first.EvaluateAsync<string>("el => getComputedStyle(el).color");
        await first.HoverAsync();
        var hoverColor = await first.EvaluateAsync<string>("el => getComputedStyle(el).color");
        var expectedHover = await page.EvaluateAsync<string>(
            """
            () => {
                const probe = document.createElement('span');
                probe.style.color = 'var(--tm-text-primary)';
                document.body.appendChild(probe);
                const value = getComputedStyle(probe).color;
                probe.remove();
                return value;
            }
            """);
        Assert.AreEqual(expectedHover, hoverColor,
            ".tm-th-sort:hover paints --tm-text-primary — the button must answer the pointer");
        Assert.AreNotEqual(restColor, hoverColor,
            "the hover colour must differ from the resting --tm-text-secondary, otherwise the rule paints nothing");

        // ── Keyboard activation: Enter on the focused button sorts once, through the <th> ──
        var header = section.Locator("th[data-sortable='true']").First;
        await first.FocusAsync();
        await first.PressAsync("Enter");
        await Assertions.Expect(header).ToHaveAttributeAsync("aria-sort", "ascending");

        // ── Focus ring: the library ships its own :focus-visible indicator (WCAG 2.4.7) ──
        // Real keyboard focus is what :focus-visible matches, so walk Tab to the button rather
        // than scripting .focus() — programmatic focus is not guaranteed keyboard modality.
        var reachedSortButton = false;
        for (var tab = 0; tab < 30 && !reachedSortButton; tab++)
        {
            await page.Keyboard.PressAsync("Tab");
            reachedSortButton = await page.EvaluateAsync<bool>(
                "() => document.activeElement && document.activeElement.classList.contains('tm-th-sort')");
        }

        Assert.IsTrue(reachedSortButton,
            "Tab navigation must land on a .tm-th-sort button within 30 presses");

        var outline = await page.EvaluateAsync<string[]>(
            """
            () => {
                const el = document.activeElement;
                const cs = getComputedStyle(el);
                return [cs.outlineStyle, cs.outlineWidth, String(el.matches(':focus-visible'))];
            }
            """);
        Assert.AreEqual("true", outline[2],
            "a sort button reached by Tab must match :focus-visible — that is the state the ring is painted for");
        Assert.AreEqual("solid", outline[0],
            "the focused sort button paints the library's focus ring, not the platform default of 'none'");
        Assert.AreEqual("2px", outline[1]);
    }
}
