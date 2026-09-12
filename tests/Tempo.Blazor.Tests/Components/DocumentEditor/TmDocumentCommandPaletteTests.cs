using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class TmDocumentCommandPaletteTests : LocalizationTestBase
{
    [Fact]
    public void Closed_DoesNotRender()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, false)
                      .Add(p => p.Commands, MakeCommands()));

        cut.FindAll("[data-testid='document-command-palette']").Should().BeEmpty();
    }

    [Fact]
    public void Open_RendersOnlyVisibleCommands()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands()));

        cut.FindAll("[data-testid='document-command-palette-item']")
            .Should()
            .HaveCount(2);
        cut.FindAll("[data-command='hidden']").Should().BeEmpty();
    }

    [Fact]
    public void Search_FiltersByLabelCategoryAndName()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands()));

        cut.Find("[data-testid='document-command-palette-search']").Input("Review");

        var items = cut.FindAll("[data-testid='document-command-palette-item']");
        items.Should().ContainSingle();
        items[0].GetAttribute("data-command").Should().Be("comment");
    }

    [Fact]
    public void DisabledCommand_RendersReasonAndDoesNotExecute()
    {
        string? executed = null;
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands())
                      .Add(p => p.OnExecuteCommand, EventCallback.Factory.Create<string>(this, name => executed = name)));

        var disabledButton = cut.Find("[data-command='comment'] button");

        disabledButton.HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-command='comment'] [data-testid='document-command-palette-disabled-reason']")
            .TextContent
            .Should()
            .Contain("Command is unavailable");
        disabledButton.Click();

        executed.Should().BeNull();
    }

    [Fact]
    public void EnabledCommand_ClickExecutesCommand()
    {
        string? executed = null;
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands())
                      .Add(p => p.OnExecuteCommand, EventCallback.Factory.Create<string>(this, name => executed = name)));

        cut.Find("[data-command='bold'] button").Click();

        executed.Should().Be("bold");
    }

    [Fact]
    public void CloseButton_FiresCloseCallback()
    {
        var closed = false;
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands())
                      .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find("[data-testid='document-command-palette-close']").Click();

        closed.Should().BeTrue();
    }

    // ─── Keyboard activation ──────────────────────────────────────────────────
    // Command items are native <button> elements, so the browser delivers the
    // activation click itself (Enter on keydown, Space on keyup). Emulating it
    // in @onkeydown would execute the command TWICE per key press. bUnit does
    // not synthesize the native click, so tests dispatch the full sequence.

    [Fact]
    public void ItemButton_EnterSequence_ExecutesCommandExactlyOnce()
    {
        var executed = new List<string>();
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands())
                      .Add(p => p.OnExecuteCommand, EventCallback.Factory.Create<string>(this, name => executed.Add(name))));

        var button = cut.Find("[data-command='bold'] button");
        // Real browser sequence for Enter on a focused <button>: keydown -> click.
        button.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        button.Click();

        executed.Should().Equal("bold");
    }

    [Fact]
    public void SearchInput_Escape_Closes_Once()
    {
        // Escape is handled on keydown; a keyup handler that also closed the
        // palette would fire OnClose a second time for the same gesture.
        var closeCalls = 0;
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands())
                      .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closeCalls++)));

        var search = cut.Find("[data-testid='document-command-palette-search']");
        search.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        closeCalls.Should().Be(1);
    }

    [Fact]
    public void ItemButton_SpaceSequence_ExecutesCommandExactlyOnce()
    {
        var executed = new List<string>();
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands())
                      .Add(p => p.OnExecuteCommand, EventCallback.Factory.Create<string>(this, name => executed.Add(name))));

        var button = cut.Find("[data-command='bold'] button");
        // Real browser sequence for Space: keydown -> keyup -> click. bUnit
        // cannot dispatch keyup here (no element on the bubble path has an
        // onkeyup handler); only the resulting click matters for the count.
        button.KeyDown(new KeyboardEventArgs { Key = " " });
        button.Click();

        executed.Should().Equal("bold");
    }

    [Fact]
    public void ItemButton_ArrowKeys_StillMoveHighlight()
    {
        // Removing the Enter/Space emulation must not break the list keyboard
        // navigation that shares the same handler.
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands()));

        var firstButton = cut.Find("[data-command='bold'] button");
        firstButton.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.Find("[data-command='comment']").GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void SearchInput_Enter_ExecutesHighlightedCommand()
    {
        // Enter on the search <input> produces no native click, so the
        // emulation there is required and must keep working.
        var executed = new List<string>();
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands())
                      .Add(p => p.OnExecuteCommand, EventCallback.Factory.Create<string>(this, name => executed.Add(name))));

        cut.Find("[data-testid='document-command-palette-search']")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });

        executed.Should().Equal("bold");
    }

    private static Dictionary<string, DocumentEditorCommandState> MakeCommands() =>
        new()
        {
            ["bold"] = new DocumentEditorCommandState
            {
                Name = "bold",
                IsEnabled = true,
                IsVisible = true,
                DescriptionKey = "TmDocumentEditor_Bold",
                Category = "Home",
                DefaultShortcut = "Ctrl+B",
                Icon = "bold"
            },
            ["comment"] = new DocumentEditorCommandState
            {
                Name = "comment",
                IsEnabled = false,
                IsVisible = true,
                DescriptionKey = "TmDocumentEditor_AddComment",
                Category = "Review",
                DisabledReasonKey = "TmDocumentEditor_CommandDisabledUnavailable"
            },
            ["hidden"] = new DocumentEditorCommandState
            {
                Name = "hidden",
                IsEnabled = false,
                IsVisible = false,
                DescriptionKey = "TmDocumentEditor_InsertFootnote"
            }
        };
}
