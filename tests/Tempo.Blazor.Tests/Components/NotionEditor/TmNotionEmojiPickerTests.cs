using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 6 (N7, NO4): the one emoji picker. Search filters, Remove shows only with ShowRemove,
/// arrows move a single roving tabindex, Escape closes, and recent emoji render first.
/// </summary>
public sealed class TmNotionEmojiPickerTests : LocalizationTestBase
{
    public TmNotionEmojiPickerTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmNotionEmojiPicker_Title"] = "Choose emoji",
            ["TmNotionEmojiPicker_SearchPlaceholder"] = "Search emoji",
            ["TmNotionEmojiPicker_Remove"] = "Remove icon",
            ["TmNotionEmojiPicker_RecentlyUsed"] = "Recently used",
            ["TmNotionEmojiPicker_NoResults"] = "No emoji found",
            ["TmNotionEmojiPicker_CategorySmileys"] = "Smileys"
        });
    }

    [Fact]
    public void Search_FiltersTheGrid()
    {
        var cut = Render<TmNotionEmojiPicker>(p => p.Add(x => x.Visible, true));
        var before = cut.FindAll(".tm-notion-emoji-picker__emoji").Count;

        cut.Find(".tm-notion-emoji-picker__search").Input("zzz-no-such-emoji");

        cut.FindAll(".tm-notion-emoji-picker__emoji").Should().BeEmpty();
        cut.Markup.Should().Contain("No emoji found");
        before.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Remove_RendersOnlyWhenShowRemove()
    {
        var hidden = Render<TmNotionEmojiPicker>(p => p.Add(x => x.Visible, true));
        hidden.FindAll(".tm-notion-emoji-picker__remove").Should().BeEmpty();

        var shown = Render<TmNotionEmojiPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.ShowRemove, true));
        shown.Find(".tm-notion-emoji-picker__remove").TextContent.Should().Contain("Remove icon");
    }

    [Fact]
    public async Task Remove_RaisesOnRemoved()
    {
        var removed = 0;
        var cut = Render<TmNotionEmojiPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.ShowRemove, true)
            .Add(x => x.OnRemoved, EventCallback.Factory.Create(this, () => removed++)));

        await cut.Find(".tm-notion-emoji-picker__remove").ClickAsync(new MouseEventArgs());

        removed.Should().Be(1);
    }

    [Fact]
    public async Task Arrows_MoveTheSingleRovingTabIndex()
    {
        var cut = Render<TmNotionEmojiPicker>(p => p.Add(x => x.Visible, true));
        var grid = cut.Find(".tm-notion-emoji-picker__grid");
        var before = grid.QuerySelector("[tabindex='0']")!.TextContent;

        await grid.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        var focused = cut.FindAll(".tm-notion-emoji-picker__emoji")
            .Where(b => b.GetAttribute("tabindex") == "0").ToList();
        focused.Should().ContainSingle();
        focused[0].TextContent.Should().NotBe(before);
    }

    [Fact]
    public async Task Escape_Closes()
    {
        var closed = 0;
        var cut = Render<TmNotionEmojiPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnClosed, EventCallback.Factory.Create(this, () => closed++)));

        await cut.Find(".tm-notion-emoji-picker__search")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        closed.Should().Be(1);
    }

    [Fact]
    public void Recent_RenderBeforeTheCategory()
    {
        var cut = Render<TmNotionEmojiPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.RecentEmojis, new[] { "🚀" }));

        cut.Markup.Should().Contain("Recently used");
        cut.Find(".tm-notion-emoji-picker__grid--recent").TextContent.Should().Contain("🚀");
    }
}
