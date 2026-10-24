using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 11 — the e-mail editor in the two NON-WASM render modes. The WASM lane proves the editor with a
/// host-side <c>HttpEmailPreviewRenderer</c>; this spec proves the SAME demo pages work when the render mode is
/// <b>Blazor Server</b> (circuit, :7107) and <b>InteractiveAuto</b> (:7108, WASM after the first interactive
/// render): the list loads through the API, the editor mounts, a save reaches the API and survives a reload,
/// and the preview renders through the shared <c>IEmailPreviewRenderer</c>.
/// <para>
/// Each spec creates its OWN template through the UI, so nothing here depends on — or perturbs — the shared
/// seed documents the WASM lane uses (and each mode cleans up after itself by deleting what it created).
/// </para>
/// </summary>
public abstract class EmailTemplateRenderModeTestBase : PlaywrightTestBase
{
    /// <summary>
    /// Puts the demo API's e-mail store back to its seed before EVERY spec of this lane, through the same
    /// shared helper the WASM lane uses: these specs create, edit and delete templates against the one store
    /// the WASM specs read, so a Server/InteractiveAuto run that left a template behind would change what the
    /// library list shows to everyone else (Fáze 11 review — the reset was only wired into the WASM base).
    /// </summary>
    [TestInitialize]
    public async Task ResetEmailStoreAsync() => await EmailTemplateE2ETestBase.ResetEmailStoreCoreAsync();

    /// <summary>Opens the library and waits for the app shell in whichever mode the derived lane runs.</summary>
    private protected async Task<IPage> OpenLibraryAsync()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/email-templates", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-tm-template-card]", new() { Timeout = 60000 });
        return page;
    }

    /// <summary>
    /// Creates a template from the library, edits its subject, saves and reloads — the shortest chain that
    /// proves DI, the HTTP client, the editor circuit/render mode and the PUT endpoint all line up.
    /// </summary>
    private protected async Task Editor_RoundTripsASave_ThroughTheHostApi(IPage page, string label)
    {
        await page.Locator("[data-tm-new-template]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 60000 });
        var editorUrl = page.Url;

        var unique = $"{label} subject {Guid.NewGuid():N}"[..40];
        var subject = page.Locator("[data-tm-prop='Subject'] input");
        await subject.FillAsync(unique);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-email-editor]')?.getAttribute('data-tm-dirty') === 'true'",
            null, new() { Timeout = 20000 });

        await page.Locator("[data-tm-save]").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-save-badge]')?.getAttribute('data-tm-save-state') === 'saved'",
            null, new() { Timeout = 40000 });

        // Reload the EDITOR (a full page load, so the value can only come back from the API).
        await page.GotoAsync(editorUrl, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 60000 });
        await page.WaitForFunctionAsync(
            "expected => document.querySelector(\"[data-tm-prop='Subject'] input\")?.value === expected",
            unique, new() { Timeout = 30000 });
    }

    /// <summary>The preview is rendered on the server and reaches the sandboxed frame.</summary>
    private protected async Task Preview_RendersThroughTheServerRenderer(IPage page)
    {
        await page.Locator("[data-tm-preview-btn]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-preview-frame]", new() { Timeout = 60000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-preview-root]')?.getAttribute('data-tm-preview-state') === 'rendered'",
            null, new() { Timeout = 60000 });
        var body = (await page.FrameLocator("[data-tm-preview-frame]").Locator("body").InnerTextAsync()).Trim();
        body.Should().NotBeNullOrWhiteSpace("the server renderer returned a real e-mail body");
    }
}

/// <summary>Fáze 11 — the editor's smoke flow on the Blazor Server demo (:7107).</summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailTemplateServerModeE2ETests : EmailTemplateRenderModeTestBase
{
    /// <inheritdoc />
    protected override string BaseUrl => "https://localhost:7107";

    [TestMethod]
    public async Task Server_Library_LoadsTheSeedTemplates()
    {
        var page = await OpenLibraryAsync();
        (await page.Locator("[data-tm-template-card]").CountAsync()).Should().BeGreaterThanOrEqualTo(3);
    }

    [TestMethod]
    public async Task Server_Editor_SavesThroughTheApi_AndSurvivesAReload()
    {
        var page = await OpenLibraryAsync();
        await Editor_RoundTripsASave_ThroughTheHostApi(page, "Server");
    }

    /// <summary>
    /// Phase 7 - dispatcher affinity of <c>SaveAsync()</c> on a REAL circuit. The page calls it from a
    /// thread-pool thread; the editor must hop onto the renderer's dispatcher itself, otherwise its
    /// <c>IsDirtyChanged</c> callback runs off-dispatcher and the host page's own re-render throws
    /// ("The current thread is not associated with the Dispatcher") - the host would keep showing
    /// "unsaved changes: yes" for a document that was in fact saved. Verified to fail without the hop.
    /// </summary>
    [TestMethod]
    public async Task Server_SaveAsync_FromABackgroundThread_StillReachesTheHostOnTheDispatcher()
    {
        var page = await OpenLibraryAsync();
        await page.Locator("[data-tm-new-template]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 60000 });

        var subject = page.Locator("[data-tm-prop='Subject'] input");
        await subject.FillAsync($"Background save {Guid.NewGuid():N}"[..30]);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-demo-dirty]')?.getAttribute('data-tm-demo-dirty') === 'true'",
            null, new() { Timeout = 20000 });

        await page.Locator("[data-tm-demo-save-background]").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-save-badge]')?.getAttribute('data-tm-save-state') === 'saved'",
            null, new() { Timeout = 40000 });
        // The HOST's own view: IsDirtyChanged(false) reached the page and the page re-rendered.
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-demo-dirty]')?.getAttribute('data-tm-demo-dirty') === 'false'",
            null, new() { Timeout = 20000 });

        // And it arrived ON the renderer's dispatcher: the demo counts every IsDirtyChanged callback that
        // reached it without the renderer's synchronization context. Without the editor's InvokeAsync hop the
        // clean transition of the off-thread save is such a callback (count 1), which a circuit turns into
        // "The current thread is not associated with the Dispatcher" on the page's re-render.
        (await page.Locator("[data-tm-demo-dirty]").GetAttributeAsync("data-tm-demo-off-dispatcher"))
            .Should().Be("0", "SaveAsync() hops onto the dispatcher itself, whatever thread the host calls it from");
    }

    [TestMethod]
    public async Task Server_Preview_ComesFromTheServerRenderer()
    {
        var page = await OpenLibraryAsync();
        await page.Locator("[data-tm-open]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 60000 });
        await Preview_RendersThroughTheServerRenderer(page);
    }
}

/// <summary>Fáze 11 — the editor's smoke flow on the InteractiveAuto demo (:7108).</summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailTemplateInteractiveAutoModeE2ETests : EmailTemplateRenderModeTestBase
{
    /// <inheritdoc />
    protected override string BaseUrl => "https://localhost:7108";

    [TestMethod]
    public async Task InteractiveAuto_Library_LoadsTheSeedTemplates()
    {
        var page = await OpenLibraryAsync();
        (await page.Locator("[data-tm-template-card]").CountAsync()).Should().BeGreaterThanOrEqualTo(3);
    }

    [TestMethod]
    public async Task InteractiveAuto_Editor_SavesThroughTheApi_AndSurvivesAReload()
    {
        var page = await OpenLibraryAsync();
        await Editor_RoundTripsASave_ThroughTheHostApi(page, "Auto");
    }

    [TestMethod]
    public async Task InteractiveAuto_Preview_ComesFromTheServerRenderer()
    {
        var page = await OpenLibraryAsync();
        await page.Locator("[data-tm-open]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 60000 });
        await Preview_RendersThroughTheServerRenderer(page);
    }
}
