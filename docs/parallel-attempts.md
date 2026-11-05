# TmParallelAttempts — several attempts at one task, side by side

`TmParallelAttempts` (`Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) compares several attempts at the SAME
piece of work: a column per attempt with its state, its score, its own body and the votes it collected, the **best**
attempt marked, and a winner the reader picks and confirms.

The shared identity model (`AgentRef`, `AgentActivityStatus`) and the accessibility rules every agent visualization
follows are documented once in [ai-agents.md](ai-agents.md). This page is the `TmParallelAttempts` contract.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmParallelAttempts Attempts="_attempts"
                    BestAttemptId="_bestId"
                    @bind-SelectedAttemptId="_winnerId"
                    OnSelect="AcceptAsync"
                    Title="Three drafts">
    <ContentTemplate Context="attempt">
        <pre>@attempt.Summary</pre>
    </ContentTemplate>
</TmParallelAttempts>
```

## The models

| Type | What it is |
|---|---|
| `ParallelAttempt` | One attempt: `Id`, `Label`, `Status`, `Score?`, `Votes` (`IReadOnlyList<AttemptVote>`), `Summary?`, plus `HasScore`, `VoteCount`, `Approvals`, `Rejections`, `Abstentions`, `HasRationales`. A record VALUE; the body is deliberately NOT part of it (the host renders it through `ContentTemplate`). |
| `AttemptVote` | One voter's verdict: `Voter`, `By?` (an `AgentRef`), `Outcome`, `Score?`, `Rationale?`. A score without a verdict is legal and lands on `Abstain`. |
| `AttemptVoteOutcome` | `Abstain` (0, the default) \| `Approve` \| `Reject`. The default is an abstention on purpose: an unset outcome must never count as an approval. |
| `ParallelAttemptRanking` | `Best(attempts, bestAttemptId?)` and `Order(attempts, bestAttemptId?)` — the ONE rule that decides the best attempt. |

### Which attempt is best

`ParallelAttemptRanking.Best` is deliberately conservative:

1. the host's explicit `BestAttemptId`, when it names one of the attempts, wins outright — scored or not (the host may
   know something the numbers do not);
2. otherwise the highest `Score` wins, and an unscored attempt is **not** a zero-scored one (`null` means "not scored",
   so it never wins a comparison nobody set);
3. a tie keeps the earlier attempt — except that a **succeeded** attempt beats a running or failed one, because the
   numbers are equal and only one of them is a result a reader can use;
4. nothing scored means no best attempt at all, and no column wears the mark.

`Order` puts the best first and leaves the rest in declaration order, so a live list never reshuffles equal attempts
between renders.

## Parameters

| Parameter | Type | Default | What it does |
|---|---|---|---|
| `Attempts` | `IReadOnlyList<ParallelAttempt>?` | `null` | The attempts, in the host's order (one column and one table row each). Null or empty renders the localized empty state. |
| `Title` / `ShowTitle` / `HeadingLevel` | `string?` / `bool` / `int` | `null` / `true` / `3` | The heading; the level is clamped to 2–6. |
| `BestAttemptId` | `string?` | `null` | The host's pick of the best attempt; null falls back to the highest score. |
| `SelectedAttemptId` / `SelectedAttemptIdChanged` | `string?` | `null` | The CONFIRMED winner, two-way bound (`@bind-SelectedAttemptId`). A host that pins it without binding keeps control: the confirmed pick is shown, then the pinned value is restored on the host's next render. A host that sets neither keeps the reader's pick. |
| `OnSelect` | `EventCallback<string>` | — | The reader CONFIRMED an attempt as the result. |
| `ConfirmSelection` | `bool` | `true` | Whether a pick asks for confirmation. `false` makes the pick the winner immediately. |
| `ShowComparison` | `bool` | `true` | The comparison table (the text alternative of the columns). |
| `ContentTemplate` | `RenderFragment<ParallelAttempt>?` | `null` | The host's body for one attempt (a diff, a document, a code block, prose), rendered inside that attempt's column. |
| `Culture` | `CultureInfo?` | `null` | Formats the scores. It never rescales one: the host's scale is the host's. |
| `Class` / `AdditionalAttributes` | | | The usual root hooks. |

## Choosing a winner is two steps

Picking an attempt is **not** recording a result, so the surface separates the two:

1. The column's **native radio** is the choice. All the radios share ONE name, so the browser's own arrow keys walk the
   choices and `Tab` reaches the group as a single stop — no key emulation anywhere.
2. A pick opens a confirmation bar (`role="group"`, labelled by its own question) naming the attempt, with **Use this
   attempt** and **Keep the current result**.
3. Only the confirmation raises `OnSelect` and `SelectedAttemptIdChanged`; the question disappears and the focus returns
   to the radio that opened it. Cancelling keeps the previous winner, reverts the radio and announces it.

`ConfirmSelection="false"` collapses steps 2 and 3 into one for a host that wants a one-step choice. Either way, a
stray arrow key inside the radio group can never replace a recorded result by itself.

## The comparison table

`ShowComparison` renders one row per attempt with its label, its state, its score (formatted with `Culture`), its vote
count and whether it is the best one. It is the **text alternative** of the columns: the same numbers, reachable
without a pointer, and the surface a screen reader or a phone reads. The columns themselves never carry information the
table does not.

## Accessibility

- The attempts live in ONE labelled `role="group"` ("Choose the winning attempt") described by the keyboard
  instructions. It is deliberately **not** a `radiogroup`: a radiogroup may own only radios, and the radios live inside
  their columns, so a radiogroup there would put a required-child violation in front of the whole comparison.
- Each column is its own `role="group"`, named by its label first (WCAG 2.5.3), then its state, its score and — when it
  is — the fact that it is the best pick.
- The best attempt wears an **icon and the word** "Best"; the chosen one wears the word "Chosen" and a second border.
  Three distinct marks survive high contrast: best is a `CanvasText` border, chosen a `Highlight` border, focus a thicker
  `Highlight` outline, so a focused column can never be mistaken for a chosen one.
- The vote disclosure is a native button with `aria-expanded` and — only while the panel exists — `aria-controls`, so it
  never points at an id that is not rendered. Every vote states the voter, the outcome as a WORD, the score and the
  rationale verbatim.
- **ONE polite live region** announces the pending choice, the confirmed winner and a cancelled pick. Nothing else is
  announced, and the radios carry no second live region.
- **A coarse pointer** raises the radio, the vote toggle and the confirm buttons to the 44 px `--tm-touch-target`
  (a native radio is 13 px — not a finger target).
- **Reduced motion**: nothing in the surface moves. The partial declares no transition and no animation.

## A narrow container becomes a carousel

The columns are a horizontal scroll-snap track. Under **640 px of the component's own container** (a container query,
never the viewport) each column takes 86 % of the track and snaps to the start, so a phone gets one attempt at a time
with no JavaScript and no second layout engine. Two surfaces in one row decide for themselves.

## CSS

Classes: `.tm-ai-parallel-attempts`, `__title`, `__empty`, `__track`, `__attempt`, `__attempt--best`,
`__attempt--chosen`, `__attempt-head`, `__attempt-label`, `__best`, `__best-icon`, `__chosen`, `__metrics`,
`__metric`, `__metric-label`, `__state`, `__state-icon`, `__score`, `__summary`, `__body`, `__votes-toggle`, `__votes`,
`__vote-list`, `__vote`, `__vote-head`, `__vote-voter`, `__vote-outcome`, `__vote-score`, `__vote-rationale`,
`__no-votes`, `__radio`, `__confirm`, `__confirm-question`, `__confirm-actions`, `__action`, `__action--primary`,
`__table`.

Tokens only (no colour literal, no raw palette step, no `[data-theme]` block). The root declares
`--tm-parallel-attempts-gap`, `-column-min-inline-size`, `-best-tone` and `-chosen-border`, so a host restyles the marks
without touching a rule.

## Wireframe

The palette ships the `parallel-attempts` UI role, the `TmParallelAttempts` schema (`title`, `attempts`,
`bestAttemptId`, `confirmSelection`, `showComparison`) and a declarative stencil that draws the columns, the "Best"
mark, a vote line and the comparison table.
