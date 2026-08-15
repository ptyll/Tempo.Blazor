using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Avatars;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Avatars;

/// <summary>TDD tests for TmAvatar.</summary>
public class TmAvatarTests : LocalizationTestBase
{
    [Fact]
    public void TmAvatar_Has_Base_CssClass()
    {
        var cut = Render<TmAvatar>();
        cut.Find("div").ClassList.Should().Contain("tm-avatar");
    }

    [Theory]
    [InlineData(AvatarSize.Xs,  "tm-avatar-xs")]
    [InlineData(AvatarSize.Sm,  "tm-avatar-sm")]
    [InlineData(AvatarSize.Md,  "tm-avatar-md")]
    [InlineData(AvatarSize.Lg,  "tm-avatar-lg")]
    [InlineData(AvatarSize.Xl,  "tm-avatar-xl")]
    [InlineData(AvatarSize.Xxl, "tm-avatar-2xl")]
    public void TmAvatar_Applies_Size_CssClass(AvatarSize size, string expectedClass)
    {
        var cut = Render<TmAvatar>(p => p.Add(c => c.Size, size));
        cut.Find("div.tm-avatar").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmAvatar_Default_Size_Is_Md()
    {
        var cut = Render<TmAvatar>();
        cut.Find("div").ClassList.Should().Contain("tm-avatar-md");
    }

    [Theory]
    [InlineData(AvatarShape.Circle, "tm-avatar-circle")]
    [InlineData(AvatarShape.Square, "tm-avatar-square")]
    public void TmAvatar_Applies_Shape_CssClass(AvatarShape shape, string expectedClass)
    {
        var cut = Render<TmAvatar>(p => p.Add(c => c.Shape, shape));
        cut.Find("div.tm-avatar").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmAvatar_Default_Shape_Is_Circle()
    {
        var cut = Render<TmAvatar>();
        cut.Find("div").ClassList.Should().Contain("tm-avatar-circle");
    }

    [Fact]
    public void TmAvatar_With_Src_Renders_Image()
    {
        var cut = Render<TmAvatar>(p => p
            .Add(c => c.Src, "https://example.com/avatar.jpg")
            .Add(c => c.Alt, "User"));

        var img = cut.Find("img");
        img.Should().NotBeNull();
        img.GetAttribute("src").Should().Contain("example.com");
    }

    [Fact]
    public void TmAvatar_With_Name_No_Src_Renders_Initials()
    {
        var cut = Render<TmAvatar>(p => p.Add(c => c.Name, "John Doe"));

        cut.FindAll("img").Should().BeEmpty();
        cut.Find(".tm-avatar-fallback").TextContent.Trim().Should().Be("JD");
    }

    [Fact]
    public void TmAvatar_Single_Word_Name_Uses_First_Letter()
    {
        var cut = Render<TmAvatar>(p => p.Add(c => c.Name, "Alice"));
        cut.Find(".tm-avatar-fallback").TextContent.Trim().Should().Be("A");
    }

    [Theory]
    [InlineData(AvatarColor.Gray, "tm-avatar-gray")]
    [InlineData(AvatarColor.Red, "tm-avatar-red")]
    [InlineData(AvatarColor.Orange, "tm-avatar-orange")]
    [InlineData(AvatarColor.Green, "tm-avatar-green")]
    [InlineData(AvatarColor.Blue, "tm-avatar-blue")]
    [InlineData(AvatarColor.Purple, "tm-avatar-purple")]
    [InlineData(AvatarColor.Pink, "tm-avatar-pink")]
    public void TmAvatar_Applies_Color_CssClass(AvatarColor color, string expectedClass)
    {
        var cut = Render<TmAvatar>(p => p
            .Add(c => c.Name, "Ada Lovelace")
            .Add(c => c.Color, color));

        cut.Find("div.tm-avatar").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmAvatar_Default_Color_Is_Gray()
    {
        var cut = Render<TmAvatar>(p => p.Add(c => c.Name, "Ada"));

        cut.Find("div.tm-avatar").ClassList.Should().Contain("tm-avatar-gray");
    }

    [Fact]
    public void TmAvatar_No_Src_No_Name_Shows_Placeholder()
    {
        var cut = Render<TmAvatar>();
        // Should render without error (no img, no initials)
        cut.FindAll("img").Should().BeEmpty();
    }
}
