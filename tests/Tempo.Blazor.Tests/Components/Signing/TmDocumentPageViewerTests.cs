using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

public class TmDocumentPageViewerTests : LocalizationTestBase
{
    [Fact]
    public void Render_WithoutPage_DisplaysEmptyState()
    {
        var cut = Render<TmDocumentPageViewer>();

        cut.Find(".tm-document-page-viewer").Should().NotBeNull();
        cut.Find(".tm-empty-state").TextContent.Should().Contain("No document page");
    }

    [Fact]
    public void Render_WithPage_DisplaysPageImageWithAlt()
    {
        var page = CreatePage(label: "Contract page");

        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, page));

        var image = cut.Find("img.tm-document-page-viewer__image");
        image.GetAttribute("src").Should().Be("/samples/page-1.png");
        image.GetAttribute("alt").Should().Be("Contract page");
    }

    [Fact]
    public void Render_WithPage_SetsAspectRatioFromPageSize()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage(width: 612, height: 792)));

        cut.Find(".tm-document-page-viewer__page")
            .GetAttribute("style")
            .Should()
            .Contain("aspect-ratio: 612 / 792");
    }

    [Fact]
    public void Render_WithScale_SetsPageScaleCssVariable()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.Scale, 1.25));

        cut.Find(".tm-document-page-viewer__page")
            .GetAttribute("style")
            .Should()
            .Contain("--tm-document-page-scale: 1.25");
    }

    [Fact]
    public void Render_WithInvalidScale_ClampsPageScale()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.Scale, 9)
                      .Add(p => p.MaxScale, 2));

        cut.Find(".tm-document-page-viewer__page")
            .GetAttribute("style")
            .Should()
            .Contain("--tm-document-page-scale: 2");
    }

    [Fact]
    public void Render_RootContainsClassAndAdditionalAttributes()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.Class, "custom-viewer")
                      .AddUnmatched("data-testid", "viewer"));

        var root = cut.Find("[data-testid='viewer']");
        root.ClassList.Should().Contain("tm-document-page-viewer");
        root.ClassList.Should().Contain("custom-viewer");
    }

    [Fact]
    public void Render_WhenLoading_DisplaysSkeletonState()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.IsLoading, true));

        cut.Find(".tm-document-page-viewer").GetAttribute("aria-busy").Should().Be("true");
        cut.Find(".tm-document-page-viewer__loading").Should().NotBeNull();
        cut.Find(".tm-skeleton").Should().NotBeNull();
    }

    [Fact]
    public void Render_WithError_DisplaysAlertState()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Error, "Could not render page."));

        cut.Find(".tm-alert").TextContent.Should().Contain("Could not render page.");
    }

    [Fact]
    public void Render_WithToolbar_DisplaysAccessibleZoomControls()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.ShowToolbar, true));

        cut.Find(".tm-document-page-viewer__toolbar").GetAttribute("role").Should().Be(
            "group",
            "lišta zoomu nemá roving tabindex");
        cut.Find(".tm-document-page-viewer__zoom-out").GetAttribute("aria-label").Should().Be("Zoom out");
        cut.Find(".tm-document-page-viewer__zoom-in").GetAttribute("aria-label").Should().Be("Zoom in");
        cut.Find(".tm-document-page-viewer__zoom-label").TextContent.Should().Contain("100%");
    }

    [Fact]
    public void Render_WithPaginationControls_DisplaysPageLabelAndButtons()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage(pageIndex: 1))
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowPaginationControls, true)
                      .Add(p => p.PageNumber, 2)
                      .Add(p => p.TotalPages, 4));

        cut.Find(".tm-document-page-viewer__page-label").TextContent.Should().Contain("2 / 4");
        cut.Find(".tm-document-page-viewer__previous-page").GetAttribute("aria-label").Should().Be("Previous page");
        cut.Find(".tm-document-page-viewer__next-page").GetAttribute("aria-label").Should().Be("Next page");
    }

    [Fact]
    public void ZoomIn_InvokesScaleChangedAndUsesCustomZoomMode()
    {
        double? scale = null;
        DocumentPageZoomMode? zoomMode = null;
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.Scale, 1.0)
                      .Add(p => p.ScaleChanged, value => scale = value)
                      .Add(p => p.ZoomModeChanged, value => zoomMode = value));

        cut.Find(".tm-document-page-viewer__zoom-in").Click();

        scale.Should().Be(1.25);
        zoomMode.Should().Be(DocumentPageZoomMode.Custom);
    }

    [Fact]
    public void FitPage_InvokesZoomModeChanged()
    {
        double? scale = null;
        DocumentPageZoomMode? zoomMode = null;
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ScaleChanged, value => scale = value)
                      .Add(p => p.ZoomModeChanged, value => zoomMode = value));

        cut.Find(".tm-document-page-viewer__fit-page").Click();

        scale.Should().Be(0.85);
        zoomMode.Should().Be(DocumentPageZoomMode.FitPage);
    }

    [Fact]
    public void Render_WithChildContent_RendersOverlayAbovePage()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .AddChildContent("<button class=\"overlay-child\">Sign</button>"));

        var overlay = cut.Find(".tm-document-page-viewer__overlay");
        overlay.QuerySelector(".overlay-child")!.TextContent.Should().Be("Sign");
    }

    [Fact]
    public void Render_WithOverlayTemplate_PassesPageContext()
    {
        var page = CreatePage(label: "Page A");

        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, page)
                      .Add(p => p.OverlayTemplate, context => builder =>
                      {
                          builder.OpenElement(0, "span");
                          builder.AddAttribute(1, "class", "context-label");
                          builder.AddContent(2, context.Label);
                          builder.CloseElement();
                      }));

        cut.Find(".context-label").TextContent.Should().Be("Page A");
    }

    [Fact]
    public void Render_WithReadOnlyOverlay_DisablesPointerEventsForOverlay()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage())
                      .Add(p => p.IsOverlayInteractive, false)
                      .AddChildContent("<button>Sign</button>"));

        cut.Find(".tm-document-page-viewer__overlay")
            .ClassList.Should()
            .Contain("tm-document-page-viewer__overlay--readonly");
    }

    [Fact]
    public void Render_WithPage_SetsPageIdAndDataPageIndex()
    {
        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, CreatePage(pageIndex: 2))
                      .Add(p => p.Id, "contract-page-3"));

        var page = cut.Find("#contract-page-3");
        page.GetAttribute("data-page-index").Should().Be("2");
    }

    [Fact]
    public void Click_Page_InvokesOnPageClick()
    {
        TmDocumentPageViewerPointerEventArgs? captured = null;
        var page = CreatePage(pageIndex: 4);

        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, page)
                      .Add(p => p.OnPageClick, EventCallback.Factory.Create<TmDocumentPageViewerPointerEventArgs>(this, args => captured = args)));

        cut.Find(".tm-document-page-viewer__page").Click();

        captured.Should().NotBeNull();
        captured!.Page.PageIndex.Should().Be(4);
    }

    [Fact]
    public void ContextMenu_Page_InvokesOnPageContextMenu()
    {
        TmDocumentPageViewerPointerEventArgs? captured = null;
        var page = CreatePage(pageIndex: 1);

        var cut = Render<TmDocumentPageViewer>(parameters =>
            parameters.Add(p => p.Page, page)
                      .Add(p => p.OnPageContextMenu, EventCallback.Factory.Create<TmDocumentPageViewerPointerEventArgs>(this, args => captured = args)));

        cut.Find(".tm-document-page-viewer__page").ContextMenu(new MouseEventArgs { ClientX = 120, ClientY = 240 });

        captured.Should().NotBeNull();
        captured!.Page.PageIndex.Should().Be(1);
        captured.MouseEventArgs.ClientX.Should().Be(120);
        captured.MouseEventArgs.ClientY.Should().Be(240);
    }

    private static SigningDocumentPage CreatePage(
        int pageIndex = 0,
        double width = 800,
        double height = 1000,
        string? label = null)
    {
        return new SigningDocumentPage
        {
            AttachmentUuid = "attachment-1",
            PageIndex = pageIndex,
            ImageUrl = "/samples/page-1.png",
            Width = width,
            Height = height,
            Label = label
        };
    }
}
