// Shapes the engine's command-state (from commandRuntime.queryCommandState({includeNavigation:false}))
// into the flat formatting record the C# toolbar consumes (CanvasEngineFormattingState). Kept pure +
// dependency-free so it can be reused by both the interop pull (getFormattingStateJson) and the pushed
// UI-state payload (buildUiState) and unit-tested in isolation.
export function buildFormattingState(formatting) {
    const f = formatting || {};
    const commands = f.commands || {};
    const paragraph = f.paragraph || {};
    const view = f.view || {};
    return {
        bold: commands.bold?.active === true,
        boldMixed: commands.bold?.mixed === true,
        italic: commands.italic?.active === true,
        italicMixed: commands.italic?.mixed === true,
        underline: commands.underline?.active === true,
        underlineMixed: commands.underline?.mixed === true,
        strikethrough: commands.strikethrough?.active === true,
        strikethroughMixed: commands.strikethrough?.mixed === true,
        superscript: commands.superscript?.active === true,
        superscriptMixed: commands.superscript?.mixed === true,
        subscript: commands.subscript?.active === true,
        subscriptMixed: commands.subscript?.mixed === true,
        smallCaps: commands.smallcaps?.active === true,
        smallCapsMixed: commands.smallcaps?.mixed === true,
        allCaps: commands.allcaps?.active === true,
        allCapsMixed: commands.allcaps?.mixed === true,
        doubleStrikethrough: commands.doublestrikethrough?.active === true,
        doubleStrikethroughMixed: commands.doublestrikethrough?.mixed === true,
        fontFamily: commands.fontfamily?.value || '',
        fontFamilyMixed: commands.fontfamily?.mixed === true,
        fontSize: commands.fontsize?.value || '',
        fontSizeMixed: commands.fontsize?.mixed === true,
        textColor: commands.textcolor?.value || '',
        textColorMixed: commands.textcolor?.mixed === true,
        highlightColor: commands.highlight?.value || '',
        highlightColorMixed: commands.highlight?.mixed === true,
        alignment: commands.align?.value || paragraph.alignment || 'left',
        alignmentMixed: commands.align?.mixed === true || paragraph.alignmentMixed === true,
        lineSpacing: Number(commands.lineSpacing?.value ?? paragraph.lineSpacing ?? 1) || 1,
        lineSpacingMixed: commands.lineSpacing?.mixed === true || paragraph.lineSpacingMixed === true,
        spacingBefore: Number(commands.spacingBefore?.value ?? paragraph.spacingBefore ?? 0) || 0,
        spacingBeforeMixed: commands.spacingBefore?.mixed === true || paragraph.spacingBeforeMixed === true,
        spacingAfter: Number(commands.spacingAfter?.value ?? paragraph.spacingAfter ?? 0) || 0,
        spacingAfterMixed: commands.spacingAfter?.mixed === true || paragraph.spacingAfterMixed === true,
        leftIndent: Number(paragraph.leftIndent ?? 0) || 0,
        leftIndentMixed: paragraph.leftIndentMixed === true,
        bulletList: commands.bulletList?.active === true || paragraph.bulletList === true,
        numberedList: commands.numberedList?.active === true || paragraph.numberedList === true,
        listMixed: commands.bulletList?.mixed === true || commands.numberedList?.mixed === true || paragraph.listMixed === true,
        blockStyle: commands.blockStyle?.value || paragraph.blockStyle || 'Normal',
        blockStyleMixed: commands.blockStyle?.mixed === true || paragraph.blockStyleMixed === true,
        showRuler: commands.showRuler?.active !== false,
        showBlocks: commands.showBlocks?.active === true,
        showNonPrintingCharacters: commands.toggleNonPrintingCharacters?.active === true,
        viewMode: view.viewMode || view.mode || 'print',
        zoomPercent: Number(view.zoomPercent || view.zoom?.percent || 100) || 100,
        zoomPreset: view.zoomPreset || view.zoom?.preset || 'custom',
        zoomAutoFit: view.zoomAutoFit || view.zoom?.autoFit || 'off',
        toolbarHidden: view.toolbarHidden === true,
        printPreviewActive: view.printPreview?.active === true,
        image: f.image || null,
    };
}
