using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Hygiene gate for <c>TmModelPicker</c> (plan 3e96f6d8, phase 4): every class the markup emits has a rule in
/// <c>_ai-model-picker.css</c>, the surface declares no motion of its own, a coarse pointer raises the option
/// rows to the 44 px touch target, the forced-colors block hands the focus ring back to the system's
/// <c>Highlight</c>, and the deprecated marker and the metric bars are words and tones rather than colour-only
/// cues.
/// </summary>
/// <remarks>
/// The package-wide token rules (no colour literal, no raw palette step, no <c>[data-theme]</c> block, every
/// <c>var(--tm-*)</c> resolvable) are enforced for every stylesheet of the package by
/// <c>AiMarkdownViewCssTests</c>.
/// </remarks>
public sealed class TmModelPickerCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex RazorComment =
        new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex CssClassSelector =
        new(@"\.(tm-[a-zA-Z][\w-]*)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex MotionDeclaration =
        new(@"(^|;)\s*(transition(-[a-z-]+)?|animation(-[a-z-]+)?)\s*:", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex CssRule =
        new(@"([^{}]+)\{([^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private const string Prefix = "tm-model-picker";

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    private static string Stylesheet()
        => Comment.Replace(
            File.ReadAllText(Path.Combine(
                Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-model-picker.css")),
            " ");

    private static string Markup()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI"), "TmModelPicker.razor*")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

        return RazorComment.Replace(string.Concat(files), string.Empty);
    }

    private static HashSet<string> Emitted()
    {
        var source = Markup();
        var fromAttributes = Regex.Matches(source, @"class\s*=\s*""([^""]*)""")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        var fromLiterals = Regex.Matches(source, @"""([^""]*" + Prefix + @"[^""]*)""")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        return fromAttributes
            .Concat(fromLiterals)
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Styled()
        => CssClassSelector.Matches(Stylesheet())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string RuleBlock(string selector)
    {
        var match = CssRule.Matches(Stylesheet())
            .Cast<Match>()
            .FirstOrDefault(candidate => candidate.Groups[1].Value
                .Split(',')
                .Any(token => string.Equals(token.Trim(), selector, StringComparison.Ordinal)));

        match.Should().NotBeNull($"the stylesheet declares a rule for {selector}");
        return match!.Groups[2].Value;
    }

    private static string MediaBlock(string query)
    {
        var start = Stylesheet().IndexOf(query, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the stylesheet declares a {query} block");

        var open = Stylesheet().IndexOf('{', start);
        var depth = 0;
        for (var index = open; index < Stylesheet().Length; index++)
        {
            depth += Stylesheet()[index] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return Stylesheet()[(open + 1)..index];
            }
        }

        return Stylesheet()[(open + 1)..];
    }

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var missing = Emitted().Except(Styled()).OrderBy(name => name, StringComparer.Ordinal).ToList();

        missing.Should().BeEmpty(
            $"TmModelPicker emits {string.Join(", ", missing)} without a rule in _ai-model-picker.css — an "
            + "unstyled class ships as an unstyled surface");
    }

    [Fact]
    public void TheSurfaceDeclaresNoMotion()
    {
        MotionDeclaration.Matches(Stylesheet())
            .Select(match => match.Value.Trim())
            .Should().BeEmpty("the picker is a comparison surface the reader may keep open while a run streams");
    }

    [Fact]
    public void TheMetricBarKnobIsDeclaredOnTheRoot()
    {
        RuleBlock("." + Prefix).Should().Contain($"--{Prefix}-bar:",
            "an undeclared var() is an undefined token and paints nothing");

        RuleBlock($".{Prefix}__bar-fill").Should().Contain($"var(--{Prefix}-bar");
    }

    [Fact]
    public void ThePopupPaintsItsOwnSurface()
    {
        var panel = RuleBlock(".tm-model-picker__panel");

        panel.Should().Contain("background",
            "the core overlay panel reset is transparent by design, so a panel that paints nothing shows the page through it");
        panel.Should().Contain("box-shadow");
        panel.Should().Contain("border:");
        panel.Should().Contain("max-block-size", "the list scrolls inside the card instead of growing past the viewport");
    }

    [Fact]
    public void ACoarsePointerRaisesTheOptionRowsToTheTouchTarget()
    {
        var coarse = MediaBlock("@media (pointer: coarse)");

        coarse.Should().Contain(".tm-model-picker__option");
        coarse.Should().Contain("min-block-size: var(--tm-touch-target)");
        RuleBlock(".tm-model-picker__trigger").Should().Contain("min-block-size: var(--tm-touch-target)");
        RuleBlock(".tm-model-picker__search-input").Should().Contain("min-block-size: var(--tm-touch-target)");
    }

    [Fact]
    public void ForcedColorsRestoreTheSystemFocusRing()
    {
        var forced = MediaBlock("@media (forced-colors: active)");

        forced.Should().Contain(".tm-model-picker__trigger:focus-visible");
        forced.Should().Contain(".tm-model-picker__search-input:focus-visible");
        forced.Should().Contain("outline-color: Highlight");
    }

    [Fact]
    public void ForcedColors_MarkTheSelectedRow_AndKeepTheBarsVisible()
    {
        var forced = MediaBlock("@media (forced-colors: active)");

        forced.Should().Contain(".tm-model-picker__option--selected",
            "the primary-coloured border is replaced by a system colour, so the picked row would otherwise " +
            "look exactly like every other row and only a small icon would carry the state");
        forced.Should().Contain("border-color: SelectedItem");
        forced.Should().Contain("border-width: 2px",
            "a 2 px system-coloured border survives forced colors and carries the selected state");

        forced.Should().Contain(".tm-model-picker__option--active");
        forced.Should().Contain("outline: 2px solid Highlight",
            "the highlighted row keeps a visible outline next to the selected row's border");

        forced.Should().Contain(".tm-model-picker__bar",
            "an empty track and its fill are both background-only colours and collapse into nothing");
        forced.Should().Contain("border: 1px solid CanvasText");
        forced.Should().Contain(".tm-model-picker__bar-fill");
        forced.Should().Contain("background: CanvasText");
        forced.Should().Contain("forced-color-adjust: none",
            "the fill is repainted by the browser unless it opts out, leaving a full track and no value");
    }

    [Fact]
    public void ThePickerFillsItsHostBox_SoAFormControlDoesNotShrinkToItsContent()
    {
        var root = RuleBlock("." + Prefix);

        root.Should().Contain("display: flex");
        root.Should().Contain("inline-size: 100%");
    }

    [Fact]
    public void TheMetricTextMayWrap_InsteadOfOverflowingTheCard()
    {
        var text = RuleBlock($".{Prefix}__metric-text");

        text.Should().NotContain("white-space: nowrap",
            "a price is longer than its bar column on a phone and must wrap inside the card");
        RuleBlock($".{Prefix}__metric").Should().Contain("flex-wrap: wrap");
    }

    [Fact]
    public void TheDeprecatedMarkerIsAWordOnAWarningTone()
    {
        RuleBlock(".tm-model-picker__chip--deprecated").Should()
            .Contain("--tm-color-warning", "deprecation is a warning state, and the chip also carries the word");
    }

    [Fact]
    public void TheActiveRowIsMarkedByABorderAndATone_NotByColourAlone()
    {
        var active = RuleBlock(".tm-model-picker__option--active");

        active.Should().Contain("border-color", "a colour wash alone is invisible in forced colors");
        active.Should().Contain("background");
        RuleBlock(".tm-model-picker__option--selected").Should().Contain("border-color",
            "the picked row stays distinguishable from the highlighted one");
    }
}
