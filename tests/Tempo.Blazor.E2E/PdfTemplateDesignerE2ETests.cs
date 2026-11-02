using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Server circuit: one palette click must arm draw mode and add nothing. A second click places
/// exactly one field. WASM cannot show the double activation the JS tap path used to cause.
/// </summary>
[TestClass]
public class PdfTemplateDesignerServerE2ETests : ServerTestBase
{
    [TestMethod]
    [Description("One palette click adds no field; the second places exactly one")]
    public async Task PdfTemplateDesigner_PaletteClick_AddsOneFieldOnTheSecondClick()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");

        var designer = page.Locator("[data-testid='pdf-template-designer']").First;
        await designer.ScrollIntoViewIfNeededAsync();
        var status = page.Locator("[data-testid='pdf-template-designer-status']");
        await Assertions.Expect(status).ToContainTextAsync("2 designer fields");

        var text = designer.Locator("[data-field-type='Text']").First;
        await text.ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await Assertions.Expect(status).ToContainTextAsync("2 designer fields");
        await Assertions.Expect(designer).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-pdf-template-designer--drawing"));
        await Assertions.Expect(designer.Locator(".tm-signing-field")).ToHaveCountAsync(2);

        await text.ClickAsync();
        await Assertions.Expect(status).ToContainTextAsync("3 designer fields");
        await Assertions.Expect(designer.Locator(".tm-signing-field")).ToHaveCountAsync(3);
    }
}

[TestClass]
public class PdfTemplateDesignerE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("PDF template designer demo renders the single-page designer with navigation")]
    public async Task PdfTemplateDesigner_OpensDemo()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        await Expect(designer).ToBeVisibleAsync();
        await Expect(designer.Locator(".tm-document-page-viewer")).ToHaveCountAsync(1);
        await Expect(designer.Locator(".tm-pdf-template-designer__page-label")).ToContainTextAsync("1 / 2");
        await TakeScreenshotAsync(page, "pdf-template-designer-desktop");
    }

    [TestMethod]
    [Description("PDF template designer switches between single page, next page, and continuous modes")]
    public async Task PdfTemplateDesigner_PageNavigationAndViewModeWork()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        await Expect(designer.Locator(".tm-pdf-template-designer__page-frame[data-page-key='designer-nda:0']")).ToHaveCountAsync(1);
        await Expect(designer.Locator(".tm-pdf-template-designer__page-frame[data-page-key='designer-nda:1']")).ToHaveCountAsync(0);

        await designer.Locator(".tm-pdf-template-designer__next-page").ClickAsync();
        await Expect(designer.Locator(".tm-pdf-template-designer__page-label")).ToContainTextAsync("2 / 2");
        await Expect(designer.Locator(".tm-pdf-template-designer__page-frame[data-page-key='designer-nda:0']")).ToHaveCountAsync(0);
        await Expect(designer.Locator(".tm-pdf-template-designer__page-frame[data-page-key='designer-nda:1']")).ToHaveCountAsync(1);

        await designer.Locator(".tm-pdf-template-designer__continuous").ClickAsync();
        await Expect(designer.Locator(".tm-document-page-viewer")).ToHaveCountAsync(2);

        await designer.Locator(".tm-pdf-template-designer__single-page").ClickAsync();
        await Expect(designer.Locator(".tm-document-page-viewer")).ToHaveCountAsync(1);
    }

    [TestMethod]
    [Description("PDF template designer zoom controls resize the working page without changing field count")]
    public async Task PdfTemplateDesigner_ZoomControlsResizePage()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);
        var pageElement = designer.Locator(".tm-document-page-viewer__page").First;
        var before = await pageElement.BoundingBoxAsync();
        Assert.IsNotNull(before);

        await ClickDesignerCommandAsync(page, designer, ".tm-pdf-template-designer__zoom-in", "Zoom in");
        await Expect(designer.Locator(".tm-pdf-template-designer__zoom-label")).ToContainTextAsync("125%");
        var zoomed = await pageElement.BoundingBoxAsync();
        Assert.IsNotNull(zoomed);
        Assert.IsTrue(zoomed!.Width > before!.Width, "Zoom in should increase the visible designer page width.");
        await Expect(designer.Locator(".tm-signing-field")).ToHaveCountAsync(2);

        await ClickDesignerCommandAsync(page, designer, ".tm-pdf-template-designer__fit-page", "Fit page");
        await Expect(designer.Locator(".tm-pdf-template-designer__zoom-label")).ToContainTextAsync("85%");
    }

    [TestMethod]
    [Description("PDF template designer can place a dragged field on the second page")]
    public async Task PdfTemplateDesigner_DragsPaletteFieldOntoSecondPage()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        await designer.Locator(".tm-pdf-template-designer__next-page").ClickAsync();
        var surface = designer.Locator(".tm-pdf-template-designer__page-surface[data-page-key='designer-nda:1']").First;
        var surfaceBox = await surface.BoundingBoxAsync();
        Assert.IsNotNull(surfaceBox);

        await designer.Locator("[data-field-type='Signature']").DragToAsync(surface, new LocatorDragToOptions
        {
            TargetPosition = new TargetPosition
            {
                X = (float)(surfaceBox.Width * 0.5),
                Y = (float)(surfaceBox.Height * 0.6)
            }
        });

        await Expect(page.Locator("[data-testid='pdf-template-designer-status']")).ToContainTextAsync("3 designer fields");
        await Expect(designer.Locator(".tm-signing-field")).ToHaveCountAsync(1);
        await Expect(designer.Locator(".tm-signing-field--selected")).ToHaveCountAsync(1);
    }

    [TestMethod]
    [Description("PDF template designer draws a new text field")]
    public async Task PdfTemplateDesigner_DrawsTextField()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        await designer.Locator("[data-field-type='Text']").ClickAsync();
        var surface = designer.Locator(".tm-pdf-template-designer__page-surface[data-page-key='designer-nda:0']").First;
        await surface.ScrollIntoViewIfNeededAsync();
        var box = await surface.BoundingBoxAsync();
        Assert.IsNotNull(box);

        await page.Mouse.MoveAsync(box.X + 80, box.Y + 120);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + 220, box.Y + 170);
        await page.Mouse.UpAsync();

        await Expect(page.Locator("[data-testid='pdf-template-designer-status']")).ToContainTextAsync("3 designer fields");
        await Expect(designer.Locator(".tm-signing-field")).ToHaveCountAsync(3);
    }

    [TestMethod]
    [Description("PDF template designer supports dragging a palette field directly onto the document")]
    public async Task PdfTemplateDesigner_DragsPaletteFieldOntoPage()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);
        var surface = designer.Locator(".tm-pdf-template-designer__page-surface[data-page-key='designer-nda:0']").First;
        var surfaceBox = await surface.BoundingBoxAsync();
        Assert.IsNotNull(surfaceBox);

        await designer.Locator("[data-field-type='Signature']").DragToAsync(surface, new LocatorDragToOptions
        {
            TargetPosition = new TargetPosition
            {
                X = (float)(surfaceBox.Width * 0.72),
                Y = (float)(surfaceBox.Height * 0.72)
            }
        });

        await Expect(page.Locator("[data-testid='pdf-template-designer-status']")).ToContainTextAsync("3 designer fields");
        await Expect(designer.Locator(".tm-signing-field")).ToHaveCountAsync(3);
        await Expect(designer.Locator(".tm-signing-field--selected")).ToHaveCountAsync(1);
    }

    [TestMethod]
    [Description("PDF template designer palette items stay inside the palette column")]
    public async Task PdfTemplateDesigner_PaletteDoesNotOverflowColumn()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        var overflowing = await designer.Locator(".tm-pdf-template-designer__palette").EvaluateAsync<string[]>(
            """
            palette => {
                const paletteBox = palette.getBoundingClientRect();
                return Array.from(palette.querySelectorAll('.tm-pdf-template-designer__palette-item'))
                    .filter(item => {
                        const box = item.getBoundingClientRect();
                        return box.left < paletteBox.left - 1 || box.right > paletteBox.right + 1;
                    })
                    .map(item => item.textContent.trim());
            }
            """);

        Assert.AreEqual(0, overflowing.Length, $"Palette items overflow the palette column: {string.Join(", ", overflowing)}");
    }

    [TestMethod]
    [Description("PDF template designer demo keeps delivery field away from the body paragraph")]
    public async Task PdfTemplateDesigner_DeliveryFieldIsPlacedBesideRecipient()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        var nameBox = await designer.Locator("[data-field-uuid='designer-name']").First.BoundingBoxAsync();
        var deliveryBox = await designer.Locator("[data-field-uuid='designer-delivery']").First.BoundingBoxAsync();

        Assert.IsNotNull(nameBox);
        Assert.IsNotNull(deliveryBox);
        Assert.IsTrue(deliveryBox.X > nameBox.X + nameBox.Width, "Delivery should sit beside the recipient field, not over the body text.");
        Assert.IsTrue(Math.Abs(deliveryBox.Y - nameBox.Y) < nameBox.Height, "Delivery should stay aligned with the recipient row.");
    }

    [TestMethod]
    [Description("PDF template designer moves and resizes a selected field")]
    public async Task PdfTemplateDesigner_MovesAndResizesField()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);
        var field = designer.Locator("[data-field-uuid='designer-name']").First;
        await field.ClickAsync(new LocatorClickOptions { Force = true });
        var before = await field.BoundingBoxAsync();
        Assert.IsNotNull(before);

        var handle = designer.Locator("[data-field-uuid='designer-name'] [data-handle='SouthEast']").First;
        var handleBox = await handle.BoundingBoxAsync();
        Assert.IsNotNull(handleBox);
        await page.Mouse.MoveAsync(handleBox.X + handleBox.Width / 2, handleBox.Y + handleBox.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(handleBox.X + handleBox.Width / 2 + 40, handleBox.Y + handleBox.Height / 2 + 24);
        await page.Mouse.UpAsync();

        var resized = await field.BoundingBoxAsync();
        Assert.IsNotNull(resized);
        Assert.IsTrue(resized.Width > before.Width);

        await page.Mouse.MoveAsync(resized.X + 10, resized.Y + 10);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(resized.X + 46, resized.Y + 30);
        await page.Mouse.UpAsync();

        var moved = await field.BoundingBoxAsync();
        Assert.IsNotNull(moved);
        Assert.IsTrue(moved.X > resized.X);
    }

    [TestMethod]
    [Description("PDF template designer field context menu actions stay clickable above document fields")]
    public async Task PdfTemplateDesigner_ContextMenuActionsAreClickable()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);
        var field = designer.Locator("[data-field-uuid='designer-name']").First;

        await field.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
        await Expect(page.Locator(".tm-context-menu.tm-overlay-panel--open")).ToHaveCountAsync(1);
        await ClickOpenMenuItemAsync(page, ".tm-pdf-template-designer__delete-field");

        await Expect(page.Locator("[data-testid='pdf-template-designer-status']")).ToContainTextAsync("1 designer field");
    }

    [TestMethod]
    [Description("PDF template designer copy closes the context menu, shows feedback, and pastes at the clicked page position")]
    public async Task PdfTemplateDesigner_CopyShowsFeedbackAndPastesAtClickPosition()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);
        var field = designer.Locator("[data-field-uuid='designer-name']").First;

        await field.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
        await ClickOpenMenuItemAsync(page, ".tm-pdf-template-designer__copy-field");

        await Expect(designer.Locator(".tm-pdf-template-designer__context-menu")).ToHaveCountAsync(0);
        await Expect(designer.Locator(".tm-pdf-template-designer__clipboard-status")).ToContainTextAsync("Field copied");

        var surface = designer.Locator(".tm-pdf-template-designer__page-surface[data-page-key='designer-nda:0']").First;
        var surfaceBox = await surface.BoundingBoxAsync();
        Assert.IsNotNull(surfaceBox);
        var targetX = surfaceBox.X + surfaceBox.Width * 0.72;
        var targetY = surfaceBox.Y + surfaceBox.Height * 0.68;
        await page.Mouse.ClickAsync((float)targetX, (float)targetY, new MouseClickOptions { Button = MouseButton.Right });
        await ClickOpenMenuItemAsync(page, ".tm-pdf-template-designer__paste-field");

        await Expect(page.Locator("[data-testid='pdf-template-designer-status']")).ToContainTextAsync("3 designer fields");
        await Expect(designer.Locator(".tm-pdf-template-designer__clipboard-status")).ToContainTextAsync("Field pasted");

        var selected = designer.Locator(".tm-signing-field--selected").First;
        var selectedBox = await selected.BoundingBoxAsync();
        Assert.IsNotNull(selectedBox);
        Assert.IsTrue(Math.Abs((selectedBox.X + selectedBox.Width / 2) - targetX) < 24, "Pasted field should be centered near the context-click location.");
        Assert.IsTrue(Math.Abs((selectedBox.Y + selectedBox.Height / 2) - targetY) < 24, "Pasted field should be centered near the context-click location.");
    }

    [TestMethod]
    [Description("PDF template designer moving a field does not start page rectangle selection")]
    public async Task PdfTemplateDesigner_MoveDoesNotStartSelectionBox()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);
        var field = designer.Locator("[data-field-uuid='designer-name']").First;
        var before = await field.BoundingBoxAsync();
        Assert.IsNotNull(before);

        await page.Mouse.MoveAsync(before.X + 10, before.Y + 10);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(before.X + 90, before.Y + 60);

        await Expect(designer.Locator(".tm-pdf-template-designer__draft")).ToHaveCountAsync(0);

        await page.Mouse.UpAsync();
        await Expect(designer.Locator(".tm-signing-field--selected")).ToHaveCountAsync(1);
    }

    [TestMethod]
    [Description("PDF template designer deletes the selected field with the Delete key")]
    public async Task PdfTemplateDesigner_DeleteKeyDeletesSelectedField()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        await designer.Locator("[data-field-uuid='designer-name']").ClickAsync();
        await designer.Locator(".tm-pdf-template-designer__workspace").FocusAsync();
        await page.Keyboard.PressAsync("Delete");

        await Expect(page.Locator("[data-testid='pdf-template-designer-status']")).ToContainTextAsync("1 designer field");
        await Expect(designer.Locator("[data-field-uuid='designer-name']")).ToHaveCountAsync(0);
    }

    [TestMethod]
    [Description("Delete with Autodetect focused does not delete the selected field")]
    public async Task PdfTemplateDesigner_DeleteWithDetectFocused_DoesNotDelete()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        await designer.Locator("[data-field-uuid='designer-name']").ClickAsync();
        var detect = designer.Locator(".tm-pdf-template-designer__detect").First;
        await detect.FocusAsync();
        await page.Keyboard.PressAsync("Delete");

        await Expect(designer.Locator("[data-field-uuid='designer-name']")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid='pdf-template-designer-status']")).ToContainTextAsync("2 designer fields");
    }

    [TestMethod]
    [Description("PDF template designer opens field settings and edits select options")]
    public async Task PdfTemplateDesigner_OpensSettingsAndAddsSelectOption()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        await designer.Locator("[data-field-uuid='designer-delivery']").ClickAsync();

        await Expect(designer.Locator(".tm-signing-field-editor-panel__title")).ToContainTextAsync("Delivery");
        await designer.Locator(".tm-signing-field-editor-panel__add-option").ClickAsync();
        await Expect(designer.Locator(".tm-signing-field-editor-panel__option-row")).ToHaveCountAsync(3);
    }

    [TestMethod]
    [Description("PDF template designer field condition editor stays inside the right settings panel")]
    public async Task PdfTemplateDesigner_ConditionEditorFitsSettingsPanel()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        await designer.Locator("[data-field-uuid='designer-name']").ClickAsync();
        await designer.Locator(".tm-signing-field-editor-panel__open-conditions").ClickAsync();
        await designer.Locator(".tm-condition-builder__add").ClickAsync();

        var overflowing = await designer.Locator(".tm-pdf-template-designer__panel").EvaluateAsync<string[]>(
            """
            panel => {
                const panelBox = panel.getBoundingClientRect();
                return Array.from(panel.querySelectorAll('input, select, textarea, button, .tm-condition-builder__row'))
                    .filter(element => {
                        const box = element.getBoundingClientRect();
                        return box.left < panelBox.left - 1 || box.right > panelBox.right + 1;
                    })
                    .map(element => element.className || element.tagName);
            }
            """);

        Assert.AreEqual(0, overflowing.Length, $"Settings panel controls overflow: {string.Join(", ", overflowing)}");
    }

    [TestMethod]
    [Description("PDF template designer multi-selects two fields and deletes them")]
    public async Task PdfTemplateDesigner_MultiSelectDeletesFields()
    {
        var page = await OpenDesignerAsync();
        var designer = GetDesigner(page);

        await designer.Locator("[data-field-uuid='designer-name']").ClickAsync();
        await designer.Locator("[data-field-uuid='designer-delivery']").ClickAsync(new LocatorClickOptions
        {
            Modifiers = [KeyboardModifier.Control]
        });

        await Expect(designer.Locator(".tm-signing-field--selected")).ToHaveCountAsync(2);
        await ClickDesignerCommandAsync(page, designer, ".tm-pdf-template-designer__delete-selected", "Delete selected");

        await Expect(page.Locator("[data-testid='pdf-template-designer-status']")).ToContainTextAsync("0 designer fields");
    }

    [TestMethod]
    [Description("PDF template designer keeps the mobile layout renderable")]
    public async Task PdfTemplateDesigner_MobileScreenshot()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var designer = GetDesigner(page);
        await designer.ScrollIntoViewIfNeededAsync();
        await Expect(designer).ToBeVisibleAsync();
        await TakeScreenshotAsync(page, "pdf-template-designer-mobile");
    }

    private async Task<IPage> OpenDesignerAsync()
    {
        // 1440 keeps the demo card wide enough for the shell to dock both panels. The default 1280
        // viewport classifies the card as a tablet, where the inspector is an overlay sheet.
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);
        // The demo sidebar keeps a 1440 viewport's designer under the desktop breakpoint, so the
        // palette opens as an overlay and covers the page the gestures land on.
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase8Specs.WideLayoutStyle });
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('resize'))");
        var designer = GetDesigner(page);
        await designer.ScrollIntoViewIfNeededAsync();
        return page;
    }

    /// <summary>
    /// The designer toolbar overflows at the demo card width, so a pinned zoom command can sit under
    /// the More trigger. Click the bar button when it is free; otherwise invoke the same command from More.
    /// </summary>
    private static async Task ClickDesignerCommandAsync(IPage page, ILocator designer, string hook, string label)
    {
        var button = designer.Locator(hook).First;
        var collapsed = await button.EvaluateAsync<bool>(
            "button => button.closest('.tm-toolbar-item--collapsed') !== null");
        if (!collapsed)
        {
            // The sticky demo header covers the toolbar after the page grows. Dispatch on the button
            // itself so the click is not intercepted and does not land on the More trigger.
            await button.DispatchEventAsync("click");
            return;
        }

        await designer.Locator("button.tm-toolbar-more").DispatchEventAsync("click");
        await designer.Locator(".tm-toolbar-more__item").Filter(new() { HasText = label }).First.ClickAsync();
    }

    /// <summary>
    /// A top-layer popover reports a box Playwright will not click. Hit the painted item directly.
    /// </summary>
    private static async Task ClickOpenMenuItemAsync(IPage page, string selector)
    {
        // A mouse click at the painted popover is treated as outside and dismisses it before the
        // item runs. The button's own click reaches Blazor's delegator.
        await page.EvaluateAsync("selector => document.querySelector(selector)?.click()", selector);
    }

    private static ILocator GetDesigner(IPage page)
    {
        return page.Locator("[data-testid='pdf-template-designer']").First;
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
