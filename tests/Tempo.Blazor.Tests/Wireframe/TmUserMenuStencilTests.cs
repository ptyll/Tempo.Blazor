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
/// <c>TmUserMenu</c> is the account menu every application frame carries, and it had no wireframe
/// vocabulary at all (plan 7ba95ed6 phase 8, inherited from the app-shell plan). A designer sketching
/// a header needs the closed trigger — avatar, name, chevron — and the open panel with its user card,
/// the profile-completion hint, the host items and the optional theme switch.
/// <para>
/// Those are the knobs the sketch varies on, so they are exactly the props: <c>userName</c> /
/// <c>userEmail</c> fill the card, <c>showUserName</c> drops the name from the trigger,
/// <c>profileCompletion</c> draws (or omits) the completion hint, <c>open</c> draws the panel,
/// <c>items</c> repeats the host entries and <c>showThemeToggle</c> adds the theme row.
/// </para>
/// </summary>
public sealed class TmUserMenuStencilTests
{
    private static readonly string[] MenuProps =
        ["userName", "userEmail", "showUserName", "profileCompletion", "open", "items", "showThemeToggle"];

    private static WireframeComponentSchema Schema()
        => new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmUserMenu");

    [Fact]
    public void BuiltInSchema_ListsTheMenu_WithItsRoleAndProps()
    {
        var schema = Schema();

        schema.Category.Should().Be("Layout");
        schema.DisplayName.Should().Be("User Menu");
        schema.Roles.Should().BeEquivalentTo("user-menu");
        schema.IsContainer.Should().BeFalse("the menu is a popup, not a content container");
        schema.Props.Select(prop => prop.Name).Should().Equal(MenuProps);
        schema.Props.Single(prop => prop.Name == "items").Type.Should().Be(PropType.StringList);
        schema.Props.Single(prop => prop.Name == "profileCompletion").Type.Should().Be(PropType.Int);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = Schema();

        schema.Props.Select(prop => prop.Name).Should().NotContain("size").And.NotContain("repeat");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmUserMenu")!.Slug.Should().Be("user-menu");
        vocabulary.Find("user-menu")!.DisplayName.Should().Be("User menu");
        vocabulary.Find("user menu")!.Slug.Should().Be("user-menu");
        vocabulary.Find("uzivatelske menu")!.Slug.Should().Be("user-menu");
    }

    [Fact]
    public void EveryRoleTheStencilDeclaresIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = Schema();

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheMenu_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmUserMenu");
        var schema = Schema();

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the menu is a declarative stencil, not a native renderer");
        definition.IsContainer.Should().Be(schema.IsContainer);
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(MenuProps);
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
    public void TheMenuPack_SketchesTheTriggerAndTheOpenPanel()
    {
        var entry = Entry();

        entry.Should().Contain("{userName", "the trigger paints the account name");
        entry.Should().Contain("{userEmail", "the open card repeats it with the address");
        entry.Should().Contain("{showUserName", "an avatar-only trigger drops the name");
        entry.Should().Contain("{open", "the panel is the open state of the same component");
        entry.Should().Contain("\"prop\": \"items\"", "the host entries are repeated over the items prop");
        entry.Should().Contain("{item}", "and each entry paints its own label");
        entry.Should().Contain("profileCompletion", "the completion hint is conditional on its value");
        entry.Should().Contain("showThemeToggle", "the theme row is opt-in");
    }

    [Fact]
    public async Task DroppedMenu_WhenOpen_DrawsTheHostItemsAndTheThemeRow()
    {
        // A dropped element carries every non-null schema default, so `open` and `profileCompletion`
        // arrive explicitly — the state the editor actually renders.
        var def = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmUserMenu");
        var element = WireframeDocumentExtensions.NewElement(
            "TmUserMenu", 0, 0, def.DefaultWidth, 250, def.Props);
        element.SetProp("open", true);
        element.SetProp("showThemeToggle", true);

        var svg = await RenderAsync(builder => def.RenderSvg(element, builder));

        svg.Should().Contain("Settings", "the open panel lists the host entries (the items default)");
        svg.Should().Contain("Billing");
        svg.Should().Contain("Theme", "the theme row is drawn when its opt-in flag is on");
        svg.Should().Contain("jane@acme.io", "the profile card repeats the address");
    }

    /// <summary>Hosts a bare <see cref="RenderFragment"/> so the pack's render can be serialized to HTML.</summary>
    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Content);
    }

    private static async Task<string> RenderAsync(RenderFragment fragment)
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

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmUserMenu\""));
}
