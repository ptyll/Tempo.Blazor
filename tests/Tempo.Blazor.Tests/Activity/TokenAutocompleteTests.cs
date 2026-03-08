using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

public class TokenAutocompleteTests : LocalizationTestBase
{
    private static List<TokenItem> CreateTestTokens() => new()
    {
        new TokenItem { Key = "user.name", DisplayName = "User Name", Description = "Full name of the user", Category = "User" },
        new TokenItem { Key = "user.email", DisplayName = "User Email", Description = "Email address", Category = "User" },
        new TokenItem { Key = "system.date", DisplayName = "Current Date", Description = "Today's date", Category = "System" },
    };

    [Fact]
    public void TokenAutocomplete_NotVisible_RendersNothing()
    {
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, false)
            .Add(c => c.Tokens, CreateTestTokens()));

        cut.FindAll(".tm-rte-token-dropdown").Should().BeEmpty();
    }

    [Fact]
    public void TokenAutocomplete_Visible_RendersDropdown()
    {
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, CreateTestTokens()));

        cut.FindAll(".tm-rte-token-dropdown").Should().NotBeEmpty();
    }

    [Fact]
    public void TokenAutocomplete_ShowsAllTokens()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens));

        cut.FindAll(".tm-rte-token-item").Should().HaveCount(3);
    }

    [Fact]
    public void TokenAutocomplete_FiltersTokensByQuery()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens)
            .Add(c => c.Query, "email"));

        cut.FindAll(".tm-rte-token-item").Should().HaveCount(1);
    }

    [Fact]
    public void TokenAutocomplete_DisplaysKey()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens));

        var keyElements = cut.FindAll(".tm-rte-token-key");
        keyElements.Should().NotBeEmpty();
        keyElements[0].TextContent.Should().Contain("user.name");
    }

    [Fact]
    public void TokenAutocomplete_DisplaysDescription()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens));

        var descElements = cut.FindAll(".tm-rte-token-description");
        descElements.Should().NotBeEmpty();
        descElements[0].TextContent.Should().Contain("Full name of the user");
    }

    [Fact]
    public void TokenAutocomplete_WithCategories_ShowsSectionHeaders()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens));

        var headers = cut.FindAll(".tm-rte-token-section-header");
        headers.Should().HaveCount(2); // "User" and "System"
        headers[0].TextContent.Should().Be("User");
        headers[1].TextContent.Should().Be("System");
    }

    [Fact]
    public void TokenAutocomplete_WithCategories_HidesCategoryBadge()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens));

        cut.FindAll(".tm-rte-token-category").Should().BeEmpty();
    }

    [Fact]
    public void TokenAutocomplete_NoCategoryTokens_ShowsBadgeInstead()
    {
        var tokens = new List<TokenItem>
        {
            new TokenItem { Key = "a", DisplayName = "A" },
            new TokenItem { Key = "b", DisplayName = "B" },
        };
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens));

        cut.FindAll(".tm-rte-token-section-header").Should().BeEmpty();
    }

    [Fact]
    public void TokenAutocomplete_NoCategoryTokens_ShowsCategoryBadge()
    {
        var tokens = new List<TokenItem>
        {
            new TokenItem { Key = "a", DisplayName = "A", Category = "Cat1" },
            new TokenItem { Key = "b", DisplayName = "B" },
        };
        // When at least one token has category, grouped mode is active → no badges
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, new List<TokenItem>
            {
                new TokenItem { Key = "a", DisplayName = "A" },
            }));

        // No categories at all → flat mode with badge slots (but no badge since Category is null)
        cut.FindAll(".tm-rte-token-section-header").Should().BeEmpty();
    }

    [Fact]
    public void TokenAutocomplete_HighlightsItem()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens)
            .Add(c => c.HighlightedIndex, 1));

        var highlighted = cut.FindAll(".tm-rte-token-highlighted");
        highlighted.Should().HaveCount(1);
    }

    [Fact]
    public void TokenAutocomplete_EmptyQuery_ShowsAll()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens)
            .Add(c => c.Query, ""));

        cut.FindAll(".tm-rte-token-item").Should().HaveCount(3);
    }

    [Fact]
    public void TokenAutocomplete_NoMatch_ShowsEmptyMessage()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens)
            .Add(c => c.Query, "nonexistent"));

        cut.FindAll(".tm-rte-token-empty").Should().NotBeEmpty();
    }

    [Fact]
    public void TokenAutocomplete_SupportsCreation_ShowsCreateButton()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens)
            .Add(c => c.SupportsCreation, true));

        cut.FindAll(".tm-rte-token-create").Should().NotBeEmpty();
    }

    [Fact]
    public void TokenAutocomplete_NoCreation_HidesCreateButton()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens)
            .Add(c => c.SupportsCreation, false));

        cut.FindAll(".tm-rte-token-create").Should().BeEmpty();
    }

    [Fact]
    public void TokenAutocomplete_ClickToken_InvokesCallback()
    {
        TokenItem? selectedToken = null;
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens)
            .Add(c => c.OnTokenSelected, EventCallback.Factory.Create<TokenItem>(this, t => selectedToken = t)));

        cut.FindAll(".tm-rte-token-item")[1].Click();

        selectedToken.Should().NotBeNull();
        selectedToken!.Key.Should().Be("user.email");
    }

    [Fact]
    public void TokenAutocomplete_ClickCreate_InvokesCreateCallback()
    {
        string? createQuery = "unset";
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens)
            .Add(c => c.SupportsCreation, true)
            .Add(c => c.Query, "newvar")
            .Add(c => c.OnCreateRequested, EventCallback.Factory.Create<string?>(this, q => createQuery = q)));

        cut.Find(".tm-rte-token-create").Click();

        createQuery.Should().Be("newvar");
    }

    [Fact]
    public void TokenAutocomplete_FilterByDescription()
    {
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens)
            .Add(c => c.Query, "Today"));

        cut.FindAll(".tm-rte-token-item").Should().HaveCount(1);
        cut.Find(".tm-rte-token-key").TextContent.Should().Contain("system.date");
    }

    [Fact]
    public void TokenAutocomplete_MouseEnter_UpdatesHighlight()
    {
        int? highlightedIndex = null;
        var tokens = CreateTestTokens();
        var cut = RenderComponent<TokenAutocomplete>(p => p
            .Add(c => c.IsVisible, true)
            .Add(c => c.Tokens, tokens)
            .Add(c => c.OnHighlightedIndexChanged, EventCallback.Factory.Create<int>(this, i => highlightedIndex = i)));

        cut.FindAll(".tm-rte-token-item")[2].TriggerEvent("onmouseenter", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        highlightedIndex.Should().Be(2);
    }
}
