using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Hygiene gate for the composer stylesheet (<c>_ai-composer.css</c>, plan 7f21f26b phase 6): every class the
/// markup emits has a rule, every control reaches the 44 px touch floor on a coarse pointer, the forced-colors
/// block keeps the controls visible in a forced palette, the narrow layout is what sticks the composer to the
/// bottom (with the runtime keyboard knob), and the file declares no motion of its own — a suggestion list that
/// faded in would need a reduced-motion block for no benefit.
/// </summary>
/// <remarks>
/// The package-wide token rules (no colour literal, no raw palette step, no <c>[data-theme]</c> block, every
/// <c>var(--tm-*)</c> resolvable) are enforced for every stylesheet of the package by
/// <c>AiMarkdownViewCssTests</c>.
/// </remarks>
public sealed class TmAiComposerCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex CssClassSelector = new(@"\.(tm-[a-zA-Z][\w-]*)", RegexOptions.Compiled, RegexTimeout);

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    private static string Stylesheet()
        => Comment.Replace(
            File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-composer.css")),
            " ");

    /// <summary>The component files: the markup plus every partial of the code-behind.</summary>
    private static string Markup()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI"), "TmAiComposer.razor*")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

        return RazorComment.Replace(string.Concat(files), string.Empty);
    }

    private static HashSet<string> Emitted()
    {
        var fromAttributes = Regex.Matches(Markup(), @"class\s*=\s*[""']([^""']*)[""']")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        var fromLiterals = Regex.Matches(Markup(), @"""([^""]*tm-[^""]*)""")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        return fromAttributes
            .Concat(fromLiterals)
            .Where(name => name.StartsWith("tm-ai-composer", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EveryComposerClassTheMarkupEmits_HasARule()
    {
        var emitted = Emitted();
        var styled = CssClassSelector.Matches(Stylesheet())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        emitted.Should().NotBeEmpty("the composer emits its own BEM classes");
        emitted.Should().BeSubsetOf(styled,
            "a class the composer emits without a rule ships unstyled. Missing: {0}",
            string.Join(", ", emitted.Except(styled)));
    }

    [Fact]
    public void TheStylesheetIsImportedByThePackageEntryPoint()
    {
        var entry = File.ReadAllText(Path.Combine(Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css"));

        entry.Should().Contain("@import \"components/_ai-composer.css\"",
            "a host loads exactly one stylesheet, so an unimported partial never reaches the page");
    }

    [Fact]
    public void TheStylesheetDeclaresNoMotionOfItsOwn()
    {
        var css = Stylesheet();

        css.Should().NotContain("animation", "the composer does not animate — a list that faded in adds no information");
        css.Should().NotContain("transition", "a composer that animates would owe a reduced-motion block for no benefit");
    }

    [Fact]
    public void TheNarrowLayout_SticksTheComposer_withTheRuntimeKeyboardKnob()
    {
        var css = Stylesheet();

        css.Should().Contain("container-name: tm-ai-composer",
            "the composer is a size container so its descendants branch on its own width");
        css.Should().Contain("@media (width < 640px)",
            "640px is the shared Sm breakpoint; the ROOT is the sticky subject, and a container query cannot address its own container");

        var narrow = Regex.Match(css, @"@media \(width < 640px\)\s*\{(?<body>.*)", RegexOptions.Singleline)
            .Groups["body"].Value;
        narrow.Should().Contain("position: sticky", "the mobile composer rests on the fold instead of scrolling away");
        narrow.Should().Contain("--tm-ai-composer-keyboard-inset",
            "an iOS keyboard shrinks only the visual viewport — the sticky offset has to add its runtime knob");
    }

    [Fact]
    public void TheTextarea_DoesNotResizeNativelyAndGrowsOnlyToACap()
    {
        var css = Stylesheet();

        var input = Regex.Match(css, @"\.tm-ai-composer__input\s*\{([^}]*)\}").Groups[1].Value;
        input.Should().NotBeNullOrWhiteSpace("the prompt input is the component's main surface");
        input.Should().Contain("resize: none", "the module owns the autosize; a native grabber fights it");
        input.Should().Contain("max-block-size", "an unbounded autosize would push the send button off screen");
        css.Should().Contain(".tm-ai-composer__input:focus-visible", "the prompt needs a visible focus indicator");
    }

    [Fact]
    public void TheSurfacesAndControls_ReachTheTouchFloorOnACoarsePointer()
    {
        var css = Stylesheet();

        css.Should().Contain("@media (pointer: coarse)");
        var coarse = Regex.Match(css, @"@media \(pointer: coarse\)\s*\{(?<body>.*)", RegexOptions.Singleline)
            .Groups["body"].Value;
        coarse.Should().Contain("--tm-touch-target");
        coarse.Should().Contain("min-block-size: var(--tm-touch-target)");

        foreach (var selector in new[]
                 {
                     ".tm-ai-composer__option",
                     ".tm-ai-composer__attach",
                     ".tm-ai-composer__remove",
                 })
        {
            coarse.Should().Contain(selector, $"{selector} is a control a thumb has to reach");
        }
    }

    [Fact]
    public void TheForcedColorsBlock_KeepsTheControlsVisible()
    {
        var css = Stylesheet();

        var forced = Regex.Match(css, @"@media \(forced-colors: active\)\s*\{(?<body>.*)", RegexOptions.Singleline)
            .Groups["body"].Value;
        forced.Should().NotBeNullOrWhiteSpace("a forced palette drops the token colours — the controls need system colours");

        foreach (var selector in new[]
                 {
                     ".tm-ai-composer__input",
                     ".tm-ai-composer__option",
                     ".tm-ai-composer__attach",
                 })
        {
            forced.Should().Contain(selector);
        }

        forced.Should().Contain("Highlight", "the active suggestion is the only state a forced palette must still convey");
        forced.Should().Contain(".tm-ai-composer__voice[aria-pressed='true']",
            "the listening state is drawn by a token colour, which a forced palette drops");
        forced.Should().Contain("outline: 2px solid Highlight",
            "the running microphone needs a non-colour cue in a forced palette");
    }

    /// <summary>
    /// WCAG 1.4.1: "listening" may not be conveyed by the danger colour alone — a colour-blind reader (and a
    /// forced palette) would lose it. The pressed state therefore also draws a ring.
    /// </summary>
    [Fact]
    public void TheListeningVoiceControl_AddsANonColourCue()
    {
        var css = Stylesheet();

        var pressed = Regex.Match(css, @"\.tm-ai-composer__voice\[aria-pressed='true'\]\s*\{([^}]*)\}").Groups[1].Value;
        pressed.Should().NotBeNullOrWhiteSpace("the running microphone is the component's one pressed state");
        pressed.Should().Contain("box-shadow", "a ring survives a forced palette and a colour-blind reader");
        pressed.Should().Contain("currentColor", "the ring follows the control's own colour instead of a literal");
    }

    /// <summary>
    /// WCAG 2.5.8 consistency: the model select sits in the same toolbar row as the 2.75 rem actions, so it
    /// may not be a shorter target on a fine pointer.
    /// </summary>
    [Fact]
    public void TheToolbarRow_HasOneTargetHeight()
    {
        var css = Stylesheet();

        var model = Regex.Match(css, @"\.tm-ai-composer__model\s*\{([^}]*)\}").Groups[1].Value;
        model.Should().NotBeNullOrWhiteSpace("the built-in model picker is a toolbar control");
        model.Should().Contain("min-block-size: 2.75rem",
            "the picker must be as tall as the 2.75 rem actions, chips and textarea beside it");
    }

    [Fact]
    public void TheSuggestionList_IsPositionedFromTheCaretWithAFallback()
    {
        var css = Stylesheet();

        var suggestions = Regex.Match(css, @"\.tm-ai-composer__suggestions\s*\{([^}]*)\}").Groups[1].Value;
        suggestions.Should().NotBeNullOrWhiteSpace("the combobox listbox is the composer's popup");
        suggestions.Should().Contain("--tm-ai-composer-caret-block-end",
            "the module writes the caret's position; the stylesheet places the list");
        suggestions.Should().Contain("max-block-size", "a long list scrolls instead of covering the prompt");
    }
}
