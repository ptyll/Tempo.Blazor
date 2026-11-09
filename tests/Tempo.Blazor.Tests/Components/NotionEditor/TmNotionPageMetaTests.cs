using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 5 (N2): the optional page meta row (<c>TmNotionPageHeader.ShowMeta</c>) — last-edited
/// time (formatted + absolute), author resolved from the mention provider, and the unresolved
/// comment count as a link. Opt-in: hidden by default.
/// </summary>
public sealed class TmNotionPageMetaTests : LocalizationTestBase
{
    private static readonly Guid PageId = Guid.Parse("cf510000-0000-0000-0000-000000000001");

    public TmNotionPageMetaTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmNotionPage_ChangeCover"] = "Change cover",
            ["TmNotionPage_RemoveCover"] = "Remove cover",
            ["TmNotionPageHeader_DragToReposition"] = "Drag to reposition",
            ["TmNotionPageHeader_CoverDialogTitle"] = "Change cover",
            ["TmNotionPageHeader_CoverUrl"] = "Image URL",
            ["TmNotionPageHeader_CoverPresets"] = "Presets",
            ["TmNotionPageHeader_SetCover"] = "Set cover",
            ["Tm_Cancel"] = "Cancel",
            ["TmNotionPage_IconLabel"] = "Page icon",
            ["TmNotionPage_AddIcon"] = "Add icon",
            ["TmNotionPage_AddCover"] = "Add cover",
            ["TmNotionPageHeader_AddDescription"] = "Add description",
            ["TmNotionPage_TitlePlaceholder"] = "Untitled",
            ["TmNotionPageHeader_UnresolvedComments_One"] = "{0} unresolved comment",
            ["TmNotionPageHeader_UnresolvedComments_Other"] = "{0} unresolved comments",
            ["TmNotionPageHeader_DescriptionPlaceholder"] = "Add a description",
            ["TmNotionPageHeader_RemoveDescription"] = "Remove description",
            ["TmNotionPageHeader_EmojiPickerTitle"] = "Pick an icon",
            ["TmNotionPageHeader_EmojiSearch"] = "Search emoji",
            ["TmNotionPageHeader_RemoveIcon"] = "Remove icon",
            ["TmNotionEmojiPicker_RecentlyUsed"] = "Recently used",
            ["Notion_Restrictions_Title"] = "Restricted",
            ["TmNotionPageHeader_MetaLastEdited"] = "Last edited",
            ["TmNotionPageHeader_MetaComments_One"] = "{0} comment",
            ["TmNotionPageHeader_MetaComments_Other"] = "{0} comments",
        });
    }

    [Fact]
    public void MetaRow_RendersFormattedDateAndUser_WhenShowMetaTrue()
    {
        var lastEdited = new DateTime(2026, 11, 2, 10, 24, 0, DateTimeKind.Utc);
        var page = new NotionPage
        {
            Id = PageId,
            Title = "Meta page",
            LastEditedAt = lastEdited,
            LastEditedByUserId = "u-1"
        };

        var cut = RenderHeader(page, showMeta: true);

        var meta = cut.Find("[data-testid='notion-page-meta']");
        // Absolute stamp stays machine-readable; the visible text is the localized formatting.
        var time = meta.QuerySelector("time");
        time.Should().NotBeNull();
        time!.GetAttribute("datetime").Should().Be(lastEdited.ToString("O"));
        time.TextContent.Should().NotBeNullOrWhiteSpace();
        meta.TextContent.Should().Contain("Last edited");
        // Mention provider absent — the raw id is the documented fallback.
        meta.TextContent.Should().Contain("u-1");
    }

    [Fact]
    public void MetaRow_OmitsLastEdited_WhenTimestampUnset()
    {
        var page = new NotionPage
        {
            Id = PageId,
            Title = "Meta page",
            LastEditedAt = default,
            LastEditedByUserId = "u-1"
        };

        var cut = RenderHeader(page, showMeta: true);

        cut.Find("[data-testid='notion-page-meta']").TextContent.Should().NotContain("Last edited");
    }

    [Fact]
    public void ShowMeta_HidesTheTitleCommentBadge()
    {
        var page = new NotionPage
        {
            Id = PageId,
            Title = "Meta page",
            LastEditedAt = DateTime.UtcNow
        };

        var cut = RenderHeader(page, showMeta: true, unresolvedComments: 2);

        cut.FindAll(".tm-notion-header-comment-badge").Should().BeEmpty("the meta row already links the count");
        cut.Find("[data-testid='notion-page-meta-comments']").Should().NotBeNull();
    }

    [Fact]
    public void MetaRow_Hidden_WhenShowMetaFalse()
    {
        var page = new NotionPage
        {
            Id = PageId,
            Title = "Meta page",
            LastEditedAt = DateTime.UtcNow,
            LastEditedByUserId = "u-1"
        };

        var cut = RenderHeader(page, showMeta: false);

        cut.FindAll("[data-testid='notion-page-meta']").Should().BeEmpty();
    }

    [Fact]
    public void MetaRow_CommentCountRendersAsLink_WhenUnresolvedCommentsExist()
    {
        var page = new NotionPage
        {
            Id = PageId,
            Title = "Meta page",
            LastEditedAt = DateTime.UtcNow,
            LastEditedByUserId = "u-1"
        };

        var cut = RenderHeader(page, showMeta: true, unresolvedComments: 3);

        var link = cut.Find("[data-testid='notion-page-meta'] [data-testid='notion-page-meta-comments']");
        link.TagName.Should().Be("BUTTON");
        link.TextContent.Should().Contain("3");
    }

    private IRenderedComponent<TmNotionPageHeader> RenderHeader(NotionPage page, bool showMeta, int unresolvedComments = 0)
    {
        var context = new NotionEditorContext
        {
            DataProvider = default!,
            BlockService = default!
        };

        return Render<TmNotionPageHeader>(parameters => parameters
            .AddCascadingValue(context)
            .Add(p => p.Page, page)
            .Add(p => p.ShowMeta, showMeta)
            .Add(p => p.UnresolvedCommentCount, unresolvedComments));
    }
}
