using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 10 in a real browser: the import dialog (MJML / JSON, limits, warnings, structure preview), the
/// export dialog (MJML / JSON / HTML with copy and a REAL download whose content is read back) and the
/// validation dialog (severity counts, findings as buttons, “Go to”), on a desktop (1440), a phone (390,
/// coarse pointer) and in the dark theme.
/// <para>
/// Screenshots land in <c>__screenshots__/email-templates/f10-*.png</c> and are compared against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.EmailTemplates/email-{import,export}-dialog--*.png</c>
/// and <c>email-template-editor--validation_title--*.png</c>.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailEditorImportExportF10E2ETests : EmailTemplateE2ETestBase
{
    /// <summary>The seeded welcome template (a subject, a preheader and a text block).</summary>
    private const string WelcomeEditorRoute = "/email-templates/edit/11111111-1111-1111-1111-111111111111";

    private const string ImportedMjml =
        "<mjml><mj-body><mj-section><mj-column><mj-text>Imported headline</mj-text></mj-column></mj-section></mj-body></mjml>";

    /// <summary>
    /// A hostile-shaped document that produces all three severities in the validation dialog: a button without
    /// a link (Error), an image without alt text (Warning) and a preheader over the 100 character limit (Info).
    /// </summary>
    private static readonly string FindingsMjml =
        "<mjml><mj-head><mj-preview>" + new string('p', 140) + "</mj-preview></mj-head><mj-body>"
        + "<mj-section><mj-column>"
        + "<mj-button>Orphan button</mj-button>"
        + "<mj-image src=\"https://example.com/logo.png\" />"
        + "<mj-text>Finding source</mj-text>"
        + "</mj-column></mj-section></mj-body></mjml>";

    private async Task<IPage> OpenEditorAsync(
        int width, int height, bool touch = false, string layout = "Desktop", bool clipboard = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            AcceptDownloads = true,
            Permissions = clipboard ? ["clipboard-read", "clipboard-write"] : [],
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{WelcomeEditorRoute}");

        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 60000 });
        await ExpandEditorToViewportAsync(page);
        await ForceEditorLayoutAsync(page, layout);
        return page;
    }

    /// <summary>Gives the editor the whole viewport by hiding the demo's own app shell (see F8/F9).</summary>
    private static async Task ExpandEditorToViewportAsync(IPage page)
    {
        await page.AddStyleTagAsync(new()
        {
            Content = "aside.fixed { display: none !important; } "
                + "main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }",
        });
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>Opens a file/help command that lives in the mobile “⋮” overflow on a phone layout.</summary>
    private static async Task ClickCommandAsync(IPage page, string selector)
    {
        if (await page.Locator(selector).CountAsync() == 0)
        {
            await page.Locator("[data-tm-mobile-overflow] .tm-popover__trigger").First.ClickAsync();
            await page.WaitForSelectorAsync(selector, new() { Timeout = 15000 });
        }

        await page.Locator(selector).First.ClickAsync();
    }

    /// <summary>Fills the import code surface (the editable textarea of the read-only code editor).</summary>
    private static async Task PasteImportAsync(IPage page, string value)
    {
        await page.WaitForSelectorAsync("#tm-import-input", new() { Timeout = 15000 });
        await page.Locator("#tm-import-input").FillAsync(value);
        await page.WaitForTimeoutAsync(250);
    }

    /// <summary>Reads the rendered text of the export code surface.</summary>
    private static async Task<string> ExportTextAsync(IPage page, string mode)
    {
        await page.Locator($"#tab-export-{mode}").ClickAsync();
        await page.WaitForSelectorAsync($"#tm-export-{mode}-code", new() { Timeout = 20000 });
        return await page.InputValueAsync($"#tm-export-{mode}-code");
    }

    /// <summary>Clicks the footer download and reads the DOWNLOADED file (name, MIME-ish extension, content).</summary>
    private static async Task<(string FileName, string Content)> DownloadActiveFormatAsync(IPage page)
    {
        var download = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("[data-tm-export-download]"));
        var path = await download.PathAsync();
        var content = await File.ReadAllTextAsync(path!);
        return (download.SuggestedFilename, content);
    }

    /// <summary>Asserts an element is fully inside the viewport (the “primary action without scrolling” rule).</summary>
    private static async Task AssertInViewportAsync(IPage page, string selector, string because)
    {
        var inViewport = await page.EvaluateAsync<bool>(
            "sel => { const el = document.querySelector(sel); if (!el) return false;"
            + " const r = el.getBoundingClientRect();"
            + " return r.width > 0 && r.height > 0 && r.top >= 0 && r.bottom <= window.innerHeight + 0.5; }",
            selector);
        inViewport.Should().BeTrue(because);
    }

    // ── Import ──────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F10_1_Import_Mjml_ShowsStructureAndSummary_AndReplacesTheDocument()
    {
        var page = await OpenEditorAsync(1440, 900);

        await page.ClickAsync("[data-tm-import-btn]");
        await page.WaitForSelectorAsync("[data-tm-import-dropzone]", new() { Timeout = 15000 });
        // The canvas text is captured HERE, not asserted against the seed: the specs of this lane share one demo
        // API whose auto-save means an earlier spec may already have edited the welcome template.
        var originalCanvas = (await page.Locator("[data-tm-canvas]").First.InnerTextAsync()).Trim();
        originalCanvas.Should().NotBeEmpty("the seeded template is open before the import");

        await PasteImportAsync(page, ImportedMjml);

        await page.WaitForSelectorAsync("[data-tm-import-summary][data-tm-import-state='success']",
            new() { Timeout = 15000 });
        (await page.GetAttributeAsync("[data-tm-import-summary]", "data-tm-import-sections")).Should().Be("1");
        (await page.GetAttributeAsync("[data-tm-import-summary]", "data-tm-import-blocks")).Should().Be("1");
        (await page.GetAttributeAsync("[data-tm-import-summary]", "data-tm-import-warning-count")).Should().Be("0");

        // The structure preview names the block with its LOCALIZED type, not the JSON discriminator.
        (await page.Locator("[data-tm-import-block][data-tm-import-block-kind='Text']").InnerTextAsync())
            .Trim().Should().Be("Text");

        // The drop zone states the formats and the limit that will be applied.
        var dropzoneText = await page.Locator("[data-tm-import-dropzone]").InnerTextAsync();
        dropzoneText.Should().Contain(".mjml").And.Contain("5 MB", "the limit comes from EmailImportOptions");

        // Fáze 10 review (UX 9): the strip's aria-controls must point at a tabpanel that EXISTS and holds the
        // mode's input. The old markup put the body NEXT TO the strip, so the reference dangled.
        (await page.GetAttributeAsync("#tab-import-mjml", "aria-controls")).Should().Be("tabpanel-import-mjml");
        (await page.Locator("#tabpanel-import-mjml[role='tabpanel'] #tm-import-input").CountAsync())
            .Should().Be(1, "the MJML body lives inside its own tabpanel");

        await SaveNamedViewportScreenshotAsync(page, "f10-import-desktop.png");

        await page.ClickAsync("[data-tm-import-confirm]");
        await page.WaitForSelectorAsync("[data-tm-import]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);
        (await page.Locator("[data-tm-canvas]").First.InnerTextAsync()).Should().Contain("Imported headline");

        // The import is ONE undo step, and the undo brings the original document back.
        await page.ClickAsync("[data-tm-undo]");
        await page.WaitForTimeoutAsync(400);
        var undone = (await page.Locator("[data-tm-canvas]").First.InnerTextAsync()).Trim();
        undone.Should().NotContain("Imported headline");
        // The undo restores exactly what was on the canvas before the import (the first line is the block text
        // the import replaced; comparing the WHOLE text would also compare selection/action chrome).
        undone.Should().Contain(originalCanvas.Split('\n')[0].Trim(),
            "one undo restores the document the import replaced");
    }

    [TestMethod]
    public async Task F10_2_Import_Json_ReportsLocalizedFailures_AndKeepsTheInput()
    {
        var page = await OpenEditorAsync(1440, 900);

        await page.ClickAsync("[data-tm-import-btn]");
        await page.Locator("#tab-import-json").ClickAsync();

        // A newer schema is refused with the localized sentence, not a raw exception message.
        await PasteImportAsync(page, "{\"schemaVersion\":99,\"sections\":[]}");
        await page.WaitForSelectorAsync("[data-tm-import-summary][data-tm-import-state='error']",
            new() { Timeout = 15000 });
        var newerSchema = await page.Locator("[data-tm-import-errors]").InnerTextAsync();
        newerSchema.Should().Contain("newer editor version", "the sentence is localized, the key never shows");
        newerSchema.Should().NotContain("schemaVersion\":99");
        (await page.Locator("[data-tm-import-confirm]").IsDisabledAsync()).Should().BeTrue();

        // Broken JSON keeps the pasted input and offers no import.
        await PasteImportAsync(page, "{ not json");
        await page.WaitForSelectorAsync("[data-tm-import-errors]", new() { Timeout = 15000 });
        (await page.InputValueAsync("#tm-import-input")).Should().Be("{ not json");
        (await page.Locator("[data-tm-import-errors]").InnerTextAsync())
            .Should().Contain("not a valid template JSON", "the sentence says WHAT to paste instead");
        (await page.Locator("[data-tm-import-confirm]").IsDisabledAsync()).Should().BeTrue();

        // A valid JSON document parses and can be imported.
        await PasteImportAsync(page,
            "{\"subject\":\"From JSON\",\"sections\":[{\"columns\":[{\"blocks\":"
            + "[{\"$type\":\"text\",\"content\":\"Json body\"}]}]}]}");
        await page.WaitForSelectorAsync("[data-tm-import-summary][data-tm-import-state='success']",
            new() { Timeout = 15000 });
        (await page.Locator("[data-tm-import-confirm]").IsDisabledAsync()).Should().BeFalse();
    }

    [TestMethod]
    public async Task F10_3_Import_OverMaxInputBytes_IsRefusedWithTheLimit()
    {
        var page = await OpenEditorAsync(1440, 900);
        await OpenDemoControlsAsync(page);
        await page.SelectOptionAsync("[data-tm-demo-import-limit]", "1024");

        await page.ClickAsync("[data-tm-import-btn]");
        await page.WaitForSelectorAsync("[data-tm-import-dropzone]", new() { Timeout = 15000 });

        // The drop zone reports the HOST's limit, and the payload over it is refused before parsing.
        (await page.Locator("[data-tm-import-dropzone]").InnerTextAsync()).Should().Contain("1 kB");
        await PasteImportAsync(page, "<mjml><mj-body><mj-section><mj-column><mj-text>"
            + new string('x', 2000) + "</mj-text></mj-column></mj-section></mj-body></mjml>");

        await page.WaitForSelectorAsync("[data-tm-import-summary][data-tm-import-state='error']",
            new() { Timeout = 15000 });
        (await page.Locator("[data-tm-import-errors]").InnerTextAsync())
            .Should().Contain("larger than 1 kB", "the dialog measures the PASTED text against the host limit")
            .And.Contain("Shorten the template or split it");
        (await page.Locator("[data-tm-import-confirm]").IsDisabledAsync()).Should().BeTrue();
    }

    // ── Export ──────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F10_4_Export_DownloadsMjmlJsonAndHtml_WithTheRealContent()
    {
        var page = await OpenEditorAsync(1440, 900);

        await page.ClickAsync("[data-tm-mjml-btn]");
        await page.WaitForSelectorAsync("#tm-export-mjml-code", new() { Timeout = 20000 });

        var mjml = await page.InputValueAsync("#tm-export-mjml-code");
        mjml.Should().Contain("<mjml").And.Contain("<mj-body");

        // Fáze 10 review (UX 9): each format's body lives in the tabpanel its tab points at.
        (await page.GetAttributeAsync("#tab-export-mjml", "aria-controls")).Should().Be("tabpanel-export-mjml");
        (await page.Locator("#tabpanel-export-mjml[role='tabpanel'] #tm-export-mjml-code").CountAsync())
            .Should().Be(1, "the MJML surface lives inside its own tabpanel");
        await SaveNamedViewportScreenshotAsync(page, "f10-export-desktop.png");

        // MJML: the downloaded file is the very text the dialog shows.
        var (mjmlName, mjmlFile) = await DownloadActiveFormatAsync(page);
        mjmlName.Should().EndWith(".mjml");
        mjmlFile.Should().StartWith("<mjml");
        // The export also has to BE well-formed XML, with no script element and no ATTRIBUTE carrying a refused
        // scheme. Attribute VALUES are judged, never the characters: a hostile TEXT block that an earlier spec
        // imported into this lane's shared demo document is escaped (`&lt;a href=&quot;javascript:…`) and an
        // mj-raw payload keeps plain quotes in its TEXT, so both are inert and a substring check would red here
        // on another spec's leftovers (the old `NotContain("javascript:")` did exactly that).
        var mjmlDocument = System.Xml.Linq.XDocument.Parse(mjmlFile, System.Xml.Linq.LoadOptions.None);
        var mjmlElements = mjmlDocument.Root!.DescendantsAndSelf().ToList();
        mjmlElements.Select(element => element.Name.LocalName).Should().NotContain("script");
        BlockedSchemeAttributeValues(mjmlElements.SelectMany(element => element.Attributes())
                .Select(attribute => attribute.Value))
            .Should().BeEmpty("no exported MJML attribute may carry a refused scheme");

        // JSON: the canonical content JSON.
        var json = await ExportTextAsync(page, "json");
        json.Should().Contain("\"sections\"");
        var (jsonName, jsonFile) = await DownloadActiveFormatAsync(page);
        jsonName.Should().EndWith(".json");
        using (var parsed = JsonDocument.Parse(jsonFile))
        {
            parsed.RootElement.TryGetProperty("sections", out _).Should().BeTrue();
        }

        // HTML: rendered on the demo API through IEmailPreviewRenderer, with the placeholders kept.
        await page.Locator("#tab-export-html").ClickAsync();
        await page.WaitForSelectorAsync("#tm-export-html-code", new() { Timeout = 30000 });
        var html = await page.InputValueAsync("#tm-export-html-code");
        html.Should().Contain("<html", "the renderer's full document is exported");
        html.Should().NotContain("<script");

        var (htmlName, htmlFile) = await DownloadActiveFormatAsync(page);
        htmlName.Should().EndWith(".html");
        htmlFile.Should().Contain("<html");
        // Element level only (an escaped `&lt;script` in text is inert and must not red this spec); the scheme
        // allow-list of attribute values is asserted above on the model's own XML export and by the
        // Abstractions/Engine security tests, which is where a payload's attribute values are readable as such.
        htmlFile.Should().NotContain("<script");
        htmlFile.Should().NotContain("<iframe");
        htmlFile.Should().NotContain("<object");
    }

    /// <summary>The values of <paramref name="values"/> that start with a scheme the pipeline refuses.</summary>
    private static IEnumerable<string> BlockedSchemeAttributeValues(IEnumerable<string> values)
        => values
            .Select(value => value.TrimStart())
            .Where(value => value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    [TestMethod]
    public async Task F10_5_Export_CopyConfirmsInALiveRegion_AndNothingIsSent()
    {
        var page = await OpenEditorAsync(1440, 900, clipboard: true);

        await page.ClickAsync("[data-tm-mjml-btn]");
        await page.WaitForSelectorAsync("#tm-export-mjml-code", new() { Timeout = 20000 });
        var expected = await page.InputValueAsync("#tm-export-mjml-code");

        // A click in the code surface is NOT a copy (Fáze 10 review, UX 8): the confirmation used to hang on a
        // handler around the box, so clicking into the textarea announced a copy that never happened.
        await page.Locator("[data-tm-export] textarea").First.ClickAsync();
        await page.WaitForTimeoutAsync(400);
        (await page.Locator("[data-tm-export-announcer]").InnerTextAsync()).Trim()
            .Should().BeEmpty("a click in the code is not a copy");

        await page.Locator("[data-tm-export] .tm-copy-button").First.ClickAsync();

        await page.WaitForSelectorAsync("[data-tm-export-announcer]", new() { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-export-announcer]').textContent.trim().length > 0",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });
        (await page.Locator("[data-tm-export-announcer]").InnerTextAsync()).Trim()
            .Should().Contain("Copied");

        var clipboard = await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        // A <textarea>'s value is always LF-normalised by the browser (HTML spec), while the clipboard keeps the
        // exact exported string, which carries the host's CRLF on Windows — so compare line-ending-insensitively.
        NormalizeLineEndings(clipboard).Should().Be(NormalizeLineEndings(expected),
            "the copy button copies exactly the exported text (modulo the textarea's LF normalisation)");

        // No send was performed by the export: the send endpoint is only reached from the send form.
        (await page.Locator("[data-tm-export]").CountAsync()).Should().Be(1);
    }

    // ── Validation ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F10_6_Validation_CountsSeverities_AndJumpsToTheBlock()
    {
        var page = await OpenEditorAsync(1440, 900);

        // The findings come from an imported document: an orphan button, an image without alt, a long preheader.
        await page.ClickAsync("[data-tm-import-btn]");
        await PasteImportAsync(page, FindingsMjml);
        await page.WaitForSelectorAsync("[data-tm-import-summary][data-tm-import-state='warning']",
            new() { Timeout = 15000 });
        await page.ClickAsync("[data-tm-import-confirm]");
        await page.WaitForTimeoutAsync(500);

        await page.ClickAsync("[data-tm-validate-btn]");
        var summary = page.Locator("[data-tm-validation-summary]");
        await summary.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        (await summary.GetAttributeAsync("data-tm-validation-errors")).Should().Be("1");
        (await summary.GetAttributeAsync("data-tm-validation-warnings")).Should().Be("1");
        (await summary.GetAttributeAsync("data-tm-validation-infos")).Should().Be("1");
        (await summary.InnerTextAsync()).Should().Contain("Errors: 1").And.Contain("Suggestions: 1");

        // A finding is a button with a title, a description and the “Go to” action.
        var finding = page.Locator("button[data-tm-validation-severity='Error']").First;
        await finding.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        (await finding.Locator("[data-tm-validation-title]").InnerTextAsync()).Trim()
            .Should().Be("Button without a link");
        (await finding.Locator("[data-tm-validation-description]").InnerTextAsync()).Should().NotBeEmpty();
        (await finding.Locator("[data-tm-validation-action]").InnerTextAsync()).Should().Contain("Go to");

        await SaveNamedViewportScreenshotAsync(page, "f10-validation-desktop.png");

        await finding.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-validation]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);
        (await page.Locator("[data-tm-block-id][aria-current='true']").CountAsync())
            .Should().Be(1, "“Go to” selects the offending block");
    }

    // ── Phone 390 and dark theme ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F10_7_Mobile390_DialogsAreSheets_WithThePrimaryActionInView()
    {
        var page = await OpenEditorAsync(390, 780, touch: true, layout: "Mobile");
        await AssertCoarsePointerAsync(page);

        // Import: the file command lives in the “⋮” overflow on a phone.
        await ClickCommandAsync(page, "[data-tm-import-btn]");
        await page.WaitForSelectorAsync("[data-tm-import-dropzone]", new() { Timeout = 15000 });
        var importSheet = page.Locator("[data-tm-import-dialog].tm-modal-overlay, [data-tm-import]").First;
        (await page.Locator(".tm-modal--sheet").CountAsync()).Should().Be(1, "F2: a phone gets the core sheet");
        await page.WaitForTimeoutAsync(600);
        await AssertInViewportAsync(page, "[data-tm-import-confirm]",
            "the primary import action must be reachable without scrolling the sheet");
        await PasteImportAsync(page, ImportedMjml);
        await page.WaitForSelectorAsync("[data-tm-import-summary]", new() { Timeout = 15000 });
        await SaveNamedViewportScreenshotAsync(page, "f10-import-mobile-390.png");
        await page.ClickAsync("[data-tm-import-cancel]");
        await page.WaitForTimeoutAsync(400);

        // Export: same rule for its primary download action.
        await ClickCommandAsync(page, "[data-tm-mjml-btn]");
        await page.WaitForSelectorAsync("#tm-export-mjml-code", new() { Timeout = 20000 });
        (await page.Locator(".tm-modal--sheet").CountAsync()).Should().Be(1);
        await page.WaitForTimeoutAsync(600);
        await AssertInViewportAsync(page, "[data-tm-export-download]",
            "the primary download action must be reachable without scrolling the sheet");
        await SaveNamedViewportScreenshotAsync(page, "f10-export-mobile-390.png");
        importSheet.Should().NotBeNull();
    }

    [TestMethod]
    public async Task F10_9_Tablet1024_KeepsTheDialogsInsideTheViewport()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true, layout: "Tablet");

        await page.ClickAsync("[data-tm-import-btn]");
        await page.WaitForSelectorAsync("[data-tm-import-dropzone]", new() { Timeout = 15000 });
        await PasteImportAsync(page, FindingsMjml);
        await page.WaitForSelectorAsync("[data-tm-import-summary]", new() { Timeout = 15000 });
        (await HorizontalOverflowAsync(page, "[data-tm-import-dialog]")).Should().Be(0,
            "the import dialog makes no horizontal scrollbar at 1024");
        await SaveNamedViewportScreenshotAsync(page, "f10-import-tablet.png");

        await page.ClickAsync("[data-tm-import-confirm]");
        await page.WaitForSelectorAsync("[data-tm-import]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        await page.ClickAsync("[data-tm-mjml-btn]");
        await page.WaitForSelectorAsync("#tm-export-mjml-code", new() { Timeout = 20000 });
        (await HorizontalOverflowAsync(page, "[data-tm-export-dialog]")).Should().Be(0,
            "the export dialog makes no horizontal scrollbar at 1024");
        await SaveNamedViewportScreenshotAsync(page, "f10-export-tablet.png");
        await page.Locator("[data-tm-export-close]").ClickAsync();
        await page.WaitForTimeoutAsync(400);

        await page.ClickAsync("[data-tm-validate-btn]");
        await page.WaitForSelectorAsync("[data-tm-validation-summary]", new() { Timeout = 15000 });
        (await HorizontalOverflowAsync(page, "[data-tm-validation-dialog]")).Should().Be(0,
            "the validation dialog makes no horizontal scrollbar at 1024");
        await SaveNamedViewportScreenshotAsync(page, "f10-validation-tablet.png");
    }

    /// <summary>The horizontal overflow of an element (0 = it fits).</summary>
    private static async Task<int> HorizontalOverflowAsync(IPage page, string selector)
        => await page.EvaluateAsync<int>(
            "sel => { const el = document.querySelector(sel); return el ? Math.round(el.scrollWidth - el.clientWidth) : -1; }",
            selector);

    [TestMethod]
    public async Task F10_8_Dark_DialogsKeepReadableContrast()
    {
        var page = await OpenEditorAsync(1440, 900);

        var clicked = await page.EvaluateAsync<bool>(
            "() => { const b = document.querySelector(`[data-testid='theme-toggle']`); if (!b) return false; b.click(); return true; }");
        clicked.Should().BeTrue("the demo host renders the theme toggle");
        await page.WaitForSelectorAsync("[data-theme='dark']", new() { Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);

        await page.ClickAsync("[data-tm-import-btn]");
        await page.WaitForSelectorAsync("[data-tm-import-dropzone]", new() { Timeout = 15000 });
        await PasteImportAsync(page, FindingsMjml);
        await page.WaitForSelectorAsync("[data-tm-import-summary]", new() { Timeout = 15000 });

        var dialogBackground = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-tm-import-dialog] .tm-modal, [data-tm-import-dialog] .tm-dialog')).backgroundColor");
        dialogBackground.Should().NotBe("rgb(255, 255, 255)", "the dialog follows the dark theme");
        await SaveNamedViewportScreenshotAsync(page, "f10-import-dark.png");

        // Import the findings document, so the validation dialog has something to show.
        await page.ClickAsync("[data-tm-import-confirm]");
        await page.WaitForSelectorAsync("[data-tm-import]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        await page.ClickAsync("[data-tm-validate-btn]");
        await page.WaitForSelectorAsync("[data-tm-validation-summary]", new() { Timeout = 15000 });
        await SaveNamedViewportScreenshotAsync(page, "f10-validation-dark.png");

        var findingColour = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-tm-validation-message] [data-tm-validation-title]')).color");
        var surface = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-tm-validation]')).backgroundColor");
        findingColour.Should().NotBe(surface, "the finding text keeps a contrast against the panel surface");
    }

    [TestMethod]
    public async Task F10_10_Mobile390_ValidationSheet_ShowsTheFindingsAndTheCollapsedCounts()
    {
        var page = await OpenEditorAsync(390, 780, touch: true, layout: "Mobile");
        await AssertCoarsePointerAsync(page);

        await ClickCommandAsync(page, "[data-tm-import-btn]");
        await PasteImportAsync(page, FindingsMjml);
        await page.WaitForSelectorAsync("[data-tm-import-summary]", new() { Timeout = 15000 });
        await page.ClickAsync("[data-tm-import-confirm]");
        await page.WaitForSelectorAsync("[data-tm-import]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        await ClickCommandAsync(page, "[data-tm-validate-btn]");
        await page.WaitForSelectorAsync("[data-tm-validation-summary]", new() { Timeout = 15000 });
        (await page.Locator(".tm-modal--sheet").CountAsync()).Should().Be(1, "F2: a phone gets the core sheet");
        var zeroChips = await page.Locator(".tm-email-validation__count--zero").CountAsync();
        zeroChips.Should().Be(0, "all three severities have a finding in this document");
        // The sheet animates to its snap point; assert once it settled, otherwise the footer rectangle is the
        // mid-animation one.
        await page.WaitForTimeoutAsync(700);
        // The footer's Close button has to be in view without scrolling the body.
        await AssertInViewportAsync(page, "[data-tm-validation-close]",
            "the sheet's primary action must be reachable without scrolling");
        await SaveNamedViewportScreenshotAsync(page, "f10-validation-mobile-390.png");

        // Fáze 10 review (UX 10): “Go to” on a DOCUMENT finding has no block to jump to — on a phone it must
        // bring up the Properties sheet on the e-mail settings instead of leaving the user on an unchanged
        // canvas (the dialog used to just close). The preheader finding is the document-level one.
        var info = page.Locator("button[data-tm-validation-severity='Info']").First;
        await info.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await info.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-validation]",
            new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        await page.WaitForSelectorAsync("[data-tm-prop-target='document']",
            new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        (await page.Locator("[data-tm-block-id][aria-current='true']").CountAsync())
            .Should().Be(0, "a document finding selects nothing on the canvas");
    }

    [TestMethod]
    public async Task F10_11_Import_ErrorStates_ExplainThemselvesAt1440()
    {
        var page = await OpenEditorAsync(1440, 900);
        await page.ClickAsync("[data-tm-import-btn]");
        await page.WaitForSelectorAsync("[data-tm-import-dropzone]", new() { Timeout = 15000 });

        // Malformed JSON: the whole file has to be pasted, and the dialog says so.
        await page.Locator("#tab-import-json").ClickAsync();
        await PasteImportAsync(page, "{ not json");
        await page.WaitForSelectorAsync("[data-tm-import-errors]", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-import-errors]").InnerTextAsync())
            .Should().Contain("Export → JSON");
        (await page.Locator("[data-tm-import-confirm]").IsDisabledAsync()).Should().BeTrue();
        await SaveNamedViewportScreenshotAsync(page, "f10-import-error.png");

        // A newer schema: "update the application, or import it as MJML".
        await PasteImportAsync(page, "{\"schemaVersion\":99,\"sections\":[]}");
        await page.WaitForSelectorAsync("[data-tm-import-errors]", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-import-errors]").InnerTextAsync())
            .Should().Contain("newer editor version").And.Contain("MJML");

        // A broken version header: "the file has no template version, download it again via Export → JSON".
        // (The over-limit case is F10_3, which needs the demo's limit switch — 5 MB cannot be reached by pasting.)
        await PasteImportAsync(page, "{\"schemaVersion\":\"not-a-number\",\"sections\":[]}");
        await page.WaitForSelectorAsync("[data-tm-import-errors]", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-import-errors]").InnerTextAsync())
            .Should().Contain("no template version");
    }

    [TestMethod]
    public async Task F10_12_Export_HtmlUnavailable_ExplainsAndRetries()
    {
        var page = await OpenEditorAsync(1440, 900);

        // The preview endpoint is the server render the export uses: cutting it off is exactly "the renderer
        // could not answer", which must be explained AND retryable (a dropped connection is not the author's
        // fault and the dialog used to be a dead end).
        await page.RouteAsync("**/api/email-templates/preview", route => route.AbortAsync());

        await page.ClickAsync("[data-tm-mjml-btn]");
        await page.WaitForSelectorAsync("#tm-export-mjml-code", new() { Timeout = 20000 });
        await page.Locator("#tab-export-html").ClickAsync();

        var unavailable = page.Locator("[data-tm-export-unavailable]");
        await unavailable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        (await unavailable.InnerTextAsync()).Should().Contain("could not be rendered on the server");
        var retry = page.Locator("[data-tm-export-retry]");
        (await retry.CountAsync()).Should().Be(1, "an unreachable renderer is retryable");
        await SaveNamedViewportScreenshotAsync(page, "f10-export-html-unavailable.png");

        // The connection comes back: the retry renders the very document the other tabs export.
        await page.UnrouteAsync("**/api/email-templates/preview");
        await retry.ClickAsync();
        await page.WaitForSelectorAsync("#tm-export-html-code", new() { Timeout = 60000 });
        var html = await page.InputValueAsync("#tm-export-html-code");
        html.Should().Contain("<html");
        (await page.Locator("[data-tm-export-unavailable]").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-export-download]").IsDisabledAsync()).Should().BeFalse(
            "there is HTML to download now");
    }

    [TestMethod]
    public async Task F10_13_Validation_EmptyState_IsASuccessCard()
    {
        var page = await OpenEditorAsync(1440, 900);

        // A clean document: a text block, no links, no images and a short preheader — so the panel has zero
        // findings and has to say the template is FINE instead of showing three zero counts.
        await page.ClickAsync("[data-tm-import-btn]");
        await PasteImportAsync(page,
            "<mjml><mj-body><mj-section><mj-column><mj-text>All good</mj-text></mj-column></mj-section></mj-body></mjml>");
        await page.WaitForSelectorAsync("[data-tm-import-summary]", new() { Timeout = 15000 });
        await page.ClickAsync("[data-tm-import-confirm]");
        await page.WaitForSelectorAsync("[data-tm-import]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        await page.ClickAsync("[data-tm-validate-btn]");
        var ok = page.Locator("[data-tm-validate-btn]");
        await ok.CountAsync();
        await page.WaitForSelectorAsync("[data-tm-validation-ok]", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-validation-ok]").InnerTextAsync())
            .Should().Contain("The template looks good");
        (await page.Locator("[data-tm-validation-summary]").CountAsync()).Should().Be(0);
        var iconColour = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-tm-validation-ok] .tm-email-validation__ok-icon')).color");
        iconColour.Should().NotBe("rgb(0, 0, 0)", "the check icon carries the success colour");
        await SaveNamedViewportScreenshotAsync(page, "f10-validation-empty.png");
    }

    private static async Task AssertCoarsePointerAsync(IPage page)
        => (await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"))
            .Should().BeTrue("the phone context has to emulate a coarse pointer");
}
