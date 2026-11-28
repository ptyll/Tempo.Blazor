using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.DocumentEditor.Models;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class DocumentEditorToolbarRegistryTests
{
    [Fact]
    public void ViewTab_NeverGivesTwoCommandsTheSameIcon()
    {
        // Print preview and Print used to share the printer; two identical glyphs side by side on the View tab cannot be told apart by an icon-only reader.
        var duplicates = DocumentEditorBuiltInToolbar.DefaultItems.Concat(DocumentEditorBuiltInToolbar.RibbonLocalCommands)
            .Where(item => item.Tab == DocumentToolbarTab.View && !string.IsNullOrEmpty(item.Icon))
            .GroupBy(item => item.Icon!)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(i => i.Id))}")
            .ToList();

        duplicates.Should().BeEmpty();
    }

    // ─── 3.1 DocumentToolbarItem ──────────────────────────────────────────────

    [Fact]
    public void DocumentToolbarItem_ExposesRequiredProperties()
    {
        var item = new DocumentToolbarItem
        {
            Id = "save",
            CommandName = "save",
            Icon = "save",
            LabelKey = "TmDocumentEditor_Save",
            Kind = DocumentToolbarItemKind.Button,
            Tab = DocumentToolbarTab.Home,
            Group = "clipboard",
            GroupId = "clipboard",
            Order = 1,
            Priority = ToolbarItemPriority.Primary
        };

        item.Id.Should().Be("save");
        item.CommandName.Should().Be("save");
        item.Icon.Should().Be("save");
        item.LabelKey.Should().Be("TmDocumentEditor_Save");
        item.Kind.Should().Be(DocumentToolbarItemKind.Button);
        item.Tab.Should().Be(DocumentToolbarTab.Home);
        item.Group.Should().Be("clipboard");
        item.GroupId.Should().Be("clipboard");
        item.EffectiveGroup.Should().Be("clipboard");
        item.Order.Should().Be(1);
        item.Priority.Should().Be(ToolbarItemPriority.Primary);
    }

    [Fact]
    public void DocumentToolbarItem_DefaultsToButtonKindAndPrimaryPriority()
    {
        var item = new DocumentToolbarItem { Id = "x" };

        item.Kind.Should().Be(DocumentToolbarItemKind.Button);
        item.Tab.Should().Be(DocumentToolbarTab.Home);
        item.Priority.Should().Be(ToolbarItemPriority.Primary);
        item.Order.Should().Be(0);
        item.CommandName.Should().BeNull();
        item.Group.Should().BeNull();
        item.GroupId.Should().BeNull();
    }

    [Fact]
    public void DocumentToolbarItemKind_ContainsPhase4RendererKinds()
    {
        var values = Enum.GetValues<DocumentToolbarItemKind>();

        values.Should().Contain(DocumentToolbarItemKind.Button);
        values.Should().Contain(DocumentToolbarItemKind.Toggle);
        values.Should().Contain(DocumentToolbarItemKind.Select);
        values.Should().Contain(DocumentToolbarItemKind.ColorPicker);
        values.Should().Contain(DocumentToolbarItemKind.SplitButton);
        values.Should().Contain(DocumentToolbarItemKind.Menu);
        values.Should().Contain(DocumentToolbarItemKind.GridPicker);
        values.Should().Contain(DocumentToolbarItemKind.Separator);
    }

    [Fact]
    public void DocumentToolbarItem_VisibleWhenPredicateControlsVisibility()
    {
        var hidden = new DocumentToolbarItem
        {
            Id = "header-only",
            VisibleWhen = context => context.IsHeaderFooterMode
        };

        hidden.IsVisible(new DocumentToolbarVisibilityContext { IsHeaderFooterMode = false }).Should().BeFalse();
        hidden.IsVisible(new DocumentToolbarVisibilityContext { IsHeaderFooterMode = true }).Should().BeTrue();
    }

    [Fact]
    public void DocumentToolbarGroup_ExposesRequiredProperties()
    {
        var group = new DocumentToolbarGroup
        {
            Id = "clipboard",
            Tab = DocumentToolbarTab.Home,
            TabId = "home",
            LabelKey = "TmDocumentEditor_GroupClipboard",
            Order = 1
        };

        group.Id.Should().Be("clipboard");
        group.Tab.Should().Be(DocumentToolbarTab.Home);
        group.TabId.Should().Be("home");
        group.LabelKey.Should().Be("TmDocumentEditor_GroupClipboard");
        group.Order.Should().Be(1);
    }

    // ─── 3.1 Sort helper ─────────────────────────────────────────────────────

    [Fact]
    public void DocumentToolbarItems_AreSortedByOrder()
    {
        var items = new[]
        {
            new DocumentToolbarItem { Id = "c", Order = 3 },
            new DocumentToolbarItem { Id = "a", Order = 1 },
            new DocumentToolbarItem { Id = "b", Order = 2 },
        };

        var sorted = DocumentToolbarItem.SortByOrder(items).ToList();

        sorted[0].Id.Should().Be("a");
        sorted[1].Id.Should().Be("b");
        sorted[2].Id.Should().Be("c");
    }

    [Fact]
    public void DocumentToolbarItems_StableSortKeepsEqualOrderPositions()
    {
        var items = new[]
        {
            new DocumentToolbarItem { Id = "first",  Order = 5 },
            new DocumentToolbarItem { Id = "second", Order = 5 },
        };

        var sorted = DocumentToolbarItem.SortByOrder(items).ToList();

        sorted[0].Id.Should().Be("first");
        sorted[1].Id.Should().Be("second");
    }

    [Fact]
    public void DocumentToolbarItems_AreSortedByTabGroupAndOrder()
    {
        var items = new[]
        {
            new DocumentToolbarItem { Id = "review", Tab = DocumentToolbarTab.Review, Group = "changes", Order = 1 },
            new DocumentToolbarItem { Id = "format-2", Tab = DocumentToolbarTab.Home, Group = "formatting", Order = 2 },
            new DocumentToolbarItem { Id = "insert", Tab = DocumentToolbarTab.Insert, Group = "insert", Order = 1 },
            new DocumentToolbarItem { Id = "format-1", Tab = DocumentToolbarTab.Home, Group = "formatting", Order = 1 },
        };

        var sorted = DocumentToolbarItem.SortByOrder(items).Select(i => i.Id);

        sorted.Should().Equal("format-1", "format-2", "insert", "review");
    }

    // ─── 3.2 DocumentEditorToolbarRegistry ───────────────────────────────────

    [Fact]
    public void ToolbarRegistry_RegistersItemAndReturnsIt()
    {
        var registry = new DocumentEditorToolbarRegistry();
        var item = new DocumentToolbarItem { Id = "save", CommandName = "save" };

        registry.Register(item);

        registry.GetItems().Should().ContainSingle(i => i.Id == "save");
    }

    [Fact]
    public void ToolbarRegistry_RegistersGroupAndReturnsItOrdered()
    {
        var registry = new DocumentEditorToolbarRegistry();
        registry.RegisterGroup(new DocumentToolbarGroup { Id = "formatting", Tab = DocumentToolbarTab.Home, Order = 2 });
        registry.RegisterGroup(new DocumentToolbarGroup { Id = "clipboard", Tab = DocumentToolbarTab.Home, Order = 1 });

        var groups = registry.GetGroups(DocumentToolbarTab.Home).ToList();

        groups.Select(g => g.Id).Should().Equal("clipboard", "formatting");
    }

    [Fact]
    public void ToolbarRegistry_ReturnsItemsOrderedByOrder()
    {
        var registry = new DocumentEditorToolbarRegistry();
        registry.Register(new DocumentToolbarItem { Id = "redo",  CommandName = "redo",  Order = 3 });
        registry.Register(new DocumentToolbarItem { Id = "undo",  CommandName = "undo",  Order = 2 });
        registry.Register(new DocumentToolbarItem { Id = "save",  CommandName = "save",  Order = 1 });

        var items = registry.GetItems().ToList();

        items[0].Id.Should().Be("save");
        items[1].Id.Should().Be("undo");
        items[2].Id.Should().Be("redo");
    }

    [Fact]
    public void ToolbarRegistry_FiltersItemsByCommandAvailability()
    {
        var commandRegistry = new DocumentEditorCommandRegistry();
        commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "bold", affectsData: true,
            computeEnabled: _ => true,
            execute: (_, _) => Task.CompletedTask));

        var toolbarRegistry = new DocumentEditorToolbarRegistry(commandRegistry);
        toolbarRegistry.Register(new DocumentToolbarItem { Id = "bold",   CommandName = "bold" });
        toolbarRegistry.Register(new DocumentToolbarItem { Id = "italic", CommandName = "italic" });
        toolbarRegistry.Register(new DocumentToolbarItem { Id = "noCmd"                         });

        var available = toolbarRegistry.GetAvailableItems().ToList();

        available.Should().Contain(i => i.Id == "bold",   "bold command is registered");
        available.Should().NotContain(i => i.Id == "italic", "italic command is not registered");
        available.Should().Contain(i => i.Id == "noCmd",  "items without command are always included");
    }

    [Fact]
    public void ToolbarRegistry_FiltersItemsByCommandVisibility()
    {
        var commandRegistry = new DocumentEditorCommandRegistry();
        commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "hidden", affectsData: false,
            computeEnabled: _ => true,
            execute: (_, _) => Task.CompletedTask,
            computeVisible: _ => false));
        commandRegistry.RefreshAllAsync(new DocumentEditorCommandContext()).GetAwaiter().GetResult();

        var toolbarRegistry = new DocumentEditorToolbarRegistry(commandRegistry);
        toolbarRegistry.Register(new DocumentToolbarItem { Id = "hidden", CommandName = "hidden" });
        toolbarRegistry.Register(new DocumentToolbarItem { Id = "custom", VisibleWhen = _ => false });

        var available = toolbarRegistry.GetAvailableItems().ToList();

        available.Should().BeEmpty();
    }

    [Fact]
    public void ToolbarRegistry_HostCanRegisterCustomItem()
    {
        var registry = new DocumentEditorToolbarRegistry();
        var customItem = new DocumentToolbarItem
        {
            Id = "my-plugin-action",
            CommandName = null,
            LabelKey = "MyPlugin_Action"
        };

        registry.Register(customItem);

        registry.GetItems().Should().Contain(i => i.Id == "my-plugin-action");
    }

    [Fact]
    public void ToolbarRegistry_WithoutCommandRegistry_ReturnsAllItems()
    {
        var registry = new DocumentEditorToolbarRegistry();
        registry.Register(new DocumentToolbarItem { Id = "a", CommandName = "some-command" });
        registry.Register(new DocumentToolbarItem { Id = "b", CommandName = null });

        var available = registry.GetAvailableItems().ToList();

        available.Should().HaveCount(2);
    }

    [Fact]
    public void CommandRegistry_UsesCanonicalFormattingUndoAndSelectionTokenStateBindings()
    {
        var commandRegistry = new DocumentEditorCommandRegistry();
        commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "bold", affectsData: true,
            computeEnabled: context => context.HasDocument && !context.FormattingState.IsDisabled,
            computeValue: context => context.FormattingState.Bold switch
            {
                WysiwygFormattingValue.Active => "active",
                WysiwygFormattingValue.Mixed => "mixed",
                _ => "inactive"
            },
            execute: (_, _) => Task.CompletedTask));
        commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "undo", affectsData: true,
            computeEnabled: context => context.UndoState.CanUndo,
            computeValue: context => context.UndoState.NextUndoDescription,
            execute: (_, _) => Task.CompletedTask));
        commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "selectionTokenProbe", affectsData: true,
            computeEnabled: context => !string.IsNullOrWhiteSpace(context.SelectionSnapshot?.SelectionToken),
            computeValue: context => context.SelectionSnapshot?.SelectionToken,
            execute: (_, _) => Task.CompletedTask));

        commandRegistry.RefreshAllAsync(new DocumentEditorCommandContext
        {
            HasDocument = true,
            FormattingState = new WysiwygFormattingState { Bold = WysiwygFormattingValue.Mixed },
            UndoState = new WysiwygUndoState { CanUndo = true, NextUndoDescription = "Typing session" },
            SelectionSnapshot = new WysiwygSelectionSnapshot
            {
                SelectionToken = "stable-selection-token",
                StableSelectionToken = "stable-selection-token"
            }
        }).GetAwaiter().GetResult();

        commandRegistry.GetState("bold")!.Value.Should().Be("mixed");
        commandRegistry.GetState("undo")!.IsEnabled.Should().BeTrue();
        commandRegistry.GetState("undo")!.Value.Should().Be("Typing session");
        commandRegistry.GetState("selectionTokenProbe")!.IsEnabled.Should().BeTrue();
        commandRegistry.GetState("selectionTokenProbe")!.Value.Should().Be("stable-selection-token");
    }

    [Fact]
    public void ToolbarRenderContext_CarriesSelectionTokenValueForDeclarativeRenderers()
    {
        var item = new DocumentToolbarItem { Id = "token-aware", CommandName = "bold" };
        var values = new Dictionary<string, object?>
        {
            ["SelectionToken"] = "stable-selection-token",
            ["FormattingState"] = new WysiwygFormattingState { Bold = WysiwygFormattingValue.Active },
            ["UndoState"] = new WysiwygUndoState { CanUndo = true }
        };

        var context = new DocumentToolbarRenderContext(item, values);

        context.Values.Should().NotBeNull();
        context.Values!["SelectionToken"].Should().Be("stable-selection-token");
        context.Values["FormattingState"].Should().BeOfType<WysiwygFormattingState>()
            .Which.Bold.Should().Be(WysiwygFormattingValue.Active);
        context.Values["UndoState"].Should().BeOfType<WysiwygUndoState>()
            .Which.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void ToolbarComponentFactory_DefaultFactoryResolvesCoreRenderers()
    {
        var factory = DocumentToolbarComponentFactory.CreateDefault();

        factory.GetRenderer(DocumentToolbarItemKind.Button).Should().BeOfType<DocumentToolbarButtonRenderer>();
        factory.GetRenderer(DocumentToolbarItemKind.Toggle).Should().BeOfType<DocumentToolbarToggleRenderer>();
        factory.GetRenderer(DocumentToolbarItemKind.Select).Should().BeOfType<DocumentToolbarSelectRenderer>();
        factory.GetRenderer(DocumentToolbarItemKind.ColorPicker).Should().BeOfType<DocumentToolbarColorPickerRenderer>();
        factory.GetRenderer(DocumentToolbarItemKind.GridPicker).Should().BeOfType<DocumentToolbarGridPickerRenderer>();
    }

    [Fact]
    public void ToolbarComponentFactory_MissingRendererThrowsClearError()
    {
        var factory = new DocumentToolbarComponentFactory();

        var act = () => factory.GetRenderer(DocumentToolbarItemKind.Button);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Button*renderer*");
    }

    [Fact]
    public void ToolbarComponentFactory_RegisteredRendererCanRenderFragment()
    {
        var factory = new DocumentToolbarComponentFactory();
        var renderer = new TestToolbarRenderer();
        var item = new DocumentToolbarItem { Id = "plugin", Kind = DocumentToolbarItemKind.Button };
        factory.Register(renderer);

        var fragment = factory.Render(new DocumentToolbarRenderContext(item));

        fragment.Should().NotBeNull();
        renderer.LastItem.Should().Be(item);
    }

    private sealed class TestToolbarRenderer : IDocumentToolbarItemRenderer
    {
        public DocumentToolbarItemKind Kind => DocumentToolbarItemKind.Button;

        public DocumentToolbarItem? LastItem { get; private set; }

        public RenderFragment Render(DocumentToolbarRenderContext context)
        {
            LastItem = context.Item;
            return builder => builder.AddContent(0, context.Item.Id);
        }
    }
}
