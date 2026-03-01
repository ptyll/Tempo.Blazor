using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// RED tests – TmSearchInput must use ITmLocalizer for aria-label and default placeholder.
/// </summary>
public class TmSearchInputLocalizationTests : LocalizationTestBase
{
    [Fact]
    public void TmSearchInput_AriaLabel_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmSearchInput>(p => p
            .Add(c => c.Value, "test")); // Value needed to render the clear button

        cut.Find(".tm-search-clear").GetAttribute("aria-label")
            .Should().Be("Vymazat hledání");
    }

    [Fact]
    public void TmSearchInput_DefaultPlaceholder_UsesLocalizer()
    {
        UseCzechLocalization();

        // No explicit Placeholder – must fallback to localizer
        var cut = RenderComponent<TmSearchInput>();

        cut.Find("input").GetAttribute("placeholder")
            .Should().Be("Hledat...");
    }

    [Fact]
    public void TmSearchInput_ExplicitPlaceholder_OverridesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmSearchInput>(p => p
            .Add(c => c.Placeholder, "Vlastní placeholder"));

        cut.Find("input").GetAttribute("placeholder")
            .Should().Be("Vlastní placeholder");
    }

    [Fact]
    public void TmSearchInput_DefaultPlaceholder_English_ShowsEnglishText()
    {
        var cut = RenderComponent<TmSearchInput>();

        cut.Find("input").GetAttribute("placeholder")
            .Should().Be("Search...");
    }
}
