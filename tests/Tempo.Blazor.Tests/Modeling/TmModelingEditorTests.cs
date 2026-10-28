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
    public void Provider_selector_lists_registered_providers_when_enabled()
    {
        // Add (NOT TryAddEnumerable): two instances of the same provider class — TryAddEnumerable
        // dedupes by implementation and would silently drop the second one.
        var alpha = new CountingModelingModelProvider("tempo.tests.modeling.alpha");
        var beta = new CountingModelingModelProvider("tempo.tests.modeling.beta");
        Services.AddSingleton<IModelingModelProvider>(alpha);
        Services.AddSingleton<IModelingModelProvider>(beta);

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, "tempo.tests.modeling.alpha")
            .Add(p => p.ShowProviderSelector, true));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        var select = cut.Find("[data-testid='modeling-provider-select']");
        select.QuerySelectorAll("option")
            .Select(option => option.GetAttribute("value"))
            .Should().Contain(["tempo.tests.modeling.alpha", "tempo.tests.modeling.beta"]);

        // The select is a labelled field: the visible label precedes the field (label ABOVE, MO3).
        var field = select.ParentElement;
        field.Should().NotBeNull("the select is wrapped in its label");
        field!.TagName.Should().Be("LABEL", "the label wraps the field so the select is programmatically labelled");
        field.TextContent.Should().Contain("Provider");
        field.Children[0].TextContent.Should().Contain("Provider", "the label renders above the field");
        cut.FindAll(".tm-modeling-editor__chip").Should().BeEmpty("the provider chip is replaced by the select");
    }

    [Fact]
    public async Task Provider_change_raises_ProviderKeyChanged_and_reloads_model()
    {
        // Add (NOT TryAddEnumerable): two instances of the same provider class — see the listing test.
        var alpha = new CountingModelingModelProvider("tempo.tests.modeling.alpha");
        var beta = new CountingModelingModelProvider("tempo.tests.modeling.beta");
        Services.AddSingleton<IModelingModelProvider>(alpha);
        Services.AddSingleton<IModelingModelProvider>(beta);

        string? bindRaised = null;
        string? eventRaised = null;

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, "tempo.tests.modeling.alpha")
            .Add(p => p.ShowProviderSelector, true)
            .Add(p => p.ProviderKeyChanged, EventCallback.Factory.Create<string?>(this, value => bindRaised = value))
            .Add(p => p.OnProviderChanged, EventCallback.Factory.Create<string?>(this, value => eventRaised = value)));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));
        alpha.LoadCount.Should().Be(1);

        // Flush the post-load renders (the diagram editor's async interop re-renders the
        // workspace and replaces nodes) so the select handle is attached when the change fires.
        cut.Render();
        cut.Find("[data-testid='modeling-provider-select']").Change("tempo.tests.modeling.beta");

        bindRaised.Should().Be("tempo.tests.modeling.beta");
        eventRaised.Should().Be("tempo.tests.modeling.beta");
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-provider").Should().Be("tempo.tests.modeling.beta");
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded");
        });
        beta.LoadCount.Should().Be(1, "the provider change reloads the model through the new provider");
        alpha.LoadCount.Should().Be(1, "the old provider is not asked again");
    }

    [Fact]
    public void Provider_selector_hidden_by_default_shows_readonly_provider_label()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.FindAll("[data-testid='modeling-provider-select']").Should().BeEmpty();
        cut.Find(".tm-modeling-editor__chip")
            .TextContent.Should().Contain("Provider")
            .And.Contain(SuccessfulModelingModelProvider.Key, "the provider stays a labelled value when the selector is off");
    }

    [Fact]
    public async Task Host_provider_binding_round_trip_does_not_reload_the_new_provider_twice()
    {
        var alpha = new CountingModelingModelProvider("tempo.tests.modeling.alpha");
        var beta = new CountingModelingModelProvider("tempo.tests.modeling.beta");
        Services.AddSingleton<IModelingModelProvider>(alpha);
        Services.AddSingleton<IModelingModelProvider>(beta);

        // The host side of @bind-ProviderKey: the editor raises ProviderKeyChanged, the host
        // stores the value and renders it back through the ProviderKey parameter.
        string? hostProvider = "tempo.tests.modeling.alpha";
        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, hostProvider)
            .Add(p => p.ShowProviderSelector, true)
            .Add(p => p.ProviderKeyChanged, EventCallback.Factory.Create<string?>(this, value => hostProvider = value)));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));
        alpha.LoadCount.Should().Be(1);

        cut.Render();
        cut.Find("[data-testid='modeling-provider-select']").Change("tempo.tests.modeling.beta");

        // The round trip: the host re-renders with the value the editor just reported.
        cut.Render(parameters => parameters
            .Add(p => p.ProviderKey, hostProvider)
            .Add(p => p.ShowProviderSelector, true));

        cut.WaitForAssertion(() =>
        {
            var editor = cut.Find("[data-testid='modeling-editor']");
            editor.GetAttribute("data-provider").Should().Be("tempo.tests.modeling.beta");
            editor.GetAttribute("data-state").Should().Be("loaded");
        });
        beta.LoadCount.Should().Be(1, "the round-tripped parameter must not trigger a second load");
        alpha.LoadCount.Should().Be(1, "the old provider is not asked again");
    }

    [Fact]
    public async Task Fast_provider_switch_back_discards_the_slow_provider_result()
    {
        var alpha = new GatedSwitchModelingModelProvider("tempo.tests.modeling.alpha", "alpha-element", 2, gated: false);
        var beta = new GatedSwitchModelingModelProvider("tempo.tests.modeling.beta", "beta-element", 5, gated: true);
        Services.AddSingleton<IModelingModelProvider>(alpha);
        Services.AddSingleton<IModelingModelProvider>(beta);

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, "tempo.tests.modeling.alpha")
            .Add(p => p.ShowProviderSelector, true));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded");
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-element-count").Should().Be("2");
        });

        cut.Render();
        // A → B: beta is gated, the load stays in flight.
        cut.Find("[data-testid='modeling-provider-select']").Change("tempo.tests.modeling.beta");
        // … and immediately back to A: the beta load is superseded.
        cut.Find("[data-testid='modeling-provider-select']").Change("tempo.tests.modeling.alpha");

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-provider")
                .Should().Be("tempo.tests.modeling.alpha");
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded");
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-element-count").Should().Be("2");
        });

        // Release beta now: its stale result must never reach the editor state.
        beta.Release();
        await Task.Delay(200);
        cut.Render();

        cut.Find("[data-testid='modeling-editor']").GetAttribute("data-provider")
            .Should().Be("tempo.tests.modeling.alpha", "the beta result is never applied");
        cut.Find("[data-testid='modeling-editor']").GetAttribute("data-element-count")
            .Should().Be("2", "the beta model (5 elements) never replaced the alpha model");
        cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded");
        cut.Find("[data-testid='modeling-editor']").GetAttribute("data-notation")
            .Should().Be("", "the notation override reset to the parameter baseline (none set)");
    }

    [Fact]
    public void Header_does_not_duplicate_notation_and_viewpoint_chips()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
            .Add(p => p.NotationKey, "bpmn"));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        var chips = cut.FindAll(".tm-modeling-editor__chip");
        chips.Should().HaveCount(1, "notation and viewpoint chips are gone — the labeled selects own those values");
        chips[0].TextContent.Should().Contain("Provider");
        chips[0].TextContent.Should().NotContain("Notation").And.NotContain("Viewpoint");
    }

    [Fact]
    public void Status_strip_shows_last_loaded_time_when_metadata_present()
    {
        // Both providers are registered BEFORE the first render — bUnit freezes the service
        // provider once a component has been rendered from it.
        Services.AddSingleton<IModelingModelProvider>(new TimestampedModelingModelProvider());
        Services.AddSingleton<IModelingModelProvider>(new SuccessfulModelingModelProvider());

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, TimestampedModelingModelProvider.Key));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        var stateItem = cut.Find("[data-testid='modeling-status-strip'] .tm-modeling-editor__status-item--state");
        stateItem.TextContent.Should().Contain(
            $"Last loaded: {TimestampedModelingModelProvider.LoadedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)}",
            "the localized 'Last loaded: {0}' format substitutes the shared TmModelingSourcePanel time");

        // Review fix (phase 3): the state stays available to screen readers — an sr-only
        // "Loaded – " lead-in precedes the last-loaded time (the visible text is just the time).
        var srOnly = stateItem.QuerySelector(".tm-sr-only");
        srOnly.Should().NotBeNull("the state label is kept for screen readers before the time");
        srOnly!.TextContent.Should().Be("Loaded – ");

        // The state/last-loaded item is the LAST strip item (margin-inline-start:auto anchors
        // it at the trailing edge); the counts lead the strip.
        cut.FindAll("[data-testid='modeling-status-strip'] > .tm-modeling-editor__status-item")
            .Last().ClassList.Should().Contain("tm-modeling-editor__status-item--state");

        // Fallback: without metadata the strip keeps the state label (no sr-only duplication).
        using var plain = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        plain.WaitForAssertion(() =>
            plain.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));
        var plainItem = plain.Find("[data-testid='modeling-status-strip'] .tm-modeling-editor__status-item--state");
        plainItem.TextContent.Should().Contain("Loaded", "without metadata the strip falls back to the state label");
        plainItem.QuerySelector(".tm-sr-only").Should().BeNull("the state label is not duplicated for screen readers");
    }

    [Fact]
    public void Status_strip_anchors_the_last_loaded_item_at_the_trailing_edge()
    {
        Services.AddSingleton<IModelingModelProvider>(new SuccessfulModelingModelProvider());

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        var css = File.ReadAllText(FindEditorCssPath());
        css.Should().Contain(".tm-modeling-editor__status-item--state").And.Contain("margin-inline-start: auto",
            "the state/last-loaded item is pushed to the trailing edge of the strip");
    }

    private static string FindEditorCssPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent!;
        }

        directory.Should().NotBeNull("repo root with TempoBlazor.slnx must be reachable from the test output");
        return Path.Combine(
            directory!.FullName,
            "src", "Tempo.Blazor.Modeling", "Components", "Modeling", "TmModelingEditor.razor.css");
    }

    [Fact]
    public void Provider_display_name_resolves_option_text_and_chip_value()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
            .Add(p => p.ShowProviderSelector, true)
            .Add(p => p.ProviderDisplayName, key => $"Pretty {key}"));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-provider-select']")
            .TextContent.Should().Contain($"Pretty {SuccessfulModelingModelProvider.Key}",
                "the header select option text goes through the display-name resolver");

        using var chip = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
            .Add(p => p.ProviderDisplayName, key => $"Pretty {key}"));

        chip.Find(".tm-modeling-editor__chip strong").TextContent.Should().Be($"Pretty {SuccessfulModelingModelProvider.Key}");
    }

    [Fact]
    public void Provider_display_name_falls_back_to_the_raw_key()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
            .Add(p => p.ShowProviderSelector, true));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-provider-select']")
            .TextContent.Should().Contain(SuccessfulModelingModelProvider.Key,
                "without a resolver the raw provider key is shown");
    }

    [Fact]
    public async Task Reload_indicator_announces_loading_without_repeating_the_state_text()
    {
        var provider = new GatedModelingModelProvider();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(provider));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, GatedModelingModelProvider.Key));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        // Sync click: the reload handler awaits the gated provider, so awaiting the click itself
        // would deadlock (the gate opens only after the click returns).
        cut.Find("[data-testid='modeling-source-load-button']").Click();

        cut.WaitForAssertion(() =>
        {
            var indicator = cut.Find("[data-testid='modeling-editor-loading']");
            indicator.QuerySelector(".tm-spinner").Should().NotBeNull("the indicator keeps the progress affordance");
            indicator.QuerySelectorAll(".tm-modeling-editor__reload-label")
                .Should().BeEmpty("the visible 'Loading' text is deduped — the title badge owns the state");
            indicator.QuerySelector(".tm-sr-only")
                .TextContent.Should().Contain("Loading model", "screen readers still get the announcement");
        }, TimeSpan.FromSeconds(10));

        provider.Release();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));
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
    public async Task Open_in_editor_renders_tm_modal_with_backdrop_and_dialog_semantics()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();

        // The dialog is a core TmModal: a viewport-positioned overlay with the scrim class,
        // carrying the preserved modeling-open-diagram-editor test id on its root.
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-open-diagram-editor']").ClassList.Should().Contain("tm-modal-overlay",
                "the 'Open in editor' surface is a TmModal overlay (scrim), not an in-editor box"));
        var overlay = cut.Find("[data-testid='modeling-open-diagram-editor']");

        var dialog = cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-focus-scope");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = dialog.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty("the dialog is named by its title element");
        cut.Find($"[data-testid='modeling-open-diagram-editor'] [id='{labelledBy}']").Should().NotBeNull();
    }

    [Fact]
    public async Task Open_editor_title_uses_document_title_and_falls_back_to_open_in_editor()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();

        // The generated document carries the model title — the modal header shows it, never the
        // old "Preview" fallback (the pre-redesign default).
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-modal-title").TextContent.Should().Be("Modeling editor test"));

        // A document without a title falls back to the localized "Open in editor" label.
        var untitled = CreateModel();
        untitled.Title = string.Empty;
        var document = cut.Instance.DiagramGenerator.Generate(untitled, new ModelingDiagramGenerationOptionsDto
        {
            IncludeIssues = true
        }).Document;
        document.Title = " ";
        // The trigger hands the preview's CURRENT document to the editor's open path — hand it
        // the untitled one the same way the real click would.
        var preview = cut.FindComponent<Tempo.Blazor.Components.Modeling.TmModelingDiagramPreview>();
        await cut.InvokeAsync(() => preview.Instance.OnOpenInEditor.InvokeAsync(document));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-modal-title").TextContent
                .Should().Be("Open in editor", "an untitled document falls back to the localized open-in-editor label"));
    }

    [Fact]
    public async Task Escape_closes_open_editor_dialog_and_restores_focus_to_trigger()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        // The trigger records itself as the dialog's restore target (the focus-scope restore
        // contract, resolved by the module at deactivation).
        var triggerId = cut.Find("[data-testid='modeling-open-in-editor-button']").GetAttribute("id");
        triggerId.Should().NotBeNullOrEmpty("the trigger must carry the id the dialog restores focus to");
        var restoreTarget = cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-focus-scope")
            .GetAttribute("data-restore-target");
        restoreTarget.Should().Be(triggerId, "Escape/restore returns focus to the 'Open in editor' trigger");

        // Escape arrives through the focus-trap module callback (document-level listener):
        // the topmost modal scope raises OnEscape and TmModal closes through the editor's
        // close path, which restores the trigger focus.
        var modal = cut.FindComponents<Tempo.Blazor.Components.Feedback.TmModal>()
            .Single(component => component.Markup.Contains("modeling-open-diagram-editor"));
        var scope = modal.FindComponents<Tempo.Blazor.Components.Feedback.TmFocusScope>().Single();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().BeEmpty());
        module.Invocations
            .Where(i => i.Identifier == "focusWithin")
            .Select(i => (string)i.Arguments[1]!)
            .Should().Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenInEditorFocusSelector,
                "the close path restores focus onto the trigger through its data-modeling-focus hook");
    }

    [Fact]
    public async Task Escape_is_not_handled_when_diagram_editor_consumes_it()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        // An Escape raised inside the diagram editor surface — its own view-settings menu is a
        // Blazor consumer (the canvas search/drag consumers are JS-side, same order) — is
        // consumed by the editor FIRST; the dialog layer must not close on it either.
        var viewMenu = cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor__view-menu");
        viewMenu.TriggerEvent("onkeydown", new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull(
            "an Escape the diagram editor consumes must not close the dialog through the Blazor layer");

        // Only the unhandled path — the focus-scope module callback — closes the dialog.
        var modal = cut.FindComponents<Tempo.Blazor.Components.Feedback.TmModal>()
            .Single(component => component.Markup.Contains("modeling-open-diagram-editor"));
        var scope = modal.FindComponents<Tempo.Blazor.Components.Feedback.TmFocusScope>().Single();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().BeEmpty());
    }

    [Fact]
    public async Task Workspace_is_inert_while_dialog_open()
    {
        var focusTrap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        focusTrap.SetupVoid("activate", _ => true).SetVoidResult();
        focusTrap.SetupVoid("noteReturnTarget", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();

        // The modal scope is a MODAL trap: aria-modal on the dialog and modal=true handed to the
        // focus-trap module (the module inertes the page for a modal trap only).
        cut.WaitForAssertion(() =>
        {
            var dialog = cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-focus-scope");
            dialog.GetAttribute("aria-modal").Should().Be("true");
        });

        focusTrap.Invocations
            .Where(i => i.Identifier == "activate")
            .Select(i => i.Arguments[5])
            .Should().Contain(true, "the dialog activates a modal trap — the module inertes the background");

        // The workspace stays OUTSIDE the dialog subtree: the trap's inert background covers it.
        var overlay = cut.Find("[data-testid='modeling-open-diagram-editor']");
        overlay.QuerySelector(".tm-modeling-editor__workspace").Should().BeNull(
            "the modeling workspace is the inert background, not dialog content");
        cut.Find(".tm-modeling-editor__workspace").Should().NotBeNull();
    }

    [Fact]
    public async Task Dialog_has_single_header_without_duplicate_title()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-modal-header").Should().NotBeNull());

        // One header: the TmModal header owns the title and subtitle; the embedded diagram
        // editor renders WITHOUT its own document title (ShowTitle=false through the editor API).
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-modal-header").Should().ContainSingle();
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor__title").Should().BeEmpty(
            "the dialog title lives in the TmModal header only — the diagram editor hides its own");
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-modal-title").Should().ContainSingle();
        cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-modal-subtitle").TextContent
            .Should().Be("Full diagram editor for the generated modeling document.");
    }

    [Fact]
    public async Task Live_changes_propagate_to_preview_without_footer()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        // G8: the dialog has no footer — changes keep propagating live.
        cut.FindAll("[data-testid='modeling-open-diagram-editor'] .tm-modal-footer").Should().BeEmpty(
            "live propagation has no Apply/Cancel footer");

        // A diagram-editor change flows through the close-free path: the dialog's document AND
        // the preview document behind it update (the status strip counts the nodes).
        var modal = cut.FindComponents<Tempo.Blazor.Components.Feedback.TmModal>()
            .Single(component => component.Markup.Contains("modeling-open-diagram-editor"));
        var embeddedEditor = modal.FindComponents<Tempo.Blazor.Components.Diagram.TmDiagramEditor>().Single();
        var current = embeddedEditor.Instance.Document;
        current.Should().NotBeNull("the dialog embeds the generated document");
        var modified = new DiagramDocument
        {
            Id = current.Id,
            Title = "Renamed by the editor",
            CreatedAt = current.CreatedAt,
            ModifiedAt = current.ModifiedAt,
            Pages = current.Pages,
            ActivePageIndex = current.ActivePageIndex,
            Nodes = current.Nodes.Append(new DiagramNode
            {
                Id = "editor-added-node",
                StencilId = "general.rectangle",
                X = 40,
                Y = 40,
                W = 120,
                H = 60
            }).ToList(),
            Edges = current.Edges
        };
        await cut.InvokeAsync(() => embeddedEditor.Instance.DocumentChanged.InvokeAsync(modified));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-diagram-node-count").Should().Be("2");
            cut.Find("[data-testid='modeling-open-diagram-editor'] .tm-modal-title").TextContent.Should().Be("Renamed by the editor");
            cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull(
                "the change did not close the dialog");
        });

        // A scrim click must not close the dialog either (live changes must not be lost).
        cut.Find("[data-testid='modeling-open-diagram-editor']").Click();
        cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull(
            "the scrim is not a close affordance while edits propagate live");
    }

    [Fact]
    public async Task Provider_change_closes_open_editor_dialog_through_the_close_path()
    {
        // Phase-3 carry-forward, re-verified on the TmModal dialog (phase 4): a header provider
        // change closes the open dialog through CloseGeneratedDiagramAsync — the same path the
        // close button uses — so the focus restore onto the preview trigger still runs.
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.SetupVoid("focusWithin", _ => true).SetVoidResult();

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new CountingModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
            .Add(p => p.ShowProviderSelector, true));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-provider-select']").Change(CountingModelingModelProvider.Key);

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modeling-open-diagram-editor']").Should().BeEmpty());
        module.Invocations
            .Where(i => i.Identifier == "focusWithin")
            .Select(i => (string)i.Arguments[1]!)
            .Should().Contain(Tempo.Blazor.Components.Modeling.TmModelingEditor.OpenInEditorFocusSelector,
                "the provider change closes through the close path, restoring the trigger focus");
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
        private readonly string _key;

        public CountingModelingModelProvider(string key = Key)
        {
            _key = key;
        }

        public const string Key = "tempo.tests.modeling.counting";

        public int LoadCount { get; private set; }

        public string ProviderKey => _key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return Task.FromResult(CreateModel());
        }
    }

    private sealed class TimestampedModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.timestamped";

        public static readonly DateTimeOffset LoadedAt = new(2026, 4, 16, 14, 32, 0, TimeSpan.Zero);

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = CreateModel();
            model.Metadata.LoadedAt = LoadedAt;
            return Task.FromResult(model);
        }
    }

    private sealed class GatedModelingModelProvider : IModelingModelProvider
    {
        private TaskCompletionSource<ModelingModelDto> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public const string Key = "tempo.tests.modeling.gated";

        public string ProviderKey => Key;

        public void Release() => _gate.TrySetResult(CreateModel());

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return Task.FromResult(CreateModel());
            }

            var gate = _gate;
            cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));
            return gate.Task;
        }
    }

    /// <summary>Provider whose model is distinguishable by element id/count; optionally blocks
    /// every call behind a gate until <see cref="Release"/> (fast A→B→A switch tests).</summary>
    private sealed class GatedSwitchModelingModelProvider : IModelingModelProvider
    {
        private readonly string _key;
        private readonly string _elementId;
        private readonly int _elementCount;
        private readonly bool _gated;
        private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedSwitchModelingModelProvider(string key, string elementId, int elementCount, bool gated)
        {
            _key = key;
            _elementId = elementId;
            _elementCount = elementCount;
            _gated = gated;
        }

        public string ProviderKey => _key;

        public void Release() => _gate.TrySetResult();

        public async Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            if (_gated)
            {
                var release = new TaskCompletionSource();
                await using var _ = cancellationToken.Register(() => release.TrySetCanceled(cancellationToken));
                await Task.WhenAny(_gate.Task, release.Task);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var model = new ModelingModelDto
            {
                Id = $"modeling-editor-switch-{_key}",
                Title = $"Modeling editor switch {_key}",
                Metadata = new ModelingMetadataDto { SourceSystem = "Unit tests" }
            };

            for (var index = 0; index < _elementCount; index++)
            {
                model.Elements.Add(new ModelingElementDto
                {
                    Id = $"{_elementId}-{index}",
                    SourceId = $"{_elementId}-{index}",
                    Notation = "bpmn",
                    SemanticType = "userTask",
                    Name = $"{_elementId} {index}"
                });
            }

            model.Views.Add(new ModelingViewDto
            {
                Id = "main-view",
                Name = "Main view",
                Notation = "bpmn",
                Nodes = model.Elements.Select(element => new ModelingViewNodeDto
                {
                    ElementId = element.Id,
                    X = 120,
                    Y = 160,
                    Width = 180,
                    Height = 90
                }).ToList()
            });

            return model;
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
