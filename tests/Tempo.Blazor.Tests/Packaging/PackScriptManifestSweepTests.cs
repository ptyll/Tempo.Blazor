using FluentAssertions;
using FluentAssertions.Execution;
using Xunit.Abstractions;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// The manifest sweep in <c>eng/pack-nuget-packages.sh</c>, measured by RUNNING it — the arm that
/// asks the feed about every id in <c>eng/nuget-packages.txt</c> rather than about the lead package
/// alone.
/// <para>
/// WHY A RUN AND NOT A TEXT NEEDLE. The sweep is a CHANGE OF BEHAVIOUR: before it, a version already
/// spent under a non-lead id was reported free and packed; after it, the pack is refused and the id
/// is named. Every text gate over this script — the marker lists in <c>ReleaseContractTests</c>, the
/// keep-clause needles in <c>ReleaseScriptInputReadTests</c> — stays green over a revert that deletes
/// the loop and leaves its comment block in place, which is exactly the "delete the code, keep the
/// prose" shape this repository has been bitten by before. The two members below therefore run the
/// script and read what it decided.
/// </para>
/// <para>
/// AND WHY NO LIVE FEED. Two reasons, and the second is the load-bearing one. The cheap reason is
/// that a suite arm which needs nuget.org is a suite arm that is red on a train. The real reason is
/// that the case being measured — a number SPENT under a non-lead id and FREE under the lead —
/// cannot be produced against the live feed at all: measured 2026-08-23 over all 26 manifest ids,
/// the union of every version any of them serves is exactly the 105 versions the lead id serves, and
/// 0 versions exist outside that set. So the live feed can only exercise the arm that was already
/// there. <c>PATH</c> carries a stub <c>curl</c> instead, and the ids it was asked are logged and
/// asserted, so "offline" is a measured property of the run rather than a claim about it.
/// </para>
/// <para>
/// THE SUPERSET PROPERTY IS THE CONDITION, NOT A UNIFORMITY. It is tempting to say the feed answers
/// uniformly for all 26 ids; that is false, and measurably so — the same read found SIX different
/// version sets (3 ids serve 105 versions, 19 serve 86, and four singletons serve 58 / 55 / 48 / 30).
/// What actually holds is weaker and is all the conclusion needs: every version served by any id is
/// also served by the LEAD id. Under that property a spent number is always spent on the lead too,
/// which is why one-id asking looked adequate for as long as it did — and the property dies at the
/// first partial publication of a non-lead id under a number the lead does not carry, which is the
/// state <c>eng/push-nuget-packages.sh</c> exists for. Until then this fixture is the only place the
/// refusal arm can be exercised.
/// </para>
/// </summary>
public sealed class PackScriptManifestSweepTests
{
    private const string SpentVersion = "9.9.9-fixture";

    private readonly ITestOutputHelper _output;

    public PackScriptManifestSweepTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The seam the two run members enter through is the one production reads its population from.
    /// <para>
    /// KEPT SEPARATE FROM THE RUNS ON PURPOSE. A text needle inside the run member would abort it
    /// before the script was ever started, so a revert would be reported as "the file no longer says
    /// manifest_spent" — true, and not the claim those members are about. Read this one for what the
    /// script CONTAINS and the two below for what it DECIDES.
    /// </para>
    /// <para>
    /// AND THEREFORE NO PLATFORM GUARD. This member reads a file; it starts no process and never
    /// constructs the fixture that does. <see cref="BashScriptFactAttribute"/> would report it as
    /// skipped on Windows carrying a reason that says the member starts <c>bash</c> — of THIS member
    /// that sentence is false, and a skip reason that is false about its own member is worse than the
    /// unmeasured platform it was written to disclose.
    /// </para>
    /// </summary>
    [Fact]
    public void PackScript_ReadsItsPopulationThroughThePackageManifestSeam()
    {
        string code = ReleaseScriptInputReadTests.CodeLinesOf(
            File.ReadAllText(Path.Combine(
                ReleaseScriptInputReadTests.FindRepoRoot(), "eng", "pack-nuget-packages.sh")));

        using (new AssertionScope())
        {
            code.Should().Contain(
                "manifest=\"${PACKAGE_MANIFEST:-eng/nuget-packages.txt}\"",
                "the seam the fixtures enter through has to be the same one production reads its "
                + "population from, or those runs measure a different script than the one that ships");
            code.Should().Contain(
                "for project in \"${projects[@]}\"",
                "the sweep is a loop over the manifest projects; without it the runs below could only "
                + "ever reproduce the lead arm");
            code.Should().Contain(
                "manifest_spent",
                "the refusal arm keeps the id it tripped over, because naming it is the whole "
                + "difference between this and the one-id question");
        }
    }

    /// <summary>
    /// A number spent under a NON-LEAD manifest id refuses the pack and names that id — and the old
    /// one-id shape packs it.
    /// </summary>
    [BashScriptFact]
    public void PackScript_AsksEveryManifestId_SoASpentNonLeadNumberIsRefused()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        string healthy = File.ReadAllText(Path.Combine(root, "eng", "pack-nuget-packages.sh"));

        using var fixture = new SweepFixture(
            "Tm.Fixture.Never", "Tm.Fixture.Error", "Tm.Fixture.Free", "Tm.Fixture.Spent");

        string healthyPath = Path.Combine(Path.GetTempPath(), $"tm-sweep-healthy-{Guid.NewGuid():N}.sh");
        string mutatedPath = Path.Combine(Path.GetTempPath(), $"tm-sweep-mutated-{Guid.NewGuid():N}.sh");
        try
        {
            // The healthy run comes first, and the mutant is not even CONSTRUCTED until after it:
            // DropTheManifestSweep refuses a script that no longer has the sweep, so building it up
            // front would report a revert as a broken mutation instead of as a packed spent number.
            File.WriteAllText(healthyPath, ReleaseScriptInputReadTests.InsertPastFeedHarness(healthy));

            ReleaseScriptInputReadTests.ScriptResult swept =
                ReleaseScriptInputReadTests.RunBash(healthyPath, root, fixture.Env(SpentVersion));
            Dump("sweep over a spent non-lead id", swept);
            IReadOnlyList<string> sweptAsked = fixture.AskedIds();
            _output.WriteLine("asked: " + string.Join(", ", sweptAsked));

            using (new AssertionScope())
            {
                sweptAsked.Should().Equal(
                    new[] { "tempo.blazor", "tm.fixture.never", "tm.fixture.error", "tm.fixture.free", "tm.fixture.spent" },
                    "the ids the script actually asked about: the lead first (that is the reach "
                    + "control) and then every manifest id, lowercased, until one answers with the "
                    + "version — and nothing else, which is what makes this run offline rather than "
                    + $"merely intended to be ({swept.Combined})");

                swept.Combined.Should().Contain(
                    "under package id 'tm.fixture.spent'",
                    "the refusal has to NAME the id that serves the number; 'already published' "
                    + "without an id sends the reader to the lead package, which is free "
                    + $"({swept.Combined})");
                swept.Combined.Should().Contain(
                    "The lead id 'tempo.blazor' does not serve",
                    "the refusal also has to say why the cheaper question reported the number free, or it "
                    + $"refusal reads as a contradiction of the arm above it ({swept.Combined})");
                swept.Combined.Should().Contain(
                    "tm.fixture.never: 404, never published",
                    "a 404 on a non-lead id is REPORTED and skipped — the loop reached the spent id "
                    + $"after it, which it could not have done had the 404 been fatal ({swept.Combined})");
                swept.Combined.Should().Contain(
                    "tm.fixture.error: http '503'",
                    "and an id the feed did not answer for is reported as NOT asked rather than as "
                    + $"free, which is the stated limit of the sweep ({swept.Combined})");
                swept.Combined.Should().NotContain(
                    "PAST_FEED",
                    $"the pack must not be reached at all once a manifest id serves the number ({swept.Combined})");
                swept.Exit.Should().Be(1, swept.Combined);
            }

            File.WriteAllText(
                mutatedPath,
                ReleaseScriptInputReadTests.InsertPastFeedHarness(DropTheManifestSweep(healthy)));

            fixture.ForgetAskedIds();
            ReleaseScriptInputReadTests.ScriptResult leadOnly =
                ReleaseScriptInputReadTests.RunBash(mutatedPath, root, fixture.Env(SpentVersion));
            Dump("mutation: the sweep deleted, its comment block kept", leadOnly);
            IReadOnlyList<string> leadOnlyAsked = fixture.AskedIds();
            _output.WriteLine("asked: " + string.Join(", ", leadOnlyAsked));

            using (new AssertionScope())
            {
                leadOnlyAsked.Should().Equal(
                    new[] { "tempo.blazor" },
                    "the mutation is the shape this script had before the sweep: one question, about "
                    + $"the lead id ({leadOnly.Combined})");
                leadOnly.Combined.Should().NotContain(
                    "already published",
                    "and that shape reports a number spent under another id as FREE — that green is "
                    + $"the defect the member above exists to make red ({leadOnly.Combined})");
                leadOnly.Combined.Should().Contain("PAST_FEED", leadOnly.Combined);
                leadOnly.Exit.Should().Be(97, leadOnly.Combined);
            }
        }
        finally
        {
            ReleaseScriptInputReadTests.TryDelete(healthyPath);
            ReleaseScriptInputReadTests.TryDelete(mutatedPath);
        }
    }

    /// <summary>
    /// The branch <c>404 is reported and skipped</c>, which nothing reached before this member: it
    /// cannot fire against the live feed (all 26 ids answer 200, measured 2026-08-23) and it is the
    /// branch that decides whether a newly added 27th package blocks the release.
    /// </summary>
    [BashScriptFact]
    public void PackScript_ReportsANeverPublishedManifestId_AndPacksOn()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        string healthy = File.ReadAllText(Path.Combine(root, "eng", "pack-nuget-packages.sh"));

        using var fixture = new SweepFixture("Tm.Fixture.Never", "Tm.Fixture.Free");

        string healthyPath = Path.Combine(Path.GetTempPath(), $"tm-sweep-404-{Guid.NewGuid():N}.sh");
        string fatalPath = Path.Combine(Path.GetTempPath(), $"tm-sweep-404-fatal-{Guid.NewGuid():N}.sh");
        try
        {
            // The healthy run comes first, and the mutant is not even CONSTRUCTED until after it: the
            // mutation helper refuses a script that no longer carries the branch, and a revert has to
            // be reported as the missing 404 report rather than as a broken mutation.
            File.WriteAllText(healthyPath, ReleaseScriptInputReadTests.InsertPastFeedHarness(healthy));

            ReleaseScriptInputReadTests.ScriptResult lenient =
                ReleaseScriptInputReadTests.RunBash(healthyPath, root, fixture.Env(SpentVersion));
            Dump("404 on a non-lead id", lenient);

            using (new AssertionScope())
            {
                lenient.Combined.Should().Contain(
                    "tm.fixture.never: 404, never published",
                    "the id that answered 404 is named on the way past, so a run can be told from a "
                    + $"run in which the feed answered for everything ({lenient.Combined})");
                lenient.Combined.Should().Contain(
                    "1 further manifest id(s) the feed answered for; 1 never published, 0 not answered",
                    "the tally is the record of how many of the questions were actually answered "
                    + $"— the sentence the script's own comment block promises ({lenient.Combined})");
                lenient.Combined.Should().Contain(
                    "PAST_FEED",
                    "a 404 must NOT refuse the pack: it is the legitimate state of a package added to "
                    + "the manifest and not yet published, and a guard that the first new package "
                    + $"blocks is a guard somebody switches off ({lenient.Combined})");
                lenient.Exit.Should().Be(97, lenient.Combined);
            }

            File.WriteAllText(
                fatalPath,
                ReleaseScriptInputReadTests.InsertPastFeedHarness(MakeAnUnpublishedIdFatal(healthy)));

            ReleaseScriptInputReadTests.ScriptResult fatal =
                ReleaseScriptInputReadTests.RunBash(fatalPath, root, fixture.Env(SpentVersion));
            Dump("mutation: lead strictness applied to a 404", fatal);

            using (new AssertionScope())
            {
                fatal.Combined.Should().NotContain(
                    "PAST_FEED",
                    "the mutation applies the lead id's strictness to a 404, which refuses the pack — "
                    + "that is the behaviour the member above rules out, and without this arm the "
                    + $"green above could also come from a loop that never ran ({fatal.Combined})");
                fatal.Exit.Should().Be(1, fatal.Combined);
            }
        }
        finally
        {
            ReleaseScriptInputReadTests.TryDelete(healthyPath);
            ReleaseScriptInputReadTests.TryDelete(fatalPath);
        }
    }

    /// <summary>
    /// The revert this behaviour has to be distinguishable from: the guarded manifest loop removed,
    /// its comment block left where it is. Every text needle in this repository stays green over that
    /// mutation, which is precisely why it is the one worth running.
    /// </summary>
    internal static string DropTheManifestSweep(string script)
    {
        const string open = "if [[ \"$lead_answered\" == \"1\" ]]; then\n";
        const string close = "$manifest_unanswered not answered.\" >&2\nfi\n";

        int from = script.IndexOf(open, StringComparison.Ordinal);
        int to = script.IndexOf(close, StringComparison.Ordinal);
        if (from < 0 || to < from)
        {
            throw new InvalidOperationException(
                "eng/pack-nuget-packages.sh no longer carries the guarded manifest sweep this mutation removes");
        }

        return script.Remove(from, to + close.Length - from);
    }

    /// <summary>
    /// The other revert: the lead id's strictness applied to an id that answers 404. Kept as a
    /// mutation rather than described, because "reported and skipped" and "never reached" produce the
    /// same silence in a run that has no unpublished id in it.
    /// </summary>
    internal static string MakeAnUnpublishedIdFatal(string script)
    {
        const string needle = "to collide with.\" >&2\n      continue\n";
        if (script.Split(needle).Length != 2)
        {
            throw new InvalidOperationException(
                "eng/pack-nuget-packages.sh no longer skips a 404 on a non-lead id in one place");
        }

        return script.Replace(
            needle,
            "to collide with.\" >&2\n      exit 1\n",
            StringComparison.Ordinal);
    }

    private void Dump(string label, ReleaseScriptInputReadTests.ScriptResult result)
    {
        _output.WriteLine("==== " + label + " ====");
        _output.WriteLine(result.Combined);
    }

    /// <summary>
    /// A manifest of fixture projects, plus the stub <c>curl</c> that answers for their ids and
    /// records which ids it was asked. The recording is what turns "this test does not touch the
    /// network" from an intention into an assertion.
    /// </summary>
    private sealed class SweepFixture : IDisposable
    {
        private readonly string _root;
        private readonly string _fakeBin;
        private readonly string _manifestPath;
        private readonly string _curlLogPath;

        internal SweepFixture(params string[] packageIds)
        {
            _root = Path.Combine(Path.GetTempPath(), $"tm-sweep-fixture-{Guid.NewGuid():N}");
            _fakeBin = Path.Combine(_root, "bin");
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(_fakeBin);
            _curlLogPath = Path.Combine(_root, "asked-ids.txt");
            _manifestPath = Path.Combine(_root, "manifest.txt");

            List<string> lines =
            [
                "# a comment line and the blank line below are dropped by the same read production uses",
                string.Empty,
            ];
            foreach (string packageId in packageIds)
            {
                string project = Path.Combine(_root, packageId + ".csproj");
                File.WriteAllText(
                    project,
                    "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n"
                    + $"    <PackageId>{packageId}</PackageId>\n  </PropertyGroup>\n</Project>\n");
                lines.Add(project);
            }

            File.WriteAllLines(_manifestPath, lines);
            WriteStubCurl(Path.Combine(_fakeBin, "curl"));
        }

        internal IReadOnlyDictionary<string, string> Env(string version) => new Dictionary<string, string>
        {
            ["VERSION"] = version,
            ["PACKAGE_MANIFEST"] = _manifestPath,
            ["PATH"] = _fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["TM_CURL_LOG"] = _curlLogPath,
        };

        internal IReadOnlyList<string> AskedIds() =>
            File.Exists(_curlLogPath) ? File.ReadAllLines(_curlLogPath) : [];

        internal void ForgetAskedIds() => ReleaseScriptInputReadTests.TryDelete(_curlLogPath);

        public void Dispose() => ReleaseScriptInputReadTests.TryDeleteDir(_root);

        /// <summary>
        /// Answers the flat-container question the pack script asks, in the shape it asks it: body on
        /// stdout, then a newline, then the status code — which is what <c>-w '\n%{http_code}'</c>
        /// produces. An id it was not told about answers 599 with a marked body, so an unexpected
        /// request is loud rather than silently indistinguishable from a real feed error.
        /// </summary>
        private static void WriteStubCurl(string path)
        {
            File.WriteAllText(path, """
                #!/usr/bin/env bash
                set -uo pipefail
                url="${!#}"
                id="${url##*/v3-flatcontainer/}"
                id="${id%%/index.json}"
                if [[ -n "${TM_CURL_LOG:-}" ]]; then
                  echo "$id" >> "$TM_CURL_LOG"
                fi
                case "$id" in
                  tempo.blazor)     body='{"versions":["2.8.19","2.8.20"]}'; code=200 ;;
                  tm.fixture.spent) body='{"versions":["1.0.0","9.9.9-fixture"]}'; code=200 ;;
                  tm.fixture.free)  body='{"versions":["1.0.0","2.0.0"]}'; code=200 ;;
                  tm.fixture.never) body='{"error":"not found"}'; code=404 ;;
                  tm.fixture.error) body=''; code=503 ;;
                  *)                body="UNSTUBBED-ID:$id"; code=599 ;;
                esac
                printf '%s\n%s' "$body" "$code"
                exit 0
                """);

            ReleaseScriptInputReadTests.MakeExecutable(path);
        }
    }
}
