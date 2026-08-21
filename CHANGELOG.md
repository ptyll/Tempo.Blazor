# Changelog

## 2.8.20 - 2026-08-21

Release tooling only. **No component changed**, so a consumer that is happy on 2.8.19 gains nothing by
upgrading. This number exists because the previous one was minted twice and nothing in the repository
could say so.

### Fixed (release evidence)

- **The version being announced was never compared against the tag store.** `v2.8.19` resolves to
  `714093ce` and the published `tempo.blazor.2.8.19.nupkg` records that same commit in its own nuspec;
  every one of the 26 packages staged locally under that number records `d1c8e776`. Two commits behind
  one version number, with the first artefact already immutable on nuget.org (published
  2026-08-19T06:11:27Z, measured as HTTP 200 on the flat container with 2.8.18 as the positive control).
  Both existing guards were green throughout and were right to be: one compares every packable csproj
  against the changelog, the other compares each staged nuspec against `HEAD`. Neither pair answers
  "is this number still free".

  `ReleaseContractTests.AnnouncedVersion_IsEitherUntagged_OrItsTagNamesTheCommitBeingPacked` compares
  that third pair and does it unconditionally: `v<announced>` either does not exist, or resolves to the
  commit being packed. There is no arm in which the comparison is reported to a reader instead of
  enforced.

  It does not rest on `git diff --name-only v2.8.19 HEAD -- src/` being empty, which it is. That
  implication has not been measured in either direction here — the two packages carrying 2.8.19 do not
  share a commit, so `src/` was never the only thing that differed between them — and the guard needs
  neither direction, because it compares a tag against a commit and never opens a package.

  What it does not see, since the gap is real and cheap to over-read: tags this ref store does not
  have. `actions/checkout@v4` fetches at depth 1 without tags, so on a push to `main` there is nothing
  to find and the check passes without having looked. Every run therefore prints how many tags were
  visible, so a zero says so instead of hiding inside a green. It says nothing at all about nuget.org:
  a number can be published without any tag ever existing, and only a feed lookup answers that.

- **Release gate is the CI filter, not `dotnet test TempoBlazor.slnx`.** Demo.Api including the two
  smtp4dev tests is in the gate (CI starts `rnwood/smtp4dev`; `Smtp4DevHost` starts the container
  locally). Named exceptions: `Tempo.Blazor.E2E` (measured 1220/124/79 of 1423 in 12 h 8 min —
  not green, not a gate) and `Tempo.ReportServer.Api.Tests.MsSql` (27/177, SQL Server missing;
  do not fix). `ReleaseGateFilterTests` keeps the two publish workflows on one filter.

### Internal

- **`ReleaseContractTests.PackedPackages_RecordTheCommitTheyWereBuiltFrom` was two guards in one `[Fact]`,
  and one of them is empty most of the time.** The script-text contract now lives in
  `PackScript_PassesTheCommitIn_RefusesADirtyTree_AndVerifiesTheStampBackOut` and runs unconditionally.
  The staged-package half keeps the old name, because that is the half `eng/pack-nuget-packages.sh`
  points at for the stamp comparison and the name had to stay true there.

  An empty staged population is now reported as a **skip** rather than a pass. The size of that change,
  said plainly: the old shape asserted nothing false. It wrote the population size to test output and
  passed, so "nothing was checked" and "26 packages were checked" left the same green — the loss was
  evidence, not truth, which makes this a weaker instance of vacuous green than one that certifies
  something. Deleting the reporting line left every assertion green, so the report was a claim about
  the run that the run did not enforce; the outcome carries it now.

  The skip is decided at discovery by `StagedPackagesFactAttribute`, and that is not a preference:
  xUnit v2 has no runtime skip. `Assert.Skip` is absent from `xunit.assert 2.9.3`'s `Assert`, and the
  `$XunitDynamicSkip$` protocol its `SkipException` speaks has zero occurrences in
  `xunit.execution.dotnet.dll` of the same version — measured with the tool that finds `SkipReason`
  there five times, so the zero is a reading rather than a silence. The body's first assertion is a
  tripwire and not the rule: swap the attribute back to `[Fact]` and the empty population makes it red,
  so the two reachable shapes are skip and red and the pass over nothing is gone from both.

  A second door to the same silent pass is closed in the same change: a candidate whose archive holds
  no `.nuspec` was counted and skipped by the loop, so a non-empty population made only of such files
  ran zero assertions and passed with `nuspec-inspected=0`. The tripwire cannot see that — it is about
  an EMPTY population — so `withoutNuspec` is now asserted to be zero.

  The 2.8.19 note below names the old test for the script-text assertions that have since moved into
  the new one. It is left as written — it was true when it shipped, and rewriting released notes is a
  worse habit than a superseded sentence.

## 2.8.19 - 2026-08-19

Release tooling only. **No component changed**, so a consumer that is happy on 2.8.18 gains nothing by
upgrading — this release exists so that the *next* one can be trusted.

### Fixed (release evidence)

- **`eng/pack-nuget-packages.sh` packed a dirty tree and certified the result.** The commit stamped
  into every nuspec came from `git rev-parse HEAD`; the script then re-opened the packages it had just
  produced and compared the stamp against that same value, and `ReleaseContractTests` compared it
  against `HEAD` a third time. Over a **dirty** tree all three are the same number, so the equality
  held **by construction** while the packed bytes came from source that no commit contains. This was
  not an uncovered case, it was an active false confirmation: measured on 2026-08-18, `packages/` held
  26 `Tempo.*.2.8.18.nupkg` stamped `commit="d49ede02…"` — which is the 2.8.17 commit — and all three
  checks reported them good, because HEAD really was d49ede02 while the 2.8.18 content sat uncommitted
  in the working tree. Those 26 packages then went red on their own the moment the bump was committed
  and HEAD moved: same bytes, same stamp, opposite verdict. A guard whose answer depends on when you
  ask it is not measuring the packages.

  The pack now **refuses a dirty tree** rather than labelling it. `ALLOW_DIRTY_PACK=1` is the explicit
  escape for a deliberate local experiment, and it does not restore the lie — the stamp then ends in
  `-dirty`, so a package built off uncommitted source says so in its own nuspec instead of borrowing
  its parent commit's good name. Such a package must never be published or copied into a consumed
  feed, and it cannot pass unnoticed: `-dirty` equals no commit id, so the test fails it. Nothing
  changes for CI, which checks out the tag into a clean tree — the blind spot was always the
  **local** pack, and a local pack is how consuming repositories fill their own NuGet feeds.

  Why a dirty-check and not a better stamp: the defect is not *what* is read but that the label is
  verified against the source it was minted from. Any replacement inside that loop — SourceLink, a
  content hash, another way of reading `HEAD` — only moves the tautology. The only thing that breaks
  it is refusing the situation in which a truthful answer does not exist.

### Internal

- `ReleaseContractTests.PackedPackages_RecordTheCommitTheyWereBuiltFrom` now also asserts that the
  script *contains* the refusal, using the same read-the-script technique it already applies to
  `-p:RepositoryCommit=` and `git rev-parse HEAD`. It has to be a text assertion: `ReadGitHead()`
  deliberately never shells out to git, so the test can read which commit `HEAD` names but never
  whether the tree matched it at pack time — it cannot measure the state the refusal exists for. The
  needles are matched against the script's **code** with comment lines stripped, because the same
  words appear in the comment block that explains the clause, and "delete the code, keep the prose"
  is what a hasty revert leaves behind. Stated plainly so a green is not over-read: this proves the
  clause is present, not that it runs, not that its exit code is honoured. Only running the script
  over a dirty tree proves that.

## 2.8.18 - 2026-08-18

One behaviour change in `TmDataTable`, and it is a change rather than an addition: **`ShowPagination="false"`
now means "do not paginate", not "paginate and hide the controls".** Read the breaking-change note before
upgrading — no API was added, so nothing about the parameter list tells you this happened.

### Changed — BREAKING for client-side tables with the pager hidden

- **`ShowPagination="false"` no longer slices the `Items` collection.** `ShowPagination` gated only the
  render of the pager (`TmDataTable.razor:469`); the slice in `RefreshClientItems()` ran regardless, so a
  table handed 200 rows with the pager hidden rendered rows 1–25 and offered no element that reached rows
  26–200. The summary that would have said "showing 1–25 of 200" sits inside the same `@if` as the pager,
  so it did not lie — it was simply absent, and the failure mode was silence. Slicing is now derived
  from the pager: no pager, no slice. The client-side table renders every item it was handed.

  **What changes for you:** a consumer that set `ShowPagination="false"` on a collection longer than the
  effective page size will now see the whole collection instead of its first page. That is the point — the
  rows were already handed to the component and were already unreachable — but a page that relied on the
  slice as a display limit has to limit the DATA instead, at the source, and say so. Doing it in the grid is
  what made the limit invisible. `ScrollMode="Virtualized"` is not the substitute: it renders through
  `<Virtualize>` into a fixed-height container and turns inline editing off, so it is a different layout,
  not "show everything".

  **What does not change:** server-side paging through `DataProvider` is untouched — the slice this removes
  lives only in the `Items` path. A table with the pager visible still pages exactly as before
  (`DataTable_Pagination_ShowsOnlyFirstPageRows` measures that arm, and inverting the new condition turns 34
  tests red). No parameter was added: an opt-out would have been a third independent switch beside
  `ShowPagination` and `ScrollMode` with nothing keeping the three consistent, which is the shape that
  produced this defect one level up.

## 2.8.17 - 2026-08-15

The rest of the host application's gap register that did not fit in 2.8.16. Released **as one
tag** — a register that names a version and then ships half of it stops being read. The list was
frozen before the first library change: orphan classes that the rendered-DOM probe actually
saw, the leftover `role="toolbar"` promises, and the outline button whose border token had been
measured under 3:1. Universal selectors stay a 2.9.0 target. The `dismiss` class on the Blazor
WASM error UI is a host hook, not a Tempo class.

### Fixed

- **Six class names Tempo rendered with no rule.** `tm-pagination-size` is the page-size wrapper
  (its label already had a rule). `tm-pagination-disabled` is the disabled-pager modifier —
  `TmPagination` already put it on the root when `Disabled` is true, but five guard runs never
  rendered that state, so the missing paint was a claim about the stylesheet, not a finding on a
  page. It now dims the pager and the component disables every interactive child, not only
  swallows the click. `tm-avatar-fallback` fills the initials. Every `AvatarColor` modifier
  (`gray` through `pink`) has a token-backed colour. `tm-avatar-2xl` is the box `AvatarSize.Xxl`
  already emitted; CSS only had `.tm-avatar-xxl`. `tm-input-search` is the search field's own
  chrome (`appearance: none`); `.tm-search-input` is a different class on different markup.
  Guarded by `OrphanClassCssContractTests` (source + shipped bundle, mutation both ways).

- **Twenty-five leftover `role="toolbar"` attributes in 22 files.** Same defect 2.8.16 closed on
  `TmFormActionBar`: a toolbar is a promise of one tab stop and arrow-key movement. None of the
  eight files that mention `tabindex` implement toolbar roving — ribbon tabs, canvas roots and
  comment panes are a different pattern. They are now `role="group"` and keep their accessible
  name. Guarded by `ToolbarRoleGuardTests` (empty set in `src/`, scanner mutation on attribute
  vs comment).

- **`ButtonVariant.OutlineSecondary` used the decorative border token.** `--tm-border-color`
  is 1.24:1 on white and 1.41:1 on the dark surface — below WCAG 1.4.11's 3:1. The control
  token is 4.83:1 / 5.76:1. The outline button now takes `--tm-border-color-control`.

### Not in this release

- `tm-pagination-controls` / `-prev` / `-next` remain semantic hooks beside the painted
  `tm-pagination-btn`. `tm-scroll-spy-nav` is a BEM root. `dismiss` belongs to
  `blazor.webassembly.js`. Universal `*` selectors stay 2.9.0.

## 2.8.16 - 2026-08-12

Nine entries from a host application's gap register, released **as one tag**. The register's own rule is
that a version it promises must arrive whole: a register that names a version and then ships half of it
stops being read. Six of the nine are about a form's action bar, its navigation guard and the picker
that sits above them, and they share one defect class — **an affordance with no mechanism**: a marker,
a role or a parameter that states an intent the rendering, the keyboard or the save path never honours.

### Known issue in the published 2.8.15 package — read this before auditing that release

**The `2.8.15` package on the feed carries `repository commit="efb00b89"`, which is the commit of
`2.8.14`** — see the section below, kept here because it is still true of the package on the feed.
`2.8.16` is the first release packed by the fixed script, and its label was verified against the bytes
during the pack, not only in a test.

### Changed (source-breaking, one parameter)

- **`TmNavigationGuard.OnSaveAndLeave` is now `Func<Task<bool>>?` instead of `EventCallback`, and a
  failed save no longer discards the work.** The guard used to navigate as soon as the callback
  completed, whatever it did — so a save that failed on an HTTP error or on server-side validation threw
  the changes away. That is precisely the loss the guard exists to prevent, delivered by the button that
  promises to prevent it, which is why the only host that needed this API deliberately did not use it.
  A delegate with no result cannot say "the save failed", so the type had to change; leaving a
  data-losing API alive under a familiar name would be worse than making callers pick a return value.
  Return `true` and the guard re-issues the blocked navigation, `false` and it navigates nowhere and
  leaves the dialog open, so the user keeps both the changes and the choice — including the original
  destination, so a retry that succeeds still goes where they were going. A second click while the save
  is in flight is swallowed rather than duplicated.

  Not inferred from `IsDirty` after the callback, though that was the cheaper option: the parameter is
  pushed by the parent's render, which has not necessarily happened when the awaited callback returns,
  so the guard would be reading a value that is stale for reasons it cannot see. The host knows whether
  the save succeeded; now it says so.

  Guarded by `SaveAndLeave_FailedSave_StaysPut_AndKeepsTheDialogOpen`,
  `SaveAndLeave_RetryAfterFailure_LeavesForTheOriginalDestination` and
  `SaveAndLeave_SecondClickDuringSave_DoesNotStartASecondSave`.

- **`TmStatCard` rejects `SubValueColor` without `SubValue`** with an `InvalidOperationException` naming
  the card, instead of ignoring it. The sub-value span is rendered only when `SubValue` is non-empty, so
  a colour on its own is a stated intent that rendering silently denies — the first instance of it lived
  through eight call sites and several months, because a parameter that does nothing says nothing.
  The invariant is in the COMPONENT, not in a markup scanner, and the placement is the point: it covers
  splatted `@attributes`, `DynamicComponent` and consumers outside this repository, and it fails at the
  moment of misuse rather than at the next audit. A static scan of `.razor` markup is a legitimate second
  line for a release gate, but it cannot see those paths and must carry its own denominator.

### Fixed

- **`TmUserPicker`'s result list painted below sticky chrome.** It sat on `--tm-z-dropdown` (1000) while
  a floating `TmFormActionBar` sits on `--tm-z-sticky` (1020), so a list opened near the bottom of a page
  ended up UNDER the bar and its items were unreachable by mouse — keyboard selection still worked, which
  is what makes this easy to miss. Now on `--tm-z-popover`, the same fix `_filterable-dropdown.css` already
  carries for the same defect. `FloatingMenu` widens the exposure, because a fixed list reaches the bottom
  of the viewport.

- **The picked user's chip had no accessible name.** Once a user is selected the search input disappears,
  and with it the `<label for=…>` association — a screen reader then read the person's name with no hint
  of WHICH field held it. The chip now borrows the same label through `aria-labelledby`, carried by
  `role="group"`: a plain container cannot take an accessible name, and `group` promises no keyboard
  mechanism, unlike the `toolbar` role this release removes from `TmFormActionBar`. No label, no role —
  a role whose only job is to carry a name is an empty promise when there is no name to carry.

- **"Loading…" and "No results found" were never announced.** Both were plain elements that appeared and
  disappeared with the search. The fix is not an `aria-live` attribute on the message — a live region that
  enters the DOM together with its own text is not reliably announced, because the screen reader was not
  watching that node when the text arrived. There is now ONE permanent `role="status"` region, rendered
  even when it has nothing to say; only the messages inside it are conditional. Empty it has no box of its
  own. The transient error keeps its own `role="alert"` region: it is an interruption, not a progress
  report, and it carries a retry button.

- **`TmFormActionBar` coloured the generic `Status` slot as SUCCESS.** `.tm-form-action-bar__status` hard-coded
  `color: var(--tm-color-success-text)` although the parameter is called `Status`, not `SuccessStatus`, so an
  error message next to the save button would have been GREEN — the colour contradicting the text. Severity is
  now STATED via `StatusSeverity` (default `None` = inherit) and never derived, and every severity has both a
  modifier and its own colour, because a parameter whose values do not change anything is the same defect one
  level up.

- **`TmFormActionBar` no longer claims `role="toolbar"`.** The role promises a single tab stop and arrow-key
  movement between the actions; the bar never had roving tabindex, so a screen-reader user heard "toolbar",
  pressed an arrow and nothing happened. It is a `group` with the same `aria-label` now — honest for two or
  three buttons, and it promises nothing that would have to be delivered. **This class is NOT closed:**
  `role="toolbar"` without roving tabindex is still on ~25 other components (`TmBulkActionBar`,
  `TmAuditLogViewer`, `TmLedgerGrid`, `TmReportViewer`, the editors). Recorded, not fixed — the bar is where
  it surfaced, because Phase 3.5 moved a page's PRIMARY action into it, but the mechanism is shared.

### Added

- **`--tm-form-action-bar-z-index`, defaulting to `--tm-z-sticky`.** Overriding `--tm-z-sticky` was not a
  substitute: it moves EVERY sticky element, so a host that needed the bar above its mobile menu had to lift
  its own sidebar, scrim and header instead.

- **`--tm-form-action-bar-reserve-block-size`, desktop and below 768 px.** `position: fixed` contributes
  nothing to the flow, so a page under the bar must leave room or the bar covers the end of the form — and the
  only party that knows the bar's height is this library, since it is made of its own `--tm-space-*`, the
  `__inner` border and the button height. Hosts were GUESSING it; in one application six pages guessed
  independently. The value is DERIVED from the same tokens the bar is built from, never written in pixels: a
  magic number would drift silently the moment any input changed. Its reference content is ONE row of actions —
  how many action groups reach a phone is something only the host knows, and that limit is spelled out in the
  token itself, together with the arithmetic.

### Known issue in the published 2.8.15 package — read this before auditing that release

**The `2.8.15` package on the feed carries `repository commit="efb00b89"`, which is the commit of
`2.8.14`.** The package CONTENT is correct — it contains the `width: auto` fix described under 2.8.15
— only the provenance label is one release behind. An auditor who checks out the labelled commit will
find the fix missing and conclude the release did not ship it, which is why this is written down
rather than left as a curiosity.

Cause: `eng/pack-nuget-packages.sh` packed with `--no-build`, and `dotnet pack --no-build` inherits
`SourceRevisionId` from `obj/`, where it was stamped by the LAST build — one commit back.

Fixed for every future release in `7dcdce52`: the pack script passes
`-p:RepositoryCommit=$(git rev-parse HEAD)` and, after packing, reads the label back **out of the
produced bytes** and refuses to publish a mismatch. Guarded both ways by
`ReleaseContractTests.PackedPackages_RecordTheCommitTheyWereBuiltFrom`.

The already-published `2.8.15` is deliberately **not** repackaged: republishing the same version with
different bytes is worse than the wrong label, because it breaks the one property a version number
has. It gets corrected by the next release. **When 2.8.16 ships, verify that the label matches its own
commit** — that is the first live proof the guard works during a real pack, not only in a test.

## 2.8.15 - 2026-08-10

One fix, and it is a P1: since 2.8.13 a host that set the floating action bar's start inset got a bar
that hung off the right edge of the screen, with its primary button unreachable by mouse.

### Fixed

- **`TmFormActionBar` in `FloatingBottom` never reset the base `width: 100%`, so the two insets and the
  width formed an over-constrained box.** For a `position: fixed` element the containing block is the
  viewport, so `width: 100%` resolves to the full window width. Combined with
  `inset-inline-start` + `inset-inline-end` that is three constraints for one axis, and CSS resolves it
  in LTR by dropping the END inset. A host setting
  `--tm-form-action-bar-inset-inline-start: 18.5rem` on a 1440px window therefore got a bar laid out at
  296…1736px — overflowing the viewport by exactly the start inset. Because `__end` right-aligns the
  actions, the primary button landed around x 1620…1700, entirely off-screen.

  This was worse than a cosmetic overflow: a fixed element contributes nothing to the document's
  scrollable area, so no horizontal scrollbar appeared and the button could not be reached by mouse at
  all. Keyboard users could Tab to it, but the focus ring was painted outside the window.

  The floating variant now sets `width: auto` and lets both insets decide the box, which is what the
  inset variables promised in 2.8.13. `2.8.13` and `2.8.14` both ship the defect; hosts that never set
  the inset were unaffected, because `left: 0; width: 100%` happens to equal the viewport.

  Guarded by `FormActionBarCss_FloatingBottom_ResetsBaseWidthSoBothInsetsDecideTheBox`. The existing
  inset test could not have caught it — it only ever asserted the START inset, so it kept confirming
  that the bar began in the right place while saying nothing about where it ended.

## 2.8.14 - 2026-08-09

Four review findings against 2.8.13, three of them in the floating layer that shipped the day before.
Nothing here changes an API; it makes 2.8.13 behave the way its own documentation already claimed.

### Fixed

- **`TmUserPicker`'s floating layer never actually released itself.** The tracking entry was keyed by
  the menu element and released by passing that `ElementReference` back to script. Blazor resolves an
  `ElementReference` through a document query, and a closing list is out of the DOM by the time the
  release runs — so the query returned null, `release` returned immediately, and the entry plus the
  shared scroll/resize listeners stayed behind. It was self-healing (the next scroll dropped it through
  the `isConnected` guard) but the code comment and the unit test both described something that never
  happened in a browser. The map is now keyed by a string the component owns (`{Id}-results`), released
  by that same string, and the test asserts the argument rather than merely the call.
- **Switching `FloatingMenu` off while a list was open left the layer attached.** `OnAfterRenderAsync`
  returned early on `FloatingMenu`, so the release branch was unreachable and the script went on writing
  inline `left`/`top`/`width` onto a list that was no longer floating. The guard now sits inside the
  anchor condition, leaving the release path open.
- **The floating variant silently lost the list's height cap.** `.tm-user-picker__results` caps at
  `max-height: 240px`, but the script overwrites `max-height` on every placement and wrote the whole
  available space, so a floating list could be far taller than the same list not floating. It is now
  clamped to the stylesheet's 240px, with a unit test tying the constant to the CSS so the two cannot
  drift. Note the asymmetry, which is deliberate: only the ceiling can be clamped. Forcing a *floor*
  into `max-height` would not create room — it would push the list out of its containing block, where
  the modal clips it, which is the exact symptom the floating layer exists to remove. Too little room on
  both sides is a layout problem in that dialog, and the list's honest response is to scroll within what
  is left.

### Changed

- **`.tm-form-action-bar--floating-bottom` uses the logical inset properties.** The custom properties
  were named `--tm-form-action-bar-inset-inline-start` / `-end` but were applied to physical `left` /
  `right`, so the names were only true in left-to-right text. In LTR nothing changes; in RTL the start
  inset now follows the writing direction, as the name always promised.

## 2.8.13 - 2026-08-09

Six gaps that consuming applications had been papering over in their own code. Every one of them is a
place where the library made the application reach around it — with a CSS override that has to win a
specificity fight, with an `@key` remount, or with a marker the component would not render. Everything
here is additive; no released member changed shape or meaning.

### Added

- **`--tm-form-action-bar-inset-inline-start` / `--tm-form-action-bar-inset-inline-end` — the floating
  bar can be told about the shell's chrome.** `.tm-form-action-bar--floating-bottom` pinned `left: 0`,
  so in an application with a fixed side navigation the bar ran underneath it. The only way out was to
  override `left` from application CSS — and because Tempo ships as *isolated* CSS, that rule carries a
  `[b-…]` scope attribute and therefore a specificity of at least (0,2,0), while the application's
  stylesheet is loaded *before* the scoped bundle. A tie goes to Tempo, so the obvious override is a
  dead rule that looks identical to a working one on a screenshot. Both insets now read a custom
  property, defaulting to `0` — the released full-bleed behaviour — so the host sets a variable instead
  of fighting the cascade.
- **`TmScrollSpyNav.ScrollContainerSelector` — scroll-spy on the element that actually scrolls.** The
  listener went on `window`, which is right only when the document itself scrolls. An application shell
  that puts its content in an `overflow-y: auto` column raises the scroll event on that column and never
  on the window, so `EnableScrollSpy` did nothing at all and did it silently. The selector names that
  column and `ScrollOffset` is then measured from the top of the container rather than the viewport. A
  selector that matches nothing falls back to the window rather than going quiet.
- **`TmScrollSpyNav.AutoSelectFirstItem` — the option to have nothing current.** `OnParametersSet`
  unconditionally made the first visible item active, so with scroll-spy off `aria-current="true"` sat
  on the first section forever and went stale as the reader scrolled. Setting it to `false` leaves every
  item non-current until a click or scroll-spy selects one. This has to be a parameter of its own: the
  workaround it replaces — passing `ActiveId=""` — hands the component an id no section has.
- **`TmUserPicker.Id` and `Required`, plus `aria-controls` and `aria-activedescendant`.** The label was
  a bare `<label>` with no `for` and the input had no `id`, so clicking the label did nothing and no
  assistive technology tied the two together. There was no way to mark the field required, so consumers
  reproduced the asterisk with their own CSS. And `role="combobox"` shipped without either of the two
  attributes that let a screen reader announce the option the arrow keys are moving over — focus stays
  in the input, so without them the highlight is invisible to a reader. The listbox is now
  `{Id}-results` and each option `{Id}-option-{i}`, both referenced from the input. `for` is omitted
  while a user is selected, because the input is not on screen then and a dangling `for` is worse than
  none.
- **`TmUserPicker.FloatingMenu` — the results list can leave the flow.** The list is `position:
  absolute`, so an ancestor with `overflow: auto` — a modal body, a scrolling form column — clipped it
  and scrolled it away from its input. Opting in makes it `position: fixed` and has
  `TmUserPicker.razor.js` anchor it to the input on every scroll (captured, so inner scrollers count)
  and resize, flipping it above the input and clamping its height when there is not enough room below.
  It is off by default: it costs a module import, and a picker that is not inside a scroll container
  does not need it.
  - This is a floating layer, **not** a DOM portal, and the difference is visible in one case: an
    ancestor with a `transform`, `filter`, `perspective`, `contain`, or a `will-change` naming them
    becomes the containing block for a fixed descendant. `.tm-modal` is one, because it animates in with
    a transform. Inside such an ancestor the list is bounded by that box instead of by the viewport,
    which is why the available space is measured against it and the list flips rather than overflowing.
    The clipping and the scroll-away — the actual reported failure — are gone either way.
  - The scroll and resize listeners are shared by every picker on the page and are unbound as soon as
    the last open list closes.
- **`TmNumberInput.Required` and `Id`, and `TmToggle.Required`.** A form mixing these with
  `TmTextInput`/`TmSelect`/`TmTextArea` marked some of its mandatory fields and, with no parameter to
  reach for, silently not the others. `TmNumberInput` renders the same `tm-input-label-required`
  asterisk plus `required`/`aria-required`, and its label finally has a `for` — an asterisk on a label
  that names nothing is decoration. `TmNumberInput.Id` also drives the error and help ids
  (`{Id}-error`, `{Id}-help`), which were previously generated and unaddressable.
  - `TmToggle.Required` deliberately does **not** emit the native `required` attribute. On a checkbox
    that means "must be checked", which is right for an "I agree" box — `TmCheckbox` does emit it — and
    wrong for a switch whose off state is a legitimate answer: the form would refuse to submit. It marks
    the visible label and sets `aria-required` on the input; the value is the form's to validate.

### Notes

- `TmUserPicker` now implements `IAsyncDisposable` instead of `IDisposable`, so it can release the
  floating layer's module. Blazor disposes components through whichever interface they implement; no
  consumer calls `Dispose()` on a component directly.

## 2.8.12 - 2026-08-08

Two gaps in the public API, not cosmetics: in both cases a consuming application could see the right
thing on screen and still had no supported way to ask for it. Everything here is additive — no released
member changed shape or meaning.

### Added

- **`TmDataTable<TItem>.PageSize` / `PageSizeChanged` / `public ChangePageSizeAsync(int)` — a way to
  change the page size of a table that is already mounted.** `DefaultPageSize` was read exactly once, in
  `OnInitializedAsync`, and the only member that could change the size afterwards was `private`. A page
  with its own page-size control therefore had to remount the table through `@key` — which resets the
  page size, and along with it the scroll position, the focus, the selection, the sort and every expanded
  row. Both new routes resize in place.
  - `PageSize` is the **controlled** counterpart of `DefaultPageSize`: nullable, and when supplied it wins
    — including in the provider's *first* query, because it is applied in `OnInitializedAsync` rather than
    afterwards, so a server-side table is not made to fetch the default page size first. Leaving it null
    keeps the released `DefaultPageSize` behaviour exactly as it was.
  - `PageSizeChanged` enables `@bind-PageSize`. It fires for the built-in dropdown, for
    `ChangePageSizeAsync`, for an applied saved view, and when an `IDataTableDataProvider<TItem>` answers
    with a page size other than the one asked for — a provider-imposed size wins, and a bound host that
    never heard about it would be describing a table that no longer exists. The remembered parameter value
    is updated **before** the callback is invoked, so the value coming straight back in as a parameter is
    not mistaken for a new host-driven change and does not issue a second query.
  - Changing the size **returns to page one**, deliberately: page *N* denotes a different slice of the
    data at a different size and at a larger size may not exist at all. This is what the built-in dropdown
    has always done, so the public routes and the built-in control cannot disagree.
  - A size of zero or less is rejected with `ArgumentOutOfRangeException` rather than silently producing
    an empty table (`Take(0)`).
  - **Controlled without a binding re-syncs instead of drifting.** If `PageSize` is supplied but
    `PageSizeChanged` is not, a change made through the dropdown, `ChangePageSizeAsync` or a saved view
    can never reach the host, so the next parameter set snaps the table back to `PageSize`. Documentation
    alone would not have prevented the two numbers from disagreeing silently. A page size imposed by the
    *provider* deliberately does not arm this: the provider would answer the re-synced query with the same
    imposed size again, costing one query and one jump back to page one per parent render.
- **`TmToggle.AriaLabel` / `AriaLabelledBy` / `Id` — an accessible name without visible text.** `Label`
  filled the visible `<span>` **and** the input's `aria-label` at once, and `AdditionalAttributes` splat
  onto the wrapper `<div>`, so an `aria-label` supplied from outside named the wrapper and not the switch.
  A page whose channel labels live next to the toggle — because the element carrying `data-testid` has to
  contain the switch *alone*, or a click in its middle would miss the control — therefore shipped a switch
  that a screen reader announced with no name at all.
  - All three land on the inner `<input type="checkbox" role="switch">`, never on the wrapper. `AriaLabel`
    takes precedence over `Label` for the accessible name and renders no visible text; `AriaLabelledBy`
    points at a label the page already renders; `Id` lets a host-owned `<label for="…">` reach the switch.
  - `Label` still sets `aria-label` on the input, and `AdditionalAttributes` still splat onto the wrapper,
    so `data-testid` keeps addressing the same element as before.

### Fixed

- **`TmDataTable<TItem>` applied a superseded provider response over a newer one.** The pager, the
  filters, sorting, search and the page size each start a load without awaiting the one in flight, and
  `LoadFromProviderAsync` applied whatever arrived last to `_displayedItems`, `_totalCount`,
  `_currentPage`, `_pageSize` and `_totalPages` — so a slow earlier query could overwrite a faster later
  one, and the new `PageSizeChanged` would have reported the stale size to the host on top of that. Each
  load now carries a generation id and applies its result, clears the loading flag and reports the page
  size only while it is still the newest query.
- **A saved view with `PageSize = 0` produced an empty table.** `ApplyViewAsync` accepted any non-null
  value; it now ignores a stored size that is not greater than zero, matching the validation on the new
  public routes.

## 2.8.11 - 2026-08-08

One defect, in one component, that turned out not to be a convenience problem. `TmSearchInput`
delivered every edit to `ValueChanged` **twice**, and with `DebounceMs` set the two deliveries were
`DebounceMs` apart — which is a broken parameter contract, not merely a redundant call.

### Fixed

- **`TmSearchInput` raised `ValueChanged` twice for a single user edit.** The `<input>` is bound to
  `ValueChanged` on **both** `@oninput` and `@onchange`. `HandleInputAsync` armed the debounce timer,
  but `HandleChangeAsync` fired immediately and never cancelled that timer, so one edit followed by a
  blur (or Enter, or autofill, or the browser's native clear cross) produced two deliveries
  `DebounceMs` apart. In a consuming application this issued two concurrent GETs for one keystroke
  sequence and made an end-to-end test flaky: the waiter was satisfied by the *second* request while
  the first was still in flight.
  - The component now remembers the value it last handed to `ValueChanged` in a private field and
    suppresses a delivery that would repeat it. The field is written on **every** dispatch path — both
    branches of `HandleInputAsync`, the timer's `Elapsed` callback, `HandleChangeAsync`, and
    `ClearValueAsync` — and only ever from the renderer's synchronization context; `Elapsed` runs on a
    thread-pool thread, so it hops through `InvokeAsync` before touching it.
  - The suppression deliberately does **not** compare against the `Value` parameter. `Value` is
    optional, and a consumer may bind `ValueChanged` alone and never supply it, leaving `Value` at
    `string.Empty` forever — a `Value`-based comparison would therefore fail to suppress the duplicate
    on exactly the bindings that produced the flake. A regression test covers the no-`Value` binding
    specifically.
  - `@onchange` **stays bound**. Dropping it would have removed the duplicate at the cost of browser
    autofill and the native clear cross of `<input type="search">`, neither of which necessarily raises
    an `input` event this component sees. `HandleChangeAsync` now cancels the pending debounce instead,
    so the edit is delivered once, immediately.
  - `ClearValueAsync` also cancels the pending debounce. Without it, a timer still carrying the old
    text would fire after the click and silently undo the clear. The clear button dispatches
    unconditionally: it is an explicit user action, and a consumer may have set `Value` externally
    since the last dispatch.
  - Consecutive identical values are now collapsed on the undebounced path too (`DebounceMs = 0` had
    the same double-delivery on blur). Consumers that counted on receiving the same search string
    twice in a row will see one call; no consumer in the library did.
  - **Suppressing a repeat does not outlive the consumer resetting `Value` itself.** Remembering the
    last dispatched value alone would have swallowed a legitimate search: after a "clear filters"
    button or a restored saved search writes `Value` from outside, the box no longer holds what was
    delivered, and retyping that same text would never have reached the consumer. `OnParametersSet`
    therefore forgets the remembered value when `Value` *changes* to anything other than what was
    last dispatched. It keys on the change, never on what `Value` currently is — a consumer that
    binds only `ValueChanged` leaves `Value` at `string.Empty` permanently, so it never changes and
    the check stays inert exactly where the suppression must keep working. A parent re-rendering
    mid-search (results arriving, a spinner toggling) is covered by its own regression test.

## 2.8.10 - 2026-08-08

Three defects that all trace back to the same element — the column pin toggle — plus the keyboard
contract 2.8.9 got half right. Making the sortable header focusable in 2.8.9 was correct, but it sent
keyboard users into a header nobody had ever been able to reach, and what was waiting there had been
broken since the pin toggle shipped.

### Fixed (accessibility)

- **Five of the nine Tab stops in a header painted nothing (WCAG 2.4.7 Focus Visible).** `ShowColumnMenu`
  defaults to `true`, so a pin `<button>` is rendered for **every visible column**, sortable or not, with
  no `tabindex="-1"` — it is a native focus stop. The only rule that made it visible was
  `.tm-data-table th:hover .tm-col-pin-btn`; the stylesheet contained no `:focus` or `:focus-within` rule
  for it at all. A five-column header is therefore nine Tab stops, five of which a keyboard user cannot
  see. It is now revealed by `th:focus-within` and by its own `:focus-visible`, next to the existing
  `:hover` — not instead of it.
  - The button deliberately **keeps** its place in the tab order. Adding `tabindex="-1"` would have
    removed the five invisible stops by making the control keyboard-inaccessible, trading a 2.4.7 failure
    for a 2.1.1 one. Whether a grid should expose one stop per header at all (roving `tabindex`, arrow-key
    navigation, one stop for the whole table) is a redesign of the keyboard model, not a fix.
- **Tempo now ships its own focus ring for every element the table makes focusable** — the sortable
  `<th>`, the pin button, and `tbody tr` (rows are `tabindex="0"` unconditionally). 2.8.9 turned the
  header into a control but left its focus indicator to whatever the consuming application declared
  globally. That turned out not to be a theoretical gap: measured on a live page in an application that
  *does* declare `*:focus-visible { outline: 2px solid … }`, a focused sortable header still computed
  `outline-width: 0px` with `outline-color: currentcolor` — the initial values — while an `<a>` on the
  same page got the full 2px ring. Adding the rule below raised the same element to
  `2px solid rgb(79, 70, 229)`. Whatever suppresses the global rule inside the table, a component that
  turns an element into a control cannot depend on the host to make focus visible. `.tm-col-sortable`
  also answers `:focus-visible` with the same emphasis it already gave `:hover`.
- **Space on a sortable header sorted the table *and* scrolled the page away from the result.** The
  header is a `<th tabindex="0">`, not a `<button>`, so Space keeps its browser meaning there — scroll one
  screen — and accepting it as a sort key added to that default instead of replacing it. **Space no longer
  sorts; Enter does**, with Shift still mirroring the multi-sort modifier.
  - The scroll cannot be suppressed from Blazor. `@onkeydown:preventDefault` binds when the handler is
    registered, not per event, so it cannot look at the key; a static `true` would cancel the default
    action of **Tab** as well and trap the keyboard (WCAG 2.1.2) — strictly worse than the annoyance it
    fixes. Doing it from JavaScript would make a keyboard path depend on an interop module the consumer
    has to reference. Enter alone also matches the convention: Space activates a `button`, and this
    element is a `columnheader` inside a `grid`, where Space carries no activation meaning.
  - **This is a behaviour change from 2.8.9.** A host that documented Space as a sort key needs to say
    Enter instead.

### Fixed

- **`Align=Right` still left the header short — the 2.8.9 fix landed on the specificity half only.** With
  the cascade repaired, `text-align: right` reached the `<th>`, but the pin button sat **after** the label
  and reserved `1.1rem + 0.25rem = 21.6px` of inline space permanently, because `opacity: 0` paints
  nothing yet still takes its width. The browser was therefore right-aligning "label + invisible button",
  so the label stopped ~22px short of the content edge its own cells sat flush against — measured on a
  1440px page: cells ended at x=1398, the header text at x=1375. Left-aligned columns never showed it,
  because the slack drained to the right; it appeared on exactly the columns the 2.8.9 fix was made for.
  The pin is now positioned absolutely at the inline end of its header, so it reserves nothing, clears the
  resize handle, and reads as an overlay rather than colliding with the tail of a long label.

### Internal

- `TmDataTablePinFocusTests` (7) resolves the cascade out of the shipped `_data-table.css` for a modelled
  interaction state (header hovered / focus-within, pin hovered / focused / pinned) and asserts the
  `opacity` and `position` that actually win on specificity-then-source-order — the same technique as
  `TmDataTableAlignmentTests`, because both defects are invisible in the markup. Asserted in **both**
  directions on purpose: a permanently visible pin would satisfy "visible on focus" while being a worse
  component, so `PinButton_StaysHidden_WhenNothingIsHoveringOrFocusingTheHeader` holds the other end.
- `TmDataTableKeyboardSortTests.Space_SortsAscending` is **replaced** by
  `Space_DoesNotSort_SoItKeepsScrollingThePage`. The assertion is reversed deliberately: the old one
  encoded the defect.
- Not done, and recorded rather than dropped: `HandleRowKeyDownAsync` accepts Space for row activation and
  has the identical scroll-plus-activate problem. It is left alone in this release because row activation
  is bound to consumer navigation far more widely than header sorting, so changing it is a wider
  behavioural decision than this patch should make on its own.

## 2.8.9 - 2026-08-07

### Added

- **`TmDataTable.DefaultSortColumn` / `DefaultSortDirection`** — the order a table is in *before* the
  user touches a header. Sorting is tri-state (ascending → descending → none) and always started at
  "none", so a page whose list had previously arrived pre-sorted had no way to say so: not through a
  parameter, and not through `LayoutStore`, which persists column widths and pin state only. The only
  remaining route was to fake a header click during startup, and that is a *different* state — it
  leaves the cycle one step along, so the user's next real click clears the sort instead of reversing
  it. The parameters take a column key (`PropertyName`, falling back to `Title`) and a
  `DataTableSortDirection`, and both defaults are unchanged behaviour: with `DefaultSortColumn` null
  the table still starts unsorted.
  - The default is seeded during initialization **before** the first data load, so a server-side
    `IDataTableDataProvider` receives it as `SortColumn`/`SortDescending` in its very first query. A
    table that sorted only after fetching would otherwise show page one of the *provider's* order
    re-sorted in place — the wrong rows entirely, not merely the wrong order.
  - It is a starting state, not a floor. Clicking cycles on from it (from `Ascending`, the first click
    on that same column sorts descending and the second clears the sort), applying a saved view
    replaces it, and an unknown key simply leaves the table unsorted rather than throwing.
  - The column does not have to be `Sortable`. A non-sortable column still orders the data, it just
    does not advertise `aria-sort` or respond to clicks — which is how you express "this list has a
    fixed order the user cannot change".
- **`TmPagination.ShowInfo`** (default `true`) — whether the pager renders its own "X–Y of Z" label.
  Standalone pagers keep it; a host that already states the range beside the pager turns it off.
- **`TmDataTable.ResetPageAsync()`** — the public way back to page one, for a host that narrows the
  result set itself. A page that owns its filter surface (`ToolbarMode=ContentOnly`, feeding the table
  pre-filtered `Items` or a `DataProvider`) hands over an already-narrowed list, and the table cannot
  tell a new query from the same list arriving again: `RefreshClientItems` only clamps the current page
  **down** to the new last page. So searching while on page 3 left the user on page 3 — reading the tail
  of the new results — and the reset built into the table's own search box is never reached, because the
  host did the searching. There was no public member that could fix it either: `GoToPageAsync` is
  private, and handing the table `SearchText` to trigger its reset has side effects (it switches on the
  built-in client-side filtering over the host's server-side filter).
  - Deliberately **not** automatic on an `Items` reference change: a table re-polling the same list on a
    timer would then yank a reading user back to page 1 on every refresh. The host knows which change is
    a new query; the table does not.

### Fixed (accessibility)

- **Sorting was mouse-only (WCAG 2.1.1 Keyboard).** The click handler sits on the `<th>`, which is a
  plain element — no `tabindex`, no key handler — so a keyboard user could not sort a table at all. The
  sortable header is now a focus stop and answers **Enter** and **Space**, with **Shift** mirroring the
  multi-sort modifier of Shift-click; `aria-sort` already carried the state. The target stays the `<th>`
  itself rather than becoming a `<button>`: the header also hosts the consumer's `HeaderTemplate`, the
  pin toggle and the resize handle, so a wrapping button would nest interactive elements. Non-sortable
  headers are not focus stops. The pin toggle now stops keydown propagation, so operating it with the
  keyboard does not also sort the column. The global `:focus-visible` ring covers the new focus stop.

### Fixed

- **`TmDataTable` printed the item count twice.** The paging footer rendered the range in the table's
  own summary (`pagination-summary`) *and* again inside the embedded `TmPagination`
  (`pagination-info`), side by side, so every paginated table showed "Showing 1–10 of 22   1–10 of 22".
  2.8.8 flagged this as knowingly not done, because removing either one is a visual change to a
  shipped component; it is done now, and the consumer picks which one via the new
  **`TmDataTable.PaginationInfoPlacement`**:
  - `Summary` (**default**) — the table's summary on the left of the footer. This is the placement
    that honours `PaginationInfoTemplate` and the `TmDataTable_ShowingItems` resource, so the wording
    a host had already customized is the wording that survives.
  - `Pagination` — the range moves into the pager itself (`TmDataTable_Pagination` resource), matching
    how a standalone `TmPagination` looks.
  - `None` — page controls only.

  Exactly one of the two `data-testid`s now exists in the DOM at a time, which is also what makes the
  duplication impossible to reintroduce silently.
- **The same duplication in `TmMultiViewList`**, which embeds `TmPagination` under its own identical
  summary. It gets the same `PaginationInfoPlacement` parameter with the same default. Its per-group
  mini-pagers are untouched: they have no sibling summary, so they keep stating their own range.
- **`TmDataTableColumn.Align` did nothing inside a data table.** The markup was never at fault — the
  table has always put `tm-text-center` / `tm-text-right` on both the `th` and its `td`s. The
  stylesheet discarded them: `.tm-data-table th, .tm-data-table td { text-align: left }` is a class
  plus a type (specificity 0-1-1) while the helpers `.tm-text-right` / `.tm-text-center` are a bare
  class (0-1-0), so the base rule won every column regardless of source order. Applications did not
  notice in the body because a right-aligned cell usually holds its own flex container that aligns
  itself; a header is bare text, so an actions column read `Align="Right"` and rendered its cells
  right but its **header left**. The helpers are now re-stated at the table's own specificity, so
  `Align` reaches the header and the cells together.

### Internal

- 42 new bUnit test cases: `TmDataTableDefaultSortTests` (13, including the two server-side cases that
  assert the default reaches the *first* provider query), `TmDataTableAlignmentTests` (9 cases from 5
  methods), `TmDataTableKeyboardSortTests` (8), `TmDataTableResetPageTests` (5),
  `TmDataTablePaginationTests` (+5), `TmPaginationTests` (+2). The `DataTable` namespace goes from 246
  to 288 tests.
- `TmDataTableResetPageTests` pins the regression itself, not only the fix: one test asserts that
  without `ResetPageAsync` a narrowed result set leaves the user on the clamped last page, so the fix is
  measured against the behaviour that was actually there.
- The alignment fix is CSS-only, so a class assertion could not have caught it — the class was always
  present. `TmDataTableAlignmentTests` therefore resolves the cascade out of the shipped
  `_data-table.css`: it parses the rules, matches them against a modelled
  `<table class="tm-data-table"> … <th class="tm-text-right">` chain, and asserts the `text-align`
  that actually wins on specificity-then-source-order. Mutation-checked: deleting the four-line fix
  from the stylesheet turns **exactly** the four cascade assertions red, and they report `left` — the
  real defect — while the class-list assertions stay green, which is precisely the blind spot they had.
- `JsonDocumentation` updated for the MCP server: `DefaultSortColumn`, `DefaultSortDirection` and
  `PaginationInfoPlacement` on `TmDataTable`, `ShowInfo` on `TmPagination`, `PaginationInfoPlacement`
  on `TmMultiViewList`.
- Not done, and unchanged from 2.8.8: `TmMultiViewList` and `TmTreeList` still do not derive from
  `TmComponentBase`, so their embedded pagers have no `TestIdPrefix` to forward and emit the bare
  `pagination-*` ids.

## 2.8.8 - 2026-08-07

### Added

- **`TmPagination` is addressable from the outside.** It had no test hook of any kind: the only way
  to reach the prev/next buttons, a specific page button or the item-range text was to select on the
  presentational CSS classes (`.tm-pagination-next`, `.tm-page-btn:nth-child(n)`), which makes a
  consumer's suite break on any styling refactor and gives it no way to tell two pagers apart. The
  component now derives from `TmComponentBase` and renders the library's `data-testid` convention:
  `pagination` on the root, `pagination-info` on the item-range text, `pagination-prev` /
  `pagination-next` on the step buttons, `pagination-page-{n}` on each page button, and
  `pagination-page-size` on the page size `<select>`. `TestIdPrefix` namespaces all of them
  (`users-pagination-next`), `DataTestId` overrides the root id only. With neither set the bare ids
  are used, so this is additive markup — no existing selector, class or style changes.
- **`TmDataTable` propagates the hooks into its built-in pagination.** This was the actual blocker:
  the table splats `AdditionalAttributes` onto its own wrapper but passed nothing to the embedded
  `TmPagination`, so a consumer that had migrated its own pager to `TmDataTable` lost every handle on
  the paging controls. The table's `TestIdPrefix` is now forwarded to the pagination, and the paging
  footer carries `pagination-container` (the footer row) and `pagination-summary` (the table's own
  "showing X–Y of Z" text, which is a *different* element from the pagination's `pagination-info`).
- **`TmDataTable.PaginationAttributes`** — a `Dictionary<string, object>?` splatted onto the built-in
  pagination's root element, for host-owned attributes that are not test ids (analytics ids, extra
  ARIA).
- **`TmDataTable.PaginationInfoTemplate`** — replaces the built-in `TmDataTable_ShowingItems` summary
  with host markup. The context is the new `DataTablePaginationInfo` record (`CurrentPage`,
  `TotalPages`, `PageSize`, `TotalCount`, `StartItem`, `EndItem`), so an application migrating its own
  table to `TmDataTable` can keep its established wording — e.g. "Page 1 of 3 (22 records)" — instead
  of being forced onto the library's phrasing. The resource key stays the default when the template is
  null; no localization keys were added or changed in this release.
- **The active page button is marked `aria-current="page"`.** It previously carried only the
  `tm-page-btn-active` class, so assistive technology had no way to announce which page is current,
  and a test had to assert on a styling class to find it.

### Internal

- Per-group mini-pagers (server-side group paging) get `TestIdPrefix` = `{table prefix-}group-{group
  key}`, so several group pagers on one table stay individually targetable instead of all answering
  to `pagination-next`.
- 11 new bUnit tests (`TmPaginationTests`, `TmDataTablePaginationTests`). Verified: the whole
  `Tempo.Blazor.Tests` suite is green at **9272/9272** on net10.0 with the change in.
- Mutation-checked, because "the ids are there" is easy to assert vacuously: removing the
  `TestIdPrefix="@TestIdPrefix"` propagation from `TmDataTable.razor` turns **exactly one** test red
  (`DataTable_TestIdPrefix_IsPropagatedIntoTheBuiltInPagination`) and leaves the other 245 DataTable
  tests green — the prefixed-id assertions are the only thing holding that wire, and they do hold it.
- Not done, and it is a real gap: `TmMultiViewList` and `TmTreeList` also embed `TmPagination` but do
  not derive from `TmComponentBase`, so they have no `TestIdPrefix` to forward. Their pagers emit the
  bare `pagination-*` ids. A page that puts one of them next to a `TmDataTable` must set
  `TestIdPrefix` on the table, otherwise the two pagers answer to the same ids.
- Also not done: inside a `TmDataTable` the item range is rendered **twice** — once by the table
  (`pagination-summary`) and once by `TmPagination` itself (`pagination-info`). That predates this
  release; removing either one is a visual change to a shipped component and did not belong in a
  test-hook release. The two ids at least tell them apart now.

## 2.8.7 - 2026-08-02

### Fixed

- **`TmTabs` had half a roving tabindex.** `ArrowRight`/`ArrowLeft` moved the *selection* —
  `aria-selected` and the `tabindex` 0/-1 pair — but never moved the **DOM focus**, so after a key
  press the browser focus was still sitting on the tab the user had just left, on an element that
  now carried `tabindex="-1"`. Measured before the fix on the demo strip:
  `selected=tab-details, activeElement=tab-overview`. Consequences for a keyboard user: the focus
  ring and the screen-reader cursor stay behind, the next arrow key is dispatched from the wrong
  element (so two `ArrowRight`s advance **one** tab, not two), and the next `Tab` leaves the strip
  from the wrong place. The tab buttons now carry an element handle and the newly selected one is
  focused after the render that re-assigns the `tabindex` values.
- **`Home` and `End` were not handled at all** — the key handler had a bare `else return` after the
  two arrows. They now jump to the first/last *selectable* tab, scanning inwards from the edge so a
  disabled first/last tab is stepped over rather than swallowing the key, which is the same rule the
  arrows already followed.
- **A REFUSED CLICK left the focus ring on the refused tab.** The browser focuses a clicked button
  before any component code runs, so on a strip whose consumer rejects the change the ring stayed on
  a tab that is neither `aria-selected` nor in the tab order — measured:
  `activeElement=#tab-veto-second, selected=[tab-veto-first], tabindex0=[tab-veto-first]`. Same
  divergence as the keyboard one, reached through the pointer. `SelectTab` now claims the same focus
  move the keys do, which is a no-op when the change is accepted and pulls the ring back when it is
  not. Without this the release's own invariant would have been true of the keyboard only.

### Internal

Test counts below are exact: **45** bUnit tests cover the component (25 `TmTabsTests` + 18
`TmTabsHeaderSlotsTests` + 2 `TmTabsAccessibilityTests`; `--filter ~TmTabs` reports 46 because it
also matches one method in `Wireframe.TempoPackStructurePhase10Tests`), and
`TmTabsKeyboardFocusE2ETests` has **8** `[TestMethod]`s.

- New `TmTabsKeyboardFocusE2ETests` measures the contract at `document.activeElement` in a real
  browser and sends its keys through `IKeyboard` (i.e. to whatever holds focus), never at a located
  element. This closes a whole class of vacuously-green tests: *every* assertion phrased over the
  rendered DOM stays green when the focus move is reverted, because the selection really does move.
- Mutation results, all measured against the shipped implementation:
  - removing the focus move entirely — **45/45 bUnit green**, **7 of 8** e2e red, each on the focus
    assertion with the selection assertion already passed;
  - leaving `Home`/`End` unhandled — **4** bUnit red, **2** e2e red, all on the selection floor;
  - focusing the tab the interaction *requested* instead of the one the consumer confirmed —
    **45/45 bUnit green**, **2** e2e red (both veto arms);
  - reverting only the click's focus claim — **45/45 bUnit green**, **1** e2e red (the click veto
    arm), reproducing `selected=#tab-veto-first activeElement=#tab-veto-second`.
- `HomeScrollsThePage_ButLeavesTheFocusedTabInsideTheViewport` is **not** a focus discriminator and
  must not be counted as one: it is the single method that stays green when the focus move is
  removed, because the browser's own scroll-into-view reaches the rectangle it asserts. It
  discriminates on key handling and on the scroll bound only.
- `TmTabsHeaderSlotsTests` keeps its frozen pre-slot markup hash. The `@ref` needed for focus makes
  the renderer stamp an element-reference marker on each tab button (measured: 1458 bytes vs the
  frozen 1269, i.e. exactly 3 × 63 characters and nothing else); its value is a fresh GUID on every
  render, so it is normalised away before hashing — with the removal **count** asserted, so a
  normaliser that swallowed a second change could not hand back a green hash.
- `TmTabsHeaderSlotsTests` keeps its frozen pre-slot markup hash. The `@ref` needed for focus makes
  the renderer stamp an element-reference marker on each tab button (measured: 1458 bytes vs the
  frozen 1269, i.e. exactly 3 × 63 characters and nothing else); its value is a fresh GUID on every
  render, so it is normalised away before hashing — with the removal **count** asserted, so a
  normaliser that swallowed a second change could not hand back a green hash.

### Known gaps

Both are deliberate, and neither is a regression — before this release the arrow keys moved no
focus at all and `Home`/`End` did nothing.

- The arrow keys are **not mirrored in RTL**: the handler compares physical key names, so in a
  right-to-left strip `ArrowRight` moves towards the end of the panel list rather than towards the
  tab drawn to its right. Left out because nothing hands a Tempo Blazor component a direction to
  mirror against. Population: **3090** own `.razor`/`.cs`/`.css` files under `src/` (excluding
  `obj/`, `bin/`, `wwwroot/lib/`); needles `dir="rtl"`, `[dir=`, `:dir(`, `direction: rtl`, `IsRtl`
  → **0** hits. That population deliberately does **not** cover the 530 `.mjs` files under `src/`,
  and RTL machinery does live there, *inside* a Blazor package: the document editor ships a Unicode
  bidi resolver (`Tempo.Blazor.DocumentEditor/wwwroot/js/document-editor/layout/bidi.mjs`,
  `core-engine/bidi-line.mjs`) and stamps `dir="rtl"` on spans it renders
  (`render/atomic-renderer.mjs:306`). It is the editor laying out its **own** canvas text, not a
  direction context another component can read — 16 `.mjs` files match `rtl|RightToLeft|bidi` and
  none of them is reachable from a tab strip. (The reporting engine's `ReportTextDirection` is a
  separate stack again, and its `HbDirection` is an alias for `HarfBuzzSharp.Direction`, not a
  product type.) A lone direction-aware component would have nothing to test it against.
- **`Home`/`End` do not suppress the browser's default scroll.** On a long page the viewport jumps
  before the focus call brings the strip back. Bounded by
  `HomeScrollsThePage_ButLeavesTheFocusedTabInsideTheViewport`, which reproduces the **settled**
  scroll position (`2437`) and the focused tab's rectangle (`top=429`/`bottom=471` in a 900px
  viewport); the positions on the way there vary run to run (`346 → 1788` and `285 → 1872` were both
  observed) and are logged, never asserted. So the strip is never lost, but the scroll is visible.
  Suppressing it means `@onkeydown:preventDefault`, which *does* accept a bool expression — **6**
  places in Tempo use that form (`TmRichEditorSimple`, `TmMarkdownEditor`, `TmRichEditorFull`,
  `TmSigningFieldOverlay`, and `TmDocumentPageViewer` twice) out of **187** `:preventDefault=`
  directives under `src/`, the other **181** being the literal `"true"` — but the expression is
  evaluated at **render** time and so cannot
  depend on which key was pressed. It would have to apply to every keydown on the tablist,
  swallowing `Tab` and trapping focus inside the strip: a worse defect than the one it fixes.

### Contract

- **`TmTabs` is a controlled component, and the focus now obeys that — on both input paths.** A key
  or a click *asks* through `ActiveTabIdChanged`; the focus target is read back off `ActiveTabId`
  **after** the render, never from the tab the interaction requested. A consumer that ignores or
  vetoes the change therefore keeps both its selection and its focus ring on the same tab. Focusing
  the requested tab would have re-created the exact focus/selection divergence this release removes,
  mirrored. Pinned by `AVetoedChange_LeavesTheFocusOnTheStillSelectedTab` (keyboard) and
  `AVetoedCLICK_PullsTheFocusBackToTheStillSelectedTab` (pointer) against a demo strip whose handler
  rejects everything, each with the accepting strip driven by the same interaction first as a
  liveness floor — under the "no focus at all" mutation it is that floor that fails, so a dead page
  cannot pass either arm vacuously.
  The bound: the claim is resolved on the first render after the interaction and not carried
  further. A consumer that resolves the change asynchronously past that render still gets a focus
  move, resolved against the selection as it stood then — i.e. onto the tab that is still selected,
  a no-op rather than "nothing happens"; when its update lands, the selection moves and the focus
  does not follow.

## 2.8.6 - 2026-08-01

### Fixed

- **The CSS bundler was destroying every `calc()` that adds.** `BundleCss` stripped the whitespace
  around `+` as if it were only a sibling combinator, so `calc(100% + 8px)` shipped as
  `calc(100%+8px)` — invalid per css-values-3 §8.1, which makes the browser drop the **whole**
  declaration. Measured in the committed bundle: **30 such operators across 29 `calc()`
  expressions**, and **0** in the 975 `calc()` expressions of the sources — the defect was made
  entirely by the minifier. The worst of them was `.tm-data-table-scroll`, where the negative margin
  survived but the matching width compensation did not, so **every data table overflowed its
  container**; the rest were dropped `top:`/offset declarations on tooltips, popovers, dropdowns and
  the date picker, which fall back to `auto` and land at the static position instead of the intended
  offset. `+` and `~` are no longer stripped (leaving the space around a combinator is valid CSS and
  costs ~96 bytes); `>` still is, because it is not a `calc()` operator.
- The bundle went unmeasured for a structural reason, now closed: the demo app links the **source**
  `tempo-blazor.css` through its `@import` graph, so no e2e run ever loaded
  `tempo-blazor.bundled.css` — the file the documentation tells consumers to reference. New
  `CssBundleCalcWhitespaceTests` asserts on the **bundle**, tokenising each `calc()` rather than
  pattern-matching it, so `--tm-space-1-5`, `env(safe-area-inset-bottom)`, `calc(-1 * x)` and `1e-5`
  are not mistaken for operators (18 such control samples are pinned alongside 14 defective ones).

### Changed

- `--tm-tab-row-height` is derived with `calc()` again instead of the four per-variant literals it
  carried while `calc()` was broken in the bundle. The literals only held while the root font size,
  the tab padding and the line box all stayed at their defaults: with `:root { font-size: 20px }` a
  Line row measures `2×12.5 + 25 + 2 = 52px`, while the literal still claimed 42px — a centred slot
  then sits 5px off the row. `.tm-tab` now reads its padding and line box from
  `--tm-tab-padding-y`/`-x` and `--tm-tab-line-box`, which is also what the row height is computed
  from, so the two cannot drift apart. Because var() substitution happens at computed-value time on
  the `.tm-tabs` element itself, the token now has a **single** declaration and the variants
  re-declare only the inputs — replacing four same-specificity literals whose winner was decided by
  their order in the file. Rendered geometry is unchanged: `TmTabsHeaderSlotGeometryE2ETests`
  measures slot boxes of 42/38px (Line above/below the 640px breakpoint), 44px (Pill) and 37px
  (Enclosed) with a **0.00px** alignment delta in all 12 measurements.

### Internal

- **An ordinary `dotnet test` can no longer overwrite a committed screenshot.** Two separate routes
  reached them and only one was obvious. The *generators* — classes that exist to rewrite the PNGs —
  were held back by nothing but a doc comment, and three of the seven (`DebtTokenBaselineScreenshots`,
  `ThemeTokenBaselineScreenshots`, `SpreadsheetPhase6BaselineScreenshots`) did not even carry the
  `BaselineGeneration` category. They now inherit `BaselineGeneratorTestBase` and skip unless
  `TM_WRITE_BASELINES` is `1`/`true`/`yes`.
- The larger route was *ordinary* tests writing baselines as a side effect: `NotionE2ETestBase` and
  four Notion test classes each carried a byte-identical private path calculation that wrote
  straight into `__baseline__/notion/`, reaching **765 committed PNGs — 66% of every tracked PNG in
  the repository — from 276 call sites**. A single ordinary test rewrote 12 of them. These are not
  skipped, because they assert real behaviour; their destination is REDIRECTED instead, through
  `BaselineOutput`, exactly as `Demo__DiagramsDbPath` redirects the demo database.
- Holding both routes is `BaselineWriteSweep`, whose predicate is the **write target** rather than a
  class name: it hashes every tracked PNG under the screenshot roots in `[AssemblyInitialize]` and
  fails the run from `[AssemblyCleanup]` if any changed without the opt-in. A guard keyed on naming
  could not see any of those five Notion classes; a guard keyed on what the run touched cannot be
  defeated by renaming. `BaselineGeneratorGateTests` remains as the fast, browser-free check on the
  naming convention itself.
- The demo API no longer writes into the committed `src/Tempo.Blazor.Demo.Api/diagrams.db`: the path
  is overridable through `Demo__DiagramsDbPath`, which the Playwright host launcher points at a
  per-run temp directory. Booting the demo from a test run previously left that file modified plus
  an untracked `-wal`/`-shm` pair.

## 2.8.5 - 2026-08-01

### Added

- **`TmTabs` gains `HeaderLeading` and `HeaderTrailing` slots.** Content supplied to either slot is
  rendered beside the tab strip as a **sibling** of the `role="tablist"` element, never inside it:
  the strip runs a roving tabindex (arrow keys, `tabindex` 0/-1, `aria-selected`), so an element
  that can never be selected would both misreport the strip's contract to assistive technology and
  become arrow-key reachable. The slots therefore add nothing the roving focus walks over.
- The wrapping `.tm-tabs__header-row` flex row is emitted **only** when at least one slot is
  supplied. With neither slot the rendered markup is byte-identical to the pre-slot component
  (measured, not asserted by eye: `TmTabsHeaderSlotsTests` compares a sha256 of the rendered markup
  against a hash frozen before the slots existed), so the strip stays a direct child of `.tm-tabs`
  and every consumer selector shaped `.tm-tabs > .tm-tabs__header` keeps matching.
- Consumers with a **sticky** tab strip must stick the row, not the header — pinning
  `.tm-tabs__header` alone leaves the slot content free to scroll out from beside it. The Line and
  Enclosed variants move their underline onto the row when it exists, so the rule spans the slots
  instead of stopping at the edge of the strip.
- The slots work with `Wrap="true"`. A wrapped strip is a multi-row band, so two things follow and
  both are enforced: the row drops its bottom border (every wrapped row already draws its own
  baseline — keeping it left a rule stranded 41px below the active indicator and a doubled line
  under the last row), and the slots are aligned to the **first** row of the band rather than
  centred over the whole of it.
- The Pill variant centres its header row instead of stretching it. Line and Enclosed stretch so
  their tabs meet the underline drawn on the row; Pill has no underline, and stretching let a 60px
  slot drag the grey tray from 44px to 60px and the pills from 36px to 52px.
- Layout of the slots is guarded by `TmTabsHeaderSlotGeometryE2ETests`, which measures bounding
  boxes in a real browser on both sides of the 640px breakpoint. It takes no screenshots, so the
  nightly baseline lane neither feeds nor gates it.

## 2.8.4 - 2026-07-29

### Added

- **Durable MCP idempotency boundary for Notion authoring.** `INotionIdempotentAggregateProvider`
  lets a host commit the complete response receipt of `notion_apply_block_operations` in the same
  transaction as the aggregate writes the request performs, so a host restart between the write and
  the receipt can no longer produce a second application of the same logical request. Hosts that do
  not implement it keep using the in-process receipt store unchanged. This shipped as 2.7.1 on a
  branch that never reached `main`; it is carried here so the line is single again.
- **`notion_apply_block_operations` accepts `scopeAppId`.** A stateless MCP call carries no ambient
  application scope, so a caller whose credentials reach more than one application could not use the
  tool at all against a host that scopes data per application — every call failed before touching
  the data, while `notion_create_page` and `notion_list_pages` had taken `scopeAppId` since 2.7. The
  value reaches the host as `NotionIdempotentExecutionRequest.ScopeAppId` and stays OUT of the
  request hash: it says where the write lands, not what it writes, and receipts are already scoped
  per application, so an unchanged single-application caller keeps replaying its existing receipts.

## 2.8.3 - 2026-07-26

### Fixed

- Headless document layout now preserves nested inline drawing payloads and emits their image
  display commands, so drawn signatures and other inline images render in exported PDFs with their
  requested geometry.

## 2.8.1 - 2026-07-26

### Fixed

- `DocumentTokenHelper.ExtractTokens` now traverses tables, nested block content controls and inline
  content controls, so headless document assembly discovers the same tokens as visual previews.
- Document-assembly conditions now interpret raw `true` and `false` token values as booleans instead
  of treating every non-empty string as true.
- `DocumentTemplatePreviewService` now uses the public recursive token helper, removing a divergent
  private extraction implementation.

## 2.8.0 - 2026-07-25

### Both dark-theme switches, hover you can actually see, and ink that survives the dark fill

> **What this section covers, and what is missing from this file.** The entries below describe the
> changes of THIS release only. **Versions 2.6.0, 2.6.1 and 2.7.0 were tagged and released but have
> no entry in this changelog** — nobody wrote one, and this release does not reconstruct them. So the
> step from `## 2.8.0` straight to `## 2.5.5` below is a *gap in the document*, not a renumbering:
> upgrading from 2.5.x picks up three undocumented releases as well as everything listed here. Read
> `git log v2.5.5..v2.8.0` for those. The version number is 2.8.0 (not 2.5.6) because it has to be
> higher than the already-published 2.7.0, and a minor because a new public token is added.

### Changed — behaviour

**`--tm-color-on-primary` is no longer white; it follows the theme.** It resolves through
`--tm-text-inverse`, so it stays white in light but becomes the dark ink `#0f172a` in the dark theme —
and with it every "ink on a primary fill" in the library. This is a visible change for any consumer
who is on the dark theme, and a *breaking* one for anyone who built their own rule on the assumption
that the token is white, or who hardcoded `white` next to a primary fill; such a rule now paints
white ink on a light `primary-400` box at 2.54:1 while the rest of the library flips to dark ink.
`--tm-color-primary-contrast` and `--tm-control-glyph-color` become aliases of it, so the three names
are one source instead of three copies that drifted apart.

The reason is a contrast failure, not a preference: the dark theme repoints `--tm-color-primary` to
the lighter `primary-400`, on which white text measures **2.54:1** — under the 4.5:1 SC 1.4.3 asks of
body text. Dark ink on it is **7.02:1**. `.tm-btn-primary` already had that fix in a per-component
dark block; that block is gone now, because a shadowed patch left behind is what the next reader
takes for the rule. The flip reaches every site at once and through **both** theming APIs: the filled
primary button, the `TmMultiSelect` confirm button and option tick, the `TmCheckbox` tick, the
`TmChat` outgoing bubble / send / edit-save buttons, the NotionEditor primary buttons and audit
panel, the Spreadsheet resize dialog and the DocumentEditor toolbar. Light is untouched by
construction — `--tm-text-inverse` is white there.

`.tm-btn-danger` keeps its own dark rule: its fill is `--tm-color-danger`, not primary, and there is
no `--tm-color-on-danger` yet. The collaborative cursor label was DISCONNECTED from this token
instead — it paints on `--tm-wysiwyg-remote-cursor-color`, a per-user runtime colour, so it takes a
paired `--tm-wysiwyg-remote-cursor-ink` and keeps the theme-independent white it was built on.

### Added

- **`--tm-control-hover-fill`** — the tint a selection control takes while hovered and unselected
  (`primary-100` light, `primary-900` dark). It resolves from the primary scale, so a consumer that
  repoints that scale gets a matching tint automatically and must NOT redeclare it per accent.

### Fixed

**The appearance of several components changes in the DARK theme only.** The light theme is unchanged
by construction: every new value resolves to exactly what it resolved to before in light.

- **`.tm-dark` now really is equal to `[data-theme="dark"]`.** Both are documented as public switches,
  but 93 dark rules across 15 stylesheets listed only the attribute — a consumer toggling the theme
  with the CLASS kept light colours in `TmButton`, `TmChat`, `TmDataTable`, `TmScheduler`, `TmStepper`,
  `TmValidationSummary`, `TmAIPrompt`, the PDF viewer and the diagram editor. The drift also ran the
  other way: 34 rules in the six NotionEditor database views were written class-first and never
  reached an attribute consumer. Both directions are fixed and both are now guarded by
  `DarkThemeSelectorParityTests`, which sweeps every shipped stylesheet (scoped `.razor.css`
  included) rather than the files that happened to be broken.
- **`TmCheckbox` / `TmRadio` hover is now an AREA change, not a hue change.** Strengthening the
  resting outline in 2.5.4 had a side effect: hover re-coloured that same outline from `gray-500` to
  `primary-600`, two near-isoluminant tones, so the pointer feedback measured **1.07:1 in light and
  1.01:1 in dark** — 1.00 in greyscale, i.e. nothing for a reader who does not separate those hues.
  And it could not be fixed by picking a better outline colour: the outline has to stay above 3:1
  against the surface in BOTH states, which leaves no pair of outline colours that also differ enough
  from each other. So the outline **grows from 1.5px to 2.5px** on hover. The ring it gains replaces
  box fill with the outline colour — **5.17:1 light / 5.75:1 dark** against what it covered, over the
  3:1 that SC 1.4.11 asks of the information identifying a component's state. No new colour, no
  layout shift (`box-sizing: border-box` with a fixed 1rem box), and it survives greyscale and
  colour-vision deficiency.
  The new `--tm-control-hover-fill` adds a tint on top; that is cosmetic, not the affordance.
  `border-width` joined both controls' `transition`, and `TmRadio` also gained the `background-color`
  it was missing — without them the ring would snap while the tint faded, and the radio would have
  flickered where the checkbox faded.
  Both declarations are scoped to `:not(:checked)` so a selected control keeps its fill, its glyph
  and its ring. The **mixed** checkbox needed separate handling, because it is not `checked` at all —
  `Indeterminate` renders `checked="false"` and marks the box with a class — so that guard does not
  reach it and the pale tint would have covered the mixed fill: the dash would have dropped from
  5.17:1 to 1.22:1 in light and 7.02:1 to 1.72:1 in dark, leaving a hovered mixed box looking exactly
  like a hovered empty one. A rule next to the mixed fill takes the fill back on hover. It does not
  reset the thicker ring, and that is deliberate: on a filled box the outline and the fill are the
  same token, so the ring paints primary over primary at 1.00:1 — the growing ring is the affordance
  of the UNCHECKED state, which is also why the checked box is excluded from it. `TmRadio` has no
  third state and needs nothing extra.
  A ring is used rather than an inset `box-shadow` on purpose: `:focus-visible` already owns
  `box-shadow` and shadows do not accumulate across rules, so hover+focus together would have
  silently dropped one of them.
- **`TmMultiSelect` confirm button.** Its label was a hardcoded `white`, bypassing the token graph
  entirely, and its hover repainted `primary-600` — which IS `--tm-color-primary` in light, so the
  button had **no hover feedback at all** in the light theme, and in dark it moved to a darker shade,
  the wrong way. It now takes `--tm-color-on-primary` and `--tm-color-primary-hover`: 5.17:1 → 6.70:1
  in light, 7.02:1 → 9.90:1 in dark.
- **`TmValidationSummary` no longer prints the same message twice.** `GetValidationMessages()` walks
  the store per FIELD, so one sentence attached to several fields — a cross-field rule, or a server
  response merged into the live store — was listed once per field. The summary is a per-FORM list;
  the messages are de-duplicated with first-seen order preserved, so reading and screen-reader order
  are unchanged.
- **New guard `DesignTokenDocumentationTests`** compares the token table published in
  `JsonDocumentation/gettingStarted.json` against `tokens.css`. Nothing had ever compared them and
  four values had drifted: the documentation still described `--tm-color-primary` as `primary-500`
  (it is `-600`), the hover/active aliases one step too light, and the old system font stack. A
  consumer following the documentation would have computed contrast against the wrong shade.
- `SelectionControlContrastTests` now matches selectors EXACTLY instead of by fragment. The previous
  matcher took the first rule sharing a fragment, so two of its measurements were reading the right
  declaration only by rule ordering.

## 2.5.5 - 2026-07-22

### TmCodeEditor — leaving the editor with Tab, and wrapping long lines

The editor swallowed every Tab and never wrapped. That is right for source code, but wrong for an
editor placed among ordinary form fields (a Gherkin step, a note): keyboard users could not reach
the next control, and a long sentence scrolled sideways instead of wrapping. Two opt-in parameters,
both defaulting to today's behaviour, so upgrading changes nothing until you ask for it.

- **New parameter `TrapTab` (default `true`)** — keeps Tab/Shift+Tab inside the editor as
  indent/outdent. It stays `true` on purpose: existing hosts use the component as a source editor,
  and flipping the default would silently change their keyboard behaviour on upgrade. Set it to
  `false` and Tab moves focus to the next control (no `preventDefault`), at the cost of losing
  keyboard indentation. The flag reaches the script through `tmCodeEditor.init` and can be changed
  later through the new `tmCodeEditor.setTrapTab`; a caller that omits it keeps trapping Tab.
- **New parameter `Wrap` (default `false`)** — wraps long lines instead of scrolling horizontally.
  It adds the `tm-code-editor--wrap` modifier, which switches the textarea AND the highlight
  overlay to `white-space: pre-wrap` together (wrapping only one of them would drift the highlight
  against the caret), breaks over-long tokens and drops the now-pointless horizontal scroll.
- The wireframe stencil exposes `wrap` as a component prop, so a wireframe can state whether an
  editor wraps prose or scrolls code sideways.

## 2.5.4 - 2026-07-22

### Selection controls — non-text contrast (WCAG 2.2 SC 1.4.11) in both themes

**The appearance of three components changes**: `TmCheckbox`, `TmRadio`/`TmRadioGroup` and the
`TmMultiSelect` option checkbox. A selection control carries its whole state in two graphical
objects — the outline when nothing is selected, the glyph on the filled box when something is —
and both were below the 3:1 minimum. Measured from the token graph, before → after. The outline
numbers are identical for all three — they shared the same declaration. The glyph rows apply to the
checkbox and the multiselect option; a radio marks its state with a filled dot, not a glyph, and
that dot is unchanged (5.17:1 light / 5.75:1 dark against the box):

| | before | after |
|---|---|---|
| outline vs. its own surface (dark) | 1.41:1 | **5.71:1** |
| outline vs. its own surface (light) | 1.24:1 | **4.83:1** |
| outline on `--tm-bg-muted` (dark / light) | 1.00:1 / 1.13:1 | **4.04:1 / 4.39:1** |
| glyph on the filled box (dark) | 2.54:1 | **7.02:1** |
| glyph on the filled box (light) | 5.17:1 | 5.17:1 (unchanged) |
| indeterminate dash (light / dark) | 1.00:1 / 14.63:1 on an unfilled box | **5.17:1 / 7.02:1** |

- **New token `--tm-border-color-control`** (`--tm-color-gray-500` light, `--tm-color-gray-400`
  dark) — the boundary of a control whose STATE that boundary conveys. `--tm-border-color` keeps
  its meaning as the decorative divider tone and is unchanged, so nothing outside these three
  controls moves. Both use sites carry the fallback `var(--tm-border-color-control,
  var(--tm-border-color))`, so a stale token file cannot turn the whole `border` shorthand invalid
  and leave the control with no outline at all.
- **New token `--tm-control-glyph-color`** (white light, `--tm-text-inverse` dark) — the glyph on a
  filled selection control. On dark the fill is the lighter `primary-400`, on which white is
  2.54:1; dark ink on it is 7.02:1, the rule the filled primary button and the filled badge already
  follow. Flipping it in `tokens-dark.css` covers both theming APIs (`[data-theme="dark"]` and
  `.tm-dark`) and all three components at once. The multiselect option checkbox previously
  hardcoded `color: white`, which bypassed the token graph entirely.
- **`TmCheckbox` indeterminate state fixed.** The mixed state was styled through
  `.tm-checkbox-input:indeterminate`, a pseudo-class that only matches when something sets the
  input's `indeterminate` DOM property — nothing ever did. The box therefore stayed unfilled and
  the white dash was invisible in the light theme (1.00:1). The fill is now keyed off a class the
  component renders, and the input carries `aria-checked="mixed"`, without which the state was
  indistinguishable from unchecked for assistive technology.
- Guarded by `SelectionControlContrastTests`, which resolves the `var()` chains out of the token
  files and recomputes the ratios for all three controls, so reintroducing a failing colour further
  up the graph fails the build too.
- **Known gap, not covered here**: these ratios are Tempo's own tokens. An application that
  repoints the primary scale (per-accent themes) has to re-measure the glyph against ITS fills —
  the dark glyph token is what makes that possible, but no test in this repo can see those tokens.

## 2.5.1 - 2026-07-19

### Applier convergence follow-up: `table.cell.text` + content-control children

- **`setBlockAttribute table.cell.text` now converges across runtimes** — the JS
  collaboration applier (`transform.mjs`) implements the cell-targeting
  semantics (resolve the TABLE block with a fallback without the cell
  preference, replace the first cell paragraph's runs, convert a non-paragraph
  first block, create a paragraph in an empty cell). Both runtimes create the
  empty-cell paragraph with the DETERMINISTIC id `{cellId}-text` — the previous
  random C# Guid was itself a cross-replica divergence. Pinned by the
  convergence fixture (replace + empty-cell create) and unit tests on both
  sides; `document_editor_set_table_cell_text` edits now co-edit live.
- **Blocks inside content controls are operation-addressable** — both appliers
  descend `ContentControlBlockContent.Blocks` like table cells (keeping the
  enclosing cell context for the `TableCellId` preference), so agents can
  fine-edit conditional chains and repeating sections
  (insert/replace/delete_text, format_range, set_heading, insert_token,
  delete/move/update_block) without whole-control replaces. The JS `moveBlock`
  was aligned: without an explicit cell id a nested block stays in its SOURCE
  container (previously it was re-homed to the body).
  `document_editor_describe_document` now reports
  `operationAddressable: true` for content-control children; only
  header/footer blocks remain non-addressable. Convergence fixture extended
  with in-control text/mark edits; addressing, operation-semantics, coverage
  and tool-catalog docs updated.

## 2.5.0 - 2026-07-19

### Document MCP tools — semantic editing compiled to operations + visual previews

`Tempo.Blazor.Mcp` now ships a complete agent-facing document tool suite
(~19 new tools next to the existing low-level ones; catalog with the addressing
and concurrency contract in `docs/document-mcp-tools.md`, guarded by
`DocumentMcpToolsDocumentationDriftTests`):

- **Introspection** — `document_editor_describe_document` (blocks with stable
  semantic addresses per `docs/document-mcp-addressing.md`, truncated text,
  tables with cell ids, tokens, content controls, headers/footers,
  `contentDigest` SHA-256 fingerprint) and `document_template_describe`
  (tokens, conditional chains, repeating sections).
- **Semantic text edits** — `insert_text`/`replace_text`/`delete_text`/
  `format_range` (17 marks)/`set_heading`/`set_paragraph_properties` with
  plain-text offset/length addressing, compiled into canonical
  insertText/deleteText/mark/setBlockAttribute operations and applied through
  the same convergence-tested pipeline as `document_editor_apply_operations`.
- **Blocks & tables** — `insert_block` (body order-value / cell index),
  `delete_block`, `move_block`, `update_block`, `set_table_cell_text`.
- **Authoring** — `document_editor_create`, `document_editor_import`
  (markdown/HTML text, DOCX/ODT base64; content replacement under the
  concurrency token) and `document_editor_export` (markdown/HTML verification
  channel, DOCX/ODT packages). `Tempo.Blazor.DocumentFormats` now multi-targets
  net8.0/net9.0/net10.0 (additive) so the MCP package can reference it.
- **Templates & assembly** — `insert_token` (incl. computed expressions and
  optional token-provider key validation), `wrap_conditional` (IF/ELSEIF/ELSE
  content-control chains + branch/expression updates),
  `insert_repeating_section` and `document_assemble_render` (token values with
  rows → PNG/PDF with IF/ELSE evaluation, repeat expansion and computed
  expressions). The JS collaboration applier now normalizes content-control
  persistence payloads (previously degraded to paragraphs) and the C#↔JS
  convergence fixture covers conditional/repeat authoring operations incl.
  `tmAssembly` metadata.
- **Visual feedback** — `document_render_preview` (per-page base64 PNGs, page
  selection, dpi, caps) and `document_render_pdf` (DocumentPdfExportOptions
  passthrough incl. forensic watermark) over the headless runtime with a
  configurable font catalog (`AddTempoDocumentEditorMcpRendering`: explicit
  faces, aliases, system Arial/DejaVu fallback, ImageResolver seam) — fail
  closed with agent-friendly font diagnostics.
- **Diff & redline** — `document_editor_diff_versions` (structured diff with
  word-level segments) and `document_editor_export_redline` (DOCX with real
  `w:ins`/`w:del` tracked changes or PDF with review markup).
- **Live co-editing bridge (opt-in)** —
  `AddTempoDocumentEditorMcpCollaboration`: semantic writes publish their
  operation batches to the host collaboration stream (named agent participant
  with presence color, backplane envelopes with `SourceInstanceId`); fail-open.
  The demo Api forwards publishes to the SignalR document groups, so open
  TmDocumentEditor sessions see agent edits live
  (`scripts/e2e-document-mcp-live-coedit.mjs`).
- Demo Api: `/mcp` endpoint now really serves the document tools
  (`IDocumentEditorProvider` registration), `/api/document-editor/mcp-agent-demo`
  runs a one-shot agent orchestration (create → edits → preview), and the
  contract-demo seed page breaks carry proper `PageBreakBlockContent`.

## 2.4.0 - 2026-07-19

### Headless document runtime — Phase 0: embedded headless layout bundle

- `Tempo.Blazor.DocumentFormats` now embeds the canvas editor's layout chain
  (`buildLayoutSnapshotExport` → `buildDisplayList` → `layoutCanvasDocument` →
  `translateDisplayListToLayoutSnapshot`, incl. line breaker, paragraph engine and the
  injectable font-metrics service) as a single ESM artifact
  (`HeadlessLayout/tempo-document-headless-layout.bundle.mjs`), accessible via the new
  `TempoDocumentHeadlessLayoutBundle` class. Server-side layout hosts evaluate this script to
  produce the exact layout snapshot the browser editor exports — WYSIWYG parity by construction.
- New build tooling: `npm run build:document-editor` builds the bundle
  (`scripts/build-document-editor.mjs`); `--check` is a drift gate wired into the
  `Tempo.Blazor.DocumentFormats` MSBuild pre-build and the Node test lane, so a stale embedded
  artifact fails the build. Node guard tests keep browser globals (`document`/`window`/
  `OffscreenCanvas`) out of the bundle outside the font-metrics safe fallback.

### Headless document runtime — Phase 1: font-accurate metrics from SkiaSharp

- New `TempoFontAdvanceTableExtractor` (+ `TempoFontAdvanceFace`) in
  `Tempo.Blazor.DocumentFormats.HeadlessLayout`: reads glyph advance widths and vertical metrics
  from the SAME `ReportPdfFontFace` bytes the PDF renderer embeds (SKTypeface/SKFont, unhinted
  linear metrics in font units) and serializes them into a compact JSON table for the JS side;
  thread-safe lazy cache per font face, Latin + Czech/Central European coverage by default.
- New JS module `document-editor/layout/font-advance-metrics.mjs`
  (`createAdvanceFontMetricsService`, `parseFontAdvanceTable`, `createFontAdvanceMeasureContext`,
  all exported from the headless bundle): measures text by summing advances (+ letter spacing,
  character scale, zoom) through the production font-metrics service, with face resolution
  mirroring the PDF renderer's font catalog and synthetic fallback + diagnostics for unknown
  families/glyphs. Plugs into pagination's `ensureMeasurementService` seam as a full service or a
  `{ measureRun }` partial.
- JS↔C# parity is pinned by a committed fixture
  (`font-advance-parity-fixture.json`, regenerable via `TEMPO_REGENERATE_FONT_PARITY_FIXTURE=1`):
  the Node lane replays Czech-diacritics and letter-spacing samples through the real JS measurer
  and asserts bit-identical widths; per-glyph advances equal `SKFont.MeasureText` exactly.

### Headless document runtime — Phase 2: `ITempoDocumentLayoutService` + Jint host

- New `ITempoDocumentLayoutService` in `Tempo.Blazor.DocumentFormats.HeadlessLayout`:
  `GenerateLayoutSnapshotJson(document, pageSetup?, fonts, reviewDisplayMode)` lays out a
  `DocumentEditorDocument` server-side with the exact canvas layout chain and returns the schema
  v1 snapshot JSON (`DocumentPdfExportRequest.LayoutSnapshotJson` contract). Register with
  `services.AddTempoDocumentLayout()`.
- `JintDocumentLayoutEngine` hosts the embedded bundle in pooled Jint engines (thread-safe,
  bounded by concurrency — no engine allocation per call; `CreatedEngineCount` diagnostics).
  Data-in/data-out JSON seam (`generateHeadlessLayoutSnapshotJson` in the bundle, also exported
  for Node consumers) — no .NET↔JS callbacks per glyph. Fail-closed with diagnostics: missing
  fonts, unknown font families and unmeasurable glyphs throw `TempoDocumentLayoutException`
  (`UnknownFontFamilies`, `MissingGlyphs`) instead of silently degrading to synthetic metrics.
  Page-setup override (size/orientation/margins in points) applies document-wide incl. sections;
  redaction-marked runs are destroyed in the snapshot; redline modes supported.
- Measured 2026-07-19 (Jint 4.13, .NET 10, Debug): ≈ 2.2 s cold (engine + bundle evaluation) and
  ≈ 0.9 s warm for a 54-page document (≈ 17 ms/page); 369-page stress run 5.4 s cold / 3.8 s
  warm. Perf gate budgets: 15 s cold / 6 s warm at 21+ pages.

### Headless document runtime — Phase 3: headless ↔ browser export parity

- Committed 21-page parity pair (`headless-parity-document.json` + request + snapshot fixtures,
  regenerable via `TEMPO_REGENERATE_HEADLESS_PARITY_FIXTURE=1`): the Jint-hosted layout of the
  committed Czech contract document matches the browser-generated
  `layout-snapshot-parity-fixture.json` in page count (21) and page geometry (< 1 pt — the only
  difference is the canvas engine's rounded 794×1123 px A4 default vs the exact
  595.276 pt × 96⁄72), reproduces the committed headless snapshot byte-for-byte, and replaying
  the identical request through the bundle in Node (V8) yields a DEEPLY EQUAL snapshot — layout
  does not depend on the hosting JS engine. Headless snapshot → `TempoDocumentPdfRenderer` PDF
  keeps page count and block positions within 1 pt.
- `Demo.Api` `DemoDocumentPdfExportProvider`: the legacy text-only PDF stub is deleted. Every
  export flows through the production WYSIWYG renderer — snapshot-less requests (GET exports,
  headless API clients) are laid out server-side via `ITempoDocumentLayoutService` with the new
  `DemoDocumentExportFontCatalog` (system Arial/DejaVu faces aliased as `Aptos` for the demo
  theme), the same faces the PDF embeds.
- `JintDocumentLayoutEngine.BuildRequestJson` is now public — the exact JS-seam payload, used by
  the cross-runtime parity test and available for diagnostics.
- New E2E (`DocumentEditorHeadlessExportParityE2ETests`): the same document exported through the
  browser path (live canvas snapshot) and the server path (headless GET export) agrees on
  pagination and text layer; both PDFs open in TmPdfViewer (screenshots); empty-document server
  export yields a valid single-page PDF.

### Headless document runtime — Phase 4: `TempoDocumentService` facade + PNG page previews

- New `ITempoDocumentService`/`TempoDocumentService` facade in
  `Tempo.Blazor.DocumentFormats.HeadlessLayout`: `RenderPdfAsync(template/document +
  tokenValues, options)` = `DocumentAssemblyService.Assemble` (IF/ELSE chains, repeating
  sections, computed expressions) → headless layout → `TempoDocumentPdfRenderer`, with
  watermark + forensic watermark passthrough and an injectable `TimeProvider` clock
  (deterministic `TODAY()`/`DATEADD` and forensic timestamps). Returns
  `TempoDocumentPdfResult` (PDF, page count, layout snapshot JSON, stamped forensic time).
- `RenderPageImagesAsync` rasters every laid-out page to PNG at a parametrizable DPI
  (`TempoDocumentPageImage`); `ReportPdfRenderer` gained an additive
  `RenderPagePng(page, options, scale)` overload. Register everything with
  `services.AddTempoDocumentServices()`.
- Demo.Api: new `POST /api/document-editor/assembly/render` — the demo assembly contract
  template + a dataset (scalar values + repeating item rows) → PDF or per-page PNG previews,
  rendered purely server-side. E2E proves two datasets flip the IF/ELSE branch and compute
  their totals, with PNG preview screenshots for UX review; COMPONENTS.md gained a
  "Headless dokumentový runtime" section.

### Headless document runtime — follow-ups

- Server-side image resolution: `TempoDocumentRenderRequest.ImageResolver`
  (`TempoDocumentImageSourceResolver` + `TempoDocumentImageReference`) resolves asset-backed and
  URL-based image sources to embeddable data URIs before layout — headless exports embed real
  images instead of placeholder rects. `DemoDocumentPdfExportProvider` resolves demo-store
  assets this way (`/XObject` embedded in headless PDFs); host-relative URLs remain
  unresolvable server-side by design.
- Applier convergence completed: body-level `insertBlock`/`moveBlock` now share ORDER-VALUE
  semantics with deterministic tie-breaks on both runtimes (JS previously spliced by index —
  fractional/large C# order values landed wrong), the JS applier normalizes persistence-shaped
  block payloads (`content.$type`/`inlines`) into canvas blocks on apply, and nested moves stay
  in their source cell. The convergence fixture now covers insert/update/move incl. cell
  containers and persistence payloads.
- Skia-derived parity fixtures are platform-aware: byte-exact on Windows (the generator
  platform), structural with tight tolerances elsewhere — Skia's scaler backend (FreeType vs
  DirectWrite) differs by ~1e-5 font units per advance, which broke byte comparisons on Linux
  CI.

### Headless document runtime — Phase 5: server-side operation applier

- Coverage audit of the canonical operation model across the C# applier
  (`DocumentOperationApplier`), the JS collaboration applier (`transform.mjs`) and the conflict
  resolver — table + findings in `docs/document-operation-applier-coverage.md`.
- `DocumentOperationApplier` now resolves operation targets inside table cells exactly like the
  JS applier (deep search with `TableCellId` as the container preference): text, mark, block,
  attribute and update operations work on nested blocks; block insert/move inside cells is
  index-based like JS; `table.cell.text` keeps its historical table-targeting semantics.
- Fixed the JS collab applier to split runs at mark-range boundaries (previously a
  partial-range mark bolded whole runs) — mirrors the C# and engine semantics.
- New cross-runtime convergence property tests: seeded operation batches applied by the C#
  applier produce a committed content signature
  (`operation-convergence-fixture.json`, regenerable via
  `TEMPO_REGENERATE_OPERATION_CONVERGENCE_FIXTURE=1`) that the JS applier reproduces deeply
  equal in the Node lane. Known divergences (body-level `moveBlock` order-vs-index semantics,
  `insertBlock`/`updateBlock` payload shapes) are documented and carried forward to the MCP
  tooling plan.

## 2.3.9 - 2026-07-19

### Document editor — canvas command layer completed (TmDocumentEditor)

Every command id routed from the C# UI into the canvas engine is now actually handled — the
C#↔engine command contract test runs with no allowlist. Fixed silent no-op toolbar/ribbon actions:

- **Fullscreen, header/footer toggles, insert table, delete table/page-break, table & cell
  properties mutations, protection** (plan phases 1–8): registered as real engine commands with
  full undo/redo semantics; document protection is enforced by the engine (restricted regions veto
  inline/paragraph edits).
- **Insert-ribbon token menu** (phase 9): the token button opened nothing (it routed an
  unregistered `openTokenMenu` command). It now opens a Blazor token panel (searchable, provider
  driven); picking a token inserts a first-class token run at the caret through the new
  `insertToken` engine command — rendered as its display name, exposed to assistive tech through
  the accessibility mirror, undoable as one transaction and persistent across save/reload.
- **Table properties, cell properties, replace image, set image link** (phase 10): these ribbon/
  command-palette entries routed engine commands that never existed. They now open the Properties
  side panel (the panel issues the real `setTableProperties`/`setCellProperties`/`setImageUrl`
  mutations), mirroring the table context menu. The command palette additionally syncs the live
  canvas table/image selection before computing command availability.
- **Engine fix:** commands that insert runs now follow the copy-on-write layout contract all the
  way up (new model object, cloned block, section block-list swap) — previously an inserted token
  updated the model but the canvas never repainted until reload.

## 2.3.8 - 2026-07-17

### Fixes

- Increased the dense-line marker spacing threshold from ~9 to ~24 SVG units: at ~9 units the thinned markers still touched each other (12-unit visual diameter), keeping the beaded look on very dense series (e.g. 360 monthly values). Markers now sit clearly ON the line with visible line segments between them.

## 2.3.7 - 2026-07-17

### Dense line series readability (TmChart)

- Line series (both `Line` charts and combo overlays on `Bar` charts) now thin their point markers when values are packed tighter than ~9 SVG units apart — overlapping white-stroked circles previously made a dense line (e.g. 360 monthly values) look dotted. The polyline itself always renders complete; sparse series keep a marker on every value.

## 2.3.6 - 2026-07-17

### Negative values on Bar and Line charts (TmChart)

- `Bar` charts (including combo overlays) and `Line` charts now support negative values through a signed value domain: when any visible value is negative, the Y axis extends below zero with an emphasized zero axis (parity with Area charts), bars grow downward from the zero baseline, and line/overlay points plot below the axis. Charts with only non-negative values render exactly as before (0-based scale). Previously a negative value produced an invalid negative-height bar or a point outside the plot area.

## 2.3.5 - 2026-07-17

### Combo charts (TmChart)

- Added `ChartDataset.RenderAs` (`ChartDatasetRenderAs.Default | Bar | Line`): on a `ChartType.Bar` chart, datasets marked `Line` render as a line overlay (polyline + clickable points) over the bars, centered on each category and sharing the bars' Y scale — bars for periodic flows, lines for cumulative values in one plot. Default keeps the chart's own type, so existing charts are unaffected; on non-Bar charts the override is ignored.
- Bar charts with more than 24 categories now thin their X-axis labels (every n-th label, at most ~12) so dense categorical axes stay readable; charts with up to 24 categories keep every label.

### Fixes

- Fixed `TmLightbox` stacking: the root `.tm-lightbox` used a hardcoded `z-index: 1000`, which painted the close/prev/next buttons underneath sticky chrome such as `TmTopBar` (`--tm-z-sticky` 1020). It now uses the overlay tier (`var(--tm-z-overlay, 1060)`), consistent with `.tm-lightbox-overlay`.

## 2.3.4 - 2026-07-17

### Accent-insensitive filtering (TmFilterableDropdown / TmMultiColumnComboBox)

- Client-side filtering in `TmFilterableDropdown` and `TmMultiColumnComboBox` is now accent-insensitive by default: both the filter term and the item text are normalized to Unicode FormD and combining diacritical marks are stripped before the contains comparison, so e.g. "usti" matches "Ústí nad Labem" and "práha" matches "Praha".
- Added an `AccentInsensitiveFilter` parameter (default `true`) to both components to opt back into accent-sensitive (but still case-insensitive) filtering.
- The change is match-superset only: every item that matched before still matches; accent-mismatched items are newly included. Server-side `DataProvider` filtering is unaffected (the provider owns its own matching).

## 2.3.0-preview.1 - Unreleased

- Added `ButtonVariant.OutlineSecondary`, `ButtonVariant.Warning`, and `ButtonVariant.OutlineWarning` to `TmButton`.
- Added `RowAttributes` to `TmDataTable` for applying row-level HTML attributes consistently across non-virtualized, virtualized, and grouped data rows.
- `DocumentEditorSnapshotCommand` restores the historical defensive-clone contract by default; added an opt-in `assumeOwnership` constructor parameter (default `false`) for callers that hand over dedicated snapshots and want to skip the two O(document) copies. Existing external code compiles and behaves as in 2.0.x.
- `DocumentEditorCommandRegistry.Register` now invalidates the refresh signature gate, so commands registered after the first `RefreshAllAsync` receive their state on the next refresh even when the command context is unchanged.
- TmDocumentEditor toolbar wave: native selects openable by mouse (removed `preventDefault`), 31 missing built-in icons added to `TmIcon` (+ `IconNames` constants and `DocumentToolbarItem.Options`/`DocumentToolbarRenderContext.CommandState` for declarative renderers), ribbon CSS consolidated into `_document-editor-toolbar.css`, 21 additional commands registered in the command registry with a unified enabled/visibility fallback.
- TmDocumentEditor ribbon overflow is now live: a new `toolbar-overflow.mjs` measurement controller (ResizeObserver + scroll + tab-switch mutations) reports off-screen `[data-command]` items through the existing `SetOverflowingAsync` contract, so the More menu finally appears on narrow windows; the toolbar loads the module itself — no host-app setup needed. Fixed the overflow search box opening pre-filled with a literal `_overflowSearchQuery`.
- `DocumentToolbarButtonRenderer` now honors `DocumentToolbarRenderContext.Execute` (click) and `CommandState.IsEnabled` (disabled), matching the toggle/select/color renderers; added the `/document-toolbar-renderers` demo page showcasing the declarative toolbar extension API.
- Added `TmNavigationGuard`, an unsaved-work navigation guard that gates internal router navigation with a `TmDialog` confirmation and arms a browser `beforeunload` prompt for tab close/refresh. Exposes `Suppress()` for post-commit programmatic navigation.
- Added `TmFormActionBar`, a sticky/floating action bar for long forms with `Status`/`PrimaryActions`/`SecondaryActions`/`DangerActions` slots, `Static`/`StickyTop`/`FloatingBottom` positions (`FormActionBarPosition`), and a functional `ShowOnScroll` reveal (real passive scroll listener, not a no-op hook).
- Added `TmScrollSpyNav`, a sectional in-page navigation component with an optional passive-scroll spy (`EnableScrollSpy`), `SideRail`/`Breadcrumb` variants (`ScrollSpyNavVariant`), a minimal generic `ScrollSpyNavItem` record, and an `ItemTemplate` slot for host-supplied enrichment. Active items expose both `aria-current="true"` and `data-active`.
- Added `TmUserPicker<TUser>`, a generic entity/user picker with debounced cancellable search, pointer-down selection, keyboard navigation, and explicit three-state (`TmPickerFetchState.Ok`/`Empty`/`Transient`) fetch rendering so a real search/resolve failure is never shown as a silent "no results". Plain async `SearchProvider`/`ResolveProvider` callers; no built-in retry loop.
- Added a typed `Required` parameter to `TmSelect`, `TmCheckbox`, and `TmRadioGroup`, matching the existing `TmTextInput`/`TmMultiSelect` pattern. When set, it renders the required marker (`tm-input-label-required` label class → asterisk) and sets `aria-required="true"` on the actual control — the `<select>`, the checkbox `<input>`, and the `role="radiogroup"` element respectively — instead of the wrapper, so it is exposed to assistive tech. `AdditionalAttributes` splat behavior is unchanged. Also advertised `required` in the built-in wireframe schemas for Checkbox and Radio Group.
- Extended the visible required marker (`tm-input-label-required` label asterisk) and `aria-required` to the remaining label-owning inputs so all label-bearing field types are consistent: `TmTextInput` and `TmDecimalInput` now add the marker class to their label (both already set `aria-required` on the control); `TmTextArea` and `TmDecimalInput` gained a typed `Required` parameter that drives the marker plus native `required`/`aria-required` on the `<textarea>`/`<input>`; and `TmDatePicker`/`TmDateTimePicker`'s previously-declared-but-unused `Required` is now wired to the marker and `aria-required` on the trigger button. `AdditionalAttributes` splat behavior is unchanged.

## 2.2.0 - 2026-07-06

### Data component chrome and filtering (TmDataTable / TmMultiViewList)

- Added `ShowToolbar` parameter to `TmDataTable` and `TmMultiViewList` to explicitly suppress the built-in toolbar.
- Added `ShowViewManager` parameter to `TmDataTable` and `TmMultiViewList` to control rendering of the saved-views picker independently.
- Added `SearchText` / `SearchTextChanged` two-way binding so the surrounding page can own the search state.
- Added `ToolbarMode` (`DataToolbarMode.Full`, `SearchOnly`, `ActionsOnly`, `ContentOnly`) as a higher-level API for common toolbar presets.
  - `Full` keeps the existing behavior (respects individual `Show*` flags).
  - `SearchOnly` renders only the global search input.
  - `ActionsOnly` renders only chrome actions (column picker / view switcher / view manager).
  - `ContentOnly` hides all toolbar chrome and the external filter builder, leaving a clean data surface for page-owned filtering.
- `ToolbarMode=ContentOnly` and `ShowExternalFilterBuilder=false` prevent duplicate filtering UI when the owning page already provides filters or saved views.
- Empty toolbars are no longer rendered when no control would be visible.

### Migration notes

- Existing code continues to compile and run unchanged; all new parameters have backward-compatible defaults.
- If your page already has its own filter toolbar, switch the data component to `ToolbarMode="DataToolbarMode.ContentOnly"` and bind `Items` to your pre-filtered collection.
- If you want saved views without the inline filter builder, keep `ToolbarMode="DataToolbarMode.Full"` and set `ShowExternalFilterBuilder="false"`.
- See `docs/data-component-chrome-migration.md` for a full migration guide and PromptHelper-specific replacement instructions.

## 2.1.0 - 2026-07-04

- Added the UI role vocabulary model for wireframe authoring, including built-in role synonyms and app-scoped role resolution.
- Added role-aware wireframe authoring through MCP operations and `wireframe_author_document`, with advisory warnings for role gaps, ambiguous matches, enum normalization, off-canvas placement, text overflow, required content, and layout issues.
- Added compact/filterable `wireframe_get_authoring_guide` output with category, type, role, target pack, app scope, skip, and take filters.
- Added container-aware wireframe linting through `isContainer`, so expected containment does not appear as sibling overlap.
- Added `WireframeThumbnailRenderer` in `Tempo.Blazor.Wireframe` and moved the Demo API document-library preview generation onto the package renderer.
- Updated the wireframe document schema and JSON documentation for document version 2.1, element roles, component roles, container metadata, MCP authoring, and thumbnails.
- Bumped published package metadata to `2.1.0` and aligned release workflows to derive manual/CI versions from the core package version.

Release follow-up after review: commit the prepared changes, merge to main, tag `v2.1.0`, push, then verify NuGet.org publication with a clean-project package-install smoke.
