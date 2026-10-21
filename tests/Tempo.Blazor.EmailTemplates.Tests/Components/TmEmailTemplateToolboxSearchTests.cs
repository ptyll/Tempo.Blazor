using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Tests.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7A (redesign, screen <c>email-template-editor</c>): the blocks panel gains a search field that
/// filters the tiles by their LOCALIZED name (diacritics-insensitive), a localized empty state, an
/// Escape-to-clear affordance, icon-above-label tiles whose label never crops, and a collapse toggle
/// (<c>ToolboxCollapsed</c>). The tiles keep <c>data-tm-block</c> and the groups keep following
/// <see cref="BlockDescriptor.Category"/>.
/// </summary>
public class TmEmailTemplateToolboxSearchTests : BunitContext, IDisposable
{
    private CultureScope? _culture;

    public TmEmailTemplateToolboxSearchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    void IDisposable.Dispose()
    {
        _culture?.Dispose();
        Dispose();
    }

    /// <summary>Pins the culture for the rest of this test; the scope is restored when the test ends.</summary>
    private void UseCulture(string culture) => _culture = CultureScope.Use(culture);

    private static void Search(IRenderedComponent<TmEmailTemplateToolbox> cut, string text)
        => cut.Find("[data-tm-toolbox-search] input").Input(text);

    [Fact]
    public void Search_placeholder_is_localized()
    {
        UseCulture("cs");

        var cut = Render<TmEmailTemplateToolbox>();

        cut.Find("[data-tm-toolbox-search] input").GetAttribute("placeholder").Should().Be("Hledat bloky…");
    }

    [Fact]
    public void Search_filters_tiles_by_the_localized_name()
    {
        UseCulture("en");

        var cut = Render<TmEmailTemplateToolbox>();
        Search(cut, "carousel");

        cut.FindAll("[data-tm-block]").Select(e => e.GetAttribute("data-tm-block"))
            .Should().Equal("carousel");
    }

    [Fact]
    public void Search_matches_the_localized_name_without_diacritics_and_in_any_case()
    {
        UseCulture("cs");

        var cut = Render<TmEmailTemplateToolbox>();
        Search(cut, "TLACITKO");

        cut.FindAll("[data-tm-block]").Select(e => e.GetAttribute("data-tm-block"))
            .Should().Contain("button", "the Czech tile reads „Tlačítko“ — a user types without diacritics");
    }

    [Fact]
    public void Search_matches_the_localized_name_with_diacritics()
    {
        UseCulture("cs");

        var cut = Render<TmEmailTemplateToolbox>();
        Search(cut, "tlačítko");

        cut.FindAll("[data-tm-block]").Select(e => e.GetAttribute("data-tm-block"))
            .Should().Contain("button");
    }

    [Fact]
    public void Search_filters_the_layout_presets_by_their_localized_name()
    {
        UseCulture("en");

        var cut = Render<TmEmailTemplateToolbox>();
        Search(cut, "three");

        cut.FindAll("[data-tm-preset]").Select(e => e.GetAttribute("data-tm-preset"))
            .Should().Equal(LayoutPreset.ThreeEqual.ToString());
    }

    [Fact]
    public void Empty_result_shows_the_localized_empty_state_that_names_the_term()
    {
        UseCulture("cs");

        var cut = Render<TmEmailTemplateToolbox>();
        Search(cut, "xyz");

        cut.FindAll("[data-tm-block]").Should().BeEmpty();
        cut.Find("[data-tm-toolbox-empty]").TextContent.Should().Be("Žádný blok neodpovídá „xyz“.");
    }

    [Fact]
    public void Empty_state_disappears_when_the_filter_matches_again()
    {
        UseCulture("en");

        var cut = Render<TmEmailTemplateToolbox>();
        Search(cut, "xyz");
        cut.FindAll("[data-tm-toolbox-empty]").Should().ContainSingle();

        Search(cut, "text");

        cut.FindAll("[data-tm-toolbox-empty]").Should().BeEmpty();
        cut.FindAll("[data-tm-block]").Should().NotBeEmpty();
    }

    [Fact]
    public void Escape_clears_the_search_field()
    {
        UseCulture("en");

        var cut = Render<TmEmailTemplateToolbox>();
        Search(cut, "carousel");
        cut.FindAll("[data-tm-block]").Should().ContainSingle();

        cut.Find("[data-tm-toolbox-search] input").KeyDown("Escape");

        cut.Find("[data-tm-toolbox-search] input").GetAttribute("value").Should().BeNullOrEmpty();
        cut.FindAll("[data-tm-block]").Should().HaveCount(14);
    }

    [Fact]
    public void Groups_still_follow_the_registry_category_when_filtering()
    {
        UseCulture("en");

        var cut = Render<TmEmailTemplateToolbox>();
        Search(cut, "image");

        var groups = cut.FindAll("[data-tm-toolbox-group]").Select(e => e.GetAttribute("data-tm-toolbox-group")).ToList();
        groups.Should().Contain("media", "the image tile belongs to the media category");
        cut.FindAll("[data-tm-toolbox-group]").Should().OnlyHaveUniqueItems(g => g.GetAttribute("data-tm-toolbox-group"));

        var media = cut.Find("[data-tm-toolbox-group=\"media\"]");
        media.QuerySelectorAll("[data-tm-block]").Select(e => e.GetAttribute("data-tm-block"))
            .Should().Contain("image");
    }

    [Fact]
    public void Every_tile_renders_the_icon_above_the_label()
    {
        UseCulture("en");

        var cut = Render<TmEmailTemplateToolbox>();

        var tile = cut.Find("[data-tm-block=\"button\"]");
        var children = tile.Children;
        children.Should().HaveCountGreaterThanOrEqualTo(2);
        children[0].ClassList.Should().Contain("tm-email-toolbox__icon");
        children[^1].ClassList.Should().Contain("tm-email-toolbox__label");
        children[^1].TextContent.Should().Be("Button");
    }

    [Fact]
    public void Every_tile_carries_the_full_name_in_title_so_a_long_name_is_never_lost()
    {
        UseCulture("cs");

        var cut = Render<TmEmailTemplateToolbox>();

        foreach (var tile in cut.FindAll("[data-tm-block]"))
        {
            var label = tile.QuerySelector(".tm-email-toolbox__label")!.TextContent;
            tile.GetAttribute("title").Should().Be(label,
                "a wrapped/ellipsized label must still be discoverable in full");
        }
    }

    [Fact]
    public void Tiles_keep_the_data_tm_block_selector()
    {
        var cut = Render<TmEmailTemplateToolbox>();

        cut.FindAll("[data-tm-block]").Should().HaveCount(14);
        cut.Find("[data-tm-block=\"button\"]").Should().NotBeNull();
    }

    [Fact]
    public void Collapse_toggle_hides_the_list_and_reports_the_new_state()
    {
        bool? reported = null;
        var cut = Render<TmEmailTemplateToolbox>(p => p
            .Add(c => c.CollapsedChanged, (bool value) => reported = value));

        var toggle = cut.Find("[data-tm-toolbox-collapse]");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-tm-toolbox-list]").HasAttribute("hidden").Should().BeFalse();

        toggle.Click();

        reported.Should().BeTrue();
    }

    [Fact]
    public void Collapsed_parameter_hides_the_list_and_offers_the_expand_affordance()
    {
        var cut = Render<TmEmailTemplateToolbox>(p => p.Add(c => c.Collapsed, true));

        cut.Find("[data-tm-toolbox-list]").HasAttribute("hidden").Should().BeTrue();
        var toggle = cut.Find("[data-tm-toolbox-collapse]");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        toggle.GetAttribute("aria-label").Should().Be("Expand the blocks panel");
        cut.Find("[data-tm-toolbox-root]").GetAttribute("data-tm-collapsed").Should().Be("true");
    }

    [Fact]
    public void Expanding_from_the_collapsed_state_reports_false()
    {
        bool? reported = null;
        var cut = Render<TmEmailTemplateToolbox>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.CollapsedChanged, (bool value) => reported = value));

        cut.Find("[data-tm-toolbox-collapse]").Click();

        reported.Should().BeFalse();
    }

    [Fact]
    public void ReadOnly_keeps_the_search_usable_and_the_tiles_inert()
    {
        // Reading a document still needs the catalogue: filtering is not an edit. Inserting a block is,
        // so the tiles stay disabled/aria-disabled and their handlers are guarded.
        var cut = Render<TmEmailTemplateToolbox>(p => p.Add(c => c.ReadOnly, true));

        cut.Find("[data-tm-toolbox-search] input").HasAttribute("disabled").Should().BeFalse(
            "a read-only user must still be able to find a block");

        Search(cut, "carousel");

        cut.FindAll("[data-tm-block]").Select(e => e.GetAttribute("data-tm-block")).Should().Equal("carousel");
        cut.Find("[data-tm-block=\"carousel\"]").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-tm-block=\"carousel\"]").GetAttribute("aria-disabled").Should().Be("true");
    }

    [Fact]
    public void The_header_pairs_the_title_with_the_collapse_toggle()
    {
        var cut = Render<TmEmailTemplateToolbox>();

        var header = cut.Find(".tm-email-toolbox__header");
        header.QuerySelector(".tm-email-toolbox__title")!.TextContent.Trim()
            .Should().Be("Blocks", "the palette names itself (real en table)");
        header.QuerySelector("[data-tm-toolbox-collapse]").Should().NotBeNull(
            "the title sits on the left and the arrow on the right of ONE row");
    }

    [Fact]
    public void A_host_that_owns_the_shell_chrome_can_hide_the_collapse_toggle()
    {
        // F8 lifts the collapse onto TmEditorShell.CollapsedPanels; a duplicate arrow inside the toolbox
        // would then be a second control for the same state.
        var cut = Render<TmEmailTemplateToolbox>(p => p.Add(c => c.ShowCollapseToggle, false));

        cut.FindAll("[data-tm-toolbox-collapse]").Should().BeEmpty();
        cut.Find("[data-tm-toolbox-list]").HasAttribute("hidden").Should().BeFalse();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
