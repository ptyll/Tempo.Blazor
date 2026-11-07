using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class ModelingServiceRegistrationTests
{
    [Fact]
    public async Task AddTempoBlazorModeling_resolves_modeling_services_and_generates_demo_document()
    {
        var services = new ServiceCollection();

        services.AddTempoBlazorModeling();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var modelProvider = provider.GetRequiredService<IEnumerable<IModelingModelProvider>>().Single();
        var registry = provider.GetRequiredService<ModelingNotationProfileRegistry>();
        var notationProvider = provider.GetRequiredService<IModelingNotationProfileProvider>();
        var relationshipRules = provider.GetRequiredService<IModelingRelationshipRulesProvider>();
        var viewpointRules = provider.GetRequiredService<IModelingViewpointRulesProvider>();
        var mapper = provider.GetRequiredService<IModelingStencilMapper>();
        var generator = scope.ServiceProvider.GetRequiredService<ModelingDiagramGenerator>();

        modelProvider.Should().BeOfType<DemoModelingModelProvider>();
        registry.GetProfile("bpmn2").Should().NotBeNull();
        registry.GetProfile("bpmn").Should().NotBeNull();
        registry.GetProfile("uml25").Should().NotBeNull();
        registry.GetProfile("archimate32").Should().NotBeNull();
        notationProvider.GetProfile("archimate").Should().NotBeNull();
        relationshipRules.IsValidRelationship("bpmn", "startEvent", "userTask", "sequenceFlow").Should().BeTrue();
        relationshipRules.IsValidRelationship("uml25", "Class", "Class", "Generalization").Should().BeTrue();
        viewpointRules.IsElementAllowedInViewpoint("archimate", "overview", "applicationComponent").Should().BeTrue();
        viewpointRules.IsElementAllowedInViewpoint("bpmn2", "Process", "AiTask").Should().BeTrue();
        viewpointRules.IsElementAllowedInViewpoint("archimate32", "ApplicationUsage", "ApplicationComponent").Should().BeTrue();
        viewpointRules.IsElementAllowedInViewpoint("archimate32", "ApplicationUsage", "BusinessActor").Should().BeFalse();
        mapper.GetStencilId("bpmn", "userTask").Should().Be("bpmn2.task.user");
        mapper.GetStencilId("uml25", "Class").Should().Be("uml25.class");
        mapper.GetStencilId("archimate32", "BusinessProcess").Should().Be("archimate3.business.process");
        mapper.GetEdgeStencilId("archimate", "serving").Should().Be("archimate3.relationship.serving");

        var model = await modelProvider.GetModelAsync(new ModelingModelRequest(), CancellationToken.None);
        var result = generator.Generate(model, new ModelingDiagramGenerationOptionsDto { ViewId = "demo-fulfillment-overview" });

        result.Document.Should().NotBeNull();
        result.Document!.Nodes.Should().HaveCount(model.Views[0].Nodes.Count);
        result.Document.Edges.Should().HaveCount(model.Views[0].Connections.Count);
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void AddTempoBlazorModeling_registers_exactly_one_modeling_provider_by_default()
    {
        var services = new ServiceCollection();

        services.AddTempoBlazorModeling();

        using var provider = services.BuildServiceProvider();
        var providers = provider.GetServices<IModelingModelProvider>().ToList();

        providers.Should().ContainSingle();
        providers[0].ProviderKey.Should().Be(DemoModelingModelProvider.ProviderKeyValue);
    }

    [Fact]
    public void AddTempoBlazorModeling_registers_the_demo_property_schema_and_lookup_providers()
    {
        // Phase 5: both providers are OPTIONAL services resolved per provider key. The shipped demo
        // pair answers ONLY for the phase-5 demo model, so every pre-existing scenario keeps the
        // 2.10.0 inspector.
        var services = new ServiceCollection();

        services.AddTempoBlazorModeling();

        using var provider = services.BuildServiceProvider();
        var schemaProviders = provider.GetServices<IModelingPropertySchemaProvider>().ToList();
        var lookupProviders = provider.GetServices<IModelingLookupProvider>().ToList();

        schemaProviders.Should().ContainSingle();
        schemaProviders[0].ProviderKey.Should().Be(DemoModelingModelProvider.ProviderKeyValue);
        lookupProviders.Should().ContainSingle();
        lookupProviders[0].ProviderKey.Should().Be(DemoModelingModelProvider.ProviderKeyValue);

        schemaProviders[0]
            .GetSchemaAsync(
                new ModelingPropertySchemaRequest
                {
                    ModelId = DemoModelingModelProvider.ModelIdValue,
                    Notation = "bpmn",
                    SemanticType = "userTask"
                },
                CancellationToken.None)
            .GetAwaiter().GetResult()
            .Should().BeNull("the default demo model keeps the 2.10.0 inspector");

        var stepSchema = schemaProviders[0]
            .GetSchemaAsync(
                new ModelingPropertySchemaRequest
                {
                    ModelId = DemoModelingPropertySchemaProvider.ModelIdValue,
                    Notation = "bpmn",
                    SemanticType = "userTask"
                },
                CancellationToken.None)
            .GetAwaiter().GetResult()!;

        stepSchema.SchemaId.Should().Be(DemoModelingPropertySchemaProvider.SchemaIdValue);
        stepSchema.Sections.Should().HaveCount(4);
        stepSchema.Sections.SelectMany(section => section.Fields).Select(field => field.Key)
            .Should().Contain(["instructions", "role", "systems", "sla", "exceptions"]);
    }

    [Fact]
    public void Consumer_can_add_second_modeling_provider_with_stable_order()
    {
        var services = new ServiceCollection();

        services.AddTempoBlazorModeling();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new ExtraModelingModelProvider()));

        using var provider = services.BuildServiceProvider();
        var providers = provider.GetServices<IModelingModelProvider>().ToList();

        providers.Select(modelProvider => modelProvider.ProviderKey)
            .Should().Equal(DemoModelingModelProvider.ProviderKeyValue, ExtraModelingModelProvider.Key);
    }

    [Fact]
    public void Duplicate_consumer_notation_profile_logs_warning_and_uses_built_in_profile()
    {
        var logs = new CaptureLoggerProvider();
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddTempoBlazorModeling();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingNotationProfile>(new DuplicateBpmnProfile()));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ModelingNotationProfileRegistry>();

        registry.GetProfile("bpmn").Should().NotBeNull();
        registry.GetProfile("bpmn")!.DisplayName.Should().Be("BPMN Legacy");
        registry.Count.Should().Be(5);
        logs.Messages.Should().Contain(message =>
            message.Level == LogLevel.Warning
            && message.Text.Contains("Duplicate modeling notation profile key", StringComparison.Ordinal)
            && message.Text.Contains("bpmn", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ExtraModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.extra-modeling";

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ModelingModelDto { Id = "extra", Title = "Extra" });
        }
    }

    private sealed class DuplicateBpmnProfile : IModelingNotationProfile
    {
        public string NotationKey => "bpmn";

        public string DisplayName => "Consumer BPMN Override";

        public IReadOnlyCollection<string> SupportedElementTypes { get; } = ["customTask"];

        public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } = ["customFlow"];

        public IReadOnlyCollection<string> SupportedViewpointKeys { get; } = ["custom"];
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public List<CapturedLogMessage> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class CaptureLogger(List<CapturedLogMessage> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NoopDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Add(new CapturedLogMessage(logLevel, formatter(state, exception)));
        }
    }

    private sealed record CapturedLogMessage(LogLevel Level, string Text);

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
