using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Clipboard;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public sealed class DocumentEditorPhase21AccessibilityTests : LocalizationTestBase
{
    [Fact]
    public void Toolbar_ExposesGroupAndTabSemantics()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        var toolbar = cut.Find("[data-testid='document-toolbar']");
        toolbar.GetAttribute("role").Should().Be(
            "group",
            "šipky v téhle liště obsluhují ribbon tablist, ne roving toolbaru");
        toolbar.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        var tablist = cut.Find("[role='tablist']");
        tablist.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        cut.FindAll("[role='tab']").Should().NotBeEmpty();
    }

    [Fact]
    public async Task OverflowMenu_ExposesMenuSemanticsAndRovingTabIndex()
    {
        string? executed = null;
        var cut = Render<TmDocumentToolbarOverflowMenu>(parameters => parameters
            .Add(p => p.IsOverflowing, true)
            .Add(p => p.IsOpen, true)
            .Add(p => p.MoreLabel, "More")
            .Add(p => p.MoreCommandsLabel, "More commands")
            .Add(p => p.Groups, OverflowGroups())
            .Add(p => p.OnCommandRequested, EventCallback.Factory.Create<string>(this, value => executed = value)));

        var menu = cut.Find("[data-testid='document-toolbar-more-menu']");
        menu.GetAttribute("role").Should().Be("menu");
        menu.GetAttribute("aria-label").Should().Be("More commands");

        var items = cut.FindAll("[role='menuitem']");
        items.Should().HaveCount(2);
        items[0].GetAttribute("tabindex").Should().Be("0");
        items[1].GetAttribute("tabindex").Should().Be("-1");

        await items[0].KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        items = cut.FindAll("[role='menuitem']");
        items[0].GetAttribute("tabindex").Should().Be("-1");
        items[1].GetAttribute("tabindex").Should().Be("0");

        await items[0].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        executed.Should().Be("italic");
    }

    [Fact]
    public async Task CommandPalette_ExposesDialogListboxAndKeyboardSelection()
    {
        string? executed = null;
        var closed = false;
        var cut = Render<TmDocumentCommandPalette>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Commands, CommandStates())
            .Add(p => p.OnExecuteCommand, EventCallback.Factory.Create<string>(this, value => executed = value))
            .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        var dialog = cut.Find("[role='dialog']");
        dialog.GetAttribute("aria-modal").Should().Be("true");
        dialog.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        cut.Find("[role='listbox']").GetAttribute("aria-activedescendant").Should().NotBeNullOrWhiteSpace();
        var options = cut.FindAll("[role='option']");
        options.Should().HaveCount(2);
        options[0].GetAttribute("aria-selected").Should().Be("true");

        var search = cut.Find("[data-testid='document-command-palette-search']");
        await search.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.FindAll("[role='option']")[1].GetAttribute("aria-selected").Should().Be("true");

        await search.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        executed.Should().Be("italic");

        await search.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        closed.Should().BeTrue();
    }

    [Fact]
    public void ImageInspector_ExposesComplementaryLandmarkLabelsAndLiveWarning()
    {
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, new ImageBlockContent { Source = DocumentImageSource.Url, Url = "https://example.test/evidence.png" }));

        var inspector = cut.Find("[data-testid='document-image-inspector']");
        inspector.GetAttribute("role").Should().Be("complementary");
        inspector.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        cut.Find("[data-testid='document-image-inspector-alt']").ParentElement!.TextContent.Should().Contain("Alt");
        cut.Find("[data-testid='document-image-inspector-link']").ParentElement!.TextContent.ToLowerInvariant().Should().Contain("url");
        cut.Find("[data-testid='document-image-inspector-alt-warning']").GetAttribute("role").Should().Be("status");
        cut.FindAll("[role='group']").Should().HaveCountGreaterThanOrEqualTo(
            2,
            "skupiny wrap/align slibovaly toolbar bez rovingu; group nese přístupné jméno");
    }

    [Fact]
    public void TableAndCellProperties_ExposeRegionsGroupsAndInputLabels()
    {
        var table = Render<TmDocumentTablePropertiesPanel>(parameters => parameters
            .Add(p => p.Layout, new TableLayoutContent()));

        table.Find("[data-testid='document-table-properties-panel']").GetAttribute("role").Should().Be("region");
        table.Find("[data-testid='document-table-properties-panel']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        table.Find("[role='group']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        table.Find("[data-testid='document-table-properties-width']").ParentElement!.TextContent.Should().Contain("width");
        table.Find("[data-testid='document-table-properties-border']").ParentElement!.TextContent.Should().Contain("border");

        var cell = Render<TmDocumentCellPropertiesPanel>(parameters => parameters
            .Add(p => p.Cell, new TableCellContent()));

        cell.Find("[data-testid='document-cell-properties-panel']").GetAttribute("role").Should().Be("region");
        cell.Find("[role='group']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        cell.Find("[data-testid='document-cell-properties-background']").ParentElement!.TextContent.Should().Contain("background");
        cell.Find("[data-testid='document-cell-properties-padding']").ParentElement!.TextContent.Should().Contain("padding");
    }

    [Fact]
    public void GridPickerPasteReportAutocompleteAndLiveRegion_ExposeAccessibleStatusRoles()
    {
        var grid = Render<TmDocumentTableGridPicker>();
        grid.Find("[data-testid='document-table-grid-picker']").GetAttribute("role").Should().Be("grid");
        grid.FindAll("[role='gridcell']").Should().HaveCount(100);
        grid.Find(".tm-document-table-grid-picker__dims").GetAttribute("aria-live").Should().Be("polite");

        var report = Render<TmDocumentPasteReport>(parameters => parameters
            .Add(p => p.Warnings, [new DocumentClipboardWarning { Code = "unsafe-link-removed", Message = "Link removed" }]));
        report.Find("[data-testid='document-paste-report']").GetAttribute("role").Should().Be("status");
        report.Find("[data-testid='document-paste-report']").GetAttribute("aria-live").Should().Be("polite");
        report.Find("[data-testid='document-paste-report-close']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        var autocomplete = Render<TmDocumentAutocompleteMenu>(parameters => parameters
            .Add(p => p.IsVisible, true)
            .Add(p => p.Items, AutocompleteItems())
            .Add(p => p.HighlightedIndex, 0));
        autocomplete.Find("[data-testid='document-autocomplete-menu']").GetAttribute("role").Should().Be("listbox");
        autocomplete.Find("[data-testid='document-autocomplete-menu']").GetAttribute("aria-busy").Should().Be("false");
        autocomplete.FindAll("[role='option']").Should().HaveCount(2);

        var live = Render<TmDocumentEditorLiveRegion>(parameters => parameters
            .Add(p => p.Message, "Saved")
            .Add(p => p.AriaLive, "assertive"));
        live.Find("[data-testid='document-editor-live-region']").GetAttribute("role").Should().Be("status");
        live.Find("[data-testid='document-editor-live-region']").GetAttribute("aria-live").Should().Be("assertive");
        live.Find("[data-testid='document-editor-live-region']").GetAttribute("aria-atomic").Should().Be("true");
    }

    private static IReadOnlyList<DocumentToolbarOverflowMenuGroup> OverflowGroups() =>
    [
        new(
            "formatting",
            "Formatting",
            [
                new("bold", ToolbarItem("bold"), "Bold", true),
                new("italic", ToolbarItem("italic"), "Italic", true)
            ])
    ];

    private static DocumentToolbarItem ToolbarItem(string commandName) =>
        new()
        {
            Id = commandName,
            CommandName = commandName,
            LabelKey = commandName,
            Group = "formatting"
        };

    private static Dictionary<string, DocumentEditorCommandState> CommandStates() =>
        new()
        {
            ["bold"] = new DocumentEditorCommandState
            {
                Name = "bold",
                IsEnabled = true,
                IsVisible = true,
                DescriptionKey = "TmDocumentEditor_Bold",
                Category = "Home"
            },
            ["italic"] = new DocumentEditorCommandState
            {
                Name = "italic",
                IsEnabled = true,
                IsVisible = true,
                DescriptionKey = "TmDocumentEditor_Italic",
                Category = "Home"
            }
        };

    private static IReadOnlyList<DocumentAutocompleteItem> AutocompleteItems() =>
    [
        new() { Id = "client.name", Label = "Client name", Kind = DocumentAutocompleteKind.Token },
        new() { Id = "alex", Label = "Alex Johnson", Kind = DocumentAutocompleteKind.Mention }
    ];
}
