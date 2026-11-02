using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// TB8: the activity-comments stencil draws the four 2.10 states. Defaults keep the pre-2.10 drawing.
/// </summary>
public class TmActivityCommentsStencilTests
{
    [Fact]
    public void Schema_ExposesTheFourCommentStates_DefaultsOff()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmActivityComments");
        string[] states = ["showInternalComment", "showBadges", "showCustomActions", "composerInternal"];

        foreach (var name in states)
        {
            var prop = schema.Props.Should().ContainSingle(p => p.Name == name).Subject;
            prop.Type.Should().Be(PropType.Bool);
            prop.Default.Should().Be(false);
            prop.Category.Should().Be("State");
        }

        schema.Props.Select(p => p.Name).Should().Contain("commentCount");
    }

    [Fact]
    public void Pack_PropsMatchSchema_AndValidate()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());
        var component = pack.Components.Single(c => c.Type == "TmActivityComments");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmActivityComments");

        component.Props.Select(p => p.Name).Should().Equal(schema.Props.Select(p => p.Name));
        component.Roles.Should().Equal("list-view", "text-area");
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        StencilPackValidator.ValidateRoles(pack, vocabulary).Should().NotContain(w => w.ComponentType == "TmActivityComments");
    }

    [Fact]
    public async Task Render_Defaults_OmitTheNewStates()
    {
        var svg = await RenderAsync();

        svg.Should().Contain(">Comments<");
        svg.Should().NotContain(">Internal<");
        svg.Should().NotContain(">Badge<");
    }

    [Fact]
    public async Task Render_States_DrawInternalBadgeActionAndComposer()
    {
        var svg = await RenderAsync(
            ("showInternalComment", true),
            ("showBadges", true),
            ("showCustomActions", true),
            ("composerInternal", true));

        svg.Should().Contain(">Internal<");
        svg.Should().Contain(">Badge<");
        svg.Should().Contain("#fef9c3");
    }

    private static async Task<string> RenderAsync(params (string Key, object? Value)[] props)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        var def = registry.GetDef("TmActivityComments")!;
        var page = new WireframePage { Id = "page", Name = "Golden", Width = 480, Height = 400 };
        var element = new WireframeElement
        {
            Id = "sut",
            Type = "TmActivityComments",
            X = 40,
            Y = 40,
            W = def.DefaultWidth,
            H = def.DefaultHeight
        };
        foreach (var (key, value) in props)
            element.SetProp(key, value);
        page.Elements.Add(element);

        var services = new ServiceCollection();
        services.AddLogging();
        return await new WireframeSvgRenderer(registry, services.BuildServiceProvider()).RenderPageAsync(page);
    }
}
