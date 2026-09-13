using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// 2bed637e: <c>.tm-btn</c> must carry a size of its own. Until 2.8.26 padding and height lived only
/// on the <c>tm-btn-{xs,sm,md,lg}</c> modifiers, so markup that emitted <c>class="tm-btn
/// tm-btn-primary"</c> — ten attributes across TmDashboard, TmWidgetSelector and TmViewManager when
/// this was measured — rendered at content height, about 22 px of text and no padding at all.
/// </summary>
/// <remarks>
/// <para>
/// THE SPEC ASKS FOR <c>min-height</c> ON THE BASE AND UNCHANGED SIZE CLASSES, and those two cannot
/// both be literal: <c>min-height</c> CLAMPS <c>height</c>, so a base <c>min-height: 2.375rem</c>
/// would silently turn every <c>.tm-btn-xs</c> and <c>.tm-btn-sm</c> into a medium button. The base
/// therefore carries <c>height</c> — the same declaration the medium modifier itself makes — which
/// the smaller modifiers still override by source order. The spec's value contract ("values equal
/// today's .tm-btn-md") is met exactly; the <c>min-height</c> spelling is recorded here rather than
/// copied into a regression.
/// </para>
/// <para>
/// THE HEIGHT FLOOR: the D17 note asks for <c>getBoundingClientRect().height &gt;= 36</c>. bUnit
/// has no layout engine, so the measurement is the resolved <c>height</c> declaration over the
/// shipped bundle — 2.375 rem, i.e. 38 px at the 16 px root the tokens are authored against.
/// </para>
/// </remarks>
public sealed class ButtonBaseSizeTests : LocalizationTestBase
{
    private const double RootFontPx = 16.0;

    private static readonly Regex ClassAttribute =
        new(@"class\s*=\s*""(?<classes>[^""]*)""", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ButtonBaseSizeTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact]
    public void TmButton_WithoutAnExplicitSize_MeasuresAtLeast36px()
    {
        var cut = Render<TmButton>(p => p.AddChildContent("OK"));
        var button = cut.Find("button");

        HeightPx(button.GetAttribute("class") ?? string.Empty).Should().BeGreaterThanOrEqualTo(
            36,
            "a button rendered with no Size argument must meet the 36 px floor — the resolved height "
            + "comes from the shipped bundle, not from a hoped-for default");
    }

    [Fact]
    public void TheBaseRule_CarriesTheMediumSize()
    {
        var winner = CssCascade.Winning(
            ThemeCss.BundledCss(),
            [new CssCascade.Element("button", "tm-btn")],
            "height");

        winner.Should().Be(
            "var(--tm-input-height-md)",
            "the bare .tm-btn element must paint at the medium input height — the same declaration "
            + ".tm-btn-md makes, so markup that forgot the modifier still gets the 38 px size");
    }

    /// <summary>
    /// The other half of the spec's constraint: the four modifiers are UNCHANGED, and the base
    /// declaration must not climb over them — a <c>min-height</c> on the base would clamp
    /// <c>.tm-btn-xs</c>/<c>.tm-btn-sm</c> up to the medium height without touching a byte of them.
    /// </summary>
    [Fact]
    public void SizeModifiers_StillResolveTheirOwnHeight()
    {
        var css = ThemeCss.BundledCss();

        foreach (var (sizeClass, expected) in new Dictionary<string, string>
                 {
                     ["tm-btn-xs"] = "var(--tm-input-height-xs)",
                     ["tm-btn-sm"] = "var(--tm-input-height-sm)",
                     ["tm-btn-md"] = "var(--tm-input-height-md)",
                     ["tm-btn-lg"] = "var(--tm-input-height-lg)",
                 })
        {
            CssCascade.Winning(css, [new CssCascade.Element("button", "tm-btn", sizeClass)], "height")
                .Should().Be(expected, "{0} must keep its own height — the base size may only apply "
                             + "where no modifier does", sizeClass);
        }

        // And the base must not additionally clamp from below: no min-height on .tm-btn anywhere.
        ThemeCss.TryProperty("_button.css", ".tm-btn", "min-height").Should().BeNull(
            "min-height on the base would clamp .tm-btn-xs and .tm-btn-sm to the medium height — "
            + "height is the declaration the modifiers can still override");
    }

    /// <summary>
    /// The population the defect was measured over: every literal <c>class="…"</c> attribute in
    /// <c>src/</c> markup that names <c>tm-btn</c> but no size modifier. Each must resolve to at
    /// least the 36 px floor through the base rule.
    /// </summary>
    [Fact]
    public void EveryBareTmButtonInMarkup_MeetsTheFloor()
    {
        var bare = BareButtonAttributes().ToList();
        bare.Should().NotBeEmpty(
            "the sweep found ten bare tm-btn attributes at measurement time — an empty population "
            + "here means the scanner broke, not that the defect vanished");

        _output.WriteLine($"[ButtonBaseSize] bare tm-btn attributes without a size class: {bare.Count}");
        foreach (var (path, classes) in bare)
        {
            _output.WriteLine($"  {path}: class=\"{classes}\"");
        }

        foreach (var (path, classes) in bare)
        {
            HeightPx(classes).Should().BeGreaterThanOrEqualTo(
                36,
                "{0} emits tm-btn without a size modifier; the base rule must give it a real height",
                path);
        }
    }

    private static IEnumerable<(string Path, string Classes)> BareButtonAttributes()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var src = Path.Combine(root, "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.razor", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(src, "*.razor.cs", SearchOption.AllDirectories)))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match attribute in ClassAttribute.Matches(text))
            {
                var classes = attribute.Groups["classes"].Value;
                if (classes.Contains('@'))
                {
                    continue;
                }

                var parts = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var isButton = parts.Contains("tm-btn", StringComparer.Ordinal);
                var hasSize = parts.Any(part => part is "tm-btn-xs" or "tm-btn-sm" or "tm-btn-md" or "tm-btn-lg");
                if (isButton && !hasSize)
                {
                    yield return (
                        Path.GetRelativePath(root, file).Replace('\\', '/'),
                        classes);
                }
            }
        }
    }

    /// <summary>The resolved <c>height</c> of an element carrying <paramref name="classes"/>, in px.</summary>
    private static double HeightPx(string classes)
    {
        var winner = CssCascade.Winning(
            ThemeCss.BundledCss(),
            [new CssCascade.Element("button", classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))],
            "height");

        var tokens = ThemeCss.TokenGraph(dark: false);
        var resolved = ThemeCss.ResolveColour(winner, tokens);
        var match = Regex.Match(resolved, @"^(?<value>[\d.]+)(?<unit>rem|px)$");
        match.Success.Should().BeTrue(
            "height '{0}' (resolved '{1}') must be a length the sweep can measure", winner, resolved);
        var value = double.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return match.Groups["unit"].Value == "rem" ? value * RootFontPx : value;
    }
}
