using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 5d396592 phase 5: <c>TmOnboardingChecklist</c> is a wireframe citizen, not editor chrome. It is
/// catalogued in the built-in schemas with the <c>onboarding-checklist</c> role, that role exists in
/// the built-in vocabulary (with the synonyms a designer or an MCP client would search for), and the
/// built-in stencil pack renders the checklist declaratively (no native renderer) so the MCP catalogue
/// and the editor palette agree.
/// </summary>
public sealed class OnboardingChecklistStencilTests
{
    [Fact]
    public void BuiltInSchema_ListsTheChecklist_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmOnboardingChecklist");

        schema.Category.Should().Be("Data Display");
        schema.DisplayName.Should().Be("Onboarding Checklist");
        schema.Roles.Should().BeEquivalentTo("onboarding-checklist");
        schema.Props.Select(prop => prop.Name).Should()
            .Equal("title", "layout", "stepCount", "doneCount", "showProgress", "dismissible");
        schema.Props.Single(prop => prop.Name == "layout").Options.Should().Equal("list", "card", "grid");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void EveryRoleOfTheChecklist_IsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmOnboardingChecklist");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole_SoEveryComponentHasOne()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmOnboardingChecklist")!.Slug.Should().Be("onboarding-checklist");
        vocabulary.Find("onboarding-checklist")!.Synonyms.Should()
            .Contain("setup guide").And.Contain("progress tracker").And.Contain("checklist");
    }

    [Fact]
    public void StencilPack_RendersTheChecklist_AndAgreesWithTheSchema()
    {
        var def = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmOnboardingChecklist");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmOnboardingChecklist");

        def.IsBuiltIn.Should().BeTrue();
        def.PackId.Should().Be("tempo");
        def.NativeType.Should().BeNull("the checklist is a declarative stencil, not a native renderer");
        def.Roles.Should().BeEquivalentTo(schema.Roles);
        def.DefaultWidth.Should().Be(schema.DefaultWidth);
        def.DefaultHeight.Should().Be(schema.DefaultHeight);
        def.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
    }

    [Fact]
    public void StencilPack_RendersTheChecklistWithoutANativeRenderer()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());

        var definition = registry.GetDef("TmOnboardingChecklist");

        definition.Should().NotBeNull();
        definition!.RenderSvg.Should().NotBeNull(
            "the declarative pack render is what the editor canvas paints");
    }
}
