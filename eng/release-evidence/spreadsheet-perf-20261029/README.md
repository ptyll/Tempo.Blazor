# Spreadsheet canvas performance evidence — phase 7b (remaining task `12bc2afb`)

**Verdict: Inconclusive on this machine (escalation, not a pass).** The wall-clock budget
(`median(current) <= 1.10 x median(v2.9.0)`) cannot be confirmed here because the measurement noise
(IQR / median) stays above the phase's 30 % threshold across two independent runs — this box's rAF
cadence swings by an order of magnitude and other workloads share the machine.

| Run | Baseline median (ms) | Baseline IQR | Current median (ms) | Current IQR | Ratio | Noise (max IQR/median) | Verdict |
|---|---|---|---|---|---|---|---|
| 1 (overlapping load, 21:24:52) | 789.2 | 498.9 | 1527.4 | 708.1 | 193.5 % | 63.2 % | Inconclusive |
| 2 (idle-ish, 21:41:01) | 1508.4 | 750.9 | 1146.6 | 429.1 | 76.0 % | 49.8 % | Inconclusive |

The two runs disagree on the SIGN of the ratio (193 % vs 76 %) with the same code — that is the
signature of a noise-dominated sample, not of a regression or an improvement.

## Files

The spec writes one JSON + one markdown per run, suffixed with the run's `HHmmss` so a same-day re-run
never overwrites an earlier sample:

- `summary-212452.md` — run 1 (markdown only; see the note below).
- `summary-214101.md` + `selection-drag-ab-214101.json` — run 2, the only run whose RAW JSON was kept.

Run 1's raw JSON was written before the timestamp suffix existed and was overwritten by run 2 (the
first version of `WriteEvidenceAsync` used the fixed names `summary.md` / `selection-drag-ab.json`).
Its medians, quartiles and raw duration sequence are recorded in `summary-212452.md`; the deterministic
counters for run 1 are the same as run 2's (see below).

## What the deterministic counters say (hard assertions, all green in both runs)

- Selection layer: 8 paints for 8 changed selections (one per change) in BOTH arms.
- Header layer (current arm): 8 paints for 8 changes — the phase-7b spec asserts this EXACTLY
  (`Assert.AreEqual(ChangedCells, HeaderLayerPaintCount)`), never more than the selection layer;
  `lastHeaderLayerMs` 0.5-4.3 ms, i.e. inside a frame.
- Content layer: 0 repaints during a selection-only drag in both arms.
- Baseline arm (`v2.9.0`) paints the header layer **0** times: it predates the phase-2
  header-highlight feature, so the wall-clock A/B compares a canvas that highlights headers with one
  that does not. That asymmetry is the main reason a strict +10 % wall-clock budget is not a
  like-for-like claim for the selection-drag scenario; the phase-7 in-session A/B (phase-6 canvas vs
  current, same feature set) is the fair comparison and reported a 0 % change in every counter.

## Reproduction

```bash
TM_E2E_SELF_HOST=false TM_E2E_WASM_URL=https://localhost:7106 \
  dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj \
  --filter "FullyQualifiedName~SelectionDrag_AB_WithinBudget"
```

The spec serves `git show v2.9.0:src/Tempo.Blazor.Spreadsheet/wwwroot/js/spreadsheet-canvas.js`
through Playwright route interception as arm A and the working-tree script as arm B, alternates both
arms seven times (8 selection drags per probe, timed in-page by
`tmSpreadsheetBenchmark.runSelectionDragProbe`), and writes the JSON + markdown into this folder.

## Escalation to the user

Please confirm the <= +10 % budget on an idle machine or a dedicated perf lane (`scripts/run-e2e-perf.ps1`
exists for the perf category). Do not treat this folder as a pass.
