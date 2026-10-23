using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Wireframe redesign plan ce19aa8c, phase 2 — the stencil renderers vs the tokenised editor.
/// The pack's <c>token("color.primary", "#3b82f6")</c> values are DOCUMENT-model tokens resolved by
/// <see cref="StencilTokenResolver"/> into explicit colours at render time (they are not CSS variables), and
/// the editor draws the page as theme-invariant paper — so a stencil is always light-on-paper and an export
/// never depends on a stylesheet. These tests pin that contract.
/// </summary>
public sealed class StencilPaperContractTests : LocalizationTestBase
{
    private static WireframeComponentRegistry BuiltInRegistry()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        return registry;
    }

    [Fact]
    public void EveryBuiltInStencil_RendersExplicitColours_NeverACssVariable()
    {
        var registry = BuiltInRegistry();
        var defs = registry.GetAll().ToList();
        defs.Should().NotBeEmpty();

        var offenders = new List<string>();
        foreach (var def in defs)
        {
            var element = new WireframeElement { Id = "p", Type = def.Type, W = def.DefaultWidth, H = def.DefaultHeight };
            var markup = MarkupOf(builder => def.RenderSvg(element, builder));
            if (markup.Contains("var(", StringComparison.Ordinal) || markup.Contains("--tm-", StringComparison.Ordinal))
                offenders.Add(def.Type);
        }

        offenders.Should().BeEmpty("a stencil must paint explicit colours: the PNG/PDF rasteriser has no stylesheet");
    }

    [Fact]
    public void DarkPackTheme_OnlyOverridesTokensThePackDefines()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());

        pack.Themes.Should().ContainKey("dark");
        pack.Themes["dark"].Keys.Should().OnlyContain(key => pack.Tokens.ContainsKey(key),
            "a dark override of a token the pack does not define would never be read");
        pack.Tokens.Keys.Should().Contain(["color.primary", "color.surface", "color.border", "color.text"]);
    }

    [Fact]
    public void Toolbox_PreviewTile_IsWrappedInAPaperTile()
    {
        var registry = BuiltInRegistry();
        Services.AddSingleton(registry);

        var cut = Render<TmWireframeToolbox>();

        var items = cut.FindAll(".tm-wd-toolbox__item");
        items.Should().NotBeEmpty();
        // A definition with an icon shows the icon tile; one without falls back to the mini stencil, which is light
        // and therefore always sits on a paper tile, so a dark theme never paints a white box on a dark card.
        items.Should().OnlyContain(item =>
                item.QuerySelector(".tm-wd-toolbox__item-icon svg") != null
                || item.QuerySelector(".tm-wd-toolbox__item-preview-paper > .tm-wd-toolbox__item-preview svg") != null,
            "every tile is either a component icon or a paper-wrapped mini stencil");
        items.Where(item => item.QuerySelector(".tm-wd-toolbox__item-icon") == null)
            .Should().OnlyContain(item => item.QuerySelector(".tm-wd-toolbox__item-preview-paper > .tm-wd-toolbox__item-preview svg") != null,
                "the light mini stencil sits on a paper tile, so a dark theme never paints a white box on a dark card");
    }

    private string MarkupOf(Action<RenderTreeBuilder> build)
    {
        var cut = base.Render((Microsoft.AspNetCore.Components.RenderFragment)(builder =>
        {
            builder.OpenElement(0, "svg");
            builder.AddContent(1, (Microsoft.AspNetCore.Components.RenderFragment)(inner => build(inner)));
            builder.CloseElement();
        }));
        return cut.Markup;
    }
}
