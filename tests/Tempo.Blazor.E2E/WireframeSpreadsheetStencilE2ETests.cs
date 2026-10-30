using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The <c>TmSpreadsheet</c> wireframe stencil renders the CONTAINER-driven chrome of the redesign
/// (phase 6 of the spreadsheet plan): desktop keeps the tabbed ribbon with its labelled groups, tablet
/// draws two compact command rows without a tab strip, and the phone gets the compact header, ONE command
/// row and the core mobile action bar instead of the status bar. The <c>layoutMode</c> prop
/// (<c>auto|desktop|tablet|mobile</c>, the additive prop shared with the diagram/dashboard stencils)
/// selects the variant; <c>auto</c> and any unknown value render the DESKTOP variant, because a stencil
/// communicates the chosen presentation instead of re-flowing the canvas (decision X11).
/// <para>
/// The three captures land in <c>__screenshots__/wireframe/spreadsheet-stencil/</c> and are the artifact
/// the UX review reads against the spreadsheet references — the manual remaining item carried out of
/// phase 7d. They render through <see cref="HtmlRenderer"/> (no demo host, deterministic markup), the
/// pattern the other stencil galleries use.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Wireframe")]
public class WireframeSpreadsheetStencilE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task SpreadsheetStencil_DesktopTabletMobile_RenderTheirOwnChrome()
    {
        var desktop = await RenderAsync("desktop", width: 900, height: 520);
        var tablet = await RenderAsync("tablet", width: 720, height: 470);
        var mobile = await RenderAsync("mobile", width: 340, height: 520);

        // Desktop: the full ribbon — a tab strip plus labelled group bands.
        desktop.Should().Contain("data-layout='desktop'");
        desktop.Should().Contain("data-region='ribbon-tabs'", "the desktop chrome is the tabbed ribbon");
        desktop.Should().Contain("data-region='ribbon-groups'");
        desktop.Should().Contain("Clipboard").And.Contain("Alignment");
        desktop.Should().NotContain("data-region='mobile-header'");

        // Tablet: two compact command rows, no tab strip.
        tablet.Should().Contain("data-layout='tablet'");
        tablet.Should().Contain("data-region='command-row' data-row-count='2'",
            "the tablet chrome is TWO compact rows on the real component");
        tablet.Should().NotContain("data-region='ribbon-tabs'", "there is no tab strip below the desktop breakpoint");

        // Mobile: the compact header, ONE command row and the action bar instead of the status bar.
        mobile.Should().Contain("data-layout='mobile'");
        mobile.Should().Contain("data-region='mobile-header'",
            "a phone has no room for the document header: the compact header replaces it");
        mobile.Should().Contain("data-region='command-row' data-row-count='1'");
        mobile.Should().NotContain("data-region='ribbon-tabs'");
        mobile.Should().NotContain("data-region='status-bar'",
            "the phone chrome has no status bar (its zoom + aggregations move into the action bar)");

        // `auto` — and any unknown value — is the desktop variant (X11: a stencil never re-flows itself).
        var auto = await RenderAsync(null, width: 900, height: 520);
        auto.Should().Contain("data-layout='desktop'");
        auto.Should().Contain("data-region='ribbon-tabs'");

        // The three captures the UX review reads.
        foreach (var (layout, markup) in new[] { ("desktop", desktop), ("tablet", tablet), ("mobile", mobile) })
        {
            var path = await CaptureAsync(layout, markup);
            File.Exists(path).Should().BeTrue($"{layout} capture lands on disk");
        }
    }

    [TestMethod]
    public async Task SpreadsheetStencil_ShowHeader_DrawsTheOptInDocumentHeader()
    {
        var withHeader = await RenderAsync("desktop", width: 900, height: 520, showHeader: true);
        var withoutHeader = await RenderAsync("desktop", width: 900, height: 520, showHeader: false);

        // The opt-in document header mirrors TmSpreadsheet.ShowHeader: title + the file actions + the grid
        // toggle. Without it the ribbon is the top row, exactly like the component.
        withHeader.Should().Contain("data-region='header'");
        withHeader.Should().Contain("Show grid lines");
        withoutHeader.Should().NotContain("data-region='header'");
    }

    private static async Task<string> RenderAsync(
        string? layoutMode, double width, double height, bool showHeader = false)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());

        // The page is only as wide as the stencil + its margins, so the capture is the artifact and not a
        // screenful of empty background.
        var pageWidth = width + 48;
        var page = new WireframePage { Id = "spreadsheet-stencil", Name = "Spreadsheet", Width = pageWidth, Height = height + 96 };
        var element = new WireframeElement
        {
            Id = "sheet",
            Type = "TmSpreadsheet",
            X = 24,
            Y = 72,
            W = width,
            H = height,
        };
        element.SetProp("title", "Projekt Aurora");
        element.SetProp("rows", 9);
        element.SetProp("columns", 6);
        element.SetProp("sheetCount", 3);
        element.SetProp("showHeader", showHeader);
        if (layoutMode is not null)
        {
            element.SetProp("layoutMode", layoutMode);
        }

        page.Elements.Add(element);

        var services = new ServiceCollection();
        services.AddLogging();
        await using var htmlRenderer = new HtmlRenderer(services.BuildServiceProvider(), NullLoggerFactory.Instance);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment fragment = builder =>
            {
                builder.OpenElement(0, "svg");
                builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
                builder.AddAttribute(2, "viewBox", $"0 0 {WireframeSvg.F(pageWidth)} {WireframeSvg.F(page.Height)}");
                builder.AddAttribute(3, "width", WireframeSvg.F(pageWidth));
                builder.AddAttribute(4, "height", WireframeSvg.F(page.Height));
                builder.AddMarkupContent(5, $"<rect x='0' y='0' width='{WireframeSvg.F(pageWidth)}' height='{WireframeSvg.F(page.Height)}' fill='#f8fafc'></rect>");
                builder.AddMarkupContent(6, WireframeSvg.Text(
                    $"TmSpreadsheet stencil — {layoutMode ?? "auto"}{(showHeader ? " (header)" : string.Empty)}",
                    24, 34, 18, WireframeSvg.ColorText, fontWeight: "600"));
                WireframePageSvg.BuildFragment(page, registry).Invoke(builder);
                builder.CloseElement();
            };

            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Content"] = fragment });
            var output = await htmlRenderer.RenderComponentAsync<FragmentHost>(parameters);
            return output.ToHtmlString();
        });
    }

    private async Task<string> CaptureAsync(string name, string svgMarkup)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1200, 700);
        await page.SetContentAsync(
            $"""
            <!doctype html>
            <html>
            <body style="margin:0;background:#f8fafc">{svgMarkup}</body>
            </html>
            """);

        var svg = page.Locator("svg").First;
        await svg.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var path = ScreenshotPath(name);
        await svg.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        TestContext.AddResultFile(path);
        await page.CloseAsync();
        return path;
    }

    private static string ScreenshotPath(string name)
    {
        var directory = Path.Combine(
            RepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "wireframe", "spreadsheet-stencil");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name + ".png");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("the repository root (TempoBlazor.slnx) was not found");
    }

    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => Content?.Invoke(builder);
    }
}
