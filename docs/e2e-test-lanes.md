# E2E Test Lanes

The Playwright E2E suite (`tests/Tempo.Blazor.E2E`, ~1450 tests, several hours
wall clock) is split into two lanes so pull requests get fast feedback while
exhaustive coverage still runs every night.

## Smoke lane (PR gate, < 20 minutes)

- **What runs:** every test marked `[TestCategory("Smoke")]`. The core of the
  lane is `SmokeLaneE2ETests` (boot probes across the major demo surfaces with
  unhandled-exception capture, including edge cases such as an unknown route),
  `DocumentEditorCanvasHistorySaveE2ETests` (document-editor history,
  dirty state, save, autosave, and reload persistence), and
  `DocumentEditorPdfExportE2ETests` (the PDF gate: toolbar export must produce
  a text-layer PDF with editor-parity pagination and open in TmPdfViewer).
- **How to run:** `scripts/run-e2e-smoke.ps1`
  (or `dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj --filter TestCategory=Smoke`).
- **Growing the lane:** add `[TestCategory("Smoke")]` at class or method level.
  Only add tests that are deterministic (no `WaitForTimeoutAsync`-based
  synchronization) and keep the total lane under 20 minutes including host
  startup.

## Full lane (nightly)

- **What runs:** the entire suite, no filter.
- **Acceptance:** zero *deterministic* failures. Tests that go red only under
  parallel shard contention are re-run serially (`-Filter` with their fully
  qualified names); a failure that passes the serial residual counts as
  contention flakiness and must be recorded (test + mechanism), not silently
  accepted. Anything red in serial is a real defect.
- **How to run:** `scripts/run-e2e-full.ps1` (optionally `-Filter "..."` to
  scope a rerun during triage). Failure traces are disabled by default on this
  lane (`TM_E2E_TRACE_ON_FAILURE=false`) because a full run can produce
  ~800 MB of traces; export the variable as `true` before the run to override.

## Node module lane

JS engine unit tests run separately and are cheap enough for every commit:

- `npm run test:document-editor` — all `*.test.mjs` under
  `src/Tempo.Blazor.DocumentEditor/wwwroot/js/document-editor` and
  `.../document-editor-canvas`. File enumeration is done by
  `scripts/run-node-tests.mjs` (explicit filesystem walk, no shell/Node glob
  expansion), so a stale glob can never silently skip tests again — the runner
  fails when a root is missing or matches no files.
- `npm run test:reporting-modules` — reporting JS modules via the same runner.
- `npm run test:overlay` — `TmOverlayPanel` placement JS (`src/Tempo.Blazor/wwwroot/js`,
  flip/shift/clamp math + fallback-path branches) via the same runner. The overlay
  primitive underpins 15 floating-surface components, so this lane runs on every
  commit too.

## Baseline policy

A lane is considered **baselined** when it is green three consecutive runs on
the same commit. Phases that change components must keep the smoke lane green
(PR gate); full-lane regressions are triaged nightly — classify each failure
as pre-existing (tracked in the triage list) or a regression introduced by the
change under test before merging.

## Practical notes for running locally

- All four hosts are auto-started by `PlaywrightTestBase.EnsureDemoHostsAsync`
  (Demo API on `https://localhost:5100`, Demo WASM on `https://localhost:7106`,
  Demo Server on `https://localhost:7107`, InteractiveAuto on
  `https://localhost:7108`). Set `TM_E2E_SELF_HOST=false` to run against
  externally managed hosts instead.
  Kill stale listeners on those ports before a clean run — a stale host whose
  `bin/obj` was rebuilt serves broken static assets and every test times out on
  its first locator.
- Delete `tests/Tempo.Blazor.E2E/TestResults` between long runs to keep disk
  usage bounded.
