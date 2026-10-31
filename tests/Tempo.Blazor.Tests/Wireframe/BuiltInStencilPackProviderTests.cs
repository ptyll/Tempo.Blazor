using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

public class BuiltInStencilPackProviderTests
{
    private static readonly string[] NativeTypes =
    [
        "TmChart",
        "TmGauge",
        "TmStockChart",
        "TmKanbanBoard",
        "TmPivotTable",
        "TmGantt",
        "TmWorkflowDesignerCanvas",
        "TmDiagramEditor",
        "TmSpreadsheet",
        "TmDocumentEditor",
        "TmNotionEditor",
        "TmChat"
    ];

    public static TheoryData<string> NativeComponentTypes
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var type in NativeTypes)
                data.Add(type);
            return data;
        }
    }

    [Fact]
    public void PackLoads_And_SchemaValidates()
    {
        var provider = new BuiltInStencilPackProvider();
        var packJson = BuiltInStencilPackProvider.ReadPackJson();
        var pack = StencilPackSerializer.Deserialize(packJson);

        provider.ProviderId.Should().Be("TempoBuiltInPack");
        provider.Priority.Should().Be(0);
        ValidatePackJson(packJson).IsValid.Should().BeTrue();
        provider.GetDefinitions().Should().NotBeEmpty();
        pack.Id.Should().Be("tempo");
        pack.Namespace.Should().Be("tempo");
        pack.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public void TokenDefaultsResolve()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());

        pack.Tokens["color.primary"].Should().Be("#3b82f6");
        pack.Tokens["bg.surface"].Should().Be("#ffffff");
        pack.Tokens["border.default"].Should().Be("#e5e7eb");
        pack.Tokens["text.default"].Should().Be("#111827");
        pack.Themes["dark"]["bg.surface"].Should().Be("#1e293b");

        new StencilTokenResolver(null, null, null, pack.Tokens)
            .Resolve("color.primary")
            .Should()
            .Be("#3b82f6");
        new StencilTokenResolver(null, null, pack.Themes["dark"], pack.Tokens)
            .Resolve("bg.surface")
            .Should()
            .Be("#1e293b");
    }

    [Theory]
    [MemberData(nameof(NativeComponentTypes))]
    public void NativeComponentsRenderViaCSharp(string type)
    {
        var provider = new BuiltInStencilPackProvider();
        var def = provider.GetDefinitions().Single(d => d.Type == type);

        NativeRendererRegistry.TempoBuiltIn.TryGet(type, out var renderer).Should().BeTrue();
        def.IsBuiltIn.Should().BeTrue();
        def.PackId.Should().Be("tempo");
        def.NativeType.Should().Be(type);
        def.RenderSvg.Should().BeSameAs(renderer);

        var act = () => def.RenderSvg(MakeElement(def), new RenderTreeBuilder());

        act.Should().NotThrow($"{type} should render through the existing native C# renderer");
    }

    [Theory]
    [MemberData(nameof(NativeComponentTypes))]
    public void NativeDefinitions_PreserveBuiltInSchemaMetadata(string type)
    {
        var def = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == type);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == type);

        def.Category.Should().Be(schema.Category);
        def.DisplayName.Should().Be(schema.DisplayName);
        def.DefaultWidth.Should().Be(schema.DefaultWidth);
        def.DefaultHeight.Should().Be(schema.DefaultHeight);
        def.Props.Select(p => p.Name).Should().Equal(schema.Props.Select(p => p.Name));
        def.SizePresets.Should().BeEquivalentTo(schema.SizePresets);
    }

    [Fact]
    public void AllTypesRegisteredViaPack()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());

        var expectedTypes = new BuiltInComponentSchemas().GetSchemas().Select(s => s.Type).ToList();

        expectedTypes.Where(type => registry.GetDef(type) is null).Should().BeEmpty();
        foreach (var type in NativeTypes)
        {
            var def = registry.GetDef(type)!;
            def.PackId.Should().Be("tempo");
            def.NativeType.Should().Be(type);
        }
    }

    [Theory]
    [MemberData(nameof(NativeComponentTypes))]
    public async Task NativeComponents_RenderSvgThroughPackNativeHook(string type)
    {
        var packRegistry = RegistryWith(new BuiltInStencilPackProvider());
        var def = packRegistry.GetDef(type)!;
        var page = PageWith(MakeElement(def));

        var packSvg = await BuildRenderer(packRegistry).RenderPageAsync(page);

        packSvg.Should().StartWith("<svg");
        packSvg.Should().Contain("<rect");
        packSvg.Should().NotContainEquivalentOf("<script");
        packSvg.Should().NotContainEquivalentOf("<foreignObject");
    }

    [Fact]
    public async Task SpreadsheetNative_RendersTheGroupedRibbon_AndTheOptionalHeader()
    {
        var packRegistry = RegistryWith(new BuiltInStencilPackProvider());
        var def = packRegistry.GetDef("TmSpreadsheet")!;

        var plain = MakeElement(def);
        plain.Props["showHeader"] = JsonSerializer.SerializeToElement(false);
        var plainSvg = await BuildRenderer(packRegistry).RenderPageAsync(PageWith(plain));

        foreach (var group in new[] { "Clipboard", "Font", "Alignment", "Number", "Cells", "Insert" })
        {
            plainSvg.Should().Contain(group, $"the wireframe ribbon names its group '{group}'");
        }
        plainSvg.Should().Contain("fx", "the formula bar paints the permanent fx mark");
        plainSvg.Should().Contain("Ready", "the status bar is part of the native chrome");
        plainSvg.Should().NotContain("Show grid lines", "the header is opt-in");

        var withHeader = MakeElement(def);
        withHeader.Props["showHeader"] = JsonSerializer.SerializeToElement(true);
        withHeader.Props["title"] = JsonSerializer.SerializeToElement("Projekt Aurora");
        var headerSvg = await BuildRenderer(packRegistry).RenderPageAsync(PageWith(withHeader));

        headerSvg.Should().Contain("Projekt Aurora");
        headerSvg.Should().Contain("Show grid lines");
        headerSvg.Should().Contain("Download");
    }

    private static WireframeElement MakeElement(WireframeComponentDef def)
        => new()
        {
            Id = "native",
            Type = def.Type,
            W = def.DefaultWidth,
            H = def.DefaultHeight
        };

    private static WireframePage PageWith(WireframeElement element)
    {
        var page = new WireframePage { Id = "native-page", Name = "Native", Width = 900, Height = 600 };
        page.Elements.Add(element);
        return page;
    }

    private static WireframeComponentRegistry RegistryWith(IWireframeComponentProvider provider)
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(provider);
        return registry;
    }

    private static WireframeSvgRenderer BuildRenderer(WireframeComponentRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return new WireframeSvgRenderer(registry, services.BuildServiceProvider());
    }

    private static EvaluationResults ValidatePackJson(string json)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(SchemaPath()));
        return schema.Evaluate(JsonNode.Parse(json), new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });
    }

    private static string SchemaPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the repository root should be discoverable from the test output directory");
        return Path.Combine(directory!.FullName, "src", "Tempo.Blazor.Wireframe", "wwwroot", "tempo-stencil.schema.json");
    }
}
