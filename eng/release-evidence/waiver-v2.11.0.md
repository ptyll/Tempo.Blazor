# Waiver — publishing v2.11.0 without a new full E2E run (DEC-TEMPO-RELEASE-GATE, second clause)

**Decision:** owner `ptyll`, 2026-11-14 (option A). `v2.11.0` is published WITHOUT a new owner-verified green
full-suite E2E run over the released commit.

## What was measured, and on what

| | |
|---|---|
| Measured commit | `d4ddfe72577221be01b7e3628aeb2a616923f657` (run `e2e-full-fze6`, 2026-11-13 16:00 → 2026-11-14, self-host, 8 shards, 0 host restarts, 542.3 min ≈ 9h2m) |
| Result | 3779 total — 3532 passed — **65 failed** — 182 skipped (a RED run; recorded honestly in `e2e-full-run.json`) |
| Serial re-measurement on d4ddfe7 | 53 distinct failed names selected; 28 produced a serial verdict on d4ddfe7 (25 by name filter + 3 by fqn): **24 still red**, 4 green (1 name filter, `gantt`, ran 0 tests). The other 24 names were not serially rerun on d4ddfe7. `serialResidualFailed=24` |
| Released commit | `34dbd601a774d7c4b16644a3b7b1828160692ce1` (CI green, run 43602179334) = d4ddfe7 + ONE commit: 81 files, 6 under `src/` (product bugs), the rest E2E/bUnit/node tests + CHANGELOG |

Because the evidence commit differs from the released commit in `src/` and `tests/`, and 24 recorded failures are red,
`eng/verify-release-evidence.sh` WOULD refuse this release (staleness bound + `serialResidualFailed != 0`). This waiver
exists so that refusal is a documented, owner-accepted exception rather than a silent bypass.

## Why

- A new full run takes ~9 h of wall clock (one shard alone 2h49m).
- The 65 failures were triaged: 5 product bugs in 6 `src/` files (`TmModelingInspector` draft gate, `TmPdfViewer` `ThumbnailsCollapsed` parameter compare,
  `diagram-editor.js` right-click hit-test, editor-shell nested inline-size containment, modeling dock floor 15rem) and the rest tests
  that had not followed the 2.11.0 contracts (see `full-run-20261113/FIXES.md`).
- All fixes are targeted and were verified by a targeted rerun on 34dbd601 (147/148 green; the last one 2× green).
- The CI gate is green on 34dbd601 (run 43602179334).

## What this waiver does NOT claim

No full-suite green run exists over 34dbd601. The evidence file records the d4ddfe7 run as it was (red); it is not edited to look green.
The risk accepted by the owner: a regression outside the re-run subset, introduced by the 6 `src/` changes, is not covered by a full run.

## Scope

- ONLY the tag `v2.11.0`: `publish-nuget-org.yml` skips the "Verify the release evidence" step iff
  `github.ref == 'refs/tags/v2.11.0'` AND this file exists (`hashFiles('eng/release-evidence/waiver-v2.11.0.md') != ''`).
- The evidence gate (`eng/verify-release-evidence.sh`) stays fully active for every other version, tag, branch and workflow_dispatch.
- Removing this file re-arms the gate for v2.11.0 too. The next release needs a fresh recorded full run.

