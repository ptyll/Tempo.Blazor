using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Tags;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Tags;

file record TestTag(string Id, string Name, string Color) : ITag;

/// <summary>TDD tests for TmTagPicker.</summary>
public class TmTagPickerTests : LocalizationTestBase
{
    private static List<ITag> AllTags =>
    [
        new TestTag("t1", "Bug",     "#ef4444"),
        new TestTag("t2", "Feature", "#3b82f6"),
        new TestTag("t3", "Docs",    "#10b981"),
    ];

    [Fact]
    public void TmTagPicker_Renders_TagPicker()
    {
        var cut = RenderComponent<TmTagPicker>(p => p
            .Add(c => c.AllTags, AllTags)
            .Add(c => c.SelectedTags, new List<ITag>()));

        cut.Find(".tm-tag-picker").Should().NotBeNull();
    }

    [Fact]
    public void TmTagPicker_Shows_Selected_Tags()
    {
        var cut = RenderComponent<TmTagPicker>(p => p
            .Add(c => c.AllTags, AllTags)
            .Add(c => c.SelectedTags, new List<ITag>
            {
                new TestTag("t1", "Bug", "#ef4444")
            }));

        cut.FindAll(".tm-tag-chip").Count.Should().Be(1);
        cut.Find(".tm-tag-chip").TextContent.Should().Contain("Bug");
    }

    [Fact]
    public void TmTagPicker_Click_Opens_Options()
    {
        var cut = RenderComponent<TmTagPicker>(p => p
            .Add(c => c.AllTags, AllTags)
            .Add(c => c.SelectedTags, new List<ITag>()));

        cut.Find(".tm-tag-picker-trigger").Click();

        cut.FindAll(".tm-tag-option").Should().NotBeEmpty();
    }

    [Fact]
    public void TmTagPicker_Click_Option_Fires_OnTagsChanged()
    {
        IEnumerable<ITag>? updated = null;
        var cut = RenderComponent<TmTagPicker>(p => p
            .Add(c => c.AllTags, AllTags)
            .Add(c => c.SelectedTags, new List<ITag>())
            .Add(c => c.OnTagsChanged,
                EventCallback.Factory.Create<IEnumerable<ITag>>(this, t => updated = t)));

        cut.Find(".tm-tag-picker-trigger").Click();
        cut.FindAll(".tm-tag-option").First().Click();

        updated.Should().NotBeNull();
        updated.Should().NotBeEmpty();
    }

    [Fact]
    public void TmTagPicker_Remove_Chip_Fires_OnTagsChanged()
    {
        IEnumerable<ITag>? updated = null;
        var cut = RenderComponent<TmTagPicker>(p => p
            .Add(c => c.AllTags, AllTags)
            .Add(c => c.SelectedTags, new List<ITag>
            {
                new TestTag("t1", "Bug", "#ef4444")
            })
            .Add(c => c.OnTagsChanged,
                EventCallback.Factory.Create<IEnumerable<ITag>>(this, t => updated = t)));

        cut.Find(".tm-tag-chip-remove").Click();

        updated.Should().NotBeNull();
        updated.Should().BeEmpty();
    }

    [Fact]
    public void TmTagPicker_AllowCreate_Shows_Create_Option()
    {
        var cut = RenderComponent<TmTagPicker>(p => p
            .Add(c => c.AllTags, AllTags)
            .Add(c => c.SelectedTags, new List<ITag>())
            .Add(c => c.AllowCreate, true));

        cut.Find(".tm-tag-picker-trigger").Click();
        cut.Find(".tm-tag-picker-search").Input("NewTag");

        cut.FindAll(".tm-tag-create-option").Should().NotBeEmpty();
    }
}
