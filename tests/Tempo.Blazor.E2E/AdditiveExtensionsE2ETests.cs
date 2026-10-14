using System.Text.RegularExpressions;
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

    private async Task<IPage> OpenAsync(string route, int width, int height, bool touch, string culture = "en")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = culture == "cs" ? "cs-CZ" : "en-US",
            IgnoreHTTPSErrors = true,
        });
        // The same culture bootstrap OverlayRound3FixE2ETests uses: the demo switcher persists
        // tm-demo-culture, and seeding it here renders the requested culture from the first paint.
        await context.AddInitScriptAsync(
            $"localStorage.setItem('tm-demo-culture', '{culture}'); document.cookie = 'tm-demo-culture={culture};path=/;max-age=31536000;samesite=lax';");
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
    public async Task Shortcuts_ClickInsideTheDialog_DoesNotClose_BackdropClickDoes()
    {
        // F25a: the panel click fence is a real element wrapper — the old tag-level
        // @onclick:stopPropagation on <TmFocusScope> was a runtime no-op and a click anywhere
        // inside the dialog (a text selection, the kbd row) closed it. bUnit cannot see the
        // difference (it never bubbles), so this is the browser proof.
        var page = await OpenAsync("/layout", 1280, 720, touch: false);
        await page.GetByTestId("shortcuts-described-open").ClickAsync();
        var overlay = page.Locator(".tm-keyboard-shortcuts-overlay");
        await Assertions.Expect(overlay).ToBeVisibleAsync();

        // A click INSIDE the panel — the content area — must not dismiss the dialog.
        await page.Locator(".tm-keyboard-shortcuts-content").ClickAsync();
        await Assertions.Expect(overlay).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 2000 });

        // The close button inside the fence still works through its OWN handler.
        await page.Locator(".tm-keyboard-shortcuts-close").ClickAsync();
        await Assertions.Expect(overlay).ToHaveCountAsync(0);

        // Reopen: the backdrop click still dismisses.
        await page.GetByTestId("shortcuts-described-open").ClickAsync();
        await Assertions.Expect(overlay).ToBeVisibleAsync();
        await page.Locator(".tm-keyboard-shortcuts-overlay").ClickAsync(
            new LocatorClickOptions { Position = new Position { X = 8, Y = 8 } });
        await Assertions.Expect(overlay).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task Palette_Groups_RenderASeparator_AndOneKbdPerKey()
    {
        var page = await OpenAsync("/layout", 1280, 720, touch: false);
        await page.GetByTestId("command-palette-grouped-open").ClickAsync();
        var palette = page.Locator(".tm-command-palette");
        await Assertions.Expect(palette).ToBeVisibleAsync();

        // L5: groups are named role=group rows; the divider is a border, not a separator element.
        // `group` is not an allowed role for li, so the group div sits in a role=none row.
        var groups = palette.Locator(".tm-command-palette-list > li > [role='group']");
        Assert.AreEqual(2, await groups.CountAsync(), "two categories produce two groups");
        Assert.AreEqual(0, await palette.Locator("[role='separator'], [role='presentation']").CountAsync(),
            "a listbox may not contain a separator or a presentation row");
        var labels = await palette.Locator(".tm-command-palette-group-label")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
        CollectionAssert.AreEqual(new[] { "File", "Edit" }, labels);

        var first = await palette.Locator(".tm-command-palette-shortcut").First.Locator("kbd")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");
        CollectionAssert.AreEqual(new[] { "Ctrl", "N" }, first, "a shortcut splits into one kbd per key");

        await ShootAsync(page, palette, "palette-grouped-1280");
    }

    [TestMethod]
    public async Task Palette_Grouped_HasNoCriticalOrSeriousAxeViolations()
    {
        // L5: the grouped listbox (role=group labelled by its category) must be axe-clean.
        var page = await OpenAsync("/layout", 1280, 720, touch: false);
        await page.GetByTestId("command-palette-grouped-open").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-command-palette")).ToBeVisibleAsync();

        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js",
        });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector('.tm-command-palette');
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} [${v.nodes.map(n => n.target.join(' ')).join('; ')}]`);
            }
            """);
        Assert.AreEqual(0, violations.Length,
            $"the grouped palette must have no critical/serious violations: {string.Join(" | ", violations)}");
    }

    [TestMethod]
    public async Task Palette_BackdropClick_Closes_AndPanelClickDoesNot()
    {
        // cluster-B §4: the backdrop is a click-to-dismiss surface (no opt-out parameter, matching
        // TmModal and TmKeyboardShortcutsHelp); a click inside the panel must not dismiss it. The
        // dismissal requires the PRESS to start on the backdrop: a drag-select that begins on the
        // input and ends over the backdrop fires only the click there, so the palette stays open.
        var page = await OpenAsync("/layout", 1280, 720, touch: false);
        await page.GetByTestId("open-command-palette").ClickAsync();
        var backdrop = page.Locator(".tm-command-palette-backdrop");
        await Assertions.Expect(backdrop).ToBeVisibleAsync();

        await page.Locator(".tm-command-palette-input").ClickAsync();
        await Assertions.Expect(backdrop).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 2000 });

        // Drag-select: pointerdown on the input, pointerup over the backdrop. Only the click lands
        // on the backdrop, so the palette must still be there afterwards.
        var input = page.Locator(".tm-command-palette-input");
        var box = await input.BoundingBoxAsync();
        Assert.IsNotNull(box, "the palette input must be on screen for the drag");
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(10, 700, new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();
        await Assertions.Expect(backdrop).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 2000 });

        // A press that starts ON the backdrop still dismisses, and focus returns to the trigger
        // that opened the palette (the trap's restore target).
        await page.Mouse.ClickAsync(10, 700);
        await Assertions.Expect(backdrop).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("open-command-palette")).ToBeFocusedAsync();
    }

    [TestMethod]
    public async Task Palette_Default_IsAxeClean_ForScrollableRegionAndAria()
    {
        // ec0299b9 #2: the default palette's listbox scrolls (430 > 384px on /layout), is not
        // focusable and contains no focusables — axe calls that scrollable-region-focusable (serious)
        // unless a role=combobox names it through aria-controls. f4057a47 adds the combobox wiring;
        // ec0299b9 #1 moves the groups into allowed roles. All five rules must come back empty.
        var page = await OpenAsync("/layout", 1280, 720, touch: false);
        await page.GetByTestId("open-command-palette").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-command-palette")).ToBeVisibleAsync();

        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js",
        });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector('.tm-command-palette-backdrop');
                const result = await axe.run(host, {
                    runOnly: {
                        type: 'rule',
                        values: ['scrollable-region-focusable', 'aria-allowed-role',
                                 'aria-required-children', 'aria-required-parent', 'aria-valid-attr-value']
                    },
                    resultTypes: ['violations']
                });
                return result.violations
                    .map(v => `${v.impact}: ${v.id} - ${v.help} [${v.nodes.map(n => n.target.join(' ')).join('; ')}]`);
            }
            """);
        Assert.AreEqual(0, violations.Length,
            $"the default palette must be clean for the combobox/listbox rules: {string.Join(" | ", violations)}");
    }

    [TestMethod]
    [DataRow(1440, 900, false)]
    [DataRow(390, 844, true)]
    public async Task Palette_DarkHighlight_IsVisible(int width, int height, bool touch)
    {
        // f4057a47: in dark mode the focused row's old gray-50 fill WAS the surface (1:1). The
        // highlight is now the muted fill plus a primary outline, and the ≥3:1 cue (WCAG 1.4.11)
        // is measured on the real computed colors, not on the token values.
        var page = await OpenAsync("/layout", width, height, touch);
        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
            }
            """);
        await page.GetByTestId("open-command-palette").ClickAsync();
        var palette = page.Locator(".tm-command-palette");
        await Assertions.Expect(palette).ToBeVisibleAsync();

        // Guard the theme itself: the surface must be the dark paint (#1e293b), otherwise the
        // 3:1 assertion below could quietly pass on the light pair.
        var surface = await palette.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreEqual("rgb(30, 41, 59)", surface,
            "the palette must render the dark surface for this check to mean anything");

        await Assertions.Expect(palette.Locator(".tm-command-palette-item-focused")).ToHaveCountAsync(1);

        var contrast = await page.EvaluateAsync<double[]>(
            """
            () => {
                const parse = css => {
                    const m = css.match(/rgba?\(([^)]+)\)/);
                    if (!m) return null;
                    const p = m[1].split(',').map(s => parseFloat(s));
                    return { r: p[0] / 255, g: p[1] / 255, b: p[2] / 255, a: p.length > 3 ? p[3] : 1 };
                };
                const lum = c => {
                    const f = v => v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
                    return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
                };
                const ratio = (a, b) => {
                    const la = lum(a), lb = lum(b);
                    return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
                };
                const row = document.querySelector('.tm-command-palette-item-focused');
                const panel = document.querySelector('.tm-command-palette');
                const outline = parse(getComputedStyle(row).outlineColor);
                return [
                    // The row is painted on the panel, so the outline must also hold against the
                    // panel surface — and against the row's OWN muted fill, which is what the eye
                    // actually sees the outline drawn over.
                    ratio(outline, parse(getComputedStyle(panel).backgroundColor)),
                    ratio(outline, parse(getComputedStyle(row).backgroundColor)),
                ];
            }
            """);
        Assert.IsTrue(contrast[0] >= 3.0,
            $"the focused row's outline must hold 3:1 against the dark surface, got {contrast[0]:0.00}:1");
        Assert.IsTrue(contrast[1] >= 3.0,
            $"the focused row's outline must hold 3:1 against the row's own dark fill, got {contrast[1]:0.00}:1");

        // The combobox tracks the highlight: ArrowDown moves aria-activedescendant onto the row
        // that carries the focused class.
        await page.Keyboard.PressAsync("ArrowDown");
        var ids = await page.EvaluateAsync<string[]>(
            """
            () => {
                const input = document.querySelector('.tm-command-palette-input');
                const focused = document.querySelector('.tm-command-palette-item-focused');
                return [input.getAttribute('aria-activedescendant'), focused ? focused.id : ''];
            }
            """);
        Assert.IsFalse(string.IsNullOrEmpty(ids[1]), "the focused row must carry a stable id");
        Assert.AreEqual(ids[1], ids[0], "aria-activedescendant must name the highlighted option");

        await ShootAsync(page, palette, $"palette-dark-highlight-{width}");
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
        // Both states are rendered so the swap reserves its width; the inactive one is
        // visibility:hidden, not sr-only, so the visible state is the one without --hidden.
        await Assertions.Expect(button.Locator(".tm-copy-button-label:not(.tm-copy-button-label--hidden)")).ToHaveTextAsync("Copy");

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
        await Assertions.Expect(button.Locator(".tm-copy-button-label:not(.tm-copy-button-label--hidden)")).ToHaveTextAsync("Copied");
        await Assertions.Expect(button.Locator("[role='status']")).ToHaveTextAsync("Copied");
        await ShootAsync(page, button, $"copy-button-labelled-{width}");
    }

    [TestMethod]
    [DataRow(1280, 720, false, "en")]
    [DataRow(390, 844, true, "en")]
    [DataRow(320, 700, true, "cs")]
    public async Task CopyButton_ShowLabel_KeepsItsWidth_AcrossCopyAndCopied(int width, int height, bool touch, string culture)
    {
        // A11: the labelled button stacks BOTH labels in one grid cell, and the inactive one is
        // visibility:hidden — so the wider of Copy/Copied always reserves the width and the button
        // must not jump across Copy → Copied → Copy. "Kopírovat"/"Zkopírováno" (cs) differ more
        // than the English pair and are therefore the sharper second culture.
        var page = await OpenAsync("/feedback", width, height, touch, culture);
        var button = page.GetByTestId("copy-button-labelled");
        await button.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(button).ToBeVisibleAsync();
        await page.EvaluateAsync("() => document.fonts.ready");
        var idle = await ButtonBoxAsync(button);

        // The copy flips the label only after the clipboard write resolves; stub the write so the
        // assertion is about the layout.
        await page.EvaluateAsync("() => { navigator.clipboard.writeText = () => Promise.resolve(); }");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveClassAsync(new Regex("tm-copy-button--copied"));
        var copied = await ButtonBoxAsync(button);
        Assert.AreEqual(idle.Width, copied.Width, 0.5,
            $"Copy→Copied must not change the button width ({idle.Width} → {copied.Width})");
        Assert.AreEqual(idle.Height, copied.Height, 0.5, "nor its height");

        // The 2 s reset back to Copy keeps it too.
        await Assertions.Expect(button).Not.ToHaveClassAsync(new Regex("tm-copy-button--copied"), new() { Timeout = 5000 });
        Assert.AreEqual(idle.Width, (await ButtonBoxAsync(button)).Width, 0.5,
            "Copied→Copy must not change the width either");
    }

    [TestMethod]
    public async Task CopyButton_WidthAssertion_HasTeeth_WhenTheHiddenLabelStopsReservingSpace()
    {
        // The negative control for the width guard: display:none on the hidden label removes the
        // width reservation, the button genuinely jumps, and the equality assertions of the guard
        // above MUST be able to fail — this proves the guard still detects the regression it
        // exists for (a pair of assertions that can never go red prove nothing).
        var page = await OpenAsync("/feedback", 320, 700, touch: true);
        var button = page.GetByTestId("copy-button-labelled");
        await button.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(button).ToBeVisibleAsync();
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = ".tm-copy-button-label--hidden{display:none!important}",
        });

        var idle = await ButtonBoxAsync(button);
        await page.EvaluateAsync("() => { navigator.clipboard.writeText = () => Promise.resolve(); }");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveClassAsync(new Regex("tm-copy-button--copied"));
        var copied = await ButtonBoxAsync(button);
        Assert.IsTrue(Math.Abs(idle.Width - copied.Width) > 1,
            $"teeth check: with the hidden label display:none the width must actually change ({idle.Width} → {copied.Width})");
    }

    private static async Task<LocatorBoundingBoxResult> ButtonBoxAsync(ILocator button)
    {
        var box = await button.BoundingBoxAsync();
        Assert.IsNotNull(box, "the labelled copy button must paint");
        return box!;
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

        // cluster-D §6: the overlay clear is a compact icon-only tile below 640px of the
        // component's own width, so it stops covering ~14% of the pad. The accessible name
        // survives the visually-hidden label at every width.
        var canvas = await capture.Locator(".tm-signature-capture__canvas").BoundingBoxAsync();
        Assert.IsNotNull(canvas);
        var ratio = button.Width * button.Height / (canvas!.Width * canvas!.Height);
        var ratioCeiling = width <= 320 ? 0.10 : 0.07;
        Assert.IsTrue(ratio <= ratioCeiling,
            $"the overlay clear covers {ratio:P1} of the pad at {width}px (ceiling {ratioCeiling:P0})");

        var accessible = capture.GetByRole(AriaRole.Button, new() { Name = "Clear" });
        await Assertions.Expect(accessible).ToHaveCountAsync(1);

        var label = capture.Locator(".tm-signature-capture__clear-label");
        // The compact break is the COMPONENT's own width — the overlay root is the size
        // container — not the viewport width: the demo's xl two-column grid keeps this capture
        // at ~430px even on a 1280 screen, so a wide viewport still renders the compact tile.
        // Branch on the measured container so the assertion follows the spec (≥640px of
        // container shows icon + label) at every viewport.
        var containerWidth = await capture.EvaluateAsync<double>("el => el.getBoundingClientRect().width");
        if (containerWidth >= 640)
        {
            var labelWidth = await label.EvaluateAsync<double>("el => el.clientWidth");
            Assert.IsTrue(labelWidth > 1, "at a wide container the label is real visible text");
        }
        else
        {
            var clipped = await label.EvaluateAsync<bool>(
                "el => el.clientHeight <= 1 && getComputedStyle(el).overflow === 'hidden'");
            Assert.IsTrue(clipped, "below 640px of container the label is visually hidden, not removed");
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
