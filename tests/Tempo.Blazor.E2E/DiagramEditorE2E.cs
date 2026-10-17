using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared helpers for tests that need the diagram editor at a SPECIFIC container band.
/// <para>
/// The editor resolves its presentation from its own container (F1), and the demo page's
/// content column keeps every default viewport in the TABLET band (920px at 1280, 1080 even
/// at 1440) — while the mobile band took over below 768px in phase 7. Tests written for the
/// desktop chrome (docked toolbox, always-visible toolbar buttons) or explicitly for the
/// tablet/mobile presentation therefore pin the editor container, exactly like a full-bleed
/// host would, instead of relying on the viewport.
/// </para>
/// </summary>
internal static class DiagramEditorE2E
{
    /// <summary>
    /// Pins the editor container to an explicit width and waits until the editor resolves the
    /// requested layout (the editor mirrors the shell resolution on data-editor-layout).
    /// </summary>
    public static async Task ForceEditorWidthAsync(IPage page, double width, string expectedLayout)
    {
        var target = Math.Max(320, width - 160);
        await page.EvaluateAsync(
            """
            (w) => {
                const editor = document.querySelector('.tm-diagram-editor');
                const host = editor?.parentElement;
                if (!host) throw new Error('editor host not found');
                host.style.width = w + 'px';
                host.style.maxWidth = w + 'px';
            }
            """,
            target);
        await page.WaitForFunctionAsync(
            """
            (expected) => document.querySelector('.tm-diagram-editor')?.getAttribute('data-editor-layout') === expected
            """,
            expectedLayout,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.Locator(".tm-diagram-editor").First.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(250);
    }

    /// <summary>
    /// Pins the editor to the DESKTOP band (≥ 1200 container width): a wide viewport plus the
    /// width pin, because the demo gutters would keep a 1280 viewport in the tablet band.
    /// </summary>
    public static async Task PinDesktopAsync(IPage page)
    {
        await page.SetViewportSizeAsync(1600, 900);
        await ForceEditorWidthAsync(page, 1600, "desktop");
    }

    /// <summary>
    /// Phase 4: the command row is a core TmToolbar — a command without room collapses into the
    /// "More" menu (its bar button is inert). Click it wherever it currently is.
    /// </summary>
    public static async Task OpenToolbarCommandAsync(IPage page, string barSelector, string moreEntryText)
    {
        var barButton = page.Locator(barSelector).First;
        if (await barButton.CountAsync() > 0 && await barButton.IsVisibleAsync() && await barButton.IsEnabledAsync())
        {
            await barButton.ClickAsync();
            return;
        }

        await page.Locator(".tm-toolbar-more").First.ClickAsync();
        var entry = page.Locator(".tm-toolbar-more__item")
            .Filter(new() { HasText = moreEntryText }).First;
        await entry.ScrollIntoViewIfNeededAsync();
        await entry.ClickAsync();
    }
}
