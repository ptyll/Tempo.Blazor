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
public class StencilFeedbackFormsPhase11E2ETests : WasmTestBase
{
    [TestMethod]
    public async Task FeedbackFormsPack_GalleryScreenshot()
    {
        var svgMarkup = await RenderGallerySvgAsync();
        svgMarkup.Should().StartWith("<svg");
        svgMarkup.Should().Contain("Payment failed");
        svgMarkup.Should().Contain("Billing");
        svgMarkup.Should().Contain("Attachments");
        svgMarkup.Should().Contain("Brand");
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

        var path = BaselinePath("stencil-feedback-forms", "gallery");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = true });
        TestContext.AddResultFile(path);

        File.Exists(path).Should().BeTrue();
    }

    private static async Task<string> RenderGallerySvgAsync()
    {
        var registry = Registry();
        var page = new WireframePage { Id = "feedback-forms", Name = "Feedback Forms", Width = 1500, Height = 1400 };

        var samples = Samples();
        var x = 24.0;
        var y = 72.0;
        var currentRowHeight = 0.0;
        const double colW = 480;
        const double minRowH = 82;
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
                builder.AddMarkupContent(6, WireframeSvg.Text("Phase 11 - Feedback/Forms stencil pack", 24, 34, 18, WireframeSvg.ColorText, fontWeight: "600"));
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
            S("TmAlert", ("variant", "danger"), ("message", "Payment failed")),
            S("TmModal", ("title", "Edit customer")),
            S("TmDialog", ("variant", "warning"), ("title", "Confirm delete"), ("message", "This cannot be undone.")),
            S("TmTooltip", ("text", "Save changes")),
            S("TmPopover", ("title", "Filters")),
            S("TmProgressBar", ("variant", "success"), ("value", 72), ("max", 100)),
            S("TmSpinner"),
            S("TmSkeleton", ("lines", 3), ("showAvatar", true)),
            S("TmToastContainer", ("maxVisible", 3)),
            S("TmAutoSaveIndicator", ("state", "saved")),
            S("TmNotificationBell", ("unreadCount", 3)),
            S("TmFormSection", ("title", "Billing"), ("description", "Invoice recipient and payment settings.")),
            S("TmFormRow", ("label", "Amount"), ("required", true)),
            S("TmFormField", ("label", "Email"), ("helpText", "Work email")),
            S("TmInlineEdit", ("value", "Jane Doe")),
            S("TmValidatedField", ("label", "Tax ID"), ("valid", false), ("validationMessage", "Required")),
            S("TmFormValidationMessage", ("message", "Email is required")),
            S("TmValidationSummary"),
            S("TmDynamicFormRenderer", ("fieldCount", 4)),
            S("TmConditionBuilder", ("conditions", 3)),
            S("TmFormulaBuilder", ("formula", "SUM(Revenue)")),
            S("TmFileDropZone", ("label", "Drop contracts here")),
            S("TmAttachmentManager", ("maxFiles", 3)),
            S("TmAvatar", ("name", "JD"), ("color", "blue")),
            S("TmAvatarGroup", ("count", 4), ("max", 3)),
            S("TmIcon", ("name", "bell"), ("color", "blue")),
            S("TmColorPicker", ("label", "Brand"), ("value", "#22c55e")),
            S("TmFlatColorPicker", ("value", "#3b82f6")),
            S("TmColorPalette", ("swatches", 8)),
            S("TmColorGradient", ("startColor", "#3b82f6"), ("endColor", "#8b5cf6"))
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
