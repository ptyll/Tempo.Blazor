using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Avatars;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Avatars;

/// <summary>TDD tests for TmAvatarGroup.</summary>
public class TmAvatarGroupTests : LocalizationTestBase
{
    [Fact]
    public void TmAvatarGroup_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmAvatarGroup>(p => p
            .Add(c => c.TotalCount, 3)
            .AddChildContent("<div>A</div><div>B</div><div>C</div>"));

        cut.Find(".tm-avatar-group").Should().NotBeNull();
    }

    [Fact]
    public void TmAvatarGroup_No_Overflow_When_TotalCount_Within_Max()
    {
        var cut = RenderComponent<TmAvatarGroup>(p => p
            .Add(c => c.Max, 5)
            .Add(c => c.TotalCount, 3)
            .AddChildContent("<div>A</div><div>B</div><div>C</div>"));

        cut.FindAll(".tm-avatar-overflow").Should().BeEmpty();
    }

    [Fact]
    public void TmAvatarGroup_Shows_Overflow_When_TotalCount_Exceeds_Max()
    {
        var cut = RenderComponent<TmAvatarGroup>(p => p
            .Add(c => c.Max, 3)
            .Add(c => c.TotalCount, 7)
            .AddChildContent("<div>A</div><div>B</div><div>C</div>"));

        var overflow = cut.Find(".tm-avatar-overflow");
        overflow.TextContent.Should().Contain("+4");
    }

    [Fact]
    public void TmAvatarGroup_No_Overflow_When_Max_Null()
    {
        var cut = RenderComponent<TmAvatarGroup>(p => p
            .Add(c => c.TotalCount, 10)
            .AddChildContent("<div>A</div>"));

        cut.FindAll(".tm-avatar-overflow").Should().BeEmpty();
    }

    [Fact]
    public void TmAvatarGroup_Applies_Size_CssClass()
    {
        var cut = RenderComponent<TmAvatarGroup>(p => p
            .Add(c => c.Size, AvatarSize.Sm)
            .Add(c => c.TotalCount, 2)
            .AddChildContent("<div>A</div>"));

        cut.Find(".tm-avatar-group").ClassList.Should().Contain("tm-avatar-group-sm");
    }
}
