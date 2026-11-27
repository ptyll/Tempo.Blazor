using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;

namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>
/// Interop state payload classes for <see cref="TmDocumentCanvasEngineHost"/>. Kept in a code-behind
/// partial (not the .razor @code block) so the System.Text.Json source generator can see them --
/// source generators cannot consume Razor-generated source (perf plan N10.3).
/// </summary>
public partial class TmDocumentCanvasEngineHost
{
    /// <summary>Canvas engine formatting state exposed to Blazor callers.</summary>
    public sealed class CanvasEngineFormattingState
    {
        /// <summary>Whether bold is active at the current selection.</summary>
        public bool Bold { get; set; }

        /// <summary>Whether bold is mixed across the current selection.</summary>
        public bool BoldMixed { get; set; }

        /// <summary>Whether italic is active at the current selection.</summary>
        public bool Italic { get; set; }

        /// <summary>Whether italic is mixed across the current selection.</summary>
        public bool ItalicMixed { get; set; }

        /// <summary>Whether underline is active at the current selection.</summary>
        public bool Underline { get; set; }

        /// <summary>Whether underline is mixed across the current selection.</summary>
        public bool UnderlineMixed { get; set; }

        /// <summary>Whether strikethrough is active at the current selection.</summary>
        public bool Strikethrough { get; set; }

        /// <summary>Whether strikethrough is mixed across the current selection.</summary>
        public bool StrikethroughMixed { get; set; }

        /// <summary>Whether superscript is active at the current selection.</summary>
        public bool Superscript { get; set; }

        /// <summary>Whether superscript is mixed across the current selection.</summary>
        public bool SuperscriptMixed { get; set; }

        /// <summary>Whether subscript is active at the current selection.</summary>
        public bool Subscript { get; set; }

        /// <summary>Whether subscript is mixed across the current selection.</summary>
        public bool SubscriptMixed { get; set; }

        /// <summary>Whether small caps is active at the current selection.</summary>
        public bool SmallCaps { get; set; }

        /// <summary>Whether small caps is mixed across the current selection.</summary>
        public bool SmallCapsMixed { get; set; }

        /// <summary>Whether all caps is active at the current selection.</summary>
        public bool AllCaps { get; set; }

        /// <summary>Whether all caps is mixed across the current selection.</summary>
        public bool AllCapsMixed { get; set; }

        /// <summary>Whether double strikethrough is active at the current selection.</summary>
        public bool DoubleStrikethrough { get; set; }

        /// <summary>Whether double strikethrough is mixed across the current selection.</summary>
        public bool DoubleStrikethroughMixed { get; set; }

        /// <summary>Current font family value.</summary>
        public string FontFamily { get; set; } = string.Empty;

        /// <summary>Whether font family is mixed across the current selection.</summary>
        public bool FontFamilyMixed { get; set; }

        /// <summary>Current font size value.</summary>
        public string FontSize { get; set; } = string.Empty;

        /// <summary>Whether font size is mixed across the current selection.</summary>
        public bool FontSizeMixed { get; set; }

        /// <summary>Current text color value.</summary>
        public string TextColor { get; set; } = string.Empty;

        /// <summary>Whether text color is mixed across the current selection.</summary>
        public bool TextColorMixed { get; set; }

        /// <summary>Current highlight color value.</summary>
        public string HighlightColor { get; set; } = string.Empty;

        /// <summary>Whether highlight color is mixed across the current selection.</summary>
        public bool HighlightColorMixed { get; set; }

        /// <summary>Current paragraph alignment.</summary>
        public string Alignment { get; set; } = "left";

        /// <summary>Whether paragraph alignment is mixed across the current selection.</summary>
        public bool AlignmentMixed { get; set; }

        /// <summary>Current line spacing multiplier.</summary>
        public double LineSpacing { get; set; } = 1;

        /// <summary>Whether line spacing is mixed across the current selection.</summary>
        public bool LineSpacingMixed { get; set; }

        /// <summary>Current spacing before the paragraph in points.</summary>
        public double SpacingBefore { get; set; }

        /// <summary>Whether spacing before is mixed across the current selection.</summary>
        public bool SpacingBeforeMixed { get; set; }

        /// <summary>Current spacing after the paragraph in points.</summary>
        public double SpacingAfter { get; set; }

        /// <summary>Whether spacing after is mixed across the current selection.</summary>
        public bool SpacingAfterMixed { get; set; }

        /// <summary>Current left indent in points.</summary>
        public double LeftIndent { get; set; }

        /// <summary>Whether left indent is mixed across the current selection.</summary>
        public bool LeftIndentMixed { get; set; }

        /// <summary>Whether the current paragraph is an unordered list item.</summary>
        public bool BulletList { get; set; }

        /// <summary>Whether the current paragraph is an ordered list item.</summary>
        public bool NumberedList { get; set; }

        /// <summary>Whether the current selection spans multiple list states.</summary>
        public bool ListMixed { get; set; }

        /// <summary>Current block style name.</summary>
        public string BlockStyle { get; set; } = "Normal";

        /// <summary>Whether block style is mixed across the current selection.</summary>
        public bool BlockStyleMixed { get; set; }

        /// <summary>Whether the ruler is visible in the canvas engine.</summary>
        public bool ShowRuler { get; set; } = true;

        /// <summary>Whether block boundaries are visible in the canvas engine.</summary>
        public bool ShowBlocks { get; set; }

        /// <summary>Whether non-printing characters are visible in the canvas engine.</summary>
        public bool ShowNonPrintingCharacters { get; set; }

        /// <summary>Current canvas view mode.</summary>
        public string ViewMode { get; set; } = "print";

        /// <summary>Current canvas zoom percent.</summary>
        public int ZoomPercent { get; set; } = 100;

        /// <summary>Current canvas zoom preset.</summary>
        public string ZoomPreset { get; set; } = "custom";

        /// <summary>Whether the current canvas view hides the editor toolbar.</summary>
        public bool ToolbarHidden { get; set; }

        /// <summary>Whether print preview is currently active.</summary>
        public bool PrintPreviewActive { get; set; }

        /// <summary>Selected canvas image object, if the current selection targets an image.</summary>
        public CanvasEngineImageState? Image { get; set; }
    }

    /// <summary>Canvas print preview state exposed to Blazor callers and tests.</summary>
    public sealed class CanvasEnginePrintPreviewState
    {
        /// <summary>Whether print preview is active.</summary>
        public bool Active { get; set; }

        /// <summary>Document id rendered by the preview.</summary>
        public string DocumentId { get; set; } = string.Empty;

        /// <summary>Rendered page count.</summary>
        public int PageCount { get; set; }

        /// <summary>Total display-list command count.</summary>
        public int CommandCount { get; set; }

        /// <summary>Printable display-list command count.</summary>
        public int PrintableCommandCount { get; set; }

        /// <summary>Rendered text run count.</summary>
        public int TextRunCount { get; set; }

        /// <summary>Whether the preview contains no printable document content.</summary>
        public bool IsBlank { get; set; }

        /// <summary>Print dialog invocation state, if requested.</summary>
        public CanvasEnginePrintDialogState? Dialog { get; set; }
    }

    /// <summary>Browser print dialog request state.</summary>
    public sealed class CanvasEnginePrintDialogState
    {
        /// <summary>Whether a print dialog was requested.</summary>
        public bool Requested { get; set; }

        /// <summary>Whether the browser print function was available and invoked.</summary>
        public bool Invoked { get; set; }
    }

    /// <summary>Selected canvas image state exposed to the editor properties panel.</summary>
    public sealed class CanvasEngineImageState
    {
        /// <summary>Stable image object id.</summary>
        public string ObjectId { get; set; } = string.Empty;

        /// <summary>Owning block id.</summary>
        public string BlockId { get; set; } = string.Empty;

        /// <summary>Owning drawing run id when the image is inline.</summary>
        public string RunId { get; set; } = string.Empty;

        /// <summary>Resolved image URL when available.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Provider asset id when available.</summary>
        public string AssetId { get; set; } = string.Empty;

        /// <summary>Rendered image width in points.</summary>
        public double Width { get; set; }

        /// <summary>Rendered image height in points.</summary>
        public double Height { get; set; }

        /// <summary>Horizontal offset in points.</summary>
        public double X { get; set; }

        /// <summary>Vertical offset in points.</summary>
        public double Y { get; set; }

        /// <summary>Canonical wrap mode name.</summary>
        public string WrapMode { get; set; } = "Inline";

        /// <summary>Stacking order value.</summary>
        public double ZIndex { get; set; }

        /// <summary>Alternative text.</summary>
        public string AltText { get; set; } = string.Empty;

        /// <summary>Caption text.</summary>
        public string Caption { get; set; } = string.Empty;

        /// <summary>Whether the image is decorative and does not require alternative text.</summary>
        public bool IsDecorative { get; set; }
    }

    /// <summary>Canvas context menu request sent from the canvas JavaScript runtime.</summary>
    public sealed class CanvasEngineContextMenuRequest
    {
        /// <summary>Viewport-relative left coordinate for the menu.</summary>
        public int X { get; set; }

        /// <summary>Viewport-relative top coordinate for the menu.</summary>
        public int Y { get; set; }

        /// <summary>Zero-based rendered page index under the pointer.</summary>
        public int PageIndex { get; set; }

        /// <summary>Block id under the pointer when available.</summary>
        public string? BlockId { get; set; }

        /// <summary>Text offset under the pointer when available.</summary>
        public int Offset { get; set; }

        /// <summary>Whether the context menu targets a non-collapsed text selection.</summary>
        public bool HasSelection { get; set; }

        /// <summary>Whether the pointer is inside a table block.</summary>
        public bool InTable { get; set; }

        /// <summary>Table block id under the pointer when available.</summary>
        public string? TableId { get; set; }

        /// <summary>Table cell id under or near the selection when available.</summary>
        public string? CellId { get; set; }

        /// <summary>Image block id under the pointer when available.</summary>
        public string? ImageBlockId { get; set; }

        /// <summary>Misspelling diagnostic under the pointer when available.</summary>
        public CanvasEngineMisspelling? Misspelling { get; set; }

        /// <summary>Selection snapshot to restore before running menu commands.</summary>
        public WysiwygSelectionSnapshot? Selection { get; set; }

        /// <summary>Page-break block id under the pointer when available.</summary>
        public string? PageBreakBlockId { get; set; }

        /// <summary>Browser viewport width in pixels; lets the menu clamp into the viewport.</summary>
        public int ViewportWidth { get; set; }

        /// <summary>Browser viewport height in pixels; lets the menu clamp into the viewport.</summary>
        public int ViewportHeight { get; set; }
    }

    /// <summary>Canvas misspelling diagnostic exposed to Blazor context menu handlers.</summary>
    public sealed class CanvasEngineMisspelling
    {
        /// <summary>Misspelled word.</summary>
        public string? Word { get; set; }

        /// <summary>Start offset inside the target block.</summary>
        public int Start { get; set; }

        /// <summary>End offset inside the target block.</summary>
        public int End { get; set; }

        /// <summary>Target block id.</summary>
        public string? BlockId { get; set; }

        /// <summary>Suggested replacements returned by the proofing provider.</summary>
        public List<string> Suggestions { get; set; } = [];

        /// <summary>Whether the editor may apply a replacement for this diagnostic.</summary>
        public bool CanApplyFix { get; set; } = true;
    }

    /// <summary>Canvas engine command execution result exposed to Blazor callers.</summary>
    public sealed class CanvasEngineCommandResult
    {
        /// <summary>Whether the command was handled.</summary>
        public bool Handled { get; set; }

        /// <summary>Executed command identifier.</summary>
        public string CommandId { get; set; } = string.Empty;

        /// <summary>
        /// Primitives-only UI snapshot bundled with the command response (perf phase 2.2) so the toolbar can
        /// update pressed-state / dirty / undo without a follow-up batch of interop pulls.
        /// </summary>
        public CanvasEngineUiState? UiState { get; set; }
    }

    /// <summary>Result of a programmatic clipboard operation (B11/B12 context-menu copy/cut/paste).</summary>
    public sealed class CanvasEngineClipboardResult
    {
        /// <summary>Whether the clipboard operation succeeded.</summary>
        public bool Handled { get; set; }

        /// <summary>Operation name (copy / cut / paste-*).</summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>Failure reason when not handled (e.g. "permission", "unsupported").</summary>
        public string? Reason { get; set; }
    }

    /// <summary>Canvas engine command query state exposed to Blazor callers.</summary>
    public sealed class CanvasEngineCommandState
    {
        /// <summary>Whether the command is disabled.</summary>
        public bool Disabled { get; set; }

        /// <summary>Whether the command is active.</summary>
        public bool Active { get; set; }

        /// <summary>Whether the command has mixed state.</summary>
        public bool Mixed { get; set; }

        /// <summary>Current command value.</summary>
        public string? Value { get; set; }

        /// <summary>Compact state name.</summary>
        public string State { get; set; } = "inactive";
    }

    private sealed class CanvasCommentAnchorState
    {
        public string Type { get; set; } = string.Empty;

        public string BlockId { get; set; } = string.Empty;

        public int StartOffset { get; set; }

        public int EndOffset { get; set; }
    }

    /// <summary>Canvas comment or revision marker selection emitted from JavaScript.</summary>
    public sealed class CanvasEngineAnnotationSelection
    {
        /// <summary>Annotation kind, either comment or revision.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Selected annotation identifier.</summary>
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>Canvas clipboard image upload request received from JavaScript.</summary>
    public sealed class CanvasClipboardImageUploadRequest
    {
        /// <summary>Document id that owns the pasted image.</summary>
        public string DocumentId { get; set; } = string.Empty;

        /// <summary>Browser-provided file name.</summary>
        public string FileName { get; set; } = "clipboard-image.png";

        /// <summary>Browser-provided content type.</summary>
        public string ContentType { get; set; } = "image/png";

        /// <summary>Browser-provided file size in bytes.</summary>
        public long SizeBytes { get; set; }
    }

    /// <summary>Canvas clipboard image upload result returned to JavaScript.</summary>
    public sealed class CanvasClipboardImageUploadResult
    {
        /// <summary>Whether the upload succeeded.</summary>
        public bool Success { get; set; }

        /// <summary>Provider-managed asset id.</summary>
        public string? AssetId { get; set; }

        /// <summary>Resolved URL returned by the provider.</summary>
        public string? Url { get; set; }

        /// <summary>Uploaded content type.</summary>
        public string ContentType { get; set; } = "image/png";

        /// <summary>Uploaded file name.</summary>
        public string? FileName { get; set; }

        /// <summary>Error message when upload failed.</summary>
        public string? ErrorMessage { get; set; }
    }

    private sealed class CanvasEngineClipboardDebugState
    {
        public string Operation { get; set; } = string.Empty;

        public string RawHtml { get; set; } = string.Empty;

        public string PlainText { get; set; } = string.Empty;

        public string Source { get; set; } = "unknown";

        public string NormalizedJson { get; set; } = string.Empty;

        public List<string> Warnings { get; set; } = [];

        public DateTimeOffset? CapturedAt { get; set; }
    }

    /// <summary>Canvas engine undo state exposed to Blazor callers.</summary>
    public sealed class CanvasEngineUndoState
    {
        /// <summary>Whether undo can execute.</summary>
        public bool CanUndo { get; set; }

        /// <summary>Whether redo can execute.</summary>
        public bool CanRedo { get; set; }
    }

    /// <summary>Canvas engine search state exposed to Blazor callers.</summary>
    public sealed class CanvasEngineSearchState
    {
        /// <summary>Current query text.</summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>Active result index.</summary>
        public int ActiveIndex { get; set; }

        /// <summary>Current match count.</summary>
        public int MatchCount { get; set; }

        /// <summary>Current canvas search matches.</summary>
        public List<CanvasEngineSearchMatch> Matches { get; set; } = [];
    }

    /// <summary>Canvas engine search match exposed to Blazor callers.</summary>
    public sealed class CanvasEngineSearchMatch
    {
        /// <summary>Zero-based match index.</summary>
        public int Index { get; set; }

        /// <summary>Target block id.</summary>
        public string BlockId { get; set; } = string.Empty;

        /// <summary>Start offset inside the target block.</summary>
        public int Start { get; set; }

        /// <summary>End offset inside the target block.</summary>
        public int End { get; set; }

        /// <summary>Matched text.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Search result preview.</summary>
        public string Preview { get; set; } = string.Empty;
    }

    /// <summary>Canvas engine navigation state exposed to Blazor callers.</summary>
    public sealed class CanvasEngineNavigationState
    {
        /// <summary>Heading outline extracted from the canvas layout cache.</summary>
        public List<DocumentOutlineItem> Outline { get; set; } = [];

        /// <summary>Named bookmarks discovered in canvas inline marks.</summary>
        public List<CanvasEngineBookmarkState> Bookmarks { get; set; } = [];
    }

    /// <summary>Canvas engine bookmark state exposed to Blazor callers.</summary>
    public sealed class CanvasEngineBookmarkState
    {
        /// <summary>Bookmark name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Target block id.</summary>
        public string BlockId { get; set; } = string.Empty;

        /// <summary>Start offset inside the target block.</summary>
        public int Start { get; set; }

        /// <summary>End offset inside the target block.</summary>
        public int End { get; set; }
    }

    /// <summary>Canvas engine selection state exposed to Blazor callers.</summary>
    public sealed class CanvasEngineSelectionState
    {
        /// <summary>Whether the current selection is collapsed.</summary>
        public bool IsCollapsed { get; set; } = true;

        /// <summary>Current page index.</summary>
        public int PageIndex { get; set; }

        /// <summary>Anchor block id for the current text selection.</summary>
        public string AnchorBlockId { get; set; } = string.Empty;

        /// <summary>Anchor text offset for the current text selection.</summary>
        public int AnchorOffset { get; set; }

        /// <summary>Focus block id for the current text selection.</summary>
        public string FocusBlockId { get; set; } = string.Empty;

        /// <summary>Focus text offset for the current text selection.</summary>
        public int FocusOffset { get; set; }

        /// <summary>Whether the selection focus is inside a table cell.</summary>
        public bool InTable { get; set; }

        /// <summary>Focused table block identifier when the selection is inside a table.</summary>
        public string TableId { get; set; } = string.Empty;

        /// <summary>Focused table cell identifier when the selection is inside a table.</summary>
        public string CellId { get; set; } = string.Empty;

        /// <summary>Zero-based focused table row index.</summary>
        public int RowIndex { get; set; }

        /// <summary>Zero-based focused table cell index.</summary>
        public int CellIndex { get; set; }

        /// <summary>Editable region of the caret: "Body", "Header" or "Footer" (B6).</summary>
        public string Region { get; set; } = "Body";

        /// <summary>Header/footer scope (Primary/First/Even) when the caret is in a header/footer (B6).</summary>
        public string HeaderFooterScope { get; set; } = string.Empty;

        /// <summary>Whether the caret sits on an inline signing field (plan S2.11).</summary>
        public bool SigningFieldSelected { get; set; }

        /// <summary>The signing field at the caret, when one is selected.</summary>
        public CanvasEngineSigningFieldSelection? SigningField { get; set; }

        /// <summary>Whether the caret sits on a popover-eligible content control (perf plan N2).</summary>
        public bool ContentControlSelected { get; set; }

        /// <summary>The content control at the caret, when one is selected (perf plan N2).</summary>
        public CanvasEngineContentControlState? ContentControl { get; set; }

        /// <summary>
        /// Viewport rectangle of the selected field (signing field or content control), so the field popover can anchor to it.
        /// Null when no field is selected or its page is not mounted.
        /// </summary>
        public CanvasEngineViewportRect? FieldAnchor { get; set; }
    }

    /// <summary>A rectangle in viewport (client) coordinates, in CSS pixels.</summary>
    public sealed class CanvasEngineViewportRect
    {
        /// <summary>Left edge.</summary>
        public double Left { get; set; }

        /// <summary>Top edge.</summary>
        public double Top { get; set; }

        /// <summary>Width (at least 1).</summary>
        public double Width { get; set; }

        /// <summary>Height (at least 1).</summary>
        public double Height { get; set; }
    }

    /// <summary>
    /// Content control at the current selection, computed by the engine over the focused block only
    /// (perf plan N2) so the popover never needs the full-document marshal.
    /// </summary>
    public sealed class CanvasEngineContentControlState
    {
        /// <summary>Stable content control identifier.</summary>
        public string ControlId { get; set; } = string.Empty;

        /// <summary>Control kind name as reported by the engine (date/comboBox/dropDown/picture).</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Popover heading: alias, falling back to tag, then control id.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Whether the control value is required.</summary>
        public bool IsRequired { get; set; }

        /// <summary>Whether the control content is locked against editing.</summary>
        public bool LockContent { get; set; }

        /// <summary>Free-text value (combo box text).</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Selected choice value (drop-down / combo box).</summary>
        public string SelectedValue { get; set; } = string.Empty;

        /// <summary>ISO date value (date control).</summary>
        public string DateIso { get; set; } = string.Empty;

        /// <summary>Selected image asset id (picture control).</summary>
        public string AssetId { get; set; } = string.Empty;

        /// <summary>Available choices (drop-down / combo box).</summary>
        public List<DocumentContentControlItem> Items { get; set; } = [];
    }

    /// <summary>Signing field at the current selection, for the properties panel (plan S2.11/S2.21).</summary>
    public sealed class CanvasEngineSigningFieldSelection
    {
        /// <summary>Stable signing field identifier.</summary>
        public string Uuid { get; set; } = string.Empty;

        /// <summary>Field type name.</summary>
        public string FieldType { get; set; } = "text";

        /// <summary>Signer role identifier.</summary>
        public string SubmitterUuid { get; set; } = string.Empty;

        /// <summary>Whether the field is required.</summary>
        public bool Required { get; set; }

        /// <summary>User-facing label.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Owning header/footer id when the field lives in a header/footer (else empty).</summary>
        public string HeaderFooterId { get; set; } = string.Empty;

        /// <summary>Header/footer scope name (Primary/FirstPage/EvenPages/OddPages).</summary>
        public string Scope { get; set; } = string.Empty;

        /// <summary>Whether the field repeats on every page (header/footer field).</summary>
        public bool Repeats { get; set; }
    }

    /// <summary>Canvas engine diagnostics exposed to Blazor callers.</summary>
    public sealed class CanvasEngineDiagnosticsState
    {
        /// <summary>Renderer architecture name.</summary>
        public string ArchitectureName { get; set; } = "CanvasDocumentEngine";

        /// <summary>Page surface strategy name.</summary>
        public string PageSurfaceStrategy { get; set; } = "canvas-per-visible-page";

        /// <summary>Current rendered page count.</summary>
        public int PageCount { get; set; }

        /// <summary>Current spell/proofing diagnostic count.</summary>
        public int ProofingDiagnosticCount { get; set; }

        /// <summary>Current painted spell squiggle count.</summary>
        public int ProofingSquiggleCount { get; set; }
    }

    /// <summary>Canvas remote operation apply result exposed to Blazor callers.</summary>
    public sealed class CanvasEngineRemoteApplyResult
    {
        /// <summary>Whether every operation in the remote batch was applied.</summary>
        public bool Success { get; set; } = true;

        /// <summary>Whether the runtime model changed.</summary>
        public bool Changed { get; set; }

        /// <summary>Operation ids applied by the canvas runtime.</summary>
        public List<string> AppliedOperationIds { get; set; } = [];

        /// <summary>Operation ids rejected by the canvas runtime.</summary>
        public List<string> FailedOperationIds { get; set; } = [];

        /// <summary>Conflict details returned by the deterministic merge layer.</summary>
        public List<CanvasEngineRemoteConflict> Conflicts { get; set; } = [];
    }

    /// <summary>Canvas remote operation conflict detail exposed to Blazor callers.</summary>
    public sealed class CanvasEngineRemoteConflict
    {
        /// <summary>Operation id associated with the conflict.</summary>
        public string OperationId { get; set; } = string.Empty;

        /// <summary>Reason reported by the JavaScript merge layer.</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>Canvas presence overlay state exposed to Blazor callers.</summary>
    public sealed class CanvasEnginePresenceState
    {
        /// <summary>Remote cursor count rendered in the canvas overlay.</summary>
        public int CursorCount { get; set; }

        /// <summary>Rendered remote cursor geometry snapshots.</summary>
        public List<CanvasEnginePresenceCursor> Cursors { get; set; } = [];
    }

    /// <summary>Canvas presence cursor geometry exposed to Blazor callers.</summary>
    public sealed class CanvasEnginePresenceCursor
    {
        /// <summary>Remote collaboration session id.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Block id targeted by the remote cursor.</summary>
        public string BlockId { get; set; } = string.Empty;

        /// <summary>Remote cursor text offset.</summary>
        public int Offset { get; set; }

        /// <summary>Rendered page index.</summary>
        public int PageIndex { get; set; }

        /// <summary>Rendered x coordinate.</summary>
        public double X { get; set; }

        /// <summary>Rendered y coordinate.</summary>
        public double Y { get; set; }

        /// <summary>Resolved visual cursor color.</summary>
        public string Color { get; set; } = string.Empty;
    }

    /// <summary>Canvas engine changed state exposed to Blazor callers.</summary>
    public sealed class CanvasEngineChangedState
    {
        /// <summary>Whether the engine is dirty.</summary>
        public bool IsDirty { get; set; }

        /// <summary>Current model version.</summary>
        public long ModelVersion { get; set; }
    }

    /// <summary>
    /// Primitives-only UI snapshot pushed from the engine (see interop.mjs buildUiState) so the parent toolbar
    /// can update pressed-state / dirty / undo availability / page count without a follow-up interop pull.
    /// </summary>
    public sealed class CanvasEngineUiState
    {
        public CanvasEngineFormattingState? Formatting { get; set; }
        public bool IsDirty { get; set; }
        public bool CanUndo { get; set; }
        public bool CanRedo { get; set; }
        public int PageCount { get; set; }
        public long ModelVersion { get; set; }
    }

    /// <summary>Wrapper used to pluck just the pushed <see cref="CanvasEngineUiState"/> out of the selection payload.</summary>
    public sealed class CanvasEngineUiStateEnvelope
    {
        public CanvasEngineUiState? UiState { get; set; }
    }

    /// <summary>Live comment + revision lists pulled from the engine model (B3). The lists already use the
    /// canonical <see cref="DocumentComment"/> / <see cref="DocumentRevision"/> shapes, so they bind directly.</summary>
    public sealed class CanvasEngineAnnotations
    {
        public List<DocumentComment> Comments { get; set; } = [];
        public List<DocumentRevision> Revisions { get; set; } = [];

        /// <summary>Live document body word count (B6) — read from the engine, not the C# document mirror.</summary>
        public int WordCount { get; set; }

        /// <summary>Live page count from the engine layout (B6).</summary>
        public int PageCount { get; set; }

        /// <summary>Whether the progressive first layout has finished (perf plan N11.5); word and
        /// page counts only cover the laid-out prefix until then. Defaults to <c>true</c>.</summary>
        public bool LayoutComplete { get; set; } = true;
    }
}
