using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingEditorTests : LocalizationTestBase
{
    private const string ModelingModulePath = "./_content/Tempo.Blazor.Modeling/js/modeling-editor.js";

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

    [Fact]
    public void Loaded_state_renders_status_badge_next_to_title()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() =>
        {
            var badge = cut.FindComponents<TmBadge>()
                .Single(b => b.Markup.Contains("Loaded", StringComparison.Ordinal));
            badge.Instance.Variant.Should().Be(BadgeVariant.Success);

            cut.Find(".tm-modeling-editor__title-group .tm-badge").TextContent
                .Should().Contain("Loaded", "the state badge renders next to the title");
        });
    }

    [Fact]
    public void State_badge_variants_track_editor_state()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new ThrowingModelingModelProvider()));

        using var empty = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, "missing-provider"));

        empty.FindComponents<TmBadge>()
            .Should().Contain(b => b.Instance.Variant == BadgeVariant.Default
                && b.Markup.Contains("No provider", StringComparison.Ordinal));

        using var failing = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, ThrowingModelingModelProvider.Key));

        failing.WaitForAssertion(() =>
            failing.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("error"));

        failing.FindComponents<TmBadge>()
            .Should().Contain(b => b.Instance.Variant == BadgeVariant.Danger
                && b.Markup.Contains("Error", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_and_error_state_marks_are_icons()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new ThrowingModelingModelProvider()));

        using var empty = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, "missing-provider"));

        var emptyMark = empty.Find("[data-testid='modeling-editor-empty'] .tm-modeling-editor__state-mark");
        emptyMark.QuerySelector("svg").Should().NotBeNull("the empty state mark is an icon");
        emptyMark.TextContent.Trim().Should().BeEmpty("the '0' text glyph is gone");

        using var failing = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, ThrowingModelingModelProvider.Key));

        failing.WaitForAssertion(() =>
            failing.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("error"));

        var errorMark = failing.Find("[data-testid='modeling-editor-error'] .tm-modeling-editor__state-mark");
        errorMark.QuerySelector("svg").Should().NotBeNull("the error state mark is an icon");
        errorMark.TextContent.Trim().Should().BeEmpty("the '!' text glyph is gone");
    }

    [Fact]
    public void Status_strip_renders_state_icon_and_separated_items()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() =>
        {
            var strip = cut.Find("[data-testid='modeling-status-strip']");
            strip.QuerySelectorAll(".tm-modeling-editor__status-item").Should().HaveCountGreaterThanOrEqualTo(5);
            strip.QuerySelector(".tm-modeling-editor__status-icon svg").Should().NotBeNull(
                "the status strip leads with a state icon");
        });
    }

    [Fact]
    public async Task Focus_return_after_closing_editor_is_root_scoped_and_test_id_free()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-open-diagram-close']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().BeEmpty());

        JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "eval")
            .Should().BeEmpty("focus restoration must stay CSP-safe — no eval");

        var moduleInvocations = module.Invocations.Select(invocation => invocation.Identifier).ToList();
        moduleInvocations.Should().Contain("focusWithin",
            "the overlay focus move and the focus return go through the modeling-editor.js module");
        moduleInvocations.Count(id => id == "focusWithin").Should().BeGreaterThanOrEqualTo(2,
            "one call focuses the close button when the overlay opens, the second restores the trigger");

        // Every focusWithin call is scoped to the editor root ElementReference and never keys
        // off data-testid (the stable data-modeling-focus hooks are the contract — a host
        // renaming test ids through TestIdPrefix must not break focus return).
        foreach (var invocation in module.Invocations.Where(i => i.Identifier == "focusWithin"))
        {
            invocation.Arguments.Should().HaveCount(2);
            invocation.Arguments[0].Should().BeOfType<ElementReference>(
                "the query is scoped to the editor root, not the document");
            invocation.Arguments[1]!.ToString()!.Should().NotContain("data-testid");
        }

        var selectors = module.Invocations
            .Where(i => i.Identifier == "focusWithin")
            .Select(i => (string)i.Arguments[1]!)
            .ToList();
        selectors.Should().Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenEditorCloseFocusSelector,
            "opening the overlay focuses the close button through its data-modeling-focus hook");
        selectors.Should().Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenInEditorFocusSelector,
            "closing the overlay returns focus to the trigger through its data-modeling-focus hook");
    }

    [Fact]
    public async Task Focus_return_still_targets_the_data_modeling_focus_hooks_with_a_test_id_prefix()
    {
        // TestIdPrefix namespaces the editor-owned ids ("a-modeling-open-diagram-close", …) while
        // the preview child's trigger keeps its bare id. The focus return must keep working across
        // BOTH because it targets data-modeling-focus hooks, never data-testid.
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
            .Add(p => p.TestIdPrefix, "a"));

        cut.WaitForAssertion(() => cut.Find("[data-testid='a-modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='a-modeling-open-diagram-editor']").Should().NotBeNull();
            cut.Find("[data-testid='a-modeling-open-diagram-close']")
                .Should().NotBeNull("the editor namespaced its own close button");
        });

        cut.Find("[data-testid='a-modeling-open-diagram-close']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='a-modeling-open-diagram-editor']").Should().BeEmpty());

        module.Invocations
            .Where(i => i.Identifier == "focusWithin")
            .Select(i => (string)i.Arguments[1]!)
            .Should().Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenEditorCloseFocusSelector)
            .And.Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenInEditorFocusSelector,
                "the focus return is driven by the stable data-modeling-focus hooks, not data-testid");
    }

    [Fact]
    public async Task Disposing_the_editor_disposes_the_imported_module()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        // The module wrapper is created on the first focus request: open + close the overlay.
        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());
        cut.Find("[data-testid='modeling-open-diagram-close']").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().BeEmpty());

        var wrapper = cut.Instance.TestInteropModule;
        wrapper.Should().NotBeNull("the overlay focus path created the module wrapper");

        await cut.Instance.DisposeAsync();

        wrapper!.IsDisposed.Should().BeTrue(
            "@implements IAsyncDisposable must dispose the imported ES module, or the module object leaks");
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
