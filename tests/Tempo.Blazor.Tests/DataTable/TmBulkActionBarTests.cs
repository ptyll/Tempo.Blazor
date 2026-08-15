using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>TDD tests for TmBulkActionBar.</summary>
public class TmBulkActionBarTests : LocalizationTestBase
{
    [Fact]
    public void BulkActionBar_ZeroCount_IsHidden()
    {
        var cut = Render<TmBulkActionBar>(p => p
            .Add(x => x.SelectedCount, 0));

        cut.FindAll(".tm-bulk-action-bar").Should().BeEmpty();
    }

    [Fact]
    public void BulkActionBar_PositiveCount_IsVisible()
    {
        var cut = Render<TmBulkActionBar>(p => p
            .Add(x => x.SelectedCount, 3));

        cut.Find(".tm-bulk-action-bar").Should().NotBeNull();
    }

    [Fact]
    public void BulkActionBar_ShowsSelectedCount()
    {
        var cut = Render<TmBulkActionBar>(p => p
            .Add(x => x.SelectedCount, 5));

        cut.Find(".tm-bulk-action-bar__count").TextContent.Should().Contain("5");
    }

    [Fact]
    public void BulkActionBar_DoesNotClaimToolbarRoleItCannotHonour()
    {
        var cut = Render<TmBulkActionBar>(p => p
            .Add(x => x.SelectedCount, 1));

        var root = cut.Find(".tm-bulk-action-bar");
        root.GetAttribute("role").Should().Be(
            "group",
            "lišta nemá roving tabindex; toolbar by sliboval šipky, které neumí");
        root.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BulkActionBar_ClearButton_FiresEvent()
    {
        bool cleared = false;
        var cut = Render<TmBulkActionBar>(p => p
            .Add(x => x.SelectedCount, 3)
            .Add(x => x.OnClearSelection, EventCallback.Factory.Create(this, () => cleared = true)));

        cut.Find(".tm-bulk-action-bar__clear").Click();

        cleared.Should().BeTrue();
    }

    [Fact]
    public void BulkActionBar_Actions_RendersChildContent()
    {
        var cut = Render<TmBulkActionBar>(p => p
            .Add(x => x.SelectedCount, 2)
            .AddChildContent("<button>Delete Selected</button>"));

        cut.Find(".tm-bulk-action-bar__actions").InnerHtml.Should().Contain("Delete Selected");
    }

    [Fact]
    public void BulkActionBar_CustomClass_IsApplied()
    {
        var cut = Render<TmBulkActionBar>(p => p
            .Add(x => x.SelectedCount, 1)
            .Add(x => x.Class, "my-bar"));

        cut.Find(".tm-bulk-action-bar").ClassList.Should().Contain("my-bar");
    }
}
