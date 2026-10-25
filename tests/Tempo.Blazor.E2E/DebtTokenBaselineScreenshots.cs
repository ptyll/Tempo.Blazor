using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Light/dark screenshots of every demo route that renders a component still using an undeclared
/// <c>--tm-*</c> alias (the debt tracked by <c>DesignTokenDefinitionTests</c>). This is the visual
/// guard for retiring that debt onto declared tokens.
///
/// Screenshots land in <c>__screenshots__/debt-tokens/{light,dark}/</c>; set
/// <c>TM_SCREENSHOT_PHASE</c> to capture a before/after pair.
/// </summary>
[TestClass]
public class DebtTokenBaselineScreenshots : BaselineGeneratorTestBase
{
    private static readonly (string Route, string Name)[] Routes =
    [
        ("/activity",         "activity"),          // rich-editor
        ("/charts",           "charts"),            // chart
        ("/chat",             "chat"),              // chat
        ("/color-picker",     "color-picker"),      // color-palette
        ("/dashboard",        "dashboard"),         // dashboard
        ("/data-display",     "data-display"),      // multi-view-list
        ("/data-table",       "data-table"),        // data-table, tree-list
        ("/dock-manager",     "dock-manager"),      // dock-manager
        ("/email-templates",  "email-templates"),   // modal, alert
        ("/feedback",         "feedback"),          // alert, chip, copy-button, drawer, dynamic-form,
                                                    // notification-bell, popover, split-button, stepper, toast
        ("/file-manager",     "file-manager"),      // file-manager
        ("/forms",            "forms"),             // expression-editor, inline-edit, password-strength
        ("/import-export",    "import-export"),     // modal
        ("/kanban",           "kanban"),            // kanban
        ("/layout",           "layout"),            // command-palette, keyboard-shortcuts, section
        ("/modal-dialog",     "modal-dialog"),      // modal
        ("/multi-view-list",  "multi-view-list"),   // multi-view-list
        ("/new-components",   "new-components"),     // menu
        ("/pickers",          "pickers"),           // attachment-manager, calendar-view
        ("/pivot-table",      "pivot-table"),       // pivot-table
        ("/richtext",         "richtext"),          // rich-editor, modal
        ("/scheduler",        "scheduler"),         // scheduler
        ("/toolbar-forms",    "toolbar-forms"),     // toolbar
        ("/tree-list",        "tree-list"),         // tree-list
    ];

    public static IEnumerable<object[]> RouteData => Routes.Select(r => new object[] { r.Route, r.Name });

    private static string OutputRoot
    {
        get
        {
            var phase = Environment.GetEnvironmentVariable("TM_SCREENSHOT_PHASE");
            var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "debt-tokens");
            return string.IsNullOrWhiteSpace(phase) ? dir : Path.Combine(dir, phase);
        }
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }
            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    private static async Task CaptureAsync(IPage page, string theme, string name)
    {
        var dir = Path.Combine(OutputRoot, theme);
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, $"{name}.png"),
            Type = ScreenshotType.Png,
            FullPage = true,
        });
    }

    [TestMethod]
    [DynamicData(nameof(RouteData))]
    [Description("Captures the light and dark rendering of a demo route for the token-debt review")]
    public async Task Capture_LightAndDark(string route, string name)
    {
        // The class is one dynamic row per demo route. A phase that only restyles one
        // component sets TM_DEBT_SCREENSHOT to that row's name so the other routes are
        // not rewritten.
        var only = Environment.GetEnvironmentVariable("TM_DEBT_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(only)
            && !string.Equals(name, only, StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive($"Skipped: TM_DEBT_SCREENSHOT={only}.");
        }

        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{route}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.WaitForTimeoutAsync(800);

        await CaptureAsync(page, "light", name);

        await page.Locator("button[title*='dark' i]:visible").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(800);

        await CaptureAsync(page, "dark", name);
    }
}
