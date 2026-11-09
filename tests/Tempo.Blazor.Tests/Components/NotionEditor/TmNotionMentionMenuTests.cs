using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.Tests.Localization;
using NSubstitute;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 6 (N6): a person mention renders a two-initial avatar, and a mobile layout sheets the
/// menu as "Mention" with a Close action.
/// </summary>
public sealed class TmNotionMentionMenuTests : LocalizationTestBase
{
    public TmNotionMentionMenuTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmNotionMentionMenu_Title"] = "Mention",
            ["TmNotionMentionMenu_SearchPlaceholder"] = "Search people",
            ["TmNotionMentionMenu_TabPeople"] = "People",
            ["TmNotionMentionMenu_TabPages"] = "Pages",
            ["TmNotionMentionMenu_TabDate"] = "Date",
            ["TmNotionMentionMenu_NoResults"] = "No results",
            ["Tm_Close"] = "Close"
        });
    }

    [Fact]
    public void Person_RendersTwoInitialAvatar()
    {
        var people = Substitute.For<ITmPeopleProvider>();
        people.SearchAsync(Arg.Any<TmPeopleQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new TmUser { Id = "1", DisplayName = "Ada Lovelace" } });

        var cut = Render<TmNotionMentionMenu>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop)
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = Substitute.For<INotionEditorBlockService>(),
                MentionProvider = people
            }));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-avatar-fallback").TextContent.Trim().Should().Be("AL"));
    }

    [Fact]
    public void Mobile_Sheets_WithMentionTitleAndClose()
    {
        var cut = Render<TmNotionMentionMenu>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = Substitute.For<INotionEditorBlockService>()
            }));

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        cut.Markup.Should().Contain("Mention");
        cut.FindAll("button").Select(b => b.TextContent.Trim()).Should().Contain("Close");
    }
}
