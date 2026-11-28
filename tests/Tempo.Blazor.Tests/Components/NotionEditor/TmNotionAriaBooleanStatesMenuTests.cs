using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Plan b75365fb Fáze 4j — the boolean ARIA states of the NotionEditor MENUS and SEARCH surfaces
/// render as STRINGS in both positions: <c>aria-selected</c> carries <c>"true"</c> or
/// <c>"false"</c> — never the invalid empty value a Blazor bool produces, never an omitted
/// attribute. The touched surfaces are the mention menu (People/Pages/Date tabs + the option
/// lists of every tab), the comment mention input dropdown, the emoji picker category tabs, the
/// token dropdown, the block type switcher and the page search results (pages + blocks). Every
/// listbox keeps the APG combobox pattern: the selected option announces <c>"true"</c>, every
/// inactive option keeps the attribute with <c>"false"</c> (a combobox with
/// <c>aria-activedescendant</c> announces the state on the options). These are the bUnit halves
/// of the frozen-list entries removed from <c>AriaBooleanStateAttributeGuardTests</c> in this
/// phase.
/// </summary>
public sealed class TmNotionAriaBooleanStatesMenuTests : LocalizationTestBase
{
    public TmNotionAriaBooleanStatesMenuTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmNotionMentionMenu_Title"] = "Mention",
            ["TmNotionMentionMenu_SearchPlaceholder"] = "Search people",
            ["TmNotionMentionMenu_PagesSearchPlaceholder"] = "Search pages",
            ["TmNotionMentionMenu_TabPeople"] = "People",
            ["TmNotionMentionMenu_TabPages"] = "Pages",
            ["TmNotionMentionMenu_TabDate"] = "Date",
            ["TmNotionMentionMenu_NoResults"] = "No results",
            ["TmNotionMentionMenu_Loading"] = "Loading",
            ["TmNotionMentionMenu_Untitled"] = "Untitled",
            ["TmNotionMentionMenu_DateToday"] = "Today",
            ["TmNotionMentionMenu_DateTomorrow"] = "Tomorrow",
            ["TmNotionMentionMenu_DateYesterday"] = "Yesterday",
            ["TmNotionEmojiPicker_Title"] = "Choose emoji",
            ["TmNotionEmojiPicker_SearchPlaceholder"] = "Search emoji",
            ["TmNotionEmojiPicker_RecentlyUsed"] = "Recently used",
            ["TmNotionEmojiPicker_NoResults"] = "No emoji found",
            ["TmNotionTokenDropdown_Title"] = "Insert token",
            ["TmNotionTokenDropdown_Label"] = "Insert token",
            ["TmNotionTokenDropdown_SearchPlaceholder"] = "Search tokens",
            ["TmNotionTokenDropdown_Loading"] = "Loading",
            ["TmNotionTokenDropdown_NoResults"] = "No tokens",
            ["TmNotionPageSearch_DialogLabel"] = "Search pages",
            ["TmNotionPageSearch_Placeholder"] = "Type to search",
            ["TmNotionPageSearch_ResultsLabel"] = "Results",
            ["TmNotionPageSearch_StartTyping"] = "Start typing",
            ["TmNotionPageSearch_NoResults"] = "No results",
            ["TmNotionPageSearch_Loading"] = "Loading",
            ["TmNotionPageSearch_SectionPages"] = "Pages",
            ["TmNotionPageSearch_SectionBlocks"] = "Blocks",
            ["Tm_Close"] = "Close"
        });
    }

    // ── TmNotionMentionMenu — tabs ─────────────────────────────────────────────────────────

    [Fact]
    public void MentionMenu_Tabs_AnnounceSelectedAsString_InBothStates()
    {
        var cut = RenderMentionMenu(visible: true);

        var tabs = cut.FindAll(".tm-nmm__tab");
        tabs.Should().HaveCount(3, "People, Pages and Date");
        tabs[0].GetAttribute("aria-selected").Should().Be("true",
            "the active tab announces the string \"true\"");
        tabs[1].GetAttribute("aria-selected").Should().Be("false",
            "an inactive tab keeps the attribute — the string \"false\"");
        tabs[2].GetAttribute("aria-selected").Should().Be("false");

        cut.FindAll(".tm-nmm__tab")[1].Click(); // Pages
        cut.FindAll(".tm-nmm__tab")[0].GetAttribute("aria-selected").Should().Be("false");
        cut.FindAll(".tm-nmm__tab")[1].GetAttribute("aria-selected").Should().Be("true");
        cut.FindAll(".tm-nmm__tab")[2].GetAttribute("aria-selected").Should().Be("false");
    }

    // ── TmNotionMentionMenu — option lists ─────────────────────────────────────────────────

    [Fact]
    public void MentionMenu_PeopleOptions_ArrowDown_MovesTheStringSelectedState()
    {
        var cut = RenderMentionMenu(visible: true);
        cut.WaitForAssertion(() => cut.FindAll(".tm-nmm__item").Should().HaveCount(2));

        var options = cut.FindAll(".tm-nmm__item");
        options[0].GetAttribute("aria-selected").Should().Be("true",
            "the first option starts selected — the string \"true\"");
        options[1].GetAttribute("aria-selected").Should().Be("false",
            "an inactive option keeps the attribute — the string \"false\"");

        cut.Find(".tm-nmm__search-input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var moved = cut.FindAll(".tm-nmm__item");
        moved[0].GetAttribute("aria-selected").Should().Be("false");
        moved[1].GetAttribute("aria-selected").Should().Be("true",
            "ArrowDown moves the string \"true\" to exactly one option");
        moved.Count(o => o.GetAttribute("aria-selected") == "true").Should().Be(1);
    }

    [Fact]
    public void MentionMenu_DateOptions_ArrowDown_MovesTheStringSelectedState()
    {
        var cut = RenderMentionMenu(visible: true);
        cut.WaitForAssertion(() => cut.FindAll(".tm-nmm__tab").Should().HaveCount(3));

        cut.FindAll(".tm-nmm__tab")[2].Click(); // Date
        var options = cut.FindAll(".tm-nmm__item");
        options.Should().HaveCount(3, "Today, Tomorrow and Yesterday");
        options[0].GetAttribute("aria-selected").Should().Be("true");
        options[1].GetAttribute("aria-selected").Should().Be("false");
        options[2].GetAttribute("aria-selected").Should().Be("false");

        cut.Find(".tm-nmm__search-input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.FindAll(".tm-nmm__item")[1].GetAttribute("aria-selected").Should().Be("true");
        cut.FindAll(".tm-nmm__item").Count(o => o.GetAttribute("aria-selected") == "true")
            .Should().Be(1);
    }

    // ── TmCommentMentionInput ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CommentMentionInput_Options_ArrowDown_MovesTheStringSelectedState()
    {
        var people = Substitute.For<ITmPeopleProvider>();
        people.SearchAsync(Arg.Any<TmPeopleQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new TmUser { Id = "1", DisplayName = "Ada Lovelace" },
                new TmUser { Id = "2", DisplayName = "Alan Turing" }
            });

        var cut = Render<TmCommentMentionInput>(p => p
            .Add(x => x.MentionProvider, people)
            .Add(x => x.AriaLabel, "Comment"));

        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "@a" });
        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-comment-mention-dropdown__item").Should().HaveCount(2));

        var options = cut.FindAll(".tm-comment-mention-dropdown__item");
        options[0].GetAttribute("aria-selected").Should().Be("true");
        options[1].GetAttribute("aria-selected").Should().Be("false",
            "an inactive option keeps the attribute — the string \"false\"");

        await cut.Find("textarea").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        var moved = cut.FindAll(".tm-comment-mention-dropdown__item");
        moved[0].GetAttribute("aria-selected").Should().Be("false");
        moved[1].GetAttribute("aria-selected").Should().Be("true");
        moved.Count(o => o.GetAttribute("aria-selected") == "true").Should().Be(1);
    }

    // ── TmNotionEmojiPicker — category tabs ────────────────────────────────────────────────

    [Fact]
    public void EmojiPicker_CategoryTabs_AnnounceSelectedAsString_InBothStates()
    {
        var cut = Render<TmNotionEmojiPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.RecentEmojis, Array.Empty<string>()));

        var tabs = cut.FindAll(".tm-notion-emoji-picker__tab");
        tabs.Should().HaveCount(9);
        tabs[0].GetAttribute("aria-selected").Should().Be("true",
            "the Smileys category starts active — the string \"true\"");
        tabs.Skip(1).Should().OnlyContain(tab => tab.GetAttribute("aria-selected") == "false",
            "every inactive category keeps the attribute — the string \"false\"");

        cut.FindAll(".tm-notion-emoji-picker__tab")[2].Click(); // Animals

        var flipped = cut.FindAll(".tm-notion-emoji-picker__tab");
        flipped[0].GetAttribute("aria-selected").Should().Be("false");
        flipped[2].GetAttribute("aria-selected").Should().Be("true");
        flipped.Count(tab => tab.GetAttribute("aria-selected") == "true").Should().Be(1);
    }

    // ── TmNotionTokenDropdown ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TokenDropdown_Options_ArrowDown_MovesTheStringSelectedState()
    {
        var provider = Substitute.For<ITokenDataProvider>();
        provider.SearchTokensAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IToken>>(new IToken[]
            {
                new TestToken { Key = "user.email", DisplayName = "User Email" },
                new TestToken { Key = "company.name", DisplayName = "Company Name" }
            }));
        var context = new NotionEditorContext
        {
            DataProvider = Substitute.For<INotionDataProvider>(),
            BlockService = Substitute.For<INotionEditorBlockService>(),
            TokenProvider = provider
        };

        var cut = Render<TmNotionTokenDropdown>(p => p
            .Add(x => x.Visible, true)
            .AddCascadingValue(context));

        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-notion-token-dropdown__item").Should().HaveCount(2));

        var options = cut.FindAll(".tm-notion-token-dropdown__item");
        options[0].GetAttribute("aria-selected").Should().Be("true");
        options[1].GetAttribute("aria-selected").Should().Be("false",
            "an inactive option keeps the attribute — the string \"false\"");

        await cut.Find(".tm-notion-token-dropdown__input")
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        var moved = cut.FindAll(".tm-notion-token-dropdown__item");
        moved[0].GetAttribute("aria-selected").Should().Be("false");
        moved[1].GetAttribute("aria-selected").Should().Be("true");
        moved.Count(o => o.GetAttribute("aria-selected") == "true").Should().Be(1);
    }

    // ── TmNotionBlockTypeSwitcher ──────────────────────────────────────────────────────────

    [Fact]
    public void BlockTypeSwitcher_Options_AnnounceSelectedAsString_ForCurrentAndOthers()
    {
        var cut = Render<TmNotionBlockTypeSwitcher>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.CurrentType, BlockType.Paragraph));

        var options = cut.FindAll("[data-type-idx]");
        options.Should().NotBeEmpty();
        options.Count(o => o.GetAttribute("aria-selected") == "true").Should().Be(1,
            "exactly the current type announces the string \"true\"");
        options.Count(o => o.GetAttribute("aria-selected") == "false").Should().Be(options.Count - 1,
            "every other type keeps the attribute — the string \"false\"");
        cut.Find(".tm-notion-type-switcher__item--current")
            .GetAttribute("aria-selected").Should().Be("true");
    }

    // ── TmNotionPageSearch ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PageSearch_PageAndBlockOptions_ArrowDown_MovesTheStringSelectedState()
    {
        var context = new NotionEditorContext { SearchProvider = new TwoResultSearchProvider() };
        var host = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<TmNotionPageSearch>());
        var search = host.FindComponent<TmNotionPageSearch>();

        await search.InvokeAsync(() => search.Instance.OpenPageSearch());
        await host.Find(".tm-nps__search-input").InputAsync(new ChangeEventArgs { Value = "page" });
        host.WaitForAssertion(() => host.FindAll(".tm-nps__item").Should().HaveCount(2));

        var options = host.FindAll(".tm-nps__item");
        options[0].GetAttribute("aria-selected").Should().Be("true",
            "the first page result starts selected — the string \"true\"");
        options[1].GetAttribute("aria-selected").Should().Be("false",
            "the block result keeps the attribute — the string \"false\"");

        // ArrowDown crosses the section boundary from the page result to the block result.
        await host.Find(".tm-nps__search-input").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        var moved = host.FindAll(".tm-nps__item");
        moved[0].GetAttribute("aria-selected").Should().Be("false");
        moved[1].GetAttribute("aria-selected").Should().Be("true",
            "ArrowDown moves the string \"true\" across the section boundary");
        moved.Count(o => o.GetAttribute("aria-selected") == "true").Should().Be(1);
    }

    // ── Render helpers & stubs ─────────────────────────────────────────────────────────────

    private IRenderedComponent<TmNotionMentionMenu> RenderMentionMenu(bool visible)
    {
        var people = Substitute.For<ITmPeopleProvider>();
        people.SearchAsync(Arg.Any<TmPeopleQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new TmUser { Id = "1", DisplayName = "Ada Lovelace" },
                new TmUser { Id = "2", DisplayName = "Alan Turing" }
            });

        return Render<TmNotionMentionMenu>(p => p
            .Add(x => x.Visible, visible)
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = Substitute.For<INotionEditorBlockService>(),
                MentionProvider = people
            }));
    }

    private sealed class TestToken : IToken
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description => null;
        public string? Category => null;
        public string? Icon => null;
        public string? TypeLabel => null;
        public string? ColorClass { get; set; }
    }

    private sealed class TwoResultSearchProvider : INotionSearchProvider
    {
        public Task<IEnumerable<INotionPage>> SearchPagesAsync(string query, NotionSearchFilter? filter)
            => Task.FromResult<IEnumerable<INotionPage>>([Page()]);

        public Task<IEnumerable<NotionSearchResult>> SearchBlocksAsync(string query, NotionSearchFilter? filter)
            => Task.FromResult<IEnumerable<NotionSearchResult>>([Block()]);

        public Task<(IEnumerable<INotionPage> Pages, IEnumerable<NotionSearchResult> Blocks)> SearchAllAsync(
            string query, NotionSearchFilter? filter, int maxResults)
            => Task.FromResult<(IEnumerable<INotionPage>, IEnumerable<NotionSearchResult>)>(([Page()], [Block()]));

        private static INotionPage Page() => new NotionPage
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Title = "Launch page",
            LastEditedAt = new DateTime(2026, 3, 1)
        };

        private static NotionSearchResult Block() => new()
        {
            PageId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            PageTitle = "Roadmap",
            BlockId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            BlockType = BlockType.Paragraph,
            MatchSnippet = "A matching paragraph"
        };
    }
}
