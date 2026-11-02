using FluentAssertions;
using Tempo.Blazor.Components.NotionEditor.Services;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 2 review: a stored colour is a palette name. Legacy hex still maps, unknown values
/// pass through, and the one hex the toolbar may send to <c>execCommand</c> is the allowlisted
/// <see cref="NotionColorTokens.InlineHex"/> pair.
/// </summary>
public sealed class NotionColorTokensTests
{
    [Theory]
    [InlineData("#e03e3e", "red")]
    [InlineData("#E03E3E", "red")]
    [InlineData("#64473a", "brown")]
    [InlineData("#fbe4e4", "red")]
    public void Name_MapsLegacyHex_IgnoringCase(string stored, string name)
    {
        NotionColorTokens.Name(stored).Should().Be(name);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("brown")]
    [InlineData("not-a-colour")]
    public void Name_PassesANameOrUnknownValueThrough(string stored)
    {
        NotionColorTokens.Name(stored).Should().Be(stored);
    }

    [Fact]
    public void Name_Null_StaysNull()
    {
        NotionColorTokens.Name(null).Should().BeNull();
    }

    [Theory]
    [InlineData("brown", false, "#64473a")]
    [InlineData("brown", true, "#e9e5e3")]
    [InlineData("red", false, "#e03e3e")]
    [InlineData("RED", true, "#fbe4e4")]
    public void InlineHex_IsTheAllowlistedLegacyPair(string name, bool background, string hex)
    {
        NotionColorTokens.InlineHex(name, background).Should().Be(hex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("not-a-colour")]
    [InlineData("#e03e3e")]
    public void InlineHex_Unknown_IsNull(string? name)
    {
        NotionColorTokens.InlineHex(name, false).Should().BeNull();
        NotionColorTokens.InlineHex(name, true).Should().BeNull();
    }

    [Theory]
    [InlineData("brown", "var(--tm-notion-option-brown-solid)")]
    [InlineData("Red", "var(--tm-notion-option-red-solid)")]
    [InlineData("#e03e3e", "var(--tm-notion-option-red-solid)")]
    public void OptionSolid_MapsAPaletteNameOrLegacyHex(string stored, string expected)
    {
        NotionColorTokens.OptionSolid(stored).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-colour")]
    [InlineData("#112233")]
    public void OptionSolid_Unknown_FallsBackToPrimary(string? stored)
    {
        NotionColorTokens.OptionSolid(stored).Should().Be("var(--tm-color-primary)");
    }
}
