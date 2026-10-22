using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// bUnit tests for TmRedactionLayer: rectangle drawing over an image (pointer events
/// with a mocked measure), the area panel with PII categories, before/after preview,
/// provider persistence, and the destructive export interop calls for PDF and image
/// modes. The "content really removed" contract is asserted end-to-end in Playwright
/// (text extraction + byte scan of the exported PDF) — bUnit verifies the payloads.
/// </summary>
public class TmRedactionLayerTests : LocalizationTestBase
{
    public TmRedactionLayerTests()
    {
        JSInterop.Setup<double[]>("tmRedaction.measure", _ => true).SetResult([1000, 800]);
    }

    private IRenderedComponent<TmRedactionLayer> RenderImageMode(
        Action<Bunit.ComponentParameterCollectionBuilder<TmRedactionLayer>>? configure = null)
        => Render<TmRedactionLayer>(p =>
        {
            p.Add(x => x.ImageUrl, "/img/id-card.png");
            p.Add(x => x.DocumentId, "img-1");
            configure?.Invoke(p);
        });

    private static async Task DrawRectAsync(
        IRenderedComponent<TmRedactionLayer> cut, double x1, double y1, double x2, double y2)
    {
        var surface = cut.Find("[data-testid='redaction-surface']");
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = x1, OffsetY = y1 });
        await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = x2, OffsetY = y2 });
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = x2, OffsetY = y2 });
    }

    // ── FZE-14: the PD1 lift must not reach redaction ────────────────────────

    [Fact]
    public void Redaction_StaysSinglePage_AfterPd1Lift()
    {
        // The annotator gained per-page continuous tools (FZE-14), but redaction keeps its
        // viewer pinned: the areas are measured against THE canvas and the stage offers no
        // view-mode toggle.
        var cut = Render<TmRedactionLayer>(p =>
        {
            p.Add(x => x.Url, "/sample.pdf");
            p.Add(x => x.DocumentId, "doc-1");
        });

        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        viewer.ViewMode.Should().Be(PdfViewMode.SinglePage, "redaction stays a single-page stage");
        viewer.ShowViewModeToggle.Should().BeFalse("the redaction stage never offers the continuous toggle");
        cut.FindAll(".tm-pdf-viewer__view-mode").Should().BeEmpty();
    }

    // ── Drawing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DrawingARectangle_AddsANormalizedArea()
    {
        IReadOnlyList<RedactionArea>? changed = null;
        var cut = RenderImageMode(p => p.Add(x => x.OnAreasChanged, (IReadOnlyList<RedactionArea> a) => changed = a));

        await DrawRectAsync(cut, 100, 80, 300, 240);

        cut.WaitForAssertion(() =>
        {
            var rect = cut.Find("[data-testid='redaction-rect']");
            changed.Should().NotBeNull();
        });
        var area = changed![0];
        area.PageNumber.Should().Be(1);
        area.X.Should().BeApproximately(0.1, 0.001);
        area.Y.Should().BeApproximately(0.1, 0.001);
        area.Width.Should().BeApproximately(0.2, 0.001);
        area.Height.Should().BeApproximately(0.2, 0.001);
    }

    [Fact]
    public async Task TinyDrag_IsDiscarded()
    {
        var cut = RenderImageMode();

        await DrawRectAsync(cut, 100, 80, 102, 82);

        cut.FindAll("[data-testid='redaction-rect']").Should().BeEmpty();
    }

    [Fact]
    public async Task ReverseDrag_NormalizesToPositiveRect()
    {
        var cut = RenderImageMode();

        await DrawRectAsync(cut, 300, 240, 100, 80);

        cut.WaitForAssertion(() =>
        {
            var style = cut.Find("[data-testid='redaction-rect']").GetAttribute("style")!;
            style.Should().Contain("left:10");
            style.Should().Contain("top:10");
        });
    }

    // ── Panel: categories, removal ───────────────────────────────────────────

    [Fact]
    public async Task Panel_ListsAreas_WithCategorySelect_AndDefaultCategory()
    {
        var cut = RenderImageMode(p => p.Add(x => x.DefaultCategory, RedactionCategory.PersonalId));

        await DrawRectAsync(cut, 100, 80, 300, 240);

        var select = cut.WaitForElement("[data-testid='redaction-category']");
        select.GetAttribute("value").Should().Be(nameof(RedactionCategory.PersonalId));

        select.Change(nameof(RedactionCategory.BankAccount));
        cut.Find("[data-testid='redaction-item']").TextContent.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RemovingAnArea_DeletesItsRectangle()
    {
        var cut = RenderImageMode();
        await DrawRectAsync(cut, 100, 80, 300, 240);
        cut.WaitForElement("[data-testid='redaction-rect']");

        cut.Find("[data-testid='redaction-remove']").Click();

        cut.FindAll("[data-testid='redaction-rect']").Should().BeEmpty();
        cut.Find("[data-testid='redaction-empty']");
    }

    // ── Preview before/after ─────────────────────────────────────────────────

    [Fact]
    public async Task PreviewToggle_SwitchesRectsBetweenMarkedAndApplied()
    {
        var cut = RenderImageMode();
        await DrawRectAsync(cut, 100, 80, 300, 240);

        var rect = cut.WaitForElement("[data-testid='redaction-rect']");
        rect.ClassList.Should().Contain("tm-redaction__rect--marked");

        cut.Find("[data-testid='redaction-preview-toggle']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='redaction-rect']").ClassList
                .Should().Contain("tm-redaction__rect--applied"));
    }

    // ── Provider persistence ─────────────────────────────────────────────────

    [Fact]
    public void ProviderAreas_LoadOnInit()
    {
        var provider = new InMemoryRedactionProvider();
        provider.SaveAsync("img-1",
            [new RedactionArea { PageNumber = 1, X = 0.2, Y = 0.3, Width = 0.4, Height = 0.1 }])
            .GetAwaiter().GetResult();

        var cut = RenderImageMode(p => p.Add(x => x.Provider, provider));

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='redaction-rect']").Should().HaveCount(1));
    }

    [Fact]
    public async Task SaveButton_PersistsThroughTheProvider()
    {
        var provider = new InMemoryRedactionProvider();
        var cut = RenderImageMode(p => p.Add(x => x.Provider, provider));
        await DrawRectAsync(cut, 100, 80, 300, 240);

        cut.WaitForElement("[data-testid='redaction-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var saved = provider.LoadAsync("img-1").GetAwaiter().GetResult();
            saved.Should().ContainSingle();
            saved[0].Width.Should().BeApproximately(0.2, 0.001);
        });
    }

    // ── Destructive export interop ───────────────────────────────────────────

    [Fact]
    public async Task ExportImage_InvokesJsWithClampedPayload_AndRaisesOnExported()
    {
        string? exported = null;
        var cut = RenderImageMode(p =>
        {
            p.Add(x => x.ExportFileName, "redacted-id.png");
            p.Add(x => x.OnExported, (string name) => exported = name);
        });
        await DrawRectAsync(cut, 100, 80, 300, 240);

        cut.WaitForElement("[data-testid='redaction-export']").Click();

        cut.WaitForAssertion(() =>
        {
            var invocation = JSInterop.Invocations
                .LastOrDefault(i => i.Identifier == "tmRedaction.exportRedactedImage");
            invocation.Arguments.Should().NotBeNull();
            invocation.Arguments[0].Should().Be("/img/id-card.png");
            ((string)invocation.Arguments[1]!).Should().Contain("\"x\":0.1");
            invocation.Arguments[2].Should().Be("redacted-id.png");
            exported.Should().Be("redacted-id.png");
        });
    }

    [Fact]
    public void PdfMode_RendersViewer_AndExportUsesThePdfPipeline()
    {
        var cut = Render<TmRedactionLayer>(p =>
        {
            p.Add(x => x.Url, "/docs/contract.pdf");
            p.Add(x => x.DocumentId, "doc-1");
            p.Add(x => x.Provider, Preloaded("doc-1"));
        });

        cut.WaitForElement("[data-testid='redaction-export']").Click();

        cut.WaitForAssertion(() =>
        {
            var invocation = JSInterop.Invocations
                .LastOrDefault(i => i.Identifier == "tmRedaction.exportRedactedPdf");
            invocation.Arguments.Should().NotBeNull();
            invocation.Arguments[0].Should().Be("/docs/contract.pdf");
            ((string)invocation.Arguments[1]!).Should().Contain("\"pageNumber\":1");
        });
    }

    [Fact]
    public void ExportWithoutAreas_IsDisabled()
    {
        var cut = RenderImageMode();

        cut.Find("[data-testid='redaction-export']").HasAttribute("disabled").Should().BeTrue();
    }

    private static InMemoryRedactionProvider Preloaded(string documentId)
    {
        var provider = new InMemoryRedactionProvider();
        provider.SaveAsync(documentId,
            [new RedactionArea { PageNumber = 1, X = 0.1, Y = 0.1, Width = 0.3, Height = 0.05 }])
            .GetAwaiter().GetResult();
        return provider;
    }
}
