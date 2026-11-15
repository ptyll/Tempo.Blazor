# Waiver — publishing v2.11.1 without a new full E2E run (DEC-TEMPO-RELEASE-GATE, second clause)

**Decision:** owner `ptyll`, 2026-11-15. `v2.11.1` is published WITHOUT a new owner-verified green full-suite E2E run over the released commit. Scope: ONLY the tag `v2.11.1`.

## What was measured, and on what

| | |
|---|---|
| Last full E2E run | `d4ddfe72577221be01b7e3628aeb2a616923f657` (`e2e-full-fze6`): 3779 total, 3532 passed, 65 failed, 182 skipped, `serialResidualFailed=24` — a RED run, recorded honestly in `e2e-full-run.json`. |
| Released base | v2.11.0 = `34dbd601a` (+ evidence/waiver commit `db89633f8`); v2.11.1 is that tree plus the diff below. |
| Diff of v2.11.1 | `TmToastContainer.razor` (single-flight OnAfterRenderAsync, dispose guards, module fields captured and nulled before the first await), `TmDataTable.razor.cs` (`CardMode` adds `tm-data-table-wrapper--container`), one CSS comment, docs/JSON documentation, CHANGELOG, tests, version bump of all packable projects. |
| Local gate (as `publish-nuget-org.yml` build-and-test) | Lane 1 (default culture) 25902 passed / 0 failed / 2 skipped of 25904; lane 2 (cs_CZ.UTF-8, same binaries) 25902 / 0 / 2 of 25904. |
| Targeted | bUnit TmToastTests 59/59; new CardMode Playwright test green with the fix and RED (`block` expected, `table-row` actual) without it. |
| RED proofs | NRE at `TmToastContainer.razor` line 258 reproduced on the 2.11.0 code; the new toast tests are red when the single-flight guard or the dispose guards are removed; CardMode bUnit and E2E tests red without `|| CardMode`. |
| JS node tests | overlay, core-modules, signing-runner, diagram-editor, pdf-viewer, wireframe, spreadsheet, modeling, reporting-modules all green (no JS changed). |

## What was NOT measured

No full-suite E2E run covers 2.11.1 or the 2.11.0 tree it is based on; the newest full run is the red d4ddfe7 run. A regression outside the targeted tests, introduced by the two `src/` changes, is not covered by a full run.

## Scope

- ONLY the tag `v2.11.1`: both publish workflows skip the "Verify the release evidence" step iff `github.ref == 'refs/tags/v2.11.1'` AND this file exists (`hashFiles('eng/release-evidence/waiver-v2.11.1.md') != ''`).
- The evidence gate (`eng/verify-release-evidence.sh`) stays fully active for every other version, tag, branch and workflow_dispatch; the v2.11.0 waiver is unchanged.
- Removing this file re-arms the gate for v2.11.1. The next release needs a fresh recorded full run.
