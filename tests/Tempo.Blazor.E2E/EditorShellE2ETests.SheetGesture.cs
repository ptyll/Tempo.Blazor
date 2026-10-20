using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F19: the editor-shell mobile sheet's gesture runs on the shared pointer-drag helper. A
/// snap-to-snap drag settles through the same settle() the feedback sheet uses, and a second
/// finger can neither move nor end an active drag. Touch-typed pointer events are dispatched
/// in-page — what a finger sends (a mouse press on the handle would also move focus). The
/// events carry real browser timestamps: PointerEventInit has no timeStamp, so passing one
/// does nothing.
/// </summary>
public partial class EditorShellE2ETests
{
    /// <summary>Drag the sheet grabber by <paramref name="dy"/> px (negative = up) as one finger.</summary>
    private static async Task DragSheetHandleAsync(IPage page, double dy, int steps = 20, int stepMs = 32)
    {
        await page.EvaluateAsync(
            """
            async ({ dy, steps, stepMs }) => {
                const root = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__sheet");
                const handle = root.querySelector('.tm-sheet__handle') ?? root.querySelector('.tm-drawer__header');
                const r = handle.getBoundingClientRect();
                const x = r.left + r.width / 2, y = r.top + r.height / 2;
                const fire = (type, offset) => handle.dispatchEvent(new PointerEvent(type, {
                    pointerId: 41, pointerType: 'touch', isPrimary: true, button: 0, buttons: 1,
                    clientX: x, clientY: y + offset, bubbles: true, cancelable: true,
                }));
                const wait = ms => new Promise(res => setTimeout(res, ms));
                fire('pointerdown', 0);
                for (let i = 1; i <= steps; i++) { await wait(stepMs); fire('pointermove', dy * i / steps); }
                await wait(stepMs);
                fire('pointerup', dy);
            }
            """, new Dictionary<string, object> { ["dy"] = dy, ["steps"] = steps, ["stepMs"] = stepMs });
    }

    [TestMethod]
    public async Task Demo_390_SheetDrag_SlowDrag_StepsBetweenHalfAndFullSnaps()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);
        var sheet = page.Locator($"{Shell} .tm-editor-shell__sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(sheet).ToHaveAttributeAsync("data-snap-index", "0",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });

        // 13px per 32ms is ~0.4 px/ms: a slow drag, not a flick, so the release settles on the
        // NEAREST snap. Up past the full-snap neighbourhood -> full; back down -> half.
        await DragSheetHandleAsync(page, -280);
        await Assertions.Expect(sheet).ToHaveAttributeAsync("data-snap-index", "1",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });

        await DragSheetHandleAsync(page, 280);
        await Assertions.Expect(sheet).ToHaveAttributeAsync("data-snap-index", "0",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });
        await CaptureViewportOnlyAsync(page, "390-sheet-snap-step");
    }

    [TestMethod]
    public async Task Demo_390_SheetDrag_SecondFingerDoesNotHijackTheDrag()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await GotoEditorShellAsync(context, 390, 844);
        RegisterContext(context);
        var sheet = page.Locator($"{Shell} .tm-editor-shell__sheet");
        var panel = page.Locator($"{Shell} .tm-editor-shell__sheet .tm-drawer__panel");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(sheet).ToHaveAttributeAsync("data-snap-index", "0",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });

        // Finger 1 starts a small (60px) downward drag. Finger 2 lands mid-drag and yanks down
        // 480px — far past the dismiss distance. If the second pointer hijacked the drag, the
        // sheet would collapse onto finger 2's travel instead of finger 1's.
        await page.EvaluateAsync(
            """
            async () => {
                const root = document.querySelector("[data-testid='editor-shell'] .tm-editor-shell__sheet");
                const handle = root.querySelector('.tm-sheet__handle') ?? root.querySelector('.tm-drawer__header');
                const r = handle.getBoundingClientRect();
                const x = r.left + r.width / 2, y = r.top + r.height / 2;
                const fire = (id, primary, type, offset) => handle.dispatchEvent(new PointerEvent(type, {
                    pointerId: id, pointerType: 'touch', isPrimary: primary, button: 0, buttons: 1,
                    clientX: x, clientY: y + offset, bubbles: true, cancelable: true,
                }));
                const wait = ms => new Promise(res => setTimeout(res, ms));
                fire(41, true, 'pointerdown', 0);
                for (let i = 1; i <= 4; i++) { await wait(32); fire(41, true, 'pointermove', 15 * i); }
                fire(42, false, 'pointerdown', 480);
                for (let i = 1; i <= 6; i++) { await wait(32); fire(42, false, 'pointermove', 480 + 20 * i); }
                await wait(32);
                fire(41, true, 'pointermove', 60);
                fire(41, true, 'pointerup', 60);
                fire(42, false, 'pointerup', 600);
            }
            """);
        await page.WaitForTimeoutAsync(400);

        await Assertions.Expect(sheet).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        // The release follows finger 1 (a 60px drag stays on the half snap), not finger 2's 600px
        // yank — a hijack would have collapsed or dismissed the sheet.
        await Assertions.Expect(sheet).ToHaveAttributeAsync("data-snap-index", "0",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });
        Assert.IsFalse(await panel.EvaluateAsync<bool>(
            "el => /(^|;)\\s*height:/.test(el.getAttribute('style') ?? '')"),
            "no inline gesture height may survive the release");
    }
}
