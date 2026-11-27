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
        cut.FindAll("[role='option'][data-command='hidden']").Should().BeEmpty();
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
    public void DisabledCommand_RendersReasonAsTheDescription_IsAriaDisabled_AndDoesNotExecute()
    {
        string? executed = null;
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands())
                      .Add(p => p.OnExecuteCommand, EventCallback.Factory.Create<string>(this, name => executed = name)));

        var option = cut.Find("[role='option'][data-command='comment']");

        option.GetAttribute("aria-disabled").Should().Be("true");
        option.QuerySelector(".tm-command-palette-disabled-reason")!.TextContent.Should().Contain("Command is unavailable");
        option.Click();

        executed.Should().BeNull();
        cut.FindAll("[role='option'][data-command='comment']").Should().ContainSingle("the palette stays open so the reason can be read");
    }

    [Fact]
    public void EnabledCommand_ClickExecutesCommand_AndClosesThePalette()
    {
        string? executed = null;
        var closed = 0;
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands())
                      .Add(p => p.OnExecuteCommand, EventCallback.Factory.Create<string>(this, name => executed = name))
                      .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closed++)));

        cut.Find("[role='option'][data-command='bold']").Click();

        executed.Should().Be("bold");
        closed.Should().Be(1);
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

    [Fact]
    public async Task SearchInput_Enter_ExecutesHighlightedCommand_Once()
    {
        // Enter on the search <input> produces no native click, so the core palette's keydown handler executes the highlighted action.
        var executed = new List<string>();
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands())
                      .Add(p => p.OnExecuteCommand, EventCallback.Factory.Create<string>(this, name => executed.Add(name))));

        await cut.Find("[data-testid='document-command-palette-search']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await Task.Delay(30);

        executed.Should().Equal("bold");
    }

    [Fact]
    public async Task SearchInput_ArrowKeys_MoveTheHighlight_AndReachADisabledCommand()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Commands, MakeCommands()));

        await cut.Find("[data-testid='document-command-palette-search']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.Find("[role='option'][data-command='comment']").GetAttribute("aria-selected").Should().Be("true");
    }

    // ─── Phase 5 review: an adapter over the core TmCommandPalette (decision DE-COMMAND-PALETTE-HOST) ───────────────────

    private static Dictionary<string, DocumentEditorCommandState> GroupedCommands() => new()
    {
        ["new"] = new() { Name = "new", IsEnabled = true, IsVisible = true, DescriptionKey = "TmDocumentEditor_Bold", Category = "File", DefaultShortcut = "Ctrl+N" },
        ["save"] = new() { Name = "save", IsEnabled = true, IsVisible = true, DescriptionKey = "TmDocumentEditor_Save", Category = "File", DefaultShortcut = "Ctrl+S" },
        ["find"] = new() { Name = "find", IsEnabled = true, IsVisible = true, DescriptionKey = "TmDocumentEditor_Italic", Category = "Edit", DefaultShortcut = "Ctrl+F", Icon = "search" },
        ["track"] = new() { Name = "track", IsEnabled = true, IsVisible = true, DescriptionKey = "TmDocumentEditor_TrackChanges", Category = "Review", DefaultShortcut = "Ctrl+Shift+E" },
        ["debug"] = new() { Name = "debug", IsEnabled = true, IsVisible = true, DescriptionKey = "TmDocumentEditor_Bold", Category = "Debug" },
    };

    [Fact]
    public void IsTheCoreCommandPalette_WithTheDocumentModifier_AndTheStableTestIds()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Commands, GroupedCommands()));

        var root = cut.Find("[data-testid='document-command-palette']");
        root.ClassList.Should().Contain("tm-command-palette-backdrop").And.Contain("tm-document-command-palette");
        root.GetAttribute("popover").Should().Be("manual", "the core palette promotes itself to the top layer");
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty("the palette is no TmModal any more");
        cut.Find("[data-testid='document-command-palette-search']").GetAttribute("role").Should().Be("combobox");
        cut.Find("[role='listbox']").Should().NotBeNull();
        cut.FindAll("[role='option']").Should().OnlyContain(o => o.GetAttribute("data-testid") == "document-command-palette-item");
    }

    [Fact]
    public void ListIsAValidListbox_NoInteractiveChildInsideAnOption_AndTheComboboxNamesTheActiveOption()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Commands, GroupedCommands()));

        cut.FindAll("[role='option'] button, [role='option'] a, [role='option'] input").Should().BeEmpty();
        var search = cut.Find("[data-testid='document-command-palette-search']");
        var active = search.GetAttribute("aria-activedescendant");
        active.Should().NotBeNullOrWhiteSpace();
        cut.Find($"[id='{active}']").GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void Shortcut_IsRenderedAsOneKbdPerKey()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Commands, GroupedCommands()));

        cut.FindAll("[role='option'][data-command='track'] kbd").Select(k => k.TextContent).Should().Equal("Ctrl", "Shift", "E");
        cut.FindAll("[role='option'][data-command='new'] kbd").Should().HaveCount(2);
    }

    [Fact]
    public void Categories_AreGroupedInTheEditorsOrder_WithDebugLast_AndLocalized()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Commands, GroupedCommands()));

        cut.FindAll(".tm-command-palette-group-label").Select(l => l.TextContent)
            .Should().Equal("File", "Edit", "Review", "Debug");
        cut.FindAll("[role='option']").Select(o => o.GetAttribute("data-command")).Last().Should().Be("debug");
    }

    [Fact]
    public void Search_MatchesTheCommandNameAndTheCategory_NotOnlyTheLabel()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Commands, GroupedCommands()));

        cut.Find("[data-testid='document-command-palette-search']").Input("track");
        cut.FindAll("[role='option']").Select(o => o.GetAttribute("data-command")).Should().Equal("track");

        cut.Find("[data-testid='document-command-palette-search']").Input("Review");
        cut.FindAll("[role='option']").Select(o => o.GetAttribute("data-command")).Should().Equal("track");
    }

    [Fact]
    public void Items_ShowTheirIcon_WhenTheCommandHasOne_AndKeepTheColumnOtherwise()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Commands, GroupedCommands()));

        cut.Find("[role='option'][data-command='find'] .tm-command-palette-icon svg").Should().NotBeNull();
        cut.Find("[role='option'][data-command='new'] .tm-command-palette-icon").InnerHtml.Trim().Should().BeEmpty("the column stays so the titles line up");
    }

    [Fact]
    public void NoMatch_ShowsTheEmptyState_WithItsTestId()
    {
        var cut = Render<TmDocumentCommandPalette>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Commands, GroupedCommands()));

        cut.Find("[data-testid='document-command-palette-search']").Input("zzzz-nothing");

        cut.Find("[data-testid='document-command-palette-empty']").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Palette_OwnStylesheet_RestylesNothingOfTheCore()
    {
        var css = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.DocumentEditor", "wwwroot", "css", "components", "_document-editor-floating.css"));
        var paletteBlock = css[css.IndexOf("Command palette (the core TmCommandPalette", StringComparison.Ordinal)..];
        paletteBlock = paletteBlock[..paletteBlock.IndexOf("Touch targets", StringComparison.Ordinal)];

        paletteBlock.Should().NotContain("__item", "the list rows are the core's");
        paletteBlock.Should().NotContain("kbd");
        paletteBlock.Should().NotContain("::before");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
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
                Category = "Formatting",
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
