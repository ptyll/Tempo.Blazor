# Spreadsheet selection-drag A/B (phase 7b)

- Run: 2026-10-29 21:41:01
- Baseline arm: `v2.9.0` spreadsheet-canvas.js served through Playwright route interception
- Alternations: 7 per arm, 8 selection drags per probe
- Budget: median(current) <= 110 % x median(baseline); Inconclusive when IQR > 30 % of the median

| Arm | Median (ms) | Q1 | Q3 | IQR |
|---|---|---|---|---|
| baseline (v2.9.0) | 1508.4 | 989.1 | 1740.0 | 750.9 |
| current (HEAD) | 1146.6 | 948.0 | 1377.1 | 429.1 |

- Ratio current/baseline: **76.0 %**
- Noise (max IQR/median): 49.8 % (Inconclusive above 30 %)

## Deterministic counters (current arm, per probe)

| # | Selection paints | Header paints | Content paints | Changed cells | Segment ms | lastHeaderLayerMs |
|---|---|---|---|---|---|---|
| 1 | 8 | 8 | 0 | 8 | 1146.6 | 0.50 |
| 2 | 8 | 8 | 0 | 8 | 1242.4 | 3.80 |
| 3 | 9 | 8 | 0 | 8 | 1669.3 | 1.10 |
| 4 | 8 | 8 | 0 | 8 | 1065.0 | 0.90 |
| 5 | 9 | 8 | 0 | 8 | 1377.1 | 0.70 |
| 6 | 8 | 8 | 0 | 8 | 948.0 | 0.80 |
| 7 | 8 | 8 | 0 | 8 | 844.9 | 1.00 |

## Raw samples

| Round | Arm | Duration (ms) | Selection paints | Header paints | Content paints | Changed cells |
|---|---|---|---|---|---|---|
| 1 | baseline | 1047.6 | 8 | 0 | 0 | 8 |
| 2 | current | 1146.6 | 8 | 8 | 0 | 8 |
| 3 | baseline | 889.3 | 9 | 0 | 0 | 8 |
| 4 | current | 1242.4 | 8 | 8 | 0 | 8 |
| 5 | baseline | 1508.4 | 8 | 0 | 0 | 8 |
| 6 | current | 1669.3 | 9 | 8 | 0 | 8 |
| 7 | baseline | 1524.2 | 8 | 0 | 0 | 8 |
| 8 | current | 1065.0 | 8 | 8 | 0 | 8 |
| 9 | baseline | 4052.9 | 8 | 0 | 0 | 8 |
| 10 | current | 1377.1 | 9 | 8 | 0 | 8 |
| 11 | baseline | 1740.0 | 8 | 0 | 0 | 8 |
| 12 | current | 948.0 | 8 | 8 | 0 | 8 |
| 13 | baseline | 989.1 | 8 | 0 | 0 | 8 |
| 14 | current | 844.9 | 8 | 8 | 0 | 8 |
