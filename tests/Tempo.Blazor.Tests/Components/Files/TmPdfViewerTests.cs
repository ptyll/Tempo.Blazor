using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Files;

public partial class TmPdfViewerTests : LocalizationTestBase
{
    public TmPdfViewerTests()
    {
        // Ensure PDF.js isAvailable returns true in tests
        JSInterop.Setup<bool>("tmPdfViewer.isAvailable").SetResult(true);
    }
    // ── PDF-9: render s Url zobrazí .tm-pdf-viewer kontejner ───────────────

    [Fact]
    public void Render_WithUrl_DisplaysViewerContainer()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf"));

        cut.Find(".tm-pdf-viewer").Should().NotBeNull();
    }

    // ── PDF-10: render zobrazí canvas element ──────────────────────────────

    [Fact]
    public void Render_WithUrl_DisplaysCanvas()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf"));

        cut.Find("canvas.tm-pdf-viewer__canvas").Should().NotBeNull();
    }

    // ── PDF-11: ShowToolbar=true zobrazí toolbar ───────────────────────────

    [Fact]
    public void Render_WithShowToolbarTrue_DisplaysToolbar()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true));

        cut.Find(".tm-pdf-viewer__toolbar").Should().NotBeNull();
    }

    // ── PDF-12: toolbar obsahuje page input po načtení ─────────────────────

    [Fact]
    public void Render_AfterLoad_ToolbarContainsPageLabel()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        var input = cut.Find(".tm-pdf-viewer__page-input");
        input.GetAttribute("value").Should().Be("1");
        cut.Find(".tm-pdf-viewer__page-count").TextContent.Should().Contain("10");
    }

    // ── PDF-13: Previous page disabled na první stránce ────────────────────

    [Fact]
    public void Render_OnFirstPage_PreviousButtonDisabled()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        var prevBtn = cut.FindAll(".tm-pdf-viewer__btn")
                         .First(b => b.GetAttribute("aria-label")?.Contains("Previous") == true);
        prevBtn.HasAttribute("disabled").Should().BeTrue();
    }

    // ── PDF-14: Next page disabled na poslední stránce ─────────────────────

    [Fact]
    public void Render_OnLastPage_NextButtonDisabled()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.Page, 10));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        var nextBtn = cut.FindAll(".tm-pdf-viewer__btn")
                         .First(b => b.GetAttribute("aria-label")?.Contains("Next") == true);
        nextBtn.HasAttribute("disabled").Should().BeTrue();
    }

    // ── PDF-15: klik na Next page inkrementuje Page ────────────────────────

    [Fact]
    public void ClickNextPage_IncrementsPage()
    {
        int capturedPage = 1;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.PageChanged, EventCallback.Factory.Create<int>(this, p => capturedPage = p)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var nextBtn = cut.FindAll(".tm-pdf-viewer__btn")
                         .First(b => b.GetAttribute("aria-label")?.Contains("Next") == true);
        nextBtn.Click();

        capturedPage.Should().Be(2);
    }

    // ── PDF-16: klik na Previous page dekrementuje Page ────────────────────

    [Fact]
    public void ClickPreviousPage_DecrementsPage()
    {
        int capturedPage = 2;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.Page, 2)
                      .Add(p => p.PageChanged, EventCallback.Factory.Create<int>(this, p => capturedPage = p)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var prevBtn = cut.FindAll(".tm-pdf-viewer__btn")
                         .First(b => b.GetAttribute("aria-label")?.Contains("Previous") == true);
        prevBtn.Click();

        capturedPage.Should().Be(1);
    }

    // ── PDF-17: zoom in změní Scale ────────────────────────────────────────

    [Fact]
    public void ClickZoomIn_IncreasesScale()
    {
        double capturedScale = 1.0;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ScaleChanged, EventCallback.Factory.Create<double>(this, s => capturedScale = s)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var zoomInBtn = cut.FindAll(".tm-pdf-viewer__btn")
                           .First(b => b.GetAttribute("aria-label")?.Contains("Zoom in") == true);
        zoomInBtn.Click();

        capturedScale.Should().Be(1.25);
    }

    // ── PDF-18: zoom out změní Scale ───────────────────────────────────────

    [Fact]
    public void ClickZoomOut_DecreasesScale()
    {
        double capturedScale = 1.0;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.Scale, 1.25)
                      .Add(p => p.ScaleChanged, EventCallback.Factory.Create<double>(this, s => capturedScale = s)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var zoomOutBtn = cut.FindAll(".tm-pdf-viewer__btn")
                            .First(b => b.GetAttribute("aria-label")?.Contains("Zoom out") == true);
        zoomOutBtn.Click();

        capturedScale.Should().Be(1.0);
    }

    // ── PDF-19: zoom trigger zobrazí procenta ──────────────────────────────

    [Fact]
    public void Render_ZoomLabelShowsPercentage()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.Scale, 1.5));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var trigger = cut.Find(".tm-pdf-viewer__zoom .tm-dropdown-trigger");
        trigger.TextContent.Should().Contain("150%");
    }

    // ── PDF-20: AllowDownload=true zobrazí download link ───────────────────

    [Fact]
    public void Render_WithAllowDownloadTrue_DisplaysDownloadLink()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.AllowDownload, true));

        var link = cut.Find("a[download]");
        link.GetAttribute("href").Should().Be("https://example.com/test.pdf");
    }

    // ── PDF-21: Open link má target="_blank" ───────────────────────────────

    [Fact]
    public void Render_OpenLinkHasTargetBlank()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf"));

        var link = cut.Find("a[target='_blank']");
        link.Should().NotBeNull();
    }

    // ── PDF-22: loading spinner se zobrazí při inicializaci ────────────────

    [Fact]
    public void Render_InitialState_HasCanvasWrap()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf"));

        // Canvas wrap should always be present before fallback
        cut.Find(".tm-pdf-viewer__canvas-wrap").Should().NotBeNull();
    }

    // ── PDF-23: fallback embed se zobrazí když UseFallback=true ────────────

    [Fact]
    public void Render_WhenUseFallbackTrue_DisplaysEmbed()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf"));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoadError("test error"));
        cut.Render();

        cut.Find("embed.tm-pdf-viewer__embed").Should().NotBeNull();
    }

    // ── PDF-24: při chybě se přepne do fallback módu ─────────────────────

    [Fact]
    public void Render_OnLoadError_SwitchesToFallback()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf"));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoadError("test error"));
        cut.Render();

        cut.Find("embed.tm-pdf-viewer__embed").Should().NotBeNull();
    }

    // ── PDF-25: Class se aplikuje na root element ──────────────────────────

    [Fact]
    public void Render_WithClass_AppliesToRoot()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.Class, "my-pdf"));

        var root = cut.Find(".tm-pdf-viewer");
        root.ClassList.Should().Contain("my-pdf");
    }

    // ── PDF-26: bez Url zobrazí empty state ────────────────────────────────

    [Fact]
    public void Render_WithoutUrl_DisplaysEmptyState()
    {
        var cut = Render<TmPdfViewer>();

        cut.Find(".tm-pdf-viewer__empty").Should().NotBeNull();
    }

    // ── Additional: ShowToolbar=false skryje toolbar ───────────────────────

    [Fact]
    public void Render_WithShowToolbarFalse_HidesToolbar()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, false));

        cut.FindAll(".tm-pdf-viewer__toolbar").Should().BeEmpty();
    }

    // ── Additional: AllowDownload=false skryje download ────────────────────

    [Fact]
    public void Render_WithAllowDownloadFalse_HidesDownloadLink()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.AllowDownload, false));

        cut.FindAll("a[download]").Should().BeEmpty();
    }

    // ── Additional: Height parameter applies style ─────────────────────────

    [Fact]
    public void Render_WithHeight_AppliesStyle()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.Height, "400px"));

        var root = cut.Find(".tm-pdf-viewer");
        root.GetAttribute("style").Should().Contain("height:400px");
    }

    // ── Additional: Scale binding changes zoom trigger ─────────────────────

    [Fact]
    public void Render_ScaleBinding_UpdatesZoomLabel()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.Scale, 0.75));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var trigger = cut.Find(".tm-pdf-viewer__zoom .tm-dropdown-trigger");
        trigger.TextContent.Should().Contain("75%");
    }

    // ── Additional: Page binding changes page input ────────────────────────

    [Fact]
    public void Render_PageBinding_UpdatesPageLabel()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.Page, 3));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        cut.Find(".tm-pdf-viewer__page-input").GetAttribute("value").Should().Be("3");
    }

    // ── Additional: ShowTextLayer renders text layer div ───────────────────

    [Fact]
    public void Render_WithShowTextLayer_DisplaysTextLayer()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowTextLayer, true));

        cut.Find(".tm-pdf-viewer__text-layer").Should().NotBeNull();
    }

    // ── Additional: AllowRotation renders rotation button ──────────────────

    [Fact]
    public void Render_WithAllowRotation_DisplaysRotateButton()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.AllowRotation, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var rotateBtn = cut.FindAll(".tm-pdf-viewer__btn")
                           .FirstOrDefault(b => b.GetAttribute("aria-label")?.Contains("Rotate") == true);
        rotateBtn.Should().NotBeNull();
    }

    // ── Additional: GoToPage public API works ──────────────────────────────

    [Fact]
    public async Task GoToPage_UpdatesCurrentPage()
    {
        int capturedPage = 1;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.PageChanged, EventCallback.Factory.Create<int>(this, p => capturedPage = p)));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        await cut.Instance.GoToPageAsync(5);

        capturedPage.Should().Be(5);
    }

    // ── Additional: SetScale public API works ──────────────────────────────

    [Fact]
    public async Task SetScale_UpdatesScale()
    {
        double capturedScale = 1.0;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ScaleChanged, EventCallback.Factory.Create<double>(this, s => capturedScale = s)));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        await cut.Instance.SetScaleAsync(1.5);

        capturedScale.Should().Be(1.5);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Phase 3 — Advanced features
    // ══════════════════════════════════════════════════════════════════════════

    // ── PDF-45: ShowThumbnails=true zobrazí sidebar s miniatury ─────────────

    [Fact]
    public void Render_WithShowThumbnailsTrue_DisplaysThumbnailsSidebar()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowThumbnails, true));

        cut.Find(".tm-pdf-viewer__thumbnails").Should().NotBeNull();
    }

    // ── PDF-46: ShowThumbnails=false skryje sidebar ─────────────────────────

    [Fact]
    public void Render_WithShowThumbnailsFalse_HidesThumbnailsSidebar()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowThumbnails, false));

        cut.FindAll(".tm-pdf-viewer__thumbnails").Should().BeEmpty();
    }

    // ── PDF-47: ShowSearch=true zobrazí search bar ──────────────────────────

    [Fact]
    public void Render_WithShowSearchTrue_DisplaysSearchBar()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowSearch, true));

        cut.Find(".tm-pdf-viewer__search-bar").Should().NotBeNull();
    }

    // ── PDF-48: ShowSearch=false skryje search bar ──────────────────────────

    [Fact]
    public void Render_WithShowSearchFalse_HidesSearchBar()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowSearch, false));

        cut.FindAll(".tm-pdf-viewer__search-bar").Should().BeEmpty();
    }


    // ── PDF-52: RotateAsync cykluje 0→90→180→270→0 ─────────────────────────

    [Fact]
    public async Task RotateAsync_CyclesRotation()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.AllowRotation, true));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));

        await cut.Instance.RotateAsync();
        cut.Instance.Rotation.Should().Be(90);

        await cut.Instance.RotateAsync();
        cut.Instance.Rotation.Should().Be(180);

        await cut.Instance.RotateAsync();
        cut.Instance.Rotation.Should().Be(270);

        await cut.Instance.RotateAsync();
        cut.Instance.Rotation.Should().Be(0);
    }

    // ── PDF-53: search input existuje v search baru ─────────────────────────

    [Fact]
    public void Render_WithShowSearchTrue_HasSearchInput()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowSearch, true));

        cut.Find(".tm-pdf-viewer__search-input").Should().NotBeNull();
    }

    // ── PDF-54: thumbnails sidebar má ref element ──────────────────────────

    [Fact]
    public void Render_WithShowThumbnailsTrue_HasThumbnailsElement()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowThumbnails, true));

        cut.Find(".tm-pdf-viewer__thumbnails").Should().NotBeNull();
    }

    // ── PDF-55: toolbar obsahuje search toggle když ShowSearch=true ─────────

    [Fact]
    public void Render_WithShowToolbarAndSearch_HasSearchToggleButton()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowSearch, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var searchBtn = cut.FindAll(".tm-pdf-viewer__btn")
                           .FirstOrDefault(b => b.GetAttribute("aria-label")?.Contains("Search") == true);
        searchBtn.Should().NotBeNull();
    }


    // ── PDF-57: search toggle klik skryje/zobrazí search bar ────────────────

    [Fact]
    public void ClickSearchToggle_TogglesSearchBarVisibility()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowSearch, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        // Search bar visible by default when ShowSearch=true
        cut.FindAll(".tm-pdf-viewer__search-bar").Should().ContainSingle();

        var searchBtn = cut.FindAll(".tm-pdf-viewer__btn")
                           .First(b => b.GetAttribute("aria-label")?.Contains("Search") == true);
        searchBtn.Click();
        cut.Render();

        // After toggle click, search bar should be hidden
        cut.FindAll(".tm-pdf-viewer__search-bar").Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Wave 0 — bugs and a11y independent of the redesign
    // ══════════════════════════════════════════════════════════════════════════

    // ── W0: loading stav se renderuje s role="status" ───────────────────────

    [Fact]
    public void Render_WhileLoading_ShowsLoadingStatus()
    {
        // Hold the JS init promise open so the component stays in its loading state.
        JSInterop.SetupVoid("tmPdfViewer.init", _ => true);

        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf"));

        cut.WaitForAssertion(() =>
        {
            var status = cut.Find(".tm-pdf-viewer__loading");
            status.GetAttribute("role").Should().Be("status");
            status.TextContent.Should().Contain("Loading");
            cut.Find(".tm-pdf-viewer__body").GetAttribute("aria-busy").Should().Be("true");
        });
    }

    [Fact]
    public void Render_AfterLoad_HidesLoadingStatus()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf"));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        cut.FindAll(".tm-pdf-viewer__loading").Should().BeEmpty();
        cut.Find(".tm-pdf-viewer__body").GetAttribute("aria-busy").Should().Be("false");
    }

    // ── W0: změna Url během načítání nenechá staré načtení vypnout loading ──

    [Fact]
    public async Task Render_UrlChangedMidLoad_OlderLoadDoesNotEndLoadingState()
    {
        const string firstUrl = "https://example.com/first.pdf";
        const string secondUrl = "https://example.com/second.pdf";

        // Each load generation gets its own pending handler, matched by the Url argument,
        // so the test controls which load finishes and when.
        var firstInit = JSInterop.SetupVoid("tmPdfViewer.init",
            inv => inv.Arguments.Count > 1 && Equals(inv.Arguments[1], firstUrl));
        var secondInit = JSInterop.SetupVoid("tmPdfViewer.init",
            inv => inv.Arguments.Count > 1 && Equals(inv.Arguments[1], secondUrl));

        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, firstUrl));

        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.init"].Should().HaveCount(1));

        // Change the Url while the first download is still in flight: a new load
        // generation starts and init is invoked a second time, still pending.
        cut.Render(parameters => parameters.Add(p => p.Url, secondUrl));

        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.init"].Should().HaveCount(2));

        // Let the STALE load finish while the newer one is still downloading, then drain
        // the dispatcher so the stale finally has definitely run.
        firstInit.SetVoidResult();
        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-pdf-viewer__loading").Should().ContainSingle(
            "the loading state belongs to the newer generation and must survive the stale load");
        cut.Find(".tm-pdf-viewer__body").GetAttribute("aria-busy").Should().Be("true");

        // The newer generation finishing is what ends the loading state.
        secondInit.SetVoidResult();
        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-pdf-viewer__loading").Should().BeEmpty();
        cut.Find(".tm-pdf-viewer__body").GetAttribute("aria-busy").Should().Be("false");
    }

    // ── W0: chyba načtení se renderuje jako TmAlert nad fallback <embed> ───

    [Fact]
    public void Render_OnLoadError_ShowsErrorAlertAboveFallback()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf"));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoadError("test error"));
        cut.Render();

        cut.Find(".tm-alert").TextContent.Should().Contain("Could not load the PDF.");
        // The alert and the embed must both live inside the flex fallback wrapper,
        // with the alert rendered first (above the embed).
        var fallback = cut.Find(".tm-pdf-viewer__fallback");
        var markup = fallback.InnerHtml;
        markup.IndexOf("tm-alert", StringComparison.Ordinal)
            .Should().BeGreaterThan(-1, "the error alert renders inside the fallback wrapper");
        markup.IndexOf("tm-alert", StringComparison.Ordinal)
            .Should().BeLessThan(markup.IndexOf("<embed", StringComparison.Ordinal),
                "the error alert must render above the fallback embed");
        cut.Find(".tm-pdf-viewer__fallback .tm-pdf-viewer__embed").Should().NotBeNull();
    }


    // ── W0: ikonová tlačítka mají title (tooltip) ───────────────────────────

    [Fact]
    public void Render_IconButtons_HaveTitleOrTooltip()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.AllowRotation, true)
                      .Add(p => p.ShowViewModeToggle, true)
                      .Add(p => p.ShowSearch, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var buttons = cut.FindAll(".tm-pdf-viewer__btn");
        buttons.Should().NotBeEmpty();
        foreach (var button in buttons)
        {
            button.GetAttribute("title")
                .Should().NotBeNullOrEmpty($"every icon button needs a tooltip title, found: {button.GetAttribute("aria-label")}");
        }
    }

    // ── W0: miniatury mají aria-label regionu (TmPdfViewer_Thumbnails) ─────

    [Fact]
    public void Thumbnails_HaveRegionLabel()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowThumbnails, true));

        var thumbnails = cut.Find(".tm-pdf-viewer__thumbnails");
        thumbnails.GetAttribute("aria-label").Should().Be("Thumbnails");
    }

    // ── W0: výběr vlákna na jiné stránce naviguje na tu stránku ────────────

    [Fact]
    public async Task Viewer_SelectThreadOnOtherPage_NavigatesToPage()
    {
        var provider = new InMemoryPdfAnnotationProvider(new Dictionary<string, IReadOnlyList<DocumentCommentThread>>
        {
            ["doc"] = new List<DocumentCommentThread>
            {
                TextRangeThread("t1", page: 1),
                TextRangeThread("t3", page: 3),
            }
        });
        int capturedPage = 1;
        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.DocumentId, "doc")
            .Add(x => x.EnableAnnotations, true)
            .Add(x => x.AnnotationProvider, provider)
            .Add(x => x.PageChanged, EventCallback.Factory.Create<int>(this, page => capturedPage = page)));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        cut.FindAll("[data-testid='pdf-annotation-thread']")
            .First(b => b.GetAttribute("data-thread-id") == "t3")
            .Click();

        capturedPage.Should().Be(3, "selecting a thread anchored on another page must navigate to that page");
    }

    // ── W0: dispose volá JS destroy pro instanci ────────────────────────────

    [Fact]
    public async Task Dispose_InvokesJsDestroy_ForInstance()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf"));

        await cut.Instance.DisposeAsync();

        // destroy must receive the same ElementReference init received, so the
        // per-instance JS state (keyed by the root element) is the one destroyed.
        var init = JSInterop.Invocations["tmPdfViewer.init"].Single();
        var destroy = JSInterop.VerifyInvoke("tmPdfViewer.destroy", 1).Single();

        var initArg = Assert.IsType<ElementReference>(init.Arguments[0]);
        var destroyArg = Assert.IsType<ElementReference>(destroy.Arguments[0]);
        destroyArg.Id.Should().NotBeNullOrEmpty();
        destroyArg.Id.Should().Be(initArg.Id,
            "destroy must target the exact root element init was called with");
    }

    private static DocumentCommentThread TextRangeThread(string id, int page)
        => new()
        {
            Id = id,
            Anchor = DocumentCommentAnchor.TextRange(page, [DocumentCommentRect.Create(0.1, 0.2, 0.3, 0.02)], "quoted"),
            Comments = [new DocumentComment { Id = id + "-c", AuthorId = "u1", AuthorName = "U1", Body = "note" }]
        };

    // ── N1: additive extension points used by TmPdfAnnotator ────────────────

    [Fact]
    public void OverlayContent_RendersInsideCanvasWrap()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.OverlayContent, builder =>
                      {
                          builder.OpenElement(0, "div");
                          builder.AddAttribute(1, "data-testid", "custom-overlay-content");
                          builder.CloseElement();
                      }));

        cut.Find(".tm-pdf-viewer__canvas-wrap [data-testid='custom-overlay-content']").Should().NotBeNull();
    }

    [Fact]
    public void OnDocumentLoaded_FiresWithTotalPages()
    {
        var total = 0;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.OnDocumentLoaded, EventCallback.Factory.Create<int>(this, t => total = t)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(7));

        total.Should().Be(7);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Phase FZE-3 — toolbar, header, thumbnails (desktop reference)
    // ══════════════════════════════════════════════════════════════════════════

    // ── Page input: Enter navigates ─────────────────────────────────────────

    [Fact]
    public void PageInput_Enter_NavigatesToPage()
    {
        int capturedPage = 1;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.PageChanged, EventCallback.Factory.Create<int>(this, p => capturedPage = p)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        var input = cut.Find(".tm-pdf-viewer__page-input");
        input.Input("5");
        input.KeyDown("Enter");

        capturedPage.Should().Be(5);
    }

    // ── Page input: out-of-range value reverts to the current page ──────────

    [Fact]
    public void PageInput_OutOfRange_RevertsToCurrentPage()
    {
        int capturedPage = 0;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.Page, 2)
                      .Add(p => p.PageChanged, EventCallback.Factory.Create<int>(this, p => capturedPage = p)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        var input = cut.Find(".tm-pdf-viewer__page-input");
        input.Input("99");
        input.KeyDown("Enter");
        cut.Render();

        capturedPage.Should().Be(0, "no navigation may be raised for an out-of-range page");
        input.GetAttribute("value").Should().Be("2", "an invalid entry must revert to the current page");
    }



    // ── Comments toggle shows the open-thread count ─────────────────────────

    [Fact]
    public void CommentsToggle_ShowsOpenThreadCount()
    {
        var provider = new InMemoryPdfAnnotationProvider(new Dictionary<string, IReadOnlyList<DocumentCommentThread>>
        {
            ["doc"] = new List<DocumentCommentThread>
            {
                TextRangeThread("t-open-1", page: 1),
                TextRangeThread("t-open-2", page: 2),
                ResolvedThread("t-resolved", page: 3),
            }
        });
        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.ShowToolbar, true)
            .Add(x => x.DocumentId, "doc")
            .Add(x => x.EnableAnnotations, true)
            .Add(x => x.AnnotationProvider, provider));

        // FZE-9: the count renders as a natural plural (Loc.Plural family), not "Comments (N)".
        cut.WaitForAssertion(() =>
            cut.Find(".tm-pdf-viewer__comments-toggle").TextContent.Should().Contain("2 comments"));
    }

    // ── ShowHeader=false (default) renders no header markup ─────────────────

    [Fact]
    public void Header_ShowHeaderFalse_IsMarkupIdenticalToToday()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true));

        cut.FindAll(".tm-pdf-viewer__header").Should().BeEmpty();
    }

    // ── Header renders title/subtitle/actions and takes Open + Download ─────

    [Fact]
    public void Header_WithTitle_RendersTitleSubtitleAndActions_AndMovesOpenDownload()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowHeader, true)
                      .Add(p => p.Title, "Quarterly report")
                      .Add(p => p.Subtitle, "Prepared by finance"));

        var header = cut.Find(".tm-pdf-viewer__header");
        header.TextContent.Should().Contain("Quarterly report");
        header.TextContent.Should().Contain("Prepared by finance");
        header.QuerySelector("a[download]").Should().NotBeNull("Download moves into the header");
        header.QuerySelector("a[target='_blank']").Should().NotBeNull("Open moves into the header");

        // The toolbar keeps no file links once the header owns them.
        cut.Find(".tm-pdf-viewer__toolbar").QuerySelectorAll("a[download]").Should().BeEmpty();
        cut.Find(".tm-pdf-viewer__toolbar").QuerySelectorAll("a[target='_blank']").Should().BeEmpty();
    }

    // ── Header subtitle defaults to a neutral page count ────────────────────

    [Fact]
    public void Header_DefaultSubtitle_ShowsNeutralPageCount()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowHeader, true)
                      .Add(p => p.Title, "Quarterly report"));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(4));
        cut.Render();

        cut.Find(".tm-pdf-viewer__header").TextContent.Should().Contain("Pages: 4");
    }


    // ── ToolbarStart / ToolbarEnd slots render inside the toolbar ───────────

    [Fact]
    public void ToolbarStartEndSlots_RenderInsideToolbar()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ToolbarStart, builder =>
                          builder.AddMarkupContent(0, "<span data-testid='slot-start'>S</span>"))
                      .Add(p => p.ToolbarEnd, builder =>
                          builder.AddMarkupContent(0, "<span data-testid='slot-end'>E</span>")));

        var toolbar = cut.Find(".tm-pdf-viewer__toolbar");
        toolbar.QuerySelector("[data-testid='slot-start']").Should().NotBeNull();
        toolbar.QuerySelector("[data-testid='slot-end']").Should().NotBeNull();
    }

    // ── AllowDownload=false hides Download, Open stays ──────────────────────

    [Fact]
    public void AllowDownloadFalse_HidesDownload_OpenStays()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.AllowDownload, false));

        cut.FindAll("a[download]").Should().BeEmpty();
        cut.Find("a[target='_blank']").Should().NotBeNull();
    }

    // ── ShowOpenLink=false hides Open ───────────────────────────────────────

    [Fact]
    public void ShowOpenLinkFalse_HidesOpen()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowOpenLink, false));

        cut.FindAll("a[target='_blank']").Should().BeEmpty();
        cut.Find("a[download]").Should().NotBeNull("Download is unaffected by ShowOpenLink");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Phase FZE-6 — full continuous mode (lazy render, scroll navigation, PD1)
    // ══════════════════════════════════════════════════════════════════════════

    private static ComponentParameterCollectionBuilder<TmPdfViewer> ContinuousDefaults(
        ComponentParameterCollectionBuilder<TmPdfViewer> parameters)
        => parameters.Add(p => p.Url, "https://example.com/test.pdf")
                     .Add(p => p.ShowToolbar, true)
                     .Add(p => p.ViewMode, PdfViewMode.Continuous);

    private static IReadOnlyList<JSRuntimeInvocation> InvocationsOf(BunitContext ctx, string identifier)
        => ctx.JSInterop.Invocations.Where(i => i.Identifier == identifier).ToList();



}
