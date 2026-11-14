# Full-suite E2E run e2e-full-fze6 — 2026-11-13/14 (NOT a green run)

Self-hosted, 8 shards, filter `TestCategory!=Performance`, measured commit
`d4ddfe72577221be01b7e3628aeb2a616923f657`. Totals: 3779 total, 3532 passed, **65 failed**, 182 skipped,
0 host restarts, 542.3 min wall clock.

This is an HONEST record of a red run. It does not satisfy the DEC-TEMPO-RELEASE-GATE second clause; the
release of v2.11.0 is covered by `../waiver-v2.11.0.md` instead.

- `summary.json`, `summary.md` — the lane summary (per shard, with every failure).
- `rerun-list.txt` — the 53 distinct failed test names sent to serial re-measurement.
- `rerun2-summary.txt` — serial rerun by name filter on d4ddfe7: 26 rows selected tests (21 with `failed>0`,
  4 passed serially, 1 row `gantt` ran 0 tests); 27 rows (`Name=po.…`, a mangled fully-qualified filter) ran 0 tests.
- `rerun3-results.txt` — the fqn rerun of 3 of those 27 (all 3 failed serially on d4ddfe7).
- `FIXES.md` — each failing test → root cause → fix (commit 34dbd601) → verification.

`serialResidualTotal=53` (distinct failed names rerun), `serialResidualFailed=24` (21 + 3 still red serially on d4ddfe7).
The other 24 names were not rerun serially on d4ddfe7. The `.trx` files and PNGs are intentionally not committed.
