using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmFallbackChain</c> is a wireframe citizen: a designer sketching a model fallback needs the ordered attempts with
/// their outcomes, their reasons and the Answer node — not three identical rectangles. The palette and the MCP
/// catalogue therefore ship the same vocabulary the component does: a UI role, a declarative stencil and a schema whose
/// props the pack draws.
/// </summary>
public sealed class TmFallbackChainStencilTests
{
    private static readonly string[] FallbackProps = ["title", "attempts", "policy", "viewMode"];

    [Fact]
    public void BuiltInSchema_ListsTheChain_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFallbackChain");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Fallback Chain");
        schema.Roles.Should().BeEquivalentTo("fallback-chain");
        schema.Props.Select(prop => prop.Name).Should().Equal(FallbackProps);
        schema.Props.Single(prop => prop.Name == "policy").Options.Should().Equal("pin", "fallback", "auto");
        schema.Props.Single(prop => prop.Name == "viewMode").Options.Should().Equal("flow", "table");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFallbackChain");

        schema.Props.Should().NotContain(prop => prop.Name == "size");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmFallbackChain")!.Slug.Should().Be("fallback-chain");
        vocabulary.Find("fallback-chain")!.DisplayName.Should().Be("Fallback chain");
        vocabulary.Find("model fallback")!.Slug.Should().Be("fallback-chain");
        vocabulary.Find("zalozni model")!.Slug.Should().Be("fallback-chain");
        vocabulary.Find("retez modelu")!.Slug.Should().Be("fallback-chain");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFallbackChain");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheChain_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmFallbackChain");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFallbackChain");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the chain is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(FallbackProps);
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
    public void TheChainPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"attempts\"", "the flow is repeated over the attempts prop");
        entry.Should().Contain("{attempt}", "and each node paints its own model");
        entry.Should().Contain("{policy", "the policy chip follows the policy prop");
        entry.Should().Contain("{viewMode", "the view switcher follows the viewMode prop");
    }

    [Fact]
    public void TheChainPack_SketchesTheOutcomesTheReasonsAndTheAnswerNode()
    {
        var entry = Entry();

        entry.Should().Contain("provider rate limit", "a failed branch keeps its reason");
        entry.Should().Contain("Failed", "the outcomes are drawn as words");
        entry.Should().Contain("Skipped");
        entry.Should().Contain("Succeeded");
        entry.Should().Contain("1.2 s", "a measured latency is part of the sketch");
        entry.Should().Contain("Answer", "the flow ends in the Answer node");
        entry.Should().Contain("Answered by", "and names the model that answered");
        entry.Should().Contain("token(\\\"color.danger", "a failed attempt is sketched in the danger tone");
        entry.Should().Contain("token(\\\"color.success", "the answer in the success tone");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmFallbackChain\""));
}
