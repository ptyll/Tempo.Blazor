# Spreadsheet dialogs

Every dialog of `Tempo.Blazor.Spreadsheet` renders through the **core `TmModal`** (the redesign's G6
decision). The package paints content, the words and the footer actions only: the frame owns the fixed
overlay, the dialog role, `aria-modal`, the accessible name, the initial focus, the document-level
Escape, the focus restore and the mobile presentation. Nothing in the package declares a backdrop any
more — no `.tm-spreadsheet-dialog-backdrop`, no per-dialog `__backdrop`, no `position: fixed` card.

| Dialog | Root class (on the modal overlay) | Size | Mobile presentation |
| --- | --- | --- | --- |
| Sort | `.tm-spreadsheet-sort` | Medium | sheet |
| Custom filter | `.tm-spreadsheet-custom-filter` | Medium | sheet |
| Remove duplicates | `.tm-spreadsheet-dedup` | Medium | sheet |
| Paste special | `.tm-spreadsheet-pastespecial` | Medium | sheet |
| Text to columns | `.tm-spreadsheet-t2c` | Large | **fullscreen** |
| Hyperlink | `.tm-spreadsheet-hyperlink` | Medium | sheet |
| Name Manager | `.tm-spreadsheet-name-manager` | Large | **fullscreen** |
| Named range edit | `.tm-spreadsheet-named-range-edit` | Medium | sheet |
| Data validation | `.tm-dvd` | Medium | sheet |
| Format Cells | `.tm-fcd` | Large | **fullscreen** |
| Insert image | `.tm-spreadsheet-insert-image` | Small | sheet |
| Validation alert | `.tm-spreadsheet-alert` (+ `--stop` / `--warning` / `--information`) | Small | sheet |
| Column width / row height | `.tm-spreadsheet-resize-dialog` | Small | sheet |

Root class on the **overlay**, not on the card: `.tm-spreadsheet-name-manager` is the fixed overlay and
`.tm-spreadsheet-name-manager .tm-modal` is the card. An E2E spec or a host that located a dialog by its
class therefore still finds exactly one element per dialog, but a rule that styled the old card
(`width`, `padding`, `box-shadow`) must move to the modal's `Size` or be scoped as
`<root> .tm-modal`.

## The frame's contract

- **Name and description.** `Title` renders as `.tm-modal-title` and is wired to the scope's
  `aria-labelledby`. The validation alert additionally sets `AriaDescribedBy` to its message, renders
  `role="alertdialog"` (`TmModal.DialogRole`) and hides the header close button.
- **Initial focus.** Each dialog passes `InitialFocusTargetId`: `nr-name` (named range edit),
  `hl-target` (hyperlink), the URL field (insert image), the Add-level button (sort), the first operator
  (custom filter), the first column checkbox (remove duplicates), the first paste option, the selected
  name row or the New button (Name Manager), the primary action (alerts). The rest fall back to the
  frame's first focusable.
- **Escape and the restore target.** Escape is the frame's document-level listener; the package never
  re-handles it. Every dialog receives `RestoreFocusTargetId` from `TmSpreadsheet` (its grid root), so a
  closed dialog puts focus back into the grid. The named-range editor is the one exception: it is opened
  from the Name Manager, which stays open underneath, so it restores focus to its OPENER inside the
  manager (the frame's default) — restoring to the grid would target an element the manager keeps inert.
  Closing the manager then returns focus to the grid.
- **Ctrl+Enter** applies the named-range editor, the hyperlink dialog and Format Cells. The shortcut
  lives on the form wrapper inside the modal body, never on the modal root: the footer buttons are the
  frame's own and sit outside that wrapper, so their keydown produces exactly one activation — their
  native click.
- **Stacking.** Two dialogs can be open at once (Name Manager + Named range edit). Each is a separate
  modal, so exactly one backdrop paints (no double dimming) and Escape reaches only the topmost trap.
- **Mobile presentation.** `MobilePresentation.Auto` for the short forms → a bottom sheet on a mobile
  layout; the three tall surfaces pass the core frame's `MobilePresentation.AutoFullscreen` (fullscreen
  on mobile, the centred dialog everywhere else) — the frame resolves it from its own viewport scope,
  the package measures nothing. Each dialog exposes `LayoutMode` to pin the mode for a test or a host.
  `TmSpreadsheetCanvasGrid`'s column-width / row-height dialogs are dialogs of the package too, so they
  ride the same frame (they were the last two with a private backdrop).

## Embedding: a dialog over the Notion modal

The Spreadsheet is embedded in a Notion block and in `TmNotionSpreadsheetEditModal`. Because every
Spreadsheet dialog is now a `TmModal`, it promotes itself to the browser **top layer**
(`popover="manual"`), so the dialog opened from inside a Notion modal paints above it without a
`z-index` arms race, the Notion dialog's trap hands the keyboard over to the innermost scope, and
closing the Spreadsheet dialog restores focus to the grid inside the (still open) Notion modal. Before
the migration the package painted in its own `--tm-z-modal` band and could end up under a promoted core
surface — the recorded exception in `docs/overlays.md` rule 9 no longer applies to this package.

## Find & Replace is not a modal

Search must keep the matched cells visible, so `TmSpreadsheetFindReplaceDialog` stays non-modal (Q5):
a floating panel on the desktop layout and the same panel inside `TmDrawer` with `Modal="false"` on the
mobile layout (grabber, Escape closes, no backdrop, the grid stays operable). `Ctrl+F` / `Ctrl+H`,
`Enter` / `Shift+Enter` / `Escape` and the `.tm-spreadsheet-find__*` classes are unchanged.

## Validation alerts

`TmSpreadsheetValidationAlert` is severity-driven (`SpreadsheetValidationErrorStyle`):

| Severity | Icon colour | Default title / message | Actions |
| --- | --- | --- | --- |
| `Stop` | `--tm-color-danger` | `…DefaultErrorTitle` / `…DefaultErrorMessage` | `TmSpreadsheet_Validation_Acknowledge` ("Got it"); a backdrop click does nothing |
| `Warning` | `--tm-color-warning` | `…DefaultWarningTitle` / `…DefaultWarningMessage` | Cancel + `TmSpreadsheet_Validation_KeepValue` ("Use anyway"); a backdrop click cancels |
| `Information` | `--tm-color-info` | `…DefaultInfoTitle` / `…DefaultInfoMessage` | Cancel + acknowledge |

A rule's own `ErrorAlert.Title`/`Message` still wins; the severity picks the icon and the defaults, so an
Information message no longer reads as a warning.

## Breaking change

The migration from the three interim dialog families (own backdrops, the shared
`.tm-spreadsheet-dialog-backdrop` wrapper, the inline `tm-fcd`/`tm-dvd` blocks) is documented with
before/after snippets in `CHANGELOG.md` → 2.10.0 → Breaking / Migration.
