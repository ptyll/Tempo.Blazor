using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingEditorTests : LocalizationTestBase
{
    public TmModelingEditorTests()
    {
        Services.AddTempoBlazorModeling();
    }

    [Fact]
    public void Shows_loading_state_while_provider_is_running()
    {
        var provider = new DelayedModelingModelProvider();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(provider));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, DelayedModelingModelProvider.Key));

        cut.Find("[data-testid='modeling-editor-loading']").Should().NotBeNull();
    }

    [Fact]
    public void Shows_loaded_state_after_successful_load_and_raises_callback()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));
        DiagramDocument? generatedDocument = null;

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
            .Add(p => p.OnDiagramGenerated, EventCallback.Factory.Create<DiagramDocument>(this, document => generatedDocument = document)));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded");
            cut.Find("[data-testid='modeling-model-tree-panel']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-preview-panel']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-inspector-panel']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-status-strip']").Should().NotBeNull();
            generatedDocument.Should().NotBeNull();
            generatedDocument!.Nodes.Should().ContainSingle();
        });
    }

    [Fact]
    public void Preview_panel_is_named_region_not_main_landmark()
    {
        // N190 — the host layout owns the single <main> landmark; the editor's preview area is a
        // labelled <section> region instead (036ce5fd/a59cac9a rule, generalised).
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("main").Should().BeEmpty("the host layout owns the single <main> landmark");
            cut.Find("section.tm-modeling-editor__preview")
                .GetAttribute("aria-label").Should().Be("Preview");
        });
    }

    [Fact]
    public void Panel_tabs_switch_active_panel_state()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded");
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-active-panel").Should().Be("preview");
        });

        cut.Find("[data-testid='modeling-panel-tab-tree']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-active-panel").Should().Be("tree"));

        cut.Find("[data-testid='modeling-panel-tab-inspector']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-active-panel").Should().Be("inspector"));

        cut.Find("[data-testid='modeling-panel-tab-preview']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-active-panel").Should().Be("preview"));
    }

    [Fact]
    public void Ui_notation_change_does_not_trigger_provider_reload_on_parent_rerender()
    {
        var provider = new CountingModelingModelProvider();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(provider));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, CountingModelingModelProvider.Key)
            .Add(p => p.NotationKey, "bpmn"));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded");
            provider.LoadCount.Should().Be(1);
        });

        cut.Find("[data-testid='modeling-notation-select']").Change("uml25");
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-notation").Should().Be("uml25");
            provider.LoadCount.Should().Be(1);
        });

        cut.Render(parameters => parameters
            .Add(p => p.ProviderKey, CountingModelingModelProvider.Key)
            .Add(p => p.NotationKey, "bpmn"));

        cut.WaitForAssertion(() => provider.LoadCount.Should().Be(1));
    }

    [Fact]
    public void Shows_error_state_when_provider_throws_without_raising_callback()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new ThrowingModelingModelProvider()));
        var callbackRaised = false;

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, ThrowingModelingModelProvider.Key)
            .Add(p => p.OnDiagramGenerated, EventCallback.Factory.Create<DiagramDocument>(this, _ => callbackRaised = true)));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("error");
            cut.Find("[data-testid='modeling-editor-error']").TextContent.Should().Contain("Provider failure");
            callbackRaised.Should().BeFalse();
        });
    }

    [Fact]
    public void Shows_empty_state_when_provider_key_has_no_provider()
    {
        var callbackRaised = false;

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, "missing-provider")
            .Add(p => p.OnDiagramGenerated, EventCallback.Factory.Create<DiagramDocument>(this, _ => callbackRaised = true)));

        cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("empty");
        cut.Find("[data-testid='modeling-editor-empty']").Should().NotBeNull();
        callbackRaised.Should().BeFalse();
    }

    [Fact]
    public void Null_provider_key_shows_empty_state_without_exception()
    {
        var callbackRaised = false;

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, (string?)null)
            .Add(p => p.OnDiagramGenerated, EventCallback.Factory.Create<DiagramDocument>(this, _ => callbackRaised = true)));

        cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("empty");
        cut.Find("[data-testid='modeling-editor-empty']").Should().NotBeNull();
        callbackRaised.Should().BeFalse();
    }

    private sealed class SuccessfulModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.success";

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateModel());
        }
    }

    private sealed class DelayedModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.delay";

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<ModelingModelDto>();
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        }
    }

    private sealed class ThrowingModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.throwing";

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Provider failure");
    }

    private sealed class CountingModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.counting";

        public int LoadCount { get; private set; }

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return Task.FromResult(CreateModel());
        }
    }

    private static ModelingModelDto CreateModel()
    {
        var model = new ModelingModelDto
        {
            Id = "modeling-editor-test",
            Title = "Modeling editor test",
            Metadata = new ModelingMetadataDto { SourceSystem = "Unit tests" },
            Elements =
            [
                new()
                {
                    Id = "task-a",
                    SourceId = "source-task-a",
                    Notation = "bpmn",
                    SemanticType = "userTask",
                    Name = "Approve request"
                }
            ]
        };

        model.Views.Add(new ModelingViewDto
        {
            Id = "main-view",
            Name = "Main view",
            Notation = "bpmn",
            Nodes =
            [
                new() { ElementId = "task-a", X = 120, Y = 160, Width = 180, Height = 90 }
            ]
        });

        return model;
    }
}
