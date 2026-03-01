using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Tags;
using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// RED tests – TmTagPicker must use ITmLocalizer for search placeholder and create option text.
/// </summary>
public class TmTagPickerLocalizationTests : LocalizationTestBase
{
    private static List<ITag> AllTags =>
    [
        new TestTag("t1", "Bug",     "#ef4444"),
        new TestTag("t2", "Feature", "#3b82f6"),
    ];

    [Fact]
    public void TmTagPicker_SearchPlaceholder_UsesLocalizer()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmTagPicker>(p => p
            .Add(c => c.AllTags, AllTags)
            .Add(c => c.SelectedTags, new List<ITag>())
            .Add(c => c.AllowCreate, true));

        cut.Find(".tm-tag-picker-trigger").Click();

        cut.Find(".tm-tag-picker-search").GetAttribute("placeholder")
            .Should().Be("Hledat štítky...");
    }

    [Fact]
    public void TmTagPicker_SearchPlaceholder_English_ShowsEnglishText()
    {
        var cut = RenderComponent<TmTagPicker>(p => p
            .Add(c => c.AllTags, AllTags)
            .Add(c => c.SelectedTags, new List<ITag>())
            .Add(c => c.AllowCreate, true));

        cut.Find(".tm-tag-picker-trigger").Click();

        cut.Find(".tm-tag-picker-search").GetAttribute("placeholder")
            .Should().Be("Search tags...");
    }

    [Fact]
    public void TmTagPicker_CreateOption_UsesLocalizer_WithSearchText()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmTagPicker>(p => p
            .Add(c => c.AllTags, AllTags)
            .Add(c => c.SelectedTags, new List<ITag>())
            .Add(c => c.AllowCreate, true));

        cut.Find(".tm-tag-picker-trigger").Click();
        cut.Find(".tm-tag-picker-search").Input("NovaZnacka");

        // Must render Czech "Vytvořit" with the search text substituted
        cut.Find(".tm-tag-create-option").TextContent
            .Should().Contain("Vytvořit").And.Contain("NovaZnacka");
    }

    [Fact]
    public void TmTagPicker_CreateOption_English_ShowsEnglishText()
    {
        var cut = RenderComponent<TmTagPicker>(p => p
            .Add(c => c.AllTags, AllTags)
            .Add(c => c.SelectedTags, new List<ITag>())
            .Add(c => c.AllowCreate, true));

        cut.Find(".tm-tag-picker-trigger").Click();
        cut.Find(".tm-tag-picker-search").Input("NewTag");

        cut.Find(".tm-tag-create-option").TextContent
            .Should().Contain("Create").And.Contain("NewTag");
    }

    private record TestTag(string Id, string Name, string Color) : ITag;
}
