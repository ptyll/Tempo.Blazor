using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// Token gate for the Tempo.Blazor.Spreadsheet package (redesign phase 2). The package used to paint
/// from literals: seven scoped dialog stylesheets carried hex colours plus hex fallbacks
/// (<c>var(--tm-color-surface, #fff)</c>), the canvas kept a hardcoded blue selection fill and six
/// hardcoded formula-reference colours, and the header label font was a monospace literal. A literal is
/// what renders, so the palette, the indigo theme and dark mode never reached the grid.
/// </summary>
/// <remarks>
/// Mirrors the strict audit (<c>scripts/audit-css-strict.mjs</c>) for this package but asserts the ZERO
/// state directly: no undefined alias, no colour literal in a stylesheet OR in the canvas script, no
/// manual dark block, no pixel font-size / radius / raw z-index, and every component token declared on
/// the <c>.tm-spreadsheet</c> root and derived from a core token.
/// </remarks>
public sealed class SpreadsheetDesignTokenTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex TokenUsage =
        new(@"var\(\s*(--tm-[a-zA-Z0-9-]+)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex TokenDefinition =
        new(@"(--tm-[a-zA-Z0-9-]+)\s*:", RegexOptions.Compiled, RegexTimeout);

    /// <summary>Same literal detector as the strict audit, including the named colours.</summary>
    private static readonly Regex ColorLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|(?<![\w-])(?:white|black)(?![\w-])",
            RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// The script-side detector. A hex, a named colour, or an <c>rgb()/rgba()/hsl()</c> call whose first
    /// argument is a NUMBER is a literal — the canvas pins one theme with it. A colour COMPOSED from a
    /// token (<c>rgba(${r}, …)</c> in the alpha helper, <c>rgba(var(--tm-…), a)</c>) is not drift.
    /// </summary>
    private static readonly Regex ScriptColorLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(\s*[0-9.]|hsla?\(\s*[0-9.]|(?<![\w-])(?:white|black)(?![\w-])",
            RegexOptions.Compiled, RegexTimeout);

    /// <summary>A data-palette custom-property definition may hold a literal — it is data, not drift.</summary>
    private static readonly Regex DataPaletteDefinition =
        new(@"^\s*--tm-[a-z0-9-]+-(palette|option|role|annotation|category)-[a-z0-9-]+\s*:",
            RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ManualDarkBlock =
        new(@"\[data-theme\s*=\s*[""']dark[""']\]|\.tm-dark\b", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex PixelFontSize =
        new(@"font-size:\s*\d+(?:\.\d+)?px", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex PixelRadius =
        new(@"border-radius:\s*\d+(?:\.\d+)?px", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex NumericZIndex =
        new(@"z-index:\s*(\d+)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// The component tokens the redesign pins on <c>.tm-spreadsheet</c>. Every one is derived from a
    /// core semantic token (never a literal) so light, dark and the indigo theme all recompute.
    /// </summary>
    private static readonly string[] RequiredComponentTokens =
    [
        "--tm-spreadsheet-header-bg",
        "--tm-spreadsheet-header-text",
        "--tm-spreadsheet-header-active-bg",
        "--tm-spreadsheet-header-active-text",
        "--tm-spreadsheet-header-font",
        "--tm-spreadsheet-gridline",
        "--tm-spreadsheet-selection-border",
        "--tm-spreadsheet-selection-fill",
        "--tm-spreadsheet-ref-0",
        "--tm-spreadsheet-ref-1",
        "--tm-spreadsheet-ref-2",
        "--tm-spreadsheet-ref-3",
        "--tm-spreadsheet-ref-4",
        "--tm-spreadsheet-ref-5",
    ];

    [Fact]
    public void PackageCss_HasNoUndefinedTokenAliases()
    {
        var defined = CoreTokenDefinitions();
        defined.UnionWith(PackageCss()
            .SelectMany(pair => TokenDefinition.Matches(Strip(pair.Text)).Select(match => match.Groups[1].Value))
            .ToHashSet());

        // The package also sets tokens from script (the canvas writes data attributes only, but a host may
        // stamp a token through setProperty) — mirror the strict audit's runtime escape hatch.
        foreach (var (_, text) in PackageScripts())
        {
            foreach (Match match in Regex.Matches(text, @"setProperty\(\s*['""](--tm-[a-zA-Z0-9-]+)['""]",
                         RegexOptions.None, RegexTimeout))
            {
                defined.Add(match.Groups[1].Value);
            }
        }

        var offences = new List<string>();
        foreach (var (file, text) in PackageCss())
        {
            foreach (Match usage in TokenUsage.Matches(Strip(text)))
            {
                var token = usage.Groups[1].Value;
                if (!defined.Contains(token))
                {
                    offences.Add($"{file}: undefined token {token}");
                }
            }
        }

        offences.Should().BeEmpty(
            "every Spreadsheet var(--tm-*) must resolve to a core token (tokens.css / tokens-dark.css / "
            + "theme-*.css) or to a component token declared on the .tm-spreadsheet root — an undefined "
            + "alias renders its fallback forever and ignores the palette and dark mode. Offences:\n"
            + string.Join("\n", offences));
    }

    [Fact]
    public void PackageCss_HasNoColourLiterals()
    {
        var offences = new List<string>();
        foreach (var (file, text) in PackageCss())
        {
            var lines = Strip(text).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (ColorLiteral.IsMatch(lines[index]) && !DataPaletteDefinition.IsMatch(lines[index]))
                {
                    offences.Add($"{file}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        offences.Should().BeEmpty(
            "Spreadsheet stylesheets paint with tokens only; the single exception is a data-palette "
            + "custom-property definition, which is data. Offences:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void CanvasScript_HasNoColourLiterals()
    {
        var offences = new List<string>();
        foreach (var (file, text) in PackageScripts())
        {
            var lines = Strip(text).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                // A CSS selector / DOM id inside a string is not a colour (`querySelector("#a1b")`).
                var literal = ScriptColorLiteral.Match(line);
                if (!literal.Success)
                {
                    continue;
                }

                var before = line[..literal.Index];
                if (before.Count(ch => ch == '"' || ch == '\'') % 2 == 1
                    && (literal.Value.StartsWith('#') || literal.Value is "white" or "black"))
                {
                    continue;
                }

                offences.Add($"{file}:{index + 1}: {line.Trim()}");
            }
        }

        offences.Should().BeEmpty(
            "the canvas reads every colour from buildPalette (component tokens or core tokens) — a literal "
            + "there pins the grid to one theme and ignores dark mode and the indigo palette. Offences:\n"
            + string.Join("\n", offences));
    }

    [Fact]
    public void PackageCss_HasNoManualDarkThemeBlocks()
    {
        var offences = new List<string>();
        foreach (var (file, text) in PackageCss())
        {
            // A package THEME layer (`theme-*.css` / `tokens*.css`) is where a dark override belongs —
            // exactly the allowlist of the strict audit (scripts/audit-css-strict.mjs, isAllowlisted).
            // The rule targets COMPONENT stylesheets, which must stay theme-agnostic.
            if (IsThemeLayer(file))
                continue;

            foreach (Match match in ManualDarkBlock.Matches(Strip(text)))
            {
                offences.Add($"{file}: {match.Value}");
            }
        }

        offences.Should().BeEmpty(
            "dark mode is tokens-dark.css's job — a [data-theme=dark]/.tm-dark block in a component "
            + "stylesheet duplicates the theme and drifts; move it to a package theme-*.css layer. Offences:\n"
            + string.Join("\n", offences));
    }

    [Fact]
    public void PackageThemeLayer_OverridesTheActiveHeaderOpaqueInDark()
    {
        var layer = PackageCss()
            .Where(entry => Path.GetFileName(entry.File).StartsWith("theme-", StringComparison.Ordinal))
            .ToList();
        layer.Should().NotBeEmpty("the package ships a theme layer for its dark overrides");

        var dark = layer.SingleOrDefault(entry => entry.Text.Contains("--tm-spreadsheet-header-active-bg", StringComparison.Ordinal));
        dark.File.Should().NotBeNull("the dark active-header override must live in the package theme layer");
        var stripped = Strip(dark.Text);

        // The theme CONDITION is wrapped in :where(...) so the rule keeps `.tm-spreadsheet`'s own
        // specificity (0,1,0) — a bare `[data-theme="dark"] .tm-spreadsheet` would be 0,2,0 and would
        // out-rank a host's `.tm-spreadsheet { --tm-spreadsheet-header-active-bg: … }` override in dark.
        // The two switches are TWO selector parts (each with its own :where), not one `:where(A, B)`: the
        // dark-theme parity guard splits selectors on commas, so a comma inside :where() would read as two
        // half-themed fragments (DarkThemeSelectorParityTests).
        stripped.Should().Contain(":where([data-theme=\"dark\"]) .tm-spreadsheet");
        stripped.Should().Contain(":where(.tm-dark) .tm-spreadsheet");
        stripped.Should().NotContain("[data-theme=\"dark\"] .tm-spreadsheet",
            "the theme condition must stay inside :where(), otherwise a host token override loses in dark");
        DarkThemeRule.Matches(stripped).Should().ContainSingle(
            "there is exactly one theme rule and its specificity is asserted above");
        stripped.Should().Contain("--tm-spreadsheet-header-active-bg: color-mix(",
            "the dark strip must be an OPAQUE mix over the header surface, not the translucent primary-subtle wash");
        ColorLiteral.Matches(stripped).Should().BeEmpty("the override is token-only — no hex, no rgb() literal");
        var usedTokens = TokenUsage.Matches(stripped).Select(match => match.Groups[1].Value).Distinct().ToList();
        usedTokens.Should().BeSubsetOf(
            new[] { "--tm-color-primary", "--tm-spreadsheet-header-bg" },
            "the override may only compose existing tokens");

        var bundle = PackageCss().Single(entry => Path.GetFileName(entry.File) == "tempo-blazor-spreadsheet.css");
        bundle.Text.Should().Contain(Path.GetFileName(dark.File),
            "the package bundle must import the theme layer, so a host that loads the bundle gets the opaque dark strip");
    }

    /// <summary>A theme rule whose selector mentions the dark attribute or class — the specificity contract.</summary>
    private static readonly Regex DarkThemeRule =
        new(@"(?<selector>[^{}]*\[data-theme\s*=\s*[""']dark[""']\][^{}]*)\{", RegexOptions.Compiled, RegexTimeout);

    /// <summary>The package theme layer carries the dark overrides the strict audit allowlists.</summary>
    private static bool IsThemeLayer(string file)
    {
        var name = Path.GetFileName(file);
        // Optional cheap hardening: a theme layer must be a TOP-LEVEL package stylesheet
        // (wwwroot/css/<name>), never a hidden one inside components/.
        var inThemeRoot = file.Replace('\\', '/').Contains("/wwwroot/css/", StringComparison.Ordinal)
            && !file.Replace('\\', '/').Contains("/wwwroot/css/components/", StringComparison.Ordinal);
        return inThemeRoot
            && (name.StartsWith("theme-", StringComparison.Ordinal)
                || name.StartsWith("tokens", StringComparison.Ordinal));
    }

    [Fact]
    public void PackageCss_HasNoPixelFontSizesRadiiOrRawZIndex()
    {
        var offences = new List<string>();
        foreach (var (file, text) in PackageCss())
        {
            var stripped = Strip(text);
            offences.AddRange(PixelFontSize.Matches(stripped)
                .Select(match => $"{file}: {match.Value} (use --tm-text-*)"));
            offences.AddRange(PixelRadius.Matches(stripped)
                .Select(match => $"{file}: {match.Value} (use --tm-radius-card/--tm-radius-control)"));
            offences.AddRange(NumericZIndex.Matches(stripped)
                .Where(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) >= 10)
                .Select(match => $"{file}: {match.Value} (use --tm-z-*)"));
        }

        offences.Should().BeEmpty(
            "typography, radii and stacking belong to the design system. Offences:\n"
            + string.Join("\n", offences));
    }

    [Fact]
    public void ComponentTokens_AreDeclaredOnTheSpreadsheetRoot_AndDerivedFromCoreTokens()
    {
        var stylesheet = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            PackageDirectory(), "wwwroot", "css", "components", "_spreadsheet.css")));
        var rootBody = RootRuleBody(stylesheet);
        rootBody.Should().NotBeNull(".tm-spreadsheet must declare the component tokens in _spreadsheet.css");

        var declarations = TokenDefinition.Matches(rootBody!)
            .ToDictionary(match => match.Groups[1].Value, _ => true, StringComparer.Ordinal);
        var values = DeclaredValues(rootBody!);

        var missing = RequiredComponentTokens.Where(token => !declarations.ContainsKey(token)).ToList();
        missing.Should().BeEmpty(
            "every component token the canvas reads must be declared on .tm-spreadsheet so a host can "
            + "override it one level up. Missing:\n" + string.Join("\n", missing));

        foreach (var token in RequiredComponentTokens)
        {
            var value = values[token];
            value.Should().Contain("var(--tm-",
                $"{token} must be derived from a core semantic token, not a literal (got '{value}')");
            ColorLiteral.IsMatch(value).Should().BeFalse(
                $"{token} must not carry a colour literal (got '{value}')");
        }
    }

    [Fact]
    public void MobileLayout_KeepsAUsableGridFloor_WhenTheHostIsShorterThanTheChrome()
    {
        // The Notion fullscreen edit modal at 568x320 overrides the phone height floor
        // (--tm-spreadsheet-mobile-min-height: 0) so the Save / Discard footer cannot be pushed out of the
        // viewport. Before this rule the fixed chrome (toolbar + formula bar + column headers + sheet tabs +
        // action bar) ate the whole 100% box, the grid resolved to ~0px and the editor showed NO rows at
        // all. The grid therefore keeps its own floor and the component's box scrolls when the host cannot
        // fit chrome + floor; a host with enough height never sees a scrollbar.
        var stylesheet = ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            PackageDirectory(), "wwwroot", "css", "components", "_spreadsheet.css")));

        var mobile = RuleBodies(stylesheet, ".tm-spreadsheet--layout-mobile");
        mobile.Should().NotBeEmpty("the phone layout must own the short-host fallback");
        mobile.Should().Contain(body => body.Contains("overflow-y: auto", StringComparison.Ordinal),
            "the phone root scrolls when the host is shorter than the chrome + the grid floor");

        var grid = RuleBodies(stylesheet, ".tm-spreadsheet--layout-mobile .tm-spreadsheet-canvas-grid");
        grid.Should().NotBeEmpty("the phone grid needs its own min-height floor");
        grid.Should().Contain(body => body.Contains(
                "min-height: var(--tm-spreadsheet-grid-min-height", StringComparison.Ordinal),
            "the floor is a host-overridable component token, not a literal");
    }

    [Fact]
    public void CanvasScript_ResolvesEveryColourThroughBuildPalette()
    {
        var script = File.ReadAllText(Path.Combine(PackageDirectory(), "wwwroot", "js", "spreadsheet-canvas.js"));

        script.Should().Contain("function buildPalette(root)",
            "the canvas must keep one palette builder that reads the tokens");
        foreach (var token in RequiredComponentTokens.Where(token => !token.StartsWith("--tm-spreadsheet-ref-", StringComparison.Ordinal)))
        {
            script.Should().Contain(token,
                $"buildPalette must read {token} so the canvas follows the theme");
        }

        // The six formula-reference colours are read in a loop (ref-0 … ref-5), so the prefix is the
        // contract rather than six repeated literals.
        script.Should().Contain("--tm-spreadsheet-ref-${index}",
            "buildPalette must read the --tm-spreadsheet-ref-0..5 palette in a loop");

        script.Should().Contain("_internals",
            "the pure helpers (palette, density, header highlight) are exposed for `npm run test:spreadsheet`");
    }

    private static string Strip(string text) => Comment.Replace(text, " ");

    private static string PackageDirectory() =>
        Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.Spreadsheet");

    /// <summary>Every stylesheet of the package (scoped and global), keyed by repo-relative path.</summary>
    private static IReadOnlyList<(string File, string Text)> PackageCss()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var directory = Path.Combine(root, "src", "Tempo.Blazor.Spreadsheet");
        Directory.Exists(directory).Should().BeTrue("the Spreadsheet package must exist");

        return Directory
            .EnumerateFiles(directory, "*.css", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)))
            .ToList();
    }

    /// <summary>The package's production scripts (test files and the packed bundle are not sources).</summary>
    private static IReadOnlyList<(string File, string Text)> PackageScripts()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var directory = Path.Combine(root, "src", "Tempo.Blazor.Spreadsheet", "wwwroot", "js");
        Directory.Exists(directory).Should().BeTrue("the Spreadsheet canvas scripts must exist");

        return Directory
            .EnumerateFiles(directory, "*.js", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .Where(path => !path.EndsWith(".test.mjs", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}__tests__{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)))
            .ToList();
    }

    private static bool IsSourcePath(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        && !path.EndsWith(".bundled.css", StringComparison.Ordinal);

    /// <summary>The declaration body of the bare <c>.tm-spreadsheet</c> rule.</summary>
    private static string? RootRuleBody(string stylesheet)
    {
        foreach (Match rule in RuleBlock.Matches(stylesheet))
        {
            var parts = ThemeCss.SelectorParts(rule.Groups["selector"].Value);
            if (parts.Count == 1 && parts[0] == ".tm-spreadsheet")
            {
                return rule.Groups["body"].Value;
            }
        }

        return null;
    }

    /// <summary>Every declaration body whose selector list contains EXACTLY the given selector.</summary>
    private static IReadOnlyList<string> RuleBodies(string stylesheet, string selector)
        => RuleBlock.Matches(stylesheet)
            .Where(rule => ThemeCss.SelectorParts(rule.Groups["selector"].Value)
                .Any(part => string.Equals(part, selector, StringComparison.Ordinal)))
            .Select(rule => rule.Groups["body"].Value)
            .ToList();

    private static Dictionary<string, string> DeclaredValues(string body)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in body.Split(';'))
        {
            var separator = part.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var name = part[..separator].Trim();
            if (name.StartsWith("--tm-", StringComparison.Ordinal))
            {
                values[name] = ThemeCss.Normalise(part[(separator + 1)..]);
            }
        }

        return values;
    }

    /// <summary>Tokens the core defines (the generated bundle is excluded — it duplicates every definition).</summary>
    private static HashSet<string> CoreTokenDefinitions()
    {
        var root = Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css");
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".bundled.css", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in TokenDefinition.Matches(Strip(File.ReadAllText(file))))
            {
                defined.Add(match.Groups[1].Value);
            }
        }

        return defined;
    }
}
