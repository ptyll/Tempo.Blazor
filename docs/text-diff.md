# Shared text diff engine (`Tempo.Blazor.Diff`)

The library compares text in exactly one place: `TextDiff` in **`Tempo.Blazor.Abstractions`**
(`src/Tempo.Blazor.Abstractions/Diff/`, namespace `Tempo.Blazor.Diff`). It has no Blazor and no UI
dependency, so the same engine serves a component, a WASM host, a Server circuit and a backend
service.

Both historical helpers are now delegating façades over it and keep their public API and output:

| Façade | Granularity | Result shape |
|---|---|---|
| `DocumentTextDiffHelper.Diff` (`Tempo.Blazor.DocumentEditor.Services`) | words | `DocumentTextDiffResult.Segments` — `Unchanged` / `Removed` / `Added` |
| `TmTextLineDiff.Compute` (`Tempo.Blazor.Components.Files`) | lines | `IReadOnlyList<TmFileVersionDiffLine>` — 1-based line numbers, CRLF normalized to LF |

A new consumer (`TmInlineDiff`, review flows, an audit trail) should call the engine directly.

## Contract

```csharp
TextDiffResult result = TextDiff.Words("Klient zaplatí 1000 Kč", "Klient zaplatí 1200 Kč");
// or TextDiff.Lines(oldText, newText, options)

foreach (var hunk in result.Hunks)
{
    // hunk.Id       — deterministic, e.g. "replace-15-15"
    // hunk.Kind     — Equal | Insert | Delete | Replace
    // hunk.OldText  — exact characters of the old input covered by this hunk
    // hunk.NewText  — exact characters of the new input covered by this hunk
    // hunk.OldRange / hunk.NewRange — character spans (TextRange: Start, Length, End)
    // hunk.OldUnitCount / hunk.NewUnitCount — tokens (word diff) or lines (line diff)
}

string withEveryChange = result.Apply(result.HunkIds);   // == result.NewText
string original        = result.Apply([]);               // == result.OldText
string firstChangeOnly = result.Apply([result.Changes[0].Id]);
```

**The hunks tile both inputs.** Concatenating `OldText` over all hunks reproduces
`result.OldText` character for character, and concatenating `NewText` reproduces `result.NewText`.
That invariant is what makes a partial accept safe: `Apply` takes every accepted hunk from the new
text and every other hunk from the old one, so no accepted subset can produce a mangled text. The
rule applies to `Equal` hunks too — they are not decisions, but they still carry the characters they
cover, which is how a whitespace-only difference stays where it was:

> Pass `HunkIds` (every hunk) when the result must be **exactly** `NewText`; pass only the ids from
> `Changes` when the result should keep the old wording — and the old whitespace — wherever the
> reader rejected or never saw a decision.
Whitespace between tokens (and the line break between lines) belongs to the hunk in front of it —
which is why a hunk's text may carry a trailing space or newline. Use
`OldUnitCount` / `NewUnitCount` (not the character span) when you need to know *how many* words or
lines a hunk covers: a trailing empty line occupies no character at all.

A word-level token is a word (letters plus combining marks and digits), a run of digits, or a single
punctuation character. Whitespace is not a token, so re-flowing a paragraph is not a change; string
comparison is ordinal, so composed and decomposed diacritics are different tokens. The token
definition lives in exactly one place (`DiffAlgorithm`): `DocumentTextDiffHelper` re-joins a hunk's
tokens through the engine's own tokenizer, so the rendered join can never split a hunk differently
from the boundaries the engine computed.

A `Replace` hunk is **one decision** even though it covers both sides; it is produced when a run of
deletions is directly followed by a run of insertions. An insertion that comes first stays its own
hunk so the walk's order is preserved.

## Behaviour at the edges

| Input | Result |
|---|---|
| two identical texts | one `Equal` hunk, `HasChanges == false` |
| empty vs text | one `Insert` (or `Delete`) hunk |
| whitespace-only text vs text | one `Replace` hunk that keeps the whitespace in `OldText` |
| mixed CRLF / LF in a line diff | the affected lines are reported as changed — `Lines` compares what it is given; `TmTextLineDiff` normalizes first |

## Limits and the documented degradation

`TextDiffOptions.MaxTokens` (default **20 000** tokens per input) and `TextDiffOptions.MaxCells`
(default **4 000 000** table cells, ≈ 16 MB) keep a pathological input from freezing the UI or
throwing. Above either limit the comparison **degrades** and says so:

* a word diff falls back to line granularity (`Fallback = LineLevel`, `Granularity = Lines`);
* a line diff that is still too large collapses to one whole-text change
  (`Fallback = SingleChange`).

`TextDiffResult.IsDegraded` is the cheap check a UI uses to tell the reader the diff is approximate.
Only the region between the common prefix and the common suffix is ever put into a table, so
editing one word of a long document stays cheap.

**Both façades inherit that degradation**, and it is the only behaviour they change for very large
inputs (their output for an input inside the limits is pinned by the regression suite):

* `DocumentTextDiffHelper.Diff` above `DefaultMaxTokens` tokens (or `DefaultMaxCells` cells) returns
  segments that describe **whole lines** instead of words and sets
  `DocumentTextDiffResult.IsDegraded`; before the extraction it built the full table no matter the
  size.
* `TmTextLineDiff.Compute` above the limits returns **one block of removed lines followed by one
  block of added lines** covering the whole text, where the old code would have tried to allocate a
  table of up to hundreds of millions of cells.

## Delegation is pinned

`tests/Tempo.Blazor.Tests/Diff/` holds the engine contract tests
(`TextDiffTests`) and the regression net for the two façades (`DiffFacadeRegressionTests`): explicit
goldens captured before the extraction plus a frozen copy of the pre-extraction algorithm compared
against both façades over a corpus that includes punctuation, repeated whitespace, diacritics, CRLF
and the empty string. A change to the walk's tie-breaking (removal first) fails those tests, which
is the point — the rendered document comparison and the file-version history must not move. The
degradation contract has its own two tests there as well
(`DocumentTextDiffHelper_ReportsThatTheResultDegraded_AndKeepsTheDocumentedShape`, plus the
non-degraded counterpart): an input above the limits must set `IsDegraded` and produce exactly one
removed block followed by one added block.
