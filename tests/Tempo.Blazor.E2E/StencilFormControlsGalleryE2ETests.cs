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
public class StencilFormControlsGalleryE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task TempoPackFormControls_GalleryScreenshot()
    {
        var svgMarkup = await RenderGallerySvgAsync();
        svgMarkup.Should().StartWith("<svg");
        svgMarkup.Should().Contain("Ulozit");
        svgMarkup.Should().Contain("March 2026");
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

        var path = BaselinePath("stencil-form-controls", "tempo-pack-form-controls-gallery");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = true });
        TestContext.AddResultFile(path);

        File.Exists(path).Should().BeTrue();
    }

    private static async Task<string> RenderGallerySvgAsync()
    {
        var registry = Registry();
        var page = new WireframePage { Id = "form-controls", Name = "Form Controls", Width = 1320, Height = 1180 };

        var samples = Samples();
        var x = 24.0;
        var y = 72.0;
        var currentRowHeight = 0.0;
        const double colW = 305;
        const double minRowH = 76;
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            if (i > 0 && i % 4 == 0)
            {
                x = 24;
                y += Math.Max(minRowH, currentRowHeight) + 24;
                currentRowHeight = 0;
            }

            var def = registry.GetDef(sample.Type)!;
            var element = new WireframeElement
            {
                Id = "sample" + i,
                Type = sample.Type,
                X = x,
                Y = y,
                W = Math.Min(def.DefaultWidth, colW - 32),
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
                builder.AddAttribute(2, "viewBox", $"0 0 1320 {page.Height}");
                builder.AddAttribute(3, "width", "1320");
                builder.AddAttribute(4, "height", WireframeSvg.F(page.Height));
                builder.AddMarkupContent(5, $"<rect x='0' y='0' width='1320' height='{WireframeSvg.F(page.Height)}' fill='#f8fafc'></rect>");
                builder.AddMarkupContent(6, WireframeSvg.Text("Tempo stencil form controls", 24, 34, 18, WireframeSvg.ColorText, fontWeight: "600"));
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
            S("TmButton", ("label", "Ulozit"), ("variant", "primary")),
            S("TmSplitButton", ("label", "Actions")),
            S("TmCopyButton"),
            S("TmFloatingActionButton"),
            S("TmTextInput", ("label", "Customer"), ("placeholder", "Enter name")),
            S("TmTextArea", ("label", "Notes"), ("placeholder", "Long notes")),
            S("TmNumberInput", ("label", "Quantity")),
            S("TmSearchInput", ("placeholder", "Search orders")),
            S("TmCurrencyInput", ("label", "Total"), ("currencySymbol", "$")),
            S("TmCheckbox", ("label", "Accepted"), ("checked", true)),
            S("TmRadio", ("label", "Option A"), ("checked", true)),
            S("TmRadioGroup", ("label", "Choice"), ("options", new[] { "One", "Two" })),
            S("TmToggle", ("label", "Enabled"), ("checked", true)),
            S("TmToggleSection", ("label", "Advanced")),
            S("TmSelect", ("label", "Country"), ("placeholder", "Choose country")),
            S("TmMultiSelect", ("label", "Roles")),
            S("TmCascadingSelect", ("label", "Region"), ("levels", 3)),
            S("TmFilterableDropdown", ("label", "Status"), ("placeholder", "Filter status")),
            S("TmEntityPicker", ("label", "Owner"), ("placeholder", "Choose owner")),
            S("TmExpressionEditor", ("label", "Rule"), ("placeholder", "amount > 0")),
            S("TmPasswordStrengthIndicator", ("strength", 4)),
            S("TmSlider", ("label", "Progress"), ("value", 60)),
            S("TmRangeSlider", ("label", "Window"), ("from", 20), ("to", 80)),
            S("TmRating", ("value", 4), ("max", 5)),
            S("TmMaskedTextBox", ("label", "Birth"), ("mask", "__.__.____")),
            S("TmMultiColumnComboBox", ("label", "Account"), ("placeholder", "Select account")),
            S("TmSignature", ("placeholder", "Sign here")),
            S("TmSignatureCapture", ("placeholder", "Draw signature")),
            S("TmTagPicker", ("tags", new[] { "Alpha", "Beta" }), ("allowCreate", true)),
            S("TmDatePicker", ("label", "Due date"), ("format", "yyyy-mm-dd")),
            S("TmDateTimePicker", ("label", "Start"), ("format", "yyyy-mm-dd HH:mm")),
            S("TmTimePicker", ("label", "Time")),
            S("TmDateRangePicker", ("label", "Period")),
            S("TmTimeRangePicker", ("label", "Hours")),
            S("TmDateTimeRangePicker", ("label", "Booking")),
            S("TmTimeInput"),
            S("TmCalendarView", ("month", "March"), ("year", 2026), ("selectedDay", 17)),
            S("TmCalendarGrid", ("month", "April"), ("year", 2026)),
            S("TmRecurrenceEditor", ("frequency", "weekly"), ("interval", 2)),
            S("TmDropdown", ("text", "Options"), ("icon", "user")),
            S("TmDropdownItem", ("label", "Archive"), ("icon", "box"))
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
