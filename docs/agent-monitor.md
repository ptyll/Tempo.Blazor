# TmAgentMonitor — one agent run, live

`TmAgentMonitor` (namespace `Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) answers the four questions
a watcher asks without opening anything: what is the run doing **now**, how long has it been going, what has it
cost, and how did it get here.

```razor
<TmAgentMonitor Steps="@steps"
                Status="@status"
                StartedAt="@startedAt"
                CompletedAt="@completedAt"
                CostMicroUnits="@cost"
                Model="gpt-4o"
                Title="Nightly reconciliation"
                @bind-Collapsed="collapsed">
    <PendingApproval>
        <TmButton Variant="ButtonVariant.Primary" OnClick="ApproveAsync">Approve</TmButton>
    </PendingApproval>
</TmAgentMonitor>
```

The component renders the snapshot it is given and never polls anything: a host pushes new steps and totals as
the run reports them (SignalR, polling, a server-side channel).

## API

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `Steps` | `IReadOnlyList<AgentMonitorStep>?` | `null` | The steps so far, oldest first. The current step is the last `Running` one, else the last step. |
| `Title` | `string?` | `null` | Heading; it also names the region. |
| `ShowTitle` | `bool` | `true` | `false` names the region by `AriaLabel` / the localized default. |
| `HeadingLevel` | `int` | `3` | Heading level, clamped to 2–6. |
| `Status` | `AgentStepStatus` | `Running` | The state of the whole run: the state word and icon, the headline and whether the clock ticks. |
| `StartedAt` / `CompletedAt` | `DateTimeOffset?` | `null` | The run's window. No start renders no clock; a completed run stops and keeps its recorded duration. |
| `CostMicroUnits` | `long?` | `null` | What the run has cost, in MICRO-UNITS (`1_000_000` = `1.00`); null renders an em dash, never a zero. |
| `Model` | `string?` | `null` | The model metric; hidden when null. |
| `PendingApproval` | `RenderFragment?` | `null` | The host's decision surface, in its own named `role="group"`. |
| `Collapsed` / `CollapsedChanged` | `bool` / `EventCallback<bool>` | `false` | Two-way fold (`@bind-Collapsed`); the toggle raises the new state. |
| `AllowCollapse` | `bool` | `true` | `false` removes the toggle and keeps the strip expanded even when `Collapsed` is pinned `true`. |
| `ShowTimeline` | `bool` | `true` | The steps' timeline. |
| `ShowElapsed`, `ShowCost`, `ShowModel` | `bool` | `true` | The optional metrics. |
| `IconResolver` | `Func<AgentStepKind, string>?` | `null` | Maps a step's kind to an icon name; null uses the built-in mapping. The kind is always also a hidden word. |
| `CostFormatter` | `Func<long, string>?` | `null` | Formats a micro-unit cost. Null uses `AiCostFormatter`. |
| `Culture` | `CultureInfo?` | `null` | Formats the cost; null means the current culture. |
| `TimeProvider` | `TimeProvider?` | `null` | The clock of the live timer; null uses the registered `TimeProvider`, then `System`. |
| `EmptyContent` | `RenderFragment?` | `null` | Replaces the localized "No steps yet." |
| `AriaLabel` | `string?` | `null` | Accessible name when `ShowTitle` is false. |
| `Class` | `string?` | `null` | Extra root classes. |
| `DataTestId` / `TestIdPrefix` | from `TmComponentBase` | — | Standard test-id namespacing. |

Public members: `Announcement`, `CurrentStep`, `Elapsed`, `IsCollapsed`, `SetCollapsedAsync(bool)`.

## The model

`AgentMonitorStep` (`Id`, `Title`, `Kind`, `Status`, `Detail`, `StartedAt`, `CompletedAt`, computed
`IsFinished` / `Duration` / `Elapsed(now)`) is a record, like `AgentRunNode`. `AgentStepKind` is
`Thinking` \| `ToolCall` \| `Message` \| `Approval` \| `Handoff` \| `Result`; it decides the timeline glyph,
and its localized name is rendered for a screen reader, so a custom `IconResolver` never removes the only copy
of the information.

The monitor keeps the run's **totals** on its own parameters rather than on the steps: a step that reported
its own cost would force every host to keep a running sum in two places, and a step that reported none would
make the timeline silently disagree with the header.

## The clock

`Elapsed` is the recorded duration once `CompletedAt` is set, otherwise the time from `StartedAt` to the
injected `TimeProvider`'s "now" — sampled once per render. The ticker is armed only while the run is
`Running` with a start and no end, so a finished run stops re-rendering. The clock-style duration
(`0:42`, `1:24`, `1:02:07`) is invariant on purpose: it is a duration, not a quantity of a unit.

## Collapsing

`Collapsed` folds the strip into one bar that keeps the **essentials** — state, title, current step, clock,
cost — and drops the timeline and the approval slot. A pending decision is never hidden by the fold: the bar
renders a "Waiting for your approval" marker, and a pending approval that appears while the strip is already
showing is announced in the component's polite live region (the marker is not live on its own). The toggle is
a native button with `aria-expanded`, the new state is announced in the same live region, and a host that pins
`Collapsed` keeps control (the reader's toggle reports the change; the next render from the host decides).
`AllowCollapse="false"` removes the toggle and ignores a pinned `true` — a bar a host cannot reopen is not a
feature.

## Layout

The strip is prose plus short metrics, and it has to work at 320 px (WCAG 1.4.10 Reflow):

* The run title, the current step and every step title / detail **wrap** (`overflow-wrap: anywhere`). The
  monitor has no detail panel, so a single ellipsized line would lose text a sighted reader could never reach.
* Only the short values — elapsed, cost and model — keep one line with an ellipsis, and the model carries its
  whole id in a `title`. On a `(pointer: coarse)` device (where a `title` cannot be hovered) those values wrap
  too.
* `(pointer: coarse)` also grows the fold toggle and every step row to the 44 px `--tm-touch-target`.

## Accessibility

* The strip is a `role="region"` named by its heading (or by `AriaLabel`), described by its headline sentence.
* Every state is an icon AND a word (`Pending` / `Running` / `Completed` / `Failed` / `Skipped`); the tone is a
  second carrier only. Timeline items carry `data-kind` and `data-status`, their kind word is visually hidden
  but present, and their own state is spelled out.
* ONE polite live region, written by the fold/unfold action and by a pending approval that APPEARS while the
  strip is showing (a folded reader is never left waiting silently; an approval present on the first render is
  read in the normal flow). The clock is explicitly `aria-live="off"`: a value that changes every second would
  talk over everything else.
* Native buttons only (the toggle), a visible focus ring repainted in the system `Highlight` colour under
  `forced-colors`, and the 44 px `--tm-touch-target` on `(pointer: coarse)`.
* The strip declares **no transition and no animation**: it repaints every second while the run is going.

## CSS classes

`.tm-ai-agent-monitor`, `--collapsed`, `__bar`, `__state`, `__state-icon`, `__state-text`, `__heading`,
`__title`, `__headline`, `__approval-marker`, `__toggle`, `__metrics`, `__metric`,
`__metric(--current|--elapsed|--cost|--model)`, `__metric-label`, `__metric-value`, `__approval`, `__timeline`,
`__step`, `__step-icon`, `__step-kind`, `__step-body`, `__step-title`, `__step-detail`, `__step-status`,
`__step-time`, `__empty` — tokens only, guarded by `AiMarkdownViewCssTests` and
`TmAgentRunComponentsCssTests`.

## Wireframe and localization

The component is a wireframe citizen: the `agent-monitor` UI role, the `TmAgentMonitor` schema (`title`,
`steps`, `state`, `showTimeline`, `showCost`, `collapsed`) and its declarative stencil, which draws the state
glyph and word, the current step, the clock, the cost, the step lines and the fold button.

Keys: `TmAgentMonitor_*` (heading, label, current step and its empty fallback, elapsed/cost/model labels, the
timeline label, the empty sentence, the untitled fallback, the approval label and its announcement, the
fold/unfold pair and their two announcements, and the five headline sentences), plus the shared
`TmAgentRun_Status*` and `TmAgentStepKind_*` families, in English, Czech, Slovak and French.

Demo: [`/agent-run`](../src/Tempo.Blazor.Demo.SharedUI/Pages/AgentRunPage.razor) — a live monitor whose
approval slot really answers, buttons that complete, fail and reset the run, and a second, folded monitor with
a decision waiting.
