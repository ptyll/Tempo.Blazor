using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// The palette renders <c>TmIcon Name="@descriptor.Icon"</c> for every registry entry. Two entries named a
/// lucide icon the core set does not ship (<c>share-2</c>, <c>move-vertical</c>), so the Social and Spacer
/// tiles rendered an empty box while every other tile carried a glyph. The registry may only name icons
/// that exist, and this guard walks the WHOLE registry instead of the two known offenders.
/// </summary>
public class BlockRegistryIconTests : BunitContext
{
    public BlockRegistryIconTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
    }

    [Fact]
    public void Every_registry_icon_renders_an_svg_with_content()
    {
        var descriptors = new BlockRegistry().GetAll();
        descriptors.Should().NotBeEmpty();

        var broken = new List<string>();
        foreach (var descriptor in descriptors)
        {
            var cut = Render<TmIcon>(p => p.Add(c => c.Name, descriptor.Icon));
            var svg = cut.FindAll("svg");
            if (svg.Count != 1 || svg[0].ChildNodes.Length == 0)
            {
                broken.Add($"{descriptor.Id} → '{descriptor.Icon}'");
            }
        }

        broken.Should().BeEmpty(
            "an icon the core set does not ship leaves an empty tile: {0}", string.Join(", ", broken));
    }

    [Fact]
    public void The_social_and_spacer_tiles_name_icons_that_exist_in_the_core_set()
    {
        // The plan suggested "share" and "between-horizontal-start"; the assertion is on the RESULT (a
        // non-empty glyph above the label), so a future core rename fails here instead of shipping a box.
        var registry = new BlockRegistry().GetAll().ToDictionary(d => d.Id, d => d.Icon);
        registry["social"].Should().NotBe("share-2");
        registry["spacer"].Should().NotBe("move-vertical");

        foreach (var icon in new[] { registry["social"], registry["spacer"] })
        {
            var cut = Render<TmIcon>(p => p.Add(c => c.Name, icon));
            cut.Find("svg").ChildNodes.Should().NotBeEmpty($"{icon} must render a path");
        }
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
