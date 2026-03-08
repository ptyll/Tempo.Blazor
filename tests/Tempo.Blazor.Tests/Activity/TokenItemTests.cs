using FluentAssertions;
using Tempo.Blazor.Components.Activity;

namespace Tempo.Blazor.Tests.Activity;

public class TokenItemTests
{
    [Fact]
    public void TokenItem_DefaultValues()
    {
        var token = new TokenItem();

        token.Key.Should().Be(string.Empty);
        token.DisplayName.Should().Be(string.Empty);
        token.Description.Should().BeNull();
        token.Category.Should().BeNull();
    }

    [Fact]
    public void TokenItem_SetProperties()
    {
        var token = new TokenItem
        {
            Key = "user.email",
            DisplayName = "User Email",
            Description = "The email address of the current user",
            Category = "User"
        };

        token.Key.Should().Be("user.email");
        token.DisplayName.Should().Be("User Email");
        token.Description.Should().Be("The email address of the current user");
        token.Category.Should().Be("User");
    }

    [Fact]
    public void TokenItem_NullableProperties()
    {
        var token = new TokenItem
        {
            Key = "system.date",
            DisplayName = "Current Date"
        };

        token.Description.Should().BeNull();
        token.Category.Should().BeNull();
    }
}
