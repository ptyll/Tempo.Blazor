using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SigningQualityE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("PDF template designer can select a field with keyboard only")]
    public async Task FieldEditorPanel_SelectsFieldWithKeyboard()
    {
        var page = await OpenSigningComponentsAsync();
        var designer = GetDesigner(page);
        await designer.ScrollIntoViewIfNeededAsync();

        var field = designer.Locator("[data-field-uuid='designer-delivery']").First;
        await field.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(designer.Locator(".tm-signing-field-editor-panel__title")).ToContainTextAsync("Delivery");
    }

    [TestMethod]
    [Description("Signing form runner supports keyboard-only field entry and step navigation")]
    public async Task SigningFormRunner_KeyboardOnlyNavigatesSteps()
    {
        var page = await OpenSigningComponentsAsync();
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        var panel = runner.Locator(".tm-signing-form-runner__steps").First;

        await panel.Locator("input.tm-signing-text-step__input").FocusAsync();
        await page.Keyboard.InsertTextAsync("Alex Johnson");
        await panel.Locator(".tm-signing-form-runner__next").FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Delivery method");
        await panel.Locator(".tm-signing-form-runner__next").FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Amount");
        await panel.Locator("input.tm-signing-number-step__input").FocusAsync();
        await page.Keyboard.InsertTextAsync("100");
        await panel.Locator(".tm-signing-form-runner__next").FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(panel.Locator(".tm-signing-step-shell__title")).ToContainTextAsync("Add internal note");
    }

    [TestMethod]
    [Description("Signing form runner mobile panel can collapse, expand, and return focus to the active input")]
    public async Task SigningFormRunner_MobilePanelDoesNotBlockFocus()
    {
        var page = await OpenSigningComponentsAsync(390, 820);
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();

        // Phase 5 review: the HANDLE is the sheet's only toggle (the Minimize row is gone).
        var handle = runner.Locator(".tm-signing-form-runner__sheet-handle").First;
        await handle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(runner.Locator(".tm-signing-form-runner__mobile-expand")).ToBeVisibleAsync();

        var expand = runner.Locator(".tm-signing-form-runner__mobile-expand").First;
        await expand.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        // Phase 2 (B25): the single steps rail is the mobile sheet — the input lives inside it.
        var input = runner.Locator(".tm-signing-form-runner__steps input.tm-signing-text-step__input").First;
        await Assertions.Expect(input).ToBeVisibleAsync();
        await input.FocusAsync();

        var activeClass = await page.EvaluateAsync<string>("() => document.activeElement?.className?.toString() ?? ''");
        StringAssert.Contains(activeClass, "tm-signing-text-step__input");
    }

    [TestMethod]
    [Description("PDF template designer desktop view remains screenshotable")]
    public async Task PdfTemplateDesigner_DesktopScreenshot()
    {
        var page = await OpenSigningComponentsAsync(1280, 900);
        var designer = GetDesigner(page);
        await designer.ScrollIntoViewIfNeededAsync();

        await Assertions.Expect(designer).ToBeVisibleAsync();
        await TakeScreenshotAsync(page, "signing-phase12-designer-desktop");
    }

    [TestMethod]
    [Description("PDF template designer mobile view remains screenshotable")]
    public async Task PdfTemplateDesigner_MobileScreenshot()
    {
        var page = await OpenSigningComponentsAsync(390, 844);
        var designer = GetDesigner(page);
        await designer.ScrollIntoViewIfNeededAsync();

        await Assertions.Expect(designer).ToBeVisibleAsync();
        await TakeScreenshotAsync(page, "signing-phase12-designer-mobile");
    }

    [TestMethod]
    [Description("Signing runner desktop view remains screenshotable")]
    public async Task SigningFormRunner_DesktopScreenshot()
    {
        var page = await OpenSigningComponentsAsync(1280, 900);
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();

        await Assertions.Expect(runner).ToBeVisibleAsync();
        await TakeScreenshotAsync(page, "signing-phase12-runner-desktop");
    }

    [TestMethod]
    [Description("Signing runner mobile view remains screenshotable")]
    public async Task SigningFormRunner_MobileScreenshot()
    {
        var page = await OpenSigningComponentsAsync(390, 844);
        var runner = GetRunner(page);
        await runner.ScrollIntoViewIfNeededAsync();

        await Assertions.Expect(runner.Locator(".tm-signing-form-runner__steps")).ToBeVisibleAsync();
        await TakeScreenshotAsync(page, "signing-phase12-runner-mobile");
    }

    [TestMethod]
    [Description("Signing field labels stay contained and overlays remain stable across resize and zoom")]
    public async Task SigningOverlays_RemainStableAcrossResizeAndZoom()
    {
        var page = await OpenSigningComponentsAsync(1280, 900);
        var designer = GetDesigner(page);
        await designer.ScrollIntoViewIfNeededAsync();

        var visuallyEscapedLabels = await page.Locator(".tm-signing-field__label").EvaluateAllAsync<string[]>(
            """
            elements => elements
                .filter(element => {
                    const label = element.getBoundingClientRect();
                    const field = element.closest('.tm-signing-field')?.getBoundingClientRect();
                    if (!field) return false;
                    return label.left < field.left - 1
                        || label.right > field.right + 1
                        || label.top < field.top - 1
                        || label.bottom > field.bottom + 1;
                })
                .map(element => element.textContent || '')
            """);
        Assert.AreEqual(0, visuallyEscapedLabels.Length, $"Labels escaping their field boxes: {string.Join(", ", visuallyEscapedLabels)}");

        var field = designer.Locator("[data-field-uuid='designer-name']").First;
        var before = await field.BoundingBoxAsync();
        Assert.IsNotNull(before);
        Assert.IsTrue(before.Width > 0);
        Assert.IsTrue(before.Height > 0);

        await page.SetViewportSizeAsync(1024, 760);
        await page.EvaluateAsync("() => { document.documentElement.style.zoom = '0.9'; window.dispatchEvent(new Event('resize')); }");
        await page.WaitForTimeoutAsync(300);

        var surface = designer.Locator("[data-page-key='designer-nda:0'] .tm-pdf-template-designer__page-surface").First;
        await surface.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(100);

        var containment = await designer.EvaluateAsync<OverlayContainmentResult>(
            """
            designer => {
                const field = designer.querySelector("[data-field-uuid='designer-name']");
                const surface = designer.querySelector("[data-page-key='designer-nda:0'] .tm-pdf-template-designer__page-surface");
                const fieldRect = field.getBoundingClientRect();
                const surfaceRect = surface.getBoundingClientRect();
                return {
                    fieldLeft: fieldRect.left,
                    fieldTop: fieldRect.top,
                    fieldRight: fieldRect.right,
                    fieldBottom: fieldRect.bottom,
                    fieldWidth: fieldRect.width,
                    fieldHeight: fieldRect.height,
                    surfaceLeft: surfaceRect.left,
                    surfaceTop: surfaceRect.top,
                    surfaceRight: surfaceRect.right,
                    surfaceBottom: surfaceRect.bottom,
                    inside: fieldRect.left >= surfaceRect.left - 1
                        && fieldRect.top >= surfaceRect.top - 1
                        && fieldRect.right <= surfaceRect.right + 1
                        && fieldRect.bottom <= surfaceRect.bottom + 1
                };
            }
            """);

        Assert.IsTrue(containment.FieldWidth > 0);
        Assert.IsTrue(containment.FieldHeight > 0);
        Assert.IsTrue(containment.Inside, $"Field rect ({containment.FieldLeft}, {containment.FieldTop}, {containment.FieldRight}, {containment.FieldBottom}) should stay inside surface rect ({containment.SurfaceLeft}, {containment.SurfaceTop}, {containment.SurfaceRight}, {containment.SurfaceBottom}).");

        await page.EvaluateAsync("() => { document.documentElement.style.zoom = ''; window.dispatchEvent(new Event('resize')); }");
    }

    private async Task<IPage> OpenSigningComponentsAsync(int width = 1280, int height = 720)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static ILocator GetDesigner(IPage page)
    {
        return page.Locator("[data-testid='pdf-template-designer']").First;
    }

    private static ILocator GetRunner(IPage page)
    {
        return page.Locator("[data-testid='signing-runner-demo']").First;
    }

    private sealed class OverlayContainmentResult
    {
        public double FieldLeft { get; set; }

        public double FieldTop { get; set; }

        public double FieldRight { get; set; }

        public double FieldBottom { get; set; }

        public double FieldWidth { get; set; }

        public double FieldHeight { get; set; }

        public double SurfaceLeft { get; set; }

        public double SurfaceTop { get; set; }

        public double SurfaceRight { get; set; }

        public double SurfaceBottom { get; set; }

        public bool Inside { get; set; }
    }
}
