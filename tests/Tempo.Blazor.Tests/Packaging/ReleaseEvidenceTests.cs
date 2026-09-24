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
        int passed = 0, int failed = 0, int skipped = 0, int total = 0,
        int serialResidualTotal = 0) => $$"""
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
          "artifactsPath": "fixture"
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

    private static string WriteEvidence(
        string directory, string commit, int serialResidualFailed = 0,
        int passed = 0, int failed = 0, int skipped = 0, int total = 0,
        int serialResidualTotal = 0)
    {
        string path = Path.Combine(directory, $"evidence-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path,
            EvidenceTemplate(commit, serialResidualFailed, passed, failed, skipped, total,
                serialResidualTotal));
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
            string baseline = WriteEvidence(fixtureDir, evidenceCommit);
            ReleaseScriptInputReadTests.ScriptResult ok = RunVerifier(worktree, baseline);
            Dump("baseline", ok);
            ok.Exit.Should().Be(0, $"baseline evidence over HEAD must pass ({ok.Combined})");

            // (2) serialResidualFailed=1 — a deterministic red can never ship.
            string residual = WriteEvidence(fixtureDir, evidenceCommit, serialResidualFailed: 1);
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
            string side = WriteEvidence(fixtureDir, sideCommit);
            ReleaseScriptInputReadTests.ScriptResult sideResult = RunVerifier(worktree, side);
            Dump("non-ancestor commit", sideResult);
            sideResult.Exit.Should().Be(1,
                $"evidence over a side-line commit must refuse ({sideResult.Combined})");

            // (4) a src/ change after the evidence commit breaks the staleness bound.
            string srcProbe = Path.Combine(worktree, "src", "Tempo.Blazor", "StalenessProbe.cs");
            File.WriteAllText(srcProbe, "// staleness probe — fixture only\n");
            CommitAll(worktree, "probe: src change after evidence");
            string staleSrc = WriteEvidence(fixtureDir, evidenceCommit);
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
            string staleTests = WriteEvidence(fixtureDir, evidenceCommit);
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
            string staleDoc = WriteEvidence(fixtureDir, evidenceCommit);
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
            string staleCi = WriteEvidence(fixtureDir, evidenceCommit);
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
    /// integer, unresolved-by-omission parallel reds (failed &gt; serialResidualTotal), and an
    /// internally inconsistent sum must each refuse — none may read as a green run.
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
                    serialResidualTotal: 1);
                ReleaseScriptInputReadTests.ScriptResult unverdictedResult =
                    RunVerifier(worktree, unverdicted);
                Dump("failed > serialResidualTotal", unverdictedResult);
                unverdictedResult.Exit.Should().Be(1,
                    "a parallel red never sent to serial re-measurement must refuse");

                // passed+failed+skipped != total: the record contradicts itself.
                string inconsistent = WriteEvidence(
                    fixtureDir, head, passed: 10, failed: 0, skipped: 0, total: 11);
                ReleaseScriptInputReadTests.ScriptResult inconsistentResult =
                    RunVerifier(worktree, inconsistent);
                Dump("sum mismatch", inconsistentResult);
                inconsistentResult.Exit.Should().Be(1,
                    "an internally inconsistent record must refuse");
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
            string evidence = WriteEvidence(fixtureDir, head);

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
