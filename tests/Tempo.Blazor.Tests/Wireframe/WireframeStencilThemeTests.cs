using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Wireframe.Export;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Decision WIREFRAME-STENCIL-THEME: the pack themes <c>indigo</c> / <c>indigo-dark</c>, the <c>StencilTheme</c> parameter of the editor, the
/// canvas and the toolbox previews, the resolution order StencilTheme → page / document <c>TargetTheme</c> → default tokens, and an unknown
/// theme = default tokens + ONE log warning. <c>StencilTokenScope</c> stays internal.
/// </summary>
public sealed class WireframeStencilThemeTests : LocalizationTestBase
{
    private const string DefaultBlue = "#3b82f6";   // pack default color.primary
    private const string IndigoPrimary = "#6366f1"; // Tailwind indigo-500 (core indigo scale, the step the blue-500 default maps to)

    private static WireframeComponentRegistry BuiltIn()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        return registry;
    }

    private static string MarkupOf(RenderFragment fragment)
    {
        using var ctx = new BunitContext();
        return ctx.Render(fragment).Markup;
    }

    private static string RenderButton(WireframeComponentRegistry registry, string? theme)
    {
        var def = registry.GetDef("TmButton")!;
        var el = new WireframeElement { Id = "b", Type = "TmButton", W = 120, H = 36 };
        el.Props["variant"] = System.Text.Json.JsonSerializer.SerializeToElement("primary");
        using var scope = StencilThemeScope.Use(theme);
        return MarkupOf(b => def.RenderSvg(el, b));
    }

    // ── The pack ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuiltInPack_HasTheIndigoAndIndigoDarkThemes_ThatOnlyOverrideDefinedTokens()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());

        pack.Themes.Keys.Should().Contain(["dark", "indigo", "indigo-dark"]);
        foreach (var name in new[] { "indigo", "indigo-dark" })
            pack.Themes[name].Keys.Should().OnlyContain(k => pack.Tokens.ContainsKey(k), $"{name} must not override a token nobody reads");
    }

    [Fact]
    public void Indigo_RecolorsThePrimaryAndInfoFamilies_FromTheCoreIndigoScale()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());
        var indigo = pack.Themes["indigo"];

        indigo["color.primary"].Should().Be(IndigoPrimary);
        indigo["color.primary.subtle"].Should().Be("#e0e7ff");     // indigo-100
        indigo["color.primary.border"].Should().Be("#a5b4fc");     // indigo-300
        indigo["color.primary.text"].Should().Be("#3730a3");       // indigo-800
        indigo["color.info"].Should().Be("#4f46e5");               // indigo-600
        indigo["color.info.subtle"].Should().Be("#e0e7ff");
        indigo["color.info.border"].Should().Be("#a5b4fc");
    }

    [Fact]
    public void IndigoDark_IsTheDarkTheme_WithTheIndigoPrimaryFamily()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());
        var dark = pack.Themes["dark"];
        var indigoDark = pack.Themes["indigo-dark"];

        foreach (var (key, value) in dark.Where(kv => !kv.Key.StartsWith("color.info", StringComparison.Ordinal)))
            indigoDark[key].Should().Be(value, $"indigo-dark keeps the dark value of {key}");
        indigoDark["color.primary"].Should().Be(IndigoPrimary);
        indigoDark["color.primary.subtle"].Should().StartWith("rgba(99,102,241");
        indigoDark["color.primary.text"].Should().Be("#a5b4fc");
        indigoDark["color.info"].Should().NotBe(dark["color.info"], "the info family follows the indigo scale too");
    }

    // ── Resolution order ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_Order_ParameterThenPageThenDocumentThenDefault()
    {
        StencilThemeScope.Resolve("indigo", "dark", "indigo-dark", null).Should().Be("indigo", "the parameter wins");
        StencilThemeScope.Resolve(null, "dark", "indigo-dark", null).Should().Be("dark", "a page theme refines the document");
        StencilThemeScope.Resolve(null, null, "indigo-dark", null).Should().Be("indigo-dark", "TargetTheme is used now");
        StencilThemeScope.Resolve(null, null, null, null).Should().BeNull("no theme = default tokens");
        StencilThemeScope.Resolve("  ", "", null, null).Should().BeNull("blank counts as unset");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("default")]
    [InlineData("LIGHT")]
    public void Resolve_LightAndDefault_AreTheDefaultTokens_WithoutAWarning(string name)
    {
        var logger = new ListLogger();
        StencilThemeScope.Resolve(name, null, null, logger).Should().BeNull();
        logger.Messages.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_IsCaseInsensitive_AndReturnsTheCanonicalName()
    {
        _ = BuiltIn().GetAll().Count();   // compiling the pack registers its theme names
        StencilThemeScope.Resolve("INDIGO-Dark", null, null, null).Should().Be("indigo-dark");
    }

    [Fact]
    public void Resolve_UnknownTheme_FallsBackToDefault_AndWarnsExactlyOncePerName()
    {
        _ = BuiltIn().GetAll().Count();
        var logger = new ListLogger();
        var name = "no-such-theme-" + Guid.NewGuid().ToString("N");

        StencilThemeScope.Resolve(name, null, null, logger).Should().BeNull();
        StencilThemeScope.Resolve(name, null, null, logger).Should().BeNull();
        StencilThemeScope.Resolve(null, null, name, logger).Should().BeNull();

        logger.Messages.Should().ContainSingle().Which.Should().Contain(name);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("a/b")]
    [InlineData("<script>")]
    [InlineData("x\ny")]
    [InlineData("caf\u00e9")]
    public void Resolve_MalformedName_IsTheDefaultTokens_NeverWarnedNeverStored(string name)
    {
        var logger = new ListLogger();

        StencilThemeScope.Resolve(name, null, null, logger).Should().BeNull();

        logger.Messages.Should().BeEmpty("a malformed name is not echoed into a log");
        using (StencilThemeScope.Use(name)) StencilThemeScope.CurrentName.Should().BeNull();
    }

    [Fact]
    public void Resolve_OverLongName_IsTheDefaultTokens_Silently_AndTheLimitItselfIsAccepted()
    {
        var logger = new ListLogger();

        StencilThemeScope.Resolve(new string('a', WireframeStencilThemeName.MaxLength + 1), null, null, logger).Should().BeNull();
        logger.Messages.Should().BeEmpty();

        WireframeStencilThemeName.IsValid(new string('a', WireframeStencilThemeName.MaxLength)).Should().BeTrue();
        WireframeStencilThemeName.IsValid(new string('a', WireframeStencilThemeName.MaxLength + 1)).Should().BeFalse();
        WireframeStencilThemeName.IsValid("indigo-dark_1.2").Should().BeTrue();
        WireframeStencilThemeName.IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void Resolve_ManyDistinctUnknownNames_AreCapped_WithOneSuppressionLine()
    {
        StencilThemeScope.ResetWarnedForTests();
        try
        {
            var logger = new ListLogger();
            for (var i = 0; i < StencilThemeScope.MaxWarnedNames * 3; i++)
                StencilThemeScope.Resolve("unknown-" + i, null, null, logger).Should().BeNull();

            logger.Messages.Count(m => m.Contains("suppressed")).Should().Be(1);
            logger.Messages.Should().HaveCount(StencilThemeScope.MaxWarnedNames + 1, "64 names, then ONE suppression line");

            // Without a logger nothing is stored at all.
            StencilThemeScope.ResetWarnedForTests();
            for (var i = 0; i < StencilThemeScope.MaxWarnedNames * 3; i++)
                StencilThemeScope.Resolve("silent-" + i, null, null, logger: null).Should().BeNull();
            var after = new ListLogger();
            StencilThemeScope.Resolve("fresh-name", null, null, after).Should().BeNull();
            after.Messages.Should().ContainSingle("the null-logger calls stored nothing, so the cap is still open");
        }
        finally { StencilThemeScope.ResetWarnedForTests(); }
    }

    [Fact]
    public void Resolve_KnowsTheBuiltInThemes_EvenWhenACustomPackCompiledFirst()
    {
        // A custom pack registering its themes first must not stop the built-in ones from being seeded (the old Known.IsEmpty check did).
        StencilThemeScope.Register(["brand-first"]);

        StencilThemeScope.Resolve("indigo-dark", null, null, null).Should().Be("indigo-dark");
        StencilThemeScope.Resolve("brand-first", null, null, null).Should().Be("brand-first");
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    [Fact]
    public void Stencil_DefaultRender_IsTheBlue_AndIndigoRecolorsIt()
    {
        var registry = BuiltIn();

        var plain = RenderButton(registry, null);
        var indigo = RenderButton(registry, "indigo");

        plain.Should().Contain(DefaultBlue).And.NotContain(IndigoPrimary);
        indigo.Should().Contain(IndigoPrimary).And.NotContain(DefaultBlue);
    }

    [Fact]
    public void Stencil_IndigoDark_PaintsTheDarkSurface_WithTheIndigoPrimary()
    {
        var registry = BuiltIn();
        var def = registry.GetDef("TmCard")!;
        var el = new WireframeElement { Id = "c", Type = "TmCard", W = 200, H = 120 };

        string markup;
        using (StencilThemeScope.Use("indigo-dark")) markup = MarkupOf(b => def.RenderSvg(el, b));

        markup.Should().Contain("#1e293b", "the dark surface");
        markup.Should().NotContain("#ffffff");
    }

    [Fact]
    public void Stencil_TheThemeNeverLeaksPastItsScope()
    {
        var registry = BuiltIn();
        using (StencilThemeScope.Use("indigo")) { }

        StencilThemeScope.CurrentName.Should().BeNull();
        RenderButton(registry, null).Should().Contain(DefaultBlue);
    }

    [Fact]
    public void CustomPack_WithItsOwnTheme_IsThemedToo_AndAnUnrelatedThemeFallsBackToItsDefaults()
    {
        var pack = new StencilPack
        {
            Format = "tempo-stencil", FormatVersion = 1, Id = "acme", Namespace = "acme",
            Tokens = new Dictionary<string, string> { ["color.primary"] = "#111111" },
            Themes = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["brand"] = new Dictionary<string, string> { ["color.primary"] = "#ff00aa" },
            },
            Components =
            [
                new StencilComponent
                {
                    Type = "Chip", DisplayName = "Chip", Category = "Acme", DefaultSize = new StencilSize { Width = 40, Height = 20 },
                    Render = StencilPackSerializer.Deserialize("""
                        {"format":"tempo-stencil","formatVersion":1,"id":"x","namespace":"x","components":[{"type":"X","displayName":"X","category":"X","defaultSize":{"width":1,"height":1},
                         "render":{"kind":"rect","x":0,"y":0,"w":"size.w","h":"size.h","fill":"token(\"color.primary\")"}}]}
                        """).Components[0].Render,
                },
            ],
        };
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new StencilPackComponentProvider(pack, "acme"));
        var def = registry.GetAll(WireframeComponentScope.ForApp("acme")).Single(d => d.PackId == "acme");
        var el = new WireframeElement { Id = "c", Type = def.Type, W = 40, H = 20 };

        string themed, other, plain;
        using (StencilThemeScope.Use("brand")) themed = MarkupOf(b => def.RenderSvg(el, b));
        using (StencilThemeScope.Use("indigo")) other = MarkupOf(b => def.RenderSvg(el, b));
        plain = MarkupOf(b => def.RenderSvg(el, b));

        themed.Should().Contain("#ff00aa");
        other.Should().Contain("#111111", "a pack without that theme keeps its own defaults");
        plain.Should().Contain("#111111");
    }

    // ── Canvas, toolbox, editor ───────────────────────────────────────────────

    private IRenderedComponent<TmWireframeDesignerCanvas> RenderCanvas(WireframeDocument doc, Action<ComponentParameterCollectionBuilder<TmWireframeDesignerCanvas>>? configure = null)
    {
        Services.AddSingleton(BuiltIn());
        return Render<TmWireframeDesignerCanvas>(p => { p.Add(c => c.Document, doc); configure?.Invoke(p); });
    }

    private static WireframeDocument DocWithPrimaryButton(string? targetTheme = null)
    {
        var doc = new WireframeDocument { Title = "T", Width = 400, Height = 300, TargetTheme = targetTheme };
        var el = WireframeDocumentExtensions.NewElement("TmButton", 20, 20, 120, 36, []);
        el.Props["variant"] = System.Text.Json.JsonSerializer.SerializeToElement("primary");
        doc.Elements.Add(el);
        return doc;
    }

    [Fact]
    public void Canvas_NoTheme_DrawsTheDefaultBlue()
        => RenderCanvas(DocWithPrimaryButton()).Markup.Should().Contain(DefaultBlue).And.NotContain(IndigoPrimary);

    [Fact]
    public void Canvas_StencilThemeParameter_Recolors()
        => RenderCanvas(DocWithPrimaryButton(), p => p.Add(c => c.StencilTheme, "indigo"))
            .Markup.Should().Contain(IndigoPrimary).And.NotContain(DefaultBlue);

    [Fact]
    public void Canvas_DocumentTargetTheme_IsNowUsed()
        => RenderCanvas(DocWithPrimaryButton("indigo")).Markup.Should().Contain(IndigoPrimary);

    [Fact]
    public void Canvas_TheParameterWinsOverTheDocumentTargetTheme()
    {
        var cut = RenderCanvas(DocWithPrimaryButton("indigo"), p => p.Add(c => c.StencilTheme, "dark"));

        cut.Markup.Should().NotContain(IndigoPrimary, "the parameter (dark) beat the document theme (indigo)");
        cut.Markup.Should().Contain("#f1f5f9", "the dark theme paints light ink");
    }

    [Fact]
    public void Canvas_ChangingTheParameter_ReRendersTheElements()
    {
        var doc = DocWithPrimaryButton();
        var cut = RenderCanvas(doc);
        cut.Markup.Should().Contain(DefaultBlue);

        cut.Render(p => p.Add(c => c.StencilTheme, "indigo"));

        cut.Markup.Should().Contain(IndigoPrimary).And.NotContain(DefaultBlue);
    }

    [Fact]
    public void Canvas_GatedRender_StillSeesAThemeChange()
    {
        var cut = RenderCanvas(DocWithPrimaryButton(), p => p.Add(c => c.LightEditing, true));
        cut.Markup.Should().Contain(DefaultBlue);

        cut.Render(p => p.Add(c => c.StencilTheme, "indigo"));

        cut.Markup.Should().Contain(IndigoPrimary, "the theme is part of the render snapshot");
    }

    [Fact]
    public void Canvas_UnknownTheme_DrawsTheDefaults()
        => RenderCanvas(DocWithPrimaryButton(), p => p.Add(c => c.StencilTheme, "nope-" + Guid.NewGuid().ToString("N")))
            .Markup.Should().Contain(DefaultBlue);

    [Fact]
    public void Editor_PassesItsThemeToTheCanvas_AndTheToolboxPreviews()
    {
        Services.AddSingleton(BuiltIn());
        var cut = Render<TmWireframeEditor>(p =>
        {
            p.Add(e => e.Document, DocWithPrimaryButton());
            p.Add(e => e.LayoutMode, TmLayoutMode.Desktop);
            p.Add(e => e.StencilTheme, "indigo");
        });

        cut.FindComponent<TmWireframeDesignerCanvas>().Instance.StencilTheme.Should().Be("indigo");
        cut.FindComponent<TmWireframeToolbox>().Instance.StencilTheme.Should().Be("indigo");
        cut.Find(".tm-wd-canvas__svg").InnerHtml.Should().Contain(IndigoPrimary);
    }

    [Fact]
    public void Editor_WithoutTheStencilTheme_FollowsTheDocumentTargetTheme()
    {
        Services.AddSingleton(BuiltIn());
        var cut = Render<TmWireframeEditor>(p =>
        {
            p.Add(e => e.Document, DocWithPrimaryButton("indigo-dark"));
            p.Add(e => e.LayoutMode, TmLayoutMode.Desktop);
        });

        cut.Find(".tm-wd-canvas__svg").InnerHtml.Should().Contain(IndigoPrimary).And.Contain("#f1f5f9");
    }

    [Fact]
    public void Toolbox_PreviewTiles_FollowTheTheme()
    {
        var registry = new WireframeComponentRegistry();
        var noIcon = BuiltIn().GetDef("TmButton")!;
        registry.RegisterDefinition(new WireframeComponentDef
        {
            Type = "TmButton", DisplayName = "Button", Category = "Buttons", Icon = null,
            DefaultWidth = 120, DefaultHeight = 36, Props = noIcon.Props, RenderSvg = noIcon.RenderSvg, IsBuiltIn = true,
        });
        Services.AddSingleton(registry);

        var plain = Render<TmWireframeToolbox>();
        var themed = Render<TmWireframeToolbox>(p => p.Add(t => t.StencilTheme, "indigo"));

        plain.FindAll(".tm-wd-toolbox__item-preview svg").Should().NotBeEmpty();
        themed.FindAll(".tm-wd-toolbox__item-preview svg").Should().NotBeEmpty();
        string.Concat(themed.FindAll(".tm-wd-toolbox__item-preview svg").Select(s => s.InnerHtml)).Should().NotContain(DefaultBlue);
        string.Concat(plain.FindAll(".tm-wd-toolbox__item-preview svg").Select(s => s.InnerHtml)).Should().NotContain(IndigoPrimary);
    }

    // ── Headless / server rendering and export ────────────────────────────────

    private static WireframeSvgRenderer Renderer(WireframeComponentRegistry registry)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new WireframeSvgRenderer(registry, services);
    }

    [Fact]
    public async Task HeadlessRenderer_OptionsStencilTheme_Recolors_AndFallsBackToTheDocumentTheme()
    {
        var registry = BuiltIn();
        var renderer = Renderer(registry);
        var doc = DocWithPrimaryButton();
        doc.Pages.Clear();
        var page = new WireframePage { Id = "p", Name = "P", Width = 400, Height = 300 };
        page.Elements.AddRange(DocWithPrimaryButton().Elements);
        doc.Pages.Add(page);

        var plain = await renderer.RenderPageAsync(page);
        var viaOptions = await renderer.RenderPageAsync(page, options: new WireframePageSvgOptions { StencilTheme = "indigo" });
        page.TargetTheme = "indigo";
        var viaPage = await renderer.RenderPageAsync(page);
        var all = await renderer.RenderDocumentAsync(new WireframeDocument { TargetTheme = "indigo", Pages = [new WireframePage { Id = "q", Name = "Q", Width = 400, Height = 300, Elements = page.Elements }] });

        plain.Should().Contain(DefaultBlue);
        viaOptions.Should().Contain(IndigoPrimary).And.NotContain(DefaultBlue);
        viaPage.Should().Contain(IndigoPrimary, "a page TargetTheme is honoured");
        all.Single().Svg.Should().Contain(IndigoPrimary, "RenderDocumentAsync resolves the document TargetTheme");
    }

    [Fact]
    public void ExportRequest_CarriesAnOptionalTheme_NullByDefault()
    {
        new WireframeExportRequest().Theme.Should().BeNull();
        new WireframeExportRequest { Theme = "indigo-dark" }.Theme.Should().Be("indigo-dark");
    }

    // ── Contract ──────────────────────────────────────────────────────────────

    [Fact]
    public void StencilTokenScope_StaysInternal()
    {
        typeof(WireframeComponentRegistry).Assembly.GetType("Tempo.Blazor.Components.Wireframe.Stencil.StencilTokenScope")!
            .IsPublic.Should().BeFalse();
        typeof(StencilThemeScope).IsPublic.Should().BeFalse("the ambient theme is an implementation detail; hosts pass a StencilTheme string");
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Messages.Add(formatter(state, exception));
        }
    }
}
