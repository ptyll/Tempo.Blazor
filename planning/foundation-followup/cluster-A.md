# Cluster A — tests, tooling, CI, E2E infrastructure (follow-ups of plan faf503eb)

Analysis on `main @ 846bc242`, working tree clean apart from untracked scratch (`diagrams.db*`, `output/`, `tmp/`).
READ-ONLY: nothing in the repository was changed. Commands that were run are listed with each item.

Evidence files produced by this analysis (under `tmp/followup-analysis/`):
- `clusterA-env.log` + `trx-A/clusterA-env.trx`: the "environment" unit-test run.
- `clusterA-ia.log` + `trx-A-ia/clusterA-ia.trx`: the InteractiveAuto E2E re-run.

> **Important for the plan writer.** Two of the items share one root cause: **CRLF working copies**.
> `git config --show-origin --get-all core.autocrlf` shows `system=true` (`C:/Program Files/Git/etc/gitconfig`) and
> `local=false` (`.git/config`). The clone was checked out while `autocrlf=true` applied, and `false` was set later.
> As a result **7 839 tracked files are `i/lf w/crlf`** (`git ls-files --eol`). `git status` still says clean only
> because of the stat cache: `git hash-object .github/workflows/publish-nuget-org.yml` = `82a60cca…` but the index blob
> is `971ed5db…`. Every later `git add` of such a file stores CRLF. That is how `98cc1c99` and the commit before
> `975a75be` got CRLF into history. The repo has **no global `text`/`eol` rule**. `.gitattributes` covers only three
> generated artefacts: `*.bundle.mjs`, `tempo-blazor.bundled.css` and `tempo-blazor*.json`.

---

## A1 — [9e46d2da] + [17b6d82d] the "environment" unit failures

### (1) Status
**OPEN (10 failing). 2 of the originally listed 12 are ALREADY DONE.**

Command (net10.0, ~15 s):
```
dotnet test tests/Tempo.Blazor.Tests -f net10.0 --filter "FullyQualifiedName~FullCloneWorkflow|FullyQualifiedName~ReleaseGateFilter|FullyQualifiedName~PublishEnvironmentGate|FullyQualifiedName~ReleaseEvidence|FullyQualifiedName~ReleaseContract|FullyQualifiedName~TmTabsHeaderSlots"
```
Result: **10 failed, 41 passed, 9 skipped, 60 total.**

| Test | Outcome | Cause |
|---|---|---|
| FullCloneWorkflowTests.TheCheckoutGuard_DetectsItsLoss_InEitherJob | FAIL: `ArgumentOutOfRangeException startIndex -1` at FullCloneWorkflowTests.cs:102 | CRLF workflow |
| FullCloneWorkflowTests.BothPublishWorkflows_RequireFullCloneInTheTestJob | FAIL: `Sequence contains no matching element` at :145 | CRLF workflow |
| FullCloneWorkflowTests.BothPublishWorkflowsCheckoutFullHistory | FAIL: population 0 | CRLF workflow |
| ReleaseGateFilterTests.TheNonEnglishLaneSetsItsLocaleAfterTheBuild_NotAroundIt | FAIL: jobsWithABuild 0 | CRLF workflow |
| ReleaseGateFilterTests.TheOrderingGuard_DetectsALocaleThatWrapsTheReleaseBuild | FAIL: "Expected jobLevelEnvOnPublish not to be …" (the `"  publish:\n"` mutation never matched) | CRLF workflow |
| ReleaseGateFilterTests.BothPublishWorkflows_SetTheBunitWaitBudgetOnEveryJobThatRunsTests | FAIL: population 0 | CRLF workflow |
| ReleaseGateFilterTests.EveryPublishTestStep_SetsTempoRequireFeed | FAIL: testSteps 0 | CRLF workflow |
| PublishEnvironmentGateTests.TheEnvironmentGuard_DetectsItsLoss_AndOnlyThePushJobCounts | FAIL: "Expected deleted not to be …" | CRLF workflow |
| ReleaseEvidenceTests.ReleaseEvidenceVerifier_RunsInsideThePublishJob | FAIL: no `publish` job | CRLF workflow |
| TmTabsHeaderSlotsTests.NoSlotMarkupIsByteIdenticalToTheFrozenPreSlotBaseline | FAIL: 1271 bytes vs 1269 | CRLF `TmTabs.razor` |
| ReleaseContractTests.AnnouncedVersion_OnTheFeed_CarriesWhatThisTreeBuilds | **PASS now** | see below |
| ReleaseContractTests.AnnouncedVersion_IsEitherUntagged_OrItsTagNamesTheCommitBeingPacked | **PASS now** | see below |

**ReleaseContract is ALREADY DONE.** Commit `7d7823ae` (2026-10-07, "chore(release): announce 2.9.1 (unreleased)") moved
the changelog off the spent `2.9.0` number, which the `v2.9.0` tag names on another commit. Both tests are green on
this tree. The feed test is `[FeedReachableFact]` and network-dependent by design, so it is not in scope.

### (2) Root cause, with proof

**(a) Nine workflow tests: the YAML is read with CRLF.**
- `.github/workflows/publish-nuget-org.yml` is `i/lf w/crlf`, with 350 CR characters. The content equals the index once
  `\r` is removed. `publish-nuget.yml` happens to be `w/lf` because it was rewritten by a later checkout (`d7466f31`).
- The reader in tests/Tempo.Blazor.Tests/Packaging/ReleaseGateFilterTests.cs:1583-1589 does not normalise line endings:
  ```csharp
  internal static string ReadRepoFile(string relativePath)
  {
      string root = FindRepoRoot();
      string path = Path.Combine(root, relativePath);
      File.Exists(path).Should().BeTrue($"release-gate workflow must exist at {relativePath}");
      return File.ReadAllText(path);
  }
  ```
- `JobSegments` (same file, :1117-1119) uses `^  (?<name>[A-Za-z0-9_.-]+):[ \t]*$` in Multiline mode. In .NET, `$`
  matches before `\n`, and `[ \t]*` does not consume `\r`, so a CRLF file yields **zero jobs**. Every population
  assertion then fails, and every mutation written as `"…\n"` (`"          fetch-depth: 0\n"`, `"  publish:\n"`,
  `"      TEMPO_BUNIT_WAIT_SECONDS: \"10\"\n"`) never matches.
- This is a **test robustness defect, not a CI defect**. GitHub reads the LF blob, and the CI runner checks out LF.

**(b) TmTabsHeaderSlots: the Razor source is CRLF, so the rendered markup is too.**
- `src/Tempo.Blazor/Components/Navigation/TmTabs.razor` is `i/lf w/crlf`. The Razor compiler copies literal
  whitespace text (including `\r\n`) into the render tree, so the markup carries **two extra `\r`** (1271 vs 1269 bytes).
- Proof (reproduced from the failure message): removing `\r` from the captured markup gives **exactly 1269 bytes and
  sha256 `9ed0c8d568723921558b342e9f54c02c47648ab880369de2b7e01a35b5c572cf`**, which is the frozen constant at
  TmTabsHeaderSlotsTests.cs:32-33.
- **The baseline is correct. Do NOT regenerate it.**

### (3) Exact change
Two layers are needed:
- **Step 1 (test robustness)** makes the result deterministic on any checkout.
- **Step 2 (repo policy)** stops further CRLF from entering history.

Step 1 alone fixes all 10 tests.

**Step 1a — ReleaseGateFilterTests.cs:1583-1589.** Replace the body:
```csharp
internal static string ReadRepoFile(string relativePath)
{
    string root = FindRepoRoot();
    string path = Path.Combine(root, relativePath);
    File.Exists(path).Should().BeTrue($"release-gate workflow must exist at {relativePath}");
    return NormalizeLineEndings(File.ReadAllText(path));
}

/// <summary>
/// The workflows, scripts and every needle in this suite are LF (the repository stores them LF and the
/// runner checks them out LF). A Windows working copy checked out under core.autocrlf=true is CRLF, and
/// the line-anchored readers (JobSegments, the env-block readers, the "\n"-terminated mutations) then
/// see no jobs at all — a red that says nothing about the workflow. Normalise once, at the reader.
/// </summary>
internal static string NormalizeLineEndings(string text) =>
    text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
```
All 9 workflow tests go through `ReadWorkflowCode`/`ReadRepoFile`, so this one change fixes them. `SqlServerImagePinTests`
also calls `ReadRepoFile` and is unaffected.

**Step 1b — TmTabsHeaderSlotsTests.cs:62-63.** Normalise EOL inside the documented normaliser. Replace:
```csharp
var rendered = RenderNoSlots().Markup;
var markup = ElementReferenceMarker.Replace(rendered, string.Empty);
```
with:
```csharp
var rendered = RenderNoSlots().Markup;
// The SECOND documented normalisation: the Razor compiler copies the source file's literal line
// endings into the markup, so a CRLF working copy (core.autocrlf=true) renders "\r\n" where the
// frozen baseline (measured on an LF tree) has "\n" — 2 bytes on this fixture. Line endings are
// a property of the checkout, not of the DOM contract this hash freezes.
var markup = ElementReferenceMarker.Replace(rendered, string.Empty)
    .Replace("\r\n", "\n", StringComparison.Ordinal);
```
Also extend the XML doc of `ElementReferenceMarker` (lines 35-53) with one sentence: "CRLF→LF is the only other
normalisation (see the test body)".

**Step 2 — repo EOL policy (see OPEN QUESTION Q1).** Recommended version, appended to `.gitattributes`. Keep the three
existing rules; they stay valid.
```gitattributes
# Every text file is LF in the index AND in every working copy, whatever core.autocrlf says:
# a CRLF working copy made the workflow-reading guards and the TmTabs byte baseline red, and
# `git add` of a CRLF copy stored CRLF in history (98cc1c99, the commit before 975a75be).
* text=auto eol=lf
# Release evidence is captured tool output and must stay byte-verbatim (13 files are i/mixed today).
eng/release-evidence/** -text
```
- Simulated with a temporary index (`git -c core.attributesFile=… add --renormalize .`): the **only** files whose
  index blob would change are the 13 `eng/release-evidence/**/*.log` files that are `i/mixed`. The `-text` line keeps
  them out.
- The index is LF everywhere else. **Renormalisation is therefore a no-op commit apart from `.gitattributes` itself.**

After committing Step 2, the CRLF **working copy** on this machine is still CRLF, because git does not rewrite
stat-clean files. One-time refresh of the working tree (no history change):
```powershell
# only when `git status --porcelain` is empty!
git rm -r -q --cached .
git reset -q --hard HEAD
```
(The plan must tell the implementer to run this only on a clean tree; it discards uncommitted changes.)

### (4) Tests
- **RED 1** — `ReleaseGateFilterTests.TheWorkflowReader_SegmentsACrlfCopyLikeTheLfOriginal` (new [Fact] in the same file):
  ```csharp
  string lf = ReadWorkflowCode(WorkflowRelativePaths[1]);
  string crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);
  JobSegments(StripYamlComments(NormalizeLineEndings(crlf))).Select(s => s.Name)
      .Should().Equal(JobSegments(lf).Select(s => s.Name));
  JobSegments(lf).Select(s => s.Name).Should().Contain(["build-and-test", "publish"]);
  ReadRepoFile(WorkflowRelativePaths[1]).Should().NotContain("\r");
  ```
  RED until `NormalizeLineEndings` exists (compile error, then the last assertion fails on this machine).
- **RED 2** — `TmTabsHeaderSlotsTests.TheBaselineHash_DoesNotDependOnTheCheckoutLineEndings`: render `RenderNoSlots()`
  and hash the markup twice through a new private helper `static string PreSlotHash(string markup)`. The helper
  contains the two normalisations from Step 1b; make the existing test use it as well. Hash once as-is and once with
  `.Replace("\n","\r\n")`, then assert equal. RED before the helper normalises.
- The 10 listed tests turn green.

### (5) Verification
```
dotnet test tests/Tempo.Blazor.Tests -f net10.0 --filter "FullyQualifiedName~FullCloneWorkflow|FullyQualifiedName~ReleaseGateFilter|FullyQualifiedName~PublishEnvironmentGate|FullyQualifiedName~ReleaseEvidence|FullyQualifiedName~ReleaseContract|FullyQualifiedName~TmTabsHeaderSlots"
# expect 0 failed (9 skipped bash/FullClone probes are environment-gated by design)
git ls-files --eol | Select-String 'w/crlf' | Measure-Object   # after Step 2 + refresh: 0
```

### (6) Risk / CHANGELOG
- Test-only plus repo-config change. No product change and **no CHANGELOG**.
- Step 2's working-tree refresh touches every file's mtime, so the next build is a full rebuild.

### (7) Effort
**S** for Step 1. Step 2 is S plus the one-time refresh.

### (8) Open question
**Q1** (EOL policy).

---

## A2 — [02dd6146] JsonDocumentationGenerator `ExtractMembers` misses expression-bodied props and positional record params

### (1) Status
**OPEN.** `tempo-blazor-abstractions.json:11828-11856` (TmLayoutContext) lists only `IsDesktop`, `IsMobile` and
`IsTablet`, and lists them as **`"kind": "Field"`**. `Mode`, `Resolved` and `CssModifier` are missing.

### (2) Current state
**Scanner:** `JsonDocumentation/JsonDocumentationGenerator/PackageDocumentationGenerator.cs`, class `SourceDocParser`
(:1182).
- Regexes (:1191-1202):
  ```csharp
  PropertyLineRegex = @"public\s+(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;"
  FieldLineRegex    = @"public\s+(?:const\s+|static\s+readonly\s+|static\s+)?(?<type>.+?)\s+(?<name>…)\s*(?:=.*)?;"
  ```
- `ExtractMembers(string source, string typeName)` (:1336-1436) runs three line loops:
  1. `{ get;` properties.
  2. Fields. Each line must start with `public `, contain `;`, and have no `(`/`{` in `line.Split('=',2)[0]`.
  3. Methods.
- Results are deduped by `Kind:Name` and capped at `Take(80)`.

**How each `TmLayoutContext` member (src/Tempo.Blazor.Abstractions/Layout/TmLayoutContext.cs) is handled:**
- `public bool IsMobile => Resolved == TmLayoutMode.Mobile;` (:33): the property regex needs `{ get;`, so it misses.
  The field loop's `Split('=')` cuts at the `=` of `=>`, so the line passes as a **Field**. That is a wrong kind.
- `public string CssModifier => Resolved switch` (:24): there is no `;` on the line, so it is **skipped**.
- `public TmLayoutMode Resolved { get => _resolved; init => … }` (:17): `{ get =>` is not `{ get;`, and the field loop
  sees `{`, so it is **skipped**.
- `Mode` is only a positional parameter (:9): there is no extraction for it at all, so it is **skipped**.

**Callers:**
- `PublicApiDocumentationScanner.Scan` (:1063-1070) calls it only for single-top-level-type files.
- `UnambiguousMemberDescription` (:445-460) uses `typeName: string.Empty` for description repair.

**Size of the impact (measured with `Select-String` over `src/**/*.cs`):**
- ~514 public expression-bodied members, 489 of them single-line ones that are currently emitted as `Field`.
- 238 positional `public … record X(`.
- 499 `"kind": "Field"` entries in `tempo-blazor-all.json`.

### (3) Exact change
`PackageDocumentationGenerator.cs` is 2 238 lines, frozen over the 1 200-line ceiling in
SourceFileLengthTests.cs:35. **Put new code in a new file.**
1. Change `internal static class SourceDocParser` (:1182) to `internal static partial class SourceDocParser`.
2. Create `JsonDocumentation/JsonDocumentationGenerator/SourceDocParser.Members.cs` (namespace `JsonDocumentationGenerator`). It contains:
   ```csharp
   internal static partial class SourceDocParser
   {
       // `public [modifiers] Type Name =>` — no '(' before the arrow (methods/operators), no indexers.
       private static readonly Regex ExpressionBodiedPropertyRegex = new(
           @"^public\s+(?:(?:static|virtual|override|sealed|abstract|new|required|readonly)\s+)*(?<type>[^=(){};]+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=>",
           RegexOptions.Compiled, GeneratorRegex.MatchTimeout);

       // `record [struct|class] Name[<T>] (` — positional records only; class primary constructors
       // create NO properties and must not match.
       private static readonly Regex PositionalRecordRegex = new(
           @"\brecord\s+(?:struct\s+|class\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>{};()]+>)?\s*\(",
           RegexOptions.Compiled, GeneratorRegex.MatchTimeout);

       /// <summary>True when the first '=' of a line is the '=>' of an expression body.</summary>
       internal static bool IsExpressionBody(string line)
       {
           var eq = line.IndexOf('=');
           return eq >= 0 && eq + 1 < line.Length && line[eq + 1] == '>';
       }

       internal static IEnumerable<MemberDocumentation> ExtractExpressionBodiedProperties(string[] lines, string typeName) { … }
       internal static IEnumerable<MemberDocumentation> ExtractPositionalRecordParameters(string source, string[] lines, string typeName) { … }
   }
   ```
   - **`ExtractExpressionBodiedProperties`**: for each trimmed line that starts with `public `, does not contain
     `" operator "`, and matches the regex, emit
     `new MemberDocumentation(name, "Property", NormalizeType(type), CleanXmlDoc(ReadXmlDocBlockBefore(lines, i)), null)`.
     Skip `name == typeName`.
   - **`ExtractPositionalRecordParameters`**:
     1. For each regex match where `typeName == ""` or `name == typeName`, take the text after `(`.
     2. Scan to the matching `)`, balancing `()`, `<>` and `[]`. It may span lines.
     3. Split on top-level `,` (depth 0 for `()`, `<>` and `[]`).
     4. For each piece, strip leading attribute lists (`[property: …]`, possibly several), cut `= default`, and take the
        last token as the name and the rest as the type.
     5. The description is the `<param name="{name}">…</param>` text from the XML doc block before the record's line.
        Get the line index of the match by counting `\n` before `match.Index` in the source normalised by
        `SplitLines`. Read the block with `ReadXmlDocBlockBefore`, take the param element by regex
        `(?s)<param\s+name="NAME">(.*?)</param>`, and clean it with the same steps as `CleanXmlDoc`. Simplest is to
        wrap it as `<summary>…</summary>` and call `CleanXmlDoc`.
     6. Emit Kind `"Property"`.
3. **Edit `ExtractMembers` (:1336-1436):**
   - In the property loop, widen `PropertyLineRegex` (:1191) to `@"public\s+(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*(?:get|init|set)\b"`.
     This picks up `{ get => …; init => …; }` (Resolved).
   - In the field loop, add `|| IsExpressionBody(line)` to the `continue` condition (:1372-1379). This stops
     `IsMobile => …` from being emitted as a Field.
   - Before the final dedupe, add:
     ```csharp
     members.AddRange(ExtractExpressionBodiedProperties(lines, typeName));
     members.AddRange(ExtractPositionalRecordParameters(source, lines, typeName));
     ```
   - Change the dedupe (:1429-1431) so an explicit body declaration and a positional parameter of the same name
     collapse to one entry, preferring a non-empty description:
     ```csharp
     .GroupBy(m => $"{m.Kind}:{m.Name}", StringComparer.OrdinalIgnoreCase)
     .Select(g => g.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Description)) ?? g.First())
     ```
     Explicit declarations are added first, so `Resolved` keeps its body doc "A resolved layout is desktop, tablet or
     mobile."
4. Regenerate everything and commit it **in the same commit** as the generator change (N328 / freshness):
   ```
   dotnet run --project JsonDocumentation/JsonDocumentationGenerator/JsonDocumentationGenerator.csproj -- JsonDocumentation generate
   ```
   **Expected bundle churn:** nearly every `tempo-blazor*.json`, measured by `"kind": "Field"` counts per bundle:
   | Bundle | Field entries |
   |---|---|
   | tempo-blazor.json / tempo-blazor-documentation.json | 113 each |
   | abstractions | 106 |
   | diagrameditor | 82 |
   | wireframe | 43 |
   | documenteditor | 34 |
   | emailtemplates-abstractions | 34 |
   | collaboration | 21 |
   | notioneditor | 16 |
   | mcp | 12 |
   | spreadsheet | 9 |
   | documentformats | 8 |
   | datatablexlsx | 6 |
   | emailtemplates | 4 |
   | modeling | 2 |
   | pdfviewer | 1 |

   In addition, every positional record gains members, and `tempo-blazor-all.json` changes. Some overlays may be
   rewritten by `RepairNamedDescriptions` (:341); commit what the generator writes. Overlay `members` arrays are only
   used when the generated side has none (`DocumentJsonMerger.Merge` default branch, :1659-1663), so overlay `Field`
   entries (37) remain untouched but are shadowed.
5. Optional cleanup: the TmLayoutContext overlay description
   (`JsonDocumentation/Abstractions/Layout/TmLayoutContext.json:5`) can drop the "Mode = …; Resolved = …" prose, since
   the members now carry it.

### (4) Tests
New file `tests/Tempo.Blazor.Tests/Documentation/SourceDocParserMemberTests.cs` (InternalsVisibleTo already exists,
JsonDocumentationGenerator.csproj:14). All tests call `SourceDocParser.ExtractMembers(source, typeName)`:

| Test | Asserts |
|---|---|
| `ExpressionBodiedProperty_IsAProperty_NotAField` | `public bool IsMobile => X == 1;` gives one member `IsMobile` with Kind `Property`, type `bool`, and the summary text; no `Field` named `IsMobile`. |
| `MultiLineExpressionBodiedProperty_IsListed` | `public string CssModifier => Resolved switch` + `{ … };` gives `CssModifier`/`Property`/`string`. |
| `AccessorBodiedProperty_IsListed` | `public TmLayoutMode Resolved { get => _r; init => _r = value; }` gives `Resolved`/`Property`. |
| `PositionalRecordParameters_AreProperties_WithTheirParamDocs` | TmLayoutContext shape: `Mode` has description "The mode the host asked for. Auto while the container is being measured." |
| `PositionalRecord_MultiLine_GenericsDefaultsAndAttributes` | `public sealed record Page<T>(\n IReadOnlyList<T> Items,\n Dictionary<string, int> Counts,\n int Total = 0,\n [property: JsonPropertyName("c")] string? Cursor = null);` gives exactly Items/Counts/Total/Cursor with types `IReadOnlyList<T>`, `Dictionary<string, int>`, `int`, `string?`. |
| `ClassPrimaryConstructorParameters_AreNotMembers` | `public sealed class Svc(ILogger log) { }` gives no `log`. |
| `ExpressionBodiedMethod_StaysAMethod_AndOperatorsAreIgnored` | `public int Twice(int x) => x * 2;` is Method; `public static bool operator ==(A a, A b) => …` gives no property. |
| `FieldWithLambdaInitializer_StaysAField` | `public static readonly Func<int,int> Twice = x => x * 2;` gives Field `Twice`. |
| `TheRealTmLayoutContext_ListsAllSixMembersAsProperties` | Read `src/Tempo.Blazor.Abstractions/Layout/TmLayoutContext.cs` (via `ThemeCss.RepositoryRoot()`); names ⊇ {Mode, Resolved, CssModifier, IsMobile, IsTablet, IsDesktop}, all Kind `Property`. |
| `TheCommittedAbstractionsBundle_DocumentsTmLayoutContextMembers` | Parse `tempo-blazor-abstractions.json`, find `itemName == "TmLayoutContext"`, its `members` contain Mode/Resolved/CssModifier with kind `Property`. RED until regeneration. |

All are RED today except `FieldWithLambdaInitializer_StaysAField` and `ExpressionBodiedMethod…`, which are guards.
Existing guards that must stay green: `GeneratedDocumentationMatchesCommittedTests`,
`ComponentDocumentationFreshnessTests`, `UndescribedParametersRatchetTests`, `ComponentChangeCarriesJsonTests`, and
`SourceFileLengthTests` (the new file must stay under 1 200 lines).

### (5) Verification
```
dotnet test tests/Tempo.Blazor.Tests -f net10.0 --filter "FullyQualifiedName~SourceDocParserMemberTests|FullyQualifiedName~GeneratedDocumentationMatchesCommitted|FullyQualifiedName~ComponentDocumentationFreshness|FullyQualifiedName~UndescribedParametersRatchet|FullyQualifiedName~SourceFileLength"
git diff --stat -- tempo-blazor*.json JsonDocumentation/   # review churn; Field→Property is expected
```

### (6) Risk / CHANGELOG
- No code API change.
- The committed JSON bundles are consumer data, read by the MCP server. The `kind` of ~490 members changes from
  `Field` to `Property`, and new members appear. **CHANGELOG `### Documentation` (or `Changed`) line:**
  "Generated JSON documentation now lists expression-bodied properties and positional record parameters as
  `Property` members (previously missing, or mis-kinded as `Field`)."
- See **Q3**.

### (7) Effort
**M**: the parser is ~150 lines; regeneration plus review of a large diff.

---

## A3 — [efdc933b] CSS bundle freshness guard

### (1) Status
**PARTLY DONE.**
- `d7466f31` "ci: close the bundled-CSS freshness hole (U9)" added the following to `.github/workflows/publish-nuget.yml`:
  - lines 94-100: step `Force the CSS bundler to run` (`touch src/Tempo.Blazor/wwwroot/css/tempo-blazor.css`), before Build;
  - lines 105-109: step `Verify the committed CSS bundle is fresh`
    (`git diff --exit-code -- src/Tempo.Blazor/wwwroot/css/tempo-blazor.bundled.css`), after Build;
  - the `eol=lf` rule for the bundle in `.gitattributes`.
- Today's bundle is fresh. I re-implemented the `BundleCss` algorithm from Tempo.Blazor.csproj:109-160 in PowerShell
  over the current sources, and the output was **byte-identical** to the committed `tempo-blazor.bundled.css`
  (480 642 chars).

**Gaps:**
- (a) `.github/workflows/publish-nuget-org.yml` has **neither step** (Build at :91, no touch, no verify). Both
  workflows run on push/PR/tag, so a release via the org workflow is unguarded.
- (b) No unit test guards the presence of these two steps, unlike every other CI-only release step
  (`ReleaseGateFilterTests.RequiredReleaseMarkers`, :917-924).
- (c) There is no local unit test that catches a stale bundle. A post-build test cannot catch one anyway:
  `BundleCssFiles` (csproj :169) regenerates the bundle on every build, so disk always equals sources. The only
  meaningful local failure is "regenerated but not staged", which the CI `git diff` already catches on every PR.

### (2) Existing tests touching the bundle (none checks freshness)
- `CssBundlerInputSourceTests` (how the bundler is wired, :388-620).
- `CssBundleCalcWhitespaceTests`, `MarkupClassCoverageTests:441`, `ReducedMotionSelectorTests:172`,
  `CodeEditorWrapStylesheetTests:59`, `TmSignatureCaptureTests:294`, `TmToolbarButtonLabelTests:180`: these read
  bundle content.

### (3) Exact change (recommended minimum)
1. In `.github/workflows/publish-nuget-org.yml`, insert immediately **before** `      - name: Build` (line 91), using
   the same indentation as the existing steps:
   ```yaml
         - name: Force the CSS bundler to run
           # Same as publish-nuget.yml: BundleCssFiles is incremental, a fresh checkout gives every file
           # one mtime, so without the touch the freshness diff below compares the bundle with itself.
           run: touch src/Tempo.Blazor/wwwroot/css/tempo-blazor.css

   ```
   and immediately **after** the Build step (after line 92):
   ```yaml

         - name: Verify the committed CSS bundle is fresh
           # A stale committed bundle would ship last cycle's CSS; eol=lf in .gitattributes keeps the diff stable.
           run: git diff --exit-code -- src/Tempo.Blazor/wwwroot/css/tempo-blazor.bundled.css
   ```
2. In tests/Tempo.Blazor.Tests/Packaging/ReleaseGateFilterTests.cs:917-924, add two markers to `RequiredReleaseMarkers`:
   ```csharp
   "touch src/Tempo.Blazor/wwwroot/css/tempo-blazor.css",
   "git diff --exit-code -- src/Tempo.Blazor/wwwroot/css/tempo-blazor.bundled.css",
   ```
   `BothPublishWorkflows_CarryTheReleaseStepsThatOnlyExistInCi` then requires them in both files.
   `TheGuard_DetectsEachReleaseStepBeingRemovedOrReverted` mutates every marker automatically.
3. New test `ReleaseGateFilterTests.TheBundleFreshnessCheck_RunsAfterTheBuild_AndTheTouchBeforeIt`. In each workflow's
   `build-and-test` segment (`JobSegments`), assert:
   - `IndexOf(touchMarker) < IndexOf("run: dotnet build")`;
   - `IndexOf(diffMarker) > IndexOf("run: dotnet build")`.

   Mutation arm: swap the two steps in a copy and expect a red.

### (4) Tests
- The RED test is `BothPublishWorkflows_CarryTheReleaseStepsThatOnlyExistInCi` after step 2. It is red until step 1
  adds the steps to the org workflow.
- Plus the ordering test from step 3.

### (5) Verification
`dotnet test tests/Tempo.Blazor.Tests -f net10.0 --filter "FullyQualifiedName~ReleaseGateFilterTests"`.
Depends on A1 Step 1a on Windows CRLF checkouts.

### (6) Risk
- CI-only. The org workflow fails on a stale bundle, which is intended. No CHANGELOG.

### (7) Effort
**S**

### (8) Open question
**Q4**: an optional local in-process bundler test. Recommendation: not worth it.

---

## A4 — [c51ddf81 #3] `ComponentChangeCarriesJsonTests.EndOfLineNormalizationCommits_AreClean`

### (1) Status
**OPEN.**

### (2) Current state
tests/Tempo.Blazor.Tests/Documentation/ComponentChangeCarriesJsonTests.cs:289-311:
```csharp
/// … <c>3f5d7f5f</c> restored LF in the round-3-touched promoted component files and
/// <c>975a75be</c> stored CRLF in them — both touched component files with zero content delta.
[FullCloneFact]
public void EndOfLineNormalizationCommits_AreClean()
{
    CommitChange lfRestore = ChangeForCommit("3f5d7f5f3a98e16168544927b1157021004cd555");
    CommitChange crlfStore = ChangeForCommit("975a75be9481d59dfeb945d554a27f558bdd71c6");
    …
    Evaluate([crlfStore]).Should().BeEmpty("975a75be only stored CRLF line endings — the same flag hides the churn");
```
Facts (git):
- **`975a75be`** = "style(demo): normalize FeedbackPage.razor line endings back to LF". It touches only
  `src/Tempo.Blazor.Demo.SharedUI/Pages/FeedbackPage.razor`. `IsComponentApiFile` (:81-87) excludes
  `src/Tempo.Blazor.Demo*`, so `ChangeForCommit` returns **no component files** and the arm is **vacuous**. It also
  does not store CRLF; it restores LF.
- **`98cc1c99`** (`98cc1c9946ed9f640e4a164fce8dcaebaa9beee9`) = "fix(overlay): promote the remaining modal
  viewport-anchored roots (V2)". This commit **stored CRLF** in TmLightbox/TmCommandPalette/TmKeyboardShortcutsHelp/
  TmNotificationToastContainer/TmGanttImportDialog: `TmCommandPalette.razor` at 98cc1c99 has 194 CR bytes. It has no
  `JsonDocumentation/**` path.
  - Raw diff: 56 `[Parameter]` lines under `src/Tempo.Blazor/Components`, which would be a finding.
  - `--ignore-cr-at-eol`: **0** `[Parameter]` lines. Only real content such as `DisposeAsync` remains, so it is clean.
  - It is the non-vacuous arm the comment meant. (It is an ancestor of `3f5d7f5f`, which restored LF in the same files.)

### (3) Exact change
1. Lines 289-294, the summary. Replace with:
   ```csharp
   /// The git layer's half of the W1 contract, over two real commits that rewrote every line of
   /// component files: <c>98cc1c99</c> (round-3 V2) changed content AND stored CRLF in TmLightbox,
   /// TmCommandPalette, TmKeyboardShortcutsHelp, TmNotificationToastContainer and TmGanttImportDialog
   /// (its raw diff carries 56 [Parameter] lines, no JsonDocumentation/** path), and <c>3f5d7f5f</c>
   /// restored LF in the same files. Under <c>--ignore-cr-at-eol</c> neither reads as a parameter change.
   /// The raw-diff arm proves the commits are not vacuous: without the flag both WOULD be findings.
   ```
2. Line 300-301: `CommitChange crlfStore = ChangeForCommit("98cc1c9946ed9f640e4a164fce8dcaebaa9beee9");`
3. Line 309: message `"98cc1c99 stored CRLF in five component files while changing their content — the flag hides the churn and leaves only the real (non-parameter) edits"`.
4. Teeth (recommended): give `ChangeForCommit` (:145) a parameter `bool ignoreCrAtEol = true`. In the args at :158-160,
   include `"--ignore-cr-at-eol"` only when it is true. Add to the test:
   ```csharp
   Evaluate([ChangeForCommit("98cc1c9946ed9f640e4a164fce8dcaebaa9beee9", ignoreCrAtEol: false)])
       .Should().ContainSingle("without --ignore-cr-at-eol the CRLF rewrite reads as 56 [Parameter] edits — the arm is not vacuous");
   Evaluate([ChangeForCommit("3f5d7f5f3a98e16168544927b1157021004cd555", ignoreCrAtEol: false)])
       .Should().ContainSingle("the LF restore likewise rewrote every [Parameter] line");
   ```
   (3f5d7f5f raw diff: 56 `[Parameter]` lines, also measured.)

### (4) Tests
- The test itself. The raw arm is RED-provable: removing the flag from the default makes the clean arm fail.
- Run it, together with `CalibrationCommits_ResolveExactlyAsRecorded`.

### (5) Verification
`dotnet test tests/Tempo.Blazor.Tests -f net10.0 --filter "FullyQualifiedName~ComponentChangeCarriesJsonTests"`.
Currently 100 % green, including the vacuous arm.

### (6) Risk
Test-only. No CHANGELOG.

### (7) Effort
**S**

---

## A5 — [ec0299b9 #3] TmIconRegistryTests vs StencilPackRendererTests share static state

### (1) Status
**OPEN. Race reproduced.**
- `dotnet test … --filter "FullyQualifiedName~TmIconRegistryTests|FullyQualifiedName~StencilPackRendererTests"`
  failed in 2 of 2 combined runs. The failing test was
  `IconRegistry_Custom_Provider_Called_For_Unknown_BuiltIn_Name`, with markup
  `<span class="tm-icon-unknown" …>` (provider wiped).
- `TmIconRegistryTests` alone was green 3 of 3.

### (2) Shared state
- `src/Tempo.Blazor/Components/Icons/IconRegistry.cs:21-24` has static `_icons` and `_providers`.
- `Reset()` (:97-104) clears both.
- `Resolve`/`Contains` (:61-91) read **without the lock**, so a concurrent `Reset` can also throw
  "Collection was modified" for any test that renders `TmIcon` with an unknown name.
- Writers:
  - `tests/Tempo.Blazor.Tests/Components/Icons/TmIconRegistryTests.cs:13`: `Register`/`RegisterProvider` plus `Reset()` in `Dispose(bool)` :16-21.
  - `tests/Tempo.Blazor.Tests/Wireframe/StencilPackRendererTests.cs:14`: `Register` at :93 and :201, plus `Reset()` in `Dispose()` :16-19.
- No xunit parallelisation config exists (no `xunit.runner.json`, no `CollectionBehavior`), so classes run in
  parallel by default (xUnit 2.9.3).

### (3) Exact change
1. New file `tests/Tempo.Blazor.Tests/Components/Icons/IconRegistryCollection.cs`. It mirrors
   Components/Spreadsheet/SpreadsheetClipboardCollection.cs:
   ```csharp
   using Xunit;

   namespace Tempo.Blazor.Tests.Components.Icons;

   /// <summary>
   /// Every test class that writes the process-wide static <c>IconRegistry</c> (Register,
   /// RegisterProvider, Reset) runs in this collection. DisableParallelization also keeps it apart from
   /// every other collection: Resolve/Contains read the registry without the lock, so a Reset racing any
   /// TmIcon render could throw "Collection was modified" there too.
   /// </summary>
   [CollectionDefinition(Name, DisableParallelization = true)]
   public sealed class IconRegistryCollection
   {
       public const string Name = "IconRegistry";
   }
   ```
2. TmIconRegistryTests.cs: add `[Collection(IconRegistryCollection.Name)]` above `public class TmIconRegistryTests` (:13).
3. StencilPackRendererTests.cs: add `using Tempo.Blazor.Tests.Components.Icons;` and
   `[Collection(IconRegistryCollection.Name)]` above `public class StencilPackRendererTests` (:14).

`DisableParallelization = true` costs under 1 s (31 tests) and removes the unlocked-reader hazard for the rest of the
suite. Recommended.

### (4) Tests
- The RED proof is the combined filter run above: red before, green after. Loop it 10× for confidence.
- Optional extra guard: `IconRegistryCollectionTests.EveryTestClassThatWritesTheRegistry_IsInTheIconRegistryCollection`.
  It reflects over the test assembly; for each type whose source file (search `tests/Tempo.Blazor.Tests/**/*.cs`)
  contains `IconRegistry.Register` or `IconRegistry.Reset`, assert that it has
  `[Collection("IconRegistry")]`. Cheap insurance against a third writer.

### (5) Verification
```powershell
1..10 | % { dotnet test tests/Tempo.Blazor.Tests -f net10.0 --no-build --filter "FullyQualifiedName~TmIconRegistryTests|FullyQualifiedName~StencilPackRendererTests" | Select-String "Neúspěšné!|Úspěšné!|Failed!|Passed!" }
```

### (6) Risk
Test-only.

### (7) Effort
**S**

---

## A6 — [4c92ba05 #1 #2] RelativeTime snippet comparison and culture restore

### (1) Status
Both **OPEN**.

### (2) Current state

**#1, the snippet test.** tests/Tempo.Blazor.Tests/Localization/RelativeTimeRegistrationTests.cs:150-173,
`DocumentedRegistrationSnippet_Compiles`:
- It compares only marker `doc-sample:user-zone` up to the first `)));`, i.e. the registration lines.
- The doc's class body is not compared. That is docs/localization-plurals.md:170-174:
  ```csharp
  sealed class UserTimeProvider(CircuitZoneCache cache) : TimeProvider
  {
      // Read lazily: the zone arrives from JS after the first render.
      public override TimeZoneInfo LocalTimeZone => cache.Zone ?? TimeZoneInfo.Utc;
  }
  ```
- The compiled sample `tests/Tempo.Blazor.Tests/Localization/DocumentedUserZoneRegistration.cs` has:
  - a **commented paraphrase** at :25-29 (`// sealed class UserTimeProvider…`);
  - the real class at :45-49, as `private sealed class UserTimeProvider(CircuitZoneCache cache) : TimeProvider`.

**#2, culture restore.** tests/Tempo.Blazor.Tests/Localization/RelativeTimeFormatterTests.cs never restores the culture:
- :27-32 `Formatter()` sets `CultureInfo.CurrentUICulture`/`CurrentCulture` and never restores them.
- It is used by `Cs/En/Fr` (:34-41) and by tests :202, :214.
- :228-229 and :254 also set culture directly.

**There is no shared culture-scope helper in the repo.** Private duplicates exist:
- `TmChartTimeAxisTests.UseCulture` + `CultureRestore` (Charts/TmChartTimeAxisTests.cs:19-35);
- the same pair in `TmCalendarHeatmapTests` (:333-350);
- hand-written try/finally in RelativeTimeAdoptionTests.cs:90-124, PluralLocalizerTests.cs:286-300 and TmSelectTests.cs:289-302.

### (3) Exact change

**#2 first (adds the helper).** New file `tests/Tempo.Blazor.Tests/Localization/CultureScope.cs`:
```csharp
using System.Globalization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>Sets CurrentCulture and CurrentUICulture and restores both on Dispose. Use with <c>using var _ = CultureScope.Use("cs");</c>.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    private CultureScope(CultureInfo culture, CultureInfo uiCulture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture;
    }

    public static CultureScope Use(string name) => Use(CultureInfo.GetCultureInfo(name));

    public static CultureScope Use(CultureInfo culture, CultureInfo? uiCulture = null) => new(culture, uiCulture ?? culture);

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
```
Changes in RelativeTimeFormatterTests.cs:
1. Replace `Formatter` (:27-32) with a version that does not touch culture:
   ```csharp
   private static ITmRelativeTimeFormatter Formatter(TimeProvider? clock = null)
       => new TmRelativeTimeFormatter(new DefaultTmLocalizer(new JsonStringLocalizer<TmResources>()), clock);

   private static string FormatIn(string culture, DateTimeOffset value, DateTimeOffset? now, TimeZoneInfo? zone)
   {
       using var _ = CultureScope.Use(culture);
       return Formatter().Format(value, now ?? Now, zone ?? Prague);
   }
   ```
2. Replace `Cs`/`En`/`Fr` (:34-41) with `=> FormatIn("cs"|"en"|"fr", value, now, zone)`. The scope must wrap `Format`,
   because the localizer reads the UI culture at format time.
3. :200-208: add `using var _ = CultureScope.Use("cs");` as the first line, and change `Formatter("cs", clock)` to `Formatter(clock)`.
4. :214: `using var _ = CultureScope.Use("en");` and `var formatter = Formatter();`.
5. :227-229: replace the two assignments with `using var _ = CultureScope.Use(ambient);`.
6. :254: replace with `using var _ = CultureScope.Use(CultureInfo.CurrentCulture, CultureInfo.GetCultureInfo("en"));`.
   That keeps the current semantics, which set only the UI culture.

Optional, separate commit: migrate the private `UseCulture` helpers in TmChartTimeAxisTests and
TmCalendarHeatmapTests to `CultureScope`.

**#1 (snippet).**
1. DocumentedUserZoneRegistration.cs:
   - Delete the commented paraphrase, lines 25-29 (the `// sealed class UserTimeProvider…` block, including the
     blank line before it).
   - At :45, change `private sealed class UserTimeProvider` to `sealed class UserTimeProvider`. A nested class is
     private by default, so the text now matches the doc exactly.
2. RelativeTimeRegistrationTests.cs: append to `DocumentedRegistrationSnippet_Compiles`, after :166:
   ```csharp
   const string classAnchor = "sealed class UserTimeProvider";
   Normalize(ClassBlock(doc, classAnchor)).Should().Be(Normalize(ClassBlock(sample, classAnchor)),
       "the documented UserTimeProvider (the lazy zone read the K1 fix depends on) is the compiled one");
   ```
   Add a helper next to `Normalize`:
   ```csharp
   /// <summary>From the anchor to the line that closes the class's brace (depth back to 0).</summary>
   private static string ClassBlock(string text, string anchor)
   {
       var start = text.IndexOf(anchor, StringComparison.Ordinal);
       start.Should().BeGreaterThanOrEqualTo(0, $"'{anchor}' must exist exactly where the doc shows it");
       text.IndexOf(anchor, start + anchor.Length, StringComparison.Ordinal).Should().Be(-1, $"'{anchor}' must appear once");
       var depth = 0; var seenBrace = false;
       for (var i = start; i < text.Length; i++)
       {
           if (text[i] == '{') { depth++; seenBrace = true; }
           else if (text[i] == '}' && --depth == 0 && seenBrace) return text[start..(i + 1)];
       }
       throw new InvalidOperationException($"unterminated class block after '{anchor}'");
   }
   ```
   The "appear once" assertion is what forces the deletion of the commented copy.

### (4) Tests
- **RED (#2):** new `RelativeTimeFormatterTests.Formatting_leaves_the_ambient_culture_as_it_found_it`:
  ```csharp
  using var _ = CultureScope.Use("de-DE");
  Cs(Now); En(Now); Fr(Now);
  CultureInfo.CurrentCulture.Name.Should().Be("de-DE");
  CultureInfo.CurrentUICulture.Name.Should().Be("de-DE");
  ```
  Red today, because `Fr` leaves `fr` behind.
- **#1:** the extended `DocumentedRegistrationSnippet_Compiles`.
  - It is RED on today's sample, because the commented copy makes "appear once" fail.
  - It is green after the edit.
  - Mutation proof: change `cache.Zone ?? TimeZoneInfo.Utc` to `TimeZoneInfo.Local` in the doc only, and the test must go red.

### (5) Verification
`dotnet test tests/Tempo.Blazor.Tests -f net10.0 --filter "FullyQualifiedName~RelativeTime"`

### (6) Risk
Test-only.

### (7) Effort
**S**

---

## A7 — [f4c27b20 #2] TmToolbarOverflowTests.cs formatting `}}`

### (1) Status
**OPEN.** `tests/Tempo.Blazor.Tests/Toolbar/TmToolbarOverflowTests.cs:497` is `    }}`. The file uses a file-scoped
namespace (`namespace Tempo.Blazor.Tests.Toolbar;`). The file's last bytes are `32,125,125,10`. It is LF.

### (2) Cause
RED commit `badf0932` appended a test and joined the method brace and the class brace.

### (3) Change
Replace line 497 `    }}` with two lines:
```
    }
}
```
Keep the trailing newline.

### (4) Tests
Formatting only; the file must still compile.

### (5) Verification
```
dotnet format tests/Tempo.Blazor.Tests/Tempo.Blazor.Tests.csproj whitespace --verify-no-changes --include tests/Tempo.Blazor.Tests/Toolbar/TmToolbarOverflowTests.cs
dotnet test tests/Tempo.Blazor.Tests -f net10.0 --filter "FullyQualifiedName~TmToolbarOverflowTests"
```

### (6) Risk
None.

### (7) Effort
**S** (trivial; fold it into any commit of this cluster).

---

## A8 — [058c3487] + [835acf83 #5] + [08a22db5 #3] MobileActionBarE2ETests flakes

### (1) Status
**OPEN.** Not reproduced in this session: it is an E2E under load and needs a long run. The races below are derived
from the code and match the recorded symptoms. `[assembly: DoNotParallelize]` already exists
(tests/Tempo.Blazor.E2E/AssemblyInfo.cs:3). "Parallel load" therefore means CPU contention from sharded processes
(docs/e2e-test-lanes.md:28-32, 103-117), not in-process parallelism. **`[DoNotParallelize]` is not a fix.**

### (2) Root cause

**Common factor: the demo sets `html { scroll-behavior: smooth; }`** (src/Tempo.Blazor.Demo/wwwroot/css/app.css:5-7).
Every `window.scrollTo(x, y)` and every focus-induced scroll **animates**. docs/e2e-test-lanes.md:142-144 already
records this pitfall.

**(a) `Demo_390_AutoCard_BarSticksAtViewportBottom_WhileTheCardStraddlesTheFold`**
(tests/Tempo.Blazor.E2E/MobileActionBarE2ETests.cs:689-759)
- :715-723 calls `window.scrollTo(0, …)` (smooth) and **in the same function** returns
  `card.getBoundingClientRect().bottom - window.innerHeight`. That is the **pre-scroll** geometry, so `pinCut` is
  measured before the scroll has started.
- Then `WaitForTimeoutAsync(200)` (:725) and the pin assertion (:726). Under CPU load a smooth scroll does not finish
  in 200 ms, so the bar is measured mid-animation.
- The same pattern repeats at :732-740 + :740 and at :756-757.
- Deterministic race: an animation versus a fixed sleep.

**(b) `Demo_1440_ForcedVariant_MorePopover_PaintsSurface_AndReturnsFocus`** (:568-632)
- It uses `CreateContextAsync()`, so the viewport is **1280×720** (PlaywrightTestBase.cs:202). The "1440" in the name
  is stale.
- The `mab-variant-sticky` More trigger (MobileActionBarPage.razor:39-43) lies below the fold.
- :588 `more.FocusAsync()` triggers a **smooth** focus scroll. :589 Enter opens the popover while the page is still
  scrolling (SlowMo 100 ms per action, PlaywrightTestBase.cs:121).
- `overlay.js place()` (src/Tempo.Blazor/wwwroot/js/overlay.js:329-337) sets `panel.style.visibility = 'hidden'` while
  the anchor is not intersecting the viewport.
- `ActionOverflowMenu.HandleOpened` → `OnAfterRenderAsync` calls `first.FocusAsync()`
  (src/Tempo.Blazor/Components/Actions/ActionOverflowMenu.cs:254-282). That focus lands on an element inside a
  `visibility:hidden` panel. `focus()` is then a silent no-op, and there is no retry.
- When the scroll finishes, the panel becomes visible, so the :591 visible-wait passes. But focus stays on the
  trigger, and the 5 s wait for `role=menuitem` (:610-613) times out. This matches "times out on the 5 s
  initial-menuitem-focus wait".
- Load stretches the smooth scroll past the open, which is why the test passes solo.
- Secondary jitter: Tailwind Play CDN transitions shift the page ~176 px for ~250 ms after `data-blazor-ready`
  (docs/e2e-test-lanes.md:136-141; TmTabsHeaderSlotGeometryE2ETests.cs:260-306).

### (3) Exact change (test-side, deterministic)
1. **Shared helper** in tests/Tempo.Blazor.E2E/PlaywrightTestBase.cs, after `WaitForAppReadyAsync` (:337). Lift it from
   `TmTabsHeaderSlotGeometryE2ETests.WaitForLayoutToSettleAsync` (:271-306) and make it selector-parametrised:
   ```csharp
   /// <summary>
   /// Waits until fonts are loaded, no CSS transition runs, no smooth scroll is in flight and the boxes of
   /// <paramref name="selector"/> are identical across 3 consecutive frames (15 s budget).
   /// </summary>
   protected static async Task WaitForLayoutToSettleAsync(IPage page, string selector)
   ```
   - The JS body is the same as :273-304.
   - Make `signature()` read `document.querySelectorAll(selector)`, and include `window.scrollX`/`window.scrollY` in
     the signature so an in-flight smooth scroll counts as "moving".
   - Assert true with a descriptive message.
   - Then switch TmTabsHeaderSlotGeometryE2ETests to call it, so there is one implementation only.
2. **AutoCard test:**
   - Every `window.scrollTo(0, Y)` → `window.scrollTo({ top: Y, behavior: 'instant' })`, at :720, :737 and :756.
   - Split :715-723 so that `pinCut` is read **after** the scroll, in a second `EvaluateAsync`.
   - Replace each `WaitForTimeoutAsync(200)` (:725, :740, :757) with
     `await WaitForLayoutToSettleAsync(page, "[data-testid='mab-auto-bar'], [data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar");`.
   - Call the same helper once right after `SetViewportSizeAsync` (:701), replacing the fonts-only wait at :705-708.
3. **ForcedVariant test:**
   - Rename it to `Demo_1280_ForcedVariant_MorePopover_PaintsSurface_AndReturnsFocus`, or pass a real 1440 context.
     Renaming is simpler; the plan must update any filter strings that cite the old name.
   - After :580, insert:
     ```csharp
     await more.EvaluateAsync("el => el.scrollIntoView({ block: 'center', behavior: 'instant' })");
     await WaitForLayoutToSettleAsync(page, "[data-testid='mab-variant-sticky'] .tm-mobile-action-bar__more");
     ```
   - At :588, replace `await more.FocusAsync();` with
     `await more.EvaluateAsync("el => el.focus({ preventScroll: true })");`.
   - At :610-613, replace the bare 5 s wait with
     `await Assertions.Expect(panel.Locator("[role='menuitem']:not([disabled])").First).ToBeFocusedAsync(new() { Timeout = 15000 });`.
     When it fails, include `document.activeElement` and `getComputedStyle(panel).visibility` in the message: wrap it
     in a try/catch that evaluates them and rethrows an `AssertFailedException`.
4. **Product hardening (optional; see Q5):** in `ActionOverflowMenu.OnAfterRenderAsync` (:269-282), keep
   `_focusFirstItemPending` armed when the focus did not land, or move the initial focus into `overlay.js` once
   `place()` first makes the panel visible. Not needed for the test fix. Record it as a follow-up if Q5 = later.

### (4) Tests
- These are E2E only. Proof of determinism: forcing the slow path must stay green.
- Temporary local check:
  - Prepend `await page.EvaluateAsync("() => window.scrollTo(0,0)")` before the focus in ForcedVariant.
  - Add `document.documentElement.style.scrollBehavior='smooth'` plus a CPU throttle via CDP:
    ```csharp
    var cdp = await page.Context.NewCDPSessionAsync(page);
    await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = 6 });
    ```
  - Before the fix the focus wait should fail; after it, it passes.
  - Remove the throttle afterwards, or keep it as a new `[TestMethod]` variant `…_UnderCpuThrottle`, which is a good
    regression guard.

### (5) Verification
```powershell
$env:TM_E2E_TRACE_ON_FAILURE='false'   # traces mask the race (docs/e2e-test-lanes.md:112-114)
1..5 | % { dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj --filter "FullyQualifiedName~MobileActionBarE2ETests" }
dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj --filter "FullyQualifiedName~TmTabsHeaderSlotGeometryE2ETests"   # helper refactor
```

### (6) Risk
Test-only, unless Q5 = now. If the product hardening is done, add a CHANGELOG "Fixed" line: "the More menu's initial
focus lands even when the menu opened while its trigger was still scrolling into view".

### (7) Effort
**M**

---

## A9 — [08a22db5 #1] EditorShellE2ETests.cs:963 trivial assertion (thresholds demo shell binds nothing)

### (1) Status
**OPEN.**

### (2) Current state
- src/Tempo.Blazor.Demo.SharedUI/Pages/EditorShellPage.razor:61-65: the thresholds shell is
  `<TmEditorShell Breakpoints="_emailBreakpoints" MobileActions="_mobileActions" LeftTitle=… RightTitle=… data-testid="editor-shell-thresholds">`.
  It has **no `LeftOpen`/`RightOpen`/`CollapsedPanels` binding**.
- The shell closes its sheet only through the host. `CloseAllPanelsAsync` (TmEditorShell.razor:728-734) raises
  `LeftOpenChanged(false)`/`RightOpenChanged(false)`. With no delegate the parameters never change, so **this sheet
  can never close**.
- In `Demo_390_TwoInlineSheetsOnOnePage_EscapeFromInsideTheMainSheet_ClosesIt_OnEveryLoad`
  (tests/Tempo.Blazor.E2E/EditorShellE2ETests.cs:940-971), line 963
  `Assert.AreEqual(1, … thresholdsSheet …CountAsync(), "the sheet that did not hold focus stays open")` is **always
  true**. Even if Escape were routed to the thresholds trap (the G3 bug), that sheet would stay open.
- Same for :986.
- `CollapsedPanels` unbound must be preserved. It is the G1 regression case tested by
  `Demo_1280_UnboundThresholdShell_ExpandingTheImposedRail_SwapsThePanels` (:615-638).

### (3) Exact change
1. EditorShellPage.razor:61-65: add two attributes. Leave `CollapsedPanels` **unbound**.
   ```razor
   <TmEditorShell Breakpoints="_emailBreakpoints"
                  @bind-LeftOpen="_thresholdsLeftOpen"
                  @bind-RightOpen="_thresholdsRightOpen"
                  MobileActions="_mobileActions"
                  …
   ```
   Add the fields after :123:
   ```csharp
   // Open state is bound so the thresholds shell's sheet can close (Escape/close/swipe) and the
   // two-sheet E2E has teeth; CollapsedPanels stays UNBOUND on purpose — the G1 imposed-rail case.
   private bool _thresholdsLeftOpen = true;
   private bool _thresholdsRightOpen = true;
   ```
2. EditorShellE2ETests.cs:
   - :618-619: change the comment to "binds no CollapsedPanels (Open is bound since the 08a22db5 follow-up)". Keep the
     test name, or rename it to `…UncollapsedBoundThresholdShell…`. Keeping it is simpler.
   - Add a mirror test after :971:
     `Demo_390_TwoInlineSheetsOnOnePage_EscapeFromInsideTheThresholdsSheet_ClosesItAndLeavesTheMainSheetOpen`.
     Focus the main sheet's tab first, then the thresholds sheet's tab (`{thresholdsSheet} .tm-editor-shell__tab`),
     press Escape, and assert:
     - `Expect(page.Locator(thresholdsSheet)).ToBeHiddenAsync(5000)`;
     - main sheet count == 1;
     - focus lands on `[data-testid='editor-shell-thresholds'] .tm-editor-shell__panel-toggle--mobile` (the sheet
       close path focuses the mobile toggle; see TmEditorShell.razor:730).

     This proves the thresholds sheet *can* close, which is what makes :963 meaningful.
   - Fold a one-line positive control into the existing test before the loop: on attempt 1, the thresholds sheet
     closes via its `.tm-editor-shell__sheet-close` and reopens via its `.tm-editor-shell__panel-toggle--mobile`.
     If it could not close, :963 would prove nothing.
3. Check the other thresholds-dependent tests:
   - :584-600, :603-613, :616-638 (tablet/desktop; Open defaults stay true);
   - :640-658 (mobile at a 700 px container, no closing);
   - :974-987.

   None closes the thresholds panels, so all stay valid.

### (4) Tests
- **RED:** the new mirror test, before the binding. The thresholds sheet stays visible after Escape, so the test fails.
- Then green after the binding.

### (5) Verification
```
dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj --filter "FullyQualifiedName~EditorShellE2ETests"
```

### (6) Risk
Demo-only change. No CHANGELOG (the demo is not packaged).

### (7) Effort
**S**

---

## A10 — [64114d91] InteractiveAutoTests.Scheduler_ViewsWork, [3f1747c0] Server/InteractiveAuto top-layer verification, [4770bcfb] smoke lane CORS

### (1) Status
- **[64114d91] ALREADY DONE (verification only).** Re-run in this session:
  ```
  $env:TM_E2E_TRACE_ON_FAILURE='false'; dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj --filter "FullyQualifiedName~InteractiveAutoTests.Scheduler_ViewsWork|FullyQualifiedName~InteractiveAutoTests.Prerender_WorksWithoutErrors"
  ```
  Result: **2 passed, 0 failed (3 m 35 s)**; log `tmp/followup-analysis/clusterA-ia.log`. The base self-hosted 7107/7108
  (PlaywrightTestBase.cs:450-464) and tore them down in `AssemblyCleanup`. The F7 failure was only "host not started".
  Nothing to implement; record it as verified.
- **[3f1747c0] OPEN.** No Server/IA overlay or top-layer E2E exists. Only two classes derive from `ServerTestBase` and
  `InteractiveAutoTestBase`: tests/Tempo.Blazor.E2E/ServerRenderingTests.cs:10 and InteractiveAutoTests.cs:10. Every
  `:popover-open` test is `WasmTestBase`, e.g. OverlayRound3FixE2ETests.cs:18.
- **[4770bcfb] OPEN.**

### (2) Current state

**CORS.** src/Tempo.Blazor.Demo.Api/Program.cs:32-38 hard-codes the policy, and `app.UseCors()` is at :199:
```csharp
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(
        "http://localhost:5010",
        "https://localhost:7106")
     .AllowAnyMethod()
     .AllowAnyHeader()
     .AllowCredentials()));   // required for SignalR WebSocket handshake
```
- A browser-side WASM host on any other origin (e.g. 7116) is refused, so API-backed smoke tests fail.
- The API-backed smoke tests are DocumentEditorCanvasHistorySaveE2ETests.cs:152 `Phase12_SaveFailure_…` and
  DocumentEditorPdfExportE2ETests.cs:96, which hard-codes `https://localhost:5100`.

**Ports.**
- Demo `https://localhost:7106;http://localhost:5010` (src/Tempo.Blazor.Demo/Properties/launchSettings.json).
- API `https://localhost:5100;http://localhost:5101` (env Development).
- Server `7107;5107`. InteractiveAuto `7108;5108`.

**Host-side API URL.**
- The WASM client hard-codes `https://localhost:5100` (src/Tempo.Blazor.Demo/Program.cs:28-29; IA client
  Program.cs:22-23).
- Server/IA servers read `DemoApi:BaseUrl` with the same default (Demo.Server/Program.cs:24,28; IA Program.cs:28,32).

**E2E base URLs.**
- `WasmTestBase.BaseUrl` honours `TM_E2E_WASM_URL` (PlaywrightTestBase.cs:763-764).
- `ServerTestBase`/`InteractiveAutoTestBase` are hard-coded to 7107/7108 (:771-783).
- Self-host probes fixed URLs (:430-464). An unrelated listener answering `/health` 200 is adopted. Right now pid
  49420 (a `blazor-devserver` not started by us) owns 7106/5010, and pid 69768 (`Tempo.Blazor.Demo.Api.exe`) owns 5100.
  **Do not kill them.**

**Dev cert.**
- `dotnet dev-certs https --check --trust` → "none of them is trusted".
- **Playwright does NOT need a trusted cert**: every context sets `IgnoreHTTPSErrors = true`
  (PlaywrightTestBase.cs:204, test helpers), and the readiness probe uses `DangerousAcceptAnyServerCertificateValidator`
  (:626-631).
- The claim in 3f1747c0 ("needs a trusted dev cert for 7107/7108") is therefore **inaccurate for the E2E lane**, as
  the IA run above shows.
- The cert matters only for **server-side** `HttpClient` calls from the Server/IA hosts to `https://localhost:5100`.
  `/feedback`, the overlay demo page (`@inject ToastService`, `ITmLocalizer` only), makes no API call.

**Server render mode.**
- Demo.Server is Blazor Server via `_Host.cshtml` `render-mode="ServerPrerendered"` + `MapBlazorHub`
  (Program.cs:18-19, :168-169).
- App.razor routes SharedUI pages (`AdditionalAssemblies`), so `/feedback` exists on 7107.
- IA: `Routes @rendermode="InteractiveAuto"` in the client project, with SharedUI in `AdditionalAssemblies`.

### (3) Exact change

**[4770bcfb] CORS (see Q2).** New file `src/Tempo.Blazor.Demo.Api/Services/DemoCorsPolicy.cs`:
```csharp
namespace Tempo.Blazor.Demo.Api.Services;

/// <summary>Origins the demo API answers with CORS headers.</summary>
public static class DemoCorsPolicy
{
    /// <summary>The demo WASM host's own launch-profile origins — always allowed.</summary>
    public static readonly string[] DefaultOrigins = ["http://localhost:5010", "https://localhost:7106"];

    /// <summary>
    /// True for an http(s) origin on a loopback host (localhost, 127.0.0.1, [::1]) on ANY port — so an
    /// E2E or dev host on another port (7116, a sharded lane) can call the API. Development only.
    /// </summary>
    public static bool IsLoopbackOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.IsLoopback;

    /// <summary>The allow decision: the defaults, the configured list (Cors:AllowedOrigins), and in Development any loopback origin.</summary>
    public static bool IsAllowed(string origin, IReadOnlyCollection<string> configured, bool isDevelopment) =>
        DefaultOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)
        || configured.Contains(origin, StringComparer.OrdinalIgnoreCase)
        || (isDevelopment && IsLoopbackOrigin(origin));
}
```

Replace Program.cs:32-38 with:
```csharp
// Cors:AllowedOrigins (env: Cors__AllowedOrigins__0=https://host:port) extends the defaults; in
// Development any loopback origin is allowed so E2E/dev hosts on other ports can reach the API.
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(origin => DemoCorsPolicy.IsAllowed(origin, configuredOrigins, isDevelopment))
     .AllowAnyMethod()
     .AllowAnyHeader()
     .AllowCredentials()));   // required for SignalR WebSocket handshake; SetIsOriginAllowed echoes the origin, so credentials stay valid
```
(`AllowAnyOrigin()` is not allowed together with `AllowCredentials()`. `SetIsOriginAllowed` is the supported way.)

Docs:
- docs/e2e-test-lanes.md "Practical notes" (:150-160): add a bullet that the Development API allows any loopback
  origin, and that a non-Development API needs `Cors__AllowedOrigins__N`.
- AGENTS.md: optionally add one line in the "Run Demo Applications" block.

Optional symmetry: give Server/IA bases env overrides like WASM:
```csharp
protected override string BaseUrl => Environment.GetEnvironmentVariable("TM_E2E_SERVER_URL") ?? "https://localhost:7107";
protected override string BaseUrl => Environment.GetEnvironmentVariable("TM_E2E_IA_URL") ?? "https://localhost:7108";
```
These go in PlaywrightTestBase.cs:773 and :782.

**[3f1747c0] Server/IA top-layer E2E.** New file `tests/Tempo.Blazor.E2E/OverlayTopLayerRenderModesE2ETests.cs`. One
internal static helper class holds the scenarios. They are copied from OverlayRound3FixE2ETests.cs:23-75, 85-118 and
177-206: `OpenPageAsync`, `PaintsAtAsync`, and the two scenarios. Two thin test classes call it:
```csharp
[TestClass] public sealed class ServerOverlayTopLayerE2ETests : ServerTestBase { … }
[TestClass] public sealed class InteractiveAutoOverlayTopLayerE2ETests : InteractiveAutoTestBase { … }
```
Each class has three `[TestMethod]`s:
1. `CommandPalette_OpenedFromDrawer_PaintsAboveTheDrawer`, on `/feedback` at 1440×900:
   - click `[data-testid='open-right-drawer']`, then `[data-testid='drawer-open-palette']`;
   - assert `.tm-command-palette-backdrop` matches `:popover-open`;
   - assert `PaintsAtAsync(".tm-command-palette", ".tm-command-palette-backdrop")`;
   - Escape closes the palette and the drawer survives.
2. `ToastAlreadyVisible_ThenDrawerOpens_ToastPaintsOnTop`: the "Long (10s)" button, then the right drawer; the pinned
   toast container re-raises above it.
3. Per render mode:
   - **Server, `CircuitReconnect_RestoredPopoverRoots_StayPromoted_AndANewSurfacePaintsOnTop`:**
     1. Open the right drawer.
     2. `await page.Context.SetOfflineAsync(true)`, then wait for the reconnect UI
        (`#components-reconnect-modal` visible, or the `components-reconnect-show` class on it).
     3. `SetOfflineAsync(false)`, then wait until it is hidden (timeout 30 s).
     4. Assert the drawer root still matches `:popover-open`.
     5. Open the palette from the drawer and assert it paints on top.
   - **IA, `RenderModeIsRecorded_AndOverlayPromotes`:** read `[data-render-mode]` (stamped by the shared layout; see
     InteractiveAutoTests.cs:36-40) and `TestContext.WriteLine` it. Then repeat scenario 1 after
     `page.ReloadAsync()`. The second load usually runs on WebAssembly once `blazor-resource-hash` is persisted (see
     InteractiveAutoTests.cs:41-63). This covers both IA legs.

Notes for the implementer:
- Replace every fixed `WaitForTimeoutAsync(600/700)` with `WaitForLayoutToSettleAsync` (A8 helper) or an
  `Expect(...)` wait.
- No trusted cert is needed.
- The reconnect step needs the shared layout's reconnect UI id. Verify it in the Server `_Host.cshtml`/`App` before
  writing the selector, and fall back to waiting for `Blazor.reconnect` completion via `page.WaitForFunctionAsync`
  if it differs.

### (4) Tests
- **RED for CORS:** new `tests/Tempo.Blazor.Demo.Api.Tests/CorsPolicyTests.cs`. It uses
  `IClassFixture<WebApplicationFactory<Program>>` (pattern: ActivityEndpointTests.cs:8-13). The default factory
  environment is Development.
  - `Development_PreflightFromAnyLoopbackPort_IsAllowed`: `HttpRequestMessage(HttpMethod.Options, "/api/persons")`
    with headers `Origin: https://localhost:7116` and `Access-Control-Request-Method: GET`. Assert the response header
    `Access-Control-Allow-Origin == "https://localhost:7116"` and `Access-Control-Allow-Credentials == "true"`.
    **RED today.**
  - `Development_PreflightFromANonLoopbackOrigin_IsRefused`: `Origin: https://evil.example` gives no
    `Access-Control-Allow-Origin`.
  - `Production_OnlyDefaultsAndConfiguredOrigins_AreAllowed`:
    ```csharp
    factory.WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.UseSetting("Cors:AllowedOrigins:0", "https://ci.example:8443"); })
    ```
    7106 is allowed, `https://ci.example:8443` is allowed, `https://localhost:7116` is refused. If Production startup
    needs extra config, use `"Staging"`; the point is "not Development".
  - Unit tests for `DemoCorsPolicy.IsLoopbackOrigin`: `http://127.0.0.1:9000` and `https://[::1]:7116` → true;
    `https://localhost.evil.com` and `file:///x` → false.
- **E2E:** the six new top-layer tests (3 Server + 3 IA). The RED aspect is that they do not exist; these are
  verification tests, so they are expected green unless a render-mode defect exists. **A red one is a finding.**
- **Smoke:**
  ```
  $env:TM_E2E_SELF_HOST='false'; $env:TM_E2E_WASM_URL='https://localhost:7116'
  scripts/run-e2e-smoke.ps1
  ```
  Run it against a self-started WASM host on 7116 and the Development API. `Phase12_SaveFailure_…` must pass.

### (5) Verification
```
dotnet test tests/Tempo.Blazor.Demo.Api.Tests --filter "FullyQualifiedName~CorsPolicyTests"
$env:TM_E2E_TRACE_ON_FAILURE='false'
dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj --filter "FullyQualifiedName~OverlayTopLayerRenderModesE2ETests|FullyQualifiedName~ServerOverlayTopLayerE2ETests|FullyQualifiedName~InteractiveAutoOverlayTopLayerE2ETests"
dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj --filter "FullyQualifiedName~InteractiveAutoTests.Scheduler_ViewsWork"
```

### (6) Risk
- The CORS change widens the **demo** API in Development only; Production behaviour is unchanged (defaults plus
  explicit config).
- The demo API is not a package, so no CHANGELOG is needed. Optionally add a line in docs/e2e-test-lanes.md.

### (7) Effort
- CORS: **S**
- Server/IA E2E: **M** (the reconnect scenario is the uncertain part)

### (8) Open question
**Q2** (CORS scope).

---

## A11 — [ec0299b9 #4] E2E: the labelled TmCopyButton keeps its width across Copy → Copied

### (1) Status
**OPEN.**
- `AdditiveExtensionsE2ETests.CopyButton_ShowLabel_RendersTheLabel_AndKeepsATouchTarget`
  (tests/Tempo.Blazor.E2E/AdditiveExtensionsE2ETests.cs:147-175) asserts the visible label text and the 44 px height
  only. There is no width check.
- Commit `282fdd09` changed it to assert the visible label.

### (2) Mechanism under test
- `_copy-button.css:47-59`:
  - `.tm-copy-button-labels { display: grid }`;
  - both labels at `grid-area: 1/1`;
  - the inactive one is `visibility:hidden`, so the wider label reserves the width.
- Markup: TmCopyButton.razor:29-30.
- The state resets after `Task.Delay(2000)` (:71-72).
- Demo element: FeedbackPage.razor:982,
  `<TmCopyButton Text="npm install tempo-blazor" ShowLabel="true" data-testid="copy-button-labelled" />`.
- cs strings: "Kopírovat" / "Zkopírováno". These differ more than "Copy"/"Copied" and are a good second culture.

### (3) Exact change
Add to AdditiveExtensionsE2ETests.cs after :175:
```csharp
[TestMethod]
[DataRow(1280, 720, false, "en")]
[DataRow(390, 844, true, "en")]
[DataRow(320, 700, true, "cs")]
public async Task CopyButton_ShowLabel_KeepsItsWidth_AcrossCopyAndCopied(int width, int height, bool touch, string culture)
{
    var page = await OpenAsync("/feedback", width, height, touch, culture);
    var button = page.GetByTestId("copy-button-labelled");
    await button.ScrollIntoViewIfNeededAsync();
    await page.EvaluateAsync("() => document.fonts.ready");
    var idle = (await button.BoundingBoxAsync())!;

    await page.EvaluateAsync("() => { navigator.clipboard.writeText = () => Promise.resolve(); }");
    await button.ClickAsync();
    await Assertions.Expect(button).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-copy-button--copied"));
    var copied = (await button.BoundingBoxAsync())!;
    Assert.AreEqual(idle.Width, copied.Width, 0.5, $"Copy→Copied must not change the button width ({idle.Width} → {copied.Width})");
    Assert.AreEqual(idle.Height, copied.Height, 0.5, "nor its height");

    // The 2 s reset back to Copy keeps it too.
    await Assertions.Expect(button).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-copy-button--copied"), new() { Timeout = 5000 });
    Assert.AreEqual(idle.Width, (await button.BoundingBoxAsync())!.Width, 0.5, "Copied→Copy must not change the width either");
}
```
Extend `OpenAsync` (:34-49) with an optional `string culture = "en"` parameter. Before `NewPageAsync`, add the culture
init script used by OverlayRound3FixE2ETests.cs:33-37:
```csharp
await context.AddInitScriptAsync($"localStorage.setItem('tm-demo-culture', '{culture}'); document.cookie = 'tm-demo-culture={culture};path=/;max-age=31536000;samesite=lax';");
```
Also set `Locale = culture == "cs" ? "cs-CZ" : "en-US"`.

**Negative control** (proves the assertion can fail): a fourth test
`CopyButton_WidthAssertion_HasTeeth_WhenTheHiddenLabelStopsReservingSpace`. Before the click, inject:
```csharp
page.AddStyleTagAsync(new() { Content = ".tm-copy-button-label--hidden{display:none!important}" })
```
Then assert `Math.Abs(idle.Width - copied.Width) > 1`.

### (4) Tests
- The test above. It is expected green on the current CSS; it is a regression guard.
- The negative control proves it has teeth.

### (5) Verification
`dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj --filter "FullyQualifiedName~AdditiveExtensionsE2ETests.CopyButton"`

### (6) Risk
Test-only.

### (7) Effort
**S**

---

## Open questions (product/repo decisions only)

**Q1 — Repository EOL policy (A1 Step 2).**
- (a) Global `* text=auto eol=lf` + `eng/release-evidence/** -text`, plus a one-time working-tree refresh on each dev
  machine.
- (b) Narrow: `.github/workflows/*.yml`, `*.razor`, `*.cs` get `eol=lf` only.
- (c) No attribute change; only the test-side normalisation.

**Recommendation: (a).** History shows CRLF leaking into commits (98cc1c99), and the index is already LF for every
file except 13 evidence logs. The test normalisation (Step 1) is needed in every option.

**Q2 — Demo API CORS scope (A10).**
- (a) Development allows any loopback origin; defaults plus `Cors:AllowedOrigins` elsewhere.
- (b) Explicit list only (`Cors:AllowedOrigins` / env `TM_E2E_ALLOWED_ORIGINS`); every non-default port must be configured.
- (c) Development `AllowAnyOrigin` without credentials. This breaks the SignalR handshake, so it is not viable.

**Recommendation: (a).**

**Q3 — JSON doc bundle kind change (A2).** Expression-bodied members change from `"kind":"Field"` to `"Property"`
(~490 entries); positional record parameters are added.
- (a) Accept, with a CHANGELOG Documentation line.
- (b) Keep `Field` for back-compat and only add the missing members.

**Recommendation: (a).** `Field` is factually wrong.

**Q4 — CSS bundle freshness (A3).**
- (a) Add the touch+diff steps to `publish-nuget-org.yml` and guard both workflows via `RequiredReleaseMarkers`.
- (b) Additionally add a local unit test that ports the inline bundler. This duplicates the logic and gives little
  value, because a local build always regenerates the bundle.

**Recommendation: (a) only.**

**Q5 — MobileActionBar More-menu focus (A8).**
- (a) Fix the test only now: instant scroll, settle helper, `ToBeFocused` with diagnostics.
- (b) Also harden `ActionOverflowMenu`/`overlay.js` so the initial focus lands once a hidden (anchor-off-screen) panel
  becomes visible.

**Recommendation: (a) now and (b) as a recorded follow-up.** A real user can trigger the same race with keyboard
focus-scroll plus Enter, but it is rare.

---

## Summary table

| # | Item | Status | Root cause / key fact | Fix | Tests | Effort | CHANGELOG |
|---|---|---|---|---|---|---|---|
| A1 | 9e46d2da+17b6d82d env unit failures | OPEN (10); ReleaseContract ×2 ALREADY DONE (7d7823ae) | CRLF working copy (system autocrlf=true at clone, local=false later; 7 839 files w/crlf). Workflow regex `[ \t]*$` vs `\r`; TmTabs.razor CRLF adds 2 `\r` to the markup (baseline itself correct: LF-normalised = 1269 B, the frozen hash) | `ReleaseGateFilterTests.ReadRepoFile` normalises EOL; TmTabs test normalises CRLF before hashing; `.gitattributes * text=auto eol=lf` + `eng/release-evidence/** -text` (Q1) + one-time tree refresh | `TheWorkflowReader_SegmentsACrlfCopyLikeTheLfOriginal`, `TheBaselineHash_DoesNotDependOnTheCheckoutLineEndings` | S | no |
| A2 | 02dd6146 ExtractMembers | OPEN | `{ get;`-only regex; `IsMobile =>` emitted as Field; `CssModifier =>` (multi-line), `{ get =>`, positional params skipped | new `SourceDocParser.Members.cs` (partial): expression-bodied + accessor-bodied + positional record params, field-loop `=>` exclusion, regenerate all bundles | `SourceDocParserMemberTests` (10 cases incl. real TmLayoutContext + committed bundle) | M | yes (Documentation) |
| A3 | efdc933b CSS bundle freshness | PARTLY DONE (d7466f31, publish-nuget.yml only; bundle fresh today) | publish-nuget-org.yml lacks touch+diff; no guard on the steps | add both steps to org workflow + 2 `RequiredReleaseMarkers` + ordering test | `BothPublishWorkflows_CarryTheReleaseStepsThatOnlyExistInCi` (red until org updated), `TheBundleFreshnessCheck_RunsAfterTheBuild_AndTheTouchBeforeIt` | S | no |
| A4 | c51ddf81 #3 EOL commit test | OPEN | 975a75be is Demo-only, so vacuous; 98cc1c99 stored CRLF in 5 components (56 raw `[Parameter]` lines, 0 with flag) | swap sha + comment; `ChangeForCommit(sha, ignoreCrAtEol)` + raw-diff teeth arm | existing test + raw arm | S | no |
| A5 | ec0299b9 #3 icon registry race | OPEN (reproduced 2/2) | static `IconRegistry`, two `Reset()`ers, unlocked readers | `IconRegistryCollection` (`DisableParallelization = true`) on both classes | combined filter ×10 (+ optional reflection guard) | S | no |
| A6 | 4c92ba05 #1/#2 relative time | OPEN | snippet compares only the registration; formatter tests leak culture; no shared culture helper | `CultureScope` helper + refactor `Formatter/Cs/En/Fr`; sample class drops `private` and the commented copy; `ClassBlock` comparison | `Formatting_leaves_the_ambient_culture_as_it_found_it` (RED); extended snippet test | S | no |
| A7 | f4c27b20 #2 `}}` | OPEN | TmToolbarOverflowTests.cs:497 | split into two lines | build | S | no |
| A8 | 058c3487/835acf83#5/08a22db5#3 MAB flakes | OPEN | `html{scroll-behavior:smooth}`: AutoCard reads geometry in the same tick as smooth `scrollTo` + 200 ms sleeps; ForcedVariant (actually 1280×720) opens during the smooth focus scroll, so overlay.js hides the panel while the anchor is off-screen and `focus()` on the menuitem is a no-op | shared `WaitForLayoutToSettleAsync`, `behavior:'instant'`, `focus({preventScroll})` after instant scroll, `ToBeFocused` 15 s with diagnostics; optional product hardening (Q5) | 5× class runs with traces off; CPU-throttle variant | M | only if Q5 = b |
| A9 | 08a22db5 #1 thresholds shell | OPEN | thresholds shell binds no Open state, so its sheet can never close and :963/:986 pass trivially | bind LeftOpen/RightOpen (CollapsedPanels stays unbound) + mirror test | `…EscapeFromInsideTheThresholdsSheet_ClosesItAndLeavesTheMainSheetOpen` (RED) | S | no |
| A10a | 64114d91 IA Scheduler | ALREADY DONE (2/2 passed today, self-hosted 7108) | host simply was not started | none | n/a | – | no |
| A10b | 4770bcfb smoke CORS | OPEN | API CORS hard-codes 5010/7106 | `DemoCorsPolicy` + `SetIsOriginAllowed` (Development: any loopback; `Cors:AllowedOrigins` config) | `CorsPolicyTests` (RED for 7116) + smoke on 7116 | S | no |
| A10c | 3f1747c0 Server/IA top layer | OPEN | no Server/IA overlay E2E; trusted cert NOT needed (`IgnoreHTTPSErrors`) | `OverlayTopLayerRenderModesE2ETests.cs`: Server + IA classes; palette-over-drawer, toast re-raise, Server reconnect, IA both legs | 6 new E2E | M | no |
| A11 | ec0299b9 #4 copy width | OPEN | only text/height asserted | `CopyButton_ShowLabel_KeepsItsWidth_AcrossCopyAndCopied` (en/cs) + negative control | new E2E | S | no |
