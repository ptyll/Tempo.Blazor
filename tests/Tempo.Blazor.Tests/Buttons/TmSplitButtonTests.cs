using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Buttons;

/// <summary>TDD tests for TmSplitButton.</summary>
public class TmSplitButtonTests : LocalizationTestBase
{
    [Fact]
    public void SplitButton_RendersPrimaryText()
    {
        var cut = RenderComponent<TmSplitButton>(p => p
            .Add(x => x.Text, "Save"));

        cut.Find(".tm-split-button__text").TextContent.Should().Contain("Save");
    }

    [Fact]
    public void SplitButton_PrimaryClick_FiresOnClick()
    {
        bool clicked = false;
        var cut = RenderComponent<TmSplitButton>(p => p
            .Add(x => x.Text, "Save")
            .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicked = true)));

        cut.Find(".tm-split-button__primary").Click();

        clicked.Should().BeTrue();
    }

    [Fact]
    public void SplitButton_DropdownToggle_OpensMenu()
    {
        var cut = RenderComponent<TmSplitButton>(p => p
            .Add(x => x.Text, "Save")
            .AddChildContent("<button role='menuitem'>Save as Draft</button>"));

        cut.FindAll("[role='menu']").Should().BeEmpty();

        cut.Find(".tm-split-button__toggle").Click();

        cut.Find("[role='menu']").Should().NotBeNull();
    }

    [Fact]
    public void SplitButton_DropdownToggle_HasAriaHasPopup()
    {
        var cut = RenderComponent<TmSplitButton>(p => p
            .Add(x => x.Text, "Save"));

        cut.Find(".tm-split-button__toggle").GetAttribute("aria-haspopup").Should().Be("true");
    }

    [Fact]
    public void SplitButton_Disabled_DisablesBothButtons()
    {
        var cut = RenderComponent<TmSplitButton>(p => p
            .Add(x => x.Text, "Save")
            .Add(x => x.Disabled, true));

        cut.Find(".tm-split-button__primary").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-split-button__toggle").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void SplitButton_Loading_ShowsSpinner()
    {
        var cut = RenderComponent<TmSplitButton>(p => p
            .Add(x => x.Text, "Saving...")
            .Add(x => x.IsLoading, true));

        cut.FindAll(".tm-split-button__spinner").Should().HaveCount(1);
    }

    [Fact]
    public void SplitButton_Variant_AppliesCss()
    {
        var cut = RenderComponent<TmSplitButton>(p => p
            .Add(x => x.Text, "Save")
            .Add(x => x.Variant, ButtonVariant.Danger));

        cut.Find(".tm-split-button").ClassList.Should().Contain("tm-split-button--danger");
    }

    [Fact]
    public void SplitButton_Size_AppliesCss()
    {
        var cut = RenderComponent<TmSplitButton>(p => p
            .Add(x => x.Text, "Save")
            .Add(x => x.Size, ButtonSize.Sm));

        cut.Find(".tm-split-button").ClassList.Should().Contain("tm-split-button--sm");
    }

    [Fact]
    public void SplitButton_Escape_ClosesDropdown()
    {
        var cut = RenderComponent<TmSplitButton>(p => p
            .Add(x => x.Text, "Save")
            .AddChildContent("<button role='menuitem'>Draft</button>"));

        cut.Find(".tm-split-button__toggle").Click();
        cut.Find("[role='menu']").Should().NotBeNull();

        cut.Find("[role='menu']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void SplitButton_DropdownItems_Render()
    {
        var cut = RenderComponent<TmSplitButton>(p => p
            .Add(x => x.Text, "Save")
            .AddChildContent("<button role='menuitem'>Save as Draft</button><button role='menuitem'>Save & Close</button>"));

        cut.Find(".tm-split-button__toggle").Click();

        var items = cut.FindAll("[role='menuitem']");
        items.Should().HaveCount(2);
    }
}
