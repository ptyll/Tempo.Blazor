using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Phase 5.5: StretchedLink makes the card title the only focusable activator. A supplied
/// ItemHref renders an anchor; otherwise the title is a button that raises OnItemClick once.
/// The default Button activation is unchanged.
/// </summary>
public class TmMultiViewListStretchedLinkTests : LocalizationTestBase
{
    private record Item(string Id, string Title);

    private static IRenderedComponent<TmMultiViewList<Item>> RenderCards(
        BunitContext context,
        CardActivation activation = CardActivation.Button,
        Func<Item, string?>? href = null,
        Action<Item>? onClick = null)
        => context.Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, new[] { new Item("1", "Ada Lovelace") })
            .Add(c => c.TitleField, (Func<Item, string>)(i => i.Title))
            .Add(c => c.ViewMode, ListViewMode.Card)
            .Add(c => c.CardActivation, activation)
            .Add(c => c.ItemHref, href)
            .Add(c => c.OnItemClick, onClick is null ? default : Microsoft.AspNetCore.Components.EventCallback.Factory.Create<Item>(context, onClick)));

    [Fact]
    public void Button_KeepsTheCardAsTheActivator()
    {
        var cut = RenderCards(this);

        var card = cut.Find(".tm-mvl-card");
        card.GetAttribute("tabindex").Should().Be("0");
        card.GetAttribute("role").Should().Be("button");
        cut.FindAll(".tm-stretched-link").Should().BeEmpty();
    }

    [Fact]
    public void StretchedLink_WithoutHref_RendersATitleButtonAndNoCardTabStop()
    {
        Item? clicked = null;
        var cut = RenderCards(this, CardActivation.StretchedLink, onClick: item => clicked = item);

        var card = cut.Find(".tm-mvl-card");
        card.ClassList.Should().Contain("tm-mvl-card--stretched");
        card.HasAttribute("tabindex").Should().BeFalse();
        card.HasAttribute("role").Should().BeFalse();

        var link = cut.Find("button.tm-stretched-link");
        link.TextContent.Should().Contain("Ada Lovelace");
        link.GetAttribute("type").Should().Be("button");
        cut.FindAll("a.tm-stretched-link").Should().BeEmpty();

        link.Click();
        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Ada Lovelace");
    }

    [Fact]
    public void StretchedLink_WithHref_RendersAnAnchorWithoutAPlaceholderHref()
    {
        var cut = RenderCards(this, CardActivation.StretchedLink, href: item => $"/people/{item.Id}");

        var link = cut.Find("a.tm-stretched-link");
        link.GetAttribute("href").Should().Be("/people/1");
        link.TextContent.Should().Contain("Ada Lovelace");
        cut.Find(".tm-mvl-card").HasAttribute("tabindex").Should().BeFalse();
    }

    [Fact]
    public void StretchedLink_CardHasNoActivationHandlers()
    {
        var cut = RenderCards(this, CardActivation.StretchedLink);

        var card = cut.Find(".tm-mvl-card");
        card.HasAttribute("blazor:onclick").Should().BeFalse();
        card.HasAttribute("blazor:onkeydown").Should().BeFalse();
        card.HasAttribute("blazor:onkeyup").Should().BeFalse();
    }

    [Fact]
    public void StretchedLink_EnterOnTheButtonRaisesOnItemClickOnce()
    {
        var clicks = 0;
        var cut = RenderCards(this, CardActivation.StretchedLink, onClick: _ => clicks++);

        // The browser fires the button's click on Enter keydown. The card no longer listens, so the
        // click is the only activation (bUnit does not synthesize the native click itself).
        var button = cut.Find("button.tm-stretched-link");
        var enterOnButton = () => button.KeyDown("Enter");
        enterOnButton.Should().Throw<MissingEventHandlerException>();
        button.Click();

        clicks.Should().Be(1);
    }

    [Fact]
    public void StretchedLink_SpaceOnTheButtonRaisesOnItemClickOnce()
    {
        var clicks = 0;
        var cut = RenderCards(this, CardActivation.StretchedLink, onClick: _ => clicks++);

        // Space activates a button on keyup. Neither the button nor the card listens for it, so the
        // native click is the only activation.
        var button = cut.Find("button.tm-stretched-link");
        var spaceUp = () => button.KeyUp(" ");
        spaceUp.Should().Throw<MissingEventHandlerException>();
        button.Click();

        clicks.Should().Be(1);
    }

    [Fact]
    public void StretchedLink_EnterOnTheAnchorNeverReachesTheCard()
    {
        var clicks = 0;
        var cut = RenderCards(this, CardActivation.StretchedLink, href: item => $"/people/{item.Id}", onClick: _ => clicks++);

        // Enter on the anchor follows the link natively. Neither the anchor nor the card has a key
        // handler, so the key never raises OnItemClick (bUnit surfaces the missing handler).
        var anchor = cut.Find("a.tm-stretched-link");
        var enterOnAnchor = () => anchor.KeyDown("Enter");
        var enterOnCard = () => cut.Find(".tm-mvl-card").KeyDown("Enter");
        enterOnAnchor.Should().Throw<MissingEventHandlerException>();
        enterOnCard.Should().Throw<MissingEventHandlerException>();
        clicks.Should().Be(0);
    }

    [Fact]
    public void StretchedLink_ClickOnTheHrefLinkRaisesOnItemClickOnce()
    {
        var clicks = 0;
        var cut = RenderCards(this, CardActivation.StretchedLink, href: item => $"/people/{item.Id}", onClick: _ => clicks++);

        cut.Find("a.tm-stretched-link").Click();

        clicks.Should().Be(1);
    }
}
