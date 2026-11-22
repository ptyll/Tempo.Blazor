using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// B7.1 — verifies the freeze the user reported ("občas se to cukne") is gone. The existing HumanTyping test
/// types at a UNIFORM 180 ms/key, which never exceeds the ~400 ms JS notify-debounce, so the .NET change-notify
/// never fires mid-burst and no parent render happens. REAL typing has inter-word pauses > 400 ms (end of a
/// word, a thinking pause) that DO expire the debounce → the old code then ran an ungated
/// <c>StateHasChanged</c> on the giant TmDocumentEditor (~120 toolbar params + status bar + mini-toolbar +
/// panels, ~200 ms BuildRenderTree) on the single WASM thread, stalling the canvas paint → glyphs appeared in
/// a batch after the freeze. B7.1 gates that render on the chrome signature, so a change-notify that alters
/// nothing toolbar-visible (the common case: dirty already true, formatting unchanged) renders nothing.
///
/// This test types several words WITH > 450 ms pauses between them (so the debounce fires repeatedly mid-
/// typing) and asserts the Blazor PARENT render count barely grows, while the engine keeps painting per key.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasParentRenderGateE2ETests : WasmTestBase
{
    private const string LargeDocId = "large-perf-1000";

    [TestMethod]
    public async Task TypingWithInterWordPauses_DoesNotRebuildParentChrome()
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
        await page.WaitForTimeoutAsync(400);

        // First keystroke flips dirty false->true (one legitimate parent render); measure AFTER it so the
        // gate's steady-state (dirty already true) is what we assert against.
        await page.Keyboard.PressAsync("a");
        await page.WaitForTimeoutAsync(700); // let the debounce + first dirty render settle

        var engineBefore = await ReadEngineRevisionAsync(page);
        var parentBefore = await ReadParentRenderCountAsync(page);

        // Type words with pauses > the ~400 ms notify-debounce between them, so the .NET change-notify fires
        // repeatedly DURING typing (the real-world cadence that produced the freeze).
        string[] words = { "rychle", "hezky", "obcas", "skace" };
        foreach (var word in words)
        {
            await page.Keyboard.TypeAsync(word, new KeyboardTypeOptions { Delay = 90 });
            await page.WaitForTimeoutAsync(550); // inter-word pause -> fires the ~400 ms debounce -> change-notify
        }

        var engineAfter = await ReadEngineRevisionAsync(page);
        var parentAfter = await ReadParentRenderCountAsync(page);

        var typedKeys = string.Concat(words).Length; // 6+5+5+5 = 21
        var engineRenders = engineAfter - engineBefore;
        var parentRenders = parentAfter - parentBefore;
        TestContext.WriteLine($"PARENT-RENDER GATE: typedKeys={typedKeys}, engineInputRevisions={engineRenders}, parentRenders={parentRenders}");

        // The engine must register every keystroke (the canvas keeps up).
        Assert.IsTrue(engineRenders >= typedKeys, $"Engine should register every keystroke (revisions={engineRenders}, keys={typedKeys}).");

        // The parent chrome must NOT rebuild per keystroke. Before B7.1 this was ~one parent render PER KEY
        // (the @onkeydown implicit render + ungated change-notify) — i.e. >= typedKeys — and each ~200 ms
        // rebuild stalled the canvas paint (the reported freeze). After B7.1 only low-frequency chrome updates
        // remain around dirty/autosave and debounced toolbar sync points. Keep the threshold well below typedKeys
        // so a per-key rendering regression still fails while allowing legitimate pause-bound updates.
        var parentRenderBudget = Math.Min(12, typedKeys - 1);
        Assert.IsTrue(parentRenders <= parentRenderBudget, $"Parent chrome rebuilt too often during typing (parentRenders={parentRenders}, budget={parentRenderBudget} for {typedKeys} keys + {words.Length} pauses) — the per-edit render gate is not holding.");
    }

    private static async Task<int> ReadEngineRevisionAsync(IPage page)
        => await page.EvaluateAsync<int>(@"() => Number(document.querySelector('[data-testid=""document-canvas-engine-root""]')?.getAttribute('data-canvas-input-revision') || '0')");

    private static async Task<int> ReadParentRenderCountAsync(IPage page)
        => await page.EvaluateAsync<int>(@"() => Number(document.querySelector('[data-blazor-render-count]')?.getAttribute('data-blazor-render-count') || '0')");
}
