using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// bUnit tests for <see cref="TmPdfAnnotatorPanel"/>: the draft indicator color must come from
/// <see cref="PdfAnnotationColorHelper"/>, not from a hex literal hard-coded in the component
/// (PdfViewer redesign phase 2 — the palette is data owned by the helper).
/// </summary>
public class TmPdfAnnotatorPanelTests : LocalizationTestBase
{
    [Fact]
    public void Draft_WithoutColor_UsesHelperDefault_NotHex()
    {
        // The helper's first palette entry is the library's default annotation color (blue 600).
        var expected = PdfAnnotationColorHelper.DefaultPalette[0];

        using var cut = Render<TmPdfAnnotatorPanel>(parameters => parameters
            .Add(p => p.Draft, new PdfAnnotatorDraft { Kind = DocumentAnnotationKind.Highlight }));

        var dot = cut.Find(".tm-pdf-annotator-panel__dot");
        dot.GetAttribute("style").Should().Be(
            $"background:{expected}",
            "the draft indicator paints the helper's default annotation color");

        // The components must not carry the color as a hex literal default either — a re-skin of
        // the palette would otherwise leave the draft behind (it is helper data, not markup data).
        foreach (var component in new[]
                 {
                     "TmPdfAnnotatorPanel.razor",
                     "TmPdfAnnotatorLayer.razor",
                 })
        {
            var source = File.ReadAllText(Path.Combine(
                ThemeCss.RepositoryRoot().FullName,
                "src", "Tempo.Blazor.PdfViewer", "Components", "Files", component));
            source.Should().NotContain(expected,
                $"{component} resolves the draft color through {nameof(PdfAnnotationColorHelper)}");
        }
    }

    [Fact]
    public void Draft_WithColor_UsesTheGivenColor()
    {
        using var cut = Render<TmPdfAnnotatorPanel>(parameters => parameters
            .Add(p => p.Draft, new PdfAnnotatorDraft { Kind = DocumentAnnotationKind.Highlight })
            .Add(p => p.DraftColor, "#0e7490"));

        cut.Find(".tm-pdf-annotator-panel__dot").GetAttribute("style")
            .Should().Be("background:#0e7490");
    }

    // ── Phase 8 (FZE-8): the mobile sheet presentation ───────────────────────

    [Fact]
    public void Mobile_DraftButtonsFullWidthSticky()
    {
        // ShowChrome=false is the sheet presentation (the annotator passes it on mobile): the
        // panel drops its own title/close for the sheet header and pins the draft composer to
        // the sheet bottom; the sheet CSS stretches its buttons to the full width.
        using var cut = Render<TmPdfAnnotatorPanel>(parameters => parameters
            .Add(p => p.ShowChrome, false)
            .Add(p => p.Draft, new PdfAnnotatorDraft { Kind = DocumentAnnotationKind.Comment }));

        cut.Find("[data-testid='pdf-annotator-panel']").ClassList
            .Should().Contain("tm-pdf-annotator-panel--sheet",
                "the sheet modifier is the full-width/sticky CSS hook");
        cut.Find("[data-testid='pdf-annotator-new']").QuerySelector(".tm-pdf-composer")
            .ClassList.Should().Contain("tm-pdf-composer--sticky",
                "the draft composer pins to the sheet bottom on mobile");
        cut.FindAll("[data-testid='pdf-annotator-close']").Should().BeEmpty(
            "the sheet header owns the close button in the sheet presentation");
    }

    [Fact]
    public void Mobile_ReplyResolveFullWidth()
    {
        var thread = new DocumentCommentThread
        {
            Id = "t1",
            Anchor = DocumentCommentAnchor.Point(1, 0.5, 0.5),
            Comments = [new DocumentComment { Id = "t1-c", AuthorId = "alice", AuthorName = "Alice", Body = "note" }]
        };

        using var cut = Render<TmPdfAnnotatorPanel>(parameters => parameters
            .Add(p => p.ShowChrome, false)
            .Add(p => p.SelectedThreadId, "t1")
            .Add(p => p.Threads, new[] { thread }));

        cut.Find("[data-testid='pdf-annotator-panel']").ClassList
            .Should().Contain("tm-pdf-annotator-panel--sheet");

        // The expanded thread's sticky reply composer: Reply submits and Resolve is its ghost
        // secondary — both stretch to the full sheet width through the --sheet hook.
        cut.Find(".tm-pdf-composer--sticky").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-resolve']").Should().NotBeNull();
    }

    // ── FZE-12: the segmented thread filter ───────────────────────────────────

    private static DocumentCommentThread OpenThread(string id) => new()
    {
        Id = id,
        Anchor = DocumentCommentAnchor.Point(1, 0.1, 0.1),
        Comments = [new DocumentComment { Id = id + "-c", AuthorId = "a", AuthorName = "Alice", Body = "note" }]
    };

    private static DocumentCommentThread ResolvedThread(string id)
    {
        var thread = OpenThread(id);
        thread.Status = DocumentCommentThreadStatus.Resolved;
        return thread;
    }

    [Fact]
    public void ThreadFilter_SheetChrome_RendersSameFilter()
    {
        // ShowChrome=false is the mobile sheet presentation: the SAME segmented filter renders
        // there (the sheet header owns only the title and the close button).
        using var cut = Render<TmPdfAnnotatorPanel>(parameters => parameters
            .Add(p => p.ShowChrome, false)
            .Add(p => p.Threads, new[] { OpenThread("t1") }));

        cut.Find("[data-testid='pdf-annotator-filter']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-filter-all']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-filter-unresolved']").Should().NotBeNull();
        cut.FindAll("[data-testid='pdf-annotator-show-resolved']").Should().BeEmpty(
            "the FZE-12 checkbox is gone in the sheet presentation too");
    }

    [Fact]
    public void ThreadFilter_EmptyUnresolved_ShowsLocalizedEmptyState()
    {
        // Unresolved filter + only resolved threads: the list shows the localized
        // "no unresolved" empty state instead of the plain "no annotations" text.
        using var cut = Render<TmPdfAnnotatorPanel>(parameters => parameters
            .Add(p => p.ShowChrome, false)
            .Add(p => p.ShowResolved, false)
            .Add(p => p.Threads, new[] { ResolvedThread("t1") }));

        cut.Find("[data-testid='pdf-annotator-empty']").TextContent
            .Should().Contain("No unresolved", "the unresolved filter has its own empty state");

        // The All filter keeps the panel's original empty text.
        using var all = Render<TmPdfAnnotatorPanel>(parameters => parameters
            .Add(p => p.ShowChrome, false)
            .Add(p => p.ShowResolved, true)
            .Add(p => p.Threads, Array.Empty<DocumentCommentThread>()));
        all.Find("[data-testid='pdf-annotator-empty']").TextContent.Should().Contain("No annotations");
    }
}
