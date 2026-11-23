using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 1c95309c Fáze 8: <c>TmTimeSelector</c> is a wireframe citizen — schema, role and pack
/// entry must agree so the palette and the MCP catalogue do not drift from the component.
/// </summary>
public sealed class TimeSelectorStencilTests
{
    private static readonly string[] ExpectedProps =
        ["hourCycle", "showSeconds", "enableSwipe", "disabled"];

    [Fact]
    public void BuiltInSchema_ListsTheTimeSelector_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmTimeSelector");

        schema.Category.Should().Be("Pickers");
        schema.DisplayName.Should().Be("Time Selector");
        schema.Roles.Should().BeEquivalentTo("time-picker");
        schema.Props.Select(prop => prop.Name).Should().Equal(ExpectedProps);
        schema.DefaultWidth.Should().Be(280);
        schema.DefaultHeight.Should().Be(120);
    }

    [Fact]
    public void StencilPack_DeclaresTheSameDefaultSizeAsTheSchema()
    {
        using var pack = System.Text.Json.JsonDocument.Parse(BuiltInStencilPackProvider.ReadPackJson());
        var component = pack.RootElement.GetProperty("components").EnumerateArray()
            .Single(entry => entry.GetProperty("type").GetString() == "TmTimeSelector");
        var size = component.GetProperty("defaultSize");

        size.GetProperty("width").GetDouble().Should().Be(280);
        size.GetProperty("height").GetDouble().Should().Be(120);
        component.GetProperty("roles")[0].GetString().Should().Be("time-picker");
    }

    [Fact]
    public void TimePickerRole_IncludesTheSelector()
    {
        var roles = new BuiltInUiRoleVocabularySource().GetRoles()
            .Single(role => role.Slug == "time-picker");

        roles.Synonyms.Should().Contain("TmTimeSelector");
    }
}
