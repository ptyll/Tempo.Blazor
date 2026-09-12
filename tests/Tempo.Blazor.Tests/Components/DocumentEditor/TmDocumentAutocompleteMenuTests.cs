using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentAutocompleteMenuTests : LocalizationTestBase
{
    [Fact]
    public void Menu_WhenVisible_RendersItemsAndHighlightedState()
    {
        var cut = Render<TmDocumentAutocompleteMenu>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Items, Items())
            .Add(p => p.HighlightedIndex, 1));

        cut.Find("[data-testid='document-autocomplete-menu']").Should().NotBeNull();
        var items = cut.FindAll("[data-testid='document-autocomplete-item']");
        items.Should().HaveCount(2);
        items[1].GetAttribute("aria-selected").Should().Be("true");
        cut.Markup.Should().Contain("Client name");
        cut.Markup.Should().Contain("Alex Johnson");
    }

    [Fact]
    public void Menu_WhenLoading_RendersLoadingState()
    {
        var cut = Render<TmDocumentAutocompleteMenu>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.IsLoading, true)
            .Add(p => p.LoadingText, "Loading feed"));

        cut.Find("[data-testid='document-autocomplete-loading']").TextContent.Should().Contain("Loading feed");
    }

    [Fact]
    public void Menu_WhenEmpty_RendersEmptyState()
    {
        var cut = Render<TmDocumentAutocompleteMenu>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.EmptyText, "Nothing here"));

        cut.Find("[data-testid='document-autocomplete-empty']").TextContent.Should().Contain("Nothing here");
    }

    [Fact]
    public void Menu_WhenError_RendersErrorState()
    {
        var cut = Render<TmDocumentAutocompleteMenu>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.ErrorMessage, "Provider failed"));

        cut.Find("[data-testid='document-autocomplete-error']").TextContent.Should().Contain("Provider failed");
    }

    [Fact]
    public void Menu_WithCustomTemplate_RendersTemplate()
    {
        var cut = Render<TmDocumentAutocompleteMenu>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Items, Items())
            .Add(p => p.ItemTemplate, item => builder =>
            {
                builder.OpenElement(0, "strong");
                builder.AddContent(1, $"custom:{item.Id}");
                builder.CloseElement();
            }));

        cut.Markup.Should().Contain("custom:client.name");
    }

    [Fact]
    public void Menu_KeyboardNavigation_ChangesHighlightAndSelectsItem()
    {
        var highlighted = 0;
        DocumentAutocompleteItem? selected = null;
        var cut = Render<TmDocumentAutocompleteMenu>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Items, Items())
            .Add(p => p.HighlightedIndex, highlighted)
            .Add(p => p.OnHighlightedIndexChanged, EventCallback.Factory.Create<int>(this, value => highlighted = value))
            .Add(p => p.OnItemSelected, EventCallback.Factory.Create<DocumentAutocompleteItem>(this, item => selected = item)));

        // Arrow keys bubble from a focused item button to the menu container.
        var items = cut.FindAll("[data-testid='document-autocomplete-item']");
        items[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        highlighted.Should().Be(1);

        cut.Render(parameters => parameters.Add(p => p.HighlightedIndex, highlighted));

        // Enter on a focused item: the native button turns it into a click —
        // keydown -> click selects exactly once.
        items = cut.FindAll("[data-testid='document-autocomplete-item']");
        items[1].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll("[data-testid='document-autocomplete-item']")[1].Click();

        selected.Should().NotBeNull();
        selected!.Id.Should().Be("alex");
    }

    [Fact]
    public void Menu_ItemEnterKeySequence_SelectsClickedItemExactlyOnce()
    {
        // Regression: an Enter keydown on an item button bubbled to the
        // container handler, which selected the *highlighted* item — and then
        // the native click selected the actually-focused item. One key press
        // fired OnItemSelected twice, possibly with two different items.
        var selections = new List<DocumentAutocompleteItem>();
        var cut = Render<TmDocumentAutocompleteMenu>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Items, Items())
            .Add(p => p.HighlightedIndex, 0)
            .Add(p => p.OnItemSelected, EventCallback.Factory.Create<DocumentAutocompleteItem>(this, item => selections.Add(item))));

        var items = cut.FindAll("[data-testid='document-autocomplete-item']");
        items[1].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll("[data-testid='document-autocomplete-item']")[1].Click();

        selections.Select(s => s.Id).Should().Equal("alex");
    }

    private static IReadOnlyList<DocumentAutocompleteItem> Items() =>
    [
        new DocumentAutocompleteItem
        {
            Id = "client.name",
            Label = "Client name",
            Description = "Client display name",
            Kind = DocumentAutocompleteKind.Token,
            Group = "Client"
        },
        new DocumentAutocompleteItem
        {
            Id = "alex",
            Label = "Alex Johnson",
            Description = "alex",
            Kind = DocumentAutocompleteKind.Mention
        }
    ];
}
