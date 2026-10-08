# Full-suite E2E run — 2026-10-08 (F19-1, Tempo.Blazor 2.9.1 release gate)

Owner-verified full-lane run over commit `89bda60ca3d00570d11d7ce1f5c250a4b6131bb9`
(main). This is the run recorded in `../e2e-full-run.json`. The E2E leg executed
inside the whole-solution gate `dotnet test TempoBlazor.slnx -c Release`; all 13
other test projects in the same invocation finished green (see `full-run.log`).

## Environment recipe (docs/e2e-test-lanes.md, "Environment for the full lane")

```
LANG=en_US.UTF-8
LC_ALL=en_US.UTF-8
FONTCONFIG_FILE=<path-to>/fontconfig-dejavu-only/fonts.conf
TM_E2E_TRACE_ON_FAILURE=false
```

Build: `dotnet build TempoBlazor.slnx -c Release --nologo` (0 warnings, 0 errors).

Lane invocation (inside the slnx run):
`dotnet test TempoBlazor.slnx -c Release --logger "trx;LogFileName=release-291.trx" --logger "console;verbosity=minimal"`

## Files in this directory

- `e2e-full.trx` / `full-run.log` — the full unfiltered lane: passed=1313,
  failed=111, skipped=77, total=1501, wall clock 6h48m (E2E assembly leg).
- `residual-names.txt` — the 111 failed test names sent to serial re-measurement.
- `e2e-serial-residual.trx` / `residual.log` — serial residual re-run over the
  21 classes containing the 111 lane failures (134 tests superset; a fresh
  browser): <TBD> passed, <TBD> failed.
- `host-restarts.jsonl` — one entry at `2026-10-07T13:05:08Z` ("Demo API",
  unreachable-past-window). That entry predates the lane window
  (2026-10-07T23:55Z → 2026-10-08T06:49Z) by ~11h and belongs to the earlier
  serial-adjudication session; zero resurrections occurred inside the lane or
  the residual window, so `hostRestarts=0`.

## Root-cause note on the 111 lane failures

**108 of 111 share one signature** — `Microsoft.Playwright.TargetClosedException:
Target page, context or browser has been closed` thrown inside
`PlaywrightTestBase.CreateContextAsync` → `Browser.NewContextAsync`, each failing
in under ~30ms. The shared static Chromium browser (one per test assembly,
`[DoNotParallelize]`) died at ~2026-10-08T06:47:28Z, after ~6.5h of execution and
~1800 renderer lifecycles. The last in-flight test
(`CorePages_RenderInEveryThemeCombination`, a 4m27s every-theme×page screenshot
matrix) completed green at the same instant; the following 8
`Capture_LightAndDark` baseline rows were skipped as usual, and every subsequent
Chromium-backed test failed instantly at context creation. Firefox-flavoured
tests kept passing after the event (e.g.
`SortButton_AccessibleName_Keyboard_And_HoverFocus_Firefox`, green at 06:47:54Z),
which isolates the loss to the single Chromium process, not the suite or the
demo hosts. No OOM-kill record is retrievable from this container's kernel log;
the Chromium profile directory is cleaned on browser exit, so no crash dump
survives. No resurrection path exists for the shared browser by design — the
failure mode is documented here and every affected test was re-measured
serially, which is the lane's adjudication mechanism for non-product reds.

**3 remaining failures** are ordinary lane reds queued by the watcher during the
run and sent to the same serial residual:

- `DocLib4_RemoteEdit_RefreshesLinkedBlock_WithoutReload` — 30s
  `WaitForFunctionAsync` timeout waiting for the SignalR-broadcast preview marker
  during a machine-wide load spike (foreign session, load ~28).
- `FileBlock_Shows_DownloadLink`, `AudioBlock_EnterUrl_ShowsPlayer` — both in the
  shared `InsertBlockViaSlashMenuAsync` helper's 5s `data-block-type` conversion
  window, again during load-11+ contention.

## Working-tree note

`tests/Tempo.Blazor.E2E/__screenshots__/` and
`tests/Tempo.Blazor.E2E/screenshots/` regenerated during the lane were reverted
after the residual finished (screenshot baselines refresh in their own dedicated
commits, per repo convention). `src/Tempo.Blazor.Demo.Api/diagrams.db-shm` /
`diagrams.db-wal` SQLite side files were removed. No source or test files were
touched by this evidence commit.
