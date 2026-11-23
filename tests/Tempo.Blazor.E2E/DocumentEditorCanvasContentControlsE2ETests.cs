using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase E9 E2E coverage for canvas content controls, forms fill commands, locks, and save/reload.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasContentControlsE2ETests : WasmTestBase
{
    private const string PhaseE9DocumentId = "phase-e9-canvas-content-controls";

    [TestMethod]
    public async Task PhaseE9_ContentControlsFillLockUndoSaveAndReload()
    {
        await DocumentEditorE2EReset.ResetAsync();
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE9DocumentAsync(page);

        var output = CreateOutputDirectory("phasee9-content-controls");
        var beforePath = Path.Combine(output, "00-content-controls-before.png");
        var afterPath = Path.Combine(output, "01-content-controls-after-reload.png");

        var initialProbe = await ReadProbeAsync(page);
        Assert.AreEqual(PhaseE9DocumentId, initialProbe.ModelDocumentId);
        Assert.IsTrue(initialProbe.ModelContentControlCount >= 5, initialProbe.Debug);
        Assert.IsTrue(initialProbe.CanvasContentControlCount >= 4, initialProbe.Debug);
        Assert.AreEqual("Customer name", initialProbe.CustomerNameText);
        Assert.AreEqual("☐", initialProbe.ApprovedText);
        Assert.AreEqual("Basic", initialProbe.PlanText);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var nameResult = await ExecuteCanvasCommandAsync(page, "setContentControlText", new
        {
            controlId = "canvas-form-name",
            text = "Ada Lovelace"
        });
        var approvedResult = await ExecuteCanvasCommandAsync(page, "toggleContentControl", new
        {
            controlId = "canvas-form-approved"
        });
        var planResult = await ExecuteCanvasCommandAsync(page, "selectContentControlOption", new
        {
            controlId = "canvas-form-plan",
            selectedValue = "enterprise"
        });
        var lockedResult = await ExecuteCanvasCommandAsync(page, "setContentControlText", new
        {
            controlId = "canvas-form-locked",
            text = "Changed"
        });

        Assert.IsTrue(nameResult.Handled && nameResult.Changed, nameResult.Debug);
        Assert.IsTrue(approvedResult.Handled && approvedResult.Changed, approvedResult.Debug);
        Assert.IsTrue(planResult.Handled && planResult.Changed, planResult.Debug);
        Assert.IsTrue(lockedResult.Handled, lockedResult.Debug);
        Assert.IsFalse(lockedResult.Changed, lockedResult.Debug);
        Assert.AreEqual("locked", lockedResult.Reason);
        await WaitForControlValueAsync(page, "canvas-form-name", "Ada Lovelace");
        await WaitForControlValueAsync(page, "canvas-form-approved", "☑");
        await WaitForControlValueAsync(page, "canvas-form-plan", "Enterprise");

        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForControlValueAsync(page, "canvas-form-plan", "Basic");
        await page.GetByTestId("document-redo").ClickAsync();
        await WaitForControlValueAsync(page, "canvas-form-plan", "Enterprise");

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE9DocumentId}&showToolbar=true");
        await WaitForPhaseE9ReadyAsync(page);
        await WaitForControlValueAsync(page, "canvas-form-name", "Ada Lovelace");
        await WaitForControlValueAsync(page, "canvas-form-approved", "☑");
        await WaitForControlValueAsync(page, "canvas-form-plan", "Enterprise");

        var reloadedProbe = await ReadProbeAsync(page);
        Assert.AreEqual("Ada Lovelace", reloadedProbe.CustomerNameText);
        Assert.AreEqual("☑", reloadedProbe.ApprovedText);
        Assert.AreEqual("Enterprise", reloadedProbe.PlanText);
        Assert.AreEqual("Readonly value", reloadedProbe.LockedText);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE9_ContentControlsFillLockUndoSaveAndReload),
            seedDocumentId = PhaseE9DocumentId,
            userActions = new[]
            {
                "Open the phase E9 canvas content controls seed document.",
                "Fill a plain text control, toggle a checkbox control, select a dropdown value, and verify a locked control rejects edits.",
                "Undo and redo the dropdown command.",
                "Save, navigate away, reload the same document, and verify all filled values persist."
            },
            expectedVisibleChanges = "Canvas form controls paint as structured content control boxes, update through production commands, preserve lock semantics, and reload with the saved values.",
            screenshotPaths = new[] { beforePath, afterPath },
            initialProbe,
            reloadedProbe,
            nameResult,
            approvedResult,
            planResult,
            lockedResult,
            contentMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task PhaseE9_AdvancedControlsNavigateRepeatSaveReloadAndScreenshot()
    {
        await DocumentEditorE2EReset.ResetAsync();
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE9DocumentAsync(page);

        var output = CreateOutputDirectory("phasee9-advanced-content-controls");
        var beforePath = Path.Combine(output, "00-advanced-controls-before.png");
        var designModePath = Path.Combine(output, "01-advanced-controls-design-mode.png");
        var keyboardTabPath = Path.Combine(output, "02-advanced-controls-keyboard-tab.png");
        var popoverPath = Path.Combine(output, "03-advanced-controls-popover.png");
        var afterPath = Path.Combine(output, "04-advanced-controls-after-fill.png");
        var reloadPath = Path.Combine(output, "05-advanced-controls-after-reload.png");

        var initialProbe = await ReadProbeAsync(page);
        Assert.AreEqual("2026-06-05", initialProbe.RenewalText);
        Assert.AreEqual("Contact method", initialProbe.ContactText);
        Assert.AreEqual("Profile photo", initialProbe.PhotoText);
        Assert.AreEqual(1, initialProbe.RepeatingItemCount, initialProbe.Debug);
        await WaitForContentControlRenderModeAsync(page, "form");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE9DocumentId}&showToolbar=true&contentControlMode=design");
        await WaitForPhaseE9ReadyAsync(page);
        await WaitForContentControlRenderModeAsync(page, "design");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = designModePath,
            Type = ScreenshotType.Png
        });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE9DocumentId}&showToolbar=true&contentControlMode=form");
        await WaitForPhaseE9ReadyAsync(page);
        await WaitForContentControlRenderModeAsync(page, "form");

        var focusName = await ExecuteCanvasCommandAsync(page, "focusContentControl", new
        {
            controlId = "canvas-form-name"
        });
        var nextControl = await ExecuteCanvasCommandAsync(page, "nextContentControl", new { });
        var previousControl = await ExecuteCanvasCommandAsync(page, "previousContentControl", new { });
        Assert.IsTrue(focusName.Handled && focusName.SelectionChanged, focusName.Debug);
        Assert.AreEqual("canvas-form-name", focusName.ControlId);
        Assert.IsTrue(nextControl.Handled && nextControl.SelectionChanged, nextControl.Debug);
        Assert.AreEqual("canvas-form-approved", nextControl.ControlId);
        Assert.IsTrue(previousControl.Handled && previousControl.SelectionChanged, previousControl.Debug);
        Assert.AreEqual("canvas-form-name", previousControl.ControlId);

        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await WaitForFocusedContentControlAsync(page, "canvas-form-approved");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = keyboardTabPath,
            Type = ScreenshotType.Png
        });

        await page.Keyboard.PressAsync("Shift+Tab");
        await WaitForFocusedContentControlAsync(page, "canvas-form-name");

        await page.Keyboard.PressAsync("Tab");
        await WaitForFocusedContentControlAsync(page, "canvas-form-approved");
        await page.Keyboard.PressAsync("Tab");
        await WaitForContentControlPopoverAsync(page, "canvas-form-plan", "DropDown");
        await page.Keyboard.PressAsync("Tab");
        await WaitForContentControlPopoverAsync(page, "canvas-form-renewal", "Date");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = popoverPath,
            Type = ScreenshotType.Png
        });
        await SetInputValueAndDispatchChangeAsync(page, "document-canvas-content-control-date", "2026-12-31");
        await WaitForControlValueAsync(page, "canvas-form-renewal", "2026-12-31");

        await page.Keyboard.PressAsync("Tab");
        await WaitForContentControlPopoverAsync(page, "canvas-form-contact", "ComboBox");
        await SetInputValueAndDispatchChangeAsync(page, "document-canvas-content-control-combo-text", "Partner portal");
        await WaitForControlValueAsync(page, "canvas-form-contact", "Partner portal");

        await page.Keyboard.PressAsync("Tab");
        await WaitForContentControlPopoverAsync(page, "canvas-form-photo", "Picture");
        var pictureAssetId = await ReadFirstSelectOptionValueAsync(page, "document-canvas-content-control-picture");
        Assert.IsFalse(string.IsNullOrWhiteSpace(pictureAssetId), "The picture content-control popover must expose at least one real image asset option.");
        await page.GetByTestId("document-canvas-content-control-picture").SelectOptionAsync([pictureAssetId]);
        await WaitForControlValueAsync(page, "canvas-form-photo", pictureAssetId);

        var addRepeatingResult = await ExecuteCanvasCommandAsync(page, "addRepeatingSectionItem", new
        {
            controlId = "canvas-form-addresses",
            text = "Shipping address: 1 Infinite Loop"
        });
        var removeRepeatingResult = await ExecuteCanvasCommandAsync(page, "removeRepeatingSectionItem", new
        {
            controlId = "canvas-form-addresses",
            index = 1
        });

        Assert.IsTrue(addRepeatingResult.Handled && addRepeatingResult.Changed, addRepeatingResult.Debug);
        Assert.IsTrue(removeRepeatingResult.Handled && removeRepeatingResult.Changed, removeRepeatingResult.Debug);

        await WaitForControlValueAsync(page, "canvas-form-renewal", "2026-12-31");
        await WaitForControlValueAsync(page, "canvas-form-contact", "Partner portal");
        await WaitForControlValueAsync(page, "canvas-form-photo", pictureAssetId);
        await WaitForRepeatingItemCountAsync(page, 1);

        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForRepeatingItemCountAsync(page, 2);
        await WaitForMirrorTextAsync(page, "Shipping address: 1 Infinite Loop");

        var afterFillProbe = await ReadProbeAsync(page);
        Assert.AreEqual("2026-12-31", afterFillProbe.RenewalText);
        Assert.AreEqual("Partner portal", afterFillProbe.ContactText);
        Assert.AreEqual(pictureAssetId, afterFillProbe.PhotoText);
        Assert.AreEqual(2, afterFillProbe.RepeatingItemCount, afterFillProbe.Debug);
        Assert.IsTrue(afterFillProbe.RepeatingText.Contains("Shipping address: 1 Infinite Loop", StringComparison.Ordinal), afterFillProbe.Debug);

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE9DocumentId}&showToolbar=true");
        await WaitForPhaseE9ReadyAsync(page);
        await WaitForControlValueAsync(page, "canvas-form-renewal", "2026-12-31");
        await WaitForControlValueAsync(page, "canvas-form-contact", "Partner portal");
        await WaitForControlValueAsync(page, "canvas-form-photo", pictureAssetId);
        await WaitForRepeatingItemCountAsync(page, 2);
        await WaitForMirrorTextAsync(page, "Shipping address: 1 Infinite Loop");

        var reloadedProbe = await ReadProbeAsync(page);
        Assert.AreEqual("2026-12-31", reloadedProbe.RenewalText);
        Assert.AreEqual("Partner portal", reloadedProbe.ContactText);
        Assert.AreEqual(pictureAssetId, reloadedProbe.PhotoText);
        Assert.AreEqual(2, reloadedProbe.RepeatingItemCount, reloadedProbe.Debug);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = reloadPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE9_AdvancedControlsNavigateRepeatSaveReloadAndScreenshot),
            seedDocumentId = PhaseE9DocumentId,
            userActions = new[]
            {
                "Open the phase E9 content controls document.",
                "Capture both form-mode plain rendering and design-mode structured-tag chrome through the production canvas route.",
                "Navigate forward and backward between form fields through the canvas command runtime.",
                "Focus the production hidden input bridge, press Tab and Shift+Tab, and verify keyboard-only field navigation.",
                "Use the Blazor form-field popover to set a date field, edit a combo-box field, and set a picture control asset.",
                "Add a repeating-section item, remove it, and undo the removal.",
                "Save, navigate away, reload, and verify the advanced content-control values and repeating item persist."
            },
            expectedVisibleChanges = "Date, combo-box, picture, and repeating-section content controls paint with updated values, the Blazor form-field popover drives production canvas commands, and values survive provider save/reload.",
            screenshotPaths = new[] { beforePath, designModePath, keyboardTabPath, popoverPath, afterPath, reloadPath },
            pictureAssetId,
            initialProbe,
            afterFillProbe,
            reloadedProbe,
            focusName,
            nextControl,
            previousControl,
            addRepeatingResult,
            removeRepeatingResult,
            contentMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(designModePath);
        TestContext.AddResultFile(keyboardTabPath);
        TestContext.AddResultFile(popoverPath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(reloadPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE9DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={PhaseE9DocumentId}&showToolbar=true");
        await WaitForPhaseE9ReadyAsync(page);
    }

    private static async Task WaitForPhaseE9ReadyAsync(IPage page)
    {
        await WaitForPredicateAsync(page, """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const hostReady = host?.getAttribute('data-canvas-engine-ready') === 'true';
                const handleReady = (host?.getAttribute('data-canvas-engine-handle') || '').length > 0;
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return hostReady
                    && handleReady
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-e9-canvas-content-controls'
                    && Number(first.getAttribute('data-canvas-model-content-control-count') || '0') >= 7
                    && Number(first.getAttribute('data-canvas-content-control-count') || '0') >= 6;
            }
            """, 30_000);

        await page.EvaluateAsync(
            """
            async () => {
                window.__tempoPhaseE9CanvasInterop = window.__tempoPhaseE9CanvasInterop
                    || await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
            }
            """);
    }

    private static async Task WaitForPhaseE9CommandReadyAsync(IPage page)
    {
        await WaitForPredicateAsync(page, """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return host?.getAttribute('data-canvas-engine-ready') === 'true'
                    && (host?.getAttribute('data-canvas-engine-handle') || '').length > 0
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-e9-canvas-content-controls';
            }
            """, 30_000);

        await page.EvaluateAsync(
            """
            async () => {
                window.__tempoPhaseE9CanvasInterop = window.__tempoPhaseE9CanvasInterop
                    || await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
            }
            """);
    }

    private static Task WaitForControlValueAsync(IPage page, string controlId, string expectedText)
        => WaitForPredicateAsync(
            page,
            """
            ([controlId, expectedText]) => Array.from(document.querySelectorAll('[data-canvas-content-control]'))
                .some(item => item.getAttribute('data-control-id') === controlId
                    && item.getAttribute('data-control-text') === expectedText)
            """,
            new[] { controlId, expectedText },
            10_000);

    private static Task WaitForRepeatingItemCountAsync(IPage page, int expectedCount)
        => WaitForPredicateAsync(page, """
            async expectedCount => {
                const model = await readCanvasModel();
                const block = (model?.body?.blocks || []).find(item => item?.content?.contentControl?.control?.controlId === 'canvas-form-addresses');
                return (block?.content?.contentControl?.blocks || []).length === expectedCount;

                async function readCanvasModel() {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                    const module = window.__tempoPhaseE9CanvasInterop
                        || await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    return JSON.parse(module.getModelJson(handle) || '{}');
                }
            }
            """, expectedCount, 10_000);

    private static Task WaitForMirrorTextAsync(IPage page, string expectedText)
        => WaitForPredicateAsync(page, "expectedText => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent?.includes(expectedText) === true", expectedText, 10_000);

    private static Task WaitForFocusedContentControlAsync(IPage page, string expectedControlId)
        => WaitForPredicateAsync(page, """
            async expectedControlId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tempoPhaseE9CanvasInterop
                    || await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const selection = JSON.parse(module.getSelectionStateJson(handle) || '{}');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                return focusedControlId(model, selection) === expectedControlId;

                function focusedControlId(documentModel, selectionState) {
                    const blockId = String(selectionState?.focusBlockId || '');
                    const offset = Number(selectionState?.focusOffset || 0) || 0;
                    for (const block of allBlocks(documentModel)) {
                        if (String(block?.id || '') !== blockId) {
                            continue;
                        }

                        const runs = Array.isArray(block?.content?.runs) ? block.content.runs : [];
                        let cursor = 0;
                        for (const run of runs) {
                            const text = String(run?.text ?? '');
                            const start = cursor;
                            const end = cursor + text.length;
                            const control = run?.contentControl?.control || run?.contentControl || null;
                            if (control && offset >= start && (offset < end || start === end)) {
                                return String(control.controlId || control.id || run.id || '');
                            }

                            cursor = end;
                        }
                    }

                    return '';
                }

                function allBlocks(documentModel) {
                    const roots = Array.isArray(documentModel?.body?.blocks) && documentModel.body.blocks.length > 0
                        ? documentModel.body.blocks
                        : (documentModel?.sections || []).flatMap(section => section?.blocks || []);
                    const stack = [...roots].reverse();
                    const result = [];
                    while (stack.length > 0) {
                        const block = stack.pop();
                        if (!block) {
                            continue;
                        }

                        result.push(block);
                        const rows = block?.content?.table?.rows;
                        if (Array.isArray(rows)) {
                            for (let rowIndex = rows.length - 1; rowIndex >= 0; rowIndex -= 1) {
                                for (const cell of [...(rows[rowIndex]?.cells || [])].reverse()) {
                                    for (const nested of [...(cell?.blocks || [])].reverse()) {
                                        stack.push(nested);
                                    }
                                }
                            }
                        }

                        const nestedControls = block?.content?.contentControl?.blocks;
                        if (Array.isArray(nestedControls)) {
                            for (const nested of [...nestedControls].reverse()) {
                                stack.push(nested);
                            }
                        }
                    }

                    return result;
                }
            }
            """, expectedControlId, 10_000);

    private static Task WaitForContentControlPopoverAsync(IPage page, string expectedControlId, string expectedKind)
        => WaitForPredicateAsync(
            page,
            """
            ([expectedControlId, expectedKind]) => {
                const popover = document.querySelector('[data-testid="document-canvas-content-control-popover"]');
                return popover?.getAttribute('data-control-id') === expectedControlId
                    && popover?.getAttribute('data-control-kind') === expectedKind;
            }
            """,
            new[] { expectedControlId, expectedKind },
            10_000);

    private static Task WaitForContentControlRenderModeAsync(IPage page, string expectedMode)
        => WaitForPredicateAsync(page, """
            expectedMode => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const controls = Array.from(document.querySelectorAll('[data-canvas-content-control]'));
                return host?.getAttribute('data-canvas-content-control-render-mode') === expectedMode
                    && controls.length > 0
                    && controls.every(item => item.getAttribute('data-control-render-mode') === expectedMode);
            }
            """, expectedMode, 10_000);

    private static async Task SetInputValueAndDispatchChangeAsync(IPage page, string testId, string value)
        => await page.GetByTestId(testId).EvaluateAsync(
            """
            (element, nextValue) => {
                element.value = nextValue;
                element.dispatchEvent(new Event('input', { bubbles: true }));
                element.dispatchEvent(new Event('change', { bubbles: true }));
            }
            """,
            value);

    private static async Task<string> ReadFirstSelectOptionValueAsync(IPage page, string testId)
        => await page.GetByTestId(testId).EvaluateAsync<string>(
            """
            element => Array.from(element.options || [])
                .map(option => option.value || '')
                .find(value => value.length > 0) || ''
            """);

    private static async Task<PhaseE9CommandProbe> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        await WaitForPhaseE9CommandReadyAsync(page);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await page.EvaluateAsync<PhaseE9CommandProbe>(
            """
            ({ commandId, json }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tempoPhaseE9CanvasInterop;
                const raw = module.execCommand(handle, commandId, json);
                const parsed = JSON.parse(raw || '{}');
                return {
                    changed: parsed?.result?.changed === true,
                    handled: parsed?.handled === true,
                    selectionChanged: parsed?.result?.selectionChanged === true,
                    reason: parsed?.result?.reason || '',
                    controlId: parsed?.result?.controlId || '',
                    repeatingItemCount: Number(parsed?.result?.repeatingSection?.itemCount || '0'),
                    debug: JSON.stringify(parsed)
                };
            }
            """,
            new { commandId, json });
    }

    private static Task<PhaseE9Probe> ReadProbeAsync(IPage page)
        => page.EvaluateAsync<PhaseE9Probe>(
            """
            async () => {
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                const controls = Array.from(document.querySelectorAll('[data-canvas-content-control]'));
                const byId = id => controls.find(item => item.getAttribute('data-control-id') === id)?.getAttribute('data-control-text') || '';
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tempoPhaseE9CanvasInterop
                    || await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const repeating = (model?.body?.blocks || []).find(block => block?.content?.contentControl?.control?.controlId === 'canvas-form-addresses');
                const repeatingBlocks = repeating?.content?.contentControl?.blocks || [];
                const repeatingText = repeatingBlocks.flatMap(block => block?.content?.runs || []).map(run => run?.text || '').join(' ');
                return {
                    modelDocumentId: first?.getAttribute('data-canvas-model-document-id') || '',
                    modelContentControlCount: Number(first?.getAttribute('data-canvas-model-content-control-count') || '0'),
                    canvasContentControlCount: Number(first?.getAttribute('data-canvas-content-control-count') || '0'),
                    customerNameText: byId('canvas-form-name'),
                    approvedText: byId('canvas-form-approved'),
                    planText: byId('canvas-form-plan'),
                    renewalText: byId('canvas-form-renewal'),
                    contactText: byId('canvas-form-contact'),
                    photoText: byId('canvas-form-photo'),
                    lockedText: byId('canvas-form-locked'),
                    repeatingItemCount: repeatingBlocks.length,
                    repeatingText,
                    debug: JSON.stringify({
                        sourceDocumentId: host?.getAttribute('data-canvas-source-document-id') || '',
                        controls: controls.map(item => ({
                            id: item.getAttribute('data-control-id') || '',
                            kind: item.getAttribute('data-control-kind') || '',
                            text: item.getAttribute('data-control-text') || '',
                            required: item.getAttribute('data-control-required') || '',
                            locked: item.getAttribute('data-control-locked') || '',
                            valid: item.getAttribute('data-control-valid') || ''
                        })),
                        repeatingItemCount: repeatingBlocks.length,
                        repeatingText
                    })
                };
            }
            """);

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private static async Task WaitForSaveBoundaryAsync(IPage page)
    {
        await WaitForPredicateAsync(page, """
            () => {
                const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                const lastSaved = document.querySelector('[data-testid="document-last-saved"]')?.textContent || '';
                const dirty = document.querySelector('[data-testid="document-editor-demo"]')?.getAttribute('data-document-dirty') === 'true';
                const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                return !dirty
                    && !saveButtonDisabled
                    && (/Saved|Autosaved/i.test(saveMessage) || /saved/i.test(lastSaved));
            }
            """, 20_000);
    }

    private static string CreateOutputDirectory(string testName)
    {
        var path = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phasee9-content-controls",
            "2026-06-04",
            testName,
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    private sealed class PhaseE9Probe
    {
        public string ModelDocumentId { get; set; } = string.Empty;
        public int ModelContentControlCount { get; set; }
        public int CanvasContentControlCount { get; set; }
        public string CustomerNameText { get; set; } = string.Empty;
        public string ApprovedText { get; set; } = string.Empty;
        public string PlanText { get; set; } = string.Empty;
        public string RenewalText { get; set; } = string.Empty;
        public string ContactText { get; set; } = string.Empty;
        public string PhotoText { get; set; } = string.Empty;
        public string LockedText { get; set; } = string.Empty;
        public int RepeatingItemCount { get; set; }
        public string RepeatingText { get; set; } = string.Empty;
        public string Debug { get; set; } = string.Empty;
    }

    private sealed class PhaseE9CommandProbe
    {
        public bool Changed { get; set; }
        public bool Handled { get; set; }
        public bool SelectionChanged { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string ControlId { get; set; } = string.Empty;
        public int RepeatingItemCount { get; set; }
        public string Debug { get; set; } = string.Empty;
    }
}
