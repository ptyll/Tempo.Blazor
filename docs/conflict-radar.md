# TmConflictRadar — the agents' branches as one file × agent table

`TmConflictRadar` (package `Tempo.Blazor.AI`, namespace `Tempo.Blazor.Components.AI`) answers one question before a
merge: **which files did more than one agent touch, and which version does the team keep?** The agents' branches are
the columns, the changed files are the rows, and a cell names the change one agent made to one file.

Where the table's facts come from is *computed*, never drawn by hand: `ConflictMatrix` decides which paths exist, which
of them are conflicts, their order, their directory groups and whether the merge may be committed.

```razor
<TmConflictRadar Agents="_branches"
                 @bind-Resolutions="_resolutions"
                 OnMerge="OnMerge" />
```

## The models

All live in `Tempo.Blazor.Abstractions` (namespace `Tempo.Blazor.AI.Models`) and are **records**; `ChangeKind` and
`ConflictResolutionKind` are enums.

| `ChangedPath` | Meaning |
|---|---|
| `Path` | The path the file sits at now, relative to the repository root. **Required** — a blank path throws `ArgumentException`, because the path is the row key and the reader's decision is stored under it. Backslashes are normalised to `/`, so two agents reporting the same file with different separators still meet in ONE row. Case is kept as the host spelled it: folding `Foo.cs` into `foo.cs` would hide a change nobody resolved. |
| `Kind` (`ChangeKind`) | `Added`, `Modified`, `Deleted`, `Renamed`. The enum carries no text — the surface renders its own localized word and icon. |
| `PreviousPath` | Where a renamed file came from. Only meaningful for `Renamed`; every other kind stores `null`, because a previous path only means something for a move. |

Derived: `Directory` (`src/AI` for `src/AI/Foo.cs`, empty at the repository root), `FileName`, `IsRename`, and the
`Normalize(path)` helper.

| `AgentBranchChanges` | Meaning |
|---|---|
| `Agent` | The agent whose branch this is. **Required** (null throws `ArgumentNullException`). |
| `Branch` | The branch or worktree name the host reported. Blank is stored as empty and the surface prints its localized "No branch" instead of a gap. |
| `ChangedPaths` | The paths the agent touched, in the host's order. A path listed twice collapses to ONE entry — the **last** one wins, because a receipt that lists a file twice describes the newer fact second, and two cells for one file would claim a conflict the agent does not have. |

Derived: `HasBranch`, `HasChanges`, `Find(path)` (separator-insensitive).

| `ConflictResolution` | Meaning |
|---|---|
| `Path` | The conflicting file the decision is about. **Required**. The path is the key, so a file has at most one decision. |
| `Kind` (`ConflictResolutionKind`) | `Agent` (one agent's version wins) or `Manual` (the reader resolves the file themselves — a REAL decision, so the file is no longer unresolved). |
| `AgentId` | The winning agent. **Required** for `Agent` (a blank id throws `ArgumentException` — "keep the agent's side" without an agent would pick a winner by accident), and always `null` for `Manual`. |

Derived: `IsAgentSide`, `IsManual`, `Decides(path)`.

`ConflictMergeRequest` (`Files`, `Resolutions`, `FileCount`, `ResolvedConflictCount`, `ResolutionOf(path)`) is what the
merge action reports: **every** file the merge touches — conflict-free files included, because the host has to merge
them too — plus the accepted decision for each conflict.

## The matrix

`new ConflictMatrix(branches)` (or `ConflictMatrix.Empty`) is pure C# with no renderer:

- one **column** per agent that changed something, in the host's order. The same agent id reported twice (two
  worktrees, two receipts) is ONE column whose path lists are merged — dropping the second receipt would silently lose
  the files it names. An agent with no changes is not a column at all, because a column with no cells only widens the
  table;
- one **row** per touched path. `Cells` is aligned with `Columns` (index *i* is the agent at index *i*), and a cell is
  `null` where that agent did not touch the file — the matrix stores no "unchanged" cell, so no surface can paint a
  change that did not happen;
- a row is a **conflict** when two or more agents changed it, which is the whole definition. One agent on a file is
  work; two agents on it is a decision;
- **ordering**: conflicting rows first and, inside both groups, ordinal path order, so the same input always paints the
  same picture;
- **directory groups** (`Directories`) are derived from that row order, so the directory holding the first conflict is
  the first group. This is what keeps "group by directory" and "conflicts on top" from fighting each other: the group
  order follows the rows, not the other way round. The groups are flat by design — a nested tree would hide a deep
  folder's conflicts behind three disclosures;
- **readiness**: `Unresolved(resolutions)` lists every conflicting row whose decision is missing **or whose decision
  names an agent that does not change that file** (a decision that would keep a version nobody wrote).
  `IsReadyToMerge(resolutions)` is true when that list is empty, and `BuildMergeRequest(resolutions)` returns the
  request the surface reports.

`ConflictMatrixRow` (path, cells, `IsConflict`, `AgentCount`, `ConflictingAgents`, `Settles(resolution)`),
`ConflictMatrixCell` (agent, change, `Kind`, `PreviousPath`, `IsRename`) and `ConflictMatrixDirectory` (`Directory`,
`Rows`, `RowCount`, `ConflictCount`, `HasConflicts`) are the shapes the table paints.

## The surface

| Parameter | Meaning |
|---|---|
| `Agents` | `IReadOnlyList<AgentBranchChanges>?` — one entry per branch. Null or empty renders the localized empty state. |
| `Resolutions` / `ResolutionsChanged` | The reader's decisions, two-way (`@bind-Resolutions`). Null means nothing is decided yet, so the merge stays unavailable. The reader's pick wins until the host supplies a **new list instance**; a host that echoes the same instance cannot undo it. |
| `OnMerge` | `EventCallback<ConflictMergeRequest>` — raised once per committed merge. The surface changes nothing itself. |
| `ConflictOnly` / `ConflictOnlyChanged` | The "Only conflicts" filter, two-way. Default `false`. The summary keeps reporting the WHOLE picture, so a reader knows what the filter hid. |
| `Title`, `ShowTitle`, `HeadingLevel`, `AriaLabel`, `Class`, `AdditionalAttributes` | The usual chrome. The heading is composed at the requested level (clamped 2–6), because Razor cannot build a dynamic element name. |

**The table** is a NATIVE `<table>` and declares no ARIA role: a header cell per agent (its `TmAgentBadge` and its
branch, `scope="col"`), a `<th scope="rowgroup">` per directory group and a `<th scope="row">` per file. The file column
is sticky, so it stays readable while many agents scroll past it. Each cell renders the change as an icon AND a word
(`Added`, `Modified`, `Deleted`, `Renamed`, `was src/Old.cs`), and an untouched file is an em dash plus the spoken words
"No change" — never an empty box a reader has to interpret. A conflicting row is painted with the danger frame, a
"Conflict" badge with an icon and the danger tone; the tone is the second cue and the word is the first.

**No `role="grid"`.** The roles a table already carries (`row` / `columnheader` / `rowheader` / `cell`) are not
re-declared, and neither are `aria-rowcount` / `aria-colcount` (the counts change with the filter and the collapsed
directories). A `role="grid"` table with no roving tabindex and no arrow-key handling makes NVDA/JAWS switch to focus
mode, swallow the table reading commands and expect keys the surface does not answer; the native table semantics are
what a reader's table navigation needs. A conflicting file's side choice is a separate row, so its cell names the file's
row header through `headers` — an anonymous row would be read as unrelated to the file above it.

**The side choice** is one native radio group per conflicting file (its options are the agents that really changed the
file plus "Merge by hand"), so the browser's own arrow keys apply and each file is its own group. Radios are re-mounted
(`@key` with a version counter) when the host replaces the decision list, so a host-driven revert cannot leave a stale
checked mark in the browser.

**The merge action** is disabled — and its hint states how many conflicts are still open — until every conflict carries
an accepted decision, and it is ready immediately when the agents do not conflict at all. A merge with no files at all
is disabled, because merging nothing is not an action.

**Directories** collapse and expand from a native button in their group header, which states its state with
`aria-expanded` and its counts in words ("2 files · 1 conflict").

## Accessibility

- every state is a word plus an icon (conflict, each change kind), never a colour alone;
- the side choice is a native radio group, the directories are native buttons and the filter is the core `TmToggle` —
  no key is emulated, and no control is drag-only (WCAG 2.5.7);
- the surface owns exactly ONE polite live region (`tm-sr-only`, `aria-live="polite"`) and announces a resolution, a
  merge, the filter change and a collapsed directory; an identical sentence is re-announced through an invisible marker
  because a live region announces a *mutation*;
- `(pointer: coarse)` raises the side LABELS, the merge action and the directory buttons to the 44 px
  `--tm-touch-target` — the label around the radio grows, the glyph still looks like a radio;
- `forced-colors: active` hands the frame, the toned words and the merge action to `CanvasText`/`Canvas`/`GrayText` and
  every focus ring to a 2 px `Highlight` outline, while the words keep carrying the meaning; the block declares no
  `--tm-*` colour at all (a token there would paint the design system instead of the reader's palette);
- focus rings are real outlines (`--tm-border-color-focus`) on the merge action, the directory buttons and the radios;
- the table does **not** declare `role="grid"` and does not implement a roving tabindex: every interactive cell is a
  native control, and a grid role would take the arrow keys those controls need. Reading order is the table's;
- the side-choice labels are 24 px tall with a `--tm-space-2` row gap, so a wrapped row keeps the WCAG 2.5.8 spacing
  exemption;

The stylesheet (`wwwroot/css/components/_ai-conflict-radar.css`) declares no transition and no animation — a row that
slid in would be motion without information, and a conflict has to be readable in a screenshot. Guarded by
`AiMarkdownViewCssTests` and `TmConflictRadarCssTests`; every class the markup emits has a rule.

## Localization

`TmConflictRadar_*` in English, Czech, Slovak and French: the title and area label, the summary, the instructions, the
filter, "Merge", the ready and unresolved sentences (a plural family), the file column, "Conflict", "No change", the
four change words, "was {0}", "Repository root", "No branch", the resolve label, "Merge by hand", the directory meta
format, the file/conflict count plural families, both empty states and the announcements.

## Demo

`/ai-agents` renders three agents on three branches over the same repository (two paths changed by two agents each, one
by a third pair) and is the STORE of the reader's decisions: the page echoes the reported list back and turns the merge
request into a log line, because the demo has no repository.
