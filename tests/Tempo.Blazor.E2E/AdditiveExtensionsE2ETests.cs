using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F8 lane: the additive core extensions on the REAL components the demo pages render
/// (TmKeyboardShortcutsHelp, TmCommandPalette, TmCopyButton, TmRadioGroup, TmSignatureCapture) at
/// 1280, 390 and 320 — the phone sizes with touch emulation. Checks are computed values, never markup
/// alone: the subtitle, the per-key kbd, the group separator, the labelled copy button, the segmented
/// radiogroup and the overlay clear button's 44px touch target. Every screenshot is viewport-only at
/// the real viewport height (FullPage resets touch emulation).
/// </summary>
[TestClass]
public class AdditiveExtensionsE2ETests : WasmTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "additive-extensions");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private async Task<IPage> OpenAsync(string route, int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{route}");
        await WaitForAppReadyAsync(page);
        return page;
    }

    private async Task<string> ShootAsync(IPage page, ILocator subject, string name)
    {
        await subject.EvaluateAsync("el => { el.scrollIntoView({ block: 'start', behavior: 'instant' }); window.scrollBy({ top: -80, behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        var coarse = await page.EvaluateAsync<bool>("() => window.matchMedia('(pointer: coarse)').matches");
        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        // FullPage resets touch emulation; a viewport-only shot must not, so re-assert after it.
        Assert.AreEqual(coarse, await page.EvaluateAsync<bool>("() => window.matchMedia('(pointer: coarse)').matches"),
            $"{name}: the screenshot must not drop touch emulation");
        return path;
    }

    [TestMethod]
    public async Task Shortcuts_Description_LabelsTheDialog_AndKeepsAlternativeKeysTogether()
    {
        var page = await OpenAsync("/layout", 1280, 720, touch: false);
        await page.GetByTestId("shortcuts-described-open").ClickAsync();
        // The dialog is a TmFocusScope root; the panel lives inside it.
        var dialog = page.Locator(".tm-focus-scope").Filter(new() { Has = page.Locator(".tm-keyboard-shortcuts-title") });
        await Assertions.Expect(dialog).ToBeVisibleAsync();

        var subtitle = dialog.Locator(".tm-keyboard-shortcuts-subtitle");
        await Assertions.Expect(subtitle).ToHaveTextAsync("Shortcuts work with an external keyboard");
        var labelledBy = await dialog.GetAttributeAsync("aria-labelledby");
        Assert.IsFalse(string.IsNullOrEmpty(labelledBy), "the dialog must be labelled by its title");
        Assert.AreEqual(labelledBy, await dialog.Locator(".tm-keyboard-shortcuts-title").GetAttributeAsync("id"));

        var keys = await dialog.Locator(".tm-keyboard-shortcuts-key kbd")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
        CollectionAssert.AreEqual(new[] { "Arrow Up", "Arrow Down", "Esc" }, keys,
            "the alternative separator must not split the pair into a chord, and a lone key stays one kbd");

        // L1: the alternative pair reads as "Arrow Up or Arrow Down", not as a chord of two keys.
        var joiner = dialog.Locator(".tm-keyboard-shortcuts-or");
        await Assertions.Expect(joiner).ToHaveCountAsync(1);
        await Assertions.Expect(joiner).ToHaveTextAsync("or");

        await ShootAsync(page, dialog, "shortcuts-described-1280");
    }

    [TestMethod]
    public async Task Palette_Groups_RenderASeparator_AndOneKbdPerKey()
    {
        var page = await OpenAsync("/layout", 1280, 720, touch: false);
        await page.GetByTestId("command-palette-grouped-open").ClickAsync();
        var palette = page.Locator(".tm-command-palette");
        await Assertions.Expect(palette).ToBeVisibleAsync();

        Assert.AreEqual(1, await palette.Locator(".tm-command-palette-separator").CountAsync(),
            "two categories produce exactly one separator");
        var labels = await palette.Locator(".tm-command-palette-group-label")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
        CollectionAssert.AreEqual(new[] { "File", "Edit" }, labels);

        var first = await palette.Locator(".tm-command-palette-shortcut").First.Locator("kbd")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
        CollectionAssert.AreEqual(new[] { "Ctrl", "N" }, first, "a shortcut splits into one kbd per key");

        await ShootAsync(page, palette, "palette-grouped-1280");
    }

    [TestMethod]
    [DataRow(1280, 720, false)]
    [DataRow(390, 844, true)]
    [DataRow(320, 700, true)]
    public async Task CopyButton_ShowLabel_RendersTheLabel_AndKeepsATouchTarget(int width, int height, bool touch)
    {
        var page = await OpenAsync("/feedback", width, height, touch);
        var button = page.GetByTestId("copy-button-labelled");
        await button.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(button).ToBeVisibleAsync();
        await Assertions.Expect(button.Locator(".tm-copy-button-label")).ToHaveTextAsync("Copy");

        var box = await button.BoundingBoxAsync();
        Assert.IsNotNull(box, "the labelled button must paint");
        if (touch)
        {
            Assert.IsTrue(box!.Height >= 44, $"a coarse pointer needs a 44px target, got {box.Height:0.#}");
        }

        // The copy flips the label only after the clipboard write resolves, and a browser grants
        // clipboard access in a secure context. Stub the write so the assertion is about the label.
        await page.EvaluateAsync("() => { navigator.clipboard.writeText = () => Promise.resolve(); }");
        await button.ClickAsync();
        await Assertions.Expect(button.Locator(".tm-copy-button-label")).ToHaveTextAsync("Copied");
        await ShootAsync(page, button, $"copy-button-labelled-{width}");
    }

    [TestMethod]
    [DataRow(1280, 720, false)]
    [DataRow(390, 844, true)]
    [DataRow(320, 700, true)]
    public async Task RadioGroup_Segmented_PaintsAPressedSegment(int width, int height, bool touch)
    {
        var page = await OpenAsync("/forms", width, height, touch);
        var group = page.GetByTestId("radio-segmented-demo").Locator(".tm-radio-group--segmented");
        await group.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(group).ToBeVisibleAsync();
        Assert.AreEqual("radiogroup", await group.GetAttributeAsync("role"));

        var segments = group.Locator(".tm-radio-segment");
        Assert.AreEqual(3, await segments.CountAsync());
        // L2: the pressed segment is a native radio, so the group exposes one checked input.
        var pressed = group.Locator(".tm-radio-segment:has(input:checked)");
        await Assertions.Expect(pressed).ToHaveCountAsync(1);

        var box = await segments.First.BoundingBoxAsync();
        Assert.IsNotNull(box);
        if (touch)
        {
            Assert.IsTrue(box!.Height >= 44, $"a segment needs a 44px touch target, got {box.Height:0.#}");
        }

        await ShootAsync(page, group, $"radio-segmented-{width}");
    }

    [TestMethod]
    public async Task RadioGroup_Segmented_KeyboardSelectsWithArrowsAndHomeEnd()
    {
        // L2: Tab lands once, ArrowRight selects the next segment, Home/End jump to the ends.
        var page = await OpenAsync("/forms", 1280, 720, touch: false);
        var group = page.GetByTestId("radio-segmented-demo").Locator(".tm-radio-group--segmented");
        await group.ScrollIntoViewIfNeededAsync();

        var radios = group.Locator("input[type='radio']");
        Assert.AreEqual(3, await radios.CountAsync(), "each segment is a native radio");
        var names = await radios.EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('name'))");
        Assert.IsTrue(names.All(n => n == names[0] && !string.IsNullOrEmpty(n)),
            "the segments share one form name");

        // One tab stop: Tab into the group lands on the checked radio, and the next Tab leaves it.
        await radios.First.FocusAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.PressAsync("Tab");
        Assert.AreEqual(true, await radios.First.EvaluateAsync<bool>("e => e === document.activeElement"),
            "Tab enters the group once, on the checked radio");
        await page.Keyboard.PressAsync("Tab");
        Assert.AreEqual(false, await group.EvaluateAsync<bool>("e => e.contains(document.activeElement)"),
            "the next Tab leaves the group");

        await radios.First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(radios.Nth(1)).ToBeCheckedAsync();
        Assert.AreEqual(true, await radios.Nth(1).EvaluateAsync<bool>("e => e === document.activeElement"),
            "ArrowRight moves the selection and the focus together");

        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(radios.Nth(2)).ToBeCheckedAsync();
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(radios.First).ToBeCheckedAsync();
    }

    [TestMethod]
    [DataRow(1280, 720, false)]
    [DataRow(390, 844, true)]
    [DataRow(320, 700, true)]
    public async Task Signature_OverlayClear_SitsInTheSurfaceCorner_WithATouchTarget(int width, int height, bool touch)
    {
        var page = await OpenAsync("/signing-components", width, height, touch);
        var capture = page.GetByTestId("signature-overlay-clear");
        await capture.ScrollIntoViewIfNeededAsync();
        var clear = capture.Locator(".tm-signature-capture__clear");
        await Assertions.Expect(clear).ToBeVisibleAsync();
        Assert.AreEqual(0, await capture.Locator(".tm-signature-capture__actions .tm-signature-capture__clear").CountAsync(),
            "overlay placement moves the button out of the actions row");

        var surface = await capture.Locator(".tm-signature-capture__surface").BoundingBoxAsync();
        var button = await clear.BoundingBoxAsync();
        Assert.IsNotNull(surface);
        Assert.IsNotNull(button);
        Assert.IsTrue(button!.Y < surface!.Y + 48, "the clear button sits in the top band of the surface");
        Assert.IsTrue(button.X + button.Width > surface.X + surface.Width - 80, "the clear button sits at the end edge");
        if (touch)
        {
            Assert.IsTrue(button.Height >= 44, $"the overlay clear needs a 44px touch target, got {button.Height:0.#}");
        }

        await ShootAsync(page, capture, $"signature-overlay-clear-{width}");
    }

    [TestMethod]
    [DataRow(1440, 900)]
    [DataRow(390, 844)]
    [DataRow(320, 700)]
    public async Task Signature_OverlayClear_LeavesTheInputClear_InTypeAndUpload(int width, int height)
    {
        // L4: overlay placement covers the typed input and the upload drop zone, so those modes
        // fall back to the actions row. The clear button must not intersect either control.
        var page = await OpenAsync("/signing-components", width, height, touch: false);
        var capture = page.GetByTestId("signature-overlay-clear");
        await capture.ScrollIntoViewIfNeededAsync();

        foreach (var (tab, target) in new (string Tab, string Target)[]
        {
            ("Type", ".tm-signature-capture__typed-input"),
            ("Upload", ".tm-signature-capture__drop-zone"),
        })
        {
            await capture.Locator(".tm-signature-capture__tab").Filter(new() { HasText = tab }).ClickAsync();
            var clear = await capture.Locator(".tm-signature-capture__clear").BoundingBoxAsync();
            var control = await capture.Locator(target).BoundingBoxAsync();
            Assert.IsNotNull(clear, $"the clear button is visible in {tab} mode");
            Assert.IsNotNull(control, $"the {tab} control is visible");
            var overlaps = clear!.X < control!.X + control.Width && clear.X + clear.Width > control.X
                && clear.Y < control.Y + control.Height && clear.Y + clear.Height > control.Y;
            Assert.IsFalse(overlaps, $"the clear button covers the {tab} control at {width}px");
        }
    }
}
