using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// RED tests – TmFilterableDropdown must use ITmLocalizer for all default text strings.
/// </summary>
public class TmFilterableDropdownLocalizationTests : LocalizationTestBase
{
    private static List<SelectOption<string>> Options =>
    [
        SelectOption<string>.From("a", "Alpha"),
        SelectOption<string>.From("b", "Beta"),
    ];

    [Fact]
    public void TmFilterableDropdown_DefaultPlaceholder_UsesLocalizer()
    {
        UseCzechLocalization();

        // No explicit Placeholder provided
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, Options)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-placeholder").TextContent
            .Should().Be("Vyberte možnost...");
    }

    [Fact]
    public void TmFilterableDropdown_ExplicitPlaceholder_OverridesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, Options)
            .Add(c => c.Placeholder, "Vlastní text")
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-placeholder").TextContent
            .Should().Be("Vlastní text");
    }

    [Fact]
    public void TmFilterableDropdown_FilterPlaceholder_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, Options)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();

        cut.Find(".tm-filterable-dropdown-filter input").GetAttribute("placeholder")
            .Should().Be("Hledat...");
    }

    [Fact]
    public void TmFilterableDropdown_EmptyMessage_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, Options)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        // Filter to produce no results
        cut.Find(".tm-filterable-dropdown-filter input").Input("zzz-no-match");

        cut.Find(".tm-filterable-dropdown-empty").TextContent
            .Should().Be("Žádné výsledky");
    }

    [Fact]
    public void TmFilterableDropdown_DefaultPlaceholder_English_ShowsEnglishText()
    {
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, Options)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-placeholder").TextContent
            .Should().Be("Select an option...");
    }
}
