using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Mutation coverage for the structural reader of <see cref="CssCascade"/> — the parts of a
/// stylesheet that are not "selector { declarations }": statement at-rules, <c>@layer</c>,
/// <c>@import</c>, <c>@container</c>, nested media, and CSS nesting.
/// <para>
/// The flat brace-walk this replaces had two fail-open shapes: a ';'-terminated at-rule
/// (<c>@import "x";</c>) was read as part of the NEXT rule's header — one stray statement
/// swallowed a whole style rule — and a rule body containing a nested block was read as one
/// declaration run, so a nested override could neither apply nor be reported. Every test here
/// pins a shape that either mis-parsed silently or could not be expressed at all.
/// </para>
/// </summary>
public sealed class CssCascadeStructureTests
{
    private static readonly IReadOnlyList<CssCascade.Element> Chain =
        [new CssCascade.Element("div", "tm-widget")];

    [Fact]
    public void A_Statement_At_Rule_Does_Not_Swallow_The_Next_Rule()
    {
        // The old walk looked for the next '{' from the cursor: "@import \"x\";" became the start
        // of the following header, so ".tm-widget { color: red }" was absorbed into an @-rule the
        // parser skipped — one statement deleted a rule from the model entirely.
        const string css =
            """
            @import "tokens.css";
            .tm-widget { color: red; }
            """;

        var rules = CssCascade.ParseStylesheet(css, _ => null).Rules;
        rules.Should().ContainSingle().Which.Selector.Should().Be(".tm-widget",
            "@import je statement ukončený ';' — nesmí se sloučit s hlavičkou dalšího pravidla");
    }

    [Fact]
    public void A_Layer_Order_Statement_Does_Not_Swallow_The_Next_Rule()
    {
        const string css =
            """
            @layer reset, components;
            .tm-widget { color: red; }
            """;

        var rules = CssCascade.ParseStylesheet(css).Rules;
        rules.Should().ContainSingle().Which.Selector.Should().Be(".tm-widget",
            "pořadová deklarace vrstev je statement — stejná díra jako @import");
    }

    [Fact]
    public void An_Import_Without_A_Resolver_Is_Reported_Not_Skipped()
    {
        const string css =
            """
            @import "tokens.css";
            .tm-widget { color: red; }
            """;

        var outcome = CssCascade.ParseStylesheet(css);

        outcome.Rules.Should().ContainSingle();
        outcome.Unmodelled.Should().ContainSingle(x => x.Contains("@import"),
            "sonda nevidí za import — mlčet o tom je díra nahlášená jako čistý výsledek");
    }

    [Fact]
    public void An_Unresolvable_Import_Is_Reported_Not_Skipped()
    {
        const string css = "@import \"gone.css\"; .tm-widget { color: red; }";

        var outcome = CssCascade.ParseStylesheet(css, _ => null);

        outcome.Unmodelled.Should().ContainSingle(x => x.Contains("gone.css"),
            "resolver, který cíl nenajde, musí díru nahlásit — ne ji ztichnout");
    }

    [Fact]
    public void An_Import_Is_Parsed_At_Its_Own_Position_In_The_Cascade()
    {
        // Import order IS cascade order: the imported rule sits where the statement sits, so the
        // later local rule wins on source order — the way the bundle the manifest produces wins.
        const string css =
            """
            .tm-widget { color: red; }
            @import "late.css";
            """;

        var resolved = CssCascade.Winning(css, Chain, "color",
            importResolver: _ => ".tm-widget { color: blue; }");

        resolved.Should().Be("blue",
            "importovaná pravidla se čtou na pozici @import — pozdější zdroj vyhrává");
    }

    [Fact]
    public void An_Import_Cycle_Terminates()
    {
        const string css = "@import \"self.css\"; .tm-widget { color: red; }";

        var act = () => CssCascade.ParseStylesheet(css, _ => css);

        act.Should().NotThrow("cyklický import se expanduje jednou — ne donekonečna");
    }

    [Fact]
    public void An_Import_With_A_Media_Tail_Stays_Conditioned()
    {
        const string css =
            """
            .tm-widget { color: red; }
            @import "mobile.css" (max-width: 768px);
            """;

        CssCascade.Winning(css, Chain, "color",
                media: new CssCascade.MediaContext(WidthPx: 1440),
                importResolver: _ => ".tm-widget { color: blue; }")
            .Should().Be("red", "import s media podmínkou na desktopu neexistuje");
        CssCascade.Winning(css, Chain, "color",
                media: new CssCascade.MediaContext(WidthPx: 360),
                importResolver: _ => ".tm-widget { color: blue; }")
            .Should().Be("blue", "na 360 px importované pravidlo platí a pozdější pořadí vítězí");
    }

    [Fact]
    public void An_Unlayered_Rule_Beats_Every_Layered_Rule()
    {
        const string css =
            """
            @layer components {
                .tm-widget { color: blue; }
            }
            .tm-widget { color: red; }
            """;

        CssCascade.Winning(css, Chain, "color").Should().Be("red",
            "nevrstvené pravidlo vítězí nad každou vrstvou bez ohledu na pořadí v textu");
    }

    [Fact]
    public void A_Later_Layer_Beats_An_Earlier_Layer_Regardless_Of_Specificity()
    {
        // The later layer wins even against a MORE specific selector in the earlier layer — that
        // is the entire point of @layer, and a cascade that compares specificity first inverts it.
        const string css =
            """
            @layer first {
                div.tm-widget { color: blue; }
            }
            @layer second {
                .tm-widget { color: green; }
            }
            """;

        CssCascade.Winning(css, Chain, "color").Should().Be("green",
            "později deklarovaná vrstva vítězí i nad specifičtějším selektorem dřívější vrstvy");
    }

    [Fact]
    public void A_Layer_Order_Statement_Binds_Blocks_Written_Later()
    {
        const string css =
            """
            @layer a, b;
            @layer b { .tm-widget { color: green; } }
            @layer a { .tm-widget { color: blue; } }
            """;

        CssCascade.Winning(css, Chain, "color").Should().Be("green",
            "pořadí vrstev říká @layer a, b; — b vítězí, i když jeho blok je v textu dřív");
    }

    [Fact]
    public void Rules_Inside_A_Layer_Block_Carry_Its_Rank()
    {
        const string css =
            """
            @layer components {
                .tm-widget { color: blue; }
            }
            """;

        var rules = CssCascade.ParseStylesheet(css).Rules;
        rules.Should().ContainSingle().Which.LayerName.Should().Be("components",
            "pravidlo z @layer bloku musí nést svou vrstvu, jinak se kaskáda počítá bez ní");
    }

    [Fact]
    public void A_Nested_Layer_Qualifies_Its_Name()
    {
        const string css =
            """
            @layer outer {
                @layer inner {
                    .tm-widget { color: blue; }
                }
            }
            """;

        var rules = CssCascade.ParseStylesheet(css).Rules;
        rules.Should().ContainSingle().Which.LayerName.Should().Be("outer.inner",
            "vnorená vrstva se kvalifikuje — outer.inner je jiná vrstva než inner");
    }

    [Fact]
    public void A_Container_Block_Is_Entered_But_Undecidable()
    {
        // @container gates on a size the model cannot measure — its rules must be READ (they are
        // selectors) but reported undecidable, never assumed off like a failed @media.
        const string css =
            """
            .tm-widget { color: red; }
            @container (min-width: 200px) {
                .tm-widget { color: blue; }
            }
            """;

        var resolved = CssCascade.Resolve(css, Chain, "color",
            media: new CssCascade.MediaContext(WidthPx: 1440));

        resolved.Unmodelled.Should().NotBeEmpty(
            "pravidlo za @container je NEROZHODNUTELNÉ — ne vypnuté ani aplikované");
    }

    [Fact]
    public void Nested_Media_Inside_Media_Joins_The_Conditions()
    {
        const string css =
            """
            .tm-widget { color: red; }
            @media screen {
                @media (max-width: 768px) {
                    .tm-widget { color: blue; }
                }
            }
            """;

        CssCascade.Winning(css, Chain, "color",
                media: new CssCascade.MediaContext(WidthPx: 1440))
            .Should().Be("red", "vnořené @media nesmí zkolabovat na bezpodmínečné");
        CssCascade.Winning(css, Chain, "color",
                media: new CssCascade.MediaContext(WidthPx: 360))
            .Should().Be("blue");
    }

    [Fact]
    public void A_Nested_Media_Inside_A_Rule_Keeps_The_Selector()
    {
        // CSS nesting: a @media block inside a rule body conditions declarations of the SAME
        // element — dropping the selector context would read them as orphan rules.
        const string css =
            """
            .tm-widget {
                color: red;
                @media (max-width: 768px) {
                    color: blue;
                }
            }
            """;

        CssCascade.Winning(css, Chain, "color",
                media: new CssCascade.MediaContext(WidthPx: 360))
            .Should().Be("blue", "vnořené @media uvnitř pravidla podmiňuje deklarace téhož prvku");
        CssCascade.Winning(css, Chain, "color",
                media: new CssCascade.MediaContext(WidthPx: 1440))
            .Should().Be("red");
    }

    [Fact]
    public void A_Nested_Selector_Expands_As_A_Descendant()
    {
        const string css =
            """
            .tm-widget {
                color: red;
                .tm-child { color: blue; }
            }
            """;

        var chain = new[]
        {
            new CssCascade.Element("div", "tm-widget"),
            new CssCascade.Element("span", "tm-child"),
        };

        CssCascade.Winning(css, chain, "color").Should().Be("blue",
            ".tm-widget { .tm-child {…} } kompiluje na '.tm-widget .tm-child' — jako prohlížeč");
    }

    [Fact]
    public void An_Ampersand_Stands_For_The_Parent_Selector()
    {
        const string css =
            """
            .tm-widget {
                color: red;
                &:hover { color: blue; }
            }
            """;

        CssCascade.Winning(css, Chain, "color",
                activeStates: new HashSet<string>(StringComparer.Ordinal) { ":hover" })
            .Should().Be("blue", "& se expanduje na rodičovský selektor — .tm-widget:hover");
    }

    [Fact]
    public void Declarations_Split_By_A_Nested_Block_Keep_Source_Order()
    {
        // ".a { x; nested{}; y }" emits .a{x}, nested, .a{y} — flattening the order would let
        // the first declaration win a tie it must lose.
        const string css =
            """
            .tm-widget {
                color: red;
                @media screen {
                    color: green;
                }
                color: blue;
            }
            """;

        CssCascade.Winning(css, Chain, "color",
                media: new CssCascade.MediaContext(WidthPx: 1440))
            .Should().Be("blue", "deklarace za vnořeným blokem stojí v kaskádě později — musí vyhrát");
    }

    [Fact]
    public void The_Last_Declaration_Needs_No_Semicolon()
    {
        const string css = ".tm-widget { color: red }";

        CssCascade.Winning(css, Chain, "color").Should().Be("red",
            "poslední deklarace bez ';' je platná CSS — zahodit ji je číst prázdné pravidlo");
    }

    [Fact]
    public void A_Statement_Inside_A_Rule_Body_Is_Reported_Not_Dropped()
    {
        const string css = ".tm-widget { color: red; @unknown thing; }";

        var outcome = CssCascade.ParseStylesheet(css);

        outcome.Unmodelled.Should().ContainSingle(
            "at-statement uvnitř těla pravidla je mimo model — nahlásit, ne spolknout");
    }

    [Fact]
    public void An_Unsupported_Ancestor_Combinator_Stays_Fail_Closed()
    {
        const string css =
            """
            .tm-shell > .tm-widget { color: blue; }
            .tm-widget { color: red; }
            """;

        var resolved = CssCascade.Resolve(css, Chain, "color");

        resolved.Unmodelled.Should().NotBeEmpty(
            "'>' na předkovi je mimo model — nikdy nesmí projít jako „nematchuje“");
    }
}
