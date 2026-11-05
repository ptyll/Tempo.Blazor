using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmAutonomyDial</c> is a wireframe citizen: a designer sketching how much an agent may do on its own needs the
/// grade of autonomy, the ceiling and the limits somebody else enforces — not one anonymous circle. The palette and the
/// MCP catalogue therefore ship the same vocabulary the component does: a UI role, a declarative stencil and a schema
/// whose props the pack draws.
/// </summary>
public sealed class TmAutonomyDialStencilTests
{
    private static readonly string[] DialProps = ["title", "levels", "value", "maxAllowed", "presentation", "locks"];

    [Fact]
    public void BuiltInSchema_ListsTheDial_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAutonomyDial");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Autonomy Dial");
        schema.Roles.Should().BeEquivalentTo("autonomy-dial");
        schema.Props.Select(prop => prop.Name).Should().Equal(DialProps);
        schema.Props.Single(prop => prop.Name == "presentation").Options.Should().Equal("dial", "segmented");
        schema.Props.Single(prop => prop.Name == "value").Type.Should().Be(PropType.Int);
        schema.Props.Single(prop => prop.Name == "maxAllowed").Type.Should().Be(PropType.Int);
        schema.Props.Single(prop => prop.Name == "levels").Type.Should().Be(PropType.StringList);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAutonomyDial");

        schema.Props.Should().NotContain(prop => prop.Name == "size");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmAutonomyDial")!.Slug.Should().Be("autonomy-dial");
        vocabulary.Find("autonomy-dial")!.DisplayName.Should().Be("Autonomy dial");
        vocabulary.Find("autonomy")!.Slug.Should().Be("autonomy-dial");
        vocabulary.Find("how much autonomy")!.Slug.Should().Be("autonomy-dial");
        vocabulary.Find("autonomie")!.Slug.Should().Be("autonomy-dial");
        vocabulary.Find("mira autonomie")!.Slug.Should().Be("autonomy-dial");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAutonomyDial");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheDial_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmAutonomyDial");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAutonomyDial");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the dial is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(DialProps);
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public void ThePackCompilesWithoutWarnings()
    {
        var provider = new BuiltInStencilPackProvider();

        _ = provider.GetDefinitions().ToList();

        provider.ValidationWarnings.Should().BeEmpty();
    }

    [Fact]
    public void TheDialPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"levels\"", "the stops are repeated over the levels prop");
        entry.Should().Contain("{level}", "and each stop paints its own label");
        entry.Should().Contain("{value", "the current grade follows the value prop");
        entry.Should().Contain("{maxAllowed", "the ceiling follows the maxAllowed prop");
        entry.Should().Contain("{presentation", "the presentation chip follows the presentation prop");
        entry.Should().Contain("\"prop\": \"locks\"", "the enforced limits repeat over the locks prop");
    }

    [Fact]
    public void TheDialPack_SketchesTheCeilingAndTheEnforcedLimits()
    {
        var entry = Entry();

        entry.Should().Contain("Enforced by", "a sketched lock names its source");
        entry.Should().Contain("Tenant policy");
        entry.Should().Contain("token(\\\"color.primary", "the reached part of the track is painted in the primary tone");
        entry.Should().Contain("token(\\\"color.warning", "the ceiling is sketched in the warning tone");
        entry.Should().Contain("token(\\\"color.border", "the unreached part of the track keeps the border tone");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmAutonomyDial\""));
}
