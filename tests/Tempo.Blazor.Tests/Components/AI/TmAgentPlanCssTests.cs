using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Hygiene gate for the agent-plan stylesheet (<c>_ai-agent-plan.css</c>, plan 3e96f6d8 phase 2): every
/// class the markup emits has a rule, the row actions are finger targets on a coarse pointer, the state is
/// never colour only, the forced-colors block restores the system focus ring on every focus stop, and the
/// plan adds no motion of its own.
/// </summary>
/// <remarks>
/// The package-wide token rules (no colour literal, no raw palette step, no <c>[data-theme]</c> block,
/// every <c>var(--tm-*)</c> resolvable) are enforced for every stylesheet of the package by
/// <c>AiMarkdownViewCssTests</c>.
/// </remarks>
public sealed class TmAgentPlanCssTests
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

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    private static string RawStylesheet()
        => File.ReadAllText(Path.Combine(
            Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-agent-plan.css"));

    private static string Stylesheet() => Comment.Replace(RawStylesheet(), " ");

    /// <summary>The component markup plus its code-behind (the class names live in both).</summary>
    private static string Markup()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI"),
                "TmAgentPlan.razor*")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

        return RazorComment.Replace(string.Concat(files), string.Empty);
    }

    /// <summary>The <c>tm-ai-agent-plan*</c> classes the markup emits, stripped of their interpolated tails.</summary>
    private static HashSet<string> Emitted()
    {
        var fromAttributes = Regex.Matches(Markup(), @"class\s*=\s*[""']([^""']*)[""']")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        var fromLiterals = Regex.Matches(Markup(), @"""([^""]*tm-ai-agent-plan[^""]*)""")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.Split('@', '{')[0].TrimEnd('-'));

        return fromAttributes
            .Concat(fromLiterals)
            .Where(name => name.StartsWith("tm-ai-agent-plan", StringComparison.Ordinal))
            .Where(name => !name.Contains("--", StringComparison.Ordinal) || name.StartsWith("tm-ai-agent-plan--", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Styled()
        => CssClassSelector.Matches(Stylesheet())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryClassTheMarkupEmits_HasARule()
    {
        var emitted = Emitted();
        var styled = Styled();

        emitted.Should().NotBeEmpty();
        emitted.Should().BeSubsetOf(styled,
            "a class the plan emits without a rule ships unstyled. Missing: {0}",
            string.Join(", ", emitted.Except(styled)));
    }

    [Fact]
    public void TheStylesheetIsImportedByThePackageEntryPoint()
    {
        var entry = File.ReadAllText(Path.Combine(
            Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css"));

        entry.Should().Contain("@import \"components/_ai-agent-plan.css\";");
    }

    [Fact]
    public void TheRowActionsReachTheTouchTargetOnACoarsePointer()
    {
        var css = Stylesheet();
        var coarse = Block(css, "@media (pointer: coarse)");

        coarse.Should().Contain("--tm-touch-target",
            "every row action must be a 44 px finger target on a touch device");
        coarse.Should().MatchRegex(@"\.tm-ai-agent-plan__(action|add)[^{]*\{", "the icon-only actions grow too");
    }

    [Fact]
    public void EveryStateIsSpelledOutNextToItsColour()
    {
        var css = Stylesheet();

        foreach (var status in new[] { "pending", "running", "done", "failed", "skipped" })
        {
            css.Should().Contain($"[data-status=\"{status}\"]",
                "the step's state must paint from a data attribute, not from a class soup");
        }

        // The state colour lives on the icon/text block; the word itself is always rendered by the markup.
        Markup().Should().Contain("TmAgentPlan_StatusDone");
        Markup().Should().Contain("TmAgentPlan_StatusFailed");
    }

    [Fact]
    public void TheForcedColorsBlock_RestoresTheSystemFocusRing()
    {
        var block = Block(Stylesheet(), "@media (forced-colors: active)");

        block.Should().Contain("Highlight", "the system palette must own the focus ring in high contrast");
        var ringed = Regex.Matches(block, @"([^{}]*:focus-visible[^{}]*)\{([^}]*)\}")
            .Where(match => ThemeCss.Normalise(match.Groups[2].Value)
                .Contains("outline-color: Highlight", StringComparison.Ordinal))
            .SelectMany(match => ThemeCss.SelectorParts(match.Groups[1].Value))
            .ToList();

        ringed.Should().Contain(".tm-ai-agent-plan__action:focus-visible");
        ringed.Should().Contain(".tm-ai-agent-plan__title-button:focus-visible");
        ringed.Should().Contain(".tm-ai-agent-plan__rename:focus-visible");
    }

    [Fact]
    public void ThePlanAddsNoMotionOfItsOwn()
    {
        // No transition/animation declaration: the reduced-motion enumeration of the package then stays
        // exactly as it is, and a plan that redraws every second does not animate while it does.
        MotionDeclaration.IsMatch(Stylesheet()).Should().BeFalse(
            "the plan animates nothing; a transition here would need its own reduced-motion entry");
    }

    /// <summary>The body of the block that starts at <paramref name="selector"/>, brace-balanced.</summary>
    private static string Block(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the stylesheet must keep its {0} block", selector);

        var open = css.IndexOf('{', start);
        open.Should().BeGreaterThanOrEqualTo(0);

        var depth = 0;
        for (var index = open; index < css.Length; index++)
        {
            depth += css[index] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return css[(open + 1)..index];
            }
        }

        return css[(open + 1)..];
    }
}
