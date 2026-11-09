# Tempo.Blazor.NotionEditor

Notion-style page editor for Blazor (WebAssembly, Server, InteractiveAuto): the shell, the page,
the sidebar, the block catalogue and the database views.

The contract — layout bands, overlays, comment presentation, frozen test ids, tokens and the
JavaScript assets — is [docs/notion-editor.md](../../docs/notion-editor.md). Parameter lists live
in the XML docs and in [COMPONENTS.md](../../COMPONENTS.md).

## Installation

```bash
dotnet add package Tempo.Blazor.NotionEditor
```

```csharp
builder.Services.AddTempoBlazor();
```

```html
<link rel="stylesheet" href="_content/Tempo.Blazor/css/tempo-blazor.css" />
<link rel="stylesheet" href="_content/Tempo.Blazor.NotionEditor/css/tempo-blazor-notion-editor.css" />
<script src="_content/Tempo.Blazor.NotionEditor/js/notion-editor.js"></script>
```

Column resize and the timeline drag are ES modules the views import themselves. Both import the
core `pointer-drag.js` by rewriting their own URL, so a host must also serve
`_content/Tempo.Blazor/js/`. A per-file CSP hash must list `pointer-drag.js` beside
`notion-editor.js`, `notion-db-column-resize.js` and `notion-db-timeline.js`.

## Hosting

```razor
<TmNotionEditor DataProvider="@provider"
                LayoutMode="TmLayoutMode.Auto"
                Chrome="NotionEditorChrome.Full" />
```

| Parameter | Default | Effect |
|---|---|---|
| `LayoutMode` | `Auto` | Desktop ≥ 1024, tablet 640–1023, mobile < 640 of the editor container. The first frame is Desktop. |
| `Chrome` | `Full` | `Minimal` is a public read-only page; `None` is the page only. |
| `InspectorContent` | `null` | Host right slot. Null renders no right region. |
| `ShellPanelPresentation` | `Overlay` | Tasks / Blog / Analytics / Audit: docked or sheeted, page stays mounted. `Main` replaces the page. |
| `CommentPresentation` | `Auto` | Block and text comments: popover on desktop, `TmModal` on touch and narrow. `Popover` pins the bubble. |
| `PageCommentsPresentation` | `Inline` | `Dialog` opens the page comments in a `TmModal` instead of the bottom panel. |

A phone keeps reading and light edits (add a block, comment, open My tasks, edit a cell). Column
drag, column resize and timeline bar drag are not offered on a coarse pointer or a mobile layout;
each has a keyboard or panel path. Pin `LayoutMode="TmLayoutMode.Tablet"` for the full editing
chrome inside a narrow box.

Localization keys live in the core tables (`TmResources.json` and `.cs` / `.sk` / `.fr`). A host
override replaces them through `ITmLocalizer`.
