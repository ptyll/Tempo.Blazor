# TmAgentRunTree — one agent run as a treegrid

`TmAgentRunTree` (namespace `Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) shows the agents,
sub-agents, tool calls and steps of one run as a tree, with each node's state, model, elapsed time, cost and
tokens — and, on a parent row, the totals of everything under it.

```razor
<TmAgentRunTree Nodes="@nodes"
                @bind-SelectedNodeId="selectedId"
                Title="Nightly reconciliation">
    <DetailTemplate Context="node">
        <p>@node.Title — @node.Model</p>
    </DetailTemplate>
</TmAgentRunTree>

@code {
    private IReadOnlyList<AgentRunNode> nodes = [
        new("Reconciliation agent", "root") { Status = AgentStepStatus.Running, Model = "gpt-4o" },
        new("Plan the reconciliation", "planner") { ParentId = "root", Model = "gpt-4o",
            StartedAt = start, CompletedAt = start.AddSeconds(12), CostMicroUnits = 500_000, Tokens = 400 },
        new("Look up the ledger", "lookup") { ParentId = "planner", Kind = AgentRunNodeKind.Tool,
            Status = AgentStepStatus.Failed, Error = "The ledger API answered 503." },
    ];
}
```

The list is **flat** and the tree comes from `AgentRunNode.ParentId`. The component never mutates `Nodes`; it
renders the host's snapshot and owns only the reader's view state (the filter, the collapsed rows and — when
the host does not bind it — the selection).

## API

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `Nodes` | `IReadOnlyList<AgentRunNode>?` | `null` | The run, as a flat list. Null/empty renders `EmptyContent` or the localized empty sentence. |
| `SelectedNodeId` / `SelectedNodeIdChanged` | `string?` / `EventCallback<string?>` | `null` | Two-way selection (`@bind-SelectedNodeId`). A row click, Enter or Space reports it. |
| `DetailTemplate` | `RenderFragment<AgentRunNode>?` | `null` | The detail panel of the selected node, in a named `region` below the grid. |
| `ShowOnly` / `ShowOnlyChanged` | `AgentRunFilter` / `EventCallback<AgentRunFilter>` | `All` | The "show only" filter (`All` \| `Running` \| `Failed`); the built-in control raises the change. |
| `ShowFilter` | `bool` | `true` | Renders the filter control. The filter still applies when the host pins `ShowOnly`. |
| `Title` | `string?` | `null` | Heading text; the grid is named by it. |
| `ShowTitle` | `bool` | `true` | `false` keeps the grid named by `AriaLabel` or the localized default. |
| `HeadingLevel` | `int` | `3` | Heading level of the rendered title, clamped to 2–6. |
| `ShowModel`, `ShowTime`, `ShowCost`, `ShowTokens` | `bool` | `true` | The optional metric columns. |
| `CostFormatter` | `Func<long, string>?` | `null` | Formats a micro-unit cost. Null uses `AiCostFormatter` (the culture's currency). |
| `Culture` | `CultureInfo?` | `null` | Formats costs and token counts; null means the current culture. Durations are clock-style everywhere. |
| `TimeProvider` | `TimeProvider?` | `null` | The clock of the live timers; null uses the registered `TimeProvider`, then `System`. |
| `EmptyContent` | `RenderFragment?` | `null` | Replaces the localized "No nodes yet." |
| `AriaLabel` | `string?` | `null` | Explicit accessible name of the grid. |
| `Class` | `string?` | `null` | Extra root classes. |
| `DataTestId` / `TestIdPrefix` | from `TmComponentBase` | — | Standard test-id namespacing. |

Public members: `Announcement`, `Filter`, `VisibleRowCount`, `SelectedNode`, `SelectNodeAsync(string?)` and
`SetCollapsedAsync(string, bool)`.

## The model is a value, and the input is untrusted

`AgentRunNode` (`Id`, `ParentId`, `Title`, `Kind`, `Status`, `Model`, `StartedAt`, `CompletedAt`,
`CostMicroUnits`, `Tokens`, `Error`, computed `IsFinished` / `Duration` / `Elapsed(now)`) is a record: a host
recomposes a node with `node with { … }` as the run moves on. Cost is stored in **micro-units**
(`1_000_000` is `1.00`), the smallest unit an LLM bill is usually quoted in.

A malformed run is handled, never thrown:

* a node whose `ParentId` names no supplied node is an **orphan** attached under the root and marked with the
  localized "Orphan" badge;
* a parent cycle (including a node parented to itself) is **rejected**: its members render no row, their
  children become orphans, and nothing loops;
* a duplicate `Id` is read once (the first node wins), so the list never produces two rows with one `@key`.

## Roll-ups

A parent row shows the SUM over its subtree for the time, cost and token columns, prefixed with a decorative
`Σ`, marked `data-rollup="true"` and named in full for a screen reader ("Total for {node} including its
sub-agents: …") — the visible glyph alone is a private shorthand. A subtree that reports NO value for a column
renders a plain em dash with no `Σ` and no summary name: a total marker over "not reported" would claim a
number that does not exist.

The arithmetic is the **public** `AgentRunRollup` helper, so a host computing the same numbers elsewhere
cannot drift from the row:

```csharp
var rollup = AgentRunRollup.For("root", nodes, DateTimeOffset.UtcNow);
// rollup.CostMicroUnits, rollup.Tokens, rollup.Duration, rollup.NodeCount, rollup.DescendantCount, rollup.IsSummary
var all = AgentRunRollup.ForAll(nodes, now); // one pass, one entry per node
```

Each value keeps the "not reported" contract: a value no node of the subtree reports is `null`, never zero.
A running node with no `CompletedAt` contributes the time from its `StartedAt` to the "now" the caller passes,
so the total mixes recorded durations and one live estimate exactly as the screen does.

## Keyboard and accessibility

* The surface is a `role="treegrid"` with `row` / `columnheader` / `gridcell` children, `aria-rowcount`,
  `aria-colcount`, `aria-level` and `aria-expanded` per row, and `aria-selected` on the selected one. The grid
  is named by the rendered heading (so the name a reader hears is the one they see) and described by hidden
  keyboard instructions.
* One **roving tab stop**: exactly one row is `tabindex="0"`. ↑/↓ move, Home/End jump to the first/last visible
  row, → expands a collapsed row or walks into the first child, ← collapses an expanded row or walks to the
  parent.
* Enter selects on **keydown** (guarded against auto-repeat) and Space on **keyup**, exactly where a native
  button fires its click; the row's disclosure button stops propagation, so toggling never also selects.
* The keys the rows handle do not scroll the page: `tm-agent-run-tree.js` cancels only those defaults on a
  row, and never for a control inside a row or for Tab/Enter/browser shortcuts — a render-time
  `@onkeydown:preventDefault` cannot be per key and would trap the focus.
* ONE polite live region carries the component's own events: selected, expanded, collapsed and the filter's
  count. A move of the roving focus is silent (the row's own state is what a screen reader reads), and the
  ticking clocks are `aria-live="off"`.
* Every state is an icon **and** a word; the tone is a second carrier only. A failed row keeps the state word
  and carries the failure REASON as its status cell's accessible name (`data-has-error="true"`, a warning glyph
  for the eye), so the grid grows no second text column and a screen reader still hears why; a host shows the
  full message in its own `DetailTemplate`. The focus ring is an INSET outline
  (the row lives in a scroller, where an outward ring is clipped) and `forced-colors` repaints it — and the
  selected row's marker — in the system `Highlight` colour.
* `(pointer: coarse)` grows the row and the disclosure to the 44 px `--tm-touch-target`.
* The surface declares **no transition and no animation**: it repaints every second while a node runs.

## Layout

The grid is the ONE scroll owner in both axes; the header sticks to the top of that scrollport and every row
is a flex row with the same cell widths, so the columns stay lined up while a narrow container scrolls them
sideways INSIDE the component — the page never does. The cell widths are the `--tm-agent-run-tree-*` knobs and
the scroll cap is `--tm-agent-run-tree-max-height` (default `26rem`).

The name and model cells clip to one line inside that scroller, so both carry the whole text in their `title`:
a host that renders no `DetailTemplate` still has a way to read a long node name or model id in full (a cell
that reports nothing keeps no title — an em dash has nothing to reveal).

## CSS classes

`.tm-ai-agent-run-tree`, `__header`, `__title`, `__filter`, `__empty`, `__grid`, `__head`,
`__head-cell(--name|--status|--model|--time|--cost|--tokens)`, `__body`, `__row`, `__name`, `__toggle`,
`__toggle-spacer`, `__kind`, `__label`, `__label--placeholder`, `__orphan`, `__status`,
`__status-icon`, `__status-text`, `__status-error`, `__model`, `__time`, `__cost`, `__tokens`, `__sum`,
`__detail` — tokens
only (no colour literal, no raw palette step, no `[data-theme]` block), guarded by `AiMarkdownViewCssTests`
and `TmAgentRunComponentsCssTests`.

## Wireframe and localization

The component is a wireframe citizen: the `agent-run-tree` UI role, the `TmAgentRunTree` schema (`title`,
`nodes`, `filter`, `showModel`, `showTime`, `showCost`, `showTokens`) and its declarative stencil, which draws
the header row, the indented nodes with a state glyph per row, the optional metric columns and the filtered
footer.

Keys: `TmAgentRun_Status*` (the shared state words), `TmAgentRunNodeKind_*`, and the `TmAgentRunTree_*` family
(heading, label, instructions, the six column headers, the filter and its three values, the two empty
sentences, the untitled and not-reported fallbacks, expand/collapse, the three announcements, the filter
count, the detail label, the roll-up sentence and the orphan badge) in English, Czech, Slovak and French.

Demo: [`/agent-run`](../src/Tempo.Blazor.Demo.SharedUI/Pages/AgentRunPage.razor) — a three-level run whose
selection fills the page's own detail panel from `AgentRunRollup`, a filter that keeps the chain to the failed
lookup, an "Advance the run" control that rewrites the host's nodes, and a malformed run with an orphan and a
cycle.
