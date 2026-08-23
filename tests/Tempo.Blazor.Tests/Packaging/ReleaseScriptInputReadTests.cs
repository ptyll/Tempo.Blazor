using System.Diagnostics;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit.Abstractions;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// Needles over the READ of each release script's input, not over the comparison that consumes it.
/// <para>
/// THE DEFECT, measured 2026-08-21: every existing text-gate over these three scripts proved the
/// comparison clauses were in the file. A mutation that left those clauses in place and hardcoded
/// the read — <c>announced="$version"</c> instead of <c>sed</c> over CHANGELOG.md;
/// <c>published=$total</c> instead of classifying <c>dotnet nuget push</c> output; a fake 200 body
/// instead of <c>curl</c> of the flat container — stayed green on all of them. The comparison then
/// agrees with itself, or the spent-version refusal never sees the spent version.
/// </para>
/// <para>
/// WHAT A GREEN HERE PROVES is that the keep-clause-break-the-read mutation turns THIS member red,
/// and that the unmutated script stays green. The sibling members in
/// <c>ReleaseGateFilterTests</c> / <c>ReleaseContractTests.Feed</c> still only prove presence of
/// the clauses; they are not this needle.
/// </para>
/// </summary>
public sealed class ReleaseScriptInputReadTests
{
    private readonly ITestOutputHelper _output;

    public ReleaseScriptInputReadTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void VerifyScript_KeepClauseBreakTheRead_TurnsTheNeedleRed_UnmutatedStaysGreen()
    {
        string root = FindRepoRoot();
        string healthy = File.ReadAllText(Path.Combine(root, "eng", "verify-announced-version.sh"));
        string code = CodeLinesOf(healthy);

        using (new AssertionScope())
        {
            code.Should().Contain(
                "announced=\"$(sed",
                "the announced number must be READ from CHANGELOG.md via sed; a comparison of "
                + "$version against $announced is vacuously true once announced is assigned from "
                + "version");

            ApplyVerifyBreakTheRead(healthy).Should().NotBe(
                healthy,
                "the documented mutation must actually change the script");

            string mutatedCode = CodeLinesOf(ApplyVerifyBreakTheRead(healthy));
            mutatedCode.Should().Contain(
                "\"$version\" == \"$announced\"",
                "the comparison clause stays — that is the keep-clause half of the mutation, and "
                + "the sibling presence needles stay green over it");
            mutatedCode.Should().NotContain(
                "announced=\"$(sed",
                "after announced=\"$version\" the sed read is gone, and this needle has to see that");
        }

        string changelog = Path.Combine(Path.GetTempPath(), $"tm-verify-changelog-{Guid.NewGuid():N}.md");
        string mutatedPath = Path.Combine(Path.GetTempPath(), $"tm-verify-mutated-{Guid.NewGuid():N}.sh");
        try
        {
            File.WriteAllText(changelog, "# Changelog\n\n## 1.2.3 - 2099-01-01\n\nfixture\n");
            File.WriteAllText(mutatedPath, ApplyVerifyBreakTheRead(healthy));

            ScriptResult agree = RunBash(
                Path.Combine(root, "eng", "verify-announced-version.sh"),
                root,
                new Dictionary<string, string>
                {
                    ["VERSION"] = "1.2.3",
                    ["CHANGELOG_PATH"] = changelog,
                    ["VERSION_SOURCE"] = "the fixture",
                });
            Dump("verify unmutated agree", agree);
            agree.Exit.Should().Be(
                0,
                "positive control: unmutated script over agreeing numbers must stay green "
                + $"({agree.Combined})");

            ScriptResult mismatch = RunBash(
                Path.Combine(root, "eng", "verify-announced-version.sh"),
                root,
                new Dictionary<string, string>
                {
                    ["VERSION"] = "9.9.9",
                    ["CHANGELOG_PATH"] = changelog,
                    ["VERSION_SOURCE"] = "the fixture",
                });
            Dump("verify unmutated mismatch", mismatch);
            mismatch.Exit.Should().Be(
                1,
                "unmutated script must refuse when VERSION is not what CHANGELOG announces "
                + $"({mismatch.Combined})");

            ScriptResult mutated = RunBash(
                mutatedPath,
                root,
                new Dictionary<string, string>
                {
                    ["VERSION"] = "9.9.9",
                    ["CHANGELOG_PATH"] = changelog,
                    ["VERSION_SOURCE"] = "the fixture",
                });
            Dump("verify mutated announced=$version", mutated);
            mutated.Exit.Should().Be(
                0,
                "the keep-clause-break-the-read mutation (announced=\"$version\") makes the "
                + "comparison agree with itself, so the SCRIPT is green — this member exists "
                + "because that green is the defect, and the text needle above is what turns "
                + $"red on it ({mutated.Combined})");
        }
        finally
        {
            TryDelete(changelog);
            TryDelete(mutatedPath);
        }
    }

    [Fact]
    public void PushScript_KeepClauseBreakTheRead_TurnsTheNeedleRed_UnmutatedStaysGreen()
    {
        string root = FindRepoRoot();
        string healthy = File.ReadAllText(Path.Combine(root, "eng", "push-nuget-packages.sh"));
        string code = CodeLinesOf(healthy);

        using (new AssertionScope())
        {
            code.Should().Contain(
                "out=\"$(DOTNET_CLI_UI_LANGUAGE=en dotnet nuget push",
                "the verdict must be READ from the tool's own output; assigning published=$total "
                + "leaves the comparison \"$published\" -eq \"$total\" true whether anything went out");

            code.Should().NotMatchRegex(
                @"(?m)^\s*published=\$total\s*$",
                "published=$total after the loop is the keep-clause-break-the-read mutation this "
                + "needle exists to catch");

            ApplyPushBreakTheRead(healthy).Should().NotBe(healthy);
            string mutatedCode = CodeLinesOf(ApplyPushBreakTheRead(healthy));
            mutatedCode.Should().Contain(
                "\"$published\" -eq \"$total\"",
                "the comparison clause stays — the sibling presence needles stay green over it");
            mutatedCode.Should().MatchRegex(
                @"(?m)^\s*published=\$total\s*$",
                "published=$total is the keep-clause-break-the-read mutation this needle sees");
        }

        string staging = Path.Combine(Path.GetTempPath(), $"tm-push-stage-{Guid.NewGuid():N}");
        string mutatedPath = Path.Combine(Path.GetTempPath(), $"tm-push-mutated-{Guid.NewGuid():N}.sh");
        string fakeBin = Path.Combine(Path.GetTempPath(), $"tm-fake-dotnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(fakeBin);
        try
        {
            File.WriteAllBytes(Path.Combine(staging, "a.nupkg"), [0x50, 0x4B]);
            File.WriteAllBytes(Path.Combine(staging, "b.nupkg"), [0x50, 0x4B]);
            WriteFakeDotnet(Path.Combine(fakeBin, "dotnet"));
            File.WriteAllText(mutatedPath, ApplyPushBreakTheRead(healthy));

            var env = new Dictionary<string, string>
            {
                ["PUSH_SOURCE"] = "probe",
                ["PUSH_API_KEY"] = "probe",
                ["PACKAGE_OUTPUT"] = staging,
                ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                ["FAKE_DOTNET_MODE"] = "conflict",
            };

            ScriptResult unmutated = RunBash(
                Path.Combine(root, "eng", "push-nuget-packages.sh"), root, env);
            Dump("push unmutated 409", unmutated);
            unmutated.Exit.Should().Be(
                1,
                "positive control: unmutated classifier over Conflict output must be red "
                + $"(skipped=2) ({unmutated.Combined})");
            unmutated.Combined.Should().Contain("skipped=2");

            ScriptResult mutated = RunBash(mutatedPath, root, env);
            Dump("push mutated published=$total", mutated);
            mutated.Exit.Should().Be(
                0,
                "published=$total leaves the comparison true over a 409, so the SCRIPT is green "
                + "— that green is the defect; the text needle above is what turns red on it "
                + $"({mutated.Combined})");
        }
        finally
        {
            TryDelete(mutatedPath);
            TryDelete(Path.Combine(fakeBin, "dotnet"));
            TryDeleteDir(fakeBin);
            TryDeleteDir(staging);
        }
    }

    /// <summary>
    /// The keep-clause-break-the-read needle over the pack script's feed probe.
    /// <para>
    /// WHAT THIS MEMBER'S 2.8.20 RUN DOES AND DOES NOT EXERCISE, measured 2026-08-23 and worth stating
    /// because the script has grown a second feed question since this was written: 2.8.20 is served by
    /// the LEAD id, so the unmutated run is refused by the lead arm — in about 0.18 s, before the
    /// manifest loop is reached at all. It therefore says nothing about the sweep over the other 25
    /// ids; that behaviour is measured offline against a stubbed feed in
    /// <c>PackScriptManifestSweepTests</c>, and this member remains what it always was, a needle on
    /// the READ rather than on the population.
    /// </para>
    /// <para>
    /// This member does reach the live feed, unlike the sweep tests. That is deliberate here: the
    /// mutation it defends against is "the curl read replaced by a canned body", and a run that never
    /// curls cannot tell the two apart.
    /// </para>
    /// </summary>
    [Fact]
    public void PackScript_KeepClauseBreakTheRead_TurnsTheNeedleRed_UnmutatedStaysGreen()
    {
        string root = FindRepoRoot();
        string healthy = File.ReadAllText(Path.Combine(root, "eng", "pack-nuget-packages.sh"));
        string code = CodeLinesOf(healthy);

        using (new AssertionScope())
        {
            code.Should().Contain(
                "feed_body=\"$(curl",
                "the spent-version refusal must READ the feed via curl; a fake 200 body leaves "
                + "every comparison clause in place and reports every number as free");

            ApplyPackBreakTheRead(healthy).Should().NotBe(healthy);
            CodeLinesOf(ApplyPackBreakTheRead(healthy)).Should().NotContain(
                "feed_body=\"$(curl",
                "after the curl read is replaced by a fake 200 body this needle has to see it");
        }

        string spent = "2.8.20";
        string harnessedHealthyPath = Path.Combine(Path.GetTempPath(), $"tm-pack-healthy-{Guid.NewGuid():N}.sh");
        string harnessedMutatedPath = Path.Combine(Path.GetTempPath(), $"tm-pack-mutated-{Guid.NewGuid():N}.sh");
        try
        {
            File.WriteAllText(harnessedHealthyPath, InsertPastFeedHarness(healthy));
            File.WriteAllText(harnessedMutatedPath, InsertPastFeedHarness(ApplyPackBreakTheRead(healthy)));

            var env = new Dictionary<string, string> { ["VERSION"] = spent };

            ScriptResult unmutated = RunBash(harnessedHealthyPath, root, env);
            Dump("pack unmutated spent version", unmutated);
            unmutated.Combined.Should().Contain(
                "already published",
                "positive control: unmutated pack script must refuse a number the feed serves "
                + $"({unmutated.Combined})");
            unmutated.Combined.Should().NotContain("PAST_FEED");
            unmutated.Exit.Should().Be(1);

            ScriptResult mutated = RunBash(harnessedMutatedPath, root, env);
            Dump("pack mutated fake feed body", mutated);
            mutated.Combined.Should().Contain(
                "PAST_FEED",
                "a fake 200 body without the spent version makes the feed clauses agree that the "
                + "number is free, so the SCRIPT walks past the refusal — that is the defect; "
                + "the text needle above is what turns red on it "
                + $"({mutated.Combined})");
            mutated.Combined.Should().NotContain("already published");
        }
        finally
        {
            TryDelete(harnessedHealthyPath);
            TryDelete(harnessedMutatedPath);
        }
    }

    [Fact]
    public void ClassifierProbeScript_IsTheReleaseProcedureForSdkWording()
    {
        string root = FindRepoRoot();
        string probe = File.ReadAllText(Path.Combine(root, "eng", "verify-push-classifier.sh"));
        string code = CodeLinesOf(probe);

        using (new AssertionScope())
        {
            code.Should().Contain("http.server");
            code.Should().Contain("allowInsecureConnections=");
            code.Should().Contain("PUSH_CONFIGFILE");
            code.Should().Contain("PUSH_SOURCE");
            code.Should().Contain("PUSH_API_KEY");
            code.Should().Contain("PACKAGE_OUTPUT");
            code.Should().Contain("published=$total");
            code.Should().Contain("skipped=$total");
            probe.Should().Contain("THIS CLASSIFIER IS BOUND TO SDK WORDING");
            probe.Should().Contain("allowInsecureConnections=\"true\"");
        }
    }

    internal static string ApplyVerifyBreakTheRead(string script)
    {
        string[] lines = script.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("announced=\"$(sed", StringComparison.Ordinal))
            {
                lines[i] = "announced=\"$version\"";
            }
        }

        return string.Join('\n', lines);
    }

    internal static string ApplyPushBreakTheRead(string script)
    {
        const string needle = "if [[ \"$published\" -eq \"$total\" ]]; then";
        if (!script.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("push script no longer compares published to total");
        }

        return script.Replace(needle, "published=$total\n" + needle, StringComparison.Ordinal);
    }

    internal static string ApplyPackBreakTheRead(string script)
    {
        string[] lines = script.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("feed_body=\"$(curl", StringComparison.Ordinal))
            {
                lines[i] = "feed_body=\"$(printf '%s\\n%s' '{\"versions\":[\"0.0.0\"]}' '200')\"";
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Stops the mutated pack script from actually packing: if the feed refusal is skipped, the
    /// next line is this harness rather than <c>dotnet pack</c>. Unmutated never reaches it.
    /// </summary>
    internal static string InsertPastFeedHarness(string script)
    {
        const string needle = "dirty_suffix=\"\"";
        if (!script.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("pack script no longer has the dirty_suffix assignment");
        }

        return script.Replace(
            needle,
            "echo PAST_FEED\nexit 97\n" + needle,
            StringComparison.Ordinal);
    }

    private static void WriteFakeDotnet(string path)
    {
        File.WriteAllText(path, """
            #!/usr/bin/env bash
            set -euo pipefail
            mode="${FAKE_DOTNET_MODE:-created}"
            if [[ "$mode" == "conflict" ]]; then
              echo "  Conflict http://127.0.0.1/v3/package"
              echo "Package 'probe' already exists at feed 'tm-push-probe'."
            else
              echo "  Created http://127.0.0.1/v3/package"
              echo "Your package was pushed."
            fi
            exit 0
            """);
        using var chmod = Process.Start(new ProcessStartInfo("/bin/chmod")
        {
            ArgumentList = { "+x", path },
            UseShellExecute = false,
        });
        chmod?.WaitForExit();
    }

    internal sealed record ScriptResult(int Exit, string StdOut, string StdErr)
    {
        public string Combined =>
            $"exit={Exit}\n--- stdout ---\n{StdOut}\n--- stderr ---\n{StdErr}";
    }

    internal static ScriptResult RunBash(
        string scriptPath,
        string workDir,
        IReadOnlyDictionary<string, string> extraEnv)
    {
        var start = new ProcessStartInfo("/usr/bin/env")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("bash");
        start.ArgumentList.Add(scriptPath);
        foreach (KeyValuePair<string, string> pair in extraEnv)
        {
            start.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("failed to start bash");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { /* best effort */ }
            throw new TimeoutException($"script {scriptPath} did not exit within 120s");
        }

        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    private void Dump(string label, ScriptResult result)
    {
        _output.WriteLine("==== " + label + " ====");
        _output.WriteLine(result.Combined);
    }

    internal static string CodeLinesOf(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    internal static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Tempo.Blazor repository root.");
    }

    internal static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* temp cleanup */ }
    }

    internal static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { /* temp cleanup */ }
    }
}
