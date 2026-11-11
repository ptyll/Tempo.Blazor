using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan d67b33af phase 9 — the <c>/ai-agent-visualizations</c> demo page drives EVERY agent surface of the package
/// from ONE simulated run. The assertions read behaviour rather than markup: the generator plays, pauses, changes its
/// rate and resets, the lanes take a block from the keyboard and switch to their table, the graph walks its nodes with
/// the arrow keys, the delegate board takes a card by pointer drag AND by its Assign menu, a remembered fact is
/// forgotten and undone, every conflict is settled from the keyboard before the merge, the autonomy dial steps by its
/// own keys, and every graphical surface reaches a keyboard-readable table. A scoped axe scan runs per section.
/// </summary>
public abstract class AiAgentVisualizationsPageBase : PlaywrightTestBase
{
    protected const string SimulatorSection = "[data-testid='ai-viz-simulator']";
    protected const string BadgeSection = "[data-testid='ai-viz-badge-section']";
    protected const string LanesSection = "[data-testid='ai-viz-lanes-section']";
    protected const string GraphSection = "[data-testid='ai-viz-graph-section']";
    protected const string FanOutSection = "[data-testid='ai-viz-fan-out-section']";
    protected const string AttemptsSection = "[data-testid='ai-viz-attempts-section']";
    protected const string HandoffSection = "[data-testid='ai-viz-handoff-section']";
    protected const string FallbackSection = "[data-testid='ai-viz-fallback-section']";
    protected const string AutonomySection = "[data-testid='ai-viz-autonomy-section']";
    protected const string MemorySection = "[data-testid='ai-viz-memory-section']";
    protected const string DelegateSection = "[data-testid='ai-viz-delegate-section']";
    protected const string RunsSection = "[data-testid='ai-viz-runs-section']";
    protected const string ConflictSection = "[data-testid='ai-viz-conflict-section']";
    protected const string PresenceSection = "[data-testid='ai-viz-presence-section']";

    protected static readonly string[] AllSections =
    [
        "ai-viz-simulator", "ai-viz-badge-section", "ai-viz-lanes-section", "ai-viz-graph-section", "ai-viz-fan-out-section",
        "ai-viz-attempts-section", "ai-viz-handoff-section", "ai-viz-fallback-section", "ai-viz-autonomy-section",
        "ai-viz-memory-section", "ai-viz-delegate-section", "ai-viz-runs-section", "ai-viz-conflict-section",
        "ai-viz-presence-section",
    ];

    protected string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "ai-agent-visualizations");

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

    /// <summary>Opens the page with the culture pinned to English, the way every AI package spec does.</summary>
    protected async Task<IPage> OpenAsync(int width = 1440, int height = 1100, bool coarsePointer = false,
        bool reduceMotion = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            ReducedMotion = reduceMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            IgnoreHTTPSErrors = true,
            HasTouch = coarsePointer,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/ai-agent-visualizations");
        await page.WaitForSelectorAsync(
            $"{SimulatorSection} [data-testid='ai-viz-play']",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    /// <summary>The text of one test id, trimmed.</summary>
    protected static async Task<string> TextAsync(IPage page, string testId)
        => ((await page.Locator($"[data-testid='{testId}']").First.TextContentAsync()) ?? string.Empty).Trim();

    /// <summary>
    /// One of the page's graphic/table switches. It is a plain ACTION button: its label names the presentation the
    /// next press shows, so it must not also carry <c>aria-pressed</c> — a screen reader would hear "Show the visual
    /// view, pressed" while the table is on screen (WCAG 4.1.2; the APG keeps a toggle button's name fixed).
    /// </summary>
    protected static async Task AssertSwitchNamesItsActionAsync(IPage page, string testId, string expectedLabel)
    {
        var switchButton = page.Locator($"[data-testid='{testId}']");

        Assert.IsNull(await switchButton.GetAttributeAsync("aria-pressed"),
            $"{testId} must stay a plain action button: its label already names the presentation");
        StringAssert.Contains(await switchButton.InnerTextAsync(), expectedLabel,
            $"{testId} names the presentation the next press shows");
    }

    protected static async Task ScrollToAsync(IPage page, string selector)
        => await page.Locator(selector).ScrollIntoViewIfNeededAsync();

    /// <summary>
    /// Brings a section to the TOP of the viewport (a plain scroll-into-view does nothing when the element is already
    /// partly visible, which framed the wrong section in a reference shot).
    /// </summary>
    protected static async Task ScrollToTopAsync(IPage page, string selector)
    {
        // The demo's top bar is sticky, so a plain block-start scroll parks the section's heading UNDER it. The scroll
        // is INSTANT on purpose: a smooth scroll is still animating when the screenshot is taken, which framed the
        // PREVIOUS section in the reference shots.
        await page.Locator(selector).EvaluateAsync(
            "el => { el.scrollIntoView({ block: 'start', behavior: 'instant' }); window.scrollBy({ top: -112, behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>Starts the generator and waits until the run has applied changes.</summary>
    protected static async Task PlayAsync(IPage page)
    {
        await ScrollToAsync(page, SimulatorSection);
        await page.Locator("[data-testid='ai-viz-play']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector(\"[data-testid='ai-viz-events']\")?.textContent) > 0",
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    /// <summary>Switches the demo's own theme through the header toggle, so the whole frame repaints.</summary>
    protected static async Task ToggleThemeAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForTimeoutAsync(600);
    }

    /// <summary>Frames the section under review: a full-page shot of a long demo page proves nothing.</summary>
    protected async Task ShootAsync(IPage page, string name)
    {
        await page.WaitForTimeoutAsync(300);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = false,
        });
    }

    /// <summary>A scoped axe scan; a color-contrast failure reports the colour pair, not just the selector.</summary>
    protected static async Task AssertNoSeriousAxeViolationsAsync(IPage page, params string[] testIds)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        foreach (var testId in testIds)
        {
            var selector = $"[data-testid='{testId}']";
            var violations = await page.EvaluateAsync<string[]>(
                """
                async (selector) => {
                    const host = document.querySelector(selector);
                    const result = await axe.run(host, {
                        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                        resultTypes: ['violations']
                    });
                    const describe = (node) => {
                        const el = document.querySelector(node.target.join(' '));
                        if (!el) return node.target.join(' ');
                        const style = getComputedStyle(el);
                        let background = style.backgroundColor;
                        let parent = el.parentElement;
                        while (parent && (background === 'rgba(0, 0, 0, 0)' || background === 'transparent')) {
                            background = getComputedStyle(parent).backgroundColor;
                            parent = parent.parentElement;
                        }
                        return `${node.target.join(' ')} [color ${style.color} on ${background}]`;
                    };
                    return result.violations
                        .filter(v => v.impact === 'critical' || v.impact === 'serious')
                        .map(v => `${v.id}: ${v.help} :: ${v.nodes.slice(0, 3).map(describe).join(' | ')}`);
                }
                """,
                selector);

            Assert.AreEqual(0, violations.Length, $"axe [{testId}]: " + string.Join(" | ", violations));
        }
    }
}
