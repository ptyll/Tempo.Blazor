using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 4ce1b994 phase 2 (decision TOP-LAYER-PIN): <c>TmPinnedOnTop</c> is a wireframe citizen
/// (repository rules 14 and 15). A new component needs a UI role and a declarative stencil whose
/// render branches on the props the palette offers. The pinned element is status UI that stays
/// above every overlay, so the sketch draws a bar with its message and a "pinned on top" marker
/// that does not rely on colour alone.
/// </summary>
public sealed class PinnedOnTopStencilTests
{
    private static readonly string[] ExpectedProps = ["message", "placement", "priority", "visible"];

    [Fact]
    public void BuiltInSchema_ListsThePinnedOnTop_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmPinnedOnTop");

        schema.Category.Should().Be("Feedback");
        schema.DisplayName.Should().Be("Pinned On Top");
        schema.Roles.Should().BeEquivalentTo("pinned-on-top");
        schema.Props.Select(prop => prop.Name).Should().Equal(ExpectedProps);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void TheComponentsTypeNameAndSynonymsResolveItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmPinnedOnTop")!.Slug.Should().Be("pinned-on-top");
        vocabulary.Find("pinned-on-top")!.DisplayName.Should().Be("Pinned on top");
        vocabulary.Find("error bar")!.Slug.Should().Be("pinned-on-top");
    }

    [Fact]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmPinnedOnTop");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmPinnedOnTop");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the pin is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.Category.Should().Be(schema.Category);
        definition.DisplayName.Should().Be(schema.DisplayName);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public async Task Sketch_DrawsTheMessageAndATextualPinnedMarker()
    {
        var svg = await RenderAsync(("message", "Connection lost"));

        svg.Should().Contain(">Connection lost<");
        svg.Should().Contain(">Pinned on top<", "the overlay-proof behaviour is spelled out, not only tinted");
        svg.Should().NotContain(">TmPinnedOnTop<");
    }

    [Fact]
    public async Task Sketch_ShowsThePriorityOnlyWhenItIsNotTheDefaultBand()
    {
        (await RenderAsync(("priority", 100))).Should().Contain(">Priority 100<");
        (await RenderAsync(("priority", 0))).Should().NotContain(">Priority");
    }

    [Fact]
    public async Task Sketch_Hidden_DrawsAnEmptyOutlinedPlaceholderInsteadOfTheBar()
    {
        var hidden = await RenderAsync(("visible", false));

        hidden.Should().Contain(">Hidden until shown<");
        hidden.Should().Contain("fill='none'", "the placeholder is an empty outline, not a painted bar");
        hidden.Should().NotContain(">Pinned on top<");
        hidden.Should().NotContain(">Connection lost");
    }

    [Fact]
    public void Pack_BranchesOnThePlacement()
    {
        var entry = BuiltInStencilPackProvider.ReadPackJson()
            .Split('\n')
            .Single(line => line.Contains("\"type\": \"TmPinnedOnTop\""));

        entry.Should().Contain("placement == \\\"top\\\"", "a top banner draws its accent on the bottom edge, a bottom bar on the top edge");
    }

    private static async Task<string> RenderAsync(params (string Key, object? Value)[] props)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        var def = registry.GetDef("TmPinnedOnTop") ?? throw new InvalidOperationException("Missing TmPinnedOnTop.");
        var page = new WireframePage { Id = "page", Name = "Golden", Width = Math.Max(640, def.DefaultWidth + 80), Height = Math.Max(360, def.DefaultHeight + 80) };
        var element = new WireframeElement { Id = "sut", Type = "TmPinnedOnTop", X = 40, Y = 40, W = def.DefaultWidth, H = def.DefaultHeight };
        foreach (var (key, value) in props)
            element.SetProp(key, value);
        page.Elements.Add(element);

        var services = new ServiceCollection();
        services.AddLogging();
        return await new WireframeSvgRenderer(registry, services.BuildServiceProvider()).RenderPageAsync(page);
    }
}
