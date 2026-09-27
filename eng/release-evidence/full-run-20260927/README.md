# Full-suite E2E run — 2026-09-27 (F2-12, release run 2)

Owner-verified full-lane run over commit `835aafde7c3d0ce137d7c985180d2687d4fe8da2`
(main after merging `fix/ci-test-stability` and `fix/e2e-residual-f12`, both reviewed
and approved). This is the run recorded in `../e2e-full-run.json`.

## Environment recipe (docs/e2e-test-lanes.md, "Environment for the full lane")

```
LANG=en_US.UTF-8
LC_ALL=en_US.UTF-8
FONTCONFIG_FILE=<path-to>/fontconfig-dejavu-only/fonts.conf
TM_E2E_TRACE_ON_FAILURE=false
```

Build: `dotnet build tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj` (0 warnings, 0 errors).

Full run: `dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj --no-build --logger "trx;LogFileName=e2e-full.trx" --logger "console;verbosity=minimal"`

## Files in this directory

- `e2e-full.trx` / `full-run.log` — the full parallel run: passed=1374, failed=22,
  skipped=76, total=1472, wall clock 4h6m.
- `e2e-serial-residual.trx` / `residual.log` — round 1 of the serial residual re-run
  over the 22 failing tests' fully-qualified names: 20/22 passed, 2 remained red
  (`EB5_PageLinkMenuNoResultsAndLongTitleChip_CaptureBaseline`, `TmBadge_Renders`).
- `e2e-serial-residual2.trx` / `residual2.log` — round 2, over the 2 remaining names:
  both passed. `serialResidualTotal=22`, `serialResidualFailed=0`.
- `host-restarts.jsonl` — empty (0 bytes): no demo-host resurrection occurred in any
  of the three runs above (no `TestResults/host-restarts.jsonl` was ever created at
  the repo root, which only happens on a resurrection).

## Root-cause note on the 22 parallel failures

20 of the 22 failures shared one signature: `System.TimeoutException: Timeout
30000ms exceeded` navigating to the self-hosted WASM demo (`https://localhost:7106`)
or Server demo (`https://localhost:7107`), or a `TypeError: Failed to fetch` from
`dotnet.js` on an already-open WASM page — consistent with one or both demo hosts
becoming briefly unresponsive partway through the ~4h parallel run (no restart was
logged, so `PlaywrightTestBase`'s resurrection path was not exercised; the hosts
recovered before the process exited). All 20 passed on a fresh serial re-run that
starts its own host instances. The other 2
(`EB5_PageLinkMenuNoResultsAndLongTitleChip_CaptureBaseline`,
`HomeScrollsThePage_ButLeavesTheFocusedTabInsideTheViewport`) are already-documented
flaky tests from the F2-12 investigation (`net::ERR_ABORTED` on a `Page.ReloadAsync`
racing the WASM boot, and a scroll-settle timing assertion, respectively); both
recorded green here too.

## Not touched by this run's cleanup

Per the release-run-2 instructions, only `tests/Tempo.Blazor.E2E/__screenshots__`
and the `*.db-shm`/`*.db-wal` SQLite side files were restored/removed after each
run. `tests/Tempo.Blazor.E2E/screenshots/` (7 files, no double underscore) was left
modified in the working tree on purpose (out of this evidence commit's scope) — see
the implementer's report to the moderator for the list.
