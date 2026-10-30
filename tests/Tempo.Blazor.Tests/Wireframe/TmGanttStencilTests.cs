using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// TmGantt redesign, phase 4: the wireframe stencil mirrors the new desktop structure - a toolbar row (icon
/// squares, or labelled pills with <c>toolbarLabels</c>) and an optional text tab strip (<c>sidebarMode=tabs</c>).
/// </summary>
public class TmGanttStencilTests
{
    [Fact]
    public async Task Default_Stencil_Has_No_Tab_Strip_And_No_Button_Labels()
    {
        var svg = await RenderAsync();

        svg.Should().NotContain(">Workload<");
        svg.Should().NotContain("+ Add task");
        svg.Should().Contain(">Today<");
    }

    [Fact]
    public async Task ToolbarLabels_Renders_Labelled_Buttons()
    {
        var svg = await RenderAsync(("toolbarLabels", true));

        svg.Should().Contain("+ Add task");
        svg.Should().Contain(">Expand<").And.Contain(">Filter<");
    }

    [Fact]
    public async Task SidebarMode_Tabs_Renders_The_Text_Tab_Strip()
    {
        var svg = await RenderAsync(("sidebarMode", "tabs"));

        svg.Should().Contain(">Tasks<").And.Contain(">Workload<").And.Contain(">History<").And.Contain(">Notifications<").And.Contain(">Reports<");
    }

    [Fact]
    public void Schema_Declares_The_New_Props()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmGantt");

        schema.Props.Select(p => p.Name).Should().Contain(["toolbarLabels", "sidebarMode"]);
        schema.Props.Single(p => p.Name == "sidebarMode").Options.Should().Equal("rail", "tabs");
    }

    [Fact]
    public async Task Portfolio_Stencil_Renders_Cards_With_Count_And_Weighted_Progress()
    {
        var svg = await RenderAsync("TmGanttPortfolio", ("projectCount", 2));

        svg.Should().Contain(">Project 1<").And.Contain(">Project 2<");
        svg.Should().Contain(">Tasks: 5<", "each card states its task count");
        svg.Should().Contain(">30%<").And.Contain(">50%<", "each card carries its weighted completion");
        svg.Should().Contain("M20,50 L24,54 L28,50", "the collapse chevron of the card header");
    }
    private static Task<string> RenderAsync(params (string Key, object? Value)[] props) => RenderAsync("TmGantt", props);

    private static async Task<string> RenderAsync(string type, params (string Key, object? Value)[] props)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        var def = registry.GetDef(type) ?? throw new InvalidOperationException($"Missing definition for {type}.");
        var element = new WireframeElement { Id = "sut", Type = type, W = def.DefaultWidth, H = def.DefaultHeight };
        foreach (var (key, value) in props)
            element.SetProp(key, value);

        var services = new ServiceCollection();
        services.AddLogging();
        await using var htmlRenderer = new HtmlRenderer(services.BuildServiceProvider(), NullLoggerFactory.Instance);
        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment fragment = builder => def.RenderSvg(element, builder);
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Content"] = fragment });
            var output = await htmlRenderer.RenderComponentAsync<FragmentHost>(parameters);
            return output.ToHtmlString();
        });
    }

    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Content);
    }
}