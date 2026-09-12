using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public class TmNotionStatusPickerTests : LocalizationTestBase
{
    public TmNotionStatusPickerTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Notion_Status_Placeholder"] = "Status label",
            ["Notion_Status_Color_Gray"] = "Gray",
            ["Notion_Status_Color_Blue"] = "Blue",
            ["Notion_Status_Color_Green"] = "Green",
            ["Notion_Status_Color_Yellow"] = "Yellow",
            ["Notion_Status_Color_Red"] = "Red",
            ["Notion_Status_Color_Purple"] = "Purple",
            ["Notion_Status_Insert"] = "Insert status"
        });
    }

    [Fact]
    public void StatusPicker_WhenHidden_RendersNothing()
    {
        var cut = Render<TmNotionStatusPicker>(p => p
            .Add(x => x.Visible, false));

        cut.FindAll(".tm-notion-status-picker").Should().BeEmpty();
    }

    [Fact]
    public void StatusPicker_RendersInitialLabelAndColor()
    {
        var cut = Render<TmNotionStatusPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Top, 120)
            .Add(x => x.Left, 240)
            .Add(x => x.InitialLabel, "DONE")
            .Add(x => x.InitialColor, NotionStatusColor.Green));

        cut.Find(".tm-notion-status-picker").GetAttribute("style").Should().Contain("top:120px");
        cut.Find(".tm-notion-status").ClassList.Should().Contain("tm-notion-status--green");
        cut.Find(".tm-notion-status__label").TextContent.Should().Be("DONE");
    }

    [Fact]
    public async Task StatusPicker_SelectsColorAndInsertsTrimmedLabel()
    {
        (string Label, NotionStatusColor Color) inserted = default;
        var cut = Render<TmNotionStatusPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Top, 120)
            .Add(x => x.Left, 240)
            .Add(x => x.InitialLabel, "  IN PROGRESS  ")
            .Add(x => x.OnInserted,
                EventCallback.Factory.Create<(string, NotionStatusColor)>(
                    this, args => inserted = args)));

        await cut.Find(".tm-notion-status-picker__swatch--blue").ClickAsync(new MouseEventArgs());
        await cut.Find(".tm-notion-status-picker__insert").ClickAsync(new MouseEventArgs());

        inserted.Label.Should().Be("IN PROGRESS");
        inserted.Color.Should().Be(NotionStatusColor.Blue);
    }

    [Fact]
    public async Task StatusPicker_DoesNotInsertEmptyLabel()
    {
        var fired = false;
        var cut = Render<TmNotionStatusPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Top, 120)
            .Add(x => x.Left, 240)
            .Add(x => x.OnInserted,
                EventCallback.Factory.Create<(string, NotionStatusColor)>(
                    this, _ => fired = true)));

        var button = cut.Find(".tm-notion-status-picker__insert");
        button.HasAttribute("disabled").Should().BeTrue();
        await button.ClickAsync(new MouseEventArgs());

        fired.Should().BeFalse();
    }

    // ── Keyboard activation ─────────────────────────────────────────────────
    // The swatches and the insert button are native <button> elements: Enter
    // produces a click on keydown. A container-level Enter->insert handler
    // therefore fired InsertAsync a second time (or while picking a color).

    [Fact]
    public void StatusPicker_EnterOnInsertButton_InsertsExactlyOnce()
    {
        var inserts = 0;
        var cut = Render<TmNotionStatusPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.InitialLabel, "DONE")
            .Add(x => x.OnInserted,
                EventCallback.Factory.Create<(string, NotionStatusColor)>(
                    this, _ => inserts++)));

        var button = cut.Find(".tm-notion-status-picker__insert");
        button.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find(".tm-notion-status-picker__insert").Click();

        inserts.Should().Be(1);
    }

    [Fact]
    public void StatusPicker_EnterOnSwatch_SelectsColor_WithoutInserting()
    {
        var fired = false;
        var cut = Render<TmNotionStatusPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.InitialLabel, "DONE")
            .Add(x => x.OnInserted,
                EventCallback.Factory.Create<(string, NotionStatusColor)>(
                    this, _ => fired = true)));

        // Real sequence for Enter on a focused <button>: keydown -> click.
        var swatch = cut.Find(".tm-notion-status-picker__swatch--blue");
        swatch.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find(".tm-notion-status-picker__swatch--blue").Click();

        fired.Should().BeFalse("picking a color must not submit the form");
        cut.Find(".tm-notion-status-picker__swatch--blue")
            .ClassList.Should().Contain("tm-notion-status-picker__swatch--selected");
    }

    [Fact]
    public void StatusPicker_EnterInLabelInput_InsertsOnce()
    {
        // Enter inside the label input produces no native click — the input's
        // own keydown handler is what submits, and it must still work.
        var inserts = 0;
        (string Label, NotionStatusColor Color) inserted = default;
        var cut = Render<TmNotionStatusPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.InitialLabel, "BLOCKED")
            .Add(x => x.OnInserted,
                EventCallback.Factory.Create<(string, NotionStatusColor)>(
                    this, args => { inserts++; inserted = args; })));

        cut.Find(".tm-notion-status-picker__input")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });

        inserts.Should().Be(1);
        inserted.Label.Should().Be("BLOCKED");
    }

    [Fact]
    public void StatusPicker_EscapeOnSwatch_StillClosesPicker()
    {
        // Escape keeps bubbling from any focused child to the container.
        var closed = false;
        var cut = Render<TmNotionStatusPicker>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.OnClosed, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find(".tm-notion-status-picker__swatch--blue")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });

        closed.Should().BeTrue();
    }
}
