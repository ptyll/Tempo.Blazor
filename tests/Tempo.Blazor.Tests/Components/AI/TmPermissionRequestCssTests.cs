using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Hygiene gate for the permission-request stylesheet (<c>_ai-permission.css</c>, plan 10b3966f phase
/// 5): every class the markup emits has a rule, the request is a finger target on a coarse pointer,
/// the forced-colors block restores the system focus ring on every focus stop, and the collapse
/// transition is switched off under <c>prefers-reduced-motion</c>.
/// </summary>
/// <remarks>
/// The package-wide token rules (no colour literal, no raw palette step, no <c>[data-theme]</c> block,
/// every <c>var(--tm-*)</c> resolvable) are enforced for every stylesheet of the package by
/// <c>AiMarkdownViewCssTests</c>.
/// </remarks>
public sealed class TmPermissionRequestCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex RazorComment =
        new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex CssClassSelector =
        new(@"\.(tm-[a-zA-Z][\w-]*)", RegexOptions.Compiled, RegexTimeout);

    private static string Root => ThemeCss.RepositoryRoot().FullName;

    private static string Stylesheet()
        => Comment.Replace(
            File.ReadAllText(Path.Combine(
                Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "components", "_ai-permission.css")),
            " ");

    /// <summary>The component files: the markup plus every partial of the code-behind.</summary>
    private static string Markup()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(Root, "src", "Tempo.Blazor.AI", "Components", "AI"),
                "TmPermissionRequest.razor*")
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
            .Where(name => name.StartsWith("tm-ai-permission", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The body of the named at-rule block, brace-balanced.</summary>
    private static string Block(string css, string atRule)
    {
        var start = css.IndexOf(atRule, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "{0} must keep its {1} block", "the stylesheet", atRule);

        var open = css.IndexOf('{', start);
        open.Should().BeGreaterThanOrEqualTo(0);

        var depth = 1;
        var index = open + 1;
        for (; index < css.Length && depth > 0; index++)
        {
            if (css[index] == '{') depth++;
            else if (css[index] == '}') depth--;
        }

        depth.Should().Be(0, "the {0} block must be balanced", atRule);
        return css[(open + 1)..(index - 1)];
    }

    [Fact]
    public void EveryPermissionClassTheMarkupEmits_HasARule()
    {
        var emitted = Emitted();
        var styled = CssClassSelector.Matches(Stylesheet())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        emitted.Should().NotBeEmpty("the component emits its own BEM classes");
        emitted.Should().BeSubsetOf(styled,
            "a class the request emits without a rule ships unstyled. Missing: {0}",
            string.Join(", ", emitted.Except(styled)));
    }

    [Fact]
    public void TheStylesheetIsImportedByThePackageEntryPoint()
    {
        var entry = File.ReadAllText(Path.Combine(
            Root, "src", "Tempo.Blazor.AI", "wwwroot", "css", "tempo-blazor-ai.css"));

        entry.Should().Contain("@import \"components/_ai-permission.css\"",
            "a host loads exactly one stylesheet, so an unimported partial never reaches the page");
    }

    [Fact]
    public void TheActionsAndOptions_ReachTheTouchFloorOnACoarsePointer()
    {
        var css = Stylesheet();
        var coarse = Block(css, "@media (pointer: coarse)");

        coarse.Should().Contain("--tm-touch-target");
        foreach (var selector in new[]
                 {
                     ".tm-ai-permission__action",
                     ".tm-ai-permission__revoke",
                     ".tm-ai-permission__option",
                     ".tm-ai-permission__radio",
                 })
        {
            coarse.Should().Contain(selector,
                "{0} is a thumb target on a phone — 24 px is not", selector);
        }
    }

    [Fact]
    public void TheForcedColorsBlock_RestoresTheSystemFocusRingOnEveryFocusStop()
    {
        var forced = Block(Stylesheet(), "@media (forced-colors: active)");

        forced.Should().Contain("Highlight",
            "the box-shadow ring disappears in a forced palette, so every focus stop repaints a "
            + "system outline");

        foreach (var selector in new[]
                 {
                     ".tm-ai-permission:focus-visible",
                     ".tm-ai-permission__action:focus-visible",
                     ".tm-ai-permission__revoke:focus-visible",
                     ".tm-ai-permission__radio:focus-visible",
                 })
        {
            forced.Should().Contain(selector, "inside the forced-colors block");
        }

        forced.Should().Contain("outline-color: Highlight",
            "outline-color is the declaration that survives forced colors");
    }

    [Fact]
    public void TheCollapseTransition_IsSwitchedOffUnderReducedMotion()
    {
        var reduce = Block(Stylesheet(), "@media (prefers-reduced-motion: reduce)");

        reduce.Should().Contain(".tm-ai-permission__collapse");
        reduce.Should().Contain("transition: none",
            "a height animation is exactly the motion a reduced-motion reader asked not to see");
    }

    [Fact]
    public void TheCountdown_DoesNotJitterWhileItTicks()
    {
        var countdown = Regex.Match(Stylesheet(), @"\.tm-ai-permission__countdown\s*\{([^}]*)\}");

        countdown.Success.Should().BeTrue("the granted row carries a per-second countdown");
        countdown.Groups[1].Value.Should().Contain("font-variant-numeric: tabular-nums",
            "a per-second countdown with proportional digits shifts the whole row on every tick");
    }
}
