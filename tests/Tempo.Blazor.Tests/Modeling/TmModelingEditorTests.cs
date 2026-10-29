using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Abstractions.Layout;
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
        // Compact layout only: the panel tabs are the compact-layout affordance (Q12) — the docked
        // layouts render all regions through the shell and hide the tabs.
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
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

        // The reload control lives in the inspector's Source tab on the docked layout.
        cut.Find("[data-testid='modeling-tab-source']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-source-load-button']").Should().NotBeNull());

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

    private sealed class TimestampedModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.timestamped";

        public static readonly DateTimeOffset LoadedAt = new(2026, 4, 16, 14, 32, 0, TimeSpan.Zero);

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = ModelingEditorTestModels.CreateModel();
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

        public void Release() => _gate.TrySetResult(ModelingEditorTestModels.CreateModel());

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return Task.FromResult(ModelingEditorTestModels.CreateModel());
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


    // ── Phase 5 (MO1): desktop workspace through TmEditorShell ──────────────

    private static void AddIssue(ModelingModelDto model, string elementId)
    {
        model.Issues.Add(new ModelingIssueDto
        {
            Id = $"issue-{elementId}",
            Category = "Validation",
            Severity = ModelingIssueSeverity.Warning,
            SourceElementId = elementId,
            Message = $"Element {elementId} needs a review."
        });
    }

    /// <summary>The phase 5 desktop cut: the workspace renders through the core editor shell.</summary>
    private IRenderedComponent<TmModelingEditor> RenderDesktopEditor(Action<ComponentParameterCollectionBuilder<TmModelingEditor>>? extra = null)
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        return Render<TmModelingEditor>(parameters =>
        {
            parameters.Add(p => p.LayoutMode, TmLayoutMode.Desktop);
            parameters.Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key);
            extra?.Invoke(parameters);
        });
    }

    [Fact]
    public void Desktop_renders_tree_preview_inspector_regions_via_editor_shell()
    {
        // Forced desktop (no DOM): the shell frame carries the three regions; N190 — no <main>.
        using var cut = RenderDesktopEditor();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded");
            cut.Find(".tm-editor-shell").Should().NotBeNull();
            cut.Find("[data-testid='modeling-model-tree-panel']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-preview-panel']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-inspector-panel']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-status-strip']").Should().NotBeNull();
            cut.FindAll("main").Should().BeEmpty("the host layout owns the single <main> landmark");
            cut.Find("[data-testid='modeling-model-tree-panel']").Closest("aside").Should().NotBeNull("the tree region keeps aside semantics");
            cut.Find("[data-testid='modeling-inspector-panel']").Closest("aside").Should().NotBeNull("the inspector region keeps aside semantics");
        });
    }

    [Fact]
    public void Issues_panel_renders_below_preview_with_count_header()
    {
        using var cut = RenderDesktopEditor();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-preview-panel']").Should().NotBeNull();
            // The issues dock is a sibling row under the preview, inside the shell canvas region.
            var canvas = cut.Find(".tm-editor-shell__canvas");
            canvas.InnerHtml.Should().Contain("modeling-issue-panel", "the issues panel lives in the canvas region");
            var header = cut.Find("[data-testid='modeling-issue-panel'] header");
            header.TextContent.Should().Contain("Issues");
            cut.Find("[data-testid='modeling-issue-count']").Should().NotBeNull();
        });
    }

    [Fact]
    public void Forced_tablet_renders_the_docked_tablet_band()
    {
        // User decision (breakpoints): the Modeling pair is now 768/1200 — a forced Tablet editor
        // is the shell's TABLET band, NOT the compact panel workspace: the compact branch stays
        // below the editor's own mobile threshold. Fáze 6 migrates the tablet presentation to the
        // shell's Overlay (tree drawer + inspector sheet, at most one open) — the phase-5
        // docked-column expectation is gone with it (Breaking / Migration).
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Tablet)
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded");
            cut.Find(".tm-editor-shell").Should().NotBeNull("the tablet band renders through the shell");
            cut.Find("[data-testid='modeling-preview-panel']").Should().NotBeNull();
            cut.FindAll("[data-testid='modeling-inspector-sheet']").Should().BeEmpty(
                "the tablet sheets start CLOSED — the preview is alone until the user opens one");
            cut.FindAll("[data-testid='modeling-tree-drawer']").Should().BeEmpty();
            cut.FindAll(".tm-editor-shell__panel").Should().BeEmpty("the tablet sides are overlays, not docked columns");
        });
    }

    [Fact]
    public void Auto_editor_shell_resolves_its_own_container()
    {
        // Review fix (phase 5, unforced shell): the docked TmEditorShell is NOT pinned to the
        // editor's resolved mode — an Auto shell measures its own container with the Modeling
        // pair, so the nested TmDiagramEditors (preview + open-editor dialog) resolve from THEIR
        // own containers. Proof: with an Auto editor the editor root AND its shell both register
        // with the layout-observer script using the Modeling thresholds (768/1200) — the shell's
        // registration is exactly what a forced shell never makes. A forced editor pins the
        // shell, and a forced mode never measures, so no observer registers the pair.
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/layout-observer.js");
        module.SetupVoid("observe", invocation => true);

        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));
        using var auto = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));
        auto.WaitForAssertion(() => auto.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        auto.WaitForAssertion(() =>
            ModelingPairObservations(module).Should().HaveCountGreaterThanOrEqualTo(2,
                "the editor root AND its shell classify the Modeling pair (the preview's own editor registers the same pair)"));
        var autoObservations = ModelingPairObservations(module).Count;

        // A forced editor pins the shell: neither the editor root nor the shell measures, so the
        // registration count does not grow when a second, forced editor mounts.
        using var forced = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));
        forced.WaitForAssertion(() => forced.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));
        ModelingPairObservations(module).Should().HaveCount(autoObservations,
            "a forced editor never measures (its own root or the shell)");
    }

    /// <summary>Whether the invocation registers a layout observer with the Modeling pair (768/1200).</summary>
    private static bool RegistersModelingPair(JSRuntimeInvocation invocation)
    {
        if (invocation.Arguments.Count < 4)
        {
            return false;
        }

        // ObserveOptions is an anonymous record; its text form carries the thresholds.
        var text = invocation.Arguments[3]?.ToString() ?? string.Empty;
        return text.Contains("sm = 768", StringComparison.Ordinal)
            && text.Contains("lg = 1200", StringComparison.Ordinal);
    }

    private static IReadOnlyList<JSRuntimeInvocation> ModelingPairObservations(BunitJSModuleInterop module)
        => module.Invocations.Where(RegistersModelingPair).ToList();

    [Fact]
    public void Issues_panel_collapse_toggle_has_aria_expanded_and_persists_during_session()
    {
        using var cut = RenderDesktopEditor();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-issue-panel']").Should().NotBeNull());

        var toggle = cut.Find("[data-testid='modeling-issues-toggle']");
        toggle.GetAttribute("aria-expanded").Should().Be("true");

        // The collapsed state lands through the editor's CollapsedChanged handler — an async
        // render, so wait instead of asserting on the synchronous frame.
        toggle.Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-issues-toggle']").GetAttribute("aria-expanded").Should().Be("false");
        });

        // The body STAYS in the DOM under [hidden] (a11y review, phase 5): aria-controls never
        // points at a missing id, but the list is not visible to anyone.
        var collapsedBody = cut.Find("[data-testid='modeling-issue-panel'] .tm-modeling-issue-panel__content");
        collapsedBody.GetAttribute("hidden").Should().NotBeNull("a collapsed dock hides its body");
        cut.Find("[data-testid='modeling-issues-toggle']").GetAttribute("aria-controls")
            .Should().Be(collapsedBody.GetAttribute("id"), "aria-controls targets the persistent body id");

        // The state is the session's: an unrelated re-render must not re-expand the panel.
        cut.Render();
        cut.Find("[data-testid='modeling-issues-toggle']").GetAttribute("aria-expanded").Should().Be("false");

        cut.Find("[data-testid='modeling-issues-toggle']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-issue-list']").Should().NotBeNull();
            cut.Find("[data-testid='modeling-issues-toggle']").GetAttribute("aria-expanded").Should().Be("true");
        });
        cut.Find("[data-testid='modeling-issue-panel'] .tm-modeling-issue-panel__content")
            .GetAttribute("hidden").Should().BeNull("an expanded dock shows its body");
    }

    [Fact]
    public async Task Inspector_has_tabs_properties_and_source()
    {
        using var cut = RenderDesktopEditor();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-inspector-panel']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-inspector-panel'] [role='tablist']").Should().NotBeNull();

        var tabs = cut.FindAll("[data-testid='modeling-inspector-panel'] [role='tab']").ToList();
        tabs.Should().HaveCount(2);
        tabs[0].TextContent.Should().Contain("Properties");
        tabs[1].TextContent.Should().Contain("Source");
        tabs[0].GetAttribute("aria-selected").Should().Be("true");
        tabs[1].GetAttribute("aria-selected").Should().Be("false");

        // Only the ACTIVE tab renders content (M26 budget: one inspector body at a time).
        cut.FindAll("[data-testid='modeling-inspector']").Should().NotBeEmpty();
        cut.FindAll("[data-testid='modeling-source-panel']").Should().BeEmpty();

        // The tab selection lands through the editor's callback — an async render.
        await cut.InvokeAsync(() => cut.FindAll("[data-testid='modeling-inspector-panel'] [role='tab']")[1].Click());
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='modeling-source-panel']").Should().NotBeEmpty();
            cut.FindAll("[data-testid='modeling-inspector']").Should().BeEmpty();
            cut.FindAll("[data-testid='modeling-inspector-panel'] [role='tab']").ToList()[1]
                .GetAttribute("aria-selected").Should().Be("true");
        });
    }

    [Fact]
    public async Task Source_tab_reload_invokes_ReloadModelAsync()
    {
        var provider = new CountingModelingModelProvider();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(provider));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ProviderKey, CountingModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));
        var initialLoadCount = GetLoadCount(cut);
        provider.LoadCount.Should().Be(1);

        cut.Find("[data-testid='modeling-tab-source']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-source-load-button']").Should().NotBeNull());
        cut.Find("[data-testid='modeling-source-load-button']").Click();

        // The Source tab reload drives the editor's ReloadModelAsync — the provider is asked again.
        cut.WaitForAssertion(() =>
        {
            provider.LoadCount.Should().Be(2);
            GetLoadCount(cut).Should().BeGreaterThan(initialLoadCount);
        });
    }

    [Fact]
    public void Stale_source_marks_source_tab_with_indicator()
    {
        // CreateModel's metadata has no IsFresh=true — the stale provider serves exactly that.
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new StaleSuccessfulModelingModelProvider()));
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new FreshSuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ProviderKey, StaleSuccessfulModelingModelProvider.Key));
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-inspector-panel']").Should().NotBeNull());

        var sourceTab = cut.FindAll("[data-testid='modeling-inspector-panel'] [role='tab']")
            .Single(tab => tab.TextContent.Contains("Source"));
        var badge = sourceTab.QuerySelector(".tm-tab__badge");
        badge.Should().NotBeNull("a stale source marks the Source tab");
        badge!.TextContent.Should().Contain("Stale", "the badge is a real word (a11y review, phase 5)");
        badge.TextContent.Should().NotContain("\u2022", "never a bare glyph");
        sourceTab.GetAttribute("data-stale").Should().Be("true");

        // The tab's accessible NAME stays its visible label; the stale hint is an sr-only span
        // OUTSIDE the tablist, referenced through aria-describedby.
        sourceTab.GetAttribute("aria-label").Should().BeNull();
        var hintId = sourceTab.GetAttribute("aria-describedby");
        hintId.Should().NotBeNull();
        var hint = cut.Find($"#{hintId}");
        hint.ClassList.Should().Contain("tm-sr-only");
        hint.TextContent.Should().Contain("stale", "the hint carries the localized SourceStaleHint text");

        // And a fresh source shows no indicator.
        using var fresh = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ProviderKey, FreshSuccessfulModelingModelProvider.Key));
        fresh.WaitForAssertion(() => fresh.Find("[data-testid='modeling-inspector-panel']").Should().NotBeNull());
        var freshTab = fresh.FindAll("[data-testid='modeling-inspector-panel'] [role='tab']")
            .Single(tab => tab.TextContent.Contains("Source"));
        freshTab.QuerySelector(".tm-tab__badge").Should().BeNull("a fresh source shows no tab indicator");
        freshTab.GetAttribute("data-stale").Should().BeNull();
        freshTab.GetAttribute("aria-describedby").Should().BeNull();
    }

    [Fact]
    public void Issue_selection_still_selects_tree_element()
    {
        // Regression (SelectIssueAsync): activating a navigable issue selects its tree element.
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new IssueModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ProviderKey, IssueModelingModelProvider.Key));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-issue-0']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-issue-0']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modeling-editor']").GetAttribute("data-selected-element-id").Should().Be("task-a");
            cut.Find("[data-testid='modeling-tree-node-task-a']").GetAttribute("aria-selected").Should().Be("true");
        });
    }

    [Fact]
    public void Panel_tabs_hidden_on_desktop()
    {
        // Q12: the compact panel tabs stay a compact-layout affordance only.
        using var cut = RenderDesktopEditor();
        cut.WaitForAssertion(() => cut.Find(".tm-editor-shell").Should().NotBeNull());
        cut.FindAll("[data-testid='modeling-panel-tabs']").Should().BeEmpty();

        // And the compact layout keeps them (today's behavior).
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));
        using var compact = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));
        compact.WaitForAssertion(() => compact.Find("[data-testid='modeling-panel-tabs']").Should().NotBeNull());
    }

    [Fact]
    public void Data_active_panel_and_test_ids_preserved()
    {
        using var cut = RenderDesktopEditor();
        cut.WaitForAssertion(() =>
        {
            var root = cut.Find("[data-testid='modeling-editor']");
            root.GetAttribute("data-active-panel").Should().Be("preview");
            root.GetAttribute("data-issue-count").Should().NotBeNull();
            root.GetAttribute("data-state").Should().Be("loaded");
        });

        // The compact layout still drives data-active-panel through the panel tabs.
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));
        using var compact = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key));
        compact.WaitForAssertion(() => compact.Find("[data-testid='modeling-panel-tabs']").Should().NotBeNull());
        compact.Find("[data-testid='modeling-panel-tab-tree']").Click();
        compact.WaitForAssertion(() =>
            compact.Find("[data-testid='modeling-editor']").GetAttribute("data-active-panel").Should().Be("tree"));
    }

    [Fact]
    public void Height_parameter_overrides_the_default_block_size()
    {
        // User decision (height contract): additive Height, default keeps min(768px, 100vh - 32px).
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new SuccessfulModelingModelProvider()));

        using var cut = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ProviderKey, SuccessfulModelingModelProvider.Key)
            .Add(p => p.Height, "600px"));

        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-editor']").Should().NotBeNull());
        cut.Find("[data-testid='modeling-editor']").GetAttribute("style").Should().Contain("600px");

        using var cutDefault = RenderDesktopEditor();
        cutDefault.WaitForAssertion(() => cutDefault.Find("[data-testid='modeling-editor']").Should().NotBeNull());
        cutDefault.Find("[data-testid='modeling-editor']").GetAttribute("style").Should().BeNull("the default height contract lives in the stylesheet");
    }

    private static int GetLoadCount(IRenderedComponent<TmModelingEditor> cut)
    {
        var raw = cut.Find("[data-testid='modeling-editor']").GetAttribute("data-load-count");
        return int.TryParse(raw, out var parsed) ? parsed : 0;
    }

    private sealed class StaleSuccessfulModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.stale";

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ModelingEditorTestModels.CreateModel());
        }
    }

    private sealed class FreshSuccessfulModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.fresh";

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = ModelingEditorTestModels.CreateModel();
            model.Metadata.IsFresh = true;
            return Task.FromResult(model);
        }
    }

    private sealed class IssueModelingModelProvider : IModelingModelProvider
    {
        public const string Key = "tempo.tests.modeling.issue";

        public string ProviderKey => Key;

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = ModelingEditorTestModels.CreateModel();
            AddIssue(model, "task-a");
            return Task.FromResult(model);
        }
    }
}
