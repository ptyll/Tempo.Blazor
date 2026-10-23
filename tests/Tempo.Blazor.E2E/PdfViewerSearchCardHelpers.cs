using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// FZE-11 review: the floating search card starts closed at load, so E2E tests that drive
/// a search open it explicitly first — through the toolbar's search toggle, or through the
/// "More" menu when the toggle has overflowed (collapsed/inert) at a narrow toolbar.
/// </summary>
internal static class PdfViewerSearchCardHelpers
{
    /// <summary>
    /// Opens the search card in <paramref name="section"/> and waits until the search input
    /// is visible. Returns the input for the caller's query.
    /// </summary>
    public static async Task<ILocator> OpenSearchCardAsync(this IPage page, ILocator section)
    {
        var toggle = section.Locator("[data-testid='pdf-search-toggle']").First;
        if (await toggle.IsVisibleAsync())
        {
            await toggle.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        }
        else
        {
            // The toggle renders collapsed (inert) and lives only in the "More" menu.
            await section.Locator(".tm-toolbar-more").First
                .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
            await section.Locator(".tm-toolbar-more__item").Filter(new() { HasTextString = "Search" }).First
                .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        }

        var input = section.Locator("[data-testid='pdf-search-input']").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return input;
    }
}
