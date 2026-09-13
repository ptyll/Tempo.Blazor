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

[TestClass]
[TestCategory("Wireframe")]
public class StencilStructurePhase10E2ETests : WasmTestBase
{
    [TestMethod]
    public async Task TempoPackStructure_GalleryScreenshot()
    {
        var svgMarkup = await RenderGallerySvgAsync();
        svgMarkup.Should().StartWith("<svg");
        svgMarkup.Should().Contain("Faktury");
        svgMarkup.Should().Contain("Invoices");
        svgMarkup.Should().Contain("Keyboard shortcuts");
        svgMarkup.Should().NotContainEquivalentOf("<script");
        svgMarkup.Should().NotContainEquivalentOf("<foreignObject");

        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(
            $"""
            <!doctype html>
            <html>
            <body style="margin:0;background:#f8fafc">{svgMarkup}</body>
            </html>
            """);

        var svg = page.Locator("svg").First;
        await svg.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var path = BaselinePath("stencil-structure", "tempo-pack-structure-phase10-gallery");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = true });
        TestContext.AddResultFile(path);

        File.Exists(path).Should().BeTrue();
    }

    private static async Task<string> RenderGallerySvgAsync()
    {
        var registry = Registry();
        var page = new WireframePage { Id = "structure", Name = "Structure", Width = 1500, Height = 1400 };

        var samples = Samples();
        var x = 24.0;
        var y = 72.0;
        var currentRowHeight = 0.0;
        const double colW = 480;
        const double minRowH = 76;
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            if (i > 0 && i % 3 == 0)
            {
                x = 24;
                y += Math.Max(minRowH, currentRowHeight) + 28;
                currentRowHeight = 0;
            }

            var def = registry.GetDef(sample.Type)!;
            var element = new WireframeElement
            {
                Id = "sample" + i,
                Type = sample.Type,
                X = x,
                Y = y,
                W = Math.Min(def.DefaultWidth, colW - 36),
                H = def.DefaultHeight
            };
            foreach (var (key, value) in sample.Props)
                element.SetProp(key, value);

            page.Elements.Add(element);
            currentRowHeight = Math.Max(currentRowHeight, element.H);
            x += colW;
        }
        page.Height = Math.Max(page.Height, y + Math.Max(minRowH, currentRowHeight) + 56);

        var services = new ServiceCollection();
        services.AddLogging();
        await using var htmlRenderer = new HtmlRenderer(services.BuildServiceProvider(), NullLoggerFactory.Instance);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment fragment = builder =>
            {
                builder.OpenElement(0, "svg");
                builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
                builder.AddAttribute(2, "viewBox", $"0 0 1500 {page.Height}");
                builder.AddAttribute(3, "width", "1500");
                builder.AddAttribute(4, "height", WireframeSvg.F(page.Height));
                builder.AddMarkupContent(5, $"<rect x='0' y='0' width='1500' height='{WireframeSvg.F(page.Height)}' fill='#f8fafc'></rect>");
                builder.AddMarkupContent(6, WireframeSvg.Text("Tempo stencil structure and navigation", 24, 34, 18, WireframeSvg.ColorText, fontWeight: "600"));
                WireframePageSvg.BuildFragment(page, registry).Invoke(builder);
                builder.CloseElement();
            };

            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Content"] = fragment });
            var output = await htmlRenderer.RenderComponentAsync<FragmentHost>(parameters);
            return output.ToHtmlString();
        });
    }

    private static Sample[] Samples()
        =>
        [
            S("TmCard", ("title", "Faktury"), ("showHeader", true), ("showFooter", true)),
            S("TmStatCard", ("title", "Revenue"), ("value", "12 450"), ("unit", "Kc"), ("trend", "up")),
            S("TmBadge", ("label", "Paid"), ("variant", "success")),
            S("TmChip", ("label", "Overdue"), ("variant", "danger")),
            S("TmChipGroup", ("chips", new[] { "Alpha", "Beta", "Gamma" })),
            S("TmFilterChip", ("label", "Open"), ("active", true)),
            S("TmDivider"),
            S("TmText", ("text", "Hello structure")),
            S("TmAccordion", ("items", new[] { "Details", "History", "Files" })),
            S("TmAccordionItem", ("title", "Advanced"), ("expanded", true)),
            S("TmEmptyState", ("title", "No invoices"), ("actionLabel", "Create")),
            S("TmQRCode", ("value", "INV-001")),
            S("TmBarcode", ("value", "1234567890")),
            S("TmChangeDiff", ("oldValue", "Draft"), ("newValue", "Approved")),
            S("TmMultiViewList", ("title", "Invoices")),
            S("TmDataTable", ("title", "Invoices"), ("columns", new[] { "Invoice", "Customer", "Total", "Status" }), ("rows", 5)),
            S("TmPagination", ("totalPages", 5), ("currentPage", 2)),
            S("TmBulkActionBar", ("selectedCount", 7)),
            S("TmColumnFilter", ("columnName", "Customer"), ("filterType", "text")),
            S("TmColumnPicker", ("columns", new[] { "Customer", "Total", "Status", "Created" })),
            S("TmViewManager", ("viewName", "My view")),
            S("TmTabs", ("tabs", new[] { "Overview", "Customers", "Revenue" }), ("activeTab", 1)),
            S("TmTabPanel", ("label", "Details panel")),
            S("TmBreadcrumbs", ("items", new[] { "Home", "Invoices", "Detail" })),
            S("TmMenu", ("items", new[] { "Dashboard", "Projects", "Settings" })),
            S("TmContextMenu", ("items", new[] { "Edit", "Duplicate", "Delete" })),
            S("TmContextMenuItem", ("text", "Remove"), ("danger", true)),
            S("TmBottomNavigation", ("items", new[] { "Home", "Search", "Inbox", "Profile" }), ("activeIndex", 2)),
            S("TmSection", ("title", "Details")),
            S("TmSidebar", ("items", new[] { "Dashboard", "Users", "Reports", "Settings" })),
            S("TmTopBar", ("title", "Tempo")),
            S("TmDrawer", ("title", "Filters")),
            S("TmSplitter", ("pane1Label", "Preview"), ("pane2Label", "Details")),
            S("TmStackLayout", ("items", 3)),
            S("TmDockManager"),
            S("TmCommandPalette", ("placeholder", "Run command")),
            S("TmKeyboardShortcutsHelp", ("shortcuts", new[] { "Ctrl+S Save", "Ctrl+K Command", "Esc Close" })),
            S("TmToolbar", ("title", "Invoices"), ("sticky", true)),
            S("TmToolbarButton", ("label", "Refresh"), ("icon", "refresh-cw")),
            S("TmToolbarDivider")
        ];

    private static WireframeComponentRegistry Registry()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        return registry;
    }

    private static Sample S(string type, params (string Key, object? Value)[] props)
        => new(type, props);

    private static string BaselinePath(string area, string state)
    {
        var dir = BaselineOutput.DirectoryFor(null, area);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{state}.png");
    }

    private readonly record struct Sample(string Type, (string Key, object? Value)[] Props);

    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => Content?.Invoke(builder);
    }
}
