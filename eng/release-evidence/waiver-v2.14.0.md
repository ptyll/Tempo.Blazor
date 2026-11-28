# Waiver — publishing v2.14.0 without a second full E2E run over the released commit (DEC-TEMPO-RELEASE-GATE, second clause)

**Decision:** owner `ptyll`, 2026-11-28 (plan b75365fb). `v2.14.0` is published WITHOUT a second owner-verified green full-suite E2E run over the released commit. Scope: ONLY the tag `v2.14.0`.

## What was measured, and on what

| | |
|---|---|
| Full run | `TestResults/e2e-full-20261127-181817` over `73bd1d059` plus the uncommitted release changes (version bump + CHANGELOG rename + the four test fixes below): 4025 total, 3739 passed, 104 failed, 182 skipped, 0 host restarts. |
| Rerun of the 104 failures | 99 were contention flakes that pass serially (95 of them in shard 3 alone). The remaining 5 were deterministic and every one was root-caused: 4 were caused by this plan's own changes and fixed test-only (below), the fifth (`TmTimeSelectorE2ETests.TimeSelector_EnableSwipe_PointerDragUp_StepsHours`) passes in isolation both on the working copy and on the 2.13.0 tag — a host flake, unchanged by this plan (carried forward in the plan's remaining tasks). |
| Targeted rerun | The four fixed test classes green after the fix: 40/40. |
| Unit / node | Full bUnit lane green (23 682 passed; one documented parallel flake, isolated green), the 12 other unit projects green (3 881), npm lanes green (`test:ai` 123, `test:core-modules` 69, `test:notion-db-resize` 46, `test:overlay` 1071). ReleaseContractTests green after the version bump. |

## The 4 deterministic failures fixed test-only

1. `ReportDesignerAriaStateE2ETests` — the spec added by phase 4h owned its Playwright/context instead of the shared readiness ratchet; now inherits `PlaywrightTestBase` + `CreateContextAsync` (ReadinessInfrastructure ratchet 18/18 green).
2. `SigningContainerQueryE2ETests` — phase 4g added a second (locked) field-editor panel to `/signing-components`; the locator is scoped to `[data-testid='field-editor-panel']`, the same panel the test resizes.
3. `SigningRedesignPhase8E2ETests` — the demo page grew in phase 4g and the sticky header covered the field; the test scrolls it to the viewport centre before the forced click (documented pattern).
4. `WireframePhase4DesktopE2ETests` — phase 4h added toolbox filter tabs (role=tab) to the demo; the tab locator is scoped to `.tm-wd-side__tabs`, expectations unchanged.

## What was NOT measured

No second full-suite run covers the released commit. Regressions outside the 104-failure set, introduced after `73bd1d059`, are covered only by the targeted rerun and the unit/node lanes. The 99 contention flakes are a host-capacity artefact of the 8-shard full lane, not product defects.

## Scope

- ONLY the tag `v2.14.0`: both publish workflows skip the "Verify the release evidence" step iff `github.ref == 'refs/tags/v2.14.0'` AND this file exists.
- The evidence gate stays fully active for every other version, tag, branch and workflow_dispatch; the v2.11.0, v2.11.1, v2.12.0 and v2.13.0 waivers are unchanged.
- Removing this file re-arms the gate for v2.14.0. The next release needs a fresh recorded full run.
