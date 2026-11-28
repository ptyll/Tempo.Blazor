using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.DocumentEditor.Features;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Components.Layout;

namespace Tempo.Blazor.Components.DocumentEditor;

// Redesign 146c7e1b, phase 8 (DE-2.7 + the phone parts of DE-1.1 / DE-1.2): the action bar of a phone. The built-in actions the compact
// header no longer carries - Comments, Print preview, Save and a "More" menu with the rest - are the core TmMobileActionBar's tiles; the
// bar is the core component, only the list of actions is the editor's.
public partial class TmDocumentEditor
{
    // The tiles that stay on the bar (the primary action Save is the third); the rest of the list overflows into the "More" sheet.
    private const int PhoneActionBarVisible = 3;

    // One badge, one number: the review summary banner of the doc region is not rendered on a phone. The Comments tile counts what it opens (the open
    // threads, read as "N open comments"); the pending changes are the badge of the Revisions tab in the sheet.

    // A phone container narrower than 340 px (a 320 px viewport; a 390 px one measures about 358) keeps the title on the header row by sending Undo / Redo to the kebab (the width is the measured one; a pinned layout has none).
    private bool IsDensePhoneHeader => IsMobileLayout && _containerWidth > 0 && _containerWidth < 340;

    private static string? BadgeText(int count) => count <= 0 ? null : count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    // The words behind the number ("3 items to review"): the tile reads them after its label, the pill itself is decorative.
    private string? BadgeLabel(int count) => count <= 0 ? null : Loc.Plural("TmDocumentEditor_ReviewBadge", count);

    private IReadOnlyList<TmActionItem> PhoneActionItems
    {
        get
        {
            var quick = DocumentQuickActionState.Resolve(_commandRegistry, EffectiveReadOnly, _isSaving, EffectiveUndoState.CanUndo, EffectiveUndoState.CanRedo);
            var items = new List<TmActionItem>();
            var showComments = ShowComments && IsFeatureEnabled(DocumentEditorFeatureNames.Comments);
            var showRevisions = IsFeatureEnabled(DocumentEditorFeatureNames.TrackChanges) || IsCanvasSuggestionMode;
            if (showComments)
            {
                items.Add(new()
                {
                    Id = "comments",
                    TestId = TestId("document-mobile-comments"),
                    Label = Loc["TmDocumentEditor_Comments"],
                    Icon = IconNames.MessageSquare,
                    // The Comments tile counts what it opens - the open threads; a pending change is counted on the Revisions tab of the sheet.
                    Badge = BadgeText(OpenCommentCount),
                    BadgeLabel = OpenCommentCount > 0 ? Loc.Plural("TmDocumentEditor_ReviewSummaryOpenComments", OpenCommentCount) : null,
                    Priority = 100,
                    Overflow = ActionOverflow.Never,
                    OnClick = EventCallback.Factory.Create(this, OpenCommentsPanelAsync)
                });
            }
            else if (showRevisions)
            {
                // Without comments the review tile is the revisions one (a tile for a panel that does not exist would be a dead end).
                items.Add(new()
                {
                    Id = "revisions",
                    TestId = TestId("document-mobile-revisions"),
                    Label = Loc["TmDocumentEditor_Revisions"],
                    Icon = IconNames.FileDiff,
                    Badge = BadgeText(PendingRevisionCount),
                    BadgeLabel = BadgeLabel(PendingRevisionCount),
                    Priority = 100,
                    Overflow = ActionOverflow.Never,
                    OnClick = EventCallback.Factory.Create(this, OpenRevisionsPanelAsync)
                });
            }

            items.Add(new()
            {
                Id = "print-preview",
                TestId = TestId("document-mobile-print-preview"),
                // The tile is two short lines wide: its label is the short form of the command's name (French "Aperçu avant impression" would be cut).
                Label = Loc["TmDocumentEditor_PrintPreviewTile"],
                Icon = IconNames.BookOpen,
                // The availability the header button had: the preview needs the canvas engine and an enabled command.
                Disabled = !UsingCanvasEngine || !IsCommandEnabled("openPrintPreview", true),
                Priority = 90,
                Overflow = ActionOverflow.Never,
                OnClick = EventCallback.Factory.Create(this, OpenCanvasPrintPreviewAsync)
            });
            items.Add(new()
            {
                Id = "save",
                TestId = TestId("document-save"),
                Label = Loc["TmDocumentEditor_Save"],
                Icon = IconNames.Save,
                Primary = true,
                Disabled = !quick.CanSave,
                Priority = 95,
                Overflow = ActionOverflow.Never,
                OnClick = EventCallback.Factory.Create(this, SaveAsync)
            });

            // "More": the panels and the search that have no tile. (Print to PDF is behind the header kebab only; the bar-only badge is not repeated here.)
            items.Add(new()
            {
                Id = "find",
                TestId = TestId("document-mobile-find"),
                Label = Loc["TmDocumentEditor_HeaderSearch"],
                Icon = IconNames.Search,
                Overflow = ActionOverflow.Always,
                OnClick = EventCallback.Factory.Create(this, OpenFindFromChromeAsync)
            });

            if (showRevisions && showComments)
            {
                items.Add(new()
                {
                    Id = "revisions",
                    TestId = TestId("document-mobile-revisions"),
                    Label = Loc["TmDocumentEditor_Revisions"],
                    Icon = IconNames.FileDiff,
                    Overflow = ActionOverflow.Always,
                    OnClick = EventCallback.Factory.Create(this, OpenRevisionsPanelAsync)
                });
            }

            if (ShowVersionHistory)
            {
                items.Add(new()
                {
                    Id = "versions",
                    TestId = TestId("document-mobile-versions"),
                    Label = Loc["TmDocumentEditor_Versions"],
                    Icon = IconNames.Clock,
                    Overflow = ActionOverflow.Always,
                    OnClick = EventCallback.Factory.Create(this, OpenVersionsPanelAsync)
                });
            }

            items.Add(new()
            {
                Id = "outline",
                TestId = TestId("document-mobile-outline"),
                Label = Loc["TmDocumentEditor_Outline"],
                Icon = IconNames.List,
                Overflow = ActionOverflow.Always,
                OnClick = EventCallback.Factory.Create(this, () => OpenNavigationSheetAsync(DocumentSidePanelTab.Outline))
            });
            items.Add(new()
            {
                Id = "pages",
                TestId = TestId("document-mobile-pages"),
                Label = Loc["TmDocumentEditor_Pages"],
                Icon = IconNames.Copy,
                Overflow = ActionOverflow.Always,
                OnClick = EventCallback.Factory.Create(this, () => OpenNavigationSheetAsync(DocumentSidePanelTab.Pages))
            });
            items.Add(new()
            {
                Id = "properties",
                TestId = TestId("document-mobile-properties"),
                Label = Loc["TmDocumentEditor_Properties"],
                Icon = IconNames.Settings,
                Overflow = ActionOverflow.Always,
                OnClick = EventCallback.Factory.Create(this, () => OpenNavigationSheetAsync(DocumentSidePanelTab.Properties))
            });

            return items;
        }
    }

    private Task OpenNavigationSheetAsync(DocumentSidePanelTab tab)
    {
        OpenSidePanel(tab, manual: true);
        return InvokeAsync(StateHasChanged);
    }
}
