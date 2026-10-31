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

    /// <summary>A rule inside a reduced-motion block only counts as coverage when it actually
    /// suppresses motion: it sets a <c>transition</c>/<c>animation</c> property or
    /// <c>transform: none</c>. A rule that merely restyles (opacity, colours) without touching
    /// motion does not cover its subjects.</summary>
    private static readonly Regex TransformNone =
        new(@"(^|;)\s*transform\s*:\s*none",
            RegexOptions.Compiled, RegexTimeout);

    /// <summary>Only a <c>reduce</c> preference is coverage — a <c>no-preference</c> or
    /// <c>update: slow</c> block must not count (and must not be mistaken for the guard).</summary>
    private static readonly Regex ReducedMotionMedia =
        new(@"prefers-reduced-motion\s*:\s*reduce",
            RegexOptions.Compiled, RegexTimeout);

    /// <summary>A selector part inside a package reduced-motion block must be a REAL selector: a
    /// Tempo class compound (optionally carrying an attribute selector, a pseudo-class such as
    /// <c>:hover</c>/<c>:not(...)</c>, or a <c>::before/::after</c> pseudo-element), or such a
    /// compound followed by a descendant/combinator tail (e.g. <c>.tm-diagram-edge-toolbar
    /// button</c>). The degenerate bare <c>.</c> a zero-subject generator emitted into several
    /// package blocks (cleanup round f3-review) is exactly what this rejects, along with
    /// universal (<c>*</c>) and bare-element selectors.</summary>
    private static readonly Regex ValidReducedMotionSelector =
        new(@"^\.[a-zA-Z][a-zA-Z0-9_-]*(\[[^\]]*\])?((::|:)[a-zA-Z-]+(\([^()]*\))?)*$" +
            @"|^\.[a-zA-Z][a-zA-Z0-9_-]*(\[[^\]]*\])?((::|:)[a-zA-Z-]+(\([^()]*\))?)*[ >+~]\s*[^{}*]+$",
            RegexOptions.Compiled, RegexTimeout);

    /// <summary>Build directories that hold shipped/third-party CSS, never library-owned subjects.</summary>
    private static bool IsShippedOrVendorCss(string normalizedPath) =>
        normalizedPath.Contains("/obj/")
        || normalizedPath.Contains("/bin/")
        || normalizedPath.Contains("/node_modules/")
        || normalizedPath.Contains("/lib/")
        || normalizedPath.Contains("/leaflet/")
        || normalizedPath.EndsWith("tempo-blazor.bundled.css", StringComparison.Ordinal);

    /// <summary>
    /// The subject classes of one (already normalised) selector part — the compound after the last
    /// combinator. Subject classes become <c>.tm-x</c>; an animated pseudo-element becomes
    /// <c>.tm-x::before|::after</c>; an animated element that carries no class of its own keeps its
    /// full descendant selector (e.g. <c>.tm-chat__typing-dots span</c>) because the ancestor class
    /// alone cannot reach it. A part with NO <c>.tm-*</c> class anywhere yields nothing.
    /// </summary>
    private static IEnumerable<string> SubjectsOf(string trimmed)
    {
        if (trimmed.Length == 0 || trimmed.StartsWith('@'))
        {
            yield break;
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
                yield return trimmed;
            }

            yield break;
        }

        foreach (var cls in subjectClasses)
        {
            yield return "." + cls;
        }

        foreach (Match pseudo in Regex.Matches(subject, @"::(before|after)"))
        {
            yield return "." + subjectClasses[^1] + "::" + pseudo.Groups[1].Value;
        }
    }

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
    /// LIMIT (N158): an animated rule whose selector carries NO <c>.tm-*</c> class anywhere in the
    /// chain (a bare element or a foreign class) is excluded from the population entirely — safe
    /// under the library convention (every owned selector bears a <c>tm-</c> prefix, enforced by
    /// <c>MarkupClassCoverageTests</c>/the ownership suite), but a convention breach (a
    /// bare-element <c>transition</c> with no <c>tm-</c> ancestor) would escape BOTH the
    /// reduced-motion block in <c>animations.css</c> AND this guard.
    /// </summary>
    private static SortedSet<string> AnimatedSubjects(string packageDir)
    {
        var files = Directory.EnumerateFiles(packageDir, "*.css", SearchOption.AllDirectories)
            .Where(file => !IsShippedOrVendorCss(file.Replace('\\', '/')));

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
                    foreach (var subject in SubjectsOf(ThemeCss.Normalise(part)))
                    {
                        selectors.Add(subject);
                    }
                }
            }
        }

        return selectors;
    }

    /// <summary>
    /// Every Tempo-owned subject reachable from a <c>@media (prefers-reduced-motion: reduce)</c>
    /// block in the package's stylesheets — all reduce blocks, all RULES THAT ACTUALLY SUPPRESS
    /// MOTION (a <c>transition</c>/<c>animation</c> declaration or <c>transform: none</c>), all
    /// selector parts. The source of truth for "what the reduced-motion coverage claims".
    /// </summary>
    private static SortedSet<string> CoveredSubjects(string packageDir)
    {
        var covered = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(packageDir, "*.css", SearchOption.AllDirectories)
            .Where(file => !IsShippedOrVendorCss(file.Replace('\\', '/'))))
        {
            var text = ThemeCss.StripComments(File.ReadAllText(file));
            var searchFrom = 0;
            while (true)
            {
                var media = ReducedMotionMedia.Match(text, searchFrom);
                if (!media.Success)
                {
                    break;
                }

                // Brace-count the media block: the first '{' opens it, its matching '}' closes it.
                var open = text.IndexOf('{', media.Index);
                open.Should().BeGreaterThanOrEqualTo(0, $"{file} must open the media block");
                var depth = 1;
                var i = open + 1;
                for (; i < text.Length && depth > 0; i++)
                {
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}') depth--;
                }
                depth.Should().Be(0, $"{file} media block must be balanced");
                var body = text[(open + 1)..(i - 1)];

                foreach (Match rule in RuleBlock.Matches(body))
                {
                    // A rule counts only when it suppresses motion itself.
                    if (!MotionDeclaration.IsMatch(rule.Groups["body"].Value)
                        && !TransformNone.IsMatch(rule.Groups["body"].Value))
                    {
                        continue;
                    }

                    foreach (var part in rule.Groups["selector"].Value.Split(','))
                    {
                        foreach (var subject in SubjectsOf(ThemeCss.Normalise(part)))
                        {
                            covered.Add(subject);
                        }
                    }
                }

                searchFrom = i;
            }
        }

        return covered;
    }

    /// <summary>
    /// Every RAW selector part of every rule inside every <c>@media (prefers-reduced-motion:
    /// reduce)</c> block in the package's stylesheets — before subject reduction. The guard for
    /// "the block enumerates real selectors": a zero-subject package used to carry a degenerate
    /// bare-<c>.</c> block that the subject-based coverage sweep could not see (it yields no
    /// subjects), so coverage alone cannot reject it.
    /// </summary>
    private static IEnumerable<string> ReducedMotionSelectorParts(string packageDir)
    {
        foreach (var file in Directory.EnumerateFiles(packageDir, "*.css", SearchOption.AllDirectories)
            .Where(file => !IsShippedOrVendorCss(file.Replace('\\', '/'))))
        {
            var text = ThemeCss.StripComments(File.ReadAllText(file));
            var searchFrom = 0;
            while (true)
            {
                var media = ReducedMotionMedia.Match(text, searchFrom);
                if (!media.Success)
                {
                    break;
                }

                var open = text.IndexOf('{', media.Index);
                open.Should().BeGreaterThanOrEqualTo(0, $"{file} must open the media block");
                var depth = 1;
                var i = open + 1;
                for (; i < text.Length && depth > 0; i++)
                {
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}') depth--;
                }
                depth.Should().Be(0, $"{file} media block must be balanced");
                var body = text[(open + 1)..(i - 1)];

                foreach (Match rule in RuleBlock.Matches(body))
                {
                    foreach (var part in rule.Groups["selector"].Value.Split(','))
                    {
                        var normalised = ThemeCss.Normalise(part);
                        if (normalised.Length > 0)
                        {
                            yield return normalised;
                        }
                    }
                }

                searchFrom = i;
            }
        }
    }

    /// <summary>Split packages whose uncovered animated subjects are frozen in the baseline file.</summary>
    public static TheoryData<string> SplitPackagesEnforced => new()
    {
        "Tempo.Blazor.AI",
        "Tempo.Blazor.DiagramEditor",
        "Tempo.Blazor.DocumentEditor",
        "Tempo.Blazor.EmailTemplates",
        "Tempo.Blazor.NotionEditor",
        "Tempo.Blazor.PdfViewer",
        "Tempo.Blazor.Reporting",
        "Tempo.Blazor.Spreadsheet",
        "Tempo.Blazor.Wireframe",
    };

    /// <summary>
    /// Frozen debt (2026-10-18): split packages with tm- motion and no full reduced-motion
    /// coverage. The list may only SHRINK — move a package to <see cref="SplitPackagesEnforced"/>
    /// when it gets its block, and seed its uncovered-subject entry in
    /// <see cref="UncoveredBaselineFile"/> with the then-current debt (which may only shrink).
    /// Cleanup round (f3fa78d9): every package now carries its own explicit enumeration block,
    /// so the frozen debt list is empty — it stays as the sentinel a new package must not join.
    /// </summary>
    private static readonly string[] FrozenReducedMotionDebt = [];

    /// <summary>
    /// Frozen uncovered-subject baseline, per package. Each entry is a subject the package's
    /// reduced-motion blocks may leave uncovered; the file may only SHRINK — covering a subject
    /// means deleting it from the list (a subject that is neither animated nor uncovered is not
    /// listed). The file lives next to this test so the debt is explicit and reviewable.
    /// </summary>
    private const string UncoveredBaselineFile = "ReducedMotionUncoveredBaseline.json";

    private static Dictionary<string, string[]> ReadUncoveredBaseline()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Theme", UncoveredBaselineFile));
        File.Exists(path).Should().BeTrue($"the uncovered-subject baseline must exist: {path}");
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            File.ReadAllText(path))!;
    }

    [Theory]
    [MemberData(nameof(SplitPackagesEnforced))]
    public void SplitPackage_UncoveredAnimatedSubjects_AreOnlyTheFrozenBaseline(string package)
    {
        var dir = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", package);
        var expected = AnimatedSubjects(dir);
        var uncovered = expected.Except(CoveredSubjects(dir)).ToList();

        var baseline = ReadUncoveredBaseline();
        baseline.Should().ContainKey(package,
            $"{package} must have a frozen uncovered-subject baseline entry ({UncoveredBaselineFile})");

        // No NEW uncovered subject beyond the frozen baseline…
        uncovered.Should().BeSubsetOf(baseline[package],
            "a subject with transition/animation must be reachable by a reduced-motion rule. " +
            "Chybějící: {0}", string.Join(", ", uncovered.Except(baseline[package])));

        // …and the baseline may only SHRINK: an entry that is no longer uncovered must be
        // deleted from the file, so the debt is always visible at its true size.
        baseline[package].Should().BeSubsetOf(uncovered,
            "covered subjects must be pruned from the frozen baseline — it may only shrink. " +
            "Pokryté: {0}", string.Join(", ", baseline[package].Except(uncovered)));

        expected.Should().NotBeEmpty();
        CoveredSubjects(dir).Should().OnlyContain(s => !s.Contains('*'));
    }

    [Fact]
    public void SplitPackages_WithUncoveredMotion_AreOnlyTheFrozenDebt()
    {
        var src = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src");
        var failing = Directory.EnumerateDirectories(src, "Tempo.*")
            .Select(Path.GetFileName)
            .Where(p => p != "Tempo.Blazor" && !p!.Contains(".Demo", StringComparison.Ordinal))
            .Where(p => AnimatedSubjects(Path.Combine(src, p!)).Except(CoveredSubjects(Path.Combine(src, p!))).Any())
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        failing.Should().BeEquivalentTo(FrozenReducedMotionDebt,
            "a NEW package with uncovered motion must not appear, and a fixed package must leave the frozen list");
    }

    [Theory]
    [MemberData(nameof(SplitPackagesEnforced))]
    public void SplitPackage_ReducedMotionSelectors_AreValidClassOrAttributeSelectors(string package)
    {
        var dir = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", package);
        var parts = ReducedMotionSelectorParts(dir).ToList();

        parts.Should().NotBeEmpty($"{package} carries reduced-motion blocks — the sweep must see them");
        parts.Should().OnlyContain(part => ValidReducedMotionSelector.IsMatch(part),
            "every selector inside a package reduced-motion block must be a real class/attribute " +
            "selector — a bare '.', a universal '*' or a bare-element selector reaches markup the " +
            "library does not own (cleanup round f3-review: three PdfViewer files carried a " +
            "degenerate bare-'.' block). Neplatné: {0}",
            string.Join(", ", parts.Where(part => !ValidReducedMotionSelector.IsMatch(part))));
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
        var coreDir = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor");
        var expected = AnimatedSubjects(coreDir);
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
