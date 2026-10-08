# Full-suite E2E run — 2026-10-08 (F19-1, Tempo.Blazor 2.9.1 release gate)

Owner-verified full-lane run over commit `89bda60ca3d00570d11d7ce1f5c250a4b6131bb9`
(main). `../e2e-full-run.json` still records `full-run-20260927`; it is updated when
the definitive gate run lands. The E2E leg executed inside the whole-solution gate
`dotnet test TempoBlazor.slnx -c Release`; all 13 other test projects in the same
invocation finished green (see `full-run.log`).

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
- `e2e-serial-residual.trx` — serial residual re-run over the 21 classes
  containing the 111 lane failures (134 tests superset; a fresh browser):
  total=134, executed=133, passed=127, failed=6. Run 2026-10-08T06:55Z → 07:31Z.
  The one `NotExecuted` row is `Gantt_TimelinePan_DragsHorizontally`, an
  intentional documented ignore (synthetic mouse events vs Blazor
  `@onmousemove`; coverage lives in `TmGanttPanTests.cs`).
- `residual-failed.txt` — the 6 serial red names.
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
survives. No resurrection path existed for the shared browser at this commit —
the failure mode is documented here and every affected test was re-measured
serially, which is the lane's adjudication mechanism for non-product reds.

**3 remaining failures** are ordinary lane reds queued by the watcher during the
run and sent to the same serial residual:

- `DocLib4_RemoteEdit_RefreshesLinkedBlock_WithoutReload` — 30s
  `WaitForFunctionAsync` timeout waiting for the SignalR-broadcast preview marker
  during a machine-wide load spike (foreign session, load ~28).
- `FileBlock_Shows_DownloadLink`, `AudioBlock_EnterUrl_ShowsPlayer` — both in the
  shared `InsertBlockViaSlashMenuAsync` helper's 5s `data-block-type` conversion
  window, again during load-11+ contention.

## Serial residual outcome — 6/134 red, root-caused, fixed in `400a3555`

The residual (see `e2e-serial-residual.trx`, names in `residual-failed.txt`)
produced **6 serial reds**. That fails this run's release-gate contract
(`serialResidualFailed` must be 0), so this dir documents an adjudicated —
not green — lane. Root causes, all reproduced and fixed in `400a3555`
(`test(e2e): harden release-gate flakes — SignalR join barrier, browser
resurrection, nav timeouts`):

1. **SignalR group-join race** (`DocLib4_RemoteEdit_RefreshesLinkedBlock_WithoutReload`,
   and by mechanism `Mcp4_LiveBridge_...` which passed only because its MCP
   handshake adds delay): the wireframe SVG renders while `JoinDocument` is
   still in flight, so the test's PUT broadcast misses the not-yet-member
   client. Fix: `SignalRTempoDocumentChangeNotifier` now re-joins all tracked
   groups on `Reconnected` (a real product bug — `WithAutomaticReconnect` does
   not restore group membership, so any hub drop permanently killed
   live-refresh); `TmNotionWireframeBlock` exposes `data-doclib-subscribed`
   after `SubscribeAsync` completes; both tests wait on that barrier.
2. **Slash-menu conversion window** (`ImageBlock_EnterUrl_DisplaysImage`,
   `AudioBlock_EnterUrl_ShowsPlayer`, `EmbedBlock_EnterUrl_ShowsIframe`,
   `MediaLibrary_LibraryTab_IsVisible`): `data-block-type` flips only after the
   conversion `ApplyAsync` save, which the aggregate-session mutation gate
   serializes behind any in-flight autosave — a standalone Playwright probe
   proved the conversion itself completes correctly (<3s idle), it is the 5s
   assertion window that was too short under contention. Fix: window widened
   to 20s. All four were also re-verified green individually after the fix.
3. **Navigation-load timeout** (`UndoRedo_AfterAddingElement`):
   `Page.ReloadAsync` hit Playwright's default 30s/`load` budget; the suite
   convention is 60s + `DOMContentLoaded`. Fixed accordingly; re-verified
   green standalone.

The 108 collateral failures additionally motivated browser self-resurrection
in `PlaywrightTestBase` (relaunch the shared Chromium when `IsConnected` is
false, covering `CreateContextAsync` and direct `Browser.` callers), so one
browser death can no longer cascade into ~100 instant failures. A definitive
re-run of the whole gate on `400a3555` follows under a new evidence dir.

## Working-tree note

`tests/Tempo.Blazor.E2E/__screenshots__/` and
`tests/Tempo.Blazor.E2E/screenshots/` regenerated during the lane were reverted
after the residual finished (screenshot baselines refresh in their own dedicated
commits, per repo convention). `src/Tempo.Blazor.Demo.Api/diagrams.db-shm` /
`diagrams.db-wal` SQLite side files were removed. The commit carrying this
evidence (`400a3555`) also carries the fix set described above — the evidence
files themselves are run artifacts only.
