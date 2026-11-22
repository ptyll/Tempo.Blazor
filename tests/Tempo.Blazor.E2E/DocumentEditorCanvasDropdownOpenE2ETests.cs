using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 13 — nativní selecty toolbaru musí jít otevřít myší. Root cause: <c>@onpointerdown:preventDefault</c>
/// + <c>@onmousedown:preventDefault</c> na nativním <c>&lt;select&gt;</c> ruší default akci, která otevírá
/// popup (a zároveň brání fokusu), takže dropdown nešel otevřít kliknutím. Vzor byl zkopírován z tlačítek,
/// kde je správný (drží fokus v canvasu). Ve float toolbaru navíc pointerdown bublal do sekce s preventDefault,
/// takže select tam dostal <c>stopPropagation</c> místo <c>preventDefault</c>.
///
/// Nativní popup selectu kreslí OS a nejde ověřit ze screenshotu; observabilní kontrakt „dropdown se otevře"
/// je fokus po skutečném kliknutí myší: preventDefault na mousedown blokuje i fokus, takže
/// <c>document.activeElement === select</c> po kliku je přesně to, co fix mění z RED na GREEN.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasDropdownOpenE2ETests : WasmTestBase
{
    private const string ContractDocumentId = "contract-demo";
    private const string AgreementBlockId = "contract-scope";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    /// <summary>
    /// Hlavní toolbar: klik myší na font-size select ho musí fokusovat (= popup se otevře), výběr hodnoty se
    /// aplikuje na původní canvas selection a ta zůstává zachovaná — bold pak dál funguje na tentýž výběr.
    /// </summary>
    [TestMethod]
    public async Task Phase13_MainToolbar_FontSizeDropdown_OpensByMouseAndAppliesToSelection()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, AgreementBlockId);

        var output = CreateOutputDirectory("phase13-main-toolbar-font-size");
        await SelectCanvasTextRangeAsync(page, AgreementBlockId, 0, 12);

        // Skutečný klik myší (trusted event) — s preventDefault na mousedown by select fokus nedostal.
        await page.GetByTestId("document-font-size").ClickAsync();
        var focusedTestId = await ReadActiveElementTestIdAsync(page);
        await ScreenshotAsync(page, Path.Combine(output, "00-font-size-clicked-open.png"));
        Assert.AreEqual(
            "document-font-size",
            focusedTestId,
            "Fáze 13 regrese: klik na font-size select ho nefokusoval — preventDefault na pointerdown/mousedown brání otevření nativního dropdownu.");

        // Selection v canvasu nesmí klikem do toolbaru zmizet.
        await AssertSelectionStillVisibleAsync(page, AgreementBlockId);

        // Výběr hodnoty z dropdownu se aplikuje na původní výběr.
        await page.GetByTestId("document-font-size").SelectOptionAsync("24");
        await WaitForCommandValueAsync(page, "fontsize", "24");
        await AssertSelectionStillVisibleAsync(page, AgreementBlockId);
        await ScreenshotAsync(page, Path.Combine(output, "01-font-size-applied.png"));

        // Bold dál funguje na tentýž výběr (selection přežila celý dropdown roundtrip).
        await page.GetByTestId("document-bold").ClickAsync();
        await WaitForCommandStateAsync(page, "bold", "active");
        await Assertions.Expect(page.GetByTestId("document-bold")).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5_000 });
        await AssertSelectionStillVisibleAsync(page, AgreementBlockId);
        await ScreenshotAsync(page, Path.Combine(output, "02-bold-after-dropdown.png"));

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                problem = "Fáze 13: font-size dropdown v hlavním toolbaru nešel otevřít myší (preventDefault na mousedown/pointerdown)",
                focusedTestId,
                note = "Nativní popup kreslí OS a na screenshotu není vidět; kontrakt otevření = fokus selectu po trusted mouse click."
            }, JsonWebIndented));
    }

    /// <summary>
    /// Hlavní toolbar — font-family a change-case selecty (stejný root cause jako font-size) + edge case:
    /// otevření dropdownu a únik Escape nesmí nic aplikovat ani zrušit canvas selection.
    /// </summary>
    [TestMethod]
    public async Task Phase13_MainToolbar_FontFamilyAndChangeCase_OpenByMouse_EscapeKeepsSelection()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, AgreementBlockId);

        var output = CreateOutputDirectory("phase13-main-toolbar-family-case");
        await SelectCanvasTextRangeAsync(page, AgreementBlockId, 0, 12);
        var fontSizeBefore = await ReadCommandValueAsync(page, "fontsize");

        await page.GetByTestId("document-font-family").ClickAsync();
        var familyFocus = await ReadActiveElementTestIdAsync(page);
        await ScreenshotAsync(page, Path.Combine(output, "00-font-family-clicked-open.png"));
        Assert.AreEqual("document-font-family", familyFocus, "klik na font-family select ho musí fokusovat (dropdown se otevírá).");

        // Edge case: dropdown otevřený a opuštěný Escape — žádná změna hodnoty, selection přežívá.
        await page.Keyboard.PressAsync("Escape");
        await AssertSelectionStillVisibleAsync(page, AgreementBlockId);
        var fontSizeAfterEscape = await ReadCommandValueAsync(page, "fontsize");
        Assert.AreEqual(fontSizeBefore, fontSizeAfterEscape, "Escape z otevřeného dropdownu nesmí změnit formátování.");

        var changeCase = page.GetByTestId("document-change-case");
        if (await changeCase.CountAsync() > 0)
        {
            await changeCase.ClickAsync();
            var caseFocus = await ReadActiveElementTestIdAsync(page);
            await ScreenshotAsync(page, Path.Combine(output, "01-change-case-clicked-open.png"));
            Assert.AreEqual("document-change-case", caseFocus, "klik na change-case select ho musí fokusovat (dropdown se otevírá).");
            await page.Keyboard.PressAsync("Escape");
            await AssertSelectionStillVisibleAsync(page, AgreementBlockId);
        }

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                problem = "Fáze 13: font-family + change-case dropdowny v hlavním toolbaru",
                familyFocus,
                changeCaseRendered = await changeCase.CountAsync(),
                edgeCase = "Escape z otevřeného dropdownu nemění formátování a drží selection"
            }, JsonWebIndented));
    }

    /// <summary>
    /// Float (mini) toolbar: klik na mini font-size select ho musí fokusovat, NESMÍ zavřít mini toolbar
    /// (stopPropagation drží pointerdown mimo sekci s preventDefault) a výběr hodnoty se aplikuje přes
    /// zachycený selection snapshot; mini-bold pak dál funguje na tentýž výběr.
    /// </summary>
    [TestMethod]
    public async Task Phase13_FloatToolbar_MiniFontSizeDropdown_OpensByMouse_KeepsToolbarAndSelection()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page, ContractDocumentId, AgreementBlockId);

        var output = CreateOutputDirectory("phase13-float-toolbar-font-size");
        await SelectCanvasTextRangeAsync(page, AgreementBlockId, 0, 12);

        var miniToolbar = page.GetByTestId("document-mini-toolbar");
        await Assertions.Expect(miniToolbar).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Skutečný klik myší (trusted event) — s preventDefault na mousedown by select fokus nedostal.
        // Screenshoty v tomto testu jsou VIEWPORT (page.ScreenshotAsync): locator screenshot celého ~3900px
        // vysokého demo elementu scrolluje/stitchuje stránku a scroll shodí floating mini toolbar,
        // takže by test měřil artefakt screenshotu, ne chování dropdownu.
        await page.GetByTestId("document-mini-font-size").ClickAsync();
        var focusedTestId = await ReadActiveElementTestIdAsync(page);
        await ViewportScreenshotAsync(page, Path.Combine(output, "00-mini-font-size-clicked-open.png"));
        Assert.AreEqual(
            "document-mini-font-size",
            focusedTestId,
            "Fáze 13 regrese: klik na mini font-size select ho nefokusoval — dropdown ve float toolbaru se neotevírá.");

        // Klik do selectu nesmí mini toolbar zavřít ani shodit canvas selection.
        await Assertions.Expect(miniToolbar).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await AssertSelectionStillVisibleAsync(page, AgreementBlockId);

        await page.GetByTestId("document-mini-font-size").SelectOptionAsync("24");
        await WaitForCommandValueAsync(page, "fontsize", "24");
        await AssertSelectionStillVisibleAsync(page, AgreementBlockId);
        await ViewportScreenshotAsync(page, Path.Combine(output, "01-mini-font-size-applied.png"));

        var miniBold = page.GetByTestId("document-mini-bold");
        if (await miniBold.IsVisibleAsync())
        {
            await miniBold.ClickAsync();
            await WaitForCommandStateAsync(page, "bold", "active");
            await AssertSelectionStillVisibleAsync(page, AgreementBlockId);
            await ViewportScreenshotAsync(page, Path.Combine(output, "02-mini-bold-after-dropdown.png"));
        }

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                problem = "Fáze 13: mini font-size dropdown ve float toolbaru (preventDefault na selectu + bublání do sekce s preventDefault)",
                focusedTestId,
                note = "stopPropagation na selectu drží pointerdown mimo sekci; aplikace jde přes zachycený selection snapshot."
            }, JsonWebIndented));
    }

    // ─── Helpers (vzor DocumentEditorCanvasUxFixE2ETests) ────────────────────

    private async Task OpenDocumentAsync(IPage page, string documentId, string blockId)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={documentId}", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 120_000 });
        await page.WaitForFunctionAsync(
            """
            blockId => document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`).length >= 1
                && document.querySelector('[data-testid="document-bold"]')
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 60_000 });
    }

    private static async Task SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        var start = await ReadCanvasPointAsync(page, blockId, startOffset);
        var end = await ReadCanvasPointAsync(page, blockId, endOffset);
        await page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)end.X, (float)end.Y, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await AssertSelectionStillVisibleAsync(page, blockId);
    }

    private static Task AssertSelectionStillVisibleAsync(IPage page, string blockId)
        => page.WaitForFunctionAsync(
            """
            blockId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-selection-collapsed') === 'false'
                    && document.querySelectorAll('[data-testid="document-canvas-selection-rect"]').length >= 1;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<string> ReadActiveElementTestIdAsync(IPage page)
        => page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid') || ''");

    private static Task<string> ReadCommandValueAsync(IPage page, string commandId)
        => page.EvaluateAsync<string>(
            """
            commandId => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute(`data-canvas-command-${commandId}-value`) || ''
            """,
            commandId);

    private static Task WaitForCommandStateAsync(IPage page, string commandId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([commandId, expected]) => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute(`data-canvas-command-${commandId}-state`) === expected;
            }
            """,
            new object[] { commandId, expected },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForCommandValueAsync(IPage page, string commandId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([commandId, expected]) => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return (root?.getAttribute(`data-canvas-command-${commandId}-value`) || '') === expected;
            }
            """,
            new object[] { commandId, expected },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<CanvasPoint> ReadCanvasPointAsync(IPage page, string blockId, int offset)
        => page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId, offset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => {
                        const rect = node.getBoundingClientRect();
                        const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                        const end = Number(node.getAttribute('data-canvas-end-offset') || '0');
                        return { rect, start, end };
                    })
                    .filter(item => item.end > item.start);
                if (!rects.length) throw new Error(`No canvas text rects found for ${blockId}.`);
                const target = rects.find(item => offset >= item.start && offset < item.end) || rects[0];
                const ratio = Math.max(0, Math.min(1, (offset - target.start) / Math.max(1, target.end - target.start)));
                return {
                    x: target.rect.left + Math.max(1, target.rect.width * ratio),
                    y: target.rect.top + target.rect.height / 2
                };
            }
            """,
            new object[] { blockId, offset });

    private static Task ScreenshotAsync(IPage page, string path)
        => page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png
        });

    /// <summary>Viewport screenshot — na rozdíl od locator screenshotu nescrolluje stránku, takže neshodí
    /// floating mini toolbar (scroll handler enginu floating UI zavírá).</summary>
    private static Task ViewportScreenshotAsync(IPage page, string path)
        => page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png
        });

    private static string CreateOutputDirectory(string scenario)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "TestResults", "document-editor-canvas",
            "phase13-dropdowns", "2026-07-10", scenario);
        Directory.CreateDirectory(output);
        return output;
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

    private static readonly JsonSerializerOptions JsonWebIndented =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed class CanvasPoint
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
    }
}
