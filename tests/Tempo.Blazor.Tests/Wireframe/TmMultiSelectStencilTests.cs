using FluentAssertions;
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
/// The <c>TmMultiSelect</c> wireframe stencil used to draw only the CLOSED field (a field box and two
/// chips), so the two things the component gained later — the opt-in "create" row and the typed-term
/// match highlight, both of which live inside the OPEN popup — had no wireframe representation at all
/// (plan 7ba95ed6 phase 8, carried forward from the data-and-analytics plan).
/// <para>
/// The pair is now part of the schema and the pack in ONE change, which is the rule this project keeps:
/// the palette vocabulary (<see cref="BuiltInComponentSchemas"/>) and the drawn stencil may not drift
/// apart, and <see cref="TempoPackFormControlsRenderTests"/> pins the prop order of both.
/// </para>
/// </summary>
public sealed class TmMultiSelectStencilTests
{
    private const string CreateRowText = "Create";
    private const string TypedTerm = "adm";

    private static readonly string[] MultiSelectProps =
        ["label", "placeholder", "required", "disabled", "open", "showCreateRow", "highlight"];

    private static WireframeComponentSchema Schema()
        => new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmMultiSelect");

    [Fact]
    public void BuiltInSchema_ListsTheOpenComboboxState()
    {
        var schema = Schema();

        schema.Props.Select(prop => prop.Name).Should().Equal(MultiSelectProps);

        var open = schema.Props.Single(prop => prop.Name == "open");
        open.Type.Should().Be(PropType.Bool);
        open.Default.Should().Be(false);

        var createRow = schema.Props.Single(prop => prop.Name == "showCreateRow");
        createRow.Type.Should().Be(PropType.Bool);
        createRow.Default.Should().Be(false);

        var highlight = schema.Props.Single(prop => prop.Name == "highlight");
        highlight.Type.Should().Be(PropType.Bool);
        highlight.Default.Should().Be(false);
    }

    [Fact]
    public void StencilPack_AgreesWithTheSchemaOnTheNewProps()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmMultiSelect");
        var schema = Schema();

        definition.Props.Select(prop => prop.Name).Should().Equal(MultiSelectProps);
        definition.Props.Single(prop => prop.Name == "showCreateRow").Default.Should().Be(false);
        definition.Props.Single(prop => prop.Name == "highlight").Default.Should().Be(false);
        schema.Props.Select(prop => prop.Name).Should().Equal(MultiSelectProps);
    }

    [Fact]
    public async Task ClosedField_DrawsNoPopup()
    {
        var svg = await RenderAsync();

        svg.Should().NotContain(CreateRowText, "the create row lives in the popup, so a closed field has none");
        svg.Should().NotContain(TypedTerm, "the highlight belongs to the popup's option rows");
        svg.Should().NotContain("Admin", "a closed field paints no option label either");
    }

    [Fact]
    public async Task ClosedField_DrawsNoPopup_EvenWhenTheSchemaDefaultsAreApplied()
    {
        // A dropped element carries EVERY non-null schema default (WireframeDocumentExtensions.NewElement),
        // so `open`/`showCreateRow`/`highlight` arrive as an explicit `false` rather than as an absent prop.
        // The popup must still stay closed — a row label that draws on `!highlight` alone leaks onto the canvas.
        var def = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmMultiSelect");
        var element = WireframeDocumentExtensions.NewElement(
            "TmMultiSelect", 0, 0, def.DefaultWidth, def.DefaultHeight, def.Props);

        var svg = await RenderFragmentAsync(builder => def.RenderSvg(element, builder));

        svg.Should().NotContain("Admin", "the option list belongs to the OPEN popup, not to the closed field");
        svg.Should().NotContain("Editor");
        svg.Should().NotContain("Viewer");
        svg.Should().NotContain(CreateRowText);
        svg.Should().Contain("Item", "the closed field still paints its chips");
    }

    [Fact]
    public async Task ShowCreateRow_DrawsTheCreateRowInAnOpenPopup()
    {
        var svg = await RenderAsync(("showCreateRow", true));

        svg.Should().Contain(CreateRowText,
            "the opt-in create row is the last entry of the open popup, so the sketch must show it");
    }

    [Fact]
    public async Task Highlight_DrawsTheTypedTermMarkedInTheOpenPopup()
    {
        var svg = await RenderAsync(("highlight", true));

        svg.Should().Contain(">" + TypedTerm + "<",
            "the typed term is what the match highlight marks, so the sketch must name it");
        svg.Should().Contain("font-weight='600'",
            "the marked run is painted in the primary tone and bold, not left indistinguishable from the rest");
        svg.Should().NotContain(">Admin<",
            "the match splits the option label into a marked run and the rest");
    }

    [Fact]
    public async Task Open_DrawsThePopupWithoutEitherOptIn()
    {
        var svg = await RenderAsync(("open", true));

        svg.Should().Contain("Admin",
            "an open combobox sketches the option rows even when neither create row nor highlight is on");
        svg.Should().NotContain(CreateRowText);
    }

    [Fact]
    public async Task BothStatesTogether_DrawTheCreateRowAndTheHighlight()
    {
        var svg = await RenderAsync(("showCreateRow", true), ("highlight", true));

        svg.Should().Contain(CreateRowText);
        svg.Should().Contain(TypedTerm);
    }

    private static async Task<string> RenderAsync(params (string Key, object? Value)[] props)
    {
        var def = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmMultiSelect");
        var element = new WireframeElement
        {
            Id = "sut",
            Type = "TmMultiSelect",
            W = def.DefaultWidth,
            H = def.DefaultHeight
        };
        element.SetProp("label", "Roles");
        foreach (var (key, value) in props)
            element.SetProp(key, value);

        return await RenderFragmentAsync(builder => def.RenderSvg(element, builder));
    }

    /// <summary>Hosts a bare <see cref="RenderFragment"/> so the pack's render can be serialized to HTML.</summary>
    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Content);
    }

    private static async Task<string> RenderFragmentAsync(RenderFragment fragment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var htmlRenderer = new HtmlRenderer(services.BuildServiceProvider(), NullLoggerFactory.Instance);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Content"] = fragment });
            var output = await htmlRenderer.RenderComponentAsync<FragmentHost>(parameters);
            return output.ToHtmlString();
        });
    }
}
