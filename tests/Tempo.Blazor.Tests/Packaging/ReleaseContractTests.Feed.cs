using FluentAssertions.Execution;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// The half of the release contract that asks nuget.org itself. Kept in a second file of the SAME
/// partial class rather than a class of its own, because it has to read the announced version through
/// <c>ReadAnnouncedVersion</c> — the one reader the other guards use, whose own remark says it is
/// shared so they can never disagree about which release is being shipped. A separate class would
/// either copy that reader or widen its visibility, and a copied reader is a drift waiting to happen.
/// </summary>
public sealed partial class ReleaseContractTests
{
    /// <summary>
    /// The version the changelog announces is not already published on nuget.org.
    /// <para>
    /// THE DEFECT THIS EXISTS FOR, measured rather than imagined: 2.8.19 was announced twice. The tag
    /// side of that story is guarded by
    /// <see cref="AnnouncedVersion_IsEitherUntagged_OrItsTagNamesTheCommitBeingPacked"/>; the OTHER
    /// side — an artefact already sitting on the feed, immutable, under that number — was left to a
    /// reader, and a reader is what failed. Measured 2026-08-21: 2.8.19 and 2.8.18 answer 200 on the
    /// flat container while 2.8.20 answers 404, so this question has a live answer today.
    /// </para>
    /// <para>
    /// TAG, PUT AND AVAILABILITY ARE THREE DIFFERENT QUANTITIES and this guard speaks only about the
    /// third. A number can be published with no tag ever existing, and a tag can name a commit whose
    /// package was never pushed — so nothing here may be read as a statement about the tag store, and
    /// nothing the tag guard says may be read as a statement about the feed. IT ALSO DOES NOT CLOSE
    /// the known gap that the tag guard cannot go red on any CI lane: that is a different guard about
    /// a different quantity, and this one being live in CI leaves it exactly where it was. Nor does a
    /// green mean "no push has happened": nuget.org serves this index through a CDN, so a completed
    /// push can still read as absent for a while. The question answered is "is the number VISIBLE as
    /// taken", which a release can act on — not "was a PUT accepted", which nothing here can see.
    /// </para>
    /// <para>
    /// HOW LONG "a while" WAS, ONCE — AN OBSERVATION AND NOT A BOUND. Release 2.8.20 on 2026-08-22,
    /// GHA run 32557365946: the push step's PUT completed at 06:48:30Z and the flat container first
    /// answered 200 for that number at 06:55:41Z, which is 431 s later. The whole publish job that
    /// produced the PUT ran 06:43:25Z to 06:48:35Z, i.e. 310 s. THE COMPARISON IS THE FINDING: 431 s
    /// is longer than 310 s, so the blind window outlasts the job that opens it, and a release can
    /// finish GREEN while this guard and the copy in <c>eng/pack-nuget-packages.sh</c> both still read
    /// the number as free. In that window neither of them is wrong — each answers its own question,
    /// "is the number VISIBLE as taken", correctly — and both would say "free" about a number that is
    /// already spent. ONE SAMPLE OF A CDN IS NOT A LIMIT: nothing here waits for those 431 s, no
    /// release may be scheduled against them, and the next propagation may take longer or less. What
    /// the number establishes is only that this window is not negligible and not shorter than a
    /// publish. The endpoint is unchanged by this note: <see cref="PublishedVersionSurvey.IndexUrl"/>
    /// is still the flat container, because registration and search answer a DIFFERENT question and
    /// swapping to one of them would change the quantity rather than shorten the window.
    /// </para>
    /// <para>
    /// WHAT THIS GUARD COSTS ON A PULL REQUEST, decided rather than discovered — see decision
    /// <c>REL-FEED-GUARD-PR-COST</c>. Between the moment a release becomes visible on the feed and the
    /// moment the changelog is bumped past it, this guard is RED, and it is red for a TRUE reason: the
    /// announced number really is taken. <c>build-and-test</c> carries no <c>if:</c> in either publish
    /// workflow, so it also runs on <c>pull_request</c>, and inside that window every pull request goes
    /// red over release accounting its author cannot fix. THE WINDOW WAS MEASURED, not estimated: for
    /// 2.8.20 it opened at 06:55:41Z (the first 200 above) and closed with the bump commit f86f9095
    /// dated 2026-08-22 10:40:16Z — 3 h 44 m 35 s. The previous wave's own run inside it reported
    /// 11199 total / 11198 passed / 1 failed, and that count belongs to that run and that tree.
    /// THE GUARD IS DELIBERATELY NOT SKIPPED ON <c>pull_request</c>. A skip there would buy a quiet
    /// window at the price of a NEW WAY TO BE GREEN FOR THE WRONG REASON, on the one lane where a human
    /// is reading the diff that announces the number — which is exactly where this defect gets
    /// authored. The red is a correct answer arriving at an inconvenient moment; the treatment is the
    /// bump, which the release owes anyway.
    /// </para>
    /// <para>
    /// THE REACH CONTROL IS INSIDE THE ASSERTIONS, NOT BESIDE THEM, because every failure mode of this
    /// probe wears the shape of its passing answer. Measured over four worlds: online the index returns
    /// 200 with 104 versions; with the connection refused the probe returns nothing in 31 ms; against a
    /// blackholed route it returns nothing at the timeout; and with the announced version set to 2.8.19
    /// it returns 200 and finds it. In THREE of those four the naive reading "the announced version is
    /// not in the list" is TRUE, and in only one of them does it mean anything. So the status has to be
    /// 200 and the list has to be non-empty before membership is asked about at all.
    /// </para>
    /// <para>
    /// AND THAT IS ALSO WHAT ANSWERS THE PACKAGE-ID TYPO, which is the same trap wearing different
    /// clothes: the flat container answers 404 for an id it does not know, so a misspelled id would
    /// report every number as free, forever, in green. Two things close it. The id is not written here
    /// — it is READ from <c>src/Tempo.Blazor/Tempo.Blazor.csproj</c>, the same file the publish
    /// workflow reads the version out of, so it cannot drift from what actually ships without the
    /// build noticing. And a 404 is not a value this guard accepts: it fails the status assertion,
    /// which reports a broken instrument rather than an empty feed.
    /// </para>
    /// <para>
    /// UNREACHABLE IS A SKIP, AND THAT IS A HOLE THIS NAMES RATHER THAN HIDES. A guard that reaches the
    /// network cannot be red offline without making the suite unrunnable without one. The skip carries
    /// the population — url, status, version count, elapsed time and the exception — so "nothing was
    /// asked" arrives as a third outcome in the .trx instead of dressed as "the number is free". WHAT
    /// IT COSTS: on a machine with no route to nuget.org this release number is unchecked here. The
    /// mitigation is a MEASUREMENT and not a guarantee: this repository carries no NuGet.config, so
    /// <c>dotnet restore</c> — which runs before the test step in both publish workflows — resolves
    /// against nuget.org, and CI does not reach this test without having reached that host. A runner
    /// with a fully warm package cache and no network was not measured. The other half of the answer is
    /// that the same question is asked again in <c>eng/pack-nuget-packages.sh</c>, where it refuses
    /// rather than skips; see <see cref="PackScript_RefusesAVersionTheFeedAlreadyServes"/>.
    /// </para>
    /// <para>
    /// THE SKIP IS DECIDED AT DISCOVERY, by <see cref="FeedReachableFactAttribute"/>, for the reason
    /// the staged half records: xUnit v2 has no runtime skip at all. The survey therefore runs twice
    /// per test run, once to decide and once to assert over — two requests, measured at 0.3 to 0.45 s
    /// each. It is deliberately NOT memoised: a probe that reached the feed at discovery and failed at
    /// execution is a BROKEN INSTRUMENT, and the first assertion below turns that into a named red
    /// instead of a skip granted by a healthier past.
    /// </para>
    /// <para>
    /// WHAT THIS DOES NOT COVER: the GitHub Packages feed that <c>publish-nuget.yml</c> pushes to. A
    /// number can be spent there and free here; this is keyed on nuget.org because that is the feed
    /// whose artefacts are public and immutable.
    /// </para>
    /// <para>
    /// AND WHAT IT DOES NOT COVER IN THE OTHER DIRECTION — the POPULATION, which the paragraph above
    /// about a misspelled id does not imply. This probe asks about ONE id, the lead package read from
    /// <c>src/Tempo.Blazor/Tempo.Blazor.csproj</c>, while <c>eng/nuget-packages.txt</c> lists 26. A
    /// PARTIAL release — the state <c>eng/push-nuget-packages.sh</c> exists for, where a push died
    /// part-way through an alphabetical glob — is therefore invisible here whenever Tempo.Blazor is
    /// among the ids that did not get pushed. That gap is closed in <c>eng/pack-nuget-packages.sh</c>,
    /// which asks the same question over every id in the manifest; it is deliberately NOT closed here,
    /// because this probe runs twice on every test run and 26 ids would put 52 requests on the path of
    /// a suite whose affordable failure mode is a skip rather than a refusal.
    /// </para>
    /// </summary>
    [FeedReachableFact]
    public void AnnouncedVersion_IsNotAlreadyPublishedOnTheFeed()
    {
        var survey = PublishedVersionSurvey.Take();

        _output.WriteLine(survey.Report);

        using (new AssertionScope())
        {
            survey.Unreachable.Should().BeNull(
                "this test only runs when the feed answered — FeedReachableFactAttribute skips it "
                + "otherwise. Reaching here without an answer means either the attribute was removed, "
                + "which turns 'nobody asked' back into 'the number is free', or the feed stopped "
                + "answering between discovery and execution, which makes this run's answer unknown "
                + $"rather than negative ({survey.Report})");

            survey.Status.Should().Be(
                200,
                "the membership question below is only meaningful over a list the feed actually served. "
                + "A 404 here means nuget.org does not know this package id, which read as an answer "
                + $"would report every version number as free for as long as the typo lived ({survey.Report})");

            survey.Versions.Should().NotBeEmpty(
                "an empty version list and a list not containing the announced number produce the same "
                + "green, and only one of them is evidence; the positive control for this probe is that "
                + $"it can see any versions at all ({survey.Report})");

            $"{survey.Announced} -> {(survey.Versions.Contains(survey.Announced) ? AlreadyOnFeed : StillFree)}"
                .Should().Be(
                    $"{survey.Announced} -> {StillFree}",
                    "a published version number is spent for good: the artefact on nuget.org is "
                    + "immutable, so re-announcing that number ships different bytes under a label "
                    + "consumers have already resolved to something else. No retag and no repack can "
                    + "reach what is already on the feed — bump the changelog and every packable csproj "
                    + $"to the next free number ({survey.Report})");
        }
    }

    /// <summary>
    /// The pack script refuses a version the feed already serves, and refuses to guess when it cannot
    /// ask.
    /// <para>
    /// WHY THE SAME QUESTION LIVES IN TWO PLACES, said plainly so neither is read as redundant.
    /// <see cref="AnnouncedVersion_IsNotAlreadyPublishedOnTheFeed"/> asks it on every test run, which
    /// is earlier and cheaper, and SKIPS when the feed does not answer — a suite that cannot run
    /// offline gets run less, and that is the affordable failure mode there. The pack script REFUSES
    /// instead, because by then the alternative is shipping. Same guard, opposite failure modes, each
    /// placed where its own failure mode costs least.
    /// </para>
    /// <para>
    /// AND WHY THIS IS A TEXT ASSERTION, by necessity rather than preference: a unit test cannot
    /// observe a pack. WHAT A GREEN HERE PROVES is that the clauses are PRESENT in the script — not
    /// that they fire. That they fire was measured by running the script with a version the feed
    /// serves and with one it does not, which this test has no pack run to do. It is the same admitted
    /// limit as the sibling guard over the dirty-tree clause, and it is written down for the same
    /// reason: an unstated limit gets read as a stronger claim than anyone measured.
    /// </para>
    /// <para>
    /// THE NEEDLES RUN OVER THE SCRIPT'S CODE, NOT ITS FULL TEXT, and the projection earns that for
    /// <c>ALLOW_UNVERIFIED_VERSION</c>: that one occurs in the comment block explaining the clause as
    /// well as in the code — deliberately, the block is the record of why the escape exists — so
    /// asserting over the whole file would let "delete the code, keep the prose" stay green for it,
    /// and prose is what survives a hasty revert. The other four are code-only today (measured), so
    /// over them the projection costs nothing and starts earning the day somebody explains one of
    /// them in a comment.
    /// </para>
    /// </summary>
    [Fact]
    public void PackScript_RefusesAVersionTheFeedAlreadyServes()
    {
        var packScript = File.ReadAllText(Path.Combine([FindRepoRoot(), .. PackScriptPath]));
        var packScriptCode = string.Join(
            '\n',
            packScript.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

        using (new AssertionScope())
        {
            packScriptCode.Should().Contain(
                "v3-flatcontainer",
                "the last step before a push has to ask the feed whether this number is already spent; "
                + "the two guards around it compare csproj against CHANGELOG and each nuspec against "
                + "HEAD, and both were green while 2.8.19 was being minted a second time");

            packScriptCode.Should().Contain(
                "<PackageId>",
                "the id the feed is asked about must be READ from the csproj that ships it, because the "
                + "flat container answers 404 for an id it does not know — a misspelled one written in "
                + "here would report every version as free, in green, for as long as the typo lived");

            packScriptCode.Should().Contain(
                "\"$feed_status\" != \"200\"",
                "measured offline, the probe returns nothing and 'the announced version is not in the "
                + "list' comes out TRUE — so status and population are checked BEFORE membership. "
                + "Without that order the check is at its greenest exactly when it is blind");

            packScriptCode.Should().Contain(
                "ALLOW_UNVERIFIED_VERSION",
                "the refusal needs a named, explicit escape or the next person under time pressure "
                + "deletes the check instead; and this escape covers ONLY the case where the question "
                + "could not be asked, never a number the feed answered with");

            packScriptCode.Should().Contain(
                "is already published on",
                "a version the feed serves is refused outright rather than warned about: the artefact "
                + "under that number is immutable, so there is no escape hatch that could make packing "
                + "it safe. The message names the version, because the next person needs to know which "
                + "number to bump past rather than that something went wrong");
        }
    }

    /// <summary>How an already-published number is SPELLED below; a constant for the same reason as
    /// <see cref="NoSuchTag"/> — two literals that must stay identical are one typo from a check that
    /// can never fail.</summary>
    private const string AlreadyOnFeed = "ALREADY PUBLISHED";

    /// <summary>The other accepted value of that same comparison.</summary>
    private const string StillFree = "not on the feed";

    /// <summary>
    /// What nuget.org serves right now, surveyed once and read by both the skip decision and the
    /// assertions — the same one-survey-two-readers shape as <see cref="ReleaseStagingSurvey"/>, so the
    /// attribute and the test can never be measuring different worlds by different rules.
    /// </summary>
    internal sealed record PublishedVersionSurvey(
        string Announced,
        string PackageId,
        int Status,
        IReadOnlyList<string> Versions,
        long ElapsedMilliseconds,
        string? Unreachable)
    {
        private static readonly TimeSpan FeedTimeout = TimeSpan.FromSeconds(15);

        /// <summary>The flat container index: one request, every version the feed serves. Chosen over a
        /// per-version HEAD because a 404 there cannot tell "this number is free" apart from "this
        /// package id is unknown", and over the registration endpoint because this is the resource
        /// <c>dotnet restore</c> itself resolves against.</summary>
        internal string IndexUrl =>
            $"https://api.nuget.org/v3-flatcontainer/{PackageId}/index.json";

        /// <summary>The population line: printed on every run and quoted into every failure.</summary>
        internal string Report =>
            $"[ReleaseContract] announced={Announced} feed-id={PackageId} feed-status={Status} "
            + $"feed-versions={Versions.Count} announced-on-feed={Versions.Contains(Announced)} "
            + $"elapsed-ms={ElapsedMilliseconds} url={IndexUrl}"
            + (Unreachable is null ? string.Empty : $" :: {Unreachable}");

        internal static PublishedVersionSurvey Take()
        {
            var repositoryRoot = FindRepoRoot();
            var announced = ReadAnnouncedVersion(repositoryRoot);
            var packageId = ReadLeadPackageId(repositoryRoot);
            var stopwatch = Stopwatch.StartNew();
            var probe = new PublishedVersionSurvey(
                announced, packageId, Status: -1, Versions: [], ElapsedMilliseconds: 0,
                Unreachable: "the probe did not run");

            try
            {
                using var client = new HttpClient { Timeout = FeedTimeout };
                using var response = client.GetAsync(probe.IndexUrl).GetAwaiter().GetResult();
                var status = (int)response.StatusCode;
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                stopwatch.Stop();

                var versions = status == 200
                    ? JsonDocument.Parse(body).RootElement.GetProperty("versions").EnumerateArray()
                        .Select(element => element.GetString() ?? string.Empty).ToList()
                    : [];

                return probe with
                {
                    Status = status,
                    Versions = versions,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                    Unreachable = null,
                };
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                stopwatch.Stop();
                return probe with
                {
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                    Unreachable = $"{exception.GetType().Name}: {exception.Message}",
                };
            }
        }

        /// <summary>
        /// The package id this repository actually ships its lead package under, read from the csproj
        /// rather than written here. Lower-cased because the flat container's paths are, and that is a
        /// property of the URL space, not of the id.
        /// </summary>
        private static string ReadLeadPackageId(string repositoryRoot)
        {
            var csproj = File.ReadAllText(
                Path.Combine(repositoryRoot, "src", "Tempo.Blazor", "Tempo.Blazor.csproj"));
            var id = Regex.Match(csproj, @"<PackageId>(?<id>[^<]+)</PackageId>").Groups["id"].Value;

            return id.Length == 0
                ? throw new InvalidOperationException(
                    "src/Tempo.Blazor/Tempo.Blazor.csproj carries no <PackageId>; guessing one here "
                    + "would send this guard at a feed path nobody publishes to, where every number "
                    + "reads as free.")
                : id.Trim().ToLowerInvariant();
        }
    }

    /// <summary>
    /// Marks a guard as SKIPPED when nuget.org did not answer, with the survey line as the reason.
    /// <para>
    /// The MECHANISM is shared with <see cref="StagedPackagesFactAttribute"/> and lives once in
    /// <see cref="ProbeDecidedFactAttribute"/> — including the deliberate catch-returns-null, because
    /// "skip on error" would make every future breakage in this file look like being offline. This class
    /// supplies only the question: did the flat container answer.
    /// </para>
    /// <para>
    /// UNREACHABLE IS NOT THE SAME AS 404. A feed that answers 404 for the id is REACHED, and that path
    /// is deliberately not a skip: it is a broken instrument the decorated member's own assertions must
    /// report. Only a probe that got no answer at all skips.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FeedReachableFactAttribute : ProbeDecidedFactAttribute
    {
        protected override string? ProbeSkipReason() => FeedUnreachableSkipReason();
    }

    /// <summary>
    /// The feed question itself, in one place because it now has TWO callers: this file's
    /// <see cref="FeedReachableFactAttribute"/> and
    /// <see cref="BashScriptFeedReachableFactAttribute"/>, which asks the same question after its own.
    /// Copying the two lines instead would recreate exactly the shape
    /// <see cref="ProbeDecidedFactAttribute"/> was extracted to remove.
    /// </summary>
    /// <returns>The survey line when nuget.org did not answer, or null when it did.</returns>
    internal static string? FeedUnreachableSkipReason()
    {
        var survey = PublishedVersionSurvey.Take();
        return survey.Unreachable is null ? null : survey.Report;
    }
}
