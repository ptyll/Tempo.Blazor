using System.Text;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QRCoder;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

public class TmQRCodeTests : LocalizationTestBase
{
    private static readonly Regex StandaloneModuleFill = new(
        "<g class=\"tm-qr-code__modules\" fill=\"(?<fill>[^\"]+)\"",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Regex StandaloneSurfaceFill = new(
        "<rect fill=\"(?<fill>[^\"]+)\"",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void TmQRCode_Renders_Svg()
    {
        var cut = Render<TmQRCode>(p => p.Add(x => x.Value, "https://example.com"));
        cut.Find("svg").Should().NotBeNull();
    }

    [Fact]
    public void TmQRCode_Size_Applies_Width_And_Height()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "test")
            .Add(x => x.Size, 300));

        var svg = cut.Find("svg");
        svg.GetAttribute("width").Should().Be("300");
        svg.GetAttribute("height").Should().Be("300");
    }

    [Fact]
    public void TmQRCode_Default_Size_Is_200()
    {
        var cut = Render<TmQRCode>(p => p.Add(x => x.Value, "test"));
        var svg = cut.Find("svg");
        svg.GetAttribute("width").Should().Be("200");
        svg.GetAttribute("height").Should().Be("200");
    }

    [Fact]
    public void TmQRCode_Changed_Value_Regenerates()
    {
        var cut = Render<TmQRCode>(p => p.Add(x => x.Value, "first"));
        var firstSvg = cut.Find("svg").OuterHtml;

        cut.Render(p => p.Add(x => x.Value, "second"));
        var secondSvg = cut.Find("svg").OuterHtml;

        secondSvg.Should().NotBe(firstSvg);
    }

    [Fact]
    public void TmQRCode_ForegroundColor_Applies_To_Svg()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "test")
            .Add(x => x.ForegroundColor, "#ff0000"));

        var svg = cut.Find("svg");
        svg.InnerHtml.Should().Contain("fill=\"#ff0000\"");
    }

    [Fact]
    public void TmQRCode_BackgroundColor_Applies_To_Svg()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "test")
            .Add(x => x.BackgroundColor, "#00ff00"));

        // Plan 8fa7319c, phase 3: the colour used to arrive as an inline `background-color` style on the
        // SVG, which a strict style-src blocks. It is now the surface rectangle's fill — same pixel, no
        // inline style (this assertion replaces the 2.10.0 `svg.GetAttribute("style")` one).
        cut.Find("svg").HasAttribute("style").Should().BeFalse();
        cut.Find("svg > rect").GetAttribute("fill").Should().Be("#00ff00");
    }

    [Fact]
    public void TmQRCode_Empty_Value_Renders_Nothing()
    {
        var cut = Render<TmQRCode>(p => p.Add(x => x.Value, ""));
        cut.FindAll("svg").Count.Should().Be(0);
    }

    [Fact]
    public void TmQRCode_Internal_Rects_Keep_Original_Dimensions()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "test")
            .Add(x => x.Size, 300));

        var rects = cut.FindAll("rect");
        rects.Count.Should().BeGreaterThan(10);

        // At least one rect must have a small width (a QR module), not the full 300px
        var hasSmallRect = rects.Any(r =>
        {
            var w = r.GetAttribute("width");
            return w != null && int.TryParse(w, out var wi) && wi < 100;
        });
        hasSmallRect.Should().BeTrue("QR module rects should retain their original small widths");
    }

    [Fact]
    public void TmQRCode_ErrorCorrectionLevel_Propagates()
    {
        // H level should still produce valid QR output (just different module pattern)
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "test")
            .Add(x => x.ErrorCorrectionLevel, QRErrorCorrectionLevel.H));

        cut.Find("svg").Should().NotBeNull();
    }

    // ── accessible name (plan 8fa7319c, phase 3) ─────────────────────────────────────────────────
    // Before this phase the SVG was an UNNAMED graphic: no role, no aria-label, no title, so a
    // screen reader announced nothing at all. A QR code is never decorative — it always carries a
    // name, the host's or the localized default.

    [Fact]
    public void TmQRCode_Without_AriaLabel_Uses_The_Localized_Default_Name()
    {
        var cut = Render<TmQRCode>(p => p.Add(x => x.Value, "https://example.com"));

        var svg = cut.Find("svg");
        svg.GetAttribute("role").Should().Be("img");
        svg.GetAttribute("aria-label").Should().Be("QR code",
            "an unnamed graphic is the defect this phase removes; the fallback is localized");
        cut.Find("svg > title").TextContent.Should().Be("QR code",
            "the title is the native tooltip and the name a browser falls back to");
    }

    [Fact]
    public void TmQRCode_AriaLabel_Names_The_Graphic()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.AriaLabel, "Scan to open the Tempo hub"));

        var svg = cut.Find("svg");
        svg.GetAttribute("role").Should().Be("img");
        svg.GetAttribute("aria-label").Should().Be("Scan to open the Tempo hub");
        cut.Find("svg > title").TextContent.Should().Be("Scan to open the Tempo hub");
    }

    [Fact]
    public void TmQRCode_Blank_AriaLabel_Falls_Back_To_The_Localized_Default()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.AriaLabel, "   "));

        cut.Find("svg").GetAttribute("aria-label").Should().Be("QR code",
            "whitespace is not a name — an empty aria-label would silence the graphic again");
    }

    [Fact]
    public void TmQRCode_AriaLabel_Is_Escaped_Into_The_Markup()
    {
        const string label = "Tom & \"Jerry\" <qr>";
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.AriaLabel, label));

        // A raw quote would have broken the attribute and a raw '<' the SVG; the DOM must carry the text.
        cut.Find("svg").GetAttribute("aria-label").Should().Be(label);
        cut.Find("svg > title").TextContent.Should().Be(label);
    }

    // ── colour tokens (plan 8fa7319c, phase 3) ───────────────────────────────────────────────────
    // The colours were hex-only parameters and the background was written into an inline
    // background-color. A theme-driven host cannot follow the theme that way, and the inline style
    // is what a strict style-src blocks.

    [Fact]
    public void TmQRCode_UseThemeColors_Renders_CurrentColor_And_Token_Classes_Without_Inline_Style()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.UseThemeColors, true));

        var svg = cut.Find("svg");
        svg.GetAttribute("class").Should().Contain("tm-qr-code__svg--themed");
        svg.HasAttribute("style").Should().BeFalse("a strict style-src does not allow inline styles");

        var modules = cut.Find(".tm-qr-code__modules");
        modules.GetAttribute("fill").Should().Be("currentColor",
            "currentColor is a valid SVG paint and follows the component token");
        modules.HasAttribute("style").Should().BeFalse();

        var surface = cut.Find(".tm-qr-code__surface");
        surface.HasAttribute("fill").Should().BeFalse(
            "the quiet zone colour comes from --tm-qr-code-background, not from a hex attribute");
        cut.Markup.Should().NotContain("rgb(");
        cut.Markup.Should().NotContain("style=", "neither the default nor the themed path writes inline styles");
    }

    [Fact]
    public void TmQRCode_UseThemeColors_Keeps_An_Explicit_Color()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.UseThemeColors, true)
            .Add(x => x.ForegroundColor, "#123456"));

        cut.Find(".tm-qr-code__modules").GetAttribute("fill").Should().Be("#123456",
            "an explicit colour is the host's answer and wins over the theme default");
    }

    [Fact]
    public void TmQRCode_Foreground_CurrentColor_Is_A_Paint_Attribute()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.ForegroundColor, "currentColor"));

        var modules = cut.Find(".tm-qr-code__modules");
        modules.GetAttribute("fill").Should().Be("currentColor");
        modules.HasAttribute("style").Should().BeFalse("currentColor needs no inline style");
    }

    [Fact]
    public void TmQRCode_Foreground_Var_Reference_Becomes_A_Custom_Property()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.ForegroundColor, "var(--tm-color-primary)"));

        var modules = cut.Find(".tm-qr-code__modules");
        modules.GetAttribute("style").Should().Contain("var(--tm-color-primary)",
            "a var() reference is not a valid SVG paint ATTRIBUTE; it reaches the DOM as a CSS property");
        modules.HasAttribute("fill").Should().BeFalse();
    }

    [Fact]
    public void TmQRCode_Background_Var_Reference_Becomes_A_Custom_Property()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.BackgroundColor, "var(--tm-paper)"));

        var surface = cut.Find("rect");
        surface.GetAttribute("style").Should().Contain("var(--tm-paper)");
        surface.HasAttribute("fill").Should().BeFalse();
    }

    [Theory]
    [InlineData("red; background:url(x)")]
    [InlineData("red\" onload=\"alert(1)")]
    [InlineData("url(//evil.example/x)")]
    [InlineData("not-a-colour")]
    [InlineData("var(--a&quot)")]
    public void TmQRCode_Invalid_Color_Is_Rejected_And_Logged(string color)
    {
        var factory = new RecordingLoggerFactory();
        Services.AddSingleton<ILoggerFactory>(factory);

        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.ForegroundColor, color));

        cut.Markup.Should().NotContain("url(");
        cut.Markup.Should().NotContain("onload");
        cut.Find(".tm-qr-code__modules").GetAttribute("fill").Should().Be("#000000",
            "an unusable colour falls back to the default instead of reaching the attribute");
        factory.Entries.Should().Contain(entry =>
                entry.Level == LogLevel.Warning && entry.Message.Contains("ForegroundColor"),
            "a rejected colour must be reported to the developer: "
            + string.Join(" | ", factory.Entries.Select(entry => entry.Message)));
    }

    [Fact]
    public void TmQRCode_Both_Invalid_Colours_Warn_Once_Per_Parameter_Across_Renders()
    {
        var factory = new RecordingLoggerFactory();
        Services.AddSingleton<ILoggerFactory>(factory);

        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.ForegroundColor, "not-a-colour")
            .Add(x => x.BackgroundColor, "also-not-a-colour"));

        cut.Render(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.ForegroundColor, "not-a-colour")
            .Add(x => x.BackgroundColor, "also-not-a-colour"));

        var warnings = factory.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Should().HaveCount(2,
            "a warning is keyed by parameter|value, so the second invalid parameter is not hidden by "
            + "the first one's signature and a re-render does not repeat either: "
            + string.Join(" | ", warnings.Select(entry => entry.Message)));
        warnings.Should().Contain(entry => entry.Message.Contains("ForegroundColor"));
        warnings.Should().Contain(entry => entry.Message.Contains("BackgroundColor"));
    }

    // ── contrast validation (plan 8fa7319c, phase 3) ─────────────────────────────────────────────

    [Fact]
    public void TmQRCode_Low_Contrast_Logs_A_Warning()
    {
        var factory = new RecordingLoggerFactory();
        Services.AddSingleton<ILoggerFactory>(factory);

        Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.ForegroundColor, "#f5f5f5")
            .Add(x => x.BackgroundColor, "#ffffff"));

        factory.Entries.Should().Contain(entry =>
                entry.Level == LogLevel.Warning && entry.Message.Contains("contrast"),
            "a scanner needs the modules to stand out from the quiet zone; 1,07:1 does not");
        factory.Entries.Where(entry => entry.Level == LogLevel.Warning).Should().HaveCount(1,
            "the page markup and the standalone file carry the SAME pair, so it is reported once");
    }

    [Fact]
    public void TmQRCode_Good_Contrast_Logs_No_Warning()
    {
        var factory = new RecordingLoggerFactory();
        Services.AddSingleton<ILoggerFactory>(factory);

        Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.ForegroundColor, "#000000")
            .Add(x => x.BackgroundColor, "#ffffff"));

        factory.Entries.Should().NotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void TmQRCode_Themed_Colors_Skip_The_Contrast_Check()
    {
        var factory = new RecordingLoggerFactory();
        Services.AddSingleton<ILoggerFactory>(factory);

        Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.UseThemeColors, true));

        factory.Entries.Should().NotContain(
            entry => entry.Level == LogLevel.Warning,
            "token values cannot be resolved here — guessing a contrast would warn on every render");
    }

    // ── default markup snapshot (plan 8fa7319c, phase 3) ─────────────────────────────────────────
    // The default hex path must stay exactly today's pixels: the geometry is compared module by
    // module against QRCoder's own SVG renderer, which is what the pre-2.11.0 markup contained. The
    // equivalence holds for every supported error-correction level and for a matrix that outgrows the
    // smallest version, so the run-merging above cannot be tuned to one shape.

    private const string LongQrValue =
        "https://tempo.blazor/qr?phase=3&source=newsletter&campaign=autumn-2026&segment=returning-customers"
        + "&utm_medium=email&utm_source=tempo-hub&utm_campaign=qr-equivalence&utm_content=hero"
        + "&payload=eyJpZCI6InFyLWVxdWl2YWxlbmNlIiwicmV2aXNpb24iOjMsIm5vdGUiOiJhIGxvbmcgdmFsdWUg"
        + "Zm9yY2VzIGEgbGFyZ2VyIG1vZHVsZSBtYXRyaXggdGhhbiB0aGUgZGVmYXVsdCBleGFtcGxlIn0";

    [Theory]
    [InlineData(QRErrorCorrectionLevel.L, "https://tempo.blazor/qr?phase=3")]
    [InlineData(QRErrorCorrectionLevel.Q, "https://tempo.blazor/qr?phase=3")]
    [InlineData(QRErrorCorrectionLevel.H, "https://tempo.blazor/qr?phase=3")]
    [InlineData(QRErrorCorrectionLevel.M, LongQrValue)]
    public void TmQRCode_Default_Markup_Paints_The_Same_Modules_As_QRCoder(
        QRErrorCorrectionLevel level,
        string value)
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Size, 200)
            .Add(x => x.ErrorCorrectionLevel, level));

        var ecc = level switch
        {
            QRErrorCorrectionLevel.L => QRCodeGenerator.ECCLevel.L,
            QRErrorCorrectionLevel.Q => QRCodeGenerator.ECCLevel.Q,
            QRErrorCorrectionLevel.H => QRCodeGenerator.ECCLevel.H,
            _ => QRCodeGenerator.ECCLevel.M
        };

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, ecc);
        using var reference = new SvgQRCode(data);
        var referenceMarkup = reference.GetGraphic(10, "#000000", "#ffffff");

        var expected = Regex.Matches(
                referenceMarkup,
                "<rect x=\"(?<x>[^\"]*)\" y=\"(?<y>[^\"]*)\" width=\"(?<w>[^\"]*)\" height=\"(?<h>[^\"]*)\"")
            .Select(match => (
                X: match.Groups["x"].Value,
                Y: match.Groups["y"].Value,
                W: match.Groups["w"].Value,
                H: match.Groups["h"].Value))
            .ToList();

        var actual = cut.FindAll("svg rect")
            .Select(rect => (
                X: rect.GetAttribute("x"),
                Y: rect.GetAttribute("y"),
                W: rect.GetAttribute("width"),
                H: rect.GetAttribute("height")))
            .ToList();

        expected.Should().NotBeEmpty();
        actual.Should().Equal(expected,
            "2.11.0 replaced the regex post-processing with a direct renderer — the modules must not move "
            + "at ECC {0} for a {1}-character value", level, value.Length);

        var svg = cut.Find("svg");
        svg.GetAttribute("width").Should().Be("200");
        svg.GetAttribute("height").Should().Be("200");
        svg.GetAttribute("viewBox").Should().Be(
            $"0 0 {data.ModuleMatrix.Count * 10} {data.ModuleMatrix.Count * 10}");
    }

    [Fact]
    public void TmQRCode_Default_Markup_Carries_No_Inline_Style_Anymore()
    {
        var cut = Render<TmQRCode>(p => p.Add(x => x.Value, "https://example.com"));

        cut.Find("svg").HasAttribute("style").Should().BeFalse(
            "the old background-color was an inline style a strict style-src blocks; the surface rect paints it now");
        cut.Markup.Should().NotContain("style=");
        cut.Find("svg > rect").GetAttribute("fill").Should().Be("#ffffff");
        cut.Find(".tm-qr-code__modules").GetAttribute("fill").Should().Be("#000000");
    }

    // ── SVG download (plan 8fa7319c, phase 3) ────────────────────────────────────────────────────

    [Fact]
    public void TmQRCode_GetSvg_Returns_The_Rendered_Markup()
    {
        var cut = Render<TmQRCode>(p => p.Add(x => x.Value, "https://example.com"));

        var svg = cut.Instance.GetSvg();
        svg.Should().NotBeNull();
        svg.Should().StartWith("<svg");
        svg.Should().Contain("role=\"img\"");
    }

    [Fact]
    public void TmQRCode_GetSvg_Is_Null_For_An_Empty_Value()
    {
        var cut = Render<TmQRCode>(p => p.Add(x => x.Value, ""));

        cut.Instance.GetSvg().Should().BeNull();
        cut.Instance.GetStandaloneSvg().Should().BeNull();
    }

    [Fact]
    public void TmQRCode_Download_Is_Opt_In()
    {
        var cut = Render<TmQRCode>(p => p.Add(x => x.Value, "https://example.com"));

        cut.FindAll(".tm-qr-code__download").Should().BeEmpty("the button is opt-in — no layout change for a host that never asked");
    }

    [Fact]
    public void TmQRCode_ShowDownload_Renders_A_Localized_Data_Link_Without_JavaScript()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.ShowDownload, true));

        var link = cut.Find(".tm-qr-code__download");
        link.TagName.Should().Be("A");
        link.GetAttribute("download").Should().Be("qr-code.svg");
        link.GetAttribute("type").Should().Be("image/svg+xml");
        link.TextContent.Trim().Should().Be("Download SVG");

        var href = link.GetAttribute("href")!;
        const string prefix = "data:image/svg+xml;base64,";
        href.Should().StartWith(prefix, "a data: URL needs no JS and no Blob");

        var standalone = Encoding.UTF8.GetString(Convert.FromBase64String(href[prefix.Length..]));
        standalone.Should().StartWith("<svg");
        standalone.Should().Contain("role=\"img\"");
        standalone.Should().Contain("xmlns=\"http://www.w3.org/2000/svg\"",
            "a standalone file needs the namespace to render outside the page");
        standalone.Should().Contain("fill=\"#000000\"");
        standalone.Should().Contain("fill=\"#ffffff\"");
        standalone.Should().NotContain("currentColor");
        standalone.Should().NotContain("var(");
    }

    [Fact]
    public void TmQRCode_Themed_Download_Resolves_The_Tokens_To_Absolute_Colors()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.UseThemeColors, true)
            .Add(x => x.ShowDownload, true));

        var standalone = cut.Instance.GetStandaloneSvg()!;
        standalone.Should().Contain("fill=\"#000000\"");
        standalone.Should().Contain("fill=\"#ffffff\"");
        standalone.Should().NotContain("currentColor",
            "a downloaded file has no page tokens; currentColor alone would paint a dark quiet zone");
        standalone.Should().NotContain("var(");
    }

    [Theory]
    [InlineData("var(--x)")]
    [InlineData("currentColor")]
    public void TmQRCode_Standalone_Svg_Chooses_A_Legible_Ink_When_The_Colour_Is_Not_Absolute(
        string fallbackInk)
    {
        // The page can paint var(--x) or the inherited colour through the cascade, but a downloaded file
        // cannot: exporting the default black ink on a dark surface would produce an unscannable file.
        // The export therefore picks black or white by the surface's luminance.
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.ForegroundColor, fallbackInk)
            .Add(x => x.BackgroundColor, "#111827"));

        var standalone = cut.Instance.GetStandaloneSvg()!;
        standalone.Should().NotContain("var(", "a standalone SVG has no page to resolve a token against");
        standalone.Should().NotContain("currentColor");

        var ink = FillOf(standalone, StandaloneModuleFill);
        var surface = FillOf(standalone, StandaloneSurfaceFill);
        ink.Should().NotBeNull();
        surface.Should().Be("#111827", "the export keeps the host's absolute surface");

        ContrastRatio(ink!, surface!).Should().BeGreaterThanOrEqualTo(3.0,
            "the exported pair must clear the 3:1 graphic minimum — {0} on {1}", ink, surface);
        ink.Should().Be("#ffffff",
            "#111827 is dark, so the fallback ink flips to white instead of painting black on black");
    }

    [Fact]
    public void TmQRCode_DownloadFileName_Is_Configurable()
    {
        var cut = Render<TmQRCode>(p => p
            .Add(x => x.Value, "https://example.com")
            .Add(x => x.ShowDownload, true)
            .Add(x => x.DownloadFileName, "hub-qr.svg"));

        cut.Find(".tm-qr-code__download").GetAttribute("download").Should().Be("hub-qr.svg");
    }

    private static string? FillOf(string svg, Regex pattern)
    {
        var match = pattern.Match(svg);
        return match.Success ? match.Groups["fill"].Value : null;
    }

    /// <summary>WCAG 2.1 contrast of two hex colours — an independent oracle for the exported pair.</summary>
    private static double ContrastRatio(string foreground, string background)
    {
        var foregroundLuminance = Luminance(foreground);
        var backgroundLuminance = Luminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        var digits = hex.TrimStart('#');
        if (digits.Length == 3)
        {
            digits = string.Concat(digits.Select(character => new string(character, 2)));
        }

        static double Channel(int value)
        {
            var scaled = value / 255.0;
            return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(Convert.ToInt32(digits[..2], 16)))
               + (0.7152 * Channel(Convert.ToInt32(digits.Substring(2, 2), 16)))
               + (0.0722 * Channel(Convert.ToInt32(digits.Substring(4, 2), 16)));
    }

    /// <summary>A logger the test owns: records (level, formatted message) so the assertion reads intent, not a call shape.</summary>
    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
