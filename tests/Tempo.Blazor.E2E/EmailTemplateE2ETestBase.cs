using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared harness for the email-template demo E2E flow (E11). Provides:
/// <list type="bullet">
///   <item>navigation helpers onto the WASM demo email pages (served at 7106, API at 5100);</item>
///   <item>a small smtp4dev REST client (find / poll / html / plaintext / delete) so the full
///   send flow can prove a message actually arrived and clean up after itself;</item>
///   <item>unique recipient generation for test isolation on the shared smtp4dev instance;</item>
///   <item>named-screenshot capture into <c>__screenshots__/email-templates/</c>.</item>
/// </list>
/// </summary>
public abstract class EmailTemplateE2ETestBase : WasmTestBase
{
    /// <summary>Fixed seed template ids from <c>DemoEmailTemplateStore</c>.</summary>
    protected static readonly Guid WelcomeTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    protected static readonly Guid NewsletterTemplateId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    protected static readonly Guid OrderTemplateId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// The viewport every email spec assumes unless it asks for another one. Since Fáze 11 the width is a
    /// PARAMETER (<see cref="OpenAsync(string,int,int)"/>), because the screenshot set has to be regenerated
    /// at 1440 / 1024 / 390 / 320 px and a hard-wired 1440 made that impossible without a second harness.
    /// </summary>
    protected const int DefaultViewportWidth = 1440;

    /// <summary>The default viewport height (a desktop laptop frame).</summary>
    protected const int DefaultViewportHeight = 900;

    private const string Smtp4DevBase = "http://localhost:5000";

    private static readonly HttpClient Http = new();

    /// <summary>
    /// Puts the demo API's email store back to its seeded templates before EVERY email spec (Fáze 11).
    /// <para>
    /// The lane's specs all open the same seed document and the editor auto-saves, so a spec that imported a
    /// hostile or otherwise unusual template silently replaced the seed for everyone who ran after it — the
    /// lane's outcome depended on class discovery order (F7A_4 failed only when the F9A hostile-import spec
    /// ran first, F10_4 the same way). Resetting first makes each spec order-independent instead of asking
    /// every future spec to undo its neighbours' writes.
    /// </para>
    /// </summary>
    [TestInitialize]
    public async Task ResetEmailStoreAsync() => await ResetEmailStoreCoreAsync();

    /// <summary>
    /// Posts the demo API's reset endpoint; throws when the API refuses, so a broken isolation is loud.
    /// <para>
    /// <c>internal</c> (not <c>protected</c>): the render-mode lane derives from
    /// <see cref="PlaywrightTestBase"/> instead — it boots the Server and InteractiveAuto hosts, not the WASM
    /// one — and still needs the same reset, because those specs drive the very demo API store the WASM specs
    /// read.
    /// </para>
    /// </summary>
    internal static async Task ResetEmailStoreCoreAsync()
    {
        using var api = DemoApiTestHttp.CreateClient();
        var response = await api.PostAsync("/api/email-templates/reset", content: null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Builds a unique recipient so concurrent / historical mail on the shared smtp4dev does not collide.</summary>
    protected static string UniqueRecipient(string label)
        => $"e2e-email-{label}-{Guid.NewGuid():N}@tempo.local";

    /// <summary>
    /// Opens an email demo route and waits for the Blazor app shell + WASM boot. The EDITOR route gets the
    /// whole viewport: since Fáze 8 the editor resolves its desktop / tablet / mobile chrome from its own
    /// CONTAINER, and the demo's fixed 18 rem sidebar leaves a 1440 px window a ~1080 px editor — a tablet.
    /// These specs are about the desktop editor, so the demo chrome is hidden for them.
    /// </summary>
    protected async Task<IPage> OpenAsync(string route) => await OpenAsync(route, DefaultViewportWidth, DefaultViewportHeight);

    /// <summary>
    /// Same as <see cref="OpenAsync(string)"/> with an explicit VIEWPORT (Fáze 11). The screenshot set is
    /// regenerated at 1440 / 1024 / 390 / 320 px, and a spec that talks about a phone has to boot a phone.
    /// <para>
    /// <paramref name="pinDesktopEditor"/> keeps the historical behaviour (the demo's LayoutMode switch is
    /// forced to Desktop, because the demo chrome leaves a 1440 px window a ~1080 px editor — a tablet).
    /// A spec that wants the editor to resolve the layout from its container itself passes <c>false</c>;
    /// that is how the phone / tablet reference screenshots see the real narrow chrome.
    /// </para>
    /// </summary>
    protected async Task<IPage> OpenAsync(string route, int width, int height, bool pinDesktopEditor = true)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        var isEditorRoute = route.Contains("/email-templates/edit", StringComparison.Ordinal);
        if (isEditorRoute && pinDesktopEditor)
        {
            // Seeded BEFORE the first navigation, so the demo page's remembered LayoutMode survives the
            // re-navigation a spec performs (a language switch force-reloads the page). Specs that CREATE a
            // template from the list call ForceEditorLayoutAsync after the editor appears instead — they must
            // stay free to switch the editor back to Auto for a real narrow-layout case.
            await page.AddInitScriptAsync(
                "try { if (!localStorage.getItem('tm-demo-editor-layout')) "
                + "localStorage.setItem('tm-demo-editor-layout', 'Desktop'); } catch (e) { }");
        }
        else if (isEditorRoute)
        {
            // A stale choice from an earlier page in the same context would pin the wrong chrome.
            await page.AddInitScriptAsync(
                "try { localStorage.setItem('tm-demo-editor-layout', 'Auto'); } catch (e) { }");
        }

        // Cold-boot recovery (ONE controlled reload) lives in PlaywrightTestBase.GotoAppReadyAsync, so every
        // email spec — not only the ones that kept a local copy — survives a slow first boot.
        await GotoAppReadyAsync(page, $"{BaseUrl}{route}");

        if (isEditorRoute)
        {
            await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });
            if (pinDesktopEditor) await ForceEditorLayoutAsync(page, "Desktop");
        }

        return page;
    }

    /// <summary>
    /// Pins the EDITOR's layout through the demo page's LayoutMode switch. Since Fáze 8 the editor resolves
    /// its chrome from its own CONTAINER, and the demo's fixed 18 rem sidebar leaves a 1440 px window a
    /// ~1080 px editor — a TABLET, with the palette railed. These specs describe the DESKTOP editor (docked
    /// palette + inspector), so they pin that mode instead of depending on the demo's window chrome.
    /// </summary>
    /// <summary>
    /// Opens the demo page's own switch bar when the page folded it into a disclosure. The page folds it away
    /// while the EDITOR resolved the phone layout (review item 9: the demo's chrome was eating the phone's
    /// stage), so a spec that drives a switch on a phone has to open it first — every helper that touches a
    /// <c>[data-tm-demo-*]</c> control goes through here.
    /// </summary>
    protected static async Task OpenDemoControlsAsync(IPage page)
    {
        var summary = page.Locator("[data-tm-demo-controls] > summary");
        if (await summary.CountAsync() == 0) return;
        if (await page.Locator("[data-tm-demo-controls]").GetAttributeAsync("open") is null)
        {
            await summary.ClickAsync();
            await page.WaitForSelectorAsync("[data-tm-demo-controls][open]", new() { Timeout = 15000 });
        }
    }

    protected static async Task ForceEditorLayoutAsync(IPage page, string layout)
    {
        await OpenDemoControlsAsync(page);
        await page.SelectOptionAsync("[data-tm-demo-layout]", layout);
        await page.WaitForSelectorAsync($".tm-editor-shell[data-layout='{layout.ToLowerInvariant()}']",
            new() { Timeout = 30000 });
    }

    /// <summary>
    /// Saves a VIEWPORT-sized PNG (never full page) into the repo's named screenshot folder. UX review reads
    /// these frames as the screen the user actually sees: a full-page capture of a scrolling editor stretches
    /// the canvas and hides the "does it fit the phone" question the review asks (fáze 8 review, item 8).
    /// </summary>
    protected static async Task SaveNamedViewportScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepositoryRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "email-templates");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png,
            FullPage = false,
        });
    }

    /// <summary>Saves a full-page PNG into the repo's named screenshot folder for UX review.</summary>
    protected static async Task SaveNamedScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepositoryRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "email-templates");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png,
            FullPage = true,
        });
    }

    protected static DirectoryInfo FindRepositoryRoot()
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

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    // ── smtp4dev REST client ──────────────────────────────────────────────────────────────────

    /// <summary>One delivered message as reported by smtp4dev's message list.</summary>
    protected sealed record Smtp4DevMessage(string Id, string Subject, IReadOnlyList<string> To);

    /// <summary>Polls smtp4dev until a message matching <paramref name="searchTerm"/> appears, or times out.</summary>
    protected static async Task<Smtp4DevMessage> PollForMessageAsync(string searchTerm, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            var match = await FindMessageAsync(searchTerm);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"smtp4dev did not receive a message matching '{searchTerm}' in time.");
    }

    private static async Task<Smtp4DevMessage?> FindMessageAsync(string searchTerm)
    {
        using var doc = JsonDocument.Parse(
            await Http.GetStringAsync($"{Smtp4DevBase}/api/Messages?searchTerms={Uri.EscapeDataString(searchTerm)}&pageSize=50"));
        foreach (var result in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            var to = result.TryGetProperty("to", out var toEl) && toEl.ValueKind == JsonValueKind.Array
                ? toEl.EnumerateArray().Select(t => t.GetString() ?? string.Empty).ToList()
                : new List<string>();
            return new Smtp4DevMessage(
                result.GetProperty("id").GetString()!,
                result.TryGetProperty("subject", out var s) ? s.GetString() ?? string.Empty : string.Empty,
                to);
        }

        return null;
    }

    /// <summary>Gets the rendered HTML body of a delivered message.</summary>
    protected static Task<string> GetMessageHtmlAsync(string id)
        => Http.GetStringAsync($"{Smtp4DevBase}/api/Messages/{id}/html");

    /// <summary>Gets the plain-text body, or <c>null</c> when the message has no text part (404).</summary>
    protected static async Task<string?> GetMessagePlaintextOrNullAsync(string id)
    {
        var response = await Http.GetAsync($"{Smtp4DevBase}/api/Messages/{id}/plaintext");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
    }

    /// <summary>Deletes a message from smtp4dev to keep the shared instance clean.</summary>
    protected static async Task DeleteMessageAsync(string id)
        => await Http.DeleteAsync($"{Smtp4DevBase}/api/Messages/{id}");
}
