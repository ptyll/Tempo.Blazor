using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 2 / decision <c>MODELING-WRITER-CAPABILITIES</c>: <see cref="IModelingModelWriter"/>
/// advertises what it can persist through a DEFAULT INTERFACE MEMBER — a 2.10.0 writer keeps
/// compiling and behaves as <see cref="ModelingModelWriterCapabilities.Content"/>. The editor
/// resolves the effective set once (host parameter ?? writer capability ?? Content, an explicit
/// host value may narrow but never widen what the writer stores), so phases 3–4 gate every new
/// action from one place.
/// </summary>
public sealed class TmModelingEditorCapabilityTests : LocalizationTestBase
{
    private readonly ModelingEditorTestClock _clock = new();

    public TmModelingEditorCapabilityTests()
    {
        Services.AddTempoBlazorModeling();
        Services.AddSingleton<TimeProvider>(_clock);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));
    }

    [Fact]
    public void Capability_flags_are_a_power_of_two_composition()
    {
        ModelingModelWriterCapabilities.None.Should().Be(ModelingModelWriterCapabilities.None);
        ((int)ModelingModelWriterCapabilities.Content).Should().Be(1);
        ((int)ModelingModelWriterCapabilities.Structure).Should().Be(2);
        ((int)ModelingModelWriterCapabilities.Layout).Should().Be(4);
        ModelingModelWriterCapabilities.All.Should().Be(
            ModelingModelWriterCapabilities.Content | ModelingModelWriterCapabilities.Structure | ModelingModelWriterCapabilities.Layout);
    }

    [Fact]
    public async Task Old_writer_without_the_default_member_advertises_content_only()
    {
        IModelingModelWriter writer = new LegacyModelingModelWriter();

        writer.Capabilities.Should().Be(ModelingModelWriterCapabilities.Content,
            "a 2.10.0 writer must keep working unchanged (the default interface member supplies Content)");

        var result = await writer.SaveChangesAsync(
            new ModelingModelChangeSet { ModelId = "model", ProviderKey = writer.ProviderKey, BaseVersion = "v" },
            CancellationToken.None);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Editor_without_a_writer_or_parameter_falls_back_to_content()
    {
        using var cut = RenderEditor();

        cut.Instance.EffectiveWriterCapabilities.Should().Be(ModelingModelWriterCapabilities.Content);
    }

    [Fact]
    public void Editor_takes_the_writer_capabilities_when_the_host_does_not_narrow_them()
    {
        Services.AddSingleton<IModelingModelWriter>(
            new CapabilityModelingModelWriter(SuccessfulModelingModelProvider.Key, ModelingModelWriterCapabilities.All));

        using var cut = RenderEditor();

        cut.Instance.EffectiveWriterCapabilities.Should().Be(ModelingModelWriterCapabilities.All);
    }

    [Fact]
    public void Explicit_host_capabilities_narrow_the_writer_but_never_widen_them()
    {
        Services.AddSingleton<IModelingModelWriter>(
            new CapabilityModelingModelWriter(SuccessfulModelingModelProvider.Key, ModelingModelWriterCapabilities.Content));

        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.WriterCapabilities, ModelingModelWriterCapabilities.All));

        cut.Instance.EffectiveWriterCapabilities.Should().Be(ModelingModelWriterCapabilities.Content,
            "the host can narrow, never widen, what its writer stores");
    }

    [Fact]
    public void Explicit_host_capabilities_apply_when_the_host_writes_itself()
    {
        using var cut = RenderEditor(parameters => parameters
            .Add(p => p.WriterCapabilities, ModelingModelWriterCapabilities.All));

        cut.Instance.EffectiveWriterCapabilities.Should().Be(ModelingModelWriterCapabilities.All,
            "an OnModelChanged-only host declares what it stores; the editor cannot verify it");
    }

    private IRenderedComponent<TmModelingEditor> RenderEditor(
        Action<ComponentParameterCollectionBuilder<TmModelingEditor>>? parameters = null)
        => Render<TmModelingEditor>(builder =>
        {
            builder.Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key);
            parameters?.Invoke(builder);
        });

    /// <summary>A 2.10.0-style writer: no capability member at all.</summary>
    private sealed class LegacyModelingModelWriter : IModelingModelWriter
    {
        public string ProviderKey => SuccessfulModelingModelProvider.Key;

        public Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken)
            => Task.FromResult(new ModelingModelSaveResult { Succeeded = true });
    }

    private sealed class CapabilityModelingModelWriter(string providerKey, ModelingModelWriterCapabilities capabilities) : IModelingModelWriter
    {
        public string ProviderKey { get; } = providerKey;

        public ModelingModelWriterCapabilities Capabilities { get; } = capabilities;

        public Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken)
            => Task.FromResult(new ModelingModelSaveResult { Succeeded = true });
    }
}
