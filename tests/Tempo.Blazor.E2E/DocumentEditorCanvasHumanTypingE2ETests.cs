using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 8.7 — verifies the editor stays smooth at a HUMAN typing cadence (~180 ms/key), not just at the
/// machine-gun rate used elsewhere. At human speed the inter-key gaps are wider than the old JS->.NET
/// notification debounce, so .NET callbacks fired BETWEEN keystrokes and blocked the single WASM thread,
/// making glyphs appear in batches. The fix widens the notification debounce past typing cadence; here we
/// assert the engine paints (renders) roughly once per keystroke with no batching.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasHumanTypingE2ETests : WasmTestBase
{
    private const string LargeDocId = "large-perf-1000";

    [TestMethod]
    public async Task HumanCadenceTyping_PaintsEveryKeystroke_NoBatching()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={LargeDocId}");
        await page.WaitForSelectorAsync("[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 120_000 });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-canvas-text-rect]').length > 0", new PageWaitForFunctionOptions { Timeout = 120_000 });
        await page.WaitForTimeoutAsync(500);
        var rect = await page.Locator("[data-canvas-text-rect]").First.BoundingBoxAsync();
        await page.Mouse.ClickAsync(rect!.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        await page.WaitForTimeoutAsync(300);

        const int keys = 12;
        var revBefore = await ReadIntAttrAsync(page, "data-canvas-input-revision");
        var renderBefore = await ReadIntAttrAsync(page, "data-canvas-render-count");

        // Type at a realistic human cadence.
        await page.Keyboard.TypeAsync("performance", new KeyboardTypeOptions { Delay = 180 });
        // (11 chars in "performance"; one more to reach `keys`.)
        await page.Keyboard.PressAsync("x");
        await page.WaitForTimeoutAsync(120);

        var revAfter = await ReadIntAttrAsync(page, "data-canvas-input-revision");
        var renderAfter = await ReadIntAttrAsync(page, "data-canvas-render-count");
        var inputRevisions = revAfter - revBefore;
        var renders = renderAfter - renderBefore;
        TestContext.WriteLine($"HUMAN TYPING: {keys} keys -> inputRevisions={inputRevisions}, engineRenders={renders}");

        Assert.IsTrue(inputRevisions >= keys, $"Engine should register every keystroke (revisions={inputRevisions}, keys={keys}).");
        // Each keystroke at 180 ms should get its own paint; batching (thread blocked by per-key .NET work)
        // would collapse several keys into one render. Allow a small slop for occasional coalescing.
        Assert.IsTrue(renders >= keys - 2, $"Engine batched keystrokes into too few paints: renders={renders} for {keys} keys (thread blocked between keystrokes?).");
    }

    private static async Task<int> ReadIntAttrAsync(IPage page, string attr)
        => await page.EvaluateAsync<int>($@"() => Number(document.querySelector('[data-testid=""document-canvas-engine-root""]')?.getAttribute('{attr}') || '0')");
}
