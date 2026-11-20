# Waiver — publishing v2.12.0 without a green full E2E run over the released commit (DEC-TEMPO-RELEASE-GATE, second clause)

**Decision:** owner `ptyll`, 2026-11-20 (plan 98c80bc3). `v2.12.0` is published WITHOUT an owner-verified green full-suite E2E run over the released commit. Scope: ONLY the tag `v2.12.0`.

## What was measured, and on what

| | |
|---|---|
| Full run 1 | `55d6e359` (end of phases 2–10): 3817 total, 3621 passed, 10 failed (6 deterministic, 4 contention flakes), 186 skipped. All six deterministic failures were fixed (`279cc2dc`, `6170a86d`): two plan regressions in `_modal.css`, PdfViewer snap-back / container-only scroll (CF1–CF2), Notion caret reserve (CF3), smtp4dev guard. |
| Full run 2 | `6170a86d`: 3237 total, 3063 passed, 6 failed (all contention flakes, green serially), 168 skipped, shard 1 without TRX (hung testhost), 1 host restart (`Demo WASM died-mid-run`). Every item was root-caused and fixed in `b65b076d` (overlay/Notion focus restore, prerender-safe `TmScheduler` dispose, designer palette collapse, E2E harness deadlock/exit-code fixes, full-lane inactivity watchdog); shard 1 re-run green: 551 tests, 531 passed, 0 failed, 0 host restarts. |
| Targeted run | After phases 10a–10d (`53b8c47c`, `6bbec348`, `b434d578`, `d2b4ff0f`, `15e6197a`) and `b65b076d`: every test that failed or flaked in run 2, every E2E class changed since run 2 and the screen specs of phases 10a–10d — 784 tests, 766 passed, 2 failed, 16 skipped. Both failures fixed in `84de5100` (bounded animation wait for the chart time-axis tooltip; central boot recovery for the pointer-drag / scheduler-touch specs) and green 3× in isolation plus their whole classes. |
| Unit / node | bUnit and node lanes of every touched package green per phase (reviewed: code, UX, architect). |

## What was NOT measured

No single full-suite run covers the released commit. Regressions outside the targeted set introduced after `6170a86d` (phases 10a–10d, the fixes of `b65b076d` and `84de5100`) are covered only by the targeted run and the per-phase tests.

## Scope

- ONLY the tag `v2.12.0`: both publish workflows skip the "Verify the release evidence" step iff `github.ref == 'refs/tags/v2.12.0'` AND this file exists.
- The evidence gate stays fully active for every other version, tag, branch and workflow_dispatch; the v2.11.0 and v2.11.1 waivers are unchanged.
- Removing this file re-arms the gate for v2.12.0. The next release needs a fresh recorded full run.
- Follow-up: the remaining ~360 E2E files that bypass the central boot recovery (ratcheted in `ReadinessInfrastructureTests`) are migrated right after the release.
