using Microsoft.Playwright;

namespace Tempo.Blazor.E2E.CanvasEngine;

/// <summary>Shared Playwright base for canvas document editor visual gates.</summary>
public abstract class CanvasEngineTestBase : WasmTestBase
{
    private const string CanvasHarnessRoute = "/canvas-engine-harness.html";

    /// <summary>Desktop, notebook, tablet, and mobile visual-gate viewport matrix.</summary>
    public static IReadOnlyList<CanvasEngineViewport> CanvasViewports { get; } =
    [
        new("desktop-1440x1000", 1440, 1000),
        new("notebook-1280x800", 1280, 800),
        new("tablet-900x1100", 900, 1100),
        new("mobile-390x844", 390, 844)
    ];

    /// <summary>Opens the static canvas document engine harness for a seed document.</summary>
    protected async Task<CanvasEnginePage> OpenCanvasEngineDocumentAsync(string seedId, CanvasEngineViewport viewport)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(viewport.Width, viewport.Height);

        var url = $"{BaseUrl}{CanvasHarnessRoute}?seedId={Uri.EscapeDataString(seedId)}";
        await GotoWithoutBootRecoveryAsync(page, url, "static canvas-engine-harness.html is not the Blazor app; there is no data-blazor-ready boot to recover");

        var canvasPage = new CanvasEnginePage(
            page,
            CreateOutputDirectory(GetType().Name, TestContext.TestName ?? "unknown-test", viewport.Name),
            TestContext,
            seedId,
            viewport);
        await canvasPage.WaitUntilReadyAsync();
        return canvasPage;
    }

    /// <summary>Runs an action across the standard canvas viewport matrix.</summary>
    protected static async Task ForEachViewportAsync(Func<CanvasEngineViewport, Task> action)
    {
        foreach (var viewport in CanvasViewports)
        {
            await action(viewport);
        }
    }

    private static string CreateOutputDirectory(string testClass, string testName, string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            SanitizePathSegment(testClass),
            SanitizePathSegment(testName),
            SanitizePathSegment(viewport));
        Directory.CreateDirectory(output);
        return output;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars);
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
}
