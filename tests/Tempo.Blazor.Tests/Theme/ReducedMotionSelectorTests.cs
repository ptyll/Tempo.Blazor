using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards the narrowed <c>@media (prefers-reduced-motion: reduce)</c> block in
/// <c>animations.css</c> — application gap register #9. The block used to target
/// <c>*, *::before, *::after</c>, which reached elements the library does not own and, because it
/// sets only <em>durations</em> while <c>transition-property</c> stays <c>all</c>, would have
/// manufactured 0.01ms transitions on every property of every element the day anyone raised the
/// value — more motion for the group that asked for less. The selector is now an explicit
/// enumeration of Tempo-owned animated subjects, and this test recomputes that set from the
/// sources so the list can never silently go stale: a transition added to an unlisted subject
/// fails here, and so does an entry whose class no longer animates.
/// </summary>
public sealed class ReducedMotionSelectorTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A declaration of <c>transition</c>/<c>transition-*</c> or <c>animation</c>/<c>animation-*</c>
    /// — matched on the property NAME so the <c>--tm-transition-*</c> custom properties in
    /// <c>tokens.css</c> (values only, never animated) cannot false-positive the scan.
    /// </summary>
    private static readonly Regex MotionDeclaration =
        new(@"(^|;)\s*(transition(-[a-z-]+)?|animation(-[a-z-]+)?)\s*:",
            RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}@]+)\{(?<body>[^{}]*)\}",
            RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// The selector list of the reduced-motion rule, normalised. The block is one rule inside one
    /// media query: everything between the media query's first <c>{</c> and the first <c>{</c>
    /// after it is the selector.
    /// </summary>
    private static IReadOnlyList<string> ReducedMotionSelectors(string path)
    {
        var css = ThemeCss.StripComments(File.ReadAllText(path));
        var mediaStart = css.IndexOf("prefers-reduced-motion", StringComparison.Ordinal);
        mediaStart.Should().BeGreaterThanOrEqualTo(0, "{0} must keep a prefers-reduced-motion block", path);

        var blockOpen = css.IndexOf('{', mediaStart);
        var selectorEnd = css.IndexOf('{', blockOpen + 1);
        blockOpen.Should().BeGreaterThanOrEqualTo(0);
        selectorEnd.Should().BeGreaterThanOrEqualTo(blockOpen, "the media block must contain a rule");

        return css[(blockOpen + 1)..selectorEnd]
            .Split(',')
            .Select(ThemeCss.Normalise)
            .Where(part => part.Length > 0)
            .ToList();
    }

    /// <summary>
    /// The population this block may target: every Tempo-owned subject that carries a
    /// <c>transition</c>/<c>animation</c> declaration in the core library's stylesheets — shared
    /// <c>wwwroot/css</c> sheets and scoped <c>*.razor.css</c> alike (a scoped selector still
    /// compiles to the same class names). Subject classes become <c>.tm-x</c>; an animated
    /// pseudo-element becomes <c>.tm-x::before|::after</c>; an animated element that carries no
    /// class of its own keeps its full descendant selector (e.g. <c>.tm-chat__typing-dots span</c>)
    /// because the ancestor class alone cannot reach it.
    /// </summary>
    private static SortedSet<string> AnimatedSubjects()
    {
        var root = Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor");

        var files = Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories)
            .Where(file =>
            {
                var normalized = file.Replace('\\', '/');
                return !normalized.Contains("/obj/")
                       && !normalized.Contains("/bin/")
                       && !normalized.EndsWith("tempo-blazor.bundled.css", StringComparison.Ordinal);
            });

        var selectors = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var text = ThemeCss.StripComments(File.ReadAllText(file));
            foreach (Match rule in RuleBlock.Matches(text))
            {
                if (!MotionDeclaration.IsMatch(rule.Groups["body"].Value))
                {
                    continue;
                }

                foreach (var part in rule.Groups["selector"].Value.Split(','))
                {
                    var trimmed = ThemeCss.Normalise(part);
                    if (trimmed.Length == 0 || trimmed.StartsWith('@'))
                    {
                        continue;
                    }

                    // The animated box is the SUBJECT — the compound after the last combinator.
                    var subject = Regex.Split(trimmed, @"[\s>+~]+").Last();

                    var subjectClasses = Regex.Matches(subject, @"\.(tm-[a-zA-Z0-9_-]+)")
                        .Select(m => m.Groups[1].Value)
                        .ToList();

                    if (subjectClasses.Count == 0)
                    {
                        // Keyframe steps (from/to/%) are not element selectors; an element subject
                        // under a Tempo ancestor keeps its full selector; anything else is a rule
                        // animating markup the library does not own — a defect of its own.
                        if (trimmed.Contains(".tm-", StringComparison.Ordinal))
                        {
                            selectors.Add(trimmed);
                        }

                        continue;
                    }

                    foreach (var cls in subjectClasses)
                    {
                        selectors.Add("." + cls);
                    }

                    foreach (Match pseudo in Regex.Matches(subject, @"::(before|after)"))
                    {
                        selectors.Add("." + subjectClasses[^1] + "::" + pseudo.Groups[1].Value);
                    }
                }
            }
        }

        return selectors;
    }

    [Fact]
    public void ReducedMotionBlock_TargetsNoUniversalSelector()
    {
        foreach (var selector in ReducedMotionSelectors(ThemeCss.CssPath("animations.css")))
        {
            selector.Should().NotContain("*",
                "univerzální selektor sahá na prvky, které knihovna nevlastní — blok má vyjmenovat " +
                "pouze vlastní animované třídy Tempa (gap register #9)");
        }
    }

    [Fact]
    public void ReducedMotionBlock_EnumeratesExactlyTheAnimatedSubjects()
    {
        var expected = AnimatedSubjects();
        var actual = ReducedMotionSelectors(ThemeCss.CssPath("animations.css"))
            .ToHashSet(StringComparer.Ordinal);

        expected.Should().NotBeEmpty("jmenovatel je reálný — sweep musí číst stovky animovaných tříd");
        actual.Should().BeEquivalentTo(expected,
            "seznam v animations.css musí být přesně množina Tempo subjektů s transition/animation — " +
            "jinak buď sahá mimo knihovnu, nebo nová animace zůstane bez reduced-motion krytí. " +
            "Chybějící: {0}; navíc: {1}",
            string.Join(", ", expected.Except(actual)),
            string.Join(", ", actual.Except(expected)));
    }

    [Fact]
    public void BundledCss_CarriesTheSameNarrowedBlock()
    {
        var bundleSelectors = ReducedMotionSelectors(ThemeCss.CssPath("tempo-blazor.bundled.css"));

        bundleSelectors.Should().NotBeEmpty("bundle musí blok nést — jinak se zúžení nedoručilo");
        bundleSelectors.Should().OnlyContain(
            selector => !selector.Contains('*'),
            "the consumer-facing bundle must not resurrect the universal selector");
        bundleSelectors.Should().Contain(".tm-btn",
            "the enumeration must survive the bundler — a stripped list means no reduced motion at all");
    }
}
