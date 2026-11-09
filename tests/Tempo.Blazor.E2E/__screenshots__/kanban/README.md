# Kanban board screenshots

Element screenshots of the `/kanban` demo boards, captured at a 1920px viewport (the demo page's
body is capped at `max-w-7xl`, so the captured board is 1280px wide — the same width a host gets at
that viewport; the fifth column is the board's own horizontal scroll, not a capture artefact).

They are **written by the test itself** (`KanbanVisualE2ETests` calls `locator.ScreenshotAsync`
straight into this folder, like the `otp-input` and `toasts` reference sets), so a local run of that
class refreshes them. Every state is captured in BOTH themes through the demo's own theme toggle.

| File | Producing test |
|---|---|
| `kanban-board-1920-light.png` / `-dark.png` | `KanbanVisualE2ETests.ExpandedBoard_BothThemes` |
| `kanban-column-collapsed-1920-light.png` / `-dark.png` | `KanbanVisualE2ETests.CollapsedColumn_BothThemes` |
| `kanban-swimlane-lane-collapsed-1920-light.png` / `-dark.png` | `KanbanVisualE2ETests.CollapsedLane_BothThemes` |
| `kanban-swimlane-column-collapsed-1920-light.png` / `-dark.png` | `KanbanVisualE2ETests.CollapsedColumnInTheSwimlaneLayout_BothThemes` |
| `kanban-collapsed-drop-target-1920-light.png` / `-dark.png` | `KanbanVisualE2ETests.DropTargetOnACollapsedColumn_BothThemes` (in flight — the drag is never released) |
| `kanban-collapsed-over-limit-1920-light.png` / `-dark.png` | `KanbanVisualE2ETests.OverLimitCollapsedColumn_BothThemes` (two drops onto the collapsed `review` strip, limit 2) |
| `kanban-focus-toggle-1920-light.png` / `-dark.png` | `KanbanVisualE2ETests.FocusRingOnTheCollapseToggle_BothThemes` (focus parked with real `Tab` presses) |
| `kanban-focus-card-1920-light.png` / `-dark.png` | `KanbanVisualE2ETests.FocusRingOnACard_BothThemes` (same keyboard-only path) |

The geometry these states imply is asserted by `KanbanCollapsedLayoutE2ETests` (header cell, lane
cells and collapsed-lane slots share one left edge and width before and after a collapse) and the CSS
contract by `TmKanbanCollapseCssTests`; the captures are the visual reference for reviewing that work,
not a pixel baseline.
