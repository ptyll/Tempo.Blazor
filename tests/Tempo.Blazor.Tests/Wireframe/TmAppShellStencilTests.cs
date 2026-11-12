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
/// <c>TmAppShell</c> is the frame a whole application sits in, so a wireframe of any page is
/// incomplete without it. The palette therefore ships the vocabulary the component has: a UI role,
/// a declarative stencil and a schema whose props the pack draws.
/// <para>
/// The carried-forward gap (plan 7ba95ed6 phase 8, inherited from the app-shell plan) was that the
/// shell — and its phone presentation in particular — had no stencil at all: a designer could sketch
/// a top bar, a sidebar and a bottom bar as three unrelated boxes but not the FRAME that composes
/// them. The three knobs the plan names are the ones the sketches differ by: <c>variant</c>
/// (<see cref="Tempo.Blazor.Components.Layout.AppShellVariant"/>), <c>mobileNav</c>
/// (<see cref="Tempo.Blazor.Components.Layout.AppShellMobileNav"/>) and the work-area sheet.
/// </para>
/// </summary>
public sealed class TmAppShellStencilTests
{
    private static readonly string[] ShellProps =
        ["variant", "mobileNav", "title", "navItems", "showSidebar", "sidebarCollapsed", "showWorkArea", "workAreaWidth"];

    private static WireframeComponentSchema Schema()
        => new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAppShell");

    [Fact]
    public void BuiltInSchema_ListsTheShell_WithItsRoleAndProps()
    {
        var schema = Schema();

        schema.Category.Should().Be("Layout");
        schema.DisplayName.Should().Be("App Shell");
        schema.Roles.Should().BeEquivalentTo("app-shell");
        schema.IsContainer.Should().BeTrue("pages live INSIDE the shell");
        schema.Props.Select(prop => prop.Name).Should().Equal(ShellProps);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void BuiltInSchema_OffersBothEnumsTheComponentHas()
    {
        var schema = Schema();

        var variant = schema.Props.Single(prop => prop.Name == "variant");
        variant.Type.Should().Be(PropType.Enum);
        variant.Options.Should().BeEquivalentTo("Inset", "Flat", "Workspace");
        variant.Default.Should().Be("Inset");

        var mobileNav = schema.Props.Single(prop => prop.Name == "mobileNav");
        mobileNav.Type.Should().Be(PropType.Enum);
        mobileNav.Options.Should().BeEquivalentTo("Drawer", "BottomNavigation");
        mobileNav.Default.Should().Be("Drawer");
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

        vocabulary.Find("TmAppShell")!.Slug.Should().Be("app-shell");
        vocabulary.Find("app-shell")!.DisplayName.Should().Be("App shell");
        vocabulary.Find("app shell")!.Slug.Should().Be("app-shell");
        vocabulary.Find("aplikacni ramec")!.Slug.Should().Be("app-shell");
    }

    [Fact]
    public void EveryRoleTheStencilDeclaresIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = Schema();

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheShell_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmAppShell");
        var schema = Schema();

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the shell is a declarative stencil, not a native renderer");
        definition.IsContainer.Should().Be(schema.IsContainer);
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(ShellProps);
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
    public void TheShellPack_SketchesTheFrameItsRegionsAndTheWorkArea()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the top bar paints the host's title");
        entry.Should().Contain("\"prop\": \"navItems\"", "the docked column repeats the navigation items");
        entry.Should().Contain("{navItem}", "and each row paints its own label");
        entry.Should().Contain("{showSidebar", "the docked column is conditional on showSidebar");
        entry.Should().Contain("{sidebarCollapsed", "a collapsed column is narrower, not absent");
        entry.Should().Contain("{showWorkArea", "the work-area sheet is an opt-in region");
        entry.Should().Contain("{workAreaWidth", "its width is the host's knob");
        entry.Should().Contain("{variant", "the inset/flat treatment is what the variant prop changes");
        entry.Should().Contain("Workspace",
            "the TG-008 variant drops the content panel: the pack must branch on it, not fall through to a card");
        entry.Should().Contain("{mobileNav", "the phone presentation follows the mobileNav prop");
    }

    [Fact]
    public void TheShellPack_DrawsTheBottomBarInsteadOfTheHamburger_WhenMobileNavIsBottomNavigation()
    {
        var entry = Entry();

        entry.Should().Contain("BottomNavigation",
            "the hamburger and the bottom bar are alternatives, so the sketch branches on the enum value");
        entry.Should().Contain("Drawer",
            "the hamburger branch is keyed on the Drawer value of the same enum, not on a negation");
    }

    [Fact]
    public async Task DroppedShell_DrawsItsNavigationRows()
    {
        // A dropped element carries every non-null schema default, including the navItems list, so the
        // docked column must sketch one row per item — an empty white column communicates nothing.
        var def = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmAppShell");
        var element = WireframeDocumentExtensions.NewElement(
            "TmAppShell", 0, 0, def.DefaultWidth, def.DefaultHeight, def.Props);

        var svg = await RenderAsync(builder => def.RenderSvg(element, builder));

        svg.Should().Contain("Overview", "the docked column repeats the host's navigation items");
        svg.Should().Contain("Projects");
        svg.Should().Contain("Dashboard", "and the top bar paints the title");
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
        .Single(line => line.Contains("\"type\": \"TmAppShell\""));
}
