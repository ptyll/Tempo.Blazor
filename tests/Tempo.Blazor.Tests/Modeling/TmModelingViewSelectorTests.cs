using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingViewSelectorTests : LocalizationTestBase
{
    public TmModelingViewSelectorTests()
    {
        Services.AddSingleton<IModelingNotationProfile>(new TestNotationProfile(
            "bpmn",
            "BPMN 2.0",
            ["default", "process"]));
        Services.AddSingleton<IModelingNotationProfile>(new TestNotationProfile(
            "archimate",
            "ArchiMate 3",
            ["overview", "application"]));
        Services.AddSingleton<IModelingNotationProfile>(new TestNotationProfile(
            "custom-empty",
            "Custom empty",
            []));
    }

    [Fact]
    public void Changing_notation_updates_available_viewpoints()
    {
        using var cut = Render<TmModelingViewSelector>(parameters => parameters
            .Add(p => p.NotationKey, "bpmn"));

        cut.Find("[data-testid='modeling-notation-select']").Change("archimate");

        var options = cut.Find("[data-testid='modeling-viewpoint-select']").QuerySelectorAll("option");
        options.Select(option => option.GetAttribute("value")).Should().Contain(["overview", "application"]);
        cut.Markup.Should().Contain("Application usage");
    }

    [Fact]
    public void Unknown_notation_shows_empty_viewpoint_state_without_exception()
    {
        using var cut = Render<TmModelingViewSelector>(parameters => parameters
            .Add(p => p.NotationKey, "unknown"));

        cut.Find("[data-testid='modeling-view-selector']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-viewpoint-empty'] select").HasAttribute("disabled")
            .Should().BeTrue("the Viewpoint select is disabled while no notation is chosen");
        cut.Find("[data-testid='modeling-viewpoint-empty']").TextContent.Should().Contain("No viewpoints");
    }

    [Fact]
    public void Notation_without_viewpoints_disables_viewpoint_choice_with_message()
    {
        using var cut = Render<TmModelingViewSelector>(parameters => parameters
            .Add(p => p.NotationKey, "custom-empty"));

        var empty = cut.Find("[data-testid='modeling-viewpoint-empty']");
        empty.QuerySelector("select")!.HasAttribute("disabled").Should().BeTrue();
        empty.TextContent.Should().Contain("No viewpoints");
        empty.QuerySelector(".tm-modeling-view-selector__hint").Should().NotBeNull(
            "the hint is a plain span below the disabled select");
    }

    [Fact]
    public void Disabled_viewpoint_select_is_described_by_the_per_instance_hint()
    {
        using var cut = Render<TmModelingViewSelector>(parameters => parameters
            .Add(p => p.NotationKey, (string?)null));

        var select = cut.Find("[data-testid='modeling-viewpoint-empty'] select");
        var describedBy = select.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrEmpty("the disabled select points at the hint for assistive tech");
        cut.Find($"[data-testid='modeling-viewpoint-empty'] #{describedBy}")
            .Should().NotBeNull("the referenced hint element exists");
        describedBy.Should().Contain("viewpoint-hint");
        select.GetAttribute("aria-label").Should().Be("Viewpoint",
            "the visible span is not a wrapping label — the select carries its accessible name on aria-label (axe select-name)");
    }

    [Fact]
    public void Selects_have_visible_labels_above_field()
    {
        using var cut = Render<TmModelingViewSelector>(parameters => parameters
            .Add(p => p.NotationKey, "bpmn"));

        var field = cut.Find("label.tm-modeling-view-selector__field");

        // MO3: the visible label is the FIRST child inside the field — it sits ABOVE the select
        // (the old markup kept the label text but the phase contract locks the stacking).
        field.Children.Should().HaveCountGreaterThan(1);
        field.Children[0].TagName.Should().Be("SPAN", "the label text renders above the field");
        field.Children[0].TextContent.Should().Contain("Notation");
        field.Children.Last().TagName.Should().Be("SELECT");

        var viewpointField = cut.FindAll("label.tm-modeling-view-selector__field").ElementAt(1);
        viewpointField.Children[0].TextContent.Should().Contain("Viewpoint");
        viewpointField.Children.Last().TagName.Should().Be("SELECT");
    }

    [Fact]
    public void Select_field_height_comes_from_the_input_height_token()
    {
        var css = File.ReadAllText(FindViewSelectorCssPath());

        css.Should().Contain("var(--tm-input-height-md)",
            "the select height matches the new provider select (unified header fields, MO3)");
        css.Should().NotContain("#", "no hardcoded colors — tokens only");
    }

    private static string FindViewSelectorCssPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent!;
        }

        directory.Should().NotBeNull("repo root with TempoBlazor.slnx must be reachable from the test output");
        return Path.Combine(
            directory!.FullName,
            "src", "Tempo.Blazor.Modeling", "Components", "Modeling", "TmModelingViewSelector.razor.css");
    }

    [Fact]
    public void Selecting_viewpoint_emits_selected_value()
    {
        string? selected = null;
        using var cut = Render<TmModelingViewSelector>(parameters => parameters
            .Add(p => p.NotationKey, "archimate")
            .Add(p => p.OnViewpointChanged, EventCallback.Factory.Create<string?>(this, value => selected = value)));

        cut.Find("[data-testid='modeling-viewpoint-select']").Change("application");

        selected.Should().Be("application");
    }

    [Fact]
    public void Null_notation_shows_select_notation_prompt()
    {
        using var cut = Render<TmModelingViewSelector>(parameters => parameters
            .Add(p => p.NotationKey, (string?)null));

        cut.Find("[data-testid='modeling-notation-select']").TextContent.Should().Contain("Select notation");
        cut.Find("[data-testid='modeling-viewpoint-empty']").TextContent.Should().Contain("Select a notation");
        cut.Find("[data-testid='modeling-viewpoint-empty'] select").HasAttribute("disabled")
            .Should().BeTrue("the Viewpoint select renders disabled while no notation is chosen");
    }

    private sealed class TestNotationProfile : IModelingNotationProfile
    {
        public TestNotationProfile(string notationKey, string displayName, IReadOnlyCollection<string> viewpoints)
        {
            NotationKey = notationKey;
            DisplayName = displayName;
            SupportedViewpointKeys = viewpoints;
        }

        public string NotationKey { get; }

        public string DisplayName { get; }

        public IReadOnlyCollection<string> SupportedElementTypes { get; } = [];

        public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } = [];

        public IReadOnlyCollection<string> SupportedViewpointKeys { get; }
    }
}
