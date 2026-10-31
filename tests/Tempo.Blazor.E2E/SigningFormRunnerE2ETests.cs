using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SigningFormRunnerE2ETests : WasmTestBase
{
    [TestMethod]
[Description("Signing form runner completes the required text, delivery, number, conditional note, and signature flow")]
    public async Task SigningFormRunner_CompletesRequiredFlow()
    {
        var page = await OpenRunnerAsync();
        var runner = GetRunner(page);
        var panel = GetDesktopPanel(runner);

        await panel.Locator("input.tm-signing-text-step__input").FillAsync("Alex Johnson");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Delivery method");
        await panel.Locator(".tm-signing-choice-step__select select").SelectOptionAsync("paper");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await panel.Locator("input.tm-signing-number-step__input").FillAsync("100");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await panel.Locator(".tm-signing-choice-step .tm-checkbox-label").First.ClickAsync();
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Internal note");
        await panel.Locator("input.tm-signing-text-step__input").FillAsync("Board approved");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Signing date");
        await panel.Locator("input.tm-signing-date-step__input").FillAsync("2026-05-08");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        var signatureInput = panel.Locator("input.tm-signature-capture__typed-input");
        await signatureInput.FillAsync("Alex Johnson");
        await signatureInput.PressAsync("Tab");
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__complete")).ToBeEnabledAsync();
        await panel.Locator(".tm-signing-form-runner__complete").ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid='signing-runner-status']")).ToContainTextAsync("Completed");
    }

    [TestMethod]
    [Description("Signing form runner keeps the signer on the first invalid required step")]
    public async Task SigningFormRunner_BlocksEmptyRequiredStep()
    {
        var page = await OpenRunnerAsync();
        var panel = GetDesktopPanel(GetRunner(page));

        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        // Phase 2: the step validation surfaces in the step shell (one alert per step).
        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__validation")).ToContainTextAsync("Full name is required.");
        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Full name");
    }

    [TestMethod]
    [Description("Signing form runner updates formula overlays as dependent numeric values change")]
    public async Task SigningFormRunner_UpdatesFormulaOverlay()
    {
        var page = await OpenRunnerAsync();
        var runner = GetRunner(page);
        var panel = GetDesktopPanel(runner);

        await panel.Locator("input.tm-signing-text-step__input").FillAsync("Alex Johnson");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();
        await panel.Locator(".tm-signing-choice-step__select select").SelectOptionAsync("paper");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();
        await panel.Locator("input.tm-signing-number-step__input").FillAsync("100");
        await panel.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(runner.Locator(".tm-signing-field:has-text('Total with tax')")).ToContainTextAsync("121");
        await Assertions.Expect(page.Locator("[data-testid='signing-runner-status']")).ToContainTextAsync("total: 121");
    }

    [TestMethod]
    [Description("Signing form runner field list can jump directly to a selected step")]
    public async Task SigningFormRunner_AccessibilityListJumpsToStep()
    {
        var page = await OpenRunnerAsync();
        var panel = GetDesktopPanel(GetRunner(page));

        // Phase 3: the field list is permanent (U2 — the "Accessibility mode" toggle is gone);
        // the list row itself is the jump affordance.
        await panel.Locator(".tm-signing-form-runner__field-button:has-text('Signature')").ClickAsync();

        await Assertions.Expect(panel.Locator("input.tm-signature-capture__typed-input")).ToBeVisibleAsync();
    }

    [TestMethod]
    [Description("Signing form runner keeps draw signature mode selected after completing a stroke")]
    public async Task SigningFormRunner_DrawSignatureModePersistsAfterMouseUp()
    {
        var page = await OpenRunnerAsync();
        var panel = GetDesktopPanel(GetRunner(page));

        await panel.Locator(".tm-signing-form-runner__field-button:has-text('Signature')").ClickAsync();
        await panel.GetByRole(AriaRole.Tab, new() { Name = "Draw" }).ClickAsync();

        var signatureCapture = panel.Locator(".tm-signature-capture").First;
        await Assertions.Expect(signatureCapture).ToHaveAttributeAsync("data-mode", "Draw");
        await DrawSignatureAsync(page, signatureCapture);

        await Assertions.Expect(signatureCapture).ToHaveAttributeAsync("data-mode", "Draw");
        await Assertions.Expect(panel.GetByRole(AriaRole.Tab, new() { Name = "Draw" })).ToHaveAttributeAsync("aria-selected", "true");
    }

    [TestMethod]
    [Description("Signing form runner exposes a collapsible mobile signing panel")]
    public async Task SigningFormRunner_MobilePanelCollapsesAndExpands()
    {
        var page = await OpenRunnerAsync(390, 820);
        var runner = GetRunner(page);
        var steps = GetStepsRail(runner);

        // Phase 5 review: the HANDLE is the sheet's only toggle (the phase-2 Minimize row is
        // gone) — collapsing hides the panel; the peek keeps progress + the "Sign now" CTA.
        await steps.Locator(".tm-signing-form-runner__sheet-handle").ClickAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__mobile-expand")).ToBeVisibleAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__progress")).Not.ToBeVisibleAsync();

        await steps.Locator(".tm-signing-form-runner__mobile-expand").ClickAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__progress")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Expanding the mobile sheet with the keyboard moves focus onto the HANDLE — the one
    /// affordance visible in every sheet state (phase 5 review; the expand CTA hides with the
    /// state flip, the old minimize button is gone).
    /// </summary>
    [TestMethod]
    [Description("After expanding the mobile sheet with Enter on 'Sign now', focus lands on the handle")]
    public async Task SigningFormRunner_ExpandingSheetKeepsFocusOnTheVisibleAffordance()
    {
        var page = await OpenRunnerAsync(390, 820);
        var runner = GetRunner(page);
        var steps = GetStepsRail(runner);

        // Collapse first: the sheet starts expanded at 390, so "Sign now" becomes visible.
        await steps.Locator(".tm-signing-form-runner__sheet-handle").ClickAsync();
        await Assertions.Expect(steps.Locator(".tm-signing-form-runner__mobile-expand")).ToBeVisibleAsync();

        var expand = steps.Locator(".tm-signing-form-runner__mobile-expand").First;
        await expand.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var activeClass = await page.EvaluateAsync<string>("() => document.activeElement?.className?.toString() ?? ''");
        StringAssert.Contains(activeClass, "tm-signing-form-runner__sheet-handle",
            $"expanding the sheet must move focus onto the handle (visible in every state), got '{activeClass}'");
    }

    /// <summary>
    /// B25 (Signing redesign phase 2): the step panel exists EXACTLY ONCE in the DOM at every
    /// width — the column vs. mobile-sheet presentation is CSS-only. Before the fix the mobile
    /// panel was a second rendered copy, so every input and alert existed twice for assistive
    /// technology.
    /// </summary>
    [TestMethod]
    [Description("The step panel is rendered once in the DOM at 390px and at 1440px")]
    public async Task SigningFormRunner_SinglePanelInstanceInDom_AtMobileAndDesktop()
    {
        foreach (var (width, height) in new[] { (390, 820), (1440, 900) })
        {
            var page = await OpenRunnerAsync(width, height);
            var runner = GetRunner(page);
            var steps = GetStepsRail(runner);

            await Assertions.Expect(steps.Locator(".tm-signing-step-shell__title").First).ToBeVisibleAsync();

            var panelCount = await runner.Locator(".tm-signing-form-runner__step-panel").CountAsync();
            Assert.AreEqual(1, panelCount, $"at {width}px the step panel must exist once (found {panelCount})");
            var progressCount = await runner.Locator(".tm-signing-form-runner__progress").CountAsync();
            Assert.AreEqual(1, progressCount, $"at {width}px the progress region must exist once (found {progressCount})");
            var nextCount = await runner.Locator(".tm-signing-form-runner__next").CountAsync();
            Assert.AreEqual(1, nextCount, $"at {width}px the action row must exist once (found {nextCount})");
            var shellCount = await runner.Locator(".tm-signing-step-shell").CountAsync();
            Assert.AreEqual(1, shellCount, $"at {width}px the step shell must exist once (found {shellCount})");

            await page.Context.CloseAsync();
        }
    }

    /// <summary>Every runner input is reachable through its accessible name (getByLabel).</summary>
    [TestMethod]
    [Description("Runner inputs resolve their accessible names (getByLabel) while the signer walks the steps")]
    public async Task SigningFormRunner_InputsAreReachableByLabel()
    {
        var page = await OpenRunnerAsync();
        var runner = GetRunner(page);
        var steps = GetStepsRail(runner);

        // Text step: the field label names the input.
        await steps.GetByLabel("Full name").FillAsync("Alex Johnson");
        await steps.Locator(".tm-signing-form-runner__next").ClickAsync();

        // Select step.
        await steps.GetByLabel("Delivery method").SelectOptionAsync("paper");
        await steps.Locator(".tm-signing-form-runner__next").ClickAsync();

        // Number step.
        await steps.GetByLabel("Amount without tax").FillAsync("100");
        await steps.Locator(".tm-signing-form-runner__next").ClickAsync();

        await Assertions.Expect(steps.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Add internal note");
    }

    /// <summary>
    /// Signing redesign phase 3: the permanent field list drives the ceremony — the signer can walk
    /// the steps in any order through the list rows and still finish with the banner + Complete.
    /// </summary>
    [TestMethod]
    [Description("Signing form runner completes the flow driven through the permanent field list")]
    public async Task SigningFormRunner_CompletesFlowThroughFieldList()
    {
        var page = await OpenRunnerAsync();
        var panel = GetDesktopPanel(GetRunner(page));

        // The list shows every step with its status; the first row is the active one. The demo
        // plan starts with 6 steps — the conditional "Internal note" joins once its toggle is
        // checked (the readonly formula field never becomes a step).
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__field-button")).ToHaveCountAsync(6, new() { Timeout = 10_000 });
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__field-button[aria-current='step']")).ToContainTextAsync("Full name");

        // Jump straight to the signature step through the list and fill it.
        await panel.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-signature']").ClickAsync();
        await panel.Locator("input.tm-signature-capture__typed-input").FillAsync("Alex Johnson");
        await panel.Locator("input.tm-signature-capture__typed-input").PressAsync("Tab");

        // The completed row reads Done and the progress counts FIELDS, not the step index.
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-signature'] .tm-signing-form-runner__field-status")).ToContainTextAsync("Done");
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__progress-text")).ToContainTextAsync("1 of 6 done");
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__all-done")).Not.ToBeVisibleAsync();

        // Walk the remaining steps through the list.
        await panel.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-name']").ClickAsync();
        await panel.Locator("input.tm-signing-text-step__input").FillAsync("Alex Johnson");
        await panel.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-delivery']").ClickAsync();
        await panel.Locator(".tm-signing-choice-step__select select").SelectOptionAsync("paper");
        await panel.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-amount']").ClickAsync();
        await panel.Locator("input.tm-signing-number-step__input").FillAsync("100");
        // The number input commits on change (blur) — Tab so the progress count below sees it.
        await panel.Locator("input.tm-signing-number-step__input").PressAsync("Tab");

        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__progress-text")).ToContainTextAsync("4 of 6 done");

        // The toggle reveals the conditional note (the list grows to 7 rows).
        await panel.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-extra-toggle']").ClickAsync();
        await panel.Locator(".tm-signing-choice-step .tm-checkbox-label").First.ClickAsync();
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__field-button")).ToHaveCountAsync(7);

        await panel.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-extra-note']").ClickAsync();
        await panel.Locator("input.tm-signing-text-step__input").FillAsync("Board approved");
        await panel.Locator(".tm-signing-form-runner__field-button[data-field-uuid='runner-date']").ClickAsync();
        await panel.Locator("input.tm-signing-date-step__input").FillAsync("2026-05-08");

        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__all-done")).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__progress-text")).ToContainTextAsync("7 of 7 done");
        await Assertions.Expect(panel.Locator(".tm-signing-form-runner__complete")).ToBeEnabledAsync();
        await panel.Locator(".tm-signing-form-runner__complete").ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid='signing-runner-status']")).ToContainTextAsync("Completed");
    }

    /// <summary>
    /// Signing redesign phase 3 review: on a desktop container the step panel caps at the viewport
    /// height and the action footer sticks to its bottom, so the primary action is reachable
    /// WITHOUT scrolling — the Next button must sit inside the 1440×900 viewport while the signer
    /// works through the panel.
    /// </summary>
    [TestMethod]
    [Description("At 1440x900 the sticky action footer keeps the Next button inside the viewport")]
    public async Task SigningFormRunner_NextActionStaysWithinViewport_AtDesktop()
    {
        var page = await OpenRunnerAsync(1440, 900);
        var runner = GetRunner(page);
        var panel = GetDesktopPanel(runner);

        // Land mid-panel the way a working signer would be: the runner scrolled into view is not
        // enough — scroll PAST its top so the sticky panel is what holds the footer.
        await runner.ScrollIntoViewIfNeededAsync();
        await page.Mouse.WheelAsync(0, 400);

        var next = panel.Locator(".tm-signing-form-runner__next").First;
        await Assertions.Expect(next).ToBeVisibleAsync();

        var box = await next.BoundingBoxAsync();
        Assert.IsNotNull(box, "the Next button must have a bounding box");
        Assert.IsTrue(box.Y >= 0, $"the Next button must not be above the viewport (y={box.Y})");
        Assert.IsTrue(box.Y + box.Height <= 900,
            $"the Next button must sit inside the 900px viewport (bottom={box.Y + box.Height})");
        Assert.IsTrue(box.X >= 0 && box.X + box.Width <= 1440,
            $"the Next button must sit inside the 1440px viewport (x={box.X}, width={box.Width})");
    }

    /// <summary>
    /// Signing redesign phase 3 review: the field overlay root must NOT be a CSS size container —
    /// root inline-size containment made every choice chip wrap letter-by-letter. Each rendered
    /// option keeps its laid-out text width: the bounding box is never narrower than the content
    /// (scrollWidth).
    /// </summary>
    [TestMethod]
    [Description("Choice option chips keep their text width (no letter-by-letter wrapping)")]
    public async Task SigningFormRunner_ChoiceOptionChips_KeepTheirTextWidth()
    {
        var page = await OpenRunnerAsync();
        var runner = GetRunner(page);

        var wrapped = await runner.Locator(".tm-signing-field__option").EvaluateAllAsync<string[]>(
            """
            elements => elements
                .filter(element => {
                    const box = element.getBoundingClientRect();
                    return box.width + 1 < element.scrollWidth;
                })
                .map(element => (element.textContent || '').trim())
            """);

        Assert.AreEqual(
            0,
            wrapped.Length,
            $"option chips narrower than their text (wrapped letter-by-letter): {string.Join(", ", wrapped)}");
    }

    /// <summary>
    /// Signing redesign phase 3 review (dark-mode contrast): the runner document wrapper is a LIGHT
    /// ISLAND (<c>data-theme="light"</c>), so the overlay prompt keeps light-theme ink on the white
    /// paper inside a dark host — measured WCAG AA (≥ 4.5:1).
    /// </summary>
    [TestMethod]
    [Description("In dark mode the overlay prompt on the light-island paper keeps 4.5:1 contrast")]
    public async Task SigningFormRunner_DarkMode_OverlayPromptKeepsContrast()
    {
        var page = await OpenRunnerAsync();
        var runner = GetRunner(page);

        // Force dark on the root and every [data-theme] host EXCEPT the runner's light island —
        // the white paper sheet must keep its light tokens.
        await page.EvaluateAsync(
            "() => {" +
            " document.documentElement.dataset.theme = 'dark';" +
            " document.querySelectorAll('[data-theme]').forEach(el => {" +
            "   if (!el.classList.contains('tm-signing-form-runner__document')) { el.dataset.theme = 'dark'; } }); }");
        await page.WaitForTimeoutAsync(150);

        var ratios = await runner.Locator(".tm-signing-field__prompt").EvaluateAllAsync<double[]>(PromptContrastScript);
        Assert.IsTrue(ratios.Length > 0, "the demo plan renders at least one empty-required prompt");
        var minimum = ratios.Min();
        Assert.IsTrue(
            minimum >= 4.5,
            $"dark-mode prompt contrast must be >= 4.5:1 on the light-island paper, got {minimum:0.00}");
    }

    /// <summary>
    /// Signing redesign phase 3 review round 2 (UX regression fix): the label + prompt head is no
    /// longer an inline-size container, so a label never truncates to a stub merely because the
    /// head is narrow — every label on a reasonably wide field (≥ 160px) fits its box, and the
    /// prompt shows ONLY on the current (selected) field and is actually visible.
    /// </summary>
    [TestMethod]
    [Description("Field labels on fields >= 160px wide are not truncated; the prompt rides the current field")]
    public async Task SigningFormRunner_LabelsReadFully_AndPromptRidesTheCurrentField()
    {
        var page = await OpenRunnerAsync();
        var runner = GetRunner(page);

        // Labels must not truncate on fields with room for them (>= 160px wide overlay root).
        // The +1px tolerance absorbs sub-pixel rounding of the percentage-width overlays.
        var truncated = await runner.Locator(".tm-signing-field__label").EvaluateAllAsync<string[]>(
            """
            labels => labels
                .filter(label => {
                    const overlay = label.closest('.tm-signing-field');
                    const width = overlay ? overlay.getBoundingClientRect().width : 0;
                    return width >= 160 && label.scrollWidth > label.clientWidth + 1;
                })
                .map(label => (label.textContent || '').trim())
            """);

        Assert.AreEqual(
            0,
            truncated.Length,
            $"labels truncated on fields >= 160px wide (scrollWidth > clientWidth + 1): {string.Join(", ", truncated)}");

        // The prompt is the CURRENT field's call to action: exactly one, on the selected overlay.
        var prompts = runner.Locator(".tm-signing-field__prompt");
        var promptCount = await prompts.CountAsync();
        Assert.AreEqual(
            1,
            promptCount,
            $"only the CURRENT field carries the click-to-sign prompt (found {promptCount})");

        var prompt = prompts.First;
        await Assertions.Expect(prompt).ToBeVisibleAsync();

        var promptOverlayClass = await prompt.EvaluateAsync<string>(
            "element => element.closest('.tm-signing-field')?.className ?? ''");
        StringAssert.Contains(
            promptOverlayClass,
            "tm-signing-field--selected",
            $"the prompt must sit on the current (selected) field, got '{promptOverlayClass}'");
    }

    /// <summary>
    /// Relative-luminance contrast ratio of an element's text against its effective background,
    /// blending every translucent ancestor background layer down to the first opaque one.
    /// </summary>
    private const string PromptContrastScript = """
        elements => elements.map((element) => {
            const parse = (value) => {
                const match = value.match(/rgba?\(([^)]+)\)/);
                if (!match) return null;
                const parts = match[1].split(',').map((part) => parseFloat(part));
                return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
            };
            const over = (top, bottom) => ({
                r: top.r * top.a + bottom.r * (1 - top.a),
                g: top.g * top.a + bottom.g * (1 - top.a),
                b: top.b * top.a + bottom.b * (1 - top.a),
                a: 1,
            });
            const luminance = (c) => {
                const channel = (v) => {
                    const s = v / 255;
                    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
                };
                return 0.2126 * channel(c.r) + 0.7152 * channel(c.g) + 0.0722 * channel(c.b);
            };
            const layers = [];
            let ancestor = element;
            while (ancestor) {
                const bg = parse(getComputedStyle(ancestor).backgroundColor);
                if (bg && bg.a > 0) {
                    layers.push(bg);
                    if (bg.a >= 1) break;
                }
                ancestor = ancestor.parentElement;
            }
            let background = { r: 255, g: 255, b: 255, a: 1 };
            for (let i = layers.length - 1; i >= 0; i--) {
                background = over(layers[i], background);
            }
            const foreground = parse(getComputedStyle(element).color) ?? { r: 0, g: 0, b: 0, a: 1 };
            const l1 = luminance(over(foreground, background));
            const l2 = luminance(background);
            return Math.round(((Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05)) * 100) / 100;
        })
        """;

    private async Task<IPage> OpenRunnerAsync(int width = 1280, int height = 720)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        return page;
    }

    private static ILocator GetRunner(IPage page)
    {
        return page.Locator("[data-testid='signing-runner-demo']").First;
    }

    private static ILocator GetDesktopPanel(ILocator runner)
    {
        return runner.Locator(".tm-signing-form-runner__steps").First;
    }

    private static ILocator GetStepsRail(ILocator runner)
    {
        return runner.Locator(".tm-signing-form-runner__steps").First;
    }

    private static async Task DrawSignatureAsync(IPage page, ILocator signatureCapture)
    {
        var canvas = signatureCapture.Locator(".tm-signature-capture__canvas").First;
        await canvas.ScrollIntoViewIfNeededAsync();
        var box = await canvas.BoundingBoxAsync();
        Assert.IsNotNull(box, "Signature canvas should have a bounding box.");

        await page.Mouse.MoveAsync((float)(box!.X + box.Width * 0.2), (float)(box.Y + box.Height * 0.35));
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(box.X + box.Width * 0.42), (float)(box.Y + box.Height * 0.25), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.MoveAsync((float)(box.X + box.Width * 0.7), (float)(box.Y + box.Height * 0.45), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();
    }
}
