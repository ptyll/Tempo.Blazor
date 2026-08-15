using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// 2.8.17 slib: každá osiřelá třída z registru má v CSS Tempa pravidlo, a bundl ho nese.
/// </summary>
/// <remarks>
/// Mutace obou směrů je na extraktoru selektorů, ne na „soubor existuje“. Odebrání slíbeného
/// bloku musí spadnout; přítomnost na správném stavu musí projít. Jmenovatel je seznam slibu,
/// ne „kolik pravidel sonda sama napočítala“.
/// </remarks>
public sealed class OrphanClassCssContractTests
{
    private static readonly string[] PromisedOrphanClasses =
    [
        "tm-pagination-size",
        "tm-pagination-disabled",
        "tm-avatar-fallback",
        "tm-avatar-gray",
        "tm-avatar-red",
        "tm-avatar-orange",
        "tm-avatar-green",
        "tm-avatar-blue",
        "tm-avatar-purple",
        "tm-avatar-pink",
        "tm-avatar-2xl",
        "tm-input-search"
    ];

    [Fact]
    public void PromisedOrphanClasses_HaveARuleInSourceCss()
    {
        var selectors = ClassSelectorsIn(SourceCss());

        selectors.Should().Contain(
            PromisedOrphanClasses,
            "slib 2.8.17: každá nálezová třída musí mít vlastní pravidlo, ne jen sourozence");
    }

    [Fact]
    public void PromisedOrphanClasses_HaveARuleInTheShippedBundle()
    {
        var selectors = ClassSelectorsIn(BundledCss());

        selectors.Should().Contain(
            PromisedOrphanClasses,
            "zdrojové CSS bez přegenerovaného bundlu by hostitel, který bere bundled.css, neviděl");
    }

    [Fact]
    public void PaginationSize_LaysOutTheLabelAndTheSelect()
    {
        SelectorBlock(SourceFile("components/_data-table.css"), ".tm-pagination-size")
            .Should().Contain("display: flex")
            .And.Contain("align-items:")
            .And.Contain("gap:");
    }

    [Fact]
    public void PaginationDisabled_PaintsAndBlocksThePager()
    {
        var block = SelectorBlock(SourceFile("components/_data-table.css"), ".tm-pagination-disabled");

        block.Should().Contain("opacity:");
        block.Should().Contain("pointer-events: none");
    }

    [Fact]
    public void AvatarFallback_FillsTheAvatar()
    {
        var block = SelectorBlock(SourceFile("components/_avatar.css"), ".tm-avatar-fallback");

        block.Should().Contain("width:");
        block.Should().Contain("height:");
        block.Should().Contain("display:");
    }

    [Fact]
    public void AvatarColorModifiers_SetBackgroundAndInkThroughTokens()
    {
        var css = SourceFile("components/_avatar.css");

        foreach (var color in new[] { "gray", "red", "orange", "green", "blue", "purple", "pink" })
        {
            var block = SelectorBlock(css, $".tm-avatar-{color}");
            block.Should().Contain("background-color:");
            block.Should().Contain("color:");
            block.Should().Contain("var(--tm-", "barva avatara musí jít z tokenu, ne z literálu, který se s motivem nepřeklopí");
        }
    }

    [Fact]
    public void Avatar2xl_MatchesTheXxlBoxTheStylesheetAlreadyHad()
    {
        var css = SourceFile("components/_avatar.css");
        var xxl = SelectorBlock(css, ".tm-avatar-xxl");
        var twoXl = SelectorBlock(css, ".tm-avatar-2xl");

        WidthHeight(twoXl).Should().Be(
            WidthHeight(xxl),
            "komponenta emituje tm-avatar-2xl; xxl už rozměr má — alias, ne druhé číslo");
    }

    [Fact]
    public void InputSearch_OwnsItsChrome()
    {
        var block = SelectorBlock(SourceFile("components/_input.css"), ".tm-input-search");

        block.Should().MatchRegex(@"appearance:\s*none");
    }

    [Fact]
    public void OutlineSecondary_UsesTheControlBorderToken()
    {
        var block = SelectorBlock(SourceFile("components/_button.css"), ".tm-btn-outline-secondary");

        block.Should().Contain("border-color: var(--tm-border-color-control");
        block.Should().NotContain("border-color: var(--tm-border-color);");
    }

    [Fact]
    public void TheExtractor_FailsClosed_WhenAPromisedRuleIsDeleted()
    {
        var source = SourceCss();
        ClassSelectorsIn(source).Should().Contain("tm-pagination-size");

        var mutated = Regex.Replace(
            source,
            @"\.tm-pagination-size\s*\{[^}]*\}",
            string.Empty,
            RegexOptions.Singleline);

        ClassSelectorsIn(mutated).Should().NotContain(
            "tm-pagination-size",
            "odebrání slíbeného bloku musí být vidět — jinak strážce hlídá jen že soubor existuje");
    }

    [Fact]
    public void TheExtractor_DoesNotInventASelectorFromProse()
    {
        const string css = """
            /* .tm-pagination-size is mentioned here as a class name in a comment */
            .tm-pagination-size-label { color: red; }
            """;

        ClassSelectorsIn(css).Should().BeEquivalentTo(
            ["tm-pagination-size-label"],
            "zmínka v komentáři ani delší sousední třída nejsou pravidlo");
    }

    private static (string Width, string Height) WidthHeight(string block)
    {
        var width = Regex.Match(block, @"width:\s*([^;]+);");
        var height = Regex.Match(block, @"height:\s*([^;]+);");
        width.Success.Should().BeTrue("blok musí nastavovat width");
        height.Success.Should().BeTrue("blok musí nastavovat height");
        return (width.Groups[1].Value.Trim(), height.Groups[1].Value.Trim());
    }

    private static HashSet<string> ClassSelectorsIn(string css)
    {
        var withoutComments = Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(withoutComments, @"\.([a-zA-Z][\w-]*)"))
        {
            names.Add(match.Groups[1].Value);
        }

        return names;
    }

    private static string SelectorBlock(string css, string selector)
    {
        var pattern = Regex.Escape(selector) + @"\s*\{";
        var start = Regex.Match(css, pattern);
        start.Success.Should().BeTrue("CSS musí deklarovat {0}", selector);

        var from = start.Index;
        var end = css.IndexOf('}', from);
        end.Should().BeGreaterThan(from);
        return css[from..end];
    }

    private static string SourceCss() =>
        string.Join(
            "\n",
            SourceFile("components/_data-table.css"),
            SourceFile("components/_avatar.css"),
            SourceFile("components/_input.css"),
            SourceFile("components/_button.css"));

    private static string BundledCss() =>
        File.ReadAllText(Path.Combine(CssRoot(), "tempo-blazor.bundled.css"));

    private static string SourceFile(string relative) =>
        File.ReadAllText(Path.Combine(CssRoot(), relative));

    private static string CssRoot() =>
        Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor", "wwwroot", "css");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find TempoBlazor.slnx.");
    }
}
