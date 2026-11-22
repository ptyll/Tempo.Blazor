using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 8: role matrix + external comments. An external client (Commenter role) gets a read-only
/// canvas with commenting only; the client's comments are color-coded per participant and carry the
/// KLIENT badge, with a participant legend in the panel. Edge cases: SuggestOnly typing lands as a
/// tracked revision (no direct edit), and Viewer cannot comment at all.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[DoNotParallelize]
public sealed class DocumentEditorRolePermissionsE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-8-canvas-role-comments";

    [TestMethod]
    public async Task ClientCommenter_SeesCommentOnlyUiWithColoredClientComments()
    {
        var page = await OpenAsync("role=commenter&persona=client");

        // Canvas is read-only for a commenter: typing must not change the document.
        await page.GetByTestId("document-canvas-page").First.ClickAsync();
        await page.Keyboard.TypeAsync("Xyz");
        await page.WaitForTimeoutAsync(800);
        var mirror = await page.EvaluateAsync<string>(
            "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent || ''");
        Assert.IsFalse(mirror.Contains("Xyz"), "a commenter must not be able to edit content");

        // The comments panel shows both threads, the participant legend and the client badge.
        await Assertions.Expect(page.GetByTestId("document-comment-thread")).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("document-comment-legend")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-comment-legend-item")).ToHaveCountAsync(2, new() { Timeout = 5_000 });
        await Assertions.Expect(page.GetByTestId("document-comment-client-badge").First)
            .ToHaveTextAsync("KLIENT", new() { Timeout = 5_000 });

        // The client's thread is colored with its own participant palette slot.
        var clientThreadClass = await page.EvaluateAsync<string>(
            """
            () => document.querySelector('.tm-document-comment-thread[data-comment-id="role-comments-client-thread"]')?.className || ''
            """);
        StringAssert.Contains(clientThreadClass, "tm-document-comment-thread--participant-1");
        StringAssert.Contains(clientThreadClass, "tm-document-comment-thread--external");
        var clientBadgeText = await page.EvaluateAsync<string>(
            """
            () => document.querySelector('.tm-document-comment-thread[data-comment-id="role-comments-client-thread"] .tm-document-comment-entry__external')?.textContent?.trim() || ''
            """);
        Assert.AreEqual("KLIENT", clientBadgeText);

        await ScreenshotAsync(page, "01-client-commenter-panel.png");
    }

    /// <summary>Edge case: SuggestOnly typing always lands as a tracked revision — never a direct edit.</summary>
    [TestMethod]
    public async Task SuggestOnly_TypingCreatesTrackedRevisionNotDirectEdit()
    {
        var page = await OpenAsync("role=suggestonly");

        // Track changes is forced on before any interaction.
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-track-changes-enabled') === 'true'
            """,
            options: new PageWaitForFunctionOptions { Timeout = 15_000 });

        var revisionsBefore = await ReadRevisionCountAsync(page);
        await page.GetByTestId("document-canvas-page").First.ClickAsync();
        await page.Keyboard.TypeAsync("Návrh");
        await page.WaitForFunctionAsync(
            $$"""
            () => Number(document.querySelector('[data-testid="document-canvas-a11y-mirror"]')
                ?.getAttribute('data-canvas-a11y-revision-count') || '0') > {{revisionsBefore}}
            """,
            options: new PageWaitForFunctionOptions { Timeout = 15_000 });

        // The Review-tab toggle is locked: it stays pressed after a click.
        await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
        var toggle = page.GetByTestId("document-track-changes");
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5_000 });
        await toggle.ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5_000 });

        await ScreenshotAsync(page, "02-suggestonly-tracked-edit.png");
    }

    /// <summary>Edge case: Viewer has no commenting UI at all.</summary>
    [TestMethod]
    public async Task Viewer_CannotCommentAndCannotEdit()
    {
        var page = await OpenAsync("role=viewer");

        // A viewer cannot edit: typing must not change the document.
        await page.GetByTestId("document-canvas-page").First.ClickAsync();
        await page.Keyboard.TypeAsync("Xyz");
        await page.WaitForTimeoutAsync(800);
        var mirror = await page.EvaluateAsync<string>(
            "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent || ''");
        Assert.IsFalse(mirror.Contains("Xyz"), "a viewer must not be able to edit content");

        // No "new comment" button and no reply composers for a viewer.
        await Assertions.Expect(page.GetByTestId("document-comment-new")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
        await Assertions.Expect(page.GetByTestId("document-comment-reply-composer")).ToHaveCountAsync(0, new() { Timeout = 5_000 });

        await ScreenshotAsync(page, "03-viewer-read-only.png");
    }

    private async Task<IPage> OpenAsync(string queryString)
    {
        var context = await CreateContextAsync();
        // The scenario is a Czech client review — run the demo UI in Czech (KLIENT badge).
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'cs')");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true&{queryString}&preferLocalDraft=false&disableCollaboration=true&resetSeed=true", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 45_000 });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-a11y-mirror"]')
                ?.textContent?.includes('Smluvní strany') === true
            """,
            options: new PageWaitForFunctionOptions { Timeout = 30_000 });
        return page;
    }

    private static Task<int> ReadRevisionCountAsync(IPage page)
        => page.EvaluateAsync<int>(
            """
            () => Number(document.querySelector('[data-testid="document-canvas-a11y-mirror"]')
                ?.getAttribute('data-canvas-a11y-revision-count') || '0')
            """);

    private static async Task ScreenshotAsync(IPage page, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var dir = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-role-permissions");
        Directory.CreateDirectory(dir);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png
        });
    }
}
