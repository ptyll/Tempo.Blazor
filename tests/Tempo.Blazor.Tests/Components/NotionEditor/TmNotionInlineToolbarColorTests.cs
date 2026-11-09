using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// The inline toolbar paints a swatch from the palette token but must hand
/// <c>applyInlineColor</c> the allowlisted legacy hex — a CSS colour name is not a colour
/// <c>execCommand</c> will keep across save and reload.
/// </summary>
public sealed class TmNotionInlineToolbarColorTests : LocalizationTestBase
{
    public TmNotionInlineToolbarColorTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmNotionInlineToolbar_Label"] = "Format",
            ["TmNotionInlineToolbar_Color"] = "Color",
            ["TmNotionInlineToolbar_TextColor"] = "Text color",
            ["TmNotionInlineToolbar_BgColor"] = "Background color",
            ["TmNotionInlineToolbar_ColorDefault"] = "Default",
            ["TmNotionInlineToolbar_ColorGray"] = "Gray",
            ["TmNotionInlineToolbar_ColorBrown"] = "Brown",
            ["TmNotionInlineToolbar_ColorOrange"] = "Orange",
            ["TmNotionInlineToolbar_ColorYellow"] = "Yellow",
            ["TmNotionInlineToolbar_ColorGreen"] = "Green",
            ["TmNotionInlineToolbar_ColorBlue"] = "Blue",
            ["TmNotionInlineToolbar_ColorPurple"] = "Purple",
            ["TmNotionInlineToolbar_ColorPink"] = "Pink",
            ["TmNotionInlineToolbar_ColorRed"] = "Red",
            ["TmNotionInlineToolbar_ColorDefaultBg"] = "Default background",
            ["TmNotionInlineToolbar_ColorGrayBg"] = "Gray background",
            ["TmNotionInlineToolbar_ColorBrownBg"] = "Brown background",
            ["TmNotionInlineToolbar_ColorOrangeBg"] = "Orange background",
            ["TmNotionInlineToolbar_ColorYellowBg"] = "Yellow background",
            ["TmNotionInlineToolbar_ColorGreenBg"] = "Green background",
            ["TmNotionInlineToolbar_ColorBlueBg"] = "Blue background",
            ["TmNotionInlineToolbar_ColorPurpleBg"] = "Purple background",
            ["TmNotionInlineToolbar_ColorPinkBg"] = "Pink background",
            ["TmNotionInlineToolbar_ColorRedBg"] = "Red background",
        });
    }

    [Fact]
    public void TextSwatch_SendsLegacyHex_NotThePaletteName()
    {
        var cut = Render<TmNotionInlineToolbar>(p => p.Add(x => x.Visible, true));
        cut.Find("button[title='Color']").Click();

        var brown = cut.FindAll(".tm-notion-inline-toolbar__color-swatch")
            .First(button => button.GetAttribute("title") == "Brown"
                && button.GetAttribute("style")?.Contains("--tm-notion-option-text-brown-wash") != true);
        brown.GetAttribute("style").Should().Contain("var(--tm-notion-option-text-brown-ink)");

        var call = JSInterop.SetupVoid("tmNotionEditor.applyInlineColor", _ => true).SetVoidResult();
        brown.Click(new MouseEventArgs());

        var args = call.Invocations.Single().Arguments;
        args[0].Should().Be("text");
        args[1].Should().Be(NotionColorTokens.InlineHex("brown", false));
        args[1]!.ToString().Should().StartWith("#");
    }

    [Fact]
    public void BackgroundSwatch_SendsLegacyBackgroundHex()
    {
        var cut = Render<TmNotionInlineToolbar>(p => p.Add(x => x.Visible, true));
        cut.Find("button[title='Color']").Click();

        var brown = cut.FindAll(".tm-notion-inline-toolbar__color-swatch")
            .First(button => button.GetAttribute("style")?.Contains("--tm-notion-option-text-brown-wash") == true);

        var call = JSInterop.SetupVoid("tmNotionEditor.applyInlineColor", _ => true).SetVoidResult();
        brown.Click(new MouseEventArgs());

        var args = call.Invocations.Single().Arguments;
        args[0].Should().Be("bg");
        args[1].Should().Be(NotionColorTokens.InlineHex("brown", true));
    }

    [Fact]
    public void Swatches_NameTheColor_PressTheCurrentOne_AndReportTheTokenName()
    {
        string? picked = null;
        var cut = Render<TmNotionInlineToolbar>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.CurrentTextColor, "brown")
            .Add(x => x.OnTextColorSelected, EventCallback.Factory.Create<string?>(this, value => picked = value)));
        cut.Find("button[title='Color']").Click();

        var brown = cut.FindAll(".tm-notion-inline-toolbar__color-swatch")
            .First(button => button.GetAttribute("aria-label") == "Brown"
                && button.GetAttribute("style")?.Contains("--tm-notion-option-text-brown-wash") != true);
        brown.GetAttribute("aria-pressed").Should().Be("true");

        // The callback carries the token name. The interop still receives the allowlisted hex —
        // that contract is pinned by TextSwatch_SendsLegacyHex_NotThePaletteName.
        brown.Click(new MouseEventArgs());

        picked.Should().Be("brown");
    }
}
