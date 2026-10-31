# Spreadsheet selection-drag A/B (phase 7b) — run 1 (21:24:52)

> Recorded by `SpreadsheetPerformanceE2ETests.SelectionDrag_AB_WithinBudget` at 2026-10-29 21:24:52.
> This run overlapped with other E2E specs on the same box, so its noise is not representative; it is
> kept for the record (and because the evidence folder must not hide an unfavourable run).
> NOTE: this run's raw JSON was written before the run-timestamp suffix was introduced and was
> overwritten by run 2 (`selection-drag-ab-214101.json` is run 2's). The numbers below are the ones the
> run reported.

- Baseline arm: `v2.9.0` spreadsheet-canvas.js served through Playwright route interception
- Alternations: 7 per arm, 8 selection drags per probe
- Budget: median(current) <= 110 % x median(baseline); Inconclusive when IQR > 30 % of the median

| Arm | Median (ms) | Q1 | Q3 | IQR |
|---|---|---|---|---|
| baseline (v2.9.0) | 789.2 | 675.8 | 1174.7 | 498.9 |
| current (HEAD) | 1527.4 | 889.9 | 1598.0 | 708.1 |

- Ratio current/baseline: **193.5 %**
- Noise (max IQR/median): 63.2 % → **Inconclusive** (above 30 %)

Raw samples (ms): baseline 1219.4, 675.8, 717.4, 1174.7, 789.2, 1524.2, 989.1 / current 2053.4, 590.2,
1102.2, 1527.4, 1598.0, 1533.9, 889.9.

Deterministic counters (current arm, every probe): selection paints 8 for 8 changed selections,
header paints 8 (exactly one per selection change), content paints 0, lastHeaderLayerMs 0.6-4.3 ms.
Baseline arm: selection paints 8, header paints 0 — `v2.9.0` predates the phase-2 header-highlight
layer entirely, which is why the wall-clock comparison is not like-for-like for this scenario.
