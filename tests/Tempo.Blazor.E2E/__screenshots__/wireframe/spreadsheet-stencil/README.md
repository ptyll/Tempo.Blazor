# Wireframe `TmSpreadsheet` stencil captures

Produced by `WireframeSpreadsheetStencilE2ETests` (spreadsheet redesign plan 8959eebe, phase 8 — the
manual remaining item carried out of phase 7d). The three files are the stencil's own chrome at its
three presentations, rendered through `HtmlRenderer` and captured as the SVG element, so they never
depend on a demo host, the viewport or a theme:

| File | Stencil `layoutMode` | Chrome it proves |
|------|----------------------|------------------|
| `desktop.png` | `desktop` | the tab strip (Home / Insert / Data / View) + labelled group bands (Clipboard … Insert), the opt-in document header when `showHeader` is set, the status bar |
| `tablet.png` | `tablet` | TWO compact command rows, no tab strip, status bar |
| `mobile.png` | `mobile` | the compact header (title + ⋮), ONE command row, the sheet tabs and the core mobile action bar instead of the status bar |

`layoutMode="auto"` — and any unknown value — renders the desktop variant (decision X11: a stencil
communicates the chosen presentation, it does not re-flow its canvas), which
`SpreadsheetStencil_DesktopTabletMobile_RenderTheirOwnChrome` asserts alongside the three captures.

Never copy these by hand: re-running the spec regenerates them.
