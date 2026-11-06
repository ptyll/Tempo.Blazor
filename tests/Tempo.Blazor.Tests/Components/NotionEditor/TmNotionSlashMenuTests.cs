using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 6 (N6): the slash menu is a combobox. The search input names the listbox and the active
/// option; ArrowDown moves that option; a mobile layout sheets with "Insert block" and Close.
/// </summary>
public sealed class TmNotionSlashMenuTests : LocalizationTestBase
{
    public TmNotionSlashMenuTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmNotionSlashMenu_Title"] = "Insert block",
            ["TmNotionSlashMenu_SearchPlaceholder"] = "Search blocks",
            ["TmNotionSlashMenu_CategoryBasic"] = "Basic blocks",
            ["TmNotionSlashMenu_NoResults"] = "No results",
            ["Tm_Close"] = "Close",
            ["TmNotionSlashMenu_ItemName_Paragraph"] = "Text",
            ["TmNotionSlashMenu_ItemDesc_Paragraph"] = "Plain text",
            ["TmNotionSlashMenu_ItemName_Heading1"] = "Heading 1",
            ["TmNotionSlashMenu_ItemDesc_Heading1"] = "Big heading"
        });
    }

    [Fact]
    public async Task SearchInput_NamesTheListbox_AndTheActiveOption()
    {
        var cut = RenderMenu();
        cut.WaitForAssertion(() => cut.FindAll(".tm-notion-slash__item").Should().NotBeEmpty());

        var input = cut.Find(".tm-notion-slash__input");
        input.GetAttribute("role").Should().Be("combobox");
        var listId = input.GetAttribute("aria-controls");
        listId.Should().NotBeNullOrEmpty();
        cut.Find(".tm-notion-slash__list").Id.Should().Be(listId);

        var activeId = input.GetAttribute("aria-activedescendant");
        activeId.Should().NotBeNullOrEmpty();
        cut.Find($"#{activeId}").GetAttribute("aria-selected").Should().Be("true");

        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        var moved = input.GetAttribute("aria-activedescendant");
        moved.Should().NotBe(activeId);
        cut.Find($"#{moved}").GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void Mobile_Sheets_WithInsertBlockTitleAndClose()
    {
        var cut = Render<TmNotionSlashMenu>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = NSubstitute.Substitute.For<INotionDataProvider>(),
                BlockService = NSubstitute.Substitute.For<INotionEditorBlockService>()
            }));

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        cut.Markup.Should().Contain("Insert block");
        cut.FindAll("button").Select(b => b.TextContent.Trim())
            .Should().Contain("Close");
    }

    private IRenderedComponent<TmNotionSlashMenu> RenderMenu()
        => Render<TmNotionSlashMenu>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop)
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = NSubstitute.Substitute.For<INotionDataProvider>(),
                BlockService = NSubstitute.Substitute.For<INotionEditorBlockService>()
            }));
}
