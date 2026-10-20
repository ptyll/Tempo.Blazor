using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// bUnit tests for TmPdfAnnotator: mode toolbar, annotation layer rendering per kind,
/// panel interactions (create, reply, resolve, reopen, delete), per-author colors,
/// draft lifecycle, and export/print JS dispatch.
/// </summary>
public class TmPdfAnnotatorTests : LocalizationTestBase
{
    private const string DocUrl = "/sample.pdf";
    private const string DocId = "doc-1";

    public TmPdfAnnotatorTests()
    {
        JSInterop.Setup<bool>("tmPdfViewer.isAvailable").SetResult(true);
        JSInterop.Setup<double[]>("tmPdfAnnotator.measure", _ => true).SetResult([800, 1000]);
    }

    private static DocumentCommentUser Alice => new() { UserId = "alice", DisplayName = "Alice", Role = "lawyer" };
    private static DocumentCommentUser Bob => new() { UserId = "bob", DisplayName = "Bob", Role = "client" };

    private static DocumentCommentThread Highlight(string id, string author = "alice", int page = 1)
        => new()
        {
            Id = id,
            Kind = DocumentAnnotationKind.Highlight,
            Anchor = DocumentCommentAnchor.TextRange(page,
                [DocumentCommentRect.Create(0.1, 0.2, 0.3, 0.02)], "quoted"),
            Comments = [new DocumentComment { Id = id + "-c", AuthorId = author, AuthorName = author, Body = "note", CanDelete = true }]
        };

    private static DocumentCommentThread Stamp(string id, string text = "APPROVED")
        => new()
        {
            Id = id,
            Kind = DocumentAnnotationKind.Stamp,
            StampText = text,
            Anchor = DocumentCommentAnchor.Area(1, 0.6, 0.1, 0.2, 0.06),
            Comments = [new DocumentComment { Id = id + "-c", AuthorId = "alice", AuthorName = "Alice", Body = text }]
        };

    private static DocumentCommentThread Drawing(string id)
        => new()
        {
            Id = id,
            Kind = DocumentAnnotationKind.Drawing,
            Anchor = DocumentCommentAnchor.Point(1, 0.1, 0.1),
            InkStrokes = [DocumentInkStroke.Create([(0.1, 0.1), (0.2, 0.2), (0.3, 0.1)])],
            Comments = [new DocumentComment { Id = id + "-c", AuthorId = "bob", AuthorName = "Bob", Body = "sketch" }]
        };

    private InMemoryPdfAnnotationProvider Seeded(params DocumentCommentThread[] threads)
        => new(new Dictionary<string, IReadOnlyList<DocumentCommentThread>> { [DocId] = threads });

    private IRenderedComponent<TmPdfAnnotator> Render(
        IPdfAnnotationProvider? provider = null,
        Action<Bunit.ComponentParameterCollectionBuilder<TmPdfAnnotator>>? configure = null)
    {
        return Render<TmPdfAnnotator>(p =>
        {
            p.Add(x => x.Url, DocUrl);
            p.Add(x => x.DocumentId, DocId);
            p.Add(x => x.CurrentUser, Alice);
            if (provider is not null)
            {
                p.Add(x => x.AnnotationProvider, provider);
            }
            configure?.Invoke(p);
        });
    }

    // ── Toolbar & modes ──────────────────────────────────────────────────────

    [Fact]
    public void Toolbar_RendersAllFiveModes_BrowseActiveByDefault()
    {
        var cut = Render();

        cut.Find("[data-testid='pdf-annotator-mode-browse']").ClassList.Should().Contain("tm-pdf-annotator__mode--active");
        cut.Find("[data-testid='pdf-annotator-mode-highlight']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-mode-comment']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Should().NotBeNull();
    }

    [Fact]
    public void Toolbar_ClickingMode_SwitchesActiveAndRaisesModeChanged()
    {
        PdfAnnotatorMode? changed = null;
        var cut = Render(configure: p => p
            .Add(x => x.ModeChanged, EventCallback.Factory.Create<PdfAnnotatorMode>(this, m => changed = m)));

        cut.Find("[data-testid='pdf-annotator-mode-comment']").Click();

        changed.Should().Be(PdfAnnotatorMode.Comment);
        cut.Find("[data-testid='pdf-annotator-mode-comment']").ClassList.Should().Contain("tm-pdf-annotator__mode--active");
    }

    [Fact]
    public void Toolbar_StampSelect_VisibleOnlyInStampMode()
    {
        var cut = Render();
        cut.FindAll("[data-testid='pdf-annotator-stamp-select']").Should().BeEmpty();

        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();

        cut.FindAll("[data-testid='pdf-annotator-stamp-select']").Should().HaveCount(1);
    }

    // ── Layer rendering per kind ─────────────────────────────────────────────

    [Fact]
    public void Layer_RendersHighlightStampAndInkAnnotations()
    {
        var cut = Render(Seeded(Highlight("h1"), Stamp("s1"), Drawing("d1")));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='pdf-annotator-highlight']").Should().HaveCount(1);
            cut.FindAll("[data-testid='pdf-annotator-stamp']").Should().HaveCount(1);
            cut.FindAll("[data-testid='pdf-annotator-ink']").Should().HaveCount(1);
        });
    }

    [Fact]
    public void Layer_StampShowsItsText()
    {
        var cut = Render(Seeded(Stamp("s1", "REJECTED")));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='pdf-annotator-stamp']").TextContent.Should().Contain("REJECTED"));
    }

    [Fact]
    public void Layer_UsesAuthorColorForAnnotations()
    {
        var cut = Render(Seeded(Highlight("h1", author: "alice")), p => p
            .Add(x => x.AuthorColors, new Dictionary<string, string> { ["alice"] = "#ff0066" }));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='pdf-annotator-highlight']").GetAttribute("style").Should().Contain("#ff0066"));
    }

    [Fact]
    public void Layer_UsesRoleColorWhenNoAuthorColor()
    {
        var cut = Render(Seeded(Highlight("h1", author: "alice")), p => p
            .Add(x => x.Users, new List<DocumentCommentUser> { Alice, Bob })
            .Add(x => x.RoleColors, new Dictionary<string, string> { ["lawyer"] = "#00ccff" }));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='pdf-annotator-highlight']").GetAttribute("style").Should().Contain("#00ccff"));
    }

    [Fact]
    public void Layer_HidesAnnotationsFromOtherPages()
    {
        var cut = Render(Seeded(Highlight("h1", page: 2)));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='pdf-annotator-panel'] [data-testid='pdf-annotator-thread']").Should().HaveCount(1);
            cut.FindAll("[data-testid='pdf-annotator-highlight']").Should().BeEmpty();
        });
    }

    // ── Comment mode: click → draft → create ─────────────────────────────────

    [Fact]
    public async Task CommentMode_ClickOnSurface_OpensDraftComposer()
    {
        var cut = Render();
        cut.Find("[data-testid='pdf-annotator-mode-comment']").Click();

        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 500 });

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-new']").Should().HaveCount(1));
    }

    [Fact]
    public async Task CommentMode_SubmittingDraft_CreatesPointThread()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-comment']").Click();

        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 500 });
        cut.WaitForElement("[data-testid='pdf-annotator-new-input']").Input("first note");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1);
            threads[0].Kind.Should().Be(DocumentAnnotationKind.Comment);
            threads[0].Anchor.Kind.Should().Be(DocumentCommentAnchorKind.Point);
            threads[0].Anchor.X.Should().BeApproximately(0.5, 0.01);
            threads[0].Anchor.Y.Should().BeApproximately(0.5, 0.01);
            threads[0].Comments[0].Body.Should().Be("first note");
            threads[0].Comments[0].AuthorId.Should().Be("alice");
        });
    }

    [Fact]
    public async Task Draft_Cancel_DiscardsWithoutCreating()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-comment']").Click();

        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 100, OffsetY = 100 });
        cut.WaitForElement("[data-testid='pdf-annotator-new-cancel']").Click();

        cut.FindAll("[data-testid='pdf-annotator-new']").Should().BeEmpty();
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty();
    }

    // ── Highlight mode: text selection → draft with quote ────────────────────

    [Fact]
    public async Task HighlightMode_TextSelection_CreatesHighlightThreadWithQuote()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-highlight']").Click();

        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        await cut.InvokeAsync(() => viewer.OnTextSelectionChanged("selected words", 1, [0.1, 0.2, 0.3, 0.02]));

        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        cut.Find("[data-testid='pdf-annotator-new-input']").Input("highlight note");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1);
            threads[0].Kind.Should().Be(DocumentAnnotationKind.Highlight);
            threads[0].Anchor.Kind.Should().Be(DocumentCommentAnchorKind.TextRange);
            threads[0].Anchor.HighlightedText.Should().Be("selected words");
            threads[0].Anchor.Rects.Should().HaveCount(1);
        });
    }

    // ── Stamp mode: click places stamp immediately ───────────────────────────

    [Fact]
    public async Task StampMode_Click_CreatesStampThreadImmediately()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();

        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 200, OffsetY = 100 });

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1);
            threads[0].Kind.Should().Be(DocumentAnnotationKind.Stamp);
            threads[0].StampText.Should().NotBeNullOrEmpty();
        });

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-stamp']").Should().HaveCount(1));
    }

    // ── Draw mode: pointer stroke → draft → save ─────────────────────────────

    [Fact]
    public async Task DrawMode_PointerStroke_ThenSave_CreatesDrawingThread()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();

        var surface = cut.Find("[data-testid='pdf-annotator-surface']");
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100 });
        await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 160, OffsetY = 200 });
        await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 240, OffsetY = 100 });
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 240, OffsetY = 100 });

        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1);
            threads[0].Kind.Should().Be(DocumentAnnotationKind.Drawing);
            threads[0].InkStrokes.Should().HaveCount(1);
            threads[0].InkStrokes[0].Points.Should().HaveCountGreaterThanOrEqualTo(3);
            threads[0].InkStrokes[0].Points[0].X.Should().BeApproximately(0.1, 0.01);
        });
    }

    // ── Wave 0 — a11y and cross-page thread selection ────────────────────────

    [Fact]
    public void ThreadHeader_HasAriaExpandedAndControls()
    {
        var cut = Render<TmPdfAnnotatorPanel>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { Highlight("h1") })
            .Add(x => x.SelectedThreadId, "h1"));

        var header = cut.Find("[data-testid='pdf-annotator-thread']");
        header.GetAttribute("aria-expanded").Should().Be("true");
        var controls = header.GetAttribute("aria-controls");
        controls.Should().NotBeNullOrEmpty("an expanded thread header must reference its detail via aria-controls");
        cut.Find($"#{controls}").Should().NotBeNull();

        var collapsed = Render<TmPdfAnnotatorPanel>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { Highlight("h1") })
            .Add(x => x.SelectedThreadId, (string?)null));
        collapsed.Find("[data-testid='pdf-annotator-thread']")
            .GetAttribute("aria-expanded").Should().Be("false");
        // aria-controls only makes sense while the region is expanded.
        collapsed.Find("[data-testid='pdf-annotator-thread']")
            .GetAttribute("aria-controls").Should().BeNull("a collapsed thread header must not reference a detail region");
    }

    // ── Review fix 9: per-instance detail ids ─────────────────────────────────

    [Fact]
    public void AnnotatorPanels_TwoInstancesOnOnePage_GetDistinctDetailIds()
    {
        var cut = Render<TwoAnnotatorPanelsHost>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { Highlight("h1") }));

        var headers = cut.FindAll("[data-testid='pdf-annotator-thread']");
        headers.Should().HaveCount(2);
        var details = cut.FindAll("[data-testid='pdf-annotator-detail']");
        details.Should().HaveCount(2);

        var ids = details.Select(d => d.Id).ToList();
        ids[0].Should().NotBeNullOrWhiteSpace();
        ids[1].Should().NotBeNullOrWhiteSpace();
        ids[0].Should().NotBe(ids[1], "two annotator panels on one page must not share detail ids");

        for (var i = 0; i < headers.Count; i++)
        {
            headers[i].GetAttribute("aria-controls").Should().Be(ids[i],
                "each expanded header's aria-controls points at its own detail region");
        }
    }

    /// <summary>Host rendering two annotator panels side by side (two viewers on one page).</summary>
    private sealed class TwoAnnotatorPanelsHost : ComponentBase
    {
        [Parameter] public IReadOnlyList<DocumentCommentThread> Threads { get; set; } = [];

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            for (var i = 0; i < 2; i++)
            {
                builder.OpenComponent<TmPdfAnnotatorPanel>(i * 10);
                builder.AddAttribute(i * 10 + 1, nameof(TmPdfAnnotatorPanel.Threads), Threads);
                builder.AddAttribute(i * 10 + 2, nameof(TmPdfAnnotatorPanel.SelectedThreadId), "h1");
                builder.CloseComponent();
            }
        }
    }

    [Fact]
    public void SelectThreadOnOtherPage_ChangesPage()
    {
        var cut = Render(Seeded(Highlight("h1", page: 1), Highlight("h3", page: 3)));

        cut.WaitForElement("[data-testid='pdf-annotator-thread'][data-thread-id='h3']").Click();

        // The annotator navigates to the thread's page: its layer now renders that thread's marker.
        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-pdf-annotator-layer [data-thread-id='h3']")
                .Should().NotBeEmpty("selecting a thread anchored on page 3 must navigate there and render its marker"));
    }

    [Fact]
    public void ColorIndicator_HasAccessibleLabel()
    {
        var cut = Render();

        var chip = cut.Find(".tm-pdf-annotator__color-chip");
        chip.GetAttribute("aria-label").Should().NotBeNullOrEmpty();
        chip.HasAttribute("aria-hidden").Should().BeFalse("the color indicator must be announced to screen readers");
    }

    // ── Panel: resolve / reopen / reply / delete ─────────────────────────────

    [Fact]
    public void Panel_ResolveThread_MarksResolvedAndHidesFromDefaultFilter()
    {
        var cut = Render(Seeded(Highlight("h1")));

        cut.WaitForElement("[data-testid='pdf-annotator-thread']").Click();
        cut.WaitForElement("[data-testid='pdf-annotator-resolve']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-thread']").Should().BeEmpty());

        cut.Find("[data-testid='pdf-annotator-show-resolved']").Change(true);
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-thread']").Should().HaveCount(1));
    }

    [Fact]
    public void Panel_ReopenResolvedThread_MakesItOpenAgain()
    {
        var resolved = Highlight("h1");
        resolved.Status = DocumentCommentThreadStatus.Resolved;
        var cut = Render(Seeded(resolved));

        cut.Find("[data-testid='pdf-annotator-show-resolved']").Change(true);
        cut.WaitForElement("[data-testid='pdf-annotator-thread']").Click();
        cut.WaitForElement("[data-testid='pdf-annotator-reopen']").Click();

        cut.Find("[data-testid='pdf-annotator-show-resolved']").Change(false);
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-thread']").Should().HaveCount(1));
    }

    [Fact]
    public void Panel_Reply_AppendsCommentToThread()
    {
        var provider = Seeded(Highlight("h1"));
        var cut = Render(provider);

        cut.WaitForElement("[data-testid='pdf-annotator-thread']").Click();
        cut.WaitForElement("[data-testid='pdf-annotator-reply-input']").Input("a reply");
        cut.Find("[data-testid='pdf-annotator-reply-submit']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-comment']").Should().HaveCount(2));
    }

    [Fact]
    public void Panel_EmptyState_ShownWithoutAnnotations()
    {
        var cut = Render();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-empty']").Should().HaveCount(1));
    }

    // ── Export & print ───────────────────────────────────────────────────────

    [Fact]
    public async Task Export_InvokesJsWithPayload()
    {
        var handler = JSInterop.SetupVoid("tmPdfAnnotator.exportPdf", _ => true).SetVoidResult();
        var cut = Render(Seeded(Highlight("h1")));
        cut.WaitForElement("[data-testid='pdf-annotator-thread']");

        await cut.InvokeAsync(() => cut.Find("[data-testid='pdf-annotator-export']").Click());

        cut.WaitForAssertion(() =>
        {
            handler.Invocations.Should().NotBeEmpty();
            var payload = handler.Invocations.Last().Arguments[1] as string;
            payload.Should().Contain("\"kind\":\"highlight\"");
        });
    }

    [Fact]
    public async Task ExportFlattened_PassesFlattenFlag()
    {
        var handler = JSInterop.SetupVoid("tmPdfAnnotator.exportPdf", _ => true).SetVoidResult();
        var cut = Render(Seeded(Highlight("h1")));
        cut.WaitForElement("[data-testid='pdf-annotator-thread']");

        await cut.InvokeAsync(() => cut.Find("[data-testid='pdf-annotator-export-flat']").Click());

        cut.WaitForAssertion(() =>
        {
            handler.Invocations.Should().NotBeEmpty();
            var options = handler.Invocations.Last().Arguments[2] as string;
            options.Should().Contain("\"flatten\":true");
        });
    }

    [Fact]
    public async Task Print_InvokesPrintJs()
    {
        var handler = JSInterop.SetupVoid("tmPdfAnnotator.printWithAnnotations", _ => true).SetVoidResult();
        var cut = Render(Seeded(Highlight("h1")));
        cut.WaitForElement("[data-testid='pdf-annotator-thread']");

        await cut.InvokeAsync(() => cut.Find("[data-testid='pdf-annotator-print']").Click());

        cut.WaitForAssertion(() => handler.Invocations.Should().NotBeEmpty());
    }

    // ── Host-collection safety (regression pattern from K6) ─────────────────

    [Fact]
    public void AnnotationsChanged_HandlerMutatingList_DoesNotThrow()
    {
        var external = new List<DocumentCommentThread>();
        var provider = Seeded(Highlight("h1"));

        var act = () => Render(provider, p => p
            .Add(x => x.AnnotationsChanged, EventCallback.Factory.Create<IReadOnlyList<DocumentCommentThread>>(
                this, list => external.Add(new DocumentCommentThread { Id = "mutant" }))));

        act.Should().NotThrow();
    }
}
