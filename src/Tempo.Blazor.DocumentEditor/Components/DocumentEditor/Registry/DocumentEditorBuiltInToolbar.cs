namespace Tempo.Blazor.Components.DocumentEditor.Registry;

/// <summary>Declarative metadata for the built-in document editor toolbar.</summary>
public static class DocumentEditorBuiltInToolbar
{
    private static readonly DocumentToolbarGroup[] Groups =
    [
        new() { Id = "clipboard", Tab = DocumentToolbarTab.Home, LabelKey = "TmDocumentEditor_GroupClipboard", Order = 10 },
        new() { Id = "style", Tab = DocumentToolbarTab.Home, LabelKey = "TmDocumentEditor_GroupStyle", Order = 20 },
        new() { Id = "character", Tab = DocumentToolbarTab.Home, LabelKey = "TmDocumentEditor_GroupCharacter", Order = 25 },
        new() { Id = "paragraph", Tab = DocumentToolbarTab.Home, LabelKey = "TmDocumentEditor_GroupParagraph", Order = 30 },
        new() { Id = "actions", Tab = DocumentToolbarTab.Home, LabelKey = "TmDocumentEditor_GroupInsert", Order = 40 },
        new() { Id = "moreFormatting", Tab = DocumentToolbarTab.Home, LabelKey = "TmDocumentEditor_GroupMoreFormatting", Order = 50 },
        new() { Id = "insert", Tab = DocumentToolbarTab.Insert, LabelKey = "TmDocumentEditor_GroupInsert", Order = 10 },
        new() { Id = "signing", Tab = DocumentToolbarTab.Insert, LabelKey = "TmDocumentEditor_GroupSigning", Order = 20 },
        new() { Id = "math", Tab = DocumentToolbarTab.Math, LabelKey = "TmDocumentEditor_GroupMath", Order = 10 },
        new() { Id = "layout", Tab = DocumentToolbarTab.Layout, LabelKey = "TmDocumentEditor_GroupLayout", Order = 10 },
        new() { Id = "references", Tab = DocumentToolbarTab.References, LabelKey = "TmDocumentEditor_GroupReferences", Order = 10 },
        new() { Id = "review", Tab = DocumentToolbarTab.Review, LabelKey = "TmDocumentEditor_GroupReview", Order = 10 },
        new() { Id = "view", Tab = DocumentToolbarTab.View, LabelKey = "TmDocumentEditor_TabView", Order = 10 },
        new() { Id = "file", Tab = DocumentToolbarTab.View, LabelKey = "TmDocumentEditor_GroupFile", Order = 20 },
        new() { Id = "headerFooter", Tab = DocumentToolbarTab.HeaderFooter, LabelKey = "TmDocumentEditor_TabHeaderFooter", Order = 10 },
    ];

    private static readonly DocumentToolbarItem[] Items =
    [
        Item("save", "save", "save", "TmDocumentEditor_Save", DocumentToolbarItemKind.Button, DocumentToolbarTab.Home, "clipboard", 10, ToolbarItemPriority.Primary),
        Item("undo", "undo", "undo-2", "TmDocumentEditor_Undo", DocumentToolbarItemKind.Button, DocumentToolbarTab.Home, "clipboard", 20, ToolbarItemPriority.Primary),
        Item("redo", "redo", "redo-2", "TmDocumentEditor_Redo", DocumentToolbarItemKind.Button, DocumentToolbarTab.Home, "clipboard", 30, ToolbarItemPriority.Primary),
        Item("bold", "bold", "bold", "TmDocumentEditor_Bold", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "character", 10, ToolbarItemPriority.Primary),
        Item("italic", "italic", "italic", "TmDocumentEditor_Italic", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "character", 20, ToolbarItemPriority.Primary),
        Item("underline", "underline", "underline", "TmDocumentEditor_Underline", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "character", 30, ToolbarItemPriority.Primary),
        Item("fontFamily", "fontFamily", "type", "TmDocumentEditor_FontFamily", DocumentToolbarItemKind.Select, DocumentToolbarTab.Home, "style", 40, ToolbarItemPriority.Secondary),
        Item("fontSize", "fontSize", "a-large-small", "TmDocumentEditor_FontSize", DocumentToolbarItemKind.Select, DocumentToolbarTab.Home, "style", 50, ToolbarItemPriority.Secondary),
        Item("textColor", "textColor", "paintbrush", "TmDocumentEditor_TextColor", DocumentToolbarItemKind.ColorPicker, DocumentToolbarTab.Home, "character", 60, ToolbarItemPriority.Secondary),
        Item("highlightColor", "highlightColor", "highlighter", "TmDocumentEditor_HighlightColor", DocumentToolbarItemKind.ColorPicker, DocumentToolbarTab.Home, "character", 70, ToolbarItemPriority.Secondary),
        Item("clearFormatting", "clearFormatting", "eraser", "TmDocumentEditor_ClearFormatting", DocumentToolbarItemKind.Button, DocumentToolbarTab.Home, "paragraph", 80, ToolbarItemPriority.Secondary),
        Item("link", "link", "link", "TmDocumentEditor_Link", DocumentToolbarItemKind.Button, DocumentToolbarTab.Home, "actions", 10, ToolbarItemPriority.Primary),
        Item("alignLeft", "alignLeft", "align-left", "TmDocumentEditor_AlignLeft", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "paragraph", 10, ToolbarItemPriority.Secondary),
        Item("alignCenter", "alignCenter", "align-center", "TmDocumentEditor_AlignCenter", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "paragraph", 20, ToolbarItemPriority.Secondary),
        Item("alignRight", "alignRight", "align-right", "TmDocumentEditor_AlignRight", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "paragraph", 30, ToolbarItemPriority.Secondary),
        Item("alignJustify", "alignJustify", "align-justify", "TmDocumentEditor_AlignJustify", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "paragraph", 40, ToolbarItemPriority.Secondary),
        Item("lineSpacing", "lineSpacing", "pilcrow", "TmDocumentEditor_LineSpacing", DocumentToolbarItemKind.Select, DocumentToolbarTab.Home, "paragraph", 50, ToolbarItemPriority.Secondary),
        Item("spacingBefore", "spacingBefore", "arrow-up-from-line", "TmDocumentEditor_SpacingBefore", DocumentToolbarItemKind.Select, DocumentToolbarTab.Home, "moreFormatting", 60, ToolbarItemPriority.OverflowOnly),
        Item("spacingAfter", "spacingAfter", "arrow-down-to-line", "TmDocumentEditor_SpacingAfter", DocumentToolbarItemKind.Select, DocumentToolbarTab.Home, "moreFormatting", 70, ToolbarItemPriority.OverflowOnly),
        Item("increaseIndent", "increaseIndent", "indent-increase", "TmDocumentEditor_IncreaseIndent", DocumentToolbarItemKind.Button, DocumentToolbarTab.Home, "paragraph", 80, ToolbarItemPriority.Secondary),
        Item("decreaseIndent", "decreaseIndent", "indent-decrease", "TmDocumentEditor_DecreaseIndent", DocumentToolbarItemKind.Button, DocumentToolbarTab.Home, "paragraph", 90, ToolbarItemPriority.Secondary),
        Item("strikethrough", "strikethrough", "strikethrough", "TmDocumentEditor_Strikethrough", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "character", 35, ToolbarItemPriority.Primary),
        Item("bulletList", "bulletList", "list", "TmDocumentEditor_BulletedList", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "paragraph", 75, ToolbarItemPriority.Secondary),
        Item("numberedList", "numberedList", "list-ordered", "TmDocumentEditor_NumberedList", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "paragraph", 76, ToolbarItemPriority.Secondary),
        Item("superscript", "superscript", "superscript", "TmDocumentEditor_Superscript", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "moreFormatting", 10, ToolbarItemPriority.OverflowOnly),
        Item("subscript", "subscript", "subscript", "TmDocumentEditor_Subscript", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "moreFormatting", 20, ToolbarItemPriority.OverflowOnly),
        Item("smallCaps", "smallCaps", "case-sensitive", "TmDocumentEditor_SmallCaps", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "moreFormatting", 30, ToolbarItemPriority.OverflowOnly),
        Item("allCaps", "allCaps", "case-upper", "TmDocumentEditor_AllCaps", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "moreFormatting", 40, ToolbarItemPriority.OverflowOnly),
        Item("doubleStrikethrough", "doubleStrikethrough", "double-strikethrough", "TmDocumentEditor_DoubleStrikethrough", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Home, "moreFormatting", 45, ToolbarItemPriority.OverflowOnly),
        Item("decreaseFontSize", "decreaseFontSize", "minus", "TmDocumentEditor_DecreaseFontSize", DocumentToolbarItemKind.Button, DocumentToolbarTab.Home, "moreFormatting", 46, ToolbarItemPriority.OverflowOnly),
        Item("increaseFontSize", "increaseFontSize", "plus", "TmDocumentEditor_IncreaseFontSize", DocumentToolbarItemKind.Button, DocumentToolbarTab.Home, "moreFormatting", 47, ToolbarItemPriority.OverflowOnly),
        Item("changeCase", "changeCase", "case-sensitive", "TmDocumentEditor_ChangeCase", DocumentToolbarItemKind.Select, DocumentToolbarTab.Home, "moreFormatting", 50, ToolbarItemPriority.OverflowOnly),
        Item("insertTable", "insertTable", "table", "TmDocumentEditor_InsertTable", DocumentToolbarItemKind.GridPicker, DocumentToolbarTab.Insert, "insert", 10, ToolbarItemPriority.Primary),
        Item("insertImage", "insertImage", "image", "TmDocumentEditor_InsertImage", DocumentToolbarItemKind.Button, DocumentToolbarTab.Insert, "insert", 20, ToolbarItemPriority.Primary),
        Item("insertEquation", "insertEquation", "sigma", "TmDocumentEditor_InsertEquation", DocumentToolbarItemKind.Button, DocumentToolbarTab.Insert, "insert", 30, ToolbarItemPriority.Secondary),
        Item("insertSymbol", "insertSymbol", "omega", "TmDocumentEditor_InsertSymbol", DocumentToolbarItemKind.Button, DocumentToolbarTab.Insert, "insert", 35, ToolbarItemPriority.Secondary),
        Item("insertPageBreak", "insertPageBreak", "between-horizontal-start", "TmDocumentEditor_PageBreak", DocumentToolbarItemKind.Button, DocumentToolbarTab.Insert, "insert", 40, ToolbarItemPriority.Secondary),
        // Záměrně sdílí příkaz insertEquation s Insert tabem (jeden příkaz, dvě umístění).
        // FindItemByCommandName je first-match → deterministicky vrací Insert-tab item (Id ==
        // CommandName), takže overflow menu ukáže příkaz právě jednou s Insert metadaty.
        Item("mathInsertEquation", "insertEquation", "sigma", "TmDocumentEditor_InsertEquation", DocumentToolbarItemKind.Button, DocumentToolbarTab.Math, "math", 10, ToolbarItemPriority.Primary),
        Item("insertMenu", "insertMenu", "plus", "TmDocumentEditor_Insert", DocumentToolbarItemKind.Button, DocumentToolbarTab.Insert, "insert", 5, ToolbarItemPriority.Secondary),
        Item("insertSigningField", "insertSigningField", "signature", "TmDocumentEditor_InsertSigningField", DocumentToolbarItemKind.Button, DocumentToolbarTab.Insert, "signing", 10, ToolbarItemPriority.Secondary),
        Item("toggleNonPrintingCharacters", "toggleNonPrintingCharacters", "pilcrow", "TmDocumentEditor_NonPrintingCharacters", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.View, "view", 35, ToolbarItemPriority.Secondary),
        Item("insertFootnote", "insertFootnote", "list-plus", "TmDocumentEditor_InsertFootnote", DocumentToolbarItemKind.Button, DocumentToolbarTab.References, "references", 10, ToolbarItemPriority.Secondary),
        Item("insertEndnote", "insertEndnote", "list-end", "TmDocumentEditor_InsertEndnote", DocumentToolbarItemKind.Button, DocumentToolbarTab.References, "references", 20, ToolbarItemPriority.Secondary),
        Item("insertCaption", "insertCaption", "captions", "TmDocumentEditor_InsertCaption", DocumentToolbarItemKind.Button, DocumentToolbarTab.References, "references", 30, ToolbarItemPriority.Secondary),
        Item("insertCrossReference", "insertCrossReference", "scan-line", "TmDocumentEditor_CrossReference", DocumentToolbarItemKind.Button, DocumentToolbarTab.References, "references", 40, ToolbarItemPriority.Secondary),
        Item("insertTableOfContents", "insertTableOfContents", "list", "TmDocumentEditor_TableOfContents", DocumentToolbarItemKind.Button, DocumentToolbarTab.References, "references", 50, ToolbarItemPriority.Secondary),
        Item("insertTableOfFigures", "insertTableOfFigures", "list-tree", "TmDocumentEditor_TableOfFigures", DocumentToolbarItemKind.Button, DocumentToolbarTab.References, "references", 60, ToolbarItemPriority.Secondary),
        Item("insertBibliography", "insertBibliography", "book-open", "TmDocumentEditor_Bibliography", DocumentToolbarItemKind.Button, DocumentToolbarTab.References, "references", 70, ToolbarItemPriority.Secondary),
        Item("updateAllFields", "updateAllFields", "refresh-ccw", "TmDocumentEditor_UpdateFields", DocumentToolbarItemKind.Button, DocumentToolbarTab.References, "references", 80, ToolbarItemPriority.Secondary),
        Item("trackChanges", "trackChanges", "history", "TmDocumentEditor_TrackChanges", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Review, "review", 10, ToolbarItemPriority.Primary),
        Item("reviewDisplayMode", "reviewDisplayMode", "eye", "TmDocumentEditor_ShowMarkup", DocumentToolbarItemKind.Select, DocumentToolbarTab.Review, "review", 20, ToolbarItemPriority.Secondary),
        Item("addComment", "addComment", "message-square-plus", "TmDocumentEditor_AddComment", DocumentToolbarItemKind.Button, DocumentToolbarTab.Review, "review", 30, ToolbarItemPriority.Primary),
        Item("openComments", "openComments", "message-square", "TmDocumentEditor_Comments", DocumentToolbarItemKind.Button, DocumentToolbarTab.Review, "review", 40, ToolbarItemPriority.Secondary),
        Item("openRevisions", "openRevisions", "git-compare", "TmDocumentEditor_Revisions", DocumentToolbarItemKind.Button, DocumentToolbarTab.Review, "review", 50, ToolbarItemPriority.Secondary),
        Item("compareDocuments", "compareDocuments", "columns-3", "TmDocumentEditor_CompareDocuments", DocumentToolbarItemKind.Button, DocumentToolbarTab.Review, "review", 60, ToolbarItemPriority.Secondary),
        Item("protectDocument", "protectDocument", "shield", "TmDocumentEditor_ProtectDocument", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.Review, "review", 70, ToolbarItemPriority.Secondary),
        Item("markEditableRegion", "markEditableRegion", "scan-text", "TmDocumentEditor_MarkEditableRegion", DocumentToolbarItemKind.Button, DocumentToolbarTab.Review, "review", 80, ToolbarItemPriority.Secondary),
        Item("showRuler", "showRuler", "ruler", "TmDocumentEditor_ShowRuler", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.View, "view", 10, ToolbarItemPriority.Primary),
        Item("zoomPageWidth", "zoomPageWidth", "panel-top", "TmDocumentEditor_PageWidth", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "view", 20, ToolbarItemPriority.Secondary),
        Item("resetZoom", "resetZoom", "scan", "TmDocumentEditor_ZoomAutomatic", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "view", 25, ToolbarItemPriority.Secondary),
        Item("showBlocks", "showBlocks", "layout-panel-top", "TmDocumentEditor_ShowBlocks", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.View, "view", 30, ToolbarItemPriority.Secondary),
        Item("fullscreen", "fullscreen", "maximize", "TmDocumentEditor_Fullscreen", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.View, "view", 40, ToolbarItemPriority.Secondary),
        Item("openPrintPreview", "openPrintPreview", "book-open", "TmDocumentEditor_PrintPreview", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.View, "view", 50, ToolbarItemPriority.Secondary),
        Item("printDocument", "printDocument", "printer", "TmDocumentEditor_PrintDocument", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "view", 60, ToolbarItemPriority.Secondary),
        Item("viewDocumentJson", "viewDocumentJson", "braces", "TmDocumentEditor_ViewJson", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "file", 10, ToolbarItemPriority.OverflowOnly),
        Item("viewClipboardHtml", "viewClipboardHtml", "clipboard-list", "TmDocumentEditor_ViewClipboardHtml", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "file", 20, ToolbarItemPriority.OverflowOnly),
        Item("exportPdf", "exportPdf", "file-type", "TmDocumentEditor_ExportPdf", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "file", 30, ToolbarItemPriority.Secondary),
        Item("importDocx", "importDocx", "upload", "TmDocumentEditor_ImportDocx", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "file", 40, ToolbarItemPriority.Secondary),
        Item("exportDocx", "exportDocx", "file-text", "TmDocumentEditor_ExportDocx", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "file", 50, ToolbarItemPriority.Secondary),
        Item("openVersions", "openVersions", "archive", "TmDocumentEditor_Versions", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "file", 60, ToolbarItemPriority.Secondary),
        Item("insertPageNumber", "insertPageNumber", "hash", "TmDocumentEditor_InsertPageNumber", DocumentToolbarItemKind.Button, DocumentToolbarTab.HeaderFooter, "headerFooter", 10, ToolbarItemPriority.Primary, context => context.IsHeaderFooterMode),
        Item("insertPageCount", "insertPageCount", "files", "TmDocumentEditor_InsertPageCount", DocumentToolbarItemKind.Button, DocumentToolbarTab.HeaderFooter, "headerFooter", 20, ToolbarItemPriority.Primary, context => context.IsHeaderFooterMode),
        Item("insertPageXOfY", "insertPageXOfY", "file-stack", "TmDocumentEditor_InsertPageXOfY", DocumentToolbarItemKind.Button, DocumentToolbarTab.HeaderFooter, "headerFooter", 30, ToolbarItemPriority.Primary, context => context.IsHeaderFooterMode),
        Item("insertDateField", "insertDateField", "calendar-days", "TmDocumentEditor_InsertDateField", DocumentToolbarItemKind.Button, DocumentToolbarTab.HeaderFooter, "headerFooter", 40, ToolbarItemPriority.Secondary, context => context.IsHeaderFooterMode),
        Item("insertDocumentTitleField", "insertDocumentTitleField", "file-text", "TmDocumentEditor_InsertDocumentTitleField", DocumentToolbarItemKind.Button, DocumentToolbarTab.HeaderFooter, "headerFooter", 50, ToolbarItemPriority.Secondary, context => context.IsHeaderFooterMode),
        Item("differentFirstPage", "differentFirstPage", "file-stack", "TmDocumentEditor_DifferentFirstPage", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.HeaderFooter, "headerFooter", 60, ToolbarItemPriority.Primary, context => context.IsHeaderFooterMode),
        Item("differentOddEven", "differentOddEven", "layout", "TmDocumentEditor_DifferentOddEven", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.HeaderFooter, "headerFooter", 70, ToolbarItemPriority.Primary, context => context.IsHeaderFooterMode),
        Item("closeHeaderFooter", "closeHeaderFooter", "x", "TmDocumentEditor_CloseHeaderFooter", DocumentToolbarItemKind.Button, DocumentToolbarTab.HeaderFooter, "headerFooter", 80, ToolbarItemPriority.Primary, context => context.IsHeaderFooterMode),
    ];

    /// <summary>
    /// Ribbon-local commands: controls that open a popup or act on the toolbar's own state and have no editor-registry command
    /// (the block-style select, the style gallery, the zoom steps, the template preview toggle, the header/footer menus, the page
    /// layout inspector). They are not part of <see cref="DefaultItems"/> (the declarative registry only lists registered
    /// commands) but they publish the same <c>data-priority</c> contract, so <see cref="FindItemByCommandName"/> resolves them too.
    /// </summary>
    private static readonly DocumentToolbarItem[] RibbonLocalItems =
    [
        Item("blockStyle", "blockStyle", "heading", "TmDocumentEditor_BlockStyle", DocumentToolbarItemKind.Select, DocumentToolbarTab.Home, "style", 5, ToolbarItemPriority.Secondary),
        Item("alignment", "alignment", "align-left", "TmDocumentEditor_GroupAlignment", DocumentToolbarItemKind.Menu, DocumentToolbarTab.Home, "paragraph", 5, ToolbarItemPriority.Secondary),
        Item("styleGallery", "styleGallery", "gallery-horizontal-end", "TmDocumentEditor_StyleGallery", DocumentToolbarItemKind.Button, DocumentToolbarTab.Home, "style", 6, ToolbarItemPriority.OverflowOnly),
        Item("pageLayout", "pageLayout", "panel-top", "TmDocumentEditor_PageLayout", DocumentToolbarItemKind.Button, DocumentToolbarTab.Layout, "layout", 10, ToolbarItemPriority.Primary),
        Item("zoomOut", "zoomOut", "minus", "TmDocumentEditor_ZoomOut", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "view", 12, ToolbarItemPriority.Secondary),
        Item("zoomReset", "zoomReset", "scan-line", "TmDocumentEditor_Zoom100", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "view", 14, ToolbarItemPriority.Secondary),
        Item("zoomIn", "zoomIn", "plus", "TmDocumentEditor_ZoomIn", DocumentToolbarItemKind.Button, DocumentToolbarTab.View, "view", 16, ToolbarItemPriority.Secondary),
        Item("templatePreview", "templatePreview", "eye", "TmDocumentEditor_TemplatePreview", DocumentToolbarItemKind.Toggle, DocumentToolbarTab.View, "file", 5, ToolbarItemPriority.Secondary),
        Item("insertField", "insertField", "braces", "TmDocumentEditor_InsertField", DocumentToolbarItemKind.Button, DocumentToolbarTab.HeaderFooter, "headerFooter", 35, ToolbarItemPriority.Primary, context => context.IsHeaderFooterMode),
        Item("headerFooterPresets", "headerFooterPresets", "layout-template", "TmDocumentEditor_HeaderFooterPresets", DocumentToolbarItemKind.Button, DocumentToolbarTab.HeaderFooter, "headerFooter", 36, ToolbarItemPriority.Secondary, context => context.IsHeaderFooterMode),
    ];

    /// <summary>The ribbon-local commands (no editor-registry command behind them) and their declared priority.</summary>
    public static IReadOnlyList<DocumentToolbarItem> RibbonLocalCommands => RibbonLocalItems;

    /// <summary>Built-in toolbar groups in their default order.</summary>
    public static IReadOnlyList<DocumentToolbarGroup> DefaultGroups => Groups;

    /// <summary>Built-in toolbar items in their default metadata form.</summary>
    public static IReadOnlyList<DocumentToolbarItem> DefaultItems => Items;

    /// <summary>Creates a toolbar registry preloaded with the built-in groups and items.</summary>
    public static DocumentEditorToolbarRegistry CreateRegistry(DocumentEditorCommandRegistry? commandRegistry = null)
    {
        var registry = new DocumentEditorToolbarRegistry(commandRegistry);
        foreach (var group in Groups)
        {
            registry.RegisterGroup(group);
        }

        foreach (var item in Items)
        {
            registry.Register(item);
        }

        return registry;
    }

    // Perf plan N7.2: the lookups ran a linear scan over ~60 items per call (the overflow menu calls
    // them per item per rebuild). Both dictionaries preserve the historical first-match semantics.
    private static readonly Dictionary<string, DocumentToolbarItem> ItemsByCommandName = BuildItemsByCommandName();
    private static readonly Dictionary<string, DocumentToolbarGroup> GroupsById = BuildGroupsById();

    private static Dictionary<string, DocumentToolbarItem> BuildItemsByCommandName()
    {
        var map = new Dictionary<string, DocumentToolbarItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items.Concat(RibbonLocalItems))
        {
            if (!string.IsNullOrWhiteSpace(item.CommandName))
            {
                map.TryAdd(item.CommandName, item);
            }
        }

        return map;
    }

    private static Dictionary<string, DocumentToolbarGroup> BuildGroupsById()
    {
        var map = new Dictionary<string, DocumentToolbarGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Groups)
        {
            if (!string.IsNullOrWhiteSpace(group.Id))
            {
                map.TryAdd(group.Id, group);
            }
        }

        return map;
    }

    /// <summary>Finds built-in metadata for the given command name.</summary>
    public static DocumentToolbarItem? FindItemByCommandName(string commandName) =>
        commandName is { Length: > 0 } && ItemsByCommandName.TryGetValue(commandName, out var item) ? item : null;

    /// <summary>Finds built-in group metadata for the given group id.</summary>
    public static DocumentToolbarGroup? FindGroup(string? groupId) =>
        !string.IsNullOrWhiteSpace(groupId) && GroupsById.TryGetValue(groupId, out var group) ? group : null;

    private static DocumentToolbarItem Item(
        string id,
        string? commandName,
        string? icon,
        string labelKey,
        DocumentToolbarItemKind kind,
        DocumentToolbarTab tab,
        string group,
        int order,
        ToolbarItemPriority priority,
        Func<DocumentToolbarVisibilityContext, bool>? visibleWhen = null) =>
        new()
        {
            Id = id,
            CommandName = commandName,
            Icon = icon,
            LabelKey = labelKey,
            Kind = kind,
            Tab = tab,
            Group = group,
            GroupId = group,
            Order = order,
            Priority = priority,
            VisibleWhen = visibleWhen
        };
}
