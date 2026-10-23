using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>F6 lane: the TmEditorShell resizer (pointer drag, keyboard, touch hit area, persistence) — split from the main file to stay under the source-length ceiling.</summary>
public partial class EditorShellE2ETests
{
    // ── Resizer + persistence (Q4) ──────────────────────────────────────────

    private const string StorageKey = "tempo.tm-editor-shell.editor-shell-demo";

    [TestMethod]
    public async Task Demo_1440_Resizer_PointerDragAndKeyboard_ClampToMinMaxAndTheCanvas_Persist_AndRestoreAfterReload()
    {
        var page = await OpenPlainPageAsync(1440, 900);
        RegisterContext(page.Context);
        await WaitForAppReadyAsync(page);

        var separator = page.Locator($"{Shell} [role='separator'][data-side='left']");
        await Assertions.Expect(separator).ToBeVisibleAsync();
        Assert.AreEqual("vertical", await separator.GetAttributeAsync("aria-orientation"));
        Assert.AreEqual("200", await separator.GetAttributeAsync("aria-valuemin"));
        // F6 r2 G8: the announced maximum is the REACHABLE one - at 1440 the ~398px canvas has no
        // room above MinCanvasWidth, so it is the current width, not the configured 480.
        var announcedMax = int.Parse((await separator.GetAttributeAsync("aria-valuemax"))!);
        Assert.IsTrue(announcedMax is >= 280 and < 480, $"aria-valuemax reflects what the canvas allows ({announcedMax})");
        Assert.AreEqual("280", await separator.GetAttributeAsync("aria-valuenow"));
        Assert.IsFalse(string.IsNullOrEmpty(await separator.GetAttributeAsync("aria-label")));
        Assert.IsNull(await page.EvaluateAsync<string?>($"() => localStorage.getItem('{StorageKey}')"), "nothing is stored before the user resizes");

        // At 1440 the demo canvas is ~398px — already at MinCanvasWidth — so GROWING the panel is
        // refused (the clamp working); shrinking is always allowed. Pointer drag: -60px.
        var canvasBefore = await WidthOfAsync(page, $"{Shell} [data-region='canvas']");
        Assert.IsTrue(canvasBefore <= 420, $"precondition: the canvas starts at/near the minimum ({canvasBefore}px)");
        var box = (await separator.BoundingBoxAsync())!;
        var y = box.Y + 100;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 30, y, new MouseMoveOptions { Steps = 4 });
        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 60, y, new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();

        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "220");
        Assert.AreEqual(220, Math.Round(await WidthOfAsync(page, $"{Shell} aside[data-region='left']")), "the panel follows the pointer");
        Assert.IsTrue(await WidthOfAsync(page, $"{Shell} [data-region='canvas']") > canvasBefore + 40, "the canvas absorbed the freed width");
        var stored = await page.EvaluateAsync<string?>($"() => localStorage.getItem('{StorageKey}')");
        Assert.IsNotNull(stored, "a user resize persists under the key");
        StringAssert.Contains(stored, "\"left\"");
        Assert.IsFalse(stored.Contains("\"right\""), "only the panel the user resized is stored");

        // Dragging back past what the canvas allows stops at the canvas minimum (not at the max).
        var box2 = (await separator.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box2.X + box2.Width / 2, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box2.X + box2.Width / 2 + 150, y, new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();
        var grown = int.Parse((await separator.GetAttributeAsync("aria-valuenow"))!);
        Assert.IsTrue(grown is > 220 and < 300, $"a drag to +150 is clamped by MinCanvasWidth ({grown}px)");
        Assert.IsTrue(await WidthOfAsync(page, $"{Shell} [data-region='canvas']") >= 399, "the canvas never drops below MinCanvasWidth");

        // Keyboard: ArrowRight +16, ArrowLeft -16, Shift+ArrowLeft -64 (clamped to min), Home = min, End = clamped.
        await separator.FocusAsync();
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "200");
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "216");
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "200");
        await page.Keyboard.PressAsync("Shift+ArrowLeft");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "200");
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "264");
        await page.Keyboard.PressAsync("End");
        await page.WaitForTimeoutAsync(300);
        var endWidth = int.Parse((await separator.GetAttributeAsync("aria-valuenow"))!);
        Assert.IsTrue(endWidth is > 264 and <= 480, $"End goes to the maximum or as far as the canvas allows ({endWidth})");
        Assert.IsTrue(await WidthOfAsync(page, $"{Shell} [data-region='canvas']") >= 399, "the canvas never drops below MinCanvasWidth");

        // Settle on 264 and reload: the stored width is restored.
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "264");

        await page.ReloadAsync();
        await WaitForAppReadyAsync(page);
        var restored = page.Locator($"{Shell} [role='separator'][data-side='left']");
        await Assertions.Expect(restored).ToHaveAttributeAsync("aria-valuenow", "264", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        Assert.AreEqual(264, Math.Round(await WidthOfAsync(page, $"{Shell} aside[data-region='left']")), "the stored width is applied on load");
        Assert.IsNotNull(await page.EvaluateAsync<string?>($"() => localStorage.getItem('{StorageKey}')"));
        await CaptureViewportOnlyAsync(page, "1440-resized-restored");
    }
    [TestMethod]
    public async Task Demo_768_Touch_Resizer_HitAreaIsWiderThanTheVisualStrip_DragEightPixelsOffWorks()
    {
        // F6 r2 G8: under (pointer: coarse) the 6px strip is a poor target; the hit area is widened.
        // Phase 15 (C9): at 768 the tablet fit parks the inspector at MinRightWidth (240) — the panel
        // minimum — so no drag can change it: shrinking clamps at the minimum, growing is refused
        // while the canvas is below MinCanvasWidth. The regression guard is the HIT AREA itself: a
        // touch starting 8px off the strip must still reach the resizer (the separator enters its
        // dragging state) and the clamp keeps the parked width.
        var context = await CreateTouchContextAsync(768, 1024);
        var page = await GotoEditorShellAsync(context, 768, 1024);
        RegisterContext(context);
        var shell = page.Locator(Shell);
        await Assertions.Expect(shell).ToHaveAttributeAsync("data-layout", "tablet");
        var separator = shell.Locator("[role='separator'][data-side='right']");
        await Assertions.Expect(separator).ToBeVisibleAsync();
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"), "touch emulation must be on");

        var hit = await page.EvaluateAsync<double>("() => parseFloat(getComputedStyle(document.querySelector(\"[data-testid='editor-shell'] .tm-editor-shell__resizer\"), '::before').width) || 0");
        Assert.IsTrue(hit >= 24, $"the coarse-pointer hit area (::before) is at least 24px wide ({hit}px)");

        // The C9 fit converges one extra render after the first canvas measurement: the inspector
        // parks at its minimum (240) and the canvas keeps everything the panel can give (~358).
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "240", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });

        var box = (await separator.BoundingBoxAsync())!;
        var cdp = await context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2 + 8;   // 8px off the visual strip
        var y = box.Y + 300;
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchStart", ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y } } });
        for (var i = 1; i <= 10; i++)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchMove", ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = x + 6 * i, ["y"] = y } } });
            await page.WaitForTimeoutAsync(16);
            if (i == 3)
            {
                // The gesture reached the resizer 8px off the strip: the dragging class is on.
                var dragging = await page.EvaluateAsync<bool>(
                    "() => document.querySelector(\"[data-testid='editor-shell'] [role='separator'][data-side='right']\").classList.contains('tm-editor-shell__resizer--dragging')");
                Assert.IsTrue(dragging, "a touch drag 8px off the strip is captured by the widened hit area");
            }
        }
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        await page.WaitForTimeoutAsync(500);

        // The drag was received — and clamped: the parked minimum width holds.
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "240");
        Assert.AreEqual(240, Math.Round(await WidthOfAsync(page, $"{Shell} aside.tm-editor-shell__panel--right")),
            "the drag clamps at the panel minimum the tablet fit parked at");
    }
    [TestMethod]
    public async Task Demo_1440_Resizer_MalformedStoredValue_IsIgnored_NeverInjectedIntoTheStyle()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddInitScriptAsync($"localStorage.setItem('{StorageKey}', JSON.stringify({{ left: {{ w: '300px; background:red', base: '280px' }}, right: {{ w: 99999, base: '320px' }} }}))");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{Route}");
        await WaitForAppReadyAsync(page);

        var aside = page.Locator($"{Shell} aside[data-region='left']");
        await Assertions.Expect(aside).ToBeVisibleAsync();
        Assert.AreEqual(280, Math.Round(await WidthOfAsync(page, $"{Shell} aside[data-region='left']")));
        Assert.AreEqual(320, Math.Round(await WidthOfAsync(page, $"{Shell} aside[data-region='right']")), "an out-of-range stored width is ignored");
        Assert.IsFalse((await aside.GetAttributeAsync("style"))!.Contains("red"));
    }

}
