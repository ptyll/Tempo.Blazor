using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>TDD tests for TmChip + TmChipGroup.</summary>
public class TmChipTests : LocalizationTestBase
{
    [Fact]
    public void Chip_RendersLabel()
    {
        var cut = RenderComponent<TmChip>(p => p
            .Add(x => x.Label, "Active"));

        cut.Find(".tm-chip").TextContent.Should().Contain("Active");
    }

    [Theory]
    [InlineData(ChipVariant.Soft, "tm-chip--soft")]
    [InlineData(ChipVariant.Filled, "tm-chip--filled")]
    [InlineData(ChipVariant.Outlined, "tm-chip--outlined")]
    public void Chip_Variant_AppliesCss(ChipVariant variant, string expected)
    {
        var cut = RenderComponent<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Variant, variant));

        cut.Find(".tm-chip").ClassList.Should().Contain(expected);
    }

    [Fact]
    public void Chip_CustomColor_AppliesStyle()
    {
        var cut = RenderComponent<TmChip>(p => p
            .Add(x => x.Label, "Custom")
            .Add(x => x.Color, "#ff5722"));

        var chip = cut.Find(".tm-chip");
        chip.GetAttribute("style").Should().Contain("#ff5722");
    }

    [Fact]
    public void Chip_Removable_ShowsRemoveButton()
    {
        var cut = RenderComponent<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Removable, true));

        cut.Find(".tm-chip__remove").Should().NotBeNull();
    }

    [Fact]
    public void Chip_RemoveClick_FiresOnRemove()
    {
        bool removed = false;
        var cut = RenderComponent<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Removable, true)
            .Add(x => x.OnRemove, EventCallback.Factory.Create(this, () => removed = true)));

        cut.Find(".tm-chip__remove").Click();
        removed.Should().BeTrue();
    }

    [Fact]
    public void Chip_Clickable_FiresOnClick()
    {
        bool clicked = false;
        var cut = RenderComponent<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Clickable, true)
            .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicked = true)));

        cut.Find(".tm-chip").Click();
        clicked.Should().BeTrue();
    }

    [Fact]
    public void Chip_Selected_HasSelectedClass()
    {
        var cut = RenderComponent<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Selected, true));

        cut.Find(".tm-chip").ClassList.Should().Contain("tm-chip--selected");
    }

    [Theory]
    [InlineData(ChipSize.Sm, "tm-chip--sm")]
    [InlineData(ChipSize.Md, "tm-chip--md")]
    public void Chip_Size_AppliesCss(ChipSize size, string expected)
    {
        var cut = RenderComponent<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Size, size));

        cut.Find(".tm-chip").ClassList.Should().Contain(expected);
    }

    [Fact]
    public void ChipGroup_RendersChildren()
    {
        var cut = RenderComponent<TmChipGroup>(p => p
            .AddChildContent("<span class='test-child'>Child</span>"));

        cut.Find(".tm-chip-group").InnerHtml.Should().Contain("test-child");
    }

    [Fact]
    public void ChipGroup_CustomClass_IsApplied()
    {
        var cut = RenderComponent<TmChipGroup>(p => p
            .Add(x => x.Class, "my-group")
            .AddChildContent("<span>Child</span>"));

        cut.Find(".tm-chip-group").ClassList.Should().Contain("my-group");
    }
}
