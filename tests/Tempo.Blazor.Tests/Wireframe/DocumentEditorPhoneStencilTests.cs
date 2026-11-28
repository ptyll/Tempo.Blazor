using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Redesign plan 146c7e1b, phase 8: the <c>TmDocumentEditor</c> stencil has a phone variant - a frame narrower than the stencil's default width paints the
/// compact header, the Home / Insert / Review / View tabs, one tools row and the action bar (Comments, Print, the primary Save, More) - and keeps the
/// ribbon-and-page picture at its default size. The role stays <c>rich-text-editor</c>.
/// </summary>
public sealed class DocumentEditorPhoneStencilTests
{
    [Fact]
    public async Task NarrowFrame_PaintsThePhoneVariant_WithTheActionBar()
    {
        var phone = await RenderAsync(360, 640);

        phone.Should().Contain("Comments").And.Contain("Save").And.Contain("More");
        phone.Should().Contain("Review").And.Contain("View");
        phone.Should().NotContain("Structure", "a phone has no navigation column");
    }

    [Fact]
    public async Task DefaultSizedFrame_KeepsTheDesktopVariant()
    {
        var desktop = await RenderAsync(480, 360);

        desktop.Should().NotContain("Comments");
        desktop.Should().Contain(">B<");
    }

    [Fact]
    public void TheStencilKeepsItsRole()
    {
        var def = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmDocumentEditor");

        def.Roles.Should().BeEquivalentTo("rich-text-editor");
    }

    private static async Task<string> RenderAsync(double width, double height)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        var page = new WireframePage { Id = "page", Name = "Golden", Width = width + 80, Height = height + 80 };
        page.Elements.Add(new WireframeElement { Id = "sut", Type = "TmDocumentEditor", X = 40, Y = 40, W = width, H = height });

        var services = new ServiceCollection();
        services.AddLogging();
        return await new WireframeSvgRenderer(registry, services.BuildServiceProvider()).RenderPageAsync(page);
    }
}
