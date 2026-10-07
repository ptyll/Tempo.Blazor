using FluentAssertions;
using FluentAssertions.Execution;
using Xunit.Abstractions;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// Behavioural coverage for <c>eng/verify-release-evidence.sh</c> — the script half of
/// DEC-TEMPO-RELEASE-GATE's second clause (N207). The sibling presence assertions live in the
/// workflow comments and in <see cref="ReleaseGateFilterTests"/>-style reads; this class runs the
/// script itself, over a temporary <c>git worktree</c> of this repository, so each refusal can be
/// exercised against REAL git objects rather than a mocked log.
/// <para>
/// WHY A WORKTREE AND NOT THE REAL EVIDENCE FILE: the committed
/// <c>eng/release-evidence/e2e-full-run.json</c> is the store the recorded run writes — a test
/// that mutated it to reach a refusal would leave the live gate red, or worse, leave a fabricated
/// green behind. Every member here writes its evidence fixture under <c>$TEMP</c> and points
/// <c>RELEASE_EVIDENCE_PATH</c> at it, the same env-override the script declares for exactly this
/// purpose.
/// </para>
/// </summary>
public sealed class ReleaseEvidenceTests
{
    private readonly ITestOutputHelper _output;

    public ReleaseEvidenceTests(ITestOutputHelper output) => _output = output;

    private static string ScriptPath => Path.Combine(
        ReleaseScriptInputReadTests.FindRepoRoot(), "eng", "verify-release-evidence.sh");

    private static string EvidenceTemplate(string commit, int serialResidualFailed = 0,
        int passed = 1, int failed = 0, int skipped = 0, int total = 1,
        int serialResidualTotal = 0, string artifactsPath = "fixture",
        int hostRestarts = 0, bool selfHost = true) => $$"""
        {
          "commit": "{{commit}}",
          "verifiedDate": "2026-09-23",
          "verifiedBy": "ptyll",
          "runName": "fixture",
          "passed": {{passed}},
          "failed": {{failed}},
          "skipped": {{skipped}},
          "total": {{total}},
          "serialResidualTotal": {{serialResidualTotal}},
          "serialResidualFailed": {{serialResidualFailed}},
          "wallClock": "0h0m",
          "artifactsPath": "{{artifactsPath}}",
          "hostRestarts": {{hostRestarts}},
          "selfHost": {{(selfHost ? "true" : "false")}}
        }
        """;

    private static ReleaseScriptInputReadTests.ScriptResult RunVerifier(
        string workDir, string evidencePath, IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var env = new Dictionary<string, string> { ["RELEASE_EVIDENCE_PATH"] = evidencePath };
        if (extraEnv is not null)
        {
            foreach (KeyValuePair<string, string> pair in extraEnv)
            {
                env[pair.Key] = pair.Value;
            }
        }

        return ReleaseScriptInputReadTests.RunBash(ScriptPath, workDir, env);
    }

    private void Dump(string label, ReleaseScriptInputReadTests.ScriptResult result)
    {
        _output.WriteLine("==== " + label + " ====");
        _output.WriteLine(result.Combined);
    }

    /// <summary>
    /// Adds a detached worktree of HEAD into <paramref name="path"/> and returns it. Detached so
    /// commits made inside the fixture move the worktree's HEAD without touching any branch the
    /// suite or the user holds.
    /// </summary>
    private static void AddWorktree(string repoRoot, string path)
    {
        (int exit, string stdout, string stderr) = FullCloneFactAttribute.RunGit(
            repoRoot, "worktree", "add", "--detach", path, "HEAD");
        exit.Should().Be(0, $"git worktree add must succeed (stdout: {stdout}; stderr: {stderr})");
    }

    private static void RemoveWorktree(string repoRoot, string path)
    {
        try
        {
            FullCloneFactAttribute.RunGit(repoRoot, "worktree", "remove", "--force", path);
        }
        catch (Exception)
        {
            // best-effort cleanup — the temp dir is under $TEMP regardless
        }

        ReleaseScriptInputReadTests.TryDeleteDir(path);
    }

    /// <summary>
    /// CF19b — the verifier now requires artifactsPath INSIDE <c>eng/release-evidence/</c>: the
    /// default fixture therefore creates <c>eng/release-evidence/test-artifacts/</c> inside the
    /// passed worktree (untracked, so it never reaches the staleness diff) and writes the
    /// repo-relative path. <paramref name="worktree"/> may be null only for fixtures whose
    /// refusal fires before the artifactsPath clause (missing key, duplicate key, selfHost).
    /// </summary>
    private static string WriteEvidence(
        string directory, string commit, int serialResidualFailed = 0,
        int passed = 1, int failed = 0, int skipped = 0, int total = 1,
        int serialResidualTotal = 0, string? worktree = null,
        string? artifactsPath = null, int hostRestarts = 0,
        bool selfHost = true, bool writeHostWatchLog = false)
    {
        if (artifactsPath is null)
        {
            artifactsPath = "eng/release-evidence/test-artifacts";
            if (worktree is not null)
            {
                string dir = Path.Combine(
                    worktree, "eng", "release-evidence", "test-artifacts");
                Directory.CreateDirectory(dir);
                if (writeHostWatchLog)
                {
                    File.WriteAllText(
                        Path.Combine(dir, "host-watch.log"),
                        "# external host watch — fixture\nno restarts observed\n");
                }
            }
        }

        string path = Path.Combine(directory, $"evidence-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path,
            EvidenceTemplate(commit, serialResidualFailed, passed, failed, skipped, total,
                serialResidualTotal, artifactsPath, hostRestarts, selfHost));
        return path;
    }

    private static void CommitAll(string worktree, string message)
    {
        FullCloneFactAttribute.RunGit(worktree, "add", "-A");
        (int exit, _, string stderr) = FullCloneFactAttribute.RunGit(
            worktree,
            "-c", "user.email=release-evidence-tests@localhost",
            "-c", "user.name=release-evidence-tests",
            "commit", "-m", message);
        exit.Should().Be(0, $"fixture commit must succeed (stderr: {stderr})");
    }

    /// <summary>
    /// The schema file must exist and the script must be present — the Red of this step was the
    /// missing pair (asserting File.Exists over both), kept here as the presence half so the
    /// behavioural members below are never read as covering it.
    /// </summary>
    [Fact]
    public void ReleaseEvidence_AndVerifierScript_Exist()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        using (new AssertionScope())
        {
            File.Exists(Path.Combine(root, "eng", "release-evidence", "e2e-full-run.json"))
                .Should().BeTrue(
                    "eng/release-evidence/e2e-full-run.json is the store the recorded run writes; "
                    + "without it the workflow step has nothing to verify");
            File.Exists(ScriptPath).Should().BeTrue(
                "eng/verify-release-evidence.sh is the machine half of DEC-TEMPO-RELEASE-GATE's "
                + "second clause");
        }
    }

    /// <summary>
    /// Seven-step mutation proof, each arm over the REAL script — never a re-implemented check:
    /// (1) evidence at HEAD with a clean diff passes; (2) serialResidualFailed=1 refuses; (3) a
    /// commit that exists but is not an ancestor of HEAD refuses; (4) a src/ change after the
    /// evidence commit refuses on the staleness bound; (5) a NON-E2E test change
    /// (tests/Tempo.Blazor.Tests/) refuses — DEC-TEMPO-RELEASE-EVIDENCE-SCOPE invalidates on ANY
    /// tests/ change, bUnit included; (6) a CHANGELOG.md-only change passes; (7) a
    /// .github/workflows/ change passes — .github/ is inside the owner's allowed list. The
    /// allowlist holds in BOTH directions.
    /// </summary>
    [BashScriptFact]
    public void Verifier_SevenArmProof_OverATempWorktree()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        string worktree = Path.Combine(Path.GetTempPath(), $"tm-evidence-wt-{Guid.NewGuid():N}");
        string fixtureDir = Path.Combine(Path.GetTempPath(), $"tm-evidence-fx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);

        try
        {
            AddWorktree(root, worktree);
            string evidenceCommit = FullCloneFactAttribute.RunGit(worktree, "rev-parse", "HEAD")
                .StandardOutput;

            // (1) baseline — evidence names HEAD, nothing changed after it.
            string baseline = WriteEvidence(fixtureDir, evidenceCommit, worktree: worktree);
            ReleaseScriptInputReadTests.ScriptResult ok = RunVerifier(worktree, baseline);
            Dump("baseline", ok);
            ok.Exit.Should().Be(0, $"baseline evidence over HEAD must pass ({ok.Combined})");

            // (2) serialResidualFailed=1 — a deterministic red can never ship.
            string residual = WriteEvidence(fixtureDir, evidenceCommit, serialResidualFailed: 1, worktree: worktree);
            ReleaseScriptInputReadTests.ScriptResult residualResult = RunVerifier(worktree, residual);
            Dump("serialResidualFailed=1", residualResult);
            residualResult.Exit.Should().Be(1,
                $"a deterministic serial red must refuse ({residualResult.Combined})");

            // (3) a commit that EXISTS but is not an ancestor of HEAD — made via commit-tree so
            // no branch or worktree HEAD ever points at it.
            string headTree = FullCloneFactAttribute.RunGit(worktree, "rev-parse", "HEAD^{tree}")
                .StandardOutput;
            string sideCommit = FullCloneFactAttribute.RunGit(
                    worktree, "commit-tree", headTree, "-m", "side-line")
                .StandardOutput;
            string side = WriteEvidence(fixtureDir, sideCommit, worktree: worktree);
            ReleaseScriptInputReadTests.ScriptResult sideResult = RunVerifier(worktree, side);
            Dump("non-ancestor commit", sideResult);
            sideResult.Exit.Should().Be(1,
                $"evidence over a side-line commit must refuse ({sideResult.Combined})");

            // (4) a src/ change after the evidence commit breaks the staleness bound.
            string srcProbe = Path.Combine(worktree, "src", "Tempo.Blazor", "StalenessProbe.cs");
            File.WriteAllText(srcProbe, "// staleness probe — fixture only\n");
            CommitAll(worktree, "probe: src change after evidence");
            string staleSrc = WriteEvidence(fixtureDir, evidenceCommit, worktree: worktree);
            ReleaseScriptInputReadTests.ScriptResult staleSrcResult = RunVerifier(worktree, staleSrc);
            Dump("src/ change after evidence", staleSrcResult);
            staleSrcResult.Exit.Should().Be(1,
                $"a src/ change after the evidence commit must refuse ({staleSrcResult.Combined})");
            staleSrcResult.Combined.Should().Contain("src/Tempo.Blazor/StalenessProbe.cs",
                "the refusal must name the offending path");

            // (5) undo the src/ probe and change a NON-E2E test file — the owner's scope decision
            // invalidates evidence on ANY tests/ change, bUnit included; an implementation that
            // only watches tests/Tempo.Blazor.E2E/ under-enforces the recorded policy.
            FullCloneFactAttribute.RunGit(worktree, "reset", "--hard", evidenceCommit);
            string bunitProbe = Path.Combine(
                worktree, "tests", "Tempo.Blazor.Tests", "StalenessProbeTests.cs");
            File.WriteAllText(bunitProbe, "// staleness probe — fixture only\n");
            CommitAll(worktree, "probe: bUnit test change after evidence");
            string staleTests = WriteEvidence(fixtureDir, evidenceCommit, worktree: worktree);
            ReleaseScriptInputReadTests.ScriptResult staleTestsResult =
                RunVerifier(worktree, staleTests);
            Dump("tests/Tempo.Blazor.Tests change after evidence", staleTestsResult);
            staleTestsResult.Exit.Should().Be(1,
                $"a non-E2E tests/ change after the evidence commit must refuse — "
                + $"DEC-TEMPO-RELEASE-EVIDENCE-SCOPE covers bUnit explicitly "
                + $"({staleTestsResult.Combined})");
            staleTestsResult.Combined.Should().Contain(
                "tests/Tempo.Blazor.Tests/StalenessProbeTests.cs",
                "the refusal must name the offending path");

            // (6) undo the tests/ probe and change only CHANGELOG.md — *.md is allowed.
            FullCloneFactAttribute.RunGit(worktree, "reset", "--hard", evidenceCommit);
            string changelog = Path.Combine(worktree, "CHANGELOG.md");
            File.AppendAllText(changelog, "\n<!-- staleness probe — fixture only -->\n");
            CommitAll(worktree, "probe: changelog-only change after evidence");
            string staleDoc = WriteEvidence(fixtureDir, evidenceCommit, worktree: worktree);
            ReleaseScriptInputReadTests.ScriptResult staleDocResult = RunVerifier(worktree, staleDoc);
            Dump("CHANGELOG-only change after evidence", staleDocResult);
            staleDocResult.Exit.Should().Be(0,
                $"a CHANGELOG.md-only diff after the evidence commit must pass — *.md is inside "
                + $"the allowed list ({staleDocResult.Combined})");

            // (7) undo the changelog probe and change a workflow file — .github/ is inside the
            // owner's allowed list (CI definition is not compiled into the package).
            FullCloneFactAttribute.RunGit(worktree, "reset", "--hard", evidenceCommit);
            string workflow = Path.Combine(
                worktree, ".github", "workflows", "publish-nuget-org.yml");
            File.AppendAllText(workflow, "\n# staleness probe — fixture only\n");
            CommitAll(worktree, "probe: workflow change after evidence");
            string staleCi = WriteEvidence(fixtureDir, evidenceCommit, worktree: worktree);
            ReleaseScriptInputReadTests.ScriptResult staleCiResult = RunVerifier(worktree, staleCi);
            Dump(".github/workflows change after evidence", staleCiResult);
            staleCiResult.Exit.Should().Be(0,
                $"a .github/ diff after the evidence commit must pass — the owner allowlists "
                + $"CI definition files ({staleCiResult.Combined})");
        }
        finally
        {
            RemoveWorktree(root, worktree);
            ReleaseScriptInputReadTests.TryDeleteDir(fixtureDir);
        }
    }

    /// <summary>
    /// The fail-closed input arms: a missing evidence file, a missing key, a count that is not an
    /// integer, unresolved-by-omission parallel reds (failed &gt; serialResidualTotal), an
    /// internally inconsistent sum, a vacuous all-zero record (F2), an artifactsPath naming
    /// a directory that does not exist (F2), a nonzero hostRestarts count and a record that
    /// omits the hostRestarts key entirely (F3, N209 wired into the gate) must each refuse —
    /// none may read as a green run.
    /// </summary>
    [BashScriptFact]
    public void Verifier_RefusesUnreadableOrInconsistentEvidence()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        string worktree = Path.Combine(Path.GetTempPath(), $"tm-evidence-wt-{Guid.NewGuid():N}");
        string fixtureDir = Path.Combine(Path.GetTempPath(), $"tm-evidence-fx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);

        try
        {
            AddWorktree(root, worktree);
            string head = FullCloneFactAttribute.RunGit(worktree, "rev-parse", "HEAD").StandardOutput;
            using (new AssertionScope())
            {
                ReleaseScriptInputReadTests.ScriptResult missing = RunVerifier(
                    worktree, Path.Combine(fixtureDir, "does-not-exist.json"));
                Dump("missing file", missing);
                missing.Exit.Should().Be(1, "a missing evidence file must refuse");

                string missingKey = Path.Combine(fixtureDir, "missing-key.json");
                File.WriteAllText(missingKey, "{ \"commit\": \"" + head + "\" }\n");
                ReleaseScriptInputReadTests.ScriptResult missingKeyResult =
                    RunVerifier(worktree, missingKey);
                Dump("missing key", missingKeyResult);
                missingKeyResult.Exit.Should().Be(1, "a partial record must refuse");
                missingKeyResult.Combined.Should().Contain("verifiedDate",
                    "the refusal must name the first absent key");

                // failed > serialResidualTotal: a parallel red nobody re-measured serially.
                string unverdicted = WriteEvidence(
                    fixtureDir, head, passed: 99, failed: 2, skipped: 0, total: 101,
                    serialResidualTotal: 1, worktree: worktree);
                ReleaseScriptInputReadTests.ScriptResult unverdictedResult =
                    RunVerifier(worktree, unverdicted);
                Dump("failed > serialResidualTotal", unverdictedResult);
                unverdictedResult.Exit.Should().Be(1,
                    "a parallel red never sent to serial re-measurement must refuse");

                // passed+failed+skipped != total: the record contradicts itself.
                string inconsistent = WriteEvidence(
                    fixtureDir, head, passed: 10, failed: 0, skipped: 0, total: 11,
                    worktree: worktree);
                ReleaseScriptInputReadTests.ScriptResult inconsistentResult =
                    RunVerifier(worktree, inconsistent);
                Dump("sum mismatch", inconsistentResult);
                inconsistentResult.Exit.Should().Be(1,
                    "an internally inconsistent record must refuse");

                // F2: a vacuous record — every count 0 — used to pass every clause and exit 0
                // while describing no suite at all.
                string vacuous = WriteEvidence(
                    fixtureDir, head, passed: 0, failed: 0, skipped: 0, total: 0,
                    worktree: worktree);
                ReleaseScriptInputReadTests.ScriptResult vacuousResult =
                    RunVerifier(worktree, vacuous);
                Dump("vacuous record", vacuousResult);
                vacuousResult.Exit.Should().Be(1,
                    "an all-zero record is not a recorded run and must refuse");

                // F2: artifactsPath must name a directory that exists where the gate runs —
                // required-nonempty only proved a string was written. Inside the store (so the
                // CF19b location rule passes) but absent on disk — reaches the exists-clause.
                string missingArtifacts = WriteEvidence(
                    fixtureDir, head,
                    artifactsPath: "eng/release-evidence/no-such-artifacts-dir");
                ReleaseScriptInputReadTests.ScriptResult missingArtifactsResult =
                    RunVerifier(worktree, missingArtifacts);
                Dump("nonexistent artifactsPath", missingArtifactsResult);
                missingArtifactsResult.Exit.Should().Be(1,
                    "an artifactsPath that does not exist must refuse");
                missingArtifactsResult.Combined.Should().Contain("artifactsPath",
                    "the refusal must name the offending key");

                // F3: a run whose demo hosts needed resurrecting is not a clean green — the N209
                // counter wired into the gate refuses any nonzero hostRestarts count.
                string rescued = WriteEvidence(fixtureDir, head, hostRestarts: 2, worktree: worktree);
                ReleaseScriptInputReadTests.ScriptResult rescuedResult =
                    RunVerifier(worktree, rescued);
                Dump("hostRestarts=2", rescuedResult);
                rescuedResult.Exit.Should().Be(1,
                    "a recorded run with resurrected hosts must refuse — a rescue is a finding, "
                    + "not a clean green");

                // F3, fail-closed half: a record that simply omits hostRestarts refuses on the
                // missing key like every other required key — absence is not a zero.
                string noRestartKey = Path.Combine(fixtureDir, "no-restart-key.json");
                File.WriteAllText(
                    noRestartKey,
                    $$"""
                    {
                      "commit": "{{head}}",
                      "verifiedDate": "2026-09-23",
                      "verifiedBy": "ptyll",
                      "runName": "fixture",
                      "passed": 1,
                      "failed": 0,
                      "skipped": 0,
                      "total": 1,
                      "serialResidualTotal": 0,
                      "serialResidualFailed": 0,
                      "wallClock": "0h0m",
                      "artifactsPath": "{{fixtureDir.Replace('\\', '/') }}",
                      "selfHost": true
                    }
                    """ + "\n");
                ReleaseScriptInputReadTests.ScriptResult noRestartKeyResult =
                    RunVerifier(worktree, noRestartKey);
                Dump("missing hostRestarts key", noRestartKeyResult);
                noRestartKeyResult.Exit.Should().Be(1,
                    "a record without hostRestarts must refuse on the missing key");
                noRestartKeyResult.Combined.Should().Contain("hostRestarts",
                    "the refusal must name the absent key");
            }
        }
        finally
        {
            RemoveWorktree(root, worktree);
            ReleaseScriptInputReadTests.TryDeleteDir(fixtureDir);
        }
    }

    /// <summary>
    /// Fáze 20E review F1 — the staleness bound used to read <c>git diff</c> through a process
    /// substitution, so a git that fails on <c>diff</c> produced an EMPTY changed-path list and
    /// the script exited 0: the bound silently skipped itself. This arm puts a fake <c>git</c>
    /// on PATH that exits 128 on <c>diff</c> (delegating everything else to the real binary) and
    /// requires the verifier to go nonzero; the unshadowed control over the same evidence stays
    /// green, so the red is attributed to the broken read and not to the fixture.
    /// </summary>
    [BashScriptFact]
    public void Verifier_RefusesWhenTheGitDiffReadFails()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        string worktree = Path.Combine(Path.GetTempPath(), $"tm-evidence-wt-{Guid.NewGuid():N}");
        string fixtureDir = Path.Combine(Path.GetTempPath(), $"tm-evidence-fx-{Guid.NewGuid():N}");
        string fakeBin = Path.Combine(Path.GetTempPath(), $"tm-fake-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);
        Directory.CreateDirectory(fakeBin);

        try
        {
            AddWorktree(root, worktree);
            string head = FullCloneFactAttribute.RunGit(worktree, "rev-parse", "HEAD").StandardOutput;
            string evidence = WriteEvidence(fixtureDir, head, worktree: worktree);

            WriteFakeGit(Path.Combine(fakeBin, "git"));
            var shadowedEnv = new Dictionary<string, string>
            {
                ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            };

            ReleaseScriptInputReadTests.ScriptResult shadowed =
                RunVerifier(worktree, evidence, shadowedEnv);
            Dump("PATH-shadowed git diff", shadowed);
            shadowed.Exit.Should().NotBe(0,
                "a git that exits 128 on 'diff' must turn the staleness bound red — the process "
                + $"substitution version swallowed it and exited 0 ({shadowed.Combined})");

            ReleaseScriptInputReadTests.ScriptResult control = RunVerifier(worktree, evidence);
            Dump("unshadowed control", control);
            control.Exit.Should().Be(0,
                $"the same evidence over a healthy git must stay green ({control.Combined})");
        }
        finally
        {
            RemoveWorktree(root, worktree);
            ReleaseScriptInputReadTests.TryDeleteDir(fixtureDir);
            ReleaseScriptInputReadTests.TryDeleteDir(fakeBin);
        }
    }

    /// <summary>
    /// CF15b — a required key occurring twice is two records wearing one file: the flat-JSON
    /// reader returns the FIRST match, so a second <c>hostRestarts</c> line could disagree with
    /// the number the gate actually checked. The verifier must refuse rather than arbitrate.
    /// The refusal fires before any git question, so no worktree is needed.
    /// </summary>
    [BashScriptFact]
    public void Verifier_RefusesDuplicateKey()
    {
        string fixtureDir = Path.Combine(Path.GetTempPath(), $"tm-evidence-dup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);

        try
        {
            string path = Path.Combine(fixtureDir, "evidence.json");
            string json = EvidenceTemplate("irrelevant", artifactsPath: fixtureDir.Replace('\\', '/'));
            json = json.Replace(
                "\"hostRestarts\": 0,\n",
                "\"hostRestarts\": 0,\n  \"hostRestarts\": 3,\n",
                StringComparison.Ordinal);
            json.Should().Contain("\"hostRestarts\": 3", "the fixture must carry the duplicate");
            File.WriteAllText(path, json);

            ReleaseScriptInputReadTests.ScriptResult result = RunVerifier(fixtureDir, path);
            Dump("duplicate hostRestarts", result);

            using (new AssertionScope())
            {
                result.Exit.Should().NotBe(0,
                    "a duplicated required key must refuse — the reviewer reads one number and "
                    + $"the gate checks another ({result.Combined})");
                result.Combined.Should().Contain("duplicate",
                    $"the refusal must name the shape it rejected ({result.Combined})");
            }
        }
        finally
        {
            ReleaseScriptInputReadTests.TryDeleteDir(fixtureDir);
        }
    }

    /// <summary>
    /// CF15a — the evidence <c>total</c> counts distinct test names, not testId elements: the
    /// convention must be written where the reader of the number finds it, in the script's own
    /// header, so a future evidence writer cannot fill it with the other count.
    /// </summary>
    [Fact]
    public void VerifierHeader_DefinesTotalAsDistinctTestNames()
    {
        string script = File.ReadAllText(ScriptPath);
        script.ToLowerInvariant().Should().Contain(
            "distinct test names",
            "the script header must pin total = distinct test names in the TRX (data-row "
            + "instances sharing a name count once), not the count of testId elements — "
            + "otherwise the same field means different things to the recorder and the gate");
    }

    /// <summary>
    /// CF19a — a <c>git mv src/X docs/X</c> is a src/ change wearing a docs/ costume. With
    /// rename detection on, <c>git diff --name-only</c> prints only the rename TARGET, so the
    /// allowlist saw a pure docs/ diff and the compiled side vanished. The verifier diffs with
    /// <c>--no-renames</c>, which exposes the deletion under src/ and must refuse naming it.
    /// </summary>
    [BashScriptFact]
    public void Verifier_RefusesRenameOutOfSrc()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        string worktree = Path.Combine(Path.GetTempPath(), $"tm-evidence-wt-{Guid.NewGuid():N}");
        string fixtureDir = Path.Combine(Path.GetTempPath(), $"tm-evidence-fx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);

        try
        {
            AddWorktree(root, worktree);
            string evidenceCommit =
                FullCloneFactAttribute.RunGit(worktree, "rev-parse", "HEAD").StandardOutput;

            // A real move: tracked compiled source relocated under docs/ — identical content,
            // so rename detection collapses it unless the diff runs with --no-renames.
            FullCloneFactAttribute.RunGit(
                worktree, "mv", "src/Tempo.Blazor/_Imports.razor", "docs/_Imports-moved.razor");
            CommitAll(worktree, "probe: move compiled source under docs/");

            // With -M the diff reports ONLY docs/_Imports-moved.razor — prove the fixture is a
            // genuine rename so the refusal is attributable to --no-renames, not a stray probe.
            var (_, renameView, _) = FullCloneFactAttribute.RunGit(
                worktree, "diff", "--name-only", evidenceCommit, "HEAD");
            renameView.Trim().Should().Be("docs/_Imports-moved.razor",
                "rename detection must collapse the move to its docs/ target — otherwise this "
                + "arm proves nothing about --no-renames");

            string evidence = WriteEvidence(fixtureDir, evidenceCommit, worktree: worktree);
            ReleaseScriptInputReadTests.ScriptResult result = RunVerifier(worktree, evidence);
            Dump("rename src→docs after evidence", result);

            using (new AssertionScope())
            {
                result.Exit.Should().Be(1,
                    $"a git mv out of src/ must refuse — the moved file still compiles out of the "
                    + $"package ({result.Combined})");
                result.Combined.Should().Contain("src/Tempo.Blazor/_Imports.razor",
                    $"the refusal must name the src/ side of the rename ({result.Combined})");
            }
        }
        finally
        {
            RemoveWorktree(root, worktree);
            ReleaseScriptInputReadTests.TryDeleteDir(fixtureDir);
        }
    }

    /// <summary>
    /// CF19b — artifactsPath outside <c>eng/release-evidence/</c> is unauditable evidence: an
    /// absolute path, a gitignored TestResults dir, or a <c>..</c> traversal all point at files
    /// the publish job's fresh checkout cannot see. Existing-but-outside and traversing paths
    /// must both refuse naming artifactsPath.
    /// </summary>
    [BashScriptFact]
    public void Verifier_RefusesArtifactsPathOutsideEvidenceStore()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        string worktree = Path.Combine(Path.GetTempPath(), $"tm-evidence-wt-{Guid.NewGuid():N}");
        string fixtureDir = Path.Combine(Path.GetTempPath(), $"tm-evidence-fx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);

        try
        {
            AddWorktree(root, worktree);
            string head = FullCloneFactAttribute.RunGit(worktree, "rev-parse", "HEAD").StandardOutput;

            using (new AssertionScope())
            {
                // Outside the store, and it EXISTS — the location rule must refuse before the
                // existence rule can ever make an outside path look acceptable.
                string outside = WriteEvidence(
                    fixtureDir, head, artifactsPath: fixtureDir.Replace('\\', '/'));
                ReleaseScriptInputReadTests.ScriptResult outsideResult =
                    RunVerifier(worktree, outside);
                Dump("artifactsPath outside store", outsideResult);
                outsideResult.Exit.Should().Be(1,
                    "an artifactsPath outside eng/release-evidence/ must refuse even when it exists");
                outsideResult.Combined.Should().Contain("artifactsPath",
                    "the refusal must name the offending key");

                // A traversal that still lexically starts with the store prefix.
                string traversal = WriteEvidence(
                    fixtureDir, head,
                    artifactsPath: "eng/release-evidence/../../outside");
                ReleaseScriptInputReadTests.ScriptResult traversalResult =
                    RunVerifier(worktree, traversal);
                Dump("artifactsPath traversal", traversalResult);
                traversalResult.Exit.Should().Be(1,
                    "a .. traversal under the store prefix must refuse");
                traversalResult.Combined.Should().Contain("artifactsPath",
                    "the refusal must name the offending key");

                // The control: the standard fixture inside the store stays green.
                string inside = WriteEvidence(fixtureDir, head, worktree: worktree);
                ReleaseScriptInputReadTests.ScriptResult insideResult =
                    RunVerifier(worktree, inside);
                Dump("artifactsPath inside store (control)", insideResult);
                insideResult.Exit.Should().Be(0,
                    $"an artifactsPath inside eng/release-evidence/ must pass "
                    + $"({insideResult.Combined})");
            }
        }
        finally
        {
            RemoveWorktree(root, worktree);
            ReleaseScriptInputReadTests.TryDeleteDir(fixtureDir);
        }
    }

    /// <summary>
    /// CF19d — <c>selfHost</c> declares whether hostRestarts is a measured count or a vacuous
    /// claim. Missing key, non-boolean value, and <c>selfHost=false</c> without the external
    /// host's <c>host-watch.log</c> in artifactsPath must each refuse; false WITH the log and
    /// true must pass.
    /// </summary>
    [BashScriptFact]
    public void Verifier_RefusesMissingOrExternalSelfHost()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        string worktree = Path.Combine(Path.GetTempPath(), $"tm-evidence-wt-{Guid.NewGuid():N}");
        string fixtureDir = Path.Combine(Path.GetTempPath(), $"tm-evidence-fx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);

        try
        {
            AddWorktree(root, worktree);
            string head = FullCloneFactAttribute.RunGit(worktree, "rev-parse", "HEAD").StandardOutput;

            using (new AssertionScope())
            {
                // Missing key — the required-keys loop names it like every other absent key.
                string missingKey = Path.Combine(fixtureDir, "no-selfhost.json");
                File.WriteAllText(
                    missingKey,
                    $$"""
                    {
                      "commit": "{{head}}",
                      "verifiedDate": "2026-09-23",
                      "verifiedBy": "ptyll",
                      "runName": "fixture",
                      "passed": 1,
                      "failed": 0,
                      "skipped": 0,
                      "total": 1,
                      "serialResidualTotal": 0,
                      "serialResidualFailed": 0,
                      "wallClock": "0h0m",
                      "artifactsPath": "eng/release-evidence/test-artifacts",
                      "hostRestarts": 0
                    }
                    """ + "\n");
                ReleaseScriptInputReadTests.ScriptResult missingResult =
                    RunVerifier(worktree, missingKey);
                Dump("missing selfHost", missingResult);
                missingResult.Exit.Should().Be(1,
                    "a record without selfHost must refuse — absence is not a boolean");
                missingResult.Combined.Should().Contain("selfHost",
                    "the refusal must name the absent key");

                // A non-boolean value — "external" is prose, not a declaration.
                string prose = WriteEvidence(fixtureDir, head, worktree: worktree);
                string proseJson = File.ReadAllText(prose).Replace(
                    "\"selfHost\": true", "\"selfHost\": \"external\"", StringComparison.Ordinal);
                File.WriteAllText(prose, proseJson);
                ReleaseScriptInputReadTests.ScriptResult proseResult =
                    RunVerifier(worktree, prose);
                Dump("selfHost=external", proseResult);
                proseResult.Exit.Should().Be(1,
                    "a selfHost that is not true/false must refuse");
                proseResult.Combined.Should().Contain("selfHost",
                    "the refusal must name the offending key");

                // selfHost=false WITHOUT the watch-log — hostRestarts:0 is vacuous here.
                string noLog = WriteEvidence(fixtureDir, head, worktree: worktree, selfHost: false);
                ReleaseScriptInputReadTests.ScriptResult noLogResult =
                    RunVerifier(worktree, noLog);
                Dump("selfHost=false without host-watch.log", noLogResult);
                noLogResult.Exit.Should().Be(1,
                    "externally managed hosts without host-watch.log must refuse — "
                    + "hostRestarts:0 is vacuous when the suite did not manage the hosts");
                noLogResult.Combined.Should().Contain("host-watch.log",
                    "the refusal must name the missing artifact");

                // selfHost=false WITH the watch-log — the external evidence substitutes for the
                // suite-owned counter.
                string withLog = WriteEvidence(
                    fixtureDir, head, worktree: worktree, selfHost: false,
                    writeHostWatchLog: true);
                ReleaseScriptInputReadTests.ScriptResult withLogResult =
                    RunVerifier(worktree, withLog);
                Dump("selfHost=false with host-watch.log", withLogResult);
                withLogResult.Exit.Should().Be(0,
                    $"externally managed hosts WITH host-watch.log must pass "
                    + $"({withLogResult.Combined})");

                // Control: selfHost=true keeps meaning what it always meant.
                string suiteOwned = WriteEvidence(fixtureDir, head, worktree: worktree);
                ReleaseScriptInputReadTests.ScriptResult suiteResult =
                    RunVerifier(worktree, suiteOwned);
                Dump("selfHost=true (control)", suiteResult);
                suiteResult.Exit.Should().Be(0,
                    $"selfHost=true must pass ({suiteResult.Combined})");
            }
        }
        finally
        {
            RemoveWorktree(root, worktree);
            ReleaseScriptInputReadTests.TryDeleteDir(fixtureDir);
        }
    }

    /// <summary>
    /// CF19c — the verifier step must sit INSIDE the publish job of each workflow, not merely
    /// somewhere in the file: a call that only ran in build-and-test would verify a commit the
    /// publish job later rebuilds without gating it. The mutation arm strips the call from the
    /// publish segment and requires the check to flag it.
    /// </summary>
    [Fact]
    public void ReleaseEvidenceVerifier_RunsInsideThePublishJob()
    {
        foreach (string relative in ReleaseGateFilterTests.WorkflowRelativePaths)
        {
            IReadOnlyList<(string Name, string Body)> jobs =
                ReleaseGateFilterTests.JobSegments(
                    ReleaseGateFilterTests.ReadWorkflowCode(relative));
            jobs.Where(job => job.Name == "publish").Should().ContainSingle(
                $"{relative} must have exactly one publish job for this check to scope to");
            jobs.Single(job => job.Name == "publish").Body.Should().Contain(
                "bash eng/verify-release-evidence.sh",
                $"{relative}: the release-evidence verifier must run inside the publish job — "
                + "anywhere else it measures a commit the publish step then rebuilds ungated");
        }
    }

    /// <summary>The mutation arm of <see cref="ReleaseEvidenceVerifier_RunsInsideThePublishJob"/>.</summary>
    [Fact]
    public void ReleaseEvidenceVerifier_OutsideThePublishJob_IsDetected()
    {
        string healthy = ReleaseGateFilterTests.ReadWorkflowCode(
            ReleaseGateFilterTests.WorkflowRelativePaths[0]);
        string mutated = healthy.Replace(
            "run: bash eng/verify-release-evidence.sh",
            "run: echo evidence skipped",
            StringComparison.Ordinal);
        mutated.Should().NotBe(healthy,
            "the fixture must actually remove the verifier call from the publish job");

        IReadOnlyList<(string Name, string Body)> mutatedJobs =
            ReleaseGateFilterTests.JobSegments(mutated);
        mutatedJobs.Single(job => job.Name == "publish").Body.Should().NotContain(
            "bash eng/verify-release-evidence.sh",
            "once the call leaves the publish segment the check above goes red — "
            + "a verifier invoked only in build-and-test would produce the same signature");
    }

    /// <summary>
    /// The PATH-shadow fixture for <see cref="Verifier_RefusesWhenTheGitDiffReadFails"/>: fails on
    /// <c>git diff</c> exactly the way the measured mutation did, and forwards every other
    /// subcommand to the first real <c>git</c> found on PATH outside its own directory — so
    /// cat-file/merge-base still answer honestly and only the diff read is broken.
    /// </summary>
    private static void WriteFakeGit(string path)
    {
        File.WriteAllText(path, """
            #!/usr/bin/env bash
            set -u
            if [[ "${1:-}" == "diff" ]]; then
              echo "fake-git: refusing diff (F1 mutation)" >&2
              exit 128
            fi
            self_dir="$(cd "$(dirname "$0")" && pwd)"
            IFS=':' read -ra dirs <<<"${PATH:-}"
            real=""
            for d in "${dirs[@]}"; do
              [[ "$d" == "$self_dir" ]] && continue
              if [[ -x "$d/git" ]]; then real="$d/git"; break; fi
            done
            [[ -n "$real" ]] || { echo "fake-git: real git not found" >&2; exit 127; }
            exec "$real" "$@"
            """);
        ReleaseScriptInputReadTests.MakeExecutable(path);
    }

    /// <summary>
    /// The publish job in BOTH workflows must run the verifier — a step present in only one file
    /// is the same class of hole as a filter clause that is: the release goes out through
    /// whichever workflow fired.
    /// </summary>
    [Fact]
    public void BothPublishWorkflows_RunTheReleaseEvidenceVerifier()
    {
        foreach (string relative in ReleaseGateFilterTests.WorkflowRelativePaths)
        {
            string code = ReleaseGateFilterTests.ReadWorkflowCode(relative);
            code.Should().Contain(
                "bash eng/verify-release-evidence.sh",
                $"{relative} must run the release-evidence verifier in the publish job — without "
                + "it the second clause of DEC-TEMPO-RELEASE-GATE is a comment, not a gate");
        }
    }
}
