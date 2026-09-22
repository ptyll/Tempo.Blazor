using Microsoft.AspNetCore.Components.Forms;
using System.Globalization;
using Tempo.Blazor.Components.DocumentEditor.Features;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.DocumentEditor.Models;

namespace Tempo.Blazor.Components.DocumentEditor;

public partial class TmDocumentEditor
{
    private readonly DocumentEditorCommandRegistry _commandRegistry = new();

    private void InitializeCommandRegistry()
    {
        // ── Formatting ──────────────────────────────────────────────────────
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "bold", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => FormattingValueToString(ctx.FormattingState.Bold),
            execute: (_, _) => ToggleInlineMarkAsync(InlineMarkType.Bold),
            descriptionKey: "TmDocumentEditor_Bold",
            tooltipKey: "TmDocumentEditor_Bold",
            category: "Formatting",
            defaultShortcut: "Ctrl+B",
            icon: "bold"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "italic", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => FormattingValueToString(ctx.FormattingState.Italic),
            execute: (_, _) => ToggleInlineMarkAsync(InlineMarkType.Italic),
            descriptionKey: "TmDocumentEditor_Italic",
            tooltipKey: "TmDocumentEditor_Italic",
            category: "Formatting",
            defaultShortcut: "Ctrl+I",
            icon: "italic"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "underline", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => FormattingValueToString(ctx.FormattingState.Underline),
            execute: (_, _) => ToggleInlineMarkAsync(InlineMarkType.Underline),
            descriptionKey: "TmDocumentEditor_Underline",
            tooltipKey: "TmDocumentEditor_Underline",
            category: "Formatting",
            defaultShortcut: "Ctrl+U",
            icon: "underline"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "strikethrough", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => FormattingValueToString(ctx.FormattingState.Strikethrough),
            execute: (_, _) => ToggleInlineMarkAsync(InlineMarkType.Strikethrough),
            descriptionKey: "TmDocumentEditor_Strikethrough",
            tooltipKey: "TmDocumentEditor_Strikethrough",
            category: "Formatting",
            defaultShortcut: "Alt+Shift+5",
            icon: "strikethrough"));

        // ── Document lifecycle ───────────────────────────────────────────────
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "save", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && Provider is not null && !ctx.IsSaving,
            execute: (_, _) => SaveAsync(),
            descriptionKey: "TmDocumentEditor_Save",
            tooltipKey: "TmDocumentEditor_Save",
            category: "File",
            defaultShortcut: "Ctrl+S",
            icon: "save",
            disabledReasonKey: "TmDocumentEditor_CommandDisabledBusy"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "undo", affectsData: true,
            computeEnabled: ctx => ctx.UndoState.CanUndo,
            execute: (_, _) => UndoAsync(),
            computeValue: ctx => ctx.UndoState.NextUndoDescription,
            descriptionKey: "TmDocumentEditor_Undo",
            tooltipKey: "TmDocumentEditor_Undo",
            category: "Edit",
            defaultShortcut: "Ctrl+Z",
            icon: "undo-2"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "redo", affectsData: true,
            computeEnabled: ctx => ctx.UndoState.CanRedo,
            execute: (_, _) => RedoAsync(),
            computeValue: ctx => ctx.UndoState.NextRedoDescription,
            descriptionKey: "TmDocumentEditor_Redo",
            tooltipKey: "TmDocumentEditor_Redo",
            category: "Edit",
            defaultShortcut: "Ctrl+Y",
            icon: "redo-2"));

        // ── Insert ───────────────────────────────────────────────────────────
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "link", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, payload) =>
            {
                if (payload is WysiwygLinkPayload linkPayload)
                {
                    return ApplyLinkAsync(linkPayload);
                }

                return _toolbar?.OpenLinkDialogAsync() ?? Task.CompletedTask;
            },
            descriptionKey: "TmDocumentEditor_Link",
            tooltipKey: "TmDocumentEditor_Link",
            category: "Insert",
            defaultShortcut: "Ctrl+K",
            icon: "link"));

        // Signing fields (plan S2.19): visible only when the canvas engine is active and signer roles
        // are configured, so the toolbar group has zero impact on the default editor.
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "insertSigningField", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeVisible: _ => EffectiveRenderEngine == DocumentEditorRenderEngine.CanvasEnginePreview && SigningRoles.Count > 0,
            execute: (_, _) => InsertSigningFieldFromToolbarAsync(),
            descriptionKey: "TmDocumentEditor_InsertSigningField",
            tooltipKey: "TmDocumentEditor_InsertSigningField",
            category: "Signing",
            icon: "signature"));

        if (IsFeatureEnabled(DocumentEditorFeatureNames.Table))
        {
            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "insertTable", affectsData: true,
                computeEnabled: ctx => ctx.HasDocument,
                execute: (_, _) => InsertTableAsync(2, 2),
                descriptionKey: "TmDocumentEditor_InsertTable",
                tooltipKey: "TmDocumentEditor_InsertTable",
                category: "Insert",
                icon: "table"));

            RegisterTableRuntimeCommand("insertTableRowBefore", "TmDocumentEditor_InsertRowBefore", "panel-top-open");
            RegisterTableRuntimeCommand("insertTableRowAfter", "TmDocumentEditor_InsertRow", "panel-bottom-open");
            RegisterTableRuntimeCommand("insertTableColumnBefore", "TmDocumentEditor_InsertColumnBefore", "panel-left-open");
            RegisterTableRuntimeCommand("insertTableColumnAfter", "TmDocumentEditor_InsertColumn", "panel-right-open");
            RegisterTableRuntimeCommand("deleteTableRow", "TmDocumentEditor_DeleteRow", "delete");
            RegisterTableRuntimeCommand("deleteTableColumn", "TmDocumentEditor_DeleteColumn", "delete");
            RegisterTableRuntimeCommand("deleteTable", "TmDocumentEditor_DeleteTable", "trash-2");
            RegisterTableRuntimeCommand("mergeTableCells", "TmDocumentEditor_MergeCells", "combine");
            RegisterTableRuntimeCommand("splitTableCell", "TmDocumentEditor_SplitCell", "split-square-horizontal");
            // The engine has no tableProperties/cellProperties commands (mutations are
            // setTableProperties/setCellProperties issued BY the panel) — like the table context
            // menu, the ribbon entries open the Properties side panel.
            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "tableProperties", affectsData: false,
                computeEnabled: ctx => ctx.HasDocument && HasActiveTable(ctx),
                execute: (ctx, _) => OpenTablePropertiesPanelFromCommandAsync(ctx),
                descriptionKey: "TmDocumentEditor_TableProperties",
                tooltipKey: "TmDocumentEditor_TableProperties",
                category: "Table",
                icon: "table-properties"));
            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "cellProperties", affectsData: false,
                computeEnabled: ctx => ctx.HasDocument && HasActiveTable(ctx),
                execute: (ctx, _) => OpenTablePropertiesPanelFromCommandAsync(ctx),
                descriptionKey: "TmDocumentEditor_CellProperties",
                tooltipKey: "TmDocumentEditor_CellProperties",
                category: "Table",
                icon: "square"));
        }

        if (IsFeatureEnabled(DocumentEditorFeatureNames.Image))
        {
            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "insertImage", affectsData: true,
                computeEnabled: ctx => ctx.HasDocument,
                execute: (_, _) => OpenImageDialogAsync(),
                descriptionKey: "TmDocumentEditor_InsertImage",
                tooltipKey: "TmDocumentEditor_InsertImage",
                category: "Insert",
                icon: "image"));

            // The engine has no replaceImage/setImageLink commands (the mutation is setImageUrl,
            // issued by the image inspector's URL field) — the ribbon entries open the Properties
            // side panel, which shows the image inspector for the active image.
            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "replaceImage", affectsData: false,
                computeEnabled: ctx => ctx.HasDocument && HasActiveImage(ctx),
                execute: (_, _) => OpenImagePropertiesPanelFromCommandAsync(),
                descriptionKey: "TmDocumentEditor_ReplaceImage",
                tooltipKey: "TmDocumentEditor_ReplaceImage",
                category: "Image",
                icon: "image-up"));

            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "setImageAltText", affectsData: true,
                computeEnabled: ctx => ctx.HasDocument && HasActiveImage(ctx),
                execute: (_, payload) => ExecuteImageRuntimeCommandAsync("setImageAltText", payload),
                descriptionKey: "TmDocumentEditor_ImageAltText",
                tooltipKey: "TmDocumentEditor_ImageAltText",
                category: "Image",
                icon: "text-cursor-input"));

            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "setImageDecorative", affectsData: true,
                computeEnabled: ctx => ctx.HasDocument && HasActiveImage(ctx),
                execute: (_, payload) => ExecuteImageRuntimeCommandAsync("setImageDecorative", payload),
                descriptionKey: "TmDocumentEditor_ImageDecorative",
                tooltipKey: "TmDocumentEditor_ImageDecorative",
                category: "Image",
                icon: "accessibility"));

            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "toggleImageCaption", affectsData: true,
                computeEnabled: ctx => ctx.HasDocument && HasActiveImage(ctx),
                execute: (_, _) => ExecuteImageRuntimeCommandAsync("toggleImageCaption"),
                descriptionKey: "TmDocumentEditor_ImageCaption",
                tooltipKey: "TmDocumentEditor_ImageCaption",
                category: "Image",
                icon: "captions"));

            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "setImageLink", affectsData: false,
                computeEnabled: ctx => ctx.HasDocument && HasActiveImage(ctx),
                execute: (_, _) => OpenImagePropertiesPanelFromCommandAsync(),
                descriptionKey: "TmDocumentEditor_ImageLink",
                tooltipKey: "TmDocumentEditor_ImageLink",
                category: "Image",
                icon: "link"));

            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "setImageWrapMode", affectsData: true,
                computeEnabled: ctx => ctx.HasDocument && HasActiveImage(ctx),
                execute: (_, payload) => ExecuteImageRuntimeCommandAsync("setImageWrapMode", payload),
                descriptionKey: "TmDocumentEditor_ImageWrapMode",
                tooltipKey: "TmDocumentEditor_ImageWrapMode",
                category: "Image",
                icon: "wrap-text"));

            _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
                "setImageSize", affectsData: true,
                computeEnabled: ctx => ctx.HasDocument && HasActiveImage(ctx),
                execute: (_, payload) => ExecuteImageRuntimeCommandAsync("setImageSize", payload),
                descriptionKey: "TmDocumentEditor_ImageSize",
                tooltipKey: "TmDocumentEditor_ImageSize",
                category: "Image",
                icon: "move-diagonal"));
        }

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "insertPageBreak", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && string.Equals(ctx.ActiveRegion, "Body", StringComparison.OrdinalIgnoreCase),
            execute: (_, _) => InsertPageBreakAsync(),
            descriptionKey: "TmDocumentEditor_PageBreak",
            tooltipKey: "TmDocumentEditor_PageBreak",
            category: "Insert",
            icon: "separator-horizontal",
            disabledReasonKey: "TmDocumentEditor_CommandDisabledUnavailable"));

        RegisterHeaderFooterFieldCommand("insertPageNumber", "TmDocumentEditor_InsertPageNumber", "hash", DocumentFieldType.PageNumber);
        RegisterHeaderFooterFieldCommand("insertPageCount", "TmDocumentEditor_InsertPageCount", "files", DocumentFieldType.PageCount);
        RegisterHeaderFooterFieldCommand("insertPageXOfY", "TmDocumentEditor_InsertPageXOfY", "file-stack", DocumentFieldType.PageXOfY);
        RegisterHeaderFooterFieldCommand("insertDateField", "TmDocumentEditor_InsertDateField", "calendar-days", DocumentFieldType.Date);
        RegisterHeaderFooterFieldCommand("insertDocumentTitleField", "TmDocumentEditor_InsertDocumentTitleField", "file-text", DocumentFieldType.DocumentTitle);

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "insertFootnote", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && !ctx.IsReadOnly && string.Equals(ctx.ActiveRegion, "Body", StringComparison.OrdinalIgnoreCase),
            execute: (_, _) => InsertFootnoteAsync(),
            descriptionKey: "TmDocumentEditor_InsertFootnote",
            tooltipKey: "TmDocumentEditor_InsertFootnote",
            category: "References",
            icon: "list-plus",
            disabledReasonKey: "TmDocumentEditor_CommandDisabledUnavailable"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "insertEndnote", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && !ctx.IsReadOnly && string.Equals(ctx.ActiveRegion, "Body", StringComparison.OrdinalIgnoreCase),
            execute: (_, _) => InsertEndnoteAsync(),
            descriptionKey: "TmDocumentEditor_InsertEndnote",
            tooltipKey: "TmDocumentEditor_InsertEndnote",
            category: "References",
            icon: "list-end",
            disabledReasonKey: "TmDocumentEditor_CommandDisabledUnavailable"));

        RegisterCanvasReferenceCommand("insertCaption", "TmDocumentEditor_InsertCaption", "captions", InsertCaptionAsync);
        RegisterCanvasReferenceCommand("insertCrossReference", "TmDocumentEditor_CrossReference", "scan-line", InsertCrossReferenceAsync);
        RegisterCanvasReferenceCommand("insertTableOfContents", "TmDocumentEditor_TableOfContents", "list", InsertTableOfContentsAsync);
        RegisterCanvasReferenceCommand("insertTableOfFigures", "TmDocumentEditor_TableOfFigures", "list-tree", InsertTableOfFiguresAsync);
        RegisterCanvasReferenceCommand("insertBibliography", "TmDocumentEditor_Bibliography", "book-open", InsertBibliographyAsync);
        RegisterCanvasReferenceCommand("updateAllFields", "TmDocumentEditor_UpdateFields", "refresh-ccw", UpdateFieldsAsync);

        // ── Format providers ─────────────────────────────────────────────────
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "exportPdf", affectsData: false,
            computeEnabled: ctx => ctx.CanExportPdf,
            execute: (_, _) => ExportPdfAsync(),
            descriptionKey: "TmDocumentEditor_ExportPdf",
            tooltipKey: "TmDocumentEditor_ExportPdf",
            category: "File",
            icon: "file-down"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "importDocx", affectsData: true,
            computeEnabled: ctx => ctx.CanImportDocx,
            execute: (_, payload) => payload is InputFileChangeEventArgs args
                ? ImportDocxAsync(args)
                : Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_ImportDocx",
            tooltipKey: "TmDocumentEditor_ImportDocx",
            category: "File",
            icon: "upload"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "exportDocx", affectsData: false,
            computeEnabled: ctx => ctx.CanExportDocx,
            execute: (_, _) => ExportDocxAsync(),
            descriptionKey: "TmDocumentEditor_ExportDocx",
            tooltipKey: "TmDocumentEditor_ExportDocx",
            category: "File",
            icon: "file-down"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "trackChanges", affectsData: true,
            computeEnabled: ctx => ctx.CanTrackChanges,
            execute: (_, _) => { ToggleTrackChanges(); return Task.CompletedTask; },
            descriptionKey: "TmDocumentEditor_TrackChanges",
            tooltipKey: "TmDocumentEditor_TrackChanges",
            category: "Review",
            icon: "list-checks"));

        // ── Home tab – formatting ────────────────────────────────────────────
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "fontFamily", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => FormattingTextValue(ctx.FormattingState.FontFamily, ctx.FormattingState.FontFamilyMixed),
            execute: (_, payload) => payload is string family ? ApplyFontFamilyAsync(family) : Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_FontFamily",
            tooltipKey: "TmDocumentEditor_FontFamily",
            category: "Formatting",
            icon: "type"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "fontSize", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => FormattingTextValue(ctx.FormattingState.FontSize, ctx.FormattingState.FontSizeMixed),
            execute: (_, payload) => payload is double size ? ApplyFontSizeAsync(size) : Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_FontSize",
            tooltipKey: "TmDocumentEditor_FontSize",
            category: "Formatting",
            icon: "case-sensitive"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "paragraphAlignment", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => ctx.FormattingState.ParagraphAlignmentMixed
                ? "mixed"
                : ctx.FormattingState.ParagraphAlignment.ToString(),
            execute: (_, payload) => payload is DocumentTextAlignment alignment
                ? ApplyParagraphAlignmentAsync(alignment)
                : Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_GroupParagraph",
            tooltipKey: "TmDocumentEditor_GroupParagraph",
            category: "Paragraph",
            icon: "align-left"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "clearFormatting", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, payload) => payload is WysiwygSelectionSnapshot selection
                ? ClearInlineFormattingAsync(selection)
                : ClearInlineFormattingAsync(),
            descriptionKey: "TmDocumentEditor_ClearFormatting",
            tooltipKey: "TmDocumentEditor_ClearFormatting",
            category: "Formatting",
            icon: "eraser"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "textColor", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => FormattingTextValue(ctx.FormattingState.TextColor, ctx.FormattingState.TextColorMixed),
            execute: (_, payload) => payload is string color ? ApplyTextColorAsync(color) : Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_FontColor",
            tooltipKey: "TmDocumentEditor_FontColor",
            category: "Formatting",
            icon: "palette"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "highlightColor", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => FormattingTextValue(ctx.FormattingState.HighlightColor, ctx.FormattingState.HighlightColorMixed),
            execute: (_, payload) => payload is string color ? ApplyHighlightColorAsync(color) : Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_HighlightColor",
            tooltipKey: "TmDocumentEditor_HighlightColor",
            category: "Formatting",
            icon: "highlighter"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "lineSpacing", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => ctx.FormattingState.LineSpacingMixed
                ? "mixed"
                : ctx.FormattingState.LineSpacing.ToString("0.##", CultureInfo.InvariantCulture),
            execute: (_, payload) => payload is double spacing ? ApplyLineSpacingAsync(spacing) : Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_LineSpacing",
            tooltipKey: "TmDocumentEditor_LineSpacing",
            category: "Paragraph",
            icon: "list"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "increaseIndent", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, _) => IncreaseParagraphIndentAsync(),
            descriptionKey: "TmDocumentEditor_IncreaseIndent",
            tooltipKey: "TmDocumentEditor_IncreaseIndent",
            category: "Paragraph",
            icon: "indent-increase"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "decreaseIndent", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, _) => DecreaseParagraphIndentAsync(),
            descriptionKey: "TmDocumentEditor_DecreaseIndent",
            tooltipKey: "TmDocumentEditor_DecreaseIndent",
            category: "Paragraph",
            icon: "indent-decrease"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "bulletList", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, _) => ToggleBulletListAsync(),
            descriptionKey: "TmDocumentEditor_BulletedList",
            tooltipKey: "TmDocumentEditor_BulletedList",
            category: "Paragraph",
            icon: "list"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "numberedList", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, _) => ToggleNumberedListAsync(),
            descriptionKey: "TmDocumentEditor_NumberedList",
            tooltipKey: "TmDocumentEditor_NumberedList",
            category: "Paragraph",
            icon: "list"));

        // ── Insert tab ───────────────────────────────────────────────────────
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "insertMenu", affectsData: false,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, _) => ToggleInsertPanelAsync(),
            descriptionKey: "TmDocumentEditor_Insert",
            tooltipKey: "TmDocumentEditor_Insert",
            category: "Insert",
            icon: "plus"));

        // ── Review tab ───────────────────────────────────────────────────────
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "protectDocument", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => ctx.IsProtected ? "active" : "inactive",
            execute: (_, _) => ToggleDocumentProtectionAsync(),
            descriptionKey: "TmDocumentEditor_ProtectDocument",
            tooltipKey: "TmDocumentEditor_ProtectDocument",
            category: "Review",
            icon: "lock"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "markEditableRegion", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && ctx.IsProtected,
            execute: (_, _) => MarkEditableRegionAsync(),
            descriptionKey: "TmDocumentEditor_MarkEditableRegion",
            tooltipKey: "TmDocumentEditor_MarkEditableRegion",
            category: "Review",
            icon: "pencil"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "reviewDisplayMode", affectsData: false,
            computeEnabled: ctx => ctx.HasDocument && IsFeatureEnabled(DocumentEditorFeatureNames.TrackChanges),
            execute: (_, _) => Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_ShowMarkup",
            tooltipKey: "TmDocumentEditor_ShowMarkup",
            category: "Review",
            icon: "eye"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "addComment", affectsData: true,
            computeEnabled: ctx => ctx.CanAddComment,
            execute: (_, _) => BeginCommentFromToolbarAsync(),
            descriptionKey: "TmDocumentEditor_AddComment",
            tooltipKey: "TmDocumentEditor_AddComment",
            category: "Review",
            icon: "message-square"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "compareDocuments", affectsData: false,
            computeEnabled: ctx => ctx.CanCompareDocuments,
            execute: (_, _) => OpenCompareDialogAsync(),
            descriptionKey: "TmDocumentEditor_CompareDocuments",
            tooltipKey: "TmDocumentEditor_CompareDocuments",
            category: "Review",
            icon: "git-compare"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "openComments", affectsData: false,
            computeEnabled: _ => ShowComments && IsFeatureEnabled(DocumentEditorFeatureNames.Comments),
            execute: (_, _) => OpenCommentsPanelAsync(),
            descriptionKey: "TmDocumentEditor_OpenComments",
            tooltipKey: "TmDocumentEditor_OpenComments",
            category: "Review",
            icon: "message-square"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "openRevisions", affectsData: false,
            computeEnabled: _ => IsFeatureEnabled(DocumentEditorFeatureNames.TrackChanges),
            execute: (_, _) => OpenRevisionsPanelAsync(),
            descriptionKey: "TmDocumentEditor_OpenRevisions",
            tooltipKey: "TmDocumentEditor_OpenRevisions",
            category: "Review",
            icon: "file-diff"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "acceptAllRevisions", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && CanReviewRevisions && HasPendingRevisions,
            execute: (_, _) => AcceptAllRevisionsAsync(new DocumentRevisionFilter()),
            descriptionKey: "TmDocumentEditor_AcceptAllRevisions",
            tooltipKey: "TmDocumentEditor_AcceptAllRevisions",
            category: "Review",
            icon: "check-check"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "rejectAllRevisions", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && CanReviewRevisions && HasPendingRevisions,
            execute: (_, _) => RejectAllRevisionsAsync(new DocumentRevisionFilter()),
            descriptionKey: "TmDocumentEditor_RejectAllRevisions",
            tooltipKey: "TmDocumentEditor_RejectAllRevisions",
            category: "Review",
            icon: "x"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "openVersions", affectsData: false,
            computeEnabled: _ => ShowVersionHistory,
            execute: (_, _) => OpenVersionsPanelAsync(),
            descriptionKey: "TmDocumentEditor_OpenVersions",
            tooltipKey: "TmDocumentEditor_OpenVersions",
            category: "View",
            defaultShortcut: "Ctrl+Alt+V",
            icon: "clock"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "find", affectsData: false,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, _) => OpenFindPanelAsync(replaceMode: false),
            descriptionKey: "TmDocumentEditor_FindPlaceholder",
            tooltipKey: "TmDocumentEditor_FindPanelLabel",
            category: "Edit",
            defaultShortcut: "Ctrl+F",
            icon: "search"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "replace", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, _) => OpenFindPanelAsync(replaceMode: true),
            descriptionKey: "TmDocumentEditor_FindPanelLabel",
            tooltipKey: "TmDocumentEditor_FindPanelLabel",
            category: "Edit",
            defaultShortcut: "Ctrl+H",
            icon: "replace"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "replaceAll", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, _) => OpenFindPanelAsync(replaceMode: true),
            descriptionKey: "TmDocumentEditor_FindReplaceAll",
            tooltipKey: "TmDocumentEditor_FindReplaceAll",
            category: "Edit",
            icon: "replace"));

        // ── View tab ─────────────────────────────────────────────────────────
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "showBlocks", affectsData: false,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => _showBlocks ? "active" : "inactive",
            execute: (_, _) => ToggleShowBlocksAsync(),
            descriptionKey: "TmDocumentEditor_ShowBlocks",
            tooltipKey: "TmDocumentEditor_ShowBlocks",
            category: "View",
            icon: "layout-panel-top"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "toggleNonPrintingCharacters", affectsData: false,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: _ => _showNonPrintingCharacters ? "active" : "inactive",
            execute: (_, _) => ToggleNonPrintingCharactersAsync(),
            descriptionKey: "TmDocumentEditor_NonPrintingCharacters",
            tooltipKey: "TmDocumentEditor_NonPrintingCharacters",
            category: "View",
            icon: "pilcrow"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "fullscreen", affectsData: false,
            computeEnabled: _ => true,
            computeValue: _ => _isFullscreen ? "active" : "inactive",
            execute: (_, _) => ToggleFullscreenAsync(),
            descriptionKey: "TmDocumentEditor_Fullscreen",
            tooltipKey: "TmDocumentEditor_Fullscreen",
            category: "View",
            icon: "maximize"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "openPrintPreview", affectsData: false,
            computeEnabled: ctx => ctx.HasDocument && UsingCanvasEngine,
            computeValue: _ => _canvasPrintPreviewActive ? "active" : "inactive",
            execute: (_, _) => OpenCanvasPrintPreviewAsync(),
            descriptionKey: "TmDocumentEditor_PrintPreview",
            tooltipKey: "TmDocumentEditor_PrintPreview",
            category: "View",
            icon: "printer"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "printDocument", affectsData: false,
            computeEnabled: ctx => ctx.HasDocument && UsingCanvasEngine,
            computeValue: _ => null,
            execute: (_, _) => PrintCanvasDocumentAsync(),
            descriptionKey: "TmDocumentEditor_PrintDocument",
            tooltipKey: "TmDocumentEditor_PrintDocument",
            category: "View",
            icon: "printer"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "viewDocumentJson", affectsData: false,
            computeEnabled: ctx => ShowDebugTools && ctx.HasDocument,
            computeValue: _ => null,
            execute: (_, _) => ViewDocumentJsonAsync(),
            descriptionKey: "TmDocumentEditor_ViewDocumentJson",
            tooltipKey: "TmDocumentEditor_ViewDocumentJson",
            category: "Debug",
            icon: "code"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "viewClipboardHtml", affectsData: false,
            computeEnabled: ctx => ShowDebugTools && ctx.HasDocument,
            computeValue: _ => null,
            execute: (_, _) => ViewClipboardHtmlAsync(),
            descriptionKey: "TmDocumentEditor_ViewClipboardHtml",
            tooltipKey: "TmDocumentEditor_ViewClipboardHtml",
            category: "Debug",
            icon: "clipboard"));

        // ── Fáze 16: příkazy dřív dostupné jen přes callbacky/RouteToCanvasEngineAsync ──
        // Deklarativní toolbar (DocumentEditorToolbarRegistry.IsAvailable) itemy s nezaregistrovaným
        // CommandName zahodí — každý CommandName z DocumentEditorBuiltInToolbar musí být registrovaný.

        // Dedikované alignment aliasy pro deklarativní itemy (paragraphAlignment zůstává payload-driven).
        RegisterAlignmentCommand("alignLeft", DocumentTextAlignment.Left, "align-left", "TmDocumentEditor_AlignLeft");
        RegisterAlignmentCommand("alignCenter", DocumentTextAlignment.Center, "align-center", "TmDocumentEditor_AlignCenter");
        RegisterAlignmentCommand("alignRight", DocumentTextAlignment.Right, "align-right", "TmDocumentEditor_AlignRight");
        RegisterAlignmentCommand("alignJustify", DocumentTextAlignment.Justify, "align-justify", "TmDocumentEditor_AlignJustify");

        RegisterInlineMarkCommand("superscript", InlineMarkType.Superscript, ctx => ctx.FormattingState.Superscript, "superscript", "TmDocumentEditor_Superscript");
        RegisterInlineMarkCommand("subscript", InlineMarkType.Subscript, ctx => ctx.FormattingState.Subscript, "subscript", "TmDocumentEditor_Subscript");
        RegisterInlineMarkCommand("smallCaps", InlineMarkType.SmallCaps, ctx => ctx.FormattingState.SmallCaps, "case-upper", "TmDocumentEditor_SmallCaps");
        RegisterInlineMarkCommand("allCaps", InlineMarkType.AllCaps, ctx => ctx.FormattingState.AllCaps, "case-upper", "TmDocumentEditor_AllCaps");
        RegisterInlineMarkCommand("doubleStrikethrough", InlineMarkType.DoubleStrikethrough, ctx => ctx.FormattingState.DoubleStrikethrough, "double-strikethrough", "TmDocumentEditor_DoubleStrikethrough");

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "changeCase", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, payload) => payload is string variant && variant.Length > 0
                ? ChangeCaseAsync(variant)
                : Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_ChangeCase",
            tooltipKey: "TmDocumentEditor_ChangeCase",
            category: "Formatting",
            icon: "case-sensitive"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "increaseFontSize", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, _) => IncreaseFontSizeAsync(),
            descriptionKey: "TmDocumentEditor_IncreaseFontSize",
            tooltipKey: "TmDocumentEditor_IncreaseFontSize",
            category: "Formatting",
            icon: "plus"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "decreaseFontSize", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            execute: (_, _) => DecreaseFontSizeAsync(),
            descriptionKey: "TmDocumentEditor_DecreaseFontSize",
            tooltipKey: "TmDocumentEditor_DecreaseFontSize",
            category: "Formatting",
            icon: "minus"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "spacingBefore", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => ctx.FormattingState.SpacingBeforeMixed
                ? "mixed"
                : ctx.FormattingState.SpacingBefore.ToString("0.##", CultureInfo.InvariantCulture),
            execute: (_, payload) => payload is double spacing ? ApplySpacingBeforeAsync(spacing) : Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_SpacingBefore",
            tooltipKey: "TmDocumentEditor_SpacingBefore",
            category: "Paragraph",
            icon: "arrow-up-from-line"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "spacingAfter", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => ctx.FormattingState.SpacingAfterMixed
                ? "mixed"
                : ctx.FormattingState.SpacingAfter.ToString("0.##", CultureInfo.InvariantCulture),
            execute: (_, payload) => payload is double spacing ? ApplySpacingAfterAsync(spacing) : Task.CompletedTask,
            descriptionKey: "TmDocumentEditor_SpacingAfter",
            tooltipKey: "TmDocumentEditor_SpacingAfter",
            category: "Paragraph",
            icon: "arrow-down-to-line"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "insertEquation", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && UsingCanvasEngine,
            execute: (_, payload) => InsertEquationAsync(payload as string ?? "fraction"),
            descriptionKey: "TmDocumentEditor_InsertEquation",
            tooltipKey: "TmDocumentEditor_InsertEquation",
            category: "Insert",
            icon: "sigma"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "insertSymbol", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && UsingCanvasEngine,
            execute: (_, payload) => InsertSymbolAsync(payload as string ?? "emDash"),
            descriptionKey: "TmDocumentEditor_InsertSymbol",
            tooltipKey: "TmDocumentEditor_InsertSymbol",
            category: "Insert",
            icon: "omega"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "showRuler", affectsData: false,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: _ => _showRuler ? "active" : "inactive",
            execute: (_, _) => SetRulerVisibleAsync(!_showRuler),
            descriptionKey: "TmDocumentEditor_ShowRuler",
            tooltipKey: "TmDocumentEditor_ShowRuler",
            category: "View",
            icon: "ruler"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "zoomPageWidth", affectsData: false,
            computeEnabled: ctx => ctx.HasDocument && UsingCanvasEngine,
            computeValue: _ => _zoomPageWidth ? "active" : "inactive",
            execute: (_, _) => SetZoomPageWidthAsync(),
            descriptionKey: "TmDocumentEditor_PageWidth",
            tooltipKey: "TmDocumentEditor_PageWidth",
            category: "View",
            icon: "panel-top"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "differentFirstPage", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && IsHeaderFooterRegion(ctx.ActiveRegion),
            computeValue: _ => DifferentFirstPageHeaderFooter ? "active" : "inactive",
            execute: (_, _) => SetDifferentFirstPageHeaderFooterAsync(!DifferentFirstPageHeaderFooter),
            descriptionKey: "TmDocumentEditor_DifferentFirstPage",
            tooltipKey: "TmDocumentEditor_DifferentFirstPage",
            category: "HeaderFooter",
            icon: "file-stack"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "differentOddEven", affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && IsHeaderFooterRegion(ctx.ActiveRegion),
            computeValue: _ => DifferentOddAndEvenHeaderFooter ? "active" : "inactive",
            execute: (_, _) => SetDifferentOddAndEvenHeaderFooterAsync(!DifferentOddAndEvenHeaderFooter),
            descriptionKey: "TmDocumentEditor_DifferentOddEven",
            tooltipKey: "TmDocumentEditor_DifferentOddEven",
            category: "HeaderFooter",
            icon: "layout"));

        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            "closeHeaderFooter", affectsData: false,
            computeEnabled: ctx => ctx.HasDocument && IsHeaderFooterRegion(ctx.ActiveRegion),
            execute: (_, _) => CloseHeaderFooterAsync(),
            descriptionKey: "TmDocumentEditor_CloseHeaderFooter",
            tooltipKey: "TmDocumentEditor_CloseHeaderFooter",
            category: "HeaderFooter",
            icon: "x"));
    }

    private void RegisterAlignmentCommand(string name, DocumentTextAlignment alignment, string icon, string key) =>
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            name, affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => !ctx.FormattingState.ParagraphAlignmentMixed && ctx.FormattingState.ParagraphAlignment == alignment
                ? "active"
                : "inactive",
            execute: (_, _) => ApplyParagraphAlignmentAsync(alignment),
            descriptionKey: key,
            tooltipKey: key,
            category: "Paragraph",
            icon: icon));

    private void RegisterInlineMarkCommand(
        string name,
        InlineMarkType mark,
        Func<DocumentEditorCommandContext, WysiwygFormattingValue> valueSelector,
        string icon,
        string key) =>
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            name, affectsData: true,
            computeEnabled: ctx => ctx.HasDocument,
            computeValue: ctx => FormattingValueToString(valueSelector(ctx)),
            execute: (_, _) => ToggleInlineMarkAsync(mark),
            descriptionKey: key,
            tooltipKey: key,
            category: "Formatting",
            icon: icon));

    private DocumentEditorCommandContext BuildCommandContext() =>
        new()
        {
            IsReadOnly = EffectiveReadOnly,
            Permissions = EffectivePermissions,
            ActiveRegion = _activeWysiwygRegion,
            SelectionSnapshot = _lastBodySelectionSnapshot,
            FormattingState = _formattingState,
            UndoState = EffectiveUndoState,
            HasDocument = _document is not null,
            CanExportPdf = CanExportPdf,
            CanImportDocx = CanImportDocx,
            CanExportDocx = CanExportDocx,
            IsSaving = _isSaving,
            CanTrackChanges = CanEditDocument
                && !IsVersionPreview
                && !IsTemplatePreview
                // Canvas suggestion mode locks tracking on — the toggle has no off state to offer.
                && !IsCanvasSuggestionMode
                && IsFeatureEnabled(DocumentEditorFeatureNames.TrackChanges),
            CanAddComment = CanStartComment,
            CanCompareDocuments = CanCompareDocuments,
            IsProtected = _isDocumentProtected,
            IsInEditableRegion = _isCaretInEditableRegion
        };

    private Task RefreshCommandRegistryAsync()
    {
        var context = BuildCommandContext();
        return _commandRegistry.RefreshAllAsync(context, BuildCommandContextSignature(context));
    }

    /// <summary>
    /// Fingerprint of EVERY input the ~70 command lambdas read (perf plan N7.3) — the context fields
    /// plus the live editor state referenced directly from the lambdas (audited 2026-07-10:
    /// HasActiveImage/HasActiveTable read <c>_selection</c>, view toggles, revision review state,
    /// feature gating, UsingCanvasEngine). An unchanged signature lets the registry skip rebuilding
    /// all command states. When adding a registration that reads NEW live state, extend this too.
    /// </summary>
    private string BuildCommandContextSignature(DocumentEditorCommandContext context)
    {
        var formatting = context.FormattingState;
        var undo = context.UndoState;
        var selection = context.SelectionSnapshot;
        return string.Join('',
            context.HasDocument, context.IsReadOnly, context.IsProtected, context.IsInEditableRegion,
            context.IsSaving, context.ActiveRegion,
            context.CanExportPdf, context.CanImportDocx, context.CanExportDocx,
            context.CanTrackChanges, context.CanAddComment, context.CanCompareDocuments,
            undo.CanUndo, undo.CanRedo, undo.NextUndoDescription, undo.NextRedoDescription,
            formatting.Bold, formatting.Italic, formatting.Underline, formatting.Strikethrough,
            formatting.FontFamily, formatting.FontFamilyMixed, formatting.FontSize, formatting.FontSizeMixed,
            formatting.ParagraphAlignment, formatting.ParagraphAlignmentMixed,
            formatting.TextColor, formatting.TextColorMixed,
            formatting.HighlightColor, formatting.HighlightColorMixed,
            formatting.LineSpacing, formatting.LineSpacingMixed,
            // Fáze 16: vstupy nových registrací (inline mark toggly, spacing selecty, view/header-footer stav).
            formatting.Superscript, formatting.Subscript, formatting.SmallCaps, formatting.AllCaps,
            formatting.DoubleStrikethrough,
            formatting.SpacingBefore, formatting.SpacingBeforeMixed,
            formatting.SpacingAfter, formatting.SpacingAfterMixed,
            _showRuler, _zoomPageWidth, DifferentFirstPageHeaderFooter, DifferentOddAndEvenHeaderFooter,
            selection?.ActiveTableCellId, selection?.ActiveObjectId, selection?.ObjectSelection?.ObjectId,
            _selection.ActiveTableCellId, _selection.ActiveObjectId, _selection.ObjectSelection?.ObjectId,
            // Fáze 10 (command-layer plán): HasActiveImage nově čte i canvas-side aktivní obrázek.
            _canvasActiveImage is not null,
            CanReviewRevisions, HasPendingRevisions,
            _showBlocks, _showNonPrintingCharacters, _isFullscreen, _canvasPrintPreviewActive,
            UsingCanvasEngine,
            EffectiveDisabledFeatures is null ? string.Empty : string.Join(',', EffectiveDisabledFeatures));
    }

    private bool HasActiveImage(DocumentEditorCommandContext context) =>
        !string.IsNullOrWhiteSpace(context.SelectionSnapshot?.ObjectSelection?.ObjectId)
        || !string.IsNullOrWhiteSpace(context.SelectionSnapshot?.ActiveObjectId)
        || !string.IsNullOrWhiteSpace(_selection.ObjectSelection?.ObjectId)
        || !string.IsNullOrWhiteSpace(_selection.ActiveObjectId)
        // Canvas mode: the engine's active image is mirrored via SyncCanvasActiveImage, not _selection.
        || _canvasActiveImage is not null;

    private void RegisterTableRuntimeCommand(string name, string key, string icon, bool affectsData = true)
    {
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            name,
            affectsData,
            computeEnabled: ctx => ctx.HasDocument && HasActiveTable(ctx),
            execute: (_, payload) => ExecuteTableRuntimeCommandAsync(name, payload),
            descriptionKey: key,
            tooltipKey: key,
            category: "Table",
            icon: icon));
    }

    private void RegisterHeaderFooterFieldCommand(string name, string key, string icon, DocumentFieldType fieldType)
    {
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            name,
            affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && IsHeaderFooterRegion(ctx.ActiveRegion),
            execute: (_, _) => InsertHeaderFooterFieldAsync(fieldType),
            descriptionKey: key,
            tooltipKey: key,
            category: "HeaderFooter",
            icon: icon,
            disabledReasonKey: "TmDocumentEditor_CommandDisabledUnavailable"));
    }

    private void RegisterCanvasReferenceCommand(string name, string key, string icon, Func<Task> execute)
    {
        _commandRegistry.Register(new FuncDocumentEditorCommandEntry(
            name,
            affectsData: true,
            computeEnabled: ctx => ctx.HasDocument && !ctx.IsReadOnly && UsingCanvasEngine,
            execute: (_, _) => execute(),
            descriptionKey: key,
            tooltipKey: key,
            category: "References",
            icon: icon,
            disabledReasonKey: "TmDocumentEditor_CommandDisabledUnavailable"));
    }

    private static bool IsHeaderFooterRegion(string? region) =>
        string.Equals(region, "Header", StringComparison.OrdinalIgnoreCase)
        || string.Equals(region, "Footer", StringComparison.OrdinalIgnoreCase);

    private bool HasActiveTable(DocumentEditorCommandContext context) =>
        !string.IsNullOrWhiteSpace(context.SelectionSnapshot?.ActiveTableCellId)
        || !string.IsNullOrWhiteSpace(_selection.ActiveTableCellId);

    private static string FormattingValueToString(WysiwygFormattingValue value) =>
        value switch
        {
            WysiwygFormattingValue.Active => "active",
            WysiwygFormattingValue.Mixed => "mixed",
            _ => "inactive"
        };

    private static string? FormattingTextValue(string? value, bool mixed) =>
        mixed ? "mixed" : value;
}
