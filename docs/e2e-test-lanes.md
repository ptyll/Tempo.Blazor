# E2E Test Lanes

The Playwright E2E suite (`tests/Tempo.Blazor.E2E`, ~1450 tests, several hours
wall clock) is split into two lanes so pull requests get fast feedback while
exhaustive coverage keeps its own full lane. The full lane is intended to run
nightly — today no scheduled workflow exists, so it runs manually via
`scripts/run-e2e-full.ps1` until one is added (see DEC-TEMPO-RELEASE-GATE).

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

## Full lane (nightly — intended cadence, currently manual)

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

**Regression-screenshot convention:** a `before`/`after` PNG pair may enter the
repository ONLY as the output of a named test calling `TakeScreenshotAsync`
(see `PlaywrightTestBase.TakeScreenshotAsync`), never copied in by hand. The
test that produces the pair must be cited next to the files
(`__screenshots__/<set-name>/README.md` or the test's XML comment) — a pair
with no test that reproduces it is deleted (N146, 2026-09-22: the
`regression-2.9.0` set was byte-identical between "versions" and wired into
nothing — it implied a verification that never ran).

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
(PR gate); full-lane regressions are triaged after each full run — classify each failure
as pre-existing (tracked in the triage list) or a regression introduced by the
change under test before merging.

## Environment for the full lane

A verified full-suite run needs a specific machine environment or a subset of
failures will be environment noise (class E) rather than product signal.
Recorded 2026-09-26 while investigating 12 deterministically red tests on a
machine whose defaults differed from the machine that recorded
`full-run-20260924`:

- **`LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8`** — the self-hosted demo apps
  inherit the process culture, and several full-lane tests (F9, F12, F13,
  F14) assert against localized text that differs under a non-English
  system locale (e.g. `cs_CZ.UTF-8`). Export both before the run; do not rely
  on the shell's ambient locale.
- **Font substitution pinned to DejaVu** — headless Chromium's PDF/text
  export path can pick a different font than the one the browser itself
  renders with when a system font package (e.g. `ttf-mscorefonts`, which
  substitutes Arial) is installed. `BrowserAndServerExports_AgreeOnPaginationAndTextLayer_AndOpenInTmPdfViewer`
  compares the two and goes red on font mismatch. Point `FONTCONFIG_FILE` at
  a fontconfig file that only resolves the DejaVu family before running the
  full lane, so both paths agree.
- **`TM_E2E_TRACE_ON_FAILURE=false`** for the acceptance measurement itself
  (see above) — traces are for triage runs, not the recorded evidence.

Three tests remained red even with the above (`Overlay_Hides_WhenAnchorScrollsFullyOutOfViewport`,
`EB4_InlineToolbar_BottomEdge_CaptureBaseline`, `PhaseE7_CanvasConnectorEndpointClipboardAndAllDrawingTypesPersistWithScreenshotEvidence`):
their fixed post-scroll wait windows raced the browser's default smooth-scroll
animation rather than an environment difference; the tests themselves now
pass `behavior: 'instant'` to their `scrollTo`/`scrollIntoView` calls (see the
`test(e2e): make scroll-dependent overlay, EB4 and E7 tests deterministic`
commit) so this is not a required environment step, just recorded here for
context on why those three were part of the same investigation.

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
- **Host resurrections are evidence, not noise (N209):** `PlaywrightTestBase`
  appends every demo-host resurrection to `TestResults/host-restarts.jsonl`
  (repo root) and counts them in `HostRestartLog.TotalHostRestarts`. When
  recording a release-evidence run, copy the JSONL into the run's artifacts
  dir and report the count in `eng/release-evidence/e2e-full-run.json` under
  `hostRestarts` — `eng/verify-release-evidence.sh` refuses any nonzero count
  and also refuses when `artifactsPath` does not exist where the gate runs,
  which is why release-run artifacts ship in the committed
  `eng/release-evidence/<run>/` tree rather than the gitignored `TestResults/`
  dir a fresh CI checkout cannot see.
