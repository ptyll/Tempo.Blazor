using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Mutation coverage for the <c>@media</c> model of <see cref="CssCascade"/>. Before it existed the
/// parser flattened conditional blocks to unconditional rules, so <c>@media (max-width: 768px)</c>
/// could win a desktop measurement — a winner no user ever saw, reported with perfect confidence.
/// These tests pin both directions: a conditioned rule is skipped where its condition fails, applied
/// where it holds, and reported UNMODELLED — never assumed off — where the context cannot answer it.
/// </summary>
public sealed class CssCascadeMediaTests
{
    private static readonly IReadOnlyList<CssCascade.Element> Chain =
        [new CssCascade.Element("div", "tm-widget")];

    private const string Css =
        """
        .tm-widget { color: black; }
        @media (max-width: 768px) {
            .tm-widget { color: red; }
        }
        @media (prefers-reduced-motion: reduce) {
            .tm-widget { transition: none; }
        }
        @media print {
            .tm-widget { color: gray; }
        }
        """;

    [Fact]
    public void AConditionedRule_DoesNotApplyWhereItsConditionFails()
    {
        CssCascade.Winning(Css, Chain, "color", media: new CssCascade.MediaContext(WidthPx: 1440))
            .Should().Be("black",
                "na 1440 px pravidlo z @media (max-width: 768px) neexistuje — nesmí vyhrát");
    }

    [Fact]
    public void AConditionedRule_WinsWhereItsConditionHolds()
    {
        CssCascade.Winning(Css, Chain, "color", media: new CssCascade.MediaContext(WidthPx: 360))
            .Should().Be("red", "na 360 px mobilní přepsání platí a pozdější pořadí vítězí");
    }

    [Fact]
    public void AWidthCondition_IsUndecidableWithoutAWidth()
    {
        var resolved = CssCascade.Resolve(Css, Chain, "color", media: new CssCascade.MediaContext());

        resolved.Unmodelled.Should().NotBeEmpty(
            "sonda, která nezná šířku viewportu, nesmí media podmínku mlčky vypnout — to je měření " +
            "naslepo, ne odpověď");
        resolved.Unmodelled.Should().Contain(x => x.Contains(".tm-widget"));
    }

    [Fact]
    public void PrintAndReducedMotion_AreModelledSeparately()
    {
        CssCascade.Winning(Css, Chain, "color",
                media: new CssCascade.MediaContext(WidthPx: 1440, Medium: "print"))
            .Should().Be("gray");
        CssCascade.Winning(Css, Chain, "transition",
                media: new CssCascade.MediaContext(WidthPx: 1440, ReducedMotion: true))
            .Should().Be("none");
        CssCascade.Resolve(Css, Chain, "transition", media: new CssCascade.MediaContext(WidthPx: 1440))
            .Value.Should().BeNull("bez prefers-reduced-motion pravidlo na transition neexistuje");
    }

    [Fact]
    public void AnUnknownMediaFeature_IsReportedNotSkipped()
    {
        const string css =
            "@media (prefers-color-scheme: dark) { .tm-widget { color: white; } }" +
            ".tm-widget { color: black; }";

        var resolved = CssCascade.Resolve(css, Chain, "color", media: new CssCascade.MediaContext(WidthPx: 1440));

        resolved.Unmodelled.Should().ContainSingle(
            "feature, kterou model nezná, je NEROZHODNUTELNÁ — ne vypnutá");
    }

    [Fact]
    public void KeyframesAreNotSelectors()
    {
        const string css =
            "@keyframes tm-fade { from { opacity: 0; } to { opacity: 1; } }" +
            ".tm-widget { opacity: .5; }";

        var rules = CssCascade.ParseRules(css);
        rules.Should().ContainSingle().Which.Selector.Should().Be(".tm-widget",
            "from/to jsou keyframe bloky, ne selektory — sonda je nesmí číst jako pravidla prvků");
    }
}
