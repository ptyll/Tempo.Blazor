using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b / phase 1: a dialog that opens over the editor must not take part in the document
/// flow. The compare dialog and the debug modals used to render between the ribbon and the workspace and pushed
/// the canvas down; they now live in the fixed floating root. This guard measures the workspace before and after.
/// </summary>
internal static class DocumentEditorLayoutShiftGuard
{
    public const string WorkspaceSelector = "[data-testid='document-editor-workspace']";

    public static async Task<LayoutBox> MeasureWorkspaceAsync(IPage page)
    {
        var box = await page.Locator(WorkspaceSelector).BoundingBoxAsync();
        Assert.IsNotNull(box, "the editor workspace must be measurable");
        return new LayoutBox(box.X, box.Y, box.Width, box.Height);
    }

    public static async Task AssertWorkspaceUnchangedAsync(IPage page, LayoutBox before, string because)
    {
        var after = await MeasureWorkspaceAsync(page);
        Assert.AreEqual(before.X, after.X, 0.5, $"{because}: workspace x moved");
        Assert.AreEqual(before.Y, after.Y, 0.5, $"{because}: workspace y moved (layout shift)");
        Assert.AreEqual(before.Width, after.Width, 0.5, $"{because}: workspace width changed");
        Assert.AreEqual(before.Height, after.Height, 0.5, $"{because}: workspace height changed");
    }

    internal readonly record struct LayoutBox(double X, double Y, double Width, double Height);
}
