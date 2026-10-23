using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// bUnit tests for TmPdfAnnotator: mode toolbar, annotation layer rendering per kind,
/// panel interactions (create, reply, resolve, reopen, delete), per-author colors,
/// draft lifecycle, and export/print JS dispatch.
/// </summary>
public partial class TmPdfAnnotatorTests : LocalizationTestBase
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

        // The trigger button carries the accessible name: the visible label span is its text
        // content and the aria-label names the label plus the current color; the chip inside
        // stays a decorative visual aid (aria-hidden).
        var trigger = cut.Find("[data-testid='pdf-annotator-color-trigger']");
        trigger.TagName.Should().Be("BUTTON");
        trigger.GetAttribute("aria-haspopup").Should().Be("dialog");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
        trigger.GetAttribute("aria-label").Should().Contain("annotation color",
            "the accessible name is the localized color label plus the color name");

        var chip = trigger.QuerySelector(".tm-pdf-annotator__color-chip");
        chip.Should().NotBeNull();
        chip!.HasAttribute("aria-hidden").Should().BeTrue("the chip is a visual aid only");
        cut.Find("[data-testid='pdf-annotator-color-label']").TextContent.Should().NotBeEmpty();
    }

    // ── Panel: resolve / reopen / reply / delete ─────────────────────────────


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

    // ── Phase 5 — one toolbar: the annotator groups merge into the viewer toolbar ──

    [Fact]
    public void SingleToolbar_ViewerToolbarMergedViaSlots_NavigationAndZoomWork()
    {
        var cut = Render();

        // The annotator no longer renders its own toolbar row: the viewer's toolbar is THE toolbar.
        cut.FindAll(".tm-pdf-annotator__toolbar").Should().BeEmpty();
        var toolbar = cut.Find(".tm-pdf-viewer__toolbar");
        toolbar.QuerySelector("[data-testid='pdf-annotator-mode-browse']")
            .Should().NotBeNull("the annotator tools are merged into the viewer toolbar via ToolbarStart");
        toolbar.QuerySelector("[data-testid='pdf-annotator-panel-toggle']")
            .Should().NotBeNull("the panel toggle is merged into the viewer toolbar via ToolbarEnd");

        // Navigation works inside the merged toolbar: the document loads, next-page moves on.
        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        cut.InvokeAsync(() => viewer.OnPdfLoaded(5));
        cut.Render();
        cut.Find("[data-testid='pdf-page-input']").GetAttribute("value").Should().Be("1");

        var next = cut.FindAll(".tm-pdf-viewer__toolbar .tm-pdf-viewer__btn")
            .First(b => b.GetAttribute("aria-label")?.Contains("Next", StringComparison.Ordinal) == true);
        next.Click();
        cut.Find("[data-testid='pdf-page-input']").GetAttribute("value").Should().Be("2");

        // Zoom works inside the merged toolbar: zoom-in steps to the next preset.
        var zoomIn = cut.FindAll(".tm-pdf-viewer__toolbar .tm-pdf-viewer__btn")
            .First(b => b.GetAttribute("aria-label")?.Contains("Zoom in", StringComparison.Ordinal) == true);
        zoomIn.Click();
        cut.Find(".tm-pdf-viewer__toolbar").TextContent.Should().Contain("125%");
    }

    [Fact]
    public void Tools_ActiveToolHasAriaPressedAndTonalClass()
    {
        var cut = Render();

        var browse = cut.Find("[data-testid='pdf-annotator-mode-browse']");
        browse.GetAttribute("aria-pressed").Should().Be("true");
        browse.ClassList.Should().Contain("tm-pdf-annotator__mode--active");

        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();

        var stamp = cut.Find("[data-testid='pdf-annotator-mode-stamp']");
        stamp.GetAttribute("aria-pressed").Should().Be("true");
        stamp.ClassList.Should().Contain("tm-pdf-annotator__mode--active");
        // Only one tool is active at a time, and the inactive ones are pressed=false.
        cut.Find("[data-testid='pdf-annotator-mode-browse']").GetAttribute("aria-pressed").Should().Be("false");
        cut.FindAll(".tm-pdf-annotator__mode--active").Should().HaveCount(1);
    }

    [Fact]
    public void ColorIndicator_HasVisibleLabel()
    {
        var cut = Render();

        var label = cut.Find("[data-testid='pdf-annotator-color-label']");
        label.TextContent.Should().NotBeNullOrEmpty("the color trigger carries a visible text label");
        label.TextContent.Should().Contain("annotation color");

        var chip = cut.Find(".tm-pdf-annotator__color-chip");
        chip.HasAttribute("aria-hidden").Should().BeTrue("the visible label carries the name, the chip is decorative");
    }

    [Fact]
    public void ExportGroup_RendersSecondaryButtons()
    {
        var cut = Render();

        var group = cut.Find("[data-testid='pdf-annotator-export-group']");
        group.QuerySelector("[data-testid='pdf-annotator-export']").Should().NotBeNull();
        group.QuerySelector("[data-testid='pdf-annotator-export-flat']").Should().NotBeNull();
        group.QuerySelector("[data-testid='pdf-annotator-print']").Should().NotBeNull();
        group.QuerySelectorAll("button").Cast<AngleSharp.Dom.IElement>().Should().OnlyContain(
            b => b.ClassList.Contains("tm-pdf-annotator__action--secondary"),
            "the export group renders secondary (not primary) buttons");

        // Without a header the group lives at the end of the merged toolbar.
        cut.Find(".tm-pdf-viewer__toolbar").QuerySelector("[data-testid='pdf-annotator-export-group']")
            .Should().NotBeNull();

        // With a header the group moves into the header row and leaves the toolbar.
        var headered = Render(configure: p => p
            .Add(x => x.ShowHeader, true)
            .Add(x => x.Title, "Contract"));
        headered.Find(".tm-pdf-viewer__header").QuerySelector("[data-testid='pdf-annotator-export-group']")
            .Should().NotBeNull("the export group is a header row group when the header is on");
        headered.Find(".tm-pdf-viewer__toolbar").QuerySelector("[data-testid='pdf-annotator-export-group']")
            .Should().BeNull();

        // AllowExport=false removes the group entirely.
        var noExport = Render(configure: p => p.Add(x => x.AllowExport, false));
        noExport.FindAll("[data-testid='pdf-annotator-export-group']").Should().BeEmpty();
    }

    [Fact]
    public void PanelToggle_HidesAndShowsPanel_RaisesShowPanelChanged()
    {
        var states = new List<bool>();
        var cut = Render(Seeded(Highlight("h1")), p => p
            .Add(x => x.ShowPanelChanged, EventCallback.Factory.Create<bool>(this, states.Add)));

        cut.WaitForElement("[data-testid='pdf-annotator-panel-toggle']").Click();

        states.Should().ContainSingle().Which.Should().BeFalse();
        cut.FindAll("[data-testid='pdf-annotator-panel']").Should().BeEmpty();

        cut.Find("[data-testid='pdf-annotator-panel-toggle']").Click();

        states.Should().HaveCount(2);
        states[1].Should().BeTrue();
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-panel']").Should().HaveCount(1));
    }

    [Fact]
    public void ShowThumbnails_PassedToViewer()
    {
        var cut = Render(configure: p => p.Add(x => x.ShowThumbnails, true));

        cut.FindComponent<TmPdfViewer>().Instance.ShowThumbnails.Should().BeTrue();
        cut.WaitForAssertion(() =>
            cut.Find(".tm-pdf-viewer__thumbnails").Should().NotBeNull("the viewer renders the thumbnails sidebar"));

        var off = Render();
        off.FindComponent<TmPdfViewer>().Instance.ShowThumbnails.Should().BeFalse();
        off.FindAll(".tm-pdf-viewer__thumbnails").Should().BeEmpty();
    }

    [Fact]
    public void ShowSearch_PassedToViewer()
    {
        var cut = Render(configure: p => p.Add(x => x.ShowSearch, true));

        cut.FindComponent<TmPdfViewer>().Instance.ShowSearch.Should().BeTrue();
        cut.Find(".tm-pdf-viewer__toolbar")
            .QuerySelector("button[aria-label='Search']")
            .Should().NotBeNull("the viewer renders the search toggle inside the merged toolbar");

        var off = Render();
        off.FindComponent<TmPdfViewer>().Instance.ShowSearch.Should().BeFalse();
        off.Find(".tm-pdf-viewer__toolbar").QuerySelector("button[aria-label='Search']").Should().BeNull();
    }

    [Fact]
    public void SearchPresentation_PassedToViewer()
    {
        // The default is the viewer's own Floating default — the annotator only forwards.
        Render().FindComponent<TmPdfViewer>().Instance.SearchPresentation
            .Should().Be(PdfSearchPresentation.Floating);

        var inline = Render(configure: p => p
            .Add(x => x.ShowSearch, true)
            .Add(x => x.SearchPresentation, PdfSearchPresentation.Inline));
        inline.FindComponent<TmPdfViewer>().Instance.SearchPresentation
            .Should().Be(PdfSearchPresentation.Inline);
    }

    [Fact]
    public void AllowRotation_PassedToViewer()
    {
        var cut = Render(configure: p => p.Add(x => x.AllowRotation, true));

        cut.FindComponent<TmPdfViewer>().Instance.AllowRotation.Should().BeTrue();

        // The viewer renders its built-in groups once the document is loaded.
        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        cut.InvokeAsync(() => viewer.OnPdfLoaded(4));
        cut.Render();
        cut.Find(".tm-pdf-viewer__toolbar")
            .QuerySelector("button[aria-label='Rotate clockwise']")
            .Should().NotBeNull("the viewer renders the rotation button inside the merged toolbar");

        var off = Render();
        off.FindComponent<TmPdfViewer>().Instance.AllowRotation.Should().BeFalse();
    }

    [Fact]
    public void ThumbnailsCollapsed_PassedToViewer_TwoWay()
    {
        bool? raised = null;
        var cut = Render(configure: p => p
            .Add(x => x.ShowThumbnails, true)
            .Add(x => x.ThumbnailsCollapsed, true)
            .Add(x => x.ThumbnailsCollapsedChanged, EventCallback.Factory.Create<bool>(this, v => raised = v)));

        cut.FindAll(".tm-pdf-viewer__thumbnails").Should().BeEmpty("the collapsed sidebar is hidden");
        cut.Find("[data-testid='pdf-thumbnails-expand']").Click();
        raised.Should().BeFalse("the annotator forwards the viewer's ThumbnailsCollapsedChanged");

        // Keep-user-choice: an unbound re-render does not reset the expanded sidebar
        // to the stale ThumbnailsCollapsed=true parameter.
        cut.Render();
        cut.FindComponent<TmPdfViewer>().Instance.ThumbnailsCollapsed.Should().BeFalse();
        cut.FindAll(".tm-pdf-viewer__thumbnails").Should().HaveCount(1);
    }

    [Fact]
    public void ZoomMode_PassedToViewer_TwoWay()
    {
        PdfZoomMode? raised = null;
        var cut = Render(configure: p => p
            .Add(x => x.ZoomMode, PdfZoomMode.FitWidth)
            .Add(x => x.ZoomModeChanged, EventCallback.Factory.Create<PdfZoomMode>(this, m => raised = m)));

        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        cut.InvokeAsync(() => viewer.OnPdfLoaded(4));
        cut.Render();

        cut.FindComponent<TmPdfViewer>().Instance.ZoomMode.Should().Be(PdfZoomMode.FitWidth,
            "the annotator forwards the host's ZoomMode to the embedded viewer");

        cut.Find(".tm-pdf-viewer__toolbar button[aria-label='Zoom in']").Click();

        raised.Should().Be(PdfZoomMode.Custom, "zooming in abandons the fit mode and reports Custom");
        cut.FindComponent<TmPdfViewer>().Instance.ZoomMode.Should().Be(PdfZoomMode.Custom);

        // Keep-user-choice: an unbound re-render does not reset the viewer to the
        // stale FitWidth parameter.
        var unbound = Render(configure: p => p.Add(x => x.ZoomMode, PdfZoomMode.FitWidth));
        var unboundViewer = unbound.FindComponent<TmPdfViewer>().Instance;
        unbound.InvokeAsync(() => unboundViewer.OnPdfLoaded(4));
        unbound.Find(".tm-pdf-viewer__toolbar button[aria-label='Zoom in']").Click();
        unbound.Render();
        unbound.FindComponent<TmPdfViewer>().Instance.ZoomMode.Should().Be(PdfZoomMode.Custom);
    }

    [Fact]
    public void ShowViewerToolbarFalse_RendersCompactFallbackRow_WithoutViewerChrome()
    {
        var cut = Render(configure: p => p.Add(x => x.ShowViewerToolbar, false));

        // Exactly one toolbar: the annotator's compact row, no viewer chrome.
        cut.Find("[data-testid='pdf-annotator-toolbar']").Should().NotBeNull();
        cut.FindAll(".tm-pdf-viewer__toolbar").Should().BeEmpty();
        cut.Find("[data-testid='pdf-annotator-mode-browse']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-export']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-panel-toggle']").Should().NotBeNull();
    }

    // ── FZE-14: SinglePage stays the default — the additive contract of the PD1 lift ──

    [Fact]
    public void SinglePage_DefaultUnchanged()
    {
        var cut = Render();

        // The PD1 pin is lifted (ViewMode passes through), but the DEFAULT is additive:
        // hosts that never set ViewMode keep the pre-FZE-14 single-page annotator, and
        // ShowViewModeToggle stays opt-in.
        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        viewer.ViewMode.Should().Be(PdfViewMode.SinglePage);
        viewer.ShowViewModeToggle.Should().BeFalse("the toggle is opt-in");
        cut.Find(".tm-pdf-viewer__toolbar").QuerySelector(".tm-pdf-viewer__view-mode").Should().BeNull();
    }

    [Fact]
    public void ShowHeader_RendersTitleAndHeaderActions()
    {
        var cut = Render(configure: p => p
            .Add(x => x.ShowHeader, true)
            .Add(x => x.Title, "Contract 2024")
            .Add(x => x.Subtitle, "Final draft")
            .Add(x => x.HeaderStart, builder => builder.AddMarkupContent(0, "<span data-testid='host-header-start'>S</span>"))
            .Add(x => x.HeaderActions, builder => builder.AddMarkupContent(0, "<span data-testid='host-header-actions'>H</span>")));

        var header = cut.Find(".tm-pdf-viewer__header");
        header.TextContent.Should().Contain("Contract 2024");
        header.TextContent.Should().Contain("Final draft");
        header.QuerySelector("[data-testid='host-header-start']").Should().NotBeNull();
        header.QuerySelector("[data-testid='host-header-actions']")
            .Should().NotBeNull("the host HeaderActions render in the header row after the export group");
        header.QuerySelector("[data-testid='pdf-annotator-export-group']").Should().NotBeNull();

        // Default (ShowHeader=false) renders no header row at all.
        var plain = Render();
        plain.FindAll(".tm-pdf-viewer__header").Should().BeEmpty();
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

    // ── Phase 4 — shared cards: panel close and comment edit ─────────────────

    [Fact]
    public void Panel_Close_RaisesShowPanelChanged()
    {
        var showPanel = true;
        var cut = Render(Seeded(Highlight("h1")), p => p
            .Add(x => x.ShowPanelChanged, EventCallback.Factory.Create<bool>(this, v => showPanel = v)));

        cut.WaitForElement("[data-testid='pdf-annotator-close']").Click();

        showPanel.Should().BeFalse("the panel's ✕ raises ShowPanelChanged(false) for two-way ShowPanel binding");

        // A host bound to ShowPanel=false does not render the panel at all.
        var hidden = Render(Seeded(Highlight("h1")), p => p.Add(x => x.ShowPanel, false));
        hidden.FindAll("[data-testid='pdf-annotator-panel']").Should().BeEmpty();
    }

    [Fact]
    public void Panel_CloseUnbound_HidesPanel_AndToolbarToggleReopens()
    {
        var cut = Render(Seeded(Highlight("h1")));

        cut.WaitForElement("[data-testid='pdf-annotator-close']").Click();

        cut.FindAll("[data-testid='pdf-annotator-panel']").Should().BeEmpty(
            "the ✕ must hide the panel even when ShowPanel is unbound (no host echo)");
        cut.Find("[data-testid='pdf-annotator-panel-toggle']")
            .GetAttribute("aria-pressed").Should().Be("false", "the toggle mirrors the hidden panel");

        cut.Find("[data-testid='pdf-annotator-panel-toggle']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-panel']").Should().HaveCount(1,
                "the toolbar toggle reopens the panel"));
        cut.Find("[data-testid='pdf-annotator-panel-toggle']")
            .GetAttribute("aria-pressed").Should().Be("true", "the toggle mirrors the visible panel");
    }

    [Fact]
    public async Task EditComment_CallsProviderEditAsync()
    {
        var thread = Highlight("h1");
        thread.Comments[0].CanEdit = true;
        var provider = Seeded(thread);
        var cut = Render(provider);

        cut.WaitForElement("[data-testid='pdf-annotator-thread']").Click();
        cut.WaitForElement(".tm-pdf-annotator-panel .tm-pdf-comment-item__menu .tm-dropdown-trigger").Click();
        cut.WaitForElement("[data-testid='pdf-annotator-edit']").Click();
        cut.WaitForElement("[data-testid='pdf-annotator-edit-input']").Input("edited by bob");
        cut.Find("[data-testid='pdf-annotator-edit-save']").Click();

        await cut.InvokeAsync(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads[0].Comments[0].Body.Should().Be("edited by bob");
            threads[0].Comments[0].EditedAt.Should().NotBeNull();
        });
    }

    // ── Phase 8 (FZE-8): the annotator laid out on TmEditorShell ─────────────

    /// <summary>Renders the annotator with a forced <see cref="TmLayoutMode"/> (tests need no DOM).</summary>
    private IRenderedComponent<TmPdfAnnotator> RenderInLayout(
        TmLayoutMode mode,
        IPdfAnnotationProvider? provider = null,
        Action<Bunit.ComponentParameterCollectionBuilder<TmPdfAnnotator>>? configure = null)
        => Render(provider, p =>
        {
            p.Add(x => x.LayoutMode, mode);
            configure?.Invoke(p);
        });

    [Fact]
    public void UsesEditorShell_CanvasAndRightRegions()
    {
        var cut = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")));

        // The annotator renders through the core shell: the viewer lives in the canvas region,
        // the thread panel in the docked right region.
        cut.FindComponents<TmEditorShell>().Should().HaveCount(1);
        cut.Find("[data-region='canvas'] .tm-pdf-viewer").Should().NotBeNull(
            "the viewer (with its thumbnails) is the shell's canvas");
        cut.Find("[data-region='right'] [data-testid='pdf-annotator-panel']").Should().NotBeNull(
            "the annotator panel is the shell's right dock");

        // ShowPanel=false clears the right region entirely (no docked panel, no sheet).
        var hidden = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")),
            p => p.Add(x => x.ShowPanel, false));
        hidden.FindAll("[data-region='right']").Should().BeEmpty();
    }

    [Fact]
    public void LayoutTablet_ToolsHaveLabelsBelow_PanelIsSideSheet()
    {
        var cut = RenderInLayout(TmLayoutMode.Tablet, Seeded(Highlight("h1")),
            p => p.Add(x => x.ToolbarLabels, ToolbarLabels.IconsWithText));

        // The annotator's tools show their labels UNDER the icon (the ribbon style of the
        // tablet reference) when ToolbarLabels shows labels at all.
        cut.Find("[data-testid='pdf-annotator-mode-browse']").ClassList
            .Should().Contain("tm-toolbar-btn--label-below");
        cut.Find("[data-testid='pdf-annotator-mode-draw']").ClassList
            .Should().Contain("tm-toolbar-btn--label-below");

        // The panel presents as the side sheet region of the shell (docked right, never inline
        // in the viewer column).
        cut.Find("[data-region='right'] [data-testid='pdf-annotator-panel']").Should().NotBeNull();
    }

    [Fact]
    public void LayoutMobile_HidesHighlightStampDrawTools_G2()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")));

        // G2: mobile is viewing + light edits — highlight, stamp and draw are not offered.
        cut.FindAll("[data-testid='pdf-annotator-mode-highlight']").Should().BeEmpty();
        cut.FindAll("[data-testid='pdf-annotator-mode-stamp']").Should().BeEmpty();
        cut.FindAll("[data-testid='pdf-annotator-mode-draw']").Should().BeEmpty();
        cut.Find("[data-testid='pdf-annotator-mode-browse']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-mode-comment']").Should().NotBeNull();
    }

    [Fact]
    public void LayoutMobile_ToolPaletteIsHorizontallyScrollable()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile);

        // The palette is a horizontally scrollable strip (the G2 tile row), holding exactly
        // the two mobile tiles.
        var track = cut.Find(".tm-pdf-annotator__palette-track");
        track.Should().NotBeNull();
        track.QuerySelectorAll("[data-testid='pdf-annotator-mode-browse'], [data-testid='pdf-annotator-mode-comment']")
            .Should().HaveCount(2);
    }

    [Fact]
    public void LayoutMobile_ActionBar_ExportFlattenPrintMore()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile);

        // The F5 action bar: Export PDF | Export flattened | Print on the bar, everything
        // else (the threads entry) in the shared "More" menu.
        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.QuerySelector("[data-action-id='export']").Should().NotBeNull();
        bar.QuerySelector("[data-action-id='export-flat']").Should().NotBeNull();
        bar.QuerySelector("[data-action-id='print']").Should().NotBeNull();

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.FindAll(".tm-overlay-panel-sheet [data-action-id='threads']")
            .Should().NotBeEmpty("the threads sheet opens from the action bar's More menu");
    }

    [Fact]
    public void LayoutMobile_ThreadMarkerOpensPanelSheetOnThread()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")),
            p => p.Add(x => x.ShowPanel, false));

        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty("the sheet starts closed");

        // A highlight thread renders the highlight hit area; tapping it selects the thread.
        cut.WaitForElement("[data-testid='pdf-annotator-highlight']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-editor-shell__sheet").Should().HaveCount(1,
                "tapping a thread marker opens the panel bottom sheet"));
        var sheet = cut.Find(".tm-editor-shell__sheet");
        sheet.QuerySelector("[data-testid='pdf-annotator-panel']").Should().NotBeNull();
        sheet.QuerySelector("[data-testid='pdf-annotator-thread']")
            .GetAttribute("aria-expanded").Should().Be("true", "the sheet opens on the tapped thread");
    }

    [Fact]
    public void LayoutMobile_SwitchFromDrawMode_FallsBackToBrowse()
    {
        // A host that pinned Draw (or restored it from state) on a phone gets Browse: the
        // editing tools do not exist on a mobile layout (G2).
        var cut = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")),
            p => p.Add(x => x.Mode, PdfAnnotatorMode.Draw));

        cut.Find("[data-testid='pdf-annotator-mode-browse']").ClassList
            .Should().Contain("tm-pdf-annotator__tile--active",
                "the active edit mode falls back to Browse on a mobile layout");
    }

    // ── Phase 8 review fixes ─────────────────────────────────────────────────

    [Fact]
    public void MobileSheet_StartsClosed_ByDefault_DesktopKeepsPanelOpen()
    {
        // Review 3: the first resolution to mobile closes the sheet unless the host supplied
        // ShowPanel; the desktop default stays open.
        var mobile = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")));
        mobile.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();

        var desktop = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")));
        desktop.Find("[data-region='right'] [data-testid='pdf-annotator-panel']").Should().NotBeNull();
    }

    [Fact]
    public async Task MobileSheet_SwitchToMobile_ClosesSheet_UnlessHostSuppliedShowPanel()
    {
        var states = new List<bool>();
        var cut = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")), p => p
            .Add(x => x.ShowPanelChanged, EventCallback.Factory.Create<bool>(this, states.Add)));
        cut.Find("[data-region='right'] [data-testid='pdf-annotator-panel']").Should().NotBeNull();

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmPdfAnnotator.LayoutMode)] = TmLayoutMode.Mobile })));
        cut.Render();

        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty(
            "a desktop→mobile switch closes the sheet (review 3)");
        states.Should().Contain(false, "the auto-close is announced through ShowPanelChanged");

        // A host that explicitly supplies ShowPanel keeps the control.
        var supplied = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")),
            p => p.Add(x => x.ShowPanel, true));
        supplied.Find(".tm-editor-shell__sheet").Should().NotBeNull();
    }

    [Fact]
    public async Task MobileSheet_AutoTransientLayoutFlap_DoesNotCloseThePanel()
    {
        // Phase 12 review: under Auto the shell's container measurement can flap while the page
        // settles (a full-page screenshot, a transient reflow). The mobile sheet auto-close is
        // debounced, so an intermediate blip must NOT hide the open panel.
        // The debounce runs on a FakeTimeProvider: "the window has passed" means Advance crossed
        // it, so a loaded machine can never let the 150 ms timer fire between the two re-resolves.
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
            .AddSingleton<TimeProvider>(Services, clock);
        var cut = Render(Seeded(Highlight("h1")));
        var observer = cut.FindComponent<TmEditorShell>().FindComponent<TmLayoutObserver>();

        // A transient re-resolve to mobile leaves the panel visible (the close is pending) …
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("mobile"));
        cut.Find("[data-testid='pdf-annotator-panel']").Should().NotBeNull("a blip must not close the panel");

        // … and resolving back cancels the pending close for good, even past the window.
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("desktop"));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        cut.Find("[data-testid='pdf-annotator-panel']")
            .Should().NotBeNull("the cancelled auto-close must stay cancelled");

        // A STABLE resolution to mobile still starts the sheet closed (after the debounce).
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("mobile"));
        cut.Find("[data-testid='pdf-annotator-panel']")
            .Should().NotBeNull("the close waits for the debounce window");
        clock.Advance(TimeSpan.FromMilliseconds(200));
        cut.WaitForState(() => cut.FindAll(".tm-editor-shell__sheet").Count == 0,
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MobileSheetClose_RestoresFocusToTheOpener_ViaJsHelper()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")));

        // Opening from a marker remembers the opener (review 1) ...
        cut.WaitForElement("[data-testid='pdf-annotator-highlight']").Click();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == "tmPdfAnnotator.rememberFocus").Should().Be(1));

        // ... and closing the sheet returns focus through the JS helper (the opener, or the
        // first action-bar tile when the opener is gone — the helper decides).
        cut.Find(".tm-editor-shell__sheet-close").Click();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == "tmPdfAnnotator.focusOpener").Should().Be(1));
    }

    [Fact]
    public void MobileMarkerOpensSheetAtHalfSnap_ScrollsCardAndFocusesComposer()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")));

        cut.WaitForElement("[data-testid='pdf-annotator-highlight']").Click();

        cut.WaitForAssertion(() =>
            cut.FindComponent<TmEditorShell>().Instance.MobileSheetSnapIndex
                .Should().Be(1, "the sheet opens from a marker at the HALF snap, 0.7 (review 12)"));

        // The selected card is scrolled into view and the reply composer focused (review 12).
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == "tmPdfAnnotator.scrollSelectedCardIntoView").Should().Be(1));
        var focus = JSInterop.Invocations.Single(i => i.Identifier == "tmPdfAnnotator.focusDescendant");
        focus.Arguments[^1].Should().Be("[data-testid='pdf-annotator-reply-input']");
    }

    [Fact]
    public void MobileDraft_ComposerInputIsFocusedViaJsHelper()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile);

        cut.Find("[data-testid='pdf-annotator-mode-comment']").Click();
        cut.InvokeAsync(async () => await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 500 }));

        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        var focus = JSInterop.Invocations.Single(i => i.Identifier == "tmPdfAnnotator.focusDescendant");
        focus.Arguments[^1].Should().Be("[data-testid='pdf-annotator-new-input']",
            "opening the sheet from a new draft focuses the draft textarea (review 12)");
    }

    [Fact]
    public void MobileBar_FoldsViewerPagesAndSearch_IntoSingleActionBar()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, configure: p => p
            .Add(x => x.ShowThumbnails, true)
            .Add(x => x.ShowSearch, true));

        // Exactly ONE action bar: the viewer renders none of its own (review 4).
        cut.FindAll(".tm-mobile-action-bar__bar").Should().HaveCount(1);
        cut.FindComponent<TmPdfViewer>().Instance.ShowMobileActionBar.Should().BeFalse();

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.QuerySelector("[data-action-id='pages']").Should().NotBeNull();
        bar.QuerySelector("[data-action-id='search']").Should().NotBeNull();

        // The tiles drive the viewer through its public API: Pages opens the thumbnails sheet…
        bar.QuerySelector("[data-action-id='pages']")!.Click();
        cut.WaitForAssertion(() =>
            cut.Find(".tm-pdf-viewer__thumbnails-list--sheet").Should().NotBeNull());

        // …and Search opens the viewer's search input.
        cut.Find(".tm-mobile-action-bar__bar").QuerySelector("[data-action-id='search']")!.Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='pdf-search-input']").Should().NotBeNull());
    }

    [Fact]
    public void DesktopToolbar_IconsWithText_ShowsInlineLabels()
    {
        var cut = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")),
            p => p.Add(x => x.ToolbarLabels, ToolbarLabels.IconsWithText));

        // Review 5: on a desktop layout the labels sit INLINE (the viewer's rule), so the
        // merged toolbar's annotator pills show their text like the viewer's own buttons.
        var browse = cut.Find("[data-testid='pdf-annotator-mode-browse']");
        browse.QuerySelector(".tm-toolbar-btn-text").Should().NotBeNull("the label renders on desktop");
        browse.ClassList.Should().NotContain("tm-toolbar-btn--label-below");
        browse.ClassList.Should().NotContain("tm-toolbar-btn--icon-only");
        cut.Find(".tm-pdf-viewer__toolbar").TextContent.Should().Contain("Browse annotations");
    }

    [Fact]
    public void Tablet_ToolsRenderInTheirOwnFullWidthRow_NotInTheViewerToolbar()
    {
        var cut = RenderInLayout(TmLayoutMode.Tablet, Seeded(Highlight("h1")),
            p => p.Add(x => x.ToolbarLabels, ToolbarLabels.IconsWithText));

        // Review 7: the tools + export group are the annotator's own full-width labelled row
        // above the canvas — not squeezed into the viewer toolbar's More overflow.
        var row = cut.Find("[data-testid='pdf-annotator-toolbar']");
        row.QuerySelector("[data-testid='pdf-annotator-mode-browse']").Should().NotBeNull();
        row.QuerySelector("[data-testid='pdf-annotator-export']").Should().NotBeNull();

        var labelled = row.QuerySelectorAll("[data-testid='pdf-annotator-tools'] .tm-toolbar-btn--label-below")
            .ToList();
        labelled.Should().HaveCountGreaterThanOrEqualTo(5, "the five tools carry visible labels");
        labelled.Select(b => b.QuerySelector(".tm-toolbar-btn-text")?.TextContent.Trim())
            .Should().OnlyContain(t => !string.IsNullOrEmpty(t));

        cut.Find(".tm-pdf-viewer__toolbar").QuerySelector("[data-testid='pdf-annotator-mode-browse']")
            .Should().BeNull("the annotator injects nothing into the viewer toolbar on tablet");
    }

    [Fact]
    public void Tablet_ThumbnailsDefaultCollapsed_AndForwardedToTheHost()
    {
        bool? raised = null;
        var cut = RenderInLayout(TmLayoutMode.Tablet, configure: p => p
            .Add(x => x.ShowThumbnails, true)
            .Add(x => x.ThumbnailsCollapsedChanged, EventCallback.Factory.Create<bool>(this, v => raised = v)));

        // Review 8: the viewer's guarded tablet default reaches the annotator's two-way shadow,
        // so the next parameter pass carries true back instead of overriding the default.
        cut.WaitForAssertion(() =>
            cut.FindComponent<TmPdfViewer>().Instance.ThumbnailsCollapsed.Should().BeTrue());
        raised.Should().BeTrue();

        cut.Render();
        cut.FindComponent<TmPdfViewer>().Instance.ThumbnailsCollapsed.Should().BeTrue(
            "an unbound host re-render must not expand the tablet default");
    }

    [Fact]
    public void ShellCollapseChevron_MapsToPanelClosed_AndReopenToggleRestores()
    {
        bool? raised = null;
        var cut = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")), p => p
            .Add(x => x.ShowPanelChanged, EventCallback.Factory.Create<bool>(this, v => raised = v)));

        // Review 2: the shell's collapse chevron is wired two-way — collapsing the right panel
        // closes it and raises ShowPanelChanged(false).
        cut.Find(".tm-editor-shell__panel-toggle--right").Click();
        raised.Should().BeFalse();
        cut.FindAll("[data-region='right']").Should().BeEmpty();

        // The slim reopen toggle (the "rail" affordance) brings the panel back.
        cut.Find(".tm-editor-shell__panel-toggle--right").Click();
        raised.Should().BeTrue();
        cut.WaitForAssertion(() =>
            cut.Find("[data-region='right'] [data-testid='pdf-annotator-panel']").Should().NotBeNull());
    }
}
