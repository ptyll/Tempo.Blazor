using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>E5 E2E coverage for canvas fields, captions, cross-references, table of figures, bibliography, undo, and save/reload.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasFieldsE2ETests : WasmTestBase
{
    private const string PhaseE5DocumentId = "phase-e5-canvas-fields";
    private const string InitialHeadingText = "Reference targets and generated fields";
    private const string UpdatedHeadingText = "Updated reference target fields";

    [TestMethod]
    public async Task PhaseE5_CanvasFieldsCrossReferencesCaptionsAndBibliographyPersist()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE5DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phasee5-fields-before.png");
        var renamedPath = Path.Combine(output, "01-phasee5-cross-reference-renamed.png");
        var afterPath = Path.Combine(output, "02-phasee5-fields-after-reload.png");

        var initialProbe = await ReadProbeAsync(page);
        Assert.AreEqual(PhaseE5DocumentId, initialProbe.ModelDocumentId);
        Assert.IsTrue(initialProbe.ModelFieldCount >= 7);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        await page.GetByTestId("document-ribbon-tab-references").ClickAsync();
        await page.GetByTestId("document-insert-caption").ClickAsync();
        await WaitForCaptionCountAsync(page, 1);
        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForCaptionCountAsync(page, 0);
        await page.GetByTestId("document-redo").ClickAsync();
        await WaitForCaptionCountAsync(page, 1);

        await page.GetByTestId("document-insert-cross-reference").ClickAsync();
        await WaitForCrossReferenceCountAsync(page, 1);
        await WaitForCrossReferenceTextAsync(page, InitialHeadingText);
        await page.GetByTestId("document-insert-table-of-figures").ClickAsync();
        await WaitForTableOfFiguresAsync(page);
        await page.GetByTestId("document-insert-bibliography").ClickAsync();
        await WaitForBibliographyAsync(page);
        await page.GetByTestId("document-update-fields").ClickAsync();
        await WaitForUpdatedFieldsAsync(page);

        var renameProbe = await ExecuteCanvasCommandAsync(page, "replacerange", new
        {
            blockId = "canvas-e5-heading",
            start = 0,
            end = InitialHeadingText.Length,
            text = UpdatedHeadingText
        });
        Assert.IsTrue(renameProbe.Handled, renameProbe.Debug);
        Assert.IsTrue(renameProbe.Changed, renameProbe.Debug);
        Assert.AreEqual(UpdatedHeadingText, await ReadBlockTextAsync(page, "canvas-e5-heading"));
        await page.GetByTestId("document-update-fields").ClickAsync();
        await WaitForCrossReferenceTextAsync(page, UpdatedHeadingText);
        var renamedProbe = await ReadProbeAsync(page);
        Assert.AreEqual(UpdatedHeadingText, renamedProbe.CrossReferenceText);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = renamedPath,
            Type = ScreenshotType.Png
        });

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE5DocumentId}&showToolbar=true");
        await WaitForPhaseE5ReadyAsync(page);
        await WaitForUpdatedFieldsAsync(page);

        var reloadedProbe = await ReadProbeAsync(page);
        Assert.AreEqual(PhaseE5DocumentId, reloadedProbe.ModelDocumentId);
        Assert.AreEqual(1, reloadedProbe.CaptionCount);
        Assert.IsTrue(reloadedProbe.CrossReferenceCount >= 1);
        Assert.AreEqual(UpdatedHeadingText, reloadedProbe.CrossReferenceText);
        Assert.IsTrue(reloadedProbe.TableOfFiguresText.Contains("Figure", StringComparison.Ordinal));
        Assert.IsTrue(reloadedProbe.BibliographyText.Contains("Elena Novak", StringComparison.Ordinal));

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE5_CanvasFieldsCrossReferencesCaptionsAndBibliographyPersist),
            seedDocumentId = PhaseE5DocumentId,
            userActions = new[]
            {
                "Open the phase E5 canvas fields seed document with the production toolbar.",
                "Insert a figure caption, undo it, and redo it through the shared canvas history.",
                "Insert a cross-reference, generated table of figures, bibliography, and refresh all fields.",
                "Rename the cross-reference heading target through the canvas command bridge and refresh all fields.",
                "Save, navigate away, navigate back, and verify the generated field structure survives reload."
            },
            expectedVisibleChanges = "The References ribbon creates updateable field runs for captions, cross-reference, table of figures, and bibliography; cross-reference text follows the renamed heading after update and remains available after save and reload.",
            screenshotPaths = new[] { beforePath, renamedPath, afterPath },
            initialProbe,
            renamedProbe,
            reloadedProbe,
            contentMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(renamedPath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE5DocumentAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={PhaseE5DocumentId}&showToolbar=true", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await WaitForPhaseE5ReadyAsync(page);
    }

    private static Task WaitForPhaseE5ReadyAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => {
                const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return hostReady
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-e5-canvas-fields'
                    && Number(first.getAttribute('data-canvas-model-field-count') || '0') >= 7;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static Task WaitForCaptionCountAsync(IPage page, int expected)
        => page.WaitForFunctionAsync(
            """
            expected => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-caption-count') || '0') === expected
            """,
            expected,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForCrossReferenceCountAsync(IPage page, int expected)
        => page.WaitForFunctionAsync(
            """
            expected => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-cross-reference-count') || '0') >= expected
            """,
            expected,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForTableOfFiguresAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => (document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-table-of-figures-text') || '').includes('Figure')
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForBibliographyAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => (document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-bibliography-text') || '').includes('Elena Novak')
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForCrossReferenceTextAsync(IPage page, string expected)
        => page.WaitForFunctionAsync(
            """
            expected => (document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-cross-reference-text') || '') === expected
            """,
            expected,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<string> ReadBlockTextAsync(IPage page, string blockId)
        => CanvasInteropBridge.EvaluateAsync<string>(page,
            """
            blockId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return '';
                }

                const interop = window.__tmCanvasInterop;
                const model = JSON.parse(interop.getModelJson(handle) || '{}');
                const blocks = Array.isArray(model?.body?.blocks) ? model.body.blocks : [];
                const block = blocks.find(candidate => String(candidate?.id || '') === String(blockId || ''));
                return (Array.isArray(block?.content?.runs) ? block.content.runs : [])
                    .map(run => String(run?.field?.displayText ?? run?.field?.cachedResult ?? run?.text ?? ''))
                    .join('');
            }
            """,
            blockId);

    private static async Task WaitForUpdatedFieldsAsync(IPage page)
    {
        try
        {
            await page.WaitForFunctionAsync(
                """
                () => {
                    const first = document.querySelector('[data-testid="document-canvas-page"]');
                    return Number(first?.getAttribute('data-canvas-model-caption-count') || '0') === 1
                        && Number(first?.getAttribute('data-canvas-cross-reference-count') || '0') >= 1
                        && (first?.getAttribute('data-canvas-table-of-figures-text') || '').includes('Figure')
                        && (first?.getAttribute('data-canvas-bibliography-text') || '').includes('Elena Novak');
                }
                """,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (TimeoutException ex)
        {
            var probe = await ReadProbeAsync(page);
            Assert.Fail($"Timed out waiting for phase E5 updated field diagnostics. Probe: {JsonSerializer.Serialize(probe, new JsonSerializerOptions(JsonSerializerDefaults.Web))}. {ex.Message}");
        }
    }

    private static Task<PhaseE5Probe> ReadProbeAsync(IPage page)
        => page.EvaluateAsync<PhaseE5Probe>(
            """
            () => {
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return {
                    modelDocumentId: first?.getAttribute('data-canvas-model-document-id') || '',
                    modelFieldCount: Number(first?.getAttribute('data-canvas-model-field-count') || '0'),
                    captionCount: Number(first?.getAttribute('data-canvas-model-caption-count') || '0'),
                    crossReferenceCount: Number(first?.getAttribute('data-canvas-cross-reference-count') || '0'),
                    tableOfFiguresText: first?.getAttribute('data-canvas-table-of-figures-text') || '',
                    bibliographyText: first?.getAttribute('data-canvas-bibliography-text') || '',
                    crossReferenceText: first?.getAttribute('data-canvas-cross-reference-text') || ''
                };
            }
            """);

    private static async Task WaitForSaveBoundaryAsync(IPage page)
    {
        try
        {
            await page.WaitForFunctionAsync(
                """
                () => {
                    const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                    const lastSaved = document.querySelector('[data-testid="document-last-saved"]')?.textContent || '';
                    const pending = document.querySelector('[data-testid="document-pending-status"]')?.textContent || '';
                    const dirty = document.querySelector('[data-testid="document-dirty-status"]')?.textContent || '';
                    const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                    return saveButtonDisabled === false
                        && pending.trim().length === 0
                        && dirty.trim().length === 0
                        && (saveMessage.trim().length > 0 || lastSaved.trim().length > 0);
                }
                """,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (TimeoutException ex)
        {
            var state = await page.EvaluateAsync<PhaseE5SaveDebugState>(
                """
                () => ({
                    saveMessage: document.querySelector('[data-testid="document-save-message"]')?.textContent || '',
                    lastSaved: document.querySelector('[data-testid="document-last-saved"]')?.textContent || '',
                    pending: document.querySelector('[data-testid="document-pending-status"]')?.textContent || '',
                    dirty: document.querySelector('[data-testid="document-dirty-status"]')?.textContent || '',
                    saveDisabled: document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true,
                    statusBar: document.querySelector('[data-testid="document-status-bar"]')?.textContent || '',
                    bodyHasSaveStatus: /Saved|Autosaved|Uloženo|Automaticky uloženo|Enregistré/i.test(document.body.textContent || '')
                })
                """);

            Assert.Fail($"Timed out waiting for the phase E5 save boundary. State: {JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web))}. {ex.Message}");
        }
    }

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private static async Task<PhaseE5CommandProbe> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await CanvasInteropBridge.EvaluateAsync<PhaseE5CommandProbe>(page,
            """
            ({ commandId, json }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const raw = interop.execCommand(handle, commandId, json);
                const parsed = JSON.parse(raw || '{}');
                return {
                    handled: parsed?.handled === true,
                    changed: parsed?.result?.changed === true,
                    operation: String(parsed?.result?.operation || parsed?.result?.commandId || ''),
                    debug: JSON.stringify(parsed)
                };
            }
            """,
            new { commandId, json });
    }

    private static string CreateOutputDirectory(string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phasee5-fields",
            viewport);
        Directory.CreateDirectory(output);
        return output;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        return current ?? new DirectoryInfo(Directory.GetCurrentDirectory());
    }

    /// <summary>Browser-side phase E5 field state.</summary>
    public sealed class PhaseE5Probe
    {
        /// <summary>Document id reported by the first canvas page.</summary>
        public string ModelDocumentId { get; set; } = string.Empty;

        /// <summary>Total field count reported by the canvas model diagnostics.</summary>
        public int ModelFieldCount { get; set; }

        /// <summary>Total generated caption count.</summary>
        public int CaptionCount { get; set; }

        /// <summary>Total cross-reference field count.</summary>
        public int CrossReferenceCount { get; set; }

        /// <summary>Generated table-of-figures text.</summary>
        public string TableOfFiguresText { get; set; } = string.Empty;

        /// <summary>Generated bibliography text.</summary>
        public string BibliographyText { get; set; } = string.Empty;

        /// <summary>Resolved cross-reference text.</summary>
        public string CrossReferenceText { get; set; } = string.Empty;
    }

    private sealed class PhaseE5SaveDebugState
    {
        public string SaveMessage { get; set; } = string.Empty;

        public string LastSaved { get; set; } = string.Empty;

        public string Pending { get; set; } = string.Empty;

        public string Dirty { get; set; } = string.Empty;

        public bool SaveDisabled { get; set; }

        public string StatusBar { get; set; } = string.Empty;

        public bool BodyHasSaveStatus { get; set; }
    }

    private sealed class PhaseE5CommandProbe
    {
        public bool Handled { get; set; }

        public bool Changed { get; set; }

        public string Operation { get; set; } = string.Empty;

        public string Debug { get; set; } = string.Empty;
    }
}
