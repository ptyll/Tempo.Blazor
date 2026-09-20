using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Toolbar;

/// <summary>TDD tests for TmFormActionBar.</summary>
public class TmFormActionBarTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/Components/Toolbar/TmFormActionBar.razor.js";

    /// <summary>
    /// Lišta se NEHLÁSÍ jako toolbar, protože ovládání šipkami neumí.
    /// </summary>
    /// <remarks>
    /// NAHRAZUJE `FormActionBar_RendersAsToolbarRole`, který od 2.8.16 popisuje ZRUŠENÝ kontrakt.
    /// Není to ohnutí testu pod implementaci: `role="toolbar"` je příslib jediného tab stopu
    /// a pohybu šipkami, lišta roving tabindex nikdy neměla, takže ten test hlídal, že komponenta
    /// SLIBUJE něco, co nedělá. Změnila se specifikace, ne měřidlo.
    /// </remarks>
    [Fact]
    public void FormActionBar_DoesNotClaimToolbarRoleItCannotHonour()
    {
        var cut = Render<TmFormActionBar>(p => p.Add(x => x.AriaLabel, "Akce formuláře"));

        var root = cut.Find(".tm-form-action-bar");

        root.GetAttribute("role").Should().Be(
            "group",
            "dvě až tři akce nepotřebují toolbar; skupina neslibuje ovládání šipkami");
        root.GetAttribute("aria-label").Should().Be(
            "Akce formuláře",
            "zrušení role nesmí liště vzít přístupné jméno");
    }

    /// <summary>
    /// Hostitel musí umět zvednout lištu, aniž zvedne VŠECHNY sticky prvky.
    /// </summary>
    /// <remarks>
    /// Aplikace si jinak musí zvedat vlastní hladiny (sidebar, zástin, hlavička) nad lištu,
    /// protože přebít `--tm-z-sticky` by posunulo i TmTopBar a TmFab.
    /// </remarks>
    [Fact]
    public void FormActionBarCss_ZIndex_IsHostOverridableWithoutMovingEverySticky()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor", "Components", "Toolbar", "TmFormActionBar.razor.css"));

        foreach (var selector in new[]
                 {
                     ".tm-form-action-bar--sticky-top",
                     ".tm-form-action-bar--floating-bottom",
                     ".tm-form-action-bar--floating-bottom-md"
                 })
        {
            SelectorBlock(css, selector).Should().Contain(
                "z-index: var(--tm-form-action-bar-z-index, var(--tm-z-sticky));",
                "bez fallbacku by nenastavená proměnná lištu odhladinovala úplně");
        }
    }

    [Fact]
    public void FormActionBar_DefaultPosition_HasStaticClass()
    {
        var cut = Render<TmFormActionBar>();

        cut.Find(".tm-form-action-bar").ClassList.Should().Contain("tm-form-action-bar--static");
    }

    [Fact]
    public void FormActionBar_StickyTopPosition_HasStickyClass()
    {
        var cut = Render<TmFormActionBar>(p => p.Add(x => x.Position, FormActionBarPosition.StickyTop));

        cut.Find(".tm-form-action-bar").ClassList.Should().Contain("tm-form-action-bar--sticky-top");
    }

    [Fact]
    public void FormActionBar_FloatingBottomPosition_HasFloatingClass()
    {
        var cut = Render<TmFormActionBar>(p => p.Add(x => x.Position, FormActionBarPosition.FloatingBottom));

        cut.Find(".tm-form-action-bar").ClassList.Should().Contain("tm-form-action-bar--floating-bottom");
    }

    [Fact]
    public void FormActionBar_RendersAllFourActionAndStatusSlots()
    {
        var cut = Render<TmFormActionBar>(p => p
            .Add(x => x.ChildContent, "<span class='child'>Doc title</span>")
            .Add(x => x.Status, "<span class='status-text'>Saved</span>")
            .Add(x => x.PrimaryActions, "<button class='primary-btn'>Save</button>")
            .Add(x => x.SecondaryActions, "<button class='secondary-btn'>Cancel</button>")
            .Add(x => x.DangerActions, "<button class='danger-btn'>Delete</button>"));

        cut.Find(".tm-form-action-bar__start .child").TextContent.Should().Be("Doc title");
        cut.Find(".tm-form-action-bar__status .status-text").TextContent.Should().Be("Saved");
        cut.Find(".tm-form-action-bar__primary .primary-btn").TextContent.Should().Be("Save");
        cut.Find(".tm-form-action-bar__secondary .secondary-btn").TextContent.Should().Be("Cancel");
        cut.Find(".tm-form-action-bar__danger .danger-btn").TextContent.Should().Be("Delete");
    }

    [Fact]
    public void FormActionBar_NoActionsProvided_DoesNotRenderEndWrapper()
    {
        var cut = Render<TmFormActionBar>();

        cut.FindAll(".tm-form-action-bar__end").Should().BeEmpty();
    }

    [Fact]
    public void FormActionBar_AriaLabel_SetsAttribute()
    {
        var cut = Render<TmFormActionBar>(p => p.Add(x => x.AriaLabel, "Document actions"));

        cut.Find(".tm-form-action-bar").GetAttribute("aria-label").Should().Be("Document actions");
    }

    [Fact]
    public void FormActionBar_LiveMessage_RendersPoliteLiveRegion()
    {
        var cut = Render<TmFormActionBar>(p => p.Add(x => x.LiveMessage, "All changes saved"));

        var region = cut.Find("[aria-live='polite']");
        region.TextContent.Should().Be("All changes saved");
    }

    [Fact]
    public void FormActionBar_TestId_SetsDataTestId()
    {
        var cut = Render<TmFormActionBar>(p => p.Add(x => x.TestId, "save-bar"));

        cut.Find(".tm-form-action-bar").GetAttribute("data-testid").Should().Be("save-bar");
    }

    [Fact]
    public void FormActionBar_Class_AppendsAdditionalClass()
    {
        var cut = Render<TmFormActionBar>(p => p.Add(x => x.Class, "my-extra-class"));

        cut.Find(".tm-form-action-bar").ClassList.Should().Contain("my-extra-class");
    }

    [Fact]
    public void FormActionBar_ShowOnScrollTrue_AddsShowOnScrollClassAndRegistersScrollListenerModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("register", _ => true).SetVoidResult();

        var cut = Render<TmFormActionBar>(p => p.Add(x => x.ShowOnScroll, true));

        cut.Find(".tm-form-action-bar").ClassList.Should().Contain("tm-form-action-bar--show-on-scroll");
        module.Invocations.Should().Contain(invocation => invocation.Identifier == "register");
    }

    [Fact]
    public void FormActionBar_ShowOnScrollFalse_DoesNotAddClassOrRegisterListener()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("register", _ => true).SetVoidResult();

        var cut = Render<TmFormActionBar>(p => p.Add(x => x.ShowOnScroll, false));

        cut.Find(".tm-form-action-bar").ClassList.Should().NotContain("tm-form-action-bar--show-on-scroll");
        module.Invocations.Should().NotContain(invocation => invocation.Identifier == "register");
    }

    /// <summary>
    /// The floating bar spans the viewport, which runs it underneath a shell's fixed side navigation.
    /// The inset has to be a variable a host can set, because the alternative — overriding <c>left</c>
    /// from application CSS — has to out-specify this rule's <c>[b-*]</c> scope attribute and loses the
    /// cascade whenever it merely ties. Asserted against the source file rather than a rendered element:
    /// bUnit has no layout, so nothing about the cascade is observable from a rendered component.
    /// </summary>
    [Fact]
    public void FormActionBarCss_FloatingBottomInset_IsHostOverridableViaCustomProperty()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor", "Components", "Toolbar", "TmFormActionBar.razor.css"));

        var floating = SelectorBlock(css, ".tm-form-action-bar--floating-bottom");

        // Logical properties, so the variable names describe what they actually do — in LTR they
        // resolve to the same left/right the released version pinned.
        floating.Should().Contain("inset-inline-start: var(--tm-form-action-bar-inset-inline-start, 0);");
        floating.Should().Contain("inset-inline-end: var(--tm-form-action-bar-inset-inline-end, 0);");
        // Unset, the fallback keeps the released full-bleed behaviour.
        floating.Should().NotContain("left: 0;");
        floating.Should().NotContain("right: 0;");
    }

    /// <summary>
    /// The floating bar must reset the base <c>width: 100%</c>, or the two insets and the width form an
    /// over-constrained box and the browser drops the end inset in LTR.
    /// </summary>
    /// <remarks>
    /// This is the regression test for a P1 shipped in 2.8.13 and 2.8.14. With <c>width: 100%</c> the
    /// containing block of a fixed element is the viewport, so on a 1440px window the bar resolved to
    /// 296…1736px once a host set <c>--tm-form-action-bar-inset-inline-start: 18.5rem</c> — 296px of it
    /// hanging off the right edge. Because <c>__end</c> right-aligns the actions, the primary button
    /// landed entirely off-screen, and a fixed element contributes nothing to the document's scrollable
    /// area, so no horizontal scrollbar appeared either: the button was unreachable by mouse.
    /// The inset test above passed the whole time because it only ever checked the START inset.
    /// </remarks>
    [Fact]
    public void FormActionBarCss_FloatingBottom_ResetsBaseWidthSoBothInsetsDecideTheBox()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor", "Components", "Toolbar", "TmFormActionBar.razor.css"));

        SelectorBlock(css, ".tm-form-action-bar").Should().Contain(
            "width: 100%;",
            "the base class intentionally fills its parent — the floating variant is the one that must opt out");

        SelectorBlock(css, ".tm-form-action-bar--floating-bottom").Should().Contain(
            "width: auto;",
            "start inset + width + end inset is over-constrained; without the reset the browser drops the "
            + "end inset and the bar overflows the viewport by exactly the start inset");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Slot `Status` je GENERICKÝ a nesmí si o obsahu nic domýšlet (registr 2.8.16, položka 10).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generický slot nesmí barvit obsah, o kterém nic neví.
    /// </summary>
    /// <remarks>
    /// Do 2.8.15 měl <c>.tm-form-action-bar__status</c> natvrdo <c>color: var(--tm-color-success-text)</c>,
    /// přestože parametr se jmenuje <c>Status</c>, ne <c>SuccessStatus</c>. Hlášení chyby u tlačítka
    /// tedy bylo ZELENÉ — barva tvrdila opak textu. Knihovna diktovala význam obsahu, který nezná.
    /// Hostitelská aplikace to musela obcházet tím, že barvu deklarovala na vlastním potomkovi slotu,
    /// protože přebít scoped pravidlo z aplikačního CSS nejde (DEC-TEMPO-CSS-OVERRIDE-CONTRACT).
    /// </remarks>
    [Fact]
    public void FormActionBarCss_GenericStatusSlot_DoesNotHardcodeSuccessColour()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor", "Components", "Toolbar", "TmFormActionBar.razor.css"));

        SelectorBlock(css, ".tm-form-action-bar__status").Should().NotContain(
            "color:",
            "slot je generický — barvu dědí, a sémantiku vyjadřuje parametr StatusSeverity");
    }

    [Fact]
    public void FormActionBar_StatusWithoutSeverity_CarriesNoSeverityModifier()
    {
        var cut = Render<TmFormActionBar>(p => p.Add(x => x.Status, "uloženo"));

        cut.Find(".tm-form-action-bar__status").ClassList.Should().HaveCount(
            1,
            "bez udané závažnosti nemá komponenta co tvrdit, takže nepřidá žádný modifikátor");
    }

    [Theory]
    [InlineData(FormActionBarStatusSeverity.Success, "tm-form-action-bar__status--success")]
    [InlineData(FormActionBarStatusSeverity.Warning, "tm-form-action-bar__status--warning")]
    [InlineData(FormActionBarStatusSeverity.Error, "tm-form-action-bar__status--error")]
    [InlineData(FormActionBarStatusSeverity.Info, "tm-form-action-bar__status--info")]
    public void FormActionBar_StatusSeverity_MarksSlotWithMatchingModifier(
        FormActionBarStatusSeverity severity, string expectedClass)
    {
        var cut = Render<TmFormActionBar>(p => p
            .Add(x => x.Status, "hláška")
            .Add(x => x.StatusSeverity, severity));

        cut.Find(".tm-form-action-bar__status").ClassList.Should().Contain(expectedClass);
    }

    /// <summary>
    /// Každá závažnost musí mít v CSS vlastní barvu — jinak by parametr byl afordance bez mechanismu,
    /// tedy přesně ta třída vady, kvůli které se tenhle řádek registru otevřel.
    /// </summary>
    [Theory]
    [InlineData("success")]
    [InlineData("warning")]
    [InlineData("error")]
    [InlineData("info")]
    public void FormActionBarCss_EverySeverityModifier_DeclaresItsOwnColour(string severity)
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor", "Components", "Toolbar", "TmFormActionBar.razor.css"));

        SelectorBlock(css, $".tm-form-action-bar__status--{severity}").Should().Contain("color:");
    }

    /// <summary>
    /// Výšku lišty zná jen Tempo, tak ji Tempo publikuje — a ODVOZENĚ, ne jako pixelové číslo.
    /// </summary>
    /// <remarks>
    /// `position: fixed` do toku nepřispívá, takže stránka pod lištou musí nechat rezervu.
    /// Dokud token neexistoval, hostitel ji hádal — v jedné aplikaci ji hádalo šest stránek
    /// nezávisle. Test hlídá obojí: že token existuje, a že je složený z týchž tokenů jako
    /// lišta. Opsané číslo v pixelech by se při změně kteréhokoli vstupu rozešlo TIŠE.
    /// </remarks>
    [Fact]
    public void Tokens_FormActionBarReserve_IsPublishedAndDerivedFromTheBarsOwnTokens()
    {
        var tokens = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "tokens.css"));

        tokens.Should().Contain("--tm-form-action-bar-reserve-block-size:");

        var declarations = tokens.Split("--tm-form-action-bar-reserve-block-size:")
            .Skip(1)
            .Select(part => part[..part.IndexOf(';', StringComparison.Ordinal)].Trim())
            .ToList();

        declarations.Should().HaveCount(
            3,
            "desktopová hodnota, dvouřadová varianta pod 768 px a nulová varianta pod 768 px "
            + "pro FloatingBottomFromMd, kde lišta leží v toku dokumentu");

        var sized = declarations.Where(d => d != "0").ToList();
        sized.Should().HaveCount(2, "dvě deklarace nesou výšku lišty, třetí rezervu VYPNULÁ");
        foreach (var declaration in sized)
        {
            declaration.Should().Contain("var(--tm-input-height-md)");
            declaration.Should().Contain("var(--tm-space-2)");
            declaration.Should().MatchRegex(
                @"^[^0-9]*(2px|[0-9]+ \* var|var)",
                "jediné holé číslo smí být rámeček 1px na každé straně — všechno ostatní jde z tokenů");
        }

        declarations.Should().Contain(
            "0",
            "pod breakpointem je responzivní lišta statická — rezerva se vypíná S REŽIMEM, "
            + "jinak pod breakpointem zůstane mrtvé místo (gap registr #13)");

        tokens.Should().Contain(
            "@media (max-width: 767.98px)",
            "media query musí být vedle bloku :root, ne v něm — vnořená není platné CSS "
            + "a proměnná by tiše zůstala na desktopové hodnotě");
    }

    /// <summary>
    /// Responzivní režim renderuje vlastní třídu — nesmí si půjčovat
    /// <c>--floating-bottom</c>, protože ta plovoucí deklarace platí VŠUDE a pod `md` by
    /// držela statickou lištu přišroubovanou na viewport.
    /// </summary>
    [Fact]
    public void FormActionBar_FloatingBottomFromMd_HasResponsiveClass_NotTheAlwaysFloatingOne()
    {
        var cut = Render<TmFormActionBar>(p => p.Add(
            x => x.Position, FormActionBarPosition.FloatingBottomFromMd));

        var classes = cut.Find(".tm-form-action-bar").ClassList;
        classes.Should().Contain("tm-form-action-bar--floating-bottom-md");
        classes.Should().NotContain(
            "tm-form-action-bar--floating-bottom",
            "ta třída plovoucí kontrakt aplikuje bez media query — pod md by barva zůstala fixed");
    }

    /// <summary>
    /// Responzivní varianta nese STEJNÝ plovoucí kontrakt jako trvalá — ale jen uvnitř
    /// <c>@media (min-width: 768px)</c>. Drift mezi oběma bloky by znamenal, že se responzivní
    /// lišta nad breakpointem chová jinak než trvale plovoucí — potichu, protože obě cesty
    /// deklarace kopírují.
    /// </summary>
    [Fact]
    public void FormActionBarCss_FloatingBottomFromMd_SharesTheFloatingContract_AboveMdOnly()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor", "Components", "Toolbar", "TmFormActionBar.razor.css"));

        var responsive = SelectorBlock(css, ".tm-form-action-bar--floating-bottom-md");
        // SelectorBlock hledá "selektor + ' {'" — "--floating-bottom {" trefí jen trvalou
        // variantu, responzivní končí "-md {".
        var always = SelectorBlock(css, ".tm-form-action-bar--floating-bottom");

        // Stejná sada deklarací — jediný rozdíl smí být médium, ne obsah.
        foreach (var declaration in new[]
                 {
                     "position: fixed;",
                     "width: auto;",
                     "inset-inline-end: var(--tm-form-action-bar-inset-inline-end, 0);",
                     "bottom: 0;",
                     "inset-inline-start: var(--tm-form-action-bar-inset-inline-start, 0);",
                     "z-index: var(--tm-form-action-bar-z-index, var(--tm-z-sticky));",
                 })
        {
            responsive.Should().Contain(declaration);
            always.Should().Contain(declaration);
        }

        // A pravidlo sedí UVNITŘ media query od 768 px nahoru — bez ní by třída plovala všude.
        SelectorBlock(
                MediaBlock(css, "@media (min-width: 768px)"),
                ".tm-form-action-bar--floating-bottom-md")
            .Should().Contain(
                "position: fixed;",
                "deklarace musí být uvnitř @media (min-width: 768px), jinak lišta plovoucí i pod md");

        // A jinde už žádný blok pro třídu není — mimo media query by pod breakpointem platil
        // jakýkoli plovoucí předpis z něj.
        var occurrences = css.Split(".tm-form-action-bar--floating-bottom-md {").Length - 1;
        occurrences.Should().Be(
            1,
            "jediná deklarace responzivní třídy je ta media-gated — druhá kopie mimo ni by "
            + "buď plovala pod breakpointem, nebo by přebíjela kontrakt bez testu");
    }

    /// <summary>
    /// Rezerva a režim jsou JEDNA knihovní hranice: pod breakpointem rezervu vypíná
    /// <c>:has(.tm-form-action-bar--floating-bottom-md)</c> na <c>:root</c>, nad breakpointem
    /// lištu přepne do <c>position: fixed</c> media query ve scoped stylu. Čísla
    /// <c>767.98px</c> a <c>768px</c> jsou dvě strany TÉHOŽ okraje — kdyby se rozešly
    /// (třeba 640 px v jednom souboru), vznikl by pás, kde lišta pluje bez rezervy, nebo
    /// stojí v toku a rezerva nechává mrtvé místo.
    /// </summary>
    [Fact]
    public void Tokens_FormActionBarReserve_ZeroesBelowTheSameBoundaryTheModeUses()
    {
        var tokens = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "tokens.css"));
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor", "Components", "Toolbar", "TmFormActionBar.razor.css"));

        // Vynulování sedí UVNITŘ téže media query jako dvouřadová rezerva: max-width: 767.98px.
        SelectorBlock(
                MediaBlock(tokens, "@media (max-width: 767.98px)"),
                ":root:has(.tm-form-action-bar--floating-bottom-md)")
            .Should().Contain(
                "--tm-form-action-bar-reserve-block-size: 0;",
                "statická lišta v toku nemá co rezervovat — rezerva se vypíná s režimem, "
                + "a to pod tou samou hranicí, jinak jsou to dvě čísla, která se mohou rozejít");

        css.Should().Contain(
            "@media (min-width: 768px)",
            "767.98 px pod a 768 px nad je TENTÝŽ okraj — breakpoint rezervy a režimu je jedno číslo");
    }

    /// <summary>Text of the first declaration block whose selector line starts with <paramref name="selector"/>.</summary>
    private static string SelectorBlock(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the CSS must still declare {0}", selector);

        var end = css.IndexOf('}', start);
        end.Should().BeGreaterThan(start);

        return css[start..end];
    }

    /// <summary>Inner text of the first <paramref name="mediaQuery"/> block (brace-matched).</summary>
    private static string MediaBlock(string css, string mediaQuery)
    {
        var start = css.IndexOf(mediaQuery + " {", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the CSS must still declare {0}", mediaQuery);

        var openBrace = css.IndexOf('{', start);
        var depth = 0;
        for (var i = openBrace; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return css[(openBrace + 1)..i];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after {mediaQuery}.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
