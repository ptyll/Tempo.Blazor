using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan b75365fb fáze 4e (remaining e1872ed0, decision <c>AI-PACKAGE-DISABLED-IDIOM</c> extended to the
/// core): the core <c>.tm-btn</c> speaks the SAME disabled idiom as the AI package — the
/// <c>--tm-opacity-disabled</c> token and <c>cursor: not-allowed</c> — and a disabled
/// <c>&lt;button class="tm-btn"&gt;</c> no longer carries <c>pointer-events: none</c>.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE BUTTON LOSES <c>pointer-events: none</c>: it swallowed the rule's own
/// <c>cursor: not-allowed</c> (the element never saw a hover, so the cursor never showed), blocked host
/// <c>title</c> tooltips, <c>mouseenter</c> handlers and <c>elementFromPoint</c> hit-testing, and let a
/// click fall THROUGH the button onto whatever sits under it. The native <c>disabled</c> button already
/// ignores activation, and every variant hover guard (<c>:hover:not(:disabled)</c>) keeps the fill from
/// lighting up — the E2E half of this gate measures both in a real browser.
/// </para>
/// <para>
/// WHY THE ANCHOR KEEPS IT: <c>&lt;a class="tm-btn" disabled&gt;</c> has no native disabled — nothing but
/// <c>pointer-events: none</c> stops it from navigating, so the anchor form keeps the property.
/// </para>
/// </remarks>
public sealed class ButtonDisabledIdiomTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex NotGuard =
        new(@":not\([^)]*\)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex LiteralOpacity =
        new(@"(?<![-\w])opacity:\s*(?<value>[\d.]+)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex PointerEventsNone =
        new(@"pointer-events\s*:\s*none", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ForcedColorsBlock =
        new(@"@media\s*\(\s*forced-colors\s*:\s*active\s*\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}",
            RegexOptions.Compiled, RegexTimeout);

    private static readonly IReadOnlySet<string> Disabled = new HashSet<string> { ":disabled" };

    [Fact]
    public void DisabledButton_ResolvesTheSharedDisabledIdiom()
    {
        // The forced-colors block is measured by its own gate below; the cascade probe reads the
        // normal-mode sheet (the probe cannot decide a forced-colors condition, and an undecidable
        // MAYBE-winner would fail the fail-closed Unmodelled check).
        var css = WithoutForcedColorsBlocks(ThemeCss.BundledCss());
        var chain = new[] { new CssCascade.Element("button", "tm-btn", "tm-btn-primary") };

        CssCascade.Winning(css, chain, "opacity", Disabled).Should().Be(
            "var(--tm-opacity-disabled)",
            "a disabled .tm-btn dims through the shared token (decision AI-PACKAGE-DISABLED-IDIOM), "
            + "not through a literal alpha — the literal is exactly how the core button drifted from "
            + "the AI package in the first place");

        CssCascade.Winning(css, chain, "cursor", Disabled).Should().Be(
            "not-allowed",
            "the shared idiom pairs the dim with the not-allowed cursor");

        var pointerEvents = CssCascade.Resolve(css, chain, "pointer-events", Disabled);
        pointerEvents.Unmodelled.Should().BeEmpty(
            "a selector the probe cannot read is UNMEASURABLE, never 'fine'");
        pointerEvents.Value.Should().NotBe(
            "none",
            "pointer-events: none on the BUTTON swallowed its own not-allowed cursor (the element "
            + "never saw a hover), blocked host title tooltips and mouseenter handlers, and let a "
            + "click fall through onto the element UNDER the button — the native disabled attribute "
            + "already refuses activation, so the property must go");
    }

    /// <summary>
    /// The anchor form (<c>&lt;a class="tm-btn" disabled&gt;</c>) has no native disabled — without
    /// <c>pointer-events: none</c> nothing stops it from navigating, so the property STAYS there.
    /// Attribute selectors on the subject are a decided non-match for <see cref="CssCascade"/>, so
    /// this gate reads the rule declarations of the component stylesheet directly.
    /// </summary>
    [Fact]
    public void DisabledAnchor_KeepsPointerEventsNone()
    {
        ThemeCss.TryProperty("_button.css", "a.tm-btn[disabled]", "pointer-events").Should().Be(
            "none",
            "an anchor has no native disabled — pointer-events: none is the ONLY thing that keeps a "
            + "disabled .tm-btn link from navigating");

        ThemeCss.TryProperty("_button.css", "a.tm-btn[aria-disabled=\"true\"]", "pointer-events")
            .Should().Be(
                "none",
                "the aria-disabled anchor form is just as activatable without the property");
    }

    /// <summary>
    /// aria-disabled on a <c>&lt;button&gt;</c> is COMPONENT-OWNED (fáze 4e review, variant A): the
    /// component keeps the button focusable (WCAG 2.4.3), refuses re-entry in its own handler and
    /// paints its own disabled look — the library's own aria-disabled buttons
    /// (<c>.tm-comment-submit-btn</c>, the TmEmailTemplateEditor Save, the TmPromptLibrary Use) all
    /// carry their own rules. The base rule therefore names the state on the ANCHOR form only:
    /// <c>&lt;a class="tm-btn" aria-disabled="true"&gt;</c> has no component owner and no native
    /// disabled, so the shared rule gives it the token dim and the not-allowed cursor. (A base-rule
    /// dim on the button would also sit next to a live hover fill — aria-disabled is not
    /// <c>:disabled</c>, so every <c>:hover:not(:disabled)</c> guard still matches it.)
    /// </summary>
    [Fact]
    public void AriaDisabledButton_LookIsComponentOwned_OnlyTheAnchorSpeaksTheBaseIdiom()
    {
        ThemeCss.TryProperty("_button.css", ".tm-btn[aria-disabled=\"true\"]", "opacity").Should().BeNull(
            "a <button class=\"tm-btn\" aria-disabled> is focusable and component-owned — the base "
            + "rule must NOT dim it (the component paints its own look); naming it here is exactly "
            + "the mutant this gate fails on");

        ThemeCss.TryProperty("_button.css", ".tm-btn[aria-disabled=\"true\"]", "cursor").Should().BeNull(
            "the cursor of an aria-disabled BUTTON is the component's decision too");

        ThemeCss.TryProperty("_button.css", "a.tm-btn[aria-disabled=\"true\"]", "opacity").Should().Be(
            "var(--tm-opacity-disabled)",
            "an aria-disabled ANCHOR has no component owner — the base rule dims it through the "
            + "shared token");

        ThemeCss.TryProperty("_button.css", "a.tm-btn[aria-disabled=\"true\"]", "cursor").Should().Be(
            "not-allowed",
            "the aria-disabled anchor shows the same not-allowed cursor");
    }

    /// <summary>
    /// Forced colors (Windows high contrast): the UA forces its own palette, so the shared dim would
    /// only lower the contrast the mode guarantees — the system <c>GrayText</c> carries the disabled
    /// state at FULL opacity instead. The gate pins the block's existence, its selector set and all
    /// three declarations; the E2E half measures the computed colour against a
    /// <c>span{color:GrayText}</c> probe in a real browser.
    /// </summary>
    [Fact]
    public void DisabledButton_ForcedColors_PaintsGrayTextAtFullOpacity()
    {
        var css = ThemeCss.ComponentCss("_button.css");
        var block = ForcedColorsBlock.Match(css);

        block.Success.Should().BeTrue(
            "_button.css must ship a forced-colors block for the disabled idiom — without it a "
            + "disabled button in Windows high contrast reads exactly like an enabled one");

        var body = block.Groups["body"].Value;
        foreach (var selector in new[] { ".tm-btn:disabled", ".tm-btn[disabled]", "a.tm-btn[aria-disabled=\"true\"]" })
        {
            body.Should().Contain(selector,
                "the forced-colors block must name every subject of the base disabled rule");
        }

        body.Should().Contain("color: GrayText",
            "the system disabled ink carries the state in forced colors");
        body.Should().Contain("border-color: GrayText",
            "the border follows the ink, or a forced-colors button keeps a full-contrast frame");
        body.Should().Contain("opacity: 1",
            "a dim over a system colour would only lower the contrast the mode itself guarantees");
    }

    /// <summary>
    /// Fáze 4e ratchet (pointer events): <c>pointer-events: none</c> on a DISABLED <c>.tm-btn</c> rule
    /// is legitimate ONLY on the anchor form — a <c>&lt;button&gt;</c> or any other element carrying it
    /// is transparent to the pointer (swallowed cursor, dead tooltips, click-through). The sweep reads
    /// every core component stylesheet and fails when a rule that declares <c>pointer-events: none</c>
    /// has a selector part naming a <c>.tm-btn</c> class together with a disabled marker
    /// (<c>:disabled</c>, <c>[disabled]</c>, <c>[aria-disabled…</c>) that does NOT start with
    /// <c>a.tm-btn</c>. Mutants: <c>.tm-btn[disabled]{pointer-events:none}</c> and
    /// <c>button.tm-btn:disabled{pointer-events:none}</c> both turn this RED.
    /// </summary>
    [Fact]
    public void CoreCss_TmBtnDisabledPointerEventsNone_OnlyOnAnchors()
    {
        var offences = new List<string>();
        foreach (var (file, css) in CoreCss())
        {
            foreach (Match rule in RuleBlock.Matches(WithoutForcedColorsBlocks(css)))
            {
                var body = ThemeCss.Normalise(rule.Groups["body"].Value);
                if (!PointerEventsNone.IsMatch(body))
                {
                    continue;
                }

                var disabledBtnParts = ThemeCss.SelectorParts(rule.Groups["selector"].Value)
                    .Select(selector => NotGuard.Replace(selector.Replace('\'', '"'), string.Empty))
                    .Where(selector => selector.Contains(".tm-btn", StringComparison.Ordinal)
                        && (selector.Contains(":disabled", StringComparison.Ordinal)
                            || selector.Contains("[disabled]", StringComparison.Ordinal)
                            || selector.Contains("[aria-disabled", StringComparison.Ordinal)))
                    .ToList();

                foreach (var part in disabledBtnParts.Where(
                             part => !part.StartsWith("a.tm-btn", StringComparison.Ordinal)))
                {
                    offences.Add(
                        $"{file}: '{part}' declares pointer-events: none — on a disabled .tm-btn only "
                        + "the ANCHOR form may keep the property (a button has the native disabled "
                        + "attribute; the property would swallow the not-allowed cursor, block host "
                        + "tooltips and let clicks fall through)");
                }
            }
        }

        offences.Should().BeEmpty(
            "pointer-events: none on a disabled .tm-btn is the pre-fáze-4e defect this phase removed; "
            + "only a.tm-btn[disabled] / a.tm-btn[aria-disabled=\"true\"] may carry it. Offences:\n"
            + string.Join("\n", offences));
    }

    /// <summary>
    /// Fáze 4e ratchet: no DISABLED rule of the core stylesheets that names a <c>.tm-btn*</c> class may
    /// dim with a LITERAL alpha or keep <c>cursor: default</c> — that pair is how the core button
    /// drifted from the shared idiom. Two deliberate exemptions: <c>opacity: 1</c> is not a dim but the
    /// documented "no veil, legible ink tokens instead" alternative (the dashboard dialogs use it), and
    /// the <c>@media (forced-colors: active)</c> blocks are exempt because there the system GrayText
    /// carries the state and <c>opacity: 1</c> protects its contrast. <c>:not(…)</c> guards are
    /// stripped before the markers are looked for, so a <c>:hover:not(:disabled)</c> selector is an
    /// ENABLED-state rule and never trips the ratchet. The sweep covers <c>_button.css</c> AND every
    /// <c>.tm-btn</c> disabled rule in the other core stylesheets; a repo-wide ratchet over the ~40
    /// remaining literal-alpha disabled rules is deliberately NOT introduced here (tracked as a backlog
    /// tech-debt item instead).
    /// </summary>
    [Fact]
    public void CoreCss_TmBtnDisabledRules_SpeakTheSharedDisabledIdiom()
    {
        var offences = new List<string>();
        foreach (var (file, css) in CoreCss())
        {
            foreach (Match rule in RuleBlock.Matches(WithoutForcedColorsBlocks(css)))
            {
                var disabledSelectors = ThemeCss.SelectorParts(rule.Groups["selector"].Value)
                    .Select(selector => NotGuard.Replace(selector.Replace('\'', '"'), string.Empty))
                    .Where(selector => selector.Contains(".tm-btn", StringComparison.Ordinal)
                        && (selector.Contains(":disabled", StringComparison.Ordinal)
                            || selector.Contains("[disabled]", StringComparison.Ordinal)
                            || selector.Contains("[aria-disabled=\"true\"]", StringComparison.Ordinal)))
                    .ToList();
                if (disabledSelectors.Count == 0)
                {
                    continue;
                }

                var body = ThemeCss.Normalise(rule.Groups["body"].Value);
                if (body.Contains("cursor: default", StringComparison.Ordinal))
                {
                    offences.Add($"{file}: {string.Join(", ", disabledSelectors)} keeps cursor: default");
                }

                foreach (Match literal in LiteralOpacity.Matches(body))
                {
                    var value = double.Parse(
                        literal.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (value != 1.0)
                    {
                        offences.Add(
                            $"{file}: {string.Join(", ", disabledSelectors)} dims with the literal "
                            + $"opacity {literal.Groups["value"].Value} — the shared idiom dims through "
                            + "--tm-opacity-disabled (opacity: 1 is the documented undim alternative)");
                    }
                }
            }
        }

        offences.Should().BeEmpty(
            "every disabled rule that names a .tm-btn class speaks the shared idiom (decision "
            + "AI-PACKAGE-DISABLED-IDIOM extended to the core by fáze 4e): --tm-opacity-disabled (or the "
            + "documented opacity: 1 undim with ink tokens) and cursor: not-allowed. Offences:\n"
            + string.Join("\n", offences));
    }

    /// <summary>
    /// The stylesheet with every <c>@media (forced-colors: active)</c> block removed (matched by brace
    /// depth, so nested rules go with it): the forced-colors idiom (<c>GrayText</c> + <c>opacity: 1</c>)
    /// is legitimate there and must not trip the disabled-idiom ratchet. The marker is whitespace-
    /// tolerant because the committed bundle minifies the condition to <c>(forced-colors:active)</c>.
    /// </summary>
    private static string WithoutForcedColorsBlocks(string css)
    {
        var marker = new Regex(
            @"@media\s*\(\s*forced-colors\s*:\s*active\s*\)", RegexOptions.None, RegexTimeout);
        var result = css;
        var match = marker.Match(result);
        while (match.Success)
        {
            var start = match.Index;
            var opened = result.IndexOf('{', start);
            var end = result.Length - 1;
            var depth = 0;
            for (var index = opened; index < result.Length; index++)
            {
                depth += result[index] switch { '{' => 1, '}' => -1, _ => 0 };
                if (depth == 0)
                {
                    end = index;
                    break;
                }
            }

            result = result[..start] + " " + result[(end + 1)..];
            match = marker.Match(result);
        }

        return result;
    }

    /// <summary>Every core component stylesheet, keyed by repo-relative path (bin/obj excluded).</summary>
    private static IReadOnlyList<(string File, string Css)> CoreCss()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var directory = ThemeCss.CssPath("components");
        Directory.Exists(directory).Should().BeTrue("the core component stylesheets must exist");

        return Directory
            .EnumerateFiles(directory, "*.css", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                ThemeCss.StripComments(File.ReadAllText(path))))
            .ToList();
    }
}
