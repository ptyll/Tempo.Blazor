using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan <c>eadbf967</c> phase 5 screenshot GENERATOR: freezes the schema-driven inspector
/// (docs/modeling-authoring.md §5) on the desktop dock, the tablet side sheet and the mobile
/// Inspector tab — the required-field ERRORS in light and dark (instructions cleared and blurred,
/// the role reference removed) and the LIST controls (the duration row plus a structured list with
/// its one item), plus one Czech desktop capture of the localized errors and issue rows.
/// <para>
/// Off unless <c>TM_WRITE_BASELINES=1</c> (the shared <see cref="BaselineGeneratorTestBase"/> gate).
/// Not part of the Smoke lane.
/// </para>
/// </summary>
[TestClass]
public sealed class ModelingPropertySchemaPhase5Screenshots : BaselineGeneratorTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";
    private const string StepSchemaUrl = "?editing=1&scenario=step-attributes";
    private const string DisableMotionStyle = "*{animation:none!important;transition:none!important;caret-color:transparent!important}";

    private static string OutputDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                directory = directory.Parent!;
            }

            var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "modeling");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>The three responsive bands the editor resolves from ITS container.</summary>
    private enum Band
    {
        Desktop,
        Tablet,
        Mobile
    }

    /// <summary>What the capture is OF: the two required fields in error, or the list controls.</summary>
    private enum Shot
    {
        RequiredErrors,
        Lists
    }

    // ── U11: the required-field errors, light and dark, on all three bands ───

    [TestMethod]
    [Description("1440x900 light: instructions cleared + blurred and the role reference removed, so both required fields show their error")]
    public async Task Capture_SchemaError_Desktop_Light()
        => await CaptureAsync("inspector-schema-error-desktop-1440x900", 1440, 900, touch: false, dark: false, Band.Desktop, Shot.RequiredErrors);

    [TestMethod]
    [Description("1440x900 dark: the same required-field errors")]
    public async Task Capture_SchemaError_Desktop_Dark()
        => await CaptureAsync("inspector-schema-error-desktop-dark-1440x900", 1440, 900, touch: false, dark: true, Band.Desktop, Shot.RequiredErrors);

    [TestMethod]
    [Description("1024x768 light: the tablet band's inspector side sheet with both required-field errors")]
    public async Task Capture_SchemaError_Tablet_Light()
        => await CaptureAsync("inspector-schema-error-tablet-1024x768", 1024, 768, touch: true, dark: false, Band.Tablet, Shot.RequiredErrors);

    [TestMethod]
    [Description("1024x768 dark: the tablet band's inspector side sheet with both required-field errors")]
    public async Task Capture_SchemaError_Tablet_Dark()
        => await CaptureAsync("inspector-schema-error-tablet-dark-1024x768", 1024, 768, touch: true, dark: true, Band.Tablet, Shot.RequiredErrors);

    [TestMethod]
    [Description("390x844 light: the Inspector tab with both required-field errors")]
    public async Task Capture_SchemaError_Mobile_Light()
        => await CaptureAsync("inspector-schema-error-mobile-390x844", 390, 844, touch: true, dark: false, Band.Mobile, Shot.RequiredErrors);

    [TestMethod]
    [Description("390x844 dark: the Inspector tab with both required-field errors")]
    public async Task Capture_SchemaError_Mobile_Dark()
        => await CaptureAsync("inspector-schema-error-mobile-dark-390x844", 390, 844, touch: true, dark: true, Band.Mobile, Shot.RequiredErrors);

    // ── U11: the list controls (duration row + one structured item) ─────────

    [TestMethod]
    [Description("1440x900 light: the duration row followed by the structured list with its single item")]
    public async Task Capture_SchemaLists_Desktop()
        => await CaptureAsync("inspector-schema-lists-desktop-1440x900", 1440, 900, touch: false, dark: false, Band.Desktop, Shot.Lists);

    [TestMethod]
    [Description("1024x768 light: the duration row + structured list on the tablet side sheet")]
    public async Task Capture_SchemaLists_Tablet()
        => await CaptureAsync("inspector-schema-lists-tablet-1024x768", 1024, 768, touch: true, dark: false, Band.Tablet, Shot.Lists);

    [TestMethod]
    [Description("390x844 light: the duration row + structured list on the Inspector tab")]
    public async Task Capture_SchemaLists_Mobile()
        => await CaptureAsync("inspector-schema-lists-mobile-390x844", 390, 844, touch: true, dark: false, Band.Mobile, Shot.Lists);

    // ── U11: one Czech desktop capture ──────────────────────────────────────

    [TestMethod]
    [Description("1440x900 light Czech: the localized required-field errors and issue rows")]
    public async Task Capture_SchemaError_Desktop_Czech()
        => await CaptureAsync("inspector-schema-error-cs-1440x900", 1440, 900, touch: false, dark: false, Band.Desktop, Shot.RequiredErrors, culture: "cs");

    // ── Machinery ────────────────────────────────────────────────────────────

    private async Task CaptureAsync(
        string fileName,
        int width,
        int height,
        bool touch,
        bool dark,
        Band band,
        Shot shot,
        string culture = "en")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 640,
            Locale = culture == "cs" ? "cs-CZ" : "en-US",
            IgnoreHTTPSErrors = true
        });
        RegisterContext(context);
        await context.AddInitScriptAsync($"localStorage.setItem('tm-demo-culture', '{culture}');");

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}{StepSchemaUrl}");
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = DisableMotionStyle });
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 30000 });

        await SelectRowAsync(page, band);
        await page.Locator("[data-testid='modeling-inspector-schema-section-instructions']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        if (shot == Shot.RequiredErrors)
        {
            await BreakRequiredFieldsAsync(page);

            // The desktop dock has room for the instructions section AND the role field below it, so
            // the capture starts at the section. The compact bands can only afford the owning field
            // (the role error is the compact one) and show the instructions error just above it.
            if (band == Band.Desktop)
            {
                await ScrollFieldIntoViewAsync(
                    page,
                    "[data-testid='modeling-inspector-schema-section-instructions']",
                    "start",
                    band);
            }
            else
            {
                // The FIELD WRAPPER (label + control + inline message), never the input's id.
                await ScrollFieldIntoViewAsync(
                    page,
                    "[data-testid='modeling-inspector-field-role']",
                    "end",
                    band);
            }
        }
        else
        {
            // The duration row is the anchor: the structured list with its one item follows it. The
            // FIELD WRAPPER carries the label, so it is the anchor — the input's own id would scroll
            // its label off the top.
            await ScrollFieldIntoViewAsync(
                page,
                "[data-testid='modeling-inspector-field-sla']",
                "start",
                band);
        }

        // Scrolling the schema into view can move the PAGE too (the demo chrome is sticky): reset it
        // so the capture shows the editor, not a half-scrolled demo shell.
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");

        if (band == Band.Tablet)
        {
            // The demo page is taller than a 768px viewport, so the side sheet's body would be cut at
            // the fold. Scroll the page until the sheet starts right below the demo's sticky top bar:
            // that is the most room the band can give the sheet, and it keeps its header and tabs.
            await page.EvaluateAsync(
                """
                () => {
                    const sheet = document.querySelector("[data-testid='modeling-inspector-sheet']");
                    if (!sheet) return;
                    const bar = document.querySelector('[data-demo-shell="main"] .sticky');
                    const offset = (bar ? bar.getBoundingClientRect().height : 70) + 8;
                    window.scrollBy(0, Math.max(0, sheet.getBoundingClientRect().top - offset));
                }
                """);
        }

        if (dark)
        {
            await ApplyDarkThemeAsync(page);
        }

        await SettleAsync(page);
        await SaveViewportScreenshotAsync(page, fileName);
    }

    /// <summary>
    /// U11: makes the two REQUIRED fields fail the way a user makes them fail — the markdown
    /// instructions are emptied and blurred (focusout commits the removal) and the single role
    /// reference is removed from its chips. Both errors are then user-caused, so both announce.
    /// </summary>
    private static async Task BreakRequiredFieldsAsync(IPage page)
    {
        var editor = page.Locator("#modeling-inspector-field-instructions .tm-mde-content");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await editor.ClickAsync();

        // Empty the content surface. The block editor serializes an empty H1 to "# ", so a keyboard
        // Select All is not reliable here — the generator clears the editable and raises the same
        // `input` event the browser's Delete raises, which is what the component listens to.
        await editor.EvaluateAsync(
            """
            (el) => {
                el.innerHTML = '';
                el.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """);
        await page.WaitForFunctionAsync(
            "() => window.tmMarkdownEditor.getValue(document.querySelector('#modeling-inspector-field-instructions .tm-mde-content')) === ''",
            null,
            new PageWaitForFunctionOptions { Timeout = 10000 });

        // The editor has no blur callback of its own — leaving it commits (the bubbling focusout).
        await page.Locator("#modeling-inspector-name").ClickAsync();

        var removeRole = page.Locator("#modeling-inspector-field-role .tm-multiselect__chip-remove").First;
        await removeRole.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await removeRole.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            () => {
                const instructions = document.querySelector("[data-testid='modeling-inspector-field-instructions']");
                const role = document.querySelector("[data-testid='modeling-inspector-field-role']");
                return instructions?.getAttribute('data-schema-error') === 'true'
                    && role?.querySelector("[aria-invalid='true']") !== null;
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    private static async Task SelectRowAsync(IPage page, Band band)
    {
        // Tablet: only the tree DRAWER is closed at load (the preview stays docked). Mobile mounts ONE
        // panel at a time, so the tree must be selected first.
        if (band is Band.Tablet or Band.Mobile)
        {
            await page.Locator("[data-testid='modeling-panel-tab-tree']").ClickAsync();
        }

        await page.Locator("[data-testid='modeling-tree-node-step-validate']").ClickAsync();
        await page.Locator("[data-testid='modeling-editor'][data-selected-element-id='step-validate']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 15000 });

        // Tablet: selecting a row opens the inspector SIDE SHEET itself (one open panel at a time) —
        // clicking the tab again would CLOSE it. Mobile: a tree tap is G2 browsing, so the Inspector
        // tab has to be picked explicitly.
        if (band == Band.Mobile)
        {
            await page.Locator("[data-testid='modeling-panel-tab-inspector']").ClickAsync();
        }
    }

    /// <summary>
    /// Scrolls the inspector's own scroll container so the target aligns with the given edge of the
    /// scrollport, then nudges past the STICKY tab strip: on the desktop and tablet docks the
    /// Properties/Relations/Source row lives INSIDE the scrolling panel, so an element aligned to the
    /// container's top would hide under it.
    /// </summary>
    private static Task ScrollFieldIntoViewAsync(IPage page, string selector, string block, Band band)
    {
        // 'end' anchors leave room for the field's OWN message under the control; 'start' anchors
        // clear the sticky tab strip on the docked bands and keep a small gap on the mobile tab.
        var margin = block == "end"
            ? 72
            : band is Band.Desktop or Band.Tablet ? 44 : 24;
        return page.Locator(selector).First.EvaluateAsync(
            """
            (el, args) => {
                const [block, margin] = args;
                el.scrollIntoView({ block, inline: 'nearest' });
                let node = el.parentElement;
                while (node) {
                    const style = getComputedStyle(node);
                    if (/(auto|scroll)/.test(style.overflowY) && node.scrollHeight > node.clientHeight) {
                        node.scrollTop += block === 'end' ? margin : -margin;
                        return;
                    }
                    node = node.parentElement;
                }
            }
            """,
            new object[] { block, margin });
    }

    /// <summary>The demo theme toggle hides with the demo sidebar from 1264px up, so the click is
    /// dispatched; the fallback stamps the dark scope on every theme root (the M25 pattern).</summary>
    private static async Task ApplyDarkThemeAsync(IPage page)
    {
        var toggle = page.Locator("aside [data-testid='theme-toggle']");
        if (await toggle.CountAsync() > 0)
        {
            await toggle.First.DispatchEventAsync("click");
        }

        try
        {
            await page.Locator(".demo-shell[data-theme='dark']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            await page.EvaluateAsync(
                """
                () => {
                    document.querySelectorAll('[data-theme]').forEach((el) => el.setAttribute('data-theme', 'dark'));
                    document.querySelector('.demo-shell')?.classList.add('dark');
                    document.documentElement.setAttribute('data-theme', 'dark');
                    document.documentElement.classList.add('tm-dark', 'dark');
                    document.body.classList.add('tm-dark', 'dark');
                }
                """);
        }

        await page.WaitForTimeoutAsync(300);
    }

    private static async Task SettleAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            async () => {
                await document.fonts.ready;
                const animations = document.getAnimations();
                await Promise.all(animations.map(a => a.finished.catch(() => { })));
            }
            """);
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task SaveViewportScreenshotAsync(IPage page, string fileName)
    {
        var path = Path.Combine(OutputDirectory, fileName + ".png");
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = false,
            Timeout = 30_000
        });
        Assert.IsTrue(new FileInfo(path).Length > 10_000, $"{fileName} should contain the rendered inspector.");
    }
}
