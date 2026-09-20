using System.ComponentModel;
using System.Diagnostics;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// A <c>[Fact]</c> over a member that asks <c>git cat-file</c> about commits that may be older than
/// HEAD — skipped with a named reason when the clone in front of it cannot answer that question at
/// all: a SHALLOW clone (<c>git rev-parse --is-shallow-repository</c> = <c>true</c>), a tree with no
/// <c>.git</c> (a tarball or an exported working copy), or a machine with no <c>git</c> binary.
/// <para>
/// THE DEFECT THIS WAS WRITTEN AGAINST, measured rather than imagined: the publish workflows'
/// <c>actions/checkout@v4</c> defaulted to <c>fetch-depth: 1</c>, and
/// <see cref="ChangelogReferencesExistingCommitsTests"/> then read exit 128 from
/// <c>git cat-file -e &lt;id&gt;^{commit}</c> on every commit beyond the shallow boundary — a red
/// <c>build-and-test</c> that said "the changelog cites a commit that does not exist" about a clone
/// that simply did not carry it. The workflows now fetch <c>fetch-depth: 0</c>, so on CI the guard
/// measures the changelog; this attribute is the other half of the same fix, for every OTHER clone
/// the suite may run in — a local <c>--depth 1</c>, a source tarball, a container with a working
/// tree and no git.
/// </para>
/// <para>
/// WHY A SKIP AND NOT A PASS: a member that returned early would be reported as PASSED, and "the
/// changelog's commit ids resolve" would sit in the <c>.trx</c> indistinguishable from a run that
/// asked git anything. A skip is a third outcome the runner and a reviewer both read, and the reason
/// names what was not measured. The three skip states are also deliberately NOT folded into the
/// decorated members' results: on a FULL clone a <c>cat-file</c> exit 128 is still the guard's
/// ordinary red, because there the clone can answer and the id genuinely resolves to no commit —
/// "the clone cannot know" is a claim only these three states get to make.
/// </para>
/// <para>
/// A PROBE ANSWER THAT IS NEITHER SKIP NOR RUN does not exist here by design: when <c>.git</c> is
/// present, git answers, and the clone reports full, this probe returns null and the member runs —
/// whatever <c>cat-file</c> then reports is the result, not an opinion about it. A rev-parse that
/// itself fails (<c>.git</c> present but unanswerable) is likewise handed to the member rather than
/// turned into a skip: the member's own git calls then produce the failure evidence, which is where
/// a broken instrument belongs.
/// </para>
/// <para>
/// THE MECHANISM — an overridden <c>Skip</c> getter, why that is the only skip this runner honours,
/// and why a throwing probe deliberately does not skip — lives once in
/// <see cref="ProbeDecidedFactAttribute"/> and is not repeated here. This class supplies only the
/// question: can this clone say whether an arbitrary commit exists.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FullCloneFactAttribute : ProbeDecidedFactAttribute
{
    /// <inheritdoc/>
    protected override string? ProbeSkipReason() => IncompleteCloneSkipReason();

    /// <summary>
    /// The reason a commit-resolution guard cannot run in this clone, or null when it can.
    /// <c>internal</c> rather than private so a member that wants to tripwire on the same verdict
    /// reads the same string rather than re-deciding it.
    /// </summary>
    internal static string? IncompleteCloneSkipReason()
    {
        var repositoryRoot = ReleaseScriptInputReadTests.FindRepoRoot();

        // `.git` may be a FILE, not a directory — a linked worktree or a submodule writes
        // `gitdir: <path>` into one — so the absence check must cover both shapes.
        var gitPath = Path.Combine(repositoryRoot, ".git");
        if (!Directory.Exists(gitPath) && !File.Exists(gitPath))
        {
            return
                "[FullClone] skipped: the tree carries no .git metadata (a tarball or an exported "
                + "working copy), so `git cat-file -e <id>^{commit}` cannot be asked anything — every "
                + "commit id the changelog cites would read as absent regardless of whether it exists. "
                + "This guard needs a full clone; the publish workflows fetch `fetch-depth: 0` for "
                + "exactly this reason.";
        }

        int exitCode;
        string standardOutput;
        try
        {
            (exitCode, standardOutput, _) = RunGit(
                repositoryRoot, "rev-parse", "--is-shallow-repository");
        }
        catch (Win32Exception exception) when (IsFileNotFound(exception))
        {
            return
                "[FullClone] skipped: no `git` binary could be started (Process.Start reported "
                + "file-not-found), so `git cat-file -e <id>^{commit}` cannot be asked anything. "
                + "This guard needs a full clone on a machine with git; the publish workflows run it "
                + "that way.";
        }

        return exitCode == 0
            && string.Equals(standardOutput, "true", StringComparison.Ordinal)
                ? "[FullClone] skipped: `git rev-parse --is-shallow-repository` answered true, so "
                + "commits beyond the shallow boundary resolve to nothing and `git cat-file -e` "
                + "would report exit 128 on ids that exist — a red about the clone, not about the "
                + "changelog. This guard needs a full clone; the publish workflows fetch "
                + "`fetch-depth: 0` for exactly this reason."
                : null;
    }

    /// <summary>
    /// <c>Process.Start</c> reports a missing binary as <see cref="Win32Exception"/> with
    /// <c>NativeErrorCode</c> 2 on both platforms the suite runs on (ERROR_FILE_NOT_FOUND on Windows,
    /// ENOENT under the Unix PAL), plus 3 (ERROR_PATH_NOT_FOUND) for a path-shaped name. Any other
    /// failure — EACCES and friends — is a broken instrument, not an absent one, and is deliberately
    /// not matched so it propagates and fails rather than reading as "git is not installed".
    /// </summary>
    private static bool IsFileNotFound(Win32Exception exception) =>
        exception.NativeErrorCode is 2 or 3;

    /// <summary>
    /// Same shape as <c>ReleaseContractTests.RunGit</c>: arguments through
    /// <see cref="ProcessStartInfo.ArgumentList"/> so nothing is re-parsed by a shell, and both
    /// streams read before the wait so a chatty stderr cannot deadlock the pipe.
    /// </summary>
    private static (int ExitCode, string StandardOutput, string StandardError) RunGit(
        string repositoryRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git could not be started to probe clone depth.");

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, standardOutput.Trim(), standardError.Trim());
    }
}
