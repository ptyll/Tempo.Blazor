using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Diagram.Services;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Components.Diagram.Templates;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Services;
using Xunit;

namespace Tempo.Blazor.Tests.Configuration;

public sealed class ServiceCollectionExtensionsPhase1Tests
{
    [Fact]
    public void AddTempoBlazor_registers_core_services_only()
    {
        var services = CreateServices();

        services.AddTempoBlazor();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITmLocalizer>().Should().NotBeNull();
        provider.GetRequiredService<ThemeService>().Should().NotBeNull();
        provider.GetRequiredService<ToastService>().Should().NotBeNull();
        provider.GetRequiredService<DragDropService>().Should().NotBeNull();
        provider.GetService<WireframeComponentRegistry>().Should().BeNull();
        provider.GetService<DiagramStencilRegistry>().Should().BeNull();
        provider.GetService<ModelingNotationProfileRegistry>().Should().BeNull();
    }

    [Fact]
    public void AddTempoBlazorWireframe_registers_wireframe_services()
    {
        var services = CreateServices();

        services.AddTempoBlazorWireframe();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<WireframeComponentRegistry>();
        var schemaRegistry = provider.GetRequiredService<WireframeSchemaRegistry>();

        provider.GetServices<IWireframeComponentProvider>()
            .Should()
            .ContainSingle(provider => provider is BuiltInStencilPackProvider);
        registry.GetDef("TmButton").Should().NotBeNull();
        schemaRegistry.GetSchema("TmButton").Should().NotBeNull();
        registry.GetDef("TmButton", WireframeComponentScope.ForApp(Guid.NewGuid())).Should().NotBeNull();
        schemaRegistry.GetSchema("TmButton", WireframeComponentScope.ForApp(Guid.NewGuid())).Should().NotBeNull();

        foreach (var schema in new BuiltInComponentSchemas().GetSchemas())
        {
            var def = registry.GetDef(schema.Type);
            def.Should().NotBeNull($"{schema.Type} should resolve from the built-in stencil pack");
            def!.DefaultWidth.Should().BePositive($"{schema.Type} should preserve schema width");
            def.DefaultHeight.Should().BePositive($"{schema.Type} should preserve schema height");
        }
    }

    [Fact]
    public void AddTempoBlazorPdfViewer_registers_pdf_viewer_feature()
    {
        var services = CreateServices();

        services.AddTempoBlazorPdfViewer();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITmLocalizer>().Should().NotBeNull();
        typeof(TmPdfViewer).Namespace.Should().Be("Tempo.Blazor.Components.Files");
    }

    [Fact]
    public void AddTempoBlazorCodes_registers_codes_feature()
    {
        var services = CreateServices();

        services.AddTempoBlazorCodes();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITmLocalizer>().Should().NotBeNull();
        typeof(TmQRCode).Namespace.Should().Be("Tempo.Blazor.Components.DataDisplay");
        typeof(TmBarcode).Namespace.Should().Be("Tempo.Blazor.Components.DataDisplay");
    }

    [Fact]
    public void AddTempoBlazorAI_registers_core_services()
    {
        var services = CreateServices();

        services.AddTempoBlazorAI();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITmLocalizer>().Should().NotBeNull();
        provider.GetRequiredService<ThemeService>().Should().NotBeNull();
        provider.GetRequiredService<ToastService>().Should().NotBeNull();
        provider.GetRequiredService<DragDropService>().Should().NotBeNull();
        typeof(AIServiceCollectionExtensions).Assembly.GetName().Name.Should().Be("Tempo.Blazor.AI");
    }

    [Fact]
    public void AddTempoBlazorDiagramEditor_registers_diagram_services()
    {
        var services = CreateServices();

        services.AddTempoBlazorDiagramEditor();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<DiagramStencilRegistry>();

        provider.GetServices<IDiagramStencilProvider>()
            .Should().Contain(provider => provider is BuiltInDiagramStencilProvider)
            .And.Contain(provider => provider is Uml25DiagramStencilProvider)
            .And.Contain(provider => provider is Bpmn2DiagramStencilProvider)
            .And.Contain(provider => provider is Archimate3DiagramStencilProvider)
            .And.Contain(provider => provider is ExtendedDiagramStencilProvider);
        provider.GetRequiredService<DiagramTemplateRegistry>().Should().NotBeNull();
        registry.GetStencil("general.rectangle").Should().NotBeNull();
        registry.GetStencil("uml25.class").Should().NotBeNull();
        registry.GetStencil("bpmn2.task.user").Should().NotBeNull();
        registry.GetStencil("archimate3.business.actor").Should().NotBeNull();
    }

    [Fact]
    public void AddTempoBlazorModeling_registers_modeling_and_diagram_services()
    {
        var services = CreateServices();

        services.AddTempoBlazorModeling();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ModelingNotationProfileRegistry>();

        provider.GetRequiredService<DiagramStencilRegistry>().Should().NotBeNull();
        provider.GetRequiredService<IModelingNotationProfileProvider>().Should().BeSameAs(registry);
        provider.GetRequiredService<IModelingRelationshipRulesProvider>().Should().NotBeNull();
        provider.GetRequiredService<IModelingViewpointRulesProvider>().Should().NotBeNull();
        provider.GetRequiredService<IModelingStencilMapper>().Should().NotBeNull();
        provider.GetServices<IModelingModelProvider>()
            .Should().ContainSingle(provider => provider is DemoModelingModelProvider);
        registry.GetProfile("bpmn2").Should().NotBeNull();
        registry.GetProfile("uml25").Should().NotBeNull();
        registry.GetProfile("archimate32").Should().NotBeNull();
    }

    [Fact]
    public void AddTempoBlazorAll_registers_split_feature_service_groups()
    {
        var services = CreateServices();

        services.AddTempoBlazorAll();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITmLocalizer>().Should().NotBeNull();
        provider.GetRequiredService<WireframeComponentRegistry>().Should().NotBeNull();
        provider.GetRequiredService<DiagramStencilRegistry>().Should().NotBeNull();
        provider.GetRequiredService<ModelingNotationProfileRegistry>().Should().NotBeNull();

        typeof(AllServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Should()
            .Contain("Tempo.Blazor.AI", "the compatibility package must pull in every split feature package");
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }
}
