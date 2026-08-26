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
    /// What lies under the announced number on nuget.org is what THIS TREE builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS REPLACES A BLACKLIST WITH A WHITELIST, per <c>DEC-TEMPO-RELEASE-GATE</c> points 6–10. The
    /// previous guard asked "is the announced number already taken" and refused when it was. That
    /// question is a PROXY for the harm — a consumer resolving the announced number to bytes other
    /// than the ones this tree builds — and the proxy demonstrably came apart from the harm: 2.8.22
    /// and 2.8.23 are both on the feed, both were verified byte-for-byte against this repository, and
    /// the old guard was RED over both. A guard that reports failure over a release that arrived
    /// correctly teaches its readers to bump past it, which is how a gate stops being read.
    /// </para>
    /// <para>
    /// THREE STATES, NOT TWO (<c>DEC-VACUOUS-CONSISTENCY</c> point 1). "Absent" and "present and
    /// matching" are both green and they are NOT the same evidence, so they never report the same
    /// word: absent is <c>unpublished</c> — a legal pre-publication state that proves nothing about
    /// delivery — while present-and-matching is <c>verified</c>, which is strictly stronger than
    /// anything the old guard could say, because it positively establishes that the release arrived.
    /// Present-and-different is red. A feed that does not answer is a fourth outcome,
    /// <c>unmeasured:feed-unreachable</c>, decided at discovery by <see cref="FeedReachableFact"/> and
    /// counted as its own state — never folded into green, and never into red either, because a gate
    /// that fails over infrastructure is switched off within a week.
    /// </para>
    /// <para>
    /// THE PACKAGE IS NOT COMPARED AS A FILE. A <c>.nupkg</c> is a zip carrying timestamps, entry
    /// order and a signature block, so byte equality of the ARCHIVE is not achievable in general and a
    /// gate resting on it would go red for reasons that have nothing to do with content. What is
    /// compared is the CONTENT ITEMS, and the denominator is derived from the source rather than
    /// chosen: every file under <c>src/Tempo.Blazor/wwwroot</c>, which the SDK packs to
    /// <c>staticwebassets/&lt;relative path&gt;</c>. Measured on 2.8.23: 168 files in the tree, 168
    /// present in the package, 168 byte-identical. A hand-picked list would shrink to whichever file
    /// somebody once cared about — the <c>MeasuredSites</c> mistake — so the count carries a floor and
    /// a package entry with no counterpart in the tree is REPORTED rather than ignored.
    /// </para>
    /// <para>
    /// WHAT A DIFFERENCE MEANS IS NOT ONE THING, and the message says so, because the two mechanisms
    /// have different cures. (i) The repository moved after the publish — somebody changed the library
    /// without bumping. The cure is a bump and it is the ordinary case. (ii) The published artefact
    /// did not come from the tagged tree at all. That is a supply-chain finding, it is the graver of
    /// the two, and it must NOT be quietly disposed of by bumping: the push is a named manual step and
    /// this is the only instrument that can say anything about it.
    /// </para>
    /// <para>
    /// SCOPE, stated so a green is not read as more than it is: ONE package id, the lead
    /// <c>Tempo.Blazor</c>, and only its <c>wwwroot</c> content. The other 25 ids in
    /// <c>eng/nuget-packages.txt</c> and the compiled assemblies are outside it — assemblies are not
    /// reproducible byte-for-byte from a clean build here, so comparing them would report noise as
    /// provenance. Widening the id set costs a download per id on every test run.
    /// </para>
    /// </remarks>
    [FeedReachableFact]
    public void AnnouncedVersion_OnTheFeed_CarriesWhatThisTreeBuilds()
    {
        var survey = PublishedVersionSurvey.Take();
        _output.WriteLine(survey.Report);

        using (new AssertionScope())
        {
            survey.Unreachable.Should().BeNull(
                "this test only runs when the feed answered — FeedReachableFactAttribute skips it "
                + "otherwise with unmeasured:feed-unreachable. Reaching here without an answer means "
                + "the feed stopped answering between discovery and execution, which makes this run's "
                + $"answer UNKNOWN rather than favourable ({survey.Report})");

            survey.Status.Should().Be(
                200,
                "the membership question below is only meaningful over a list the feed actually served. "
                + "A 404 here means nuget.org does not know this package id, which read as an answer "
                + $"would report every number as unpublished for as long as the typo lived ({survey.Report})");

            survey.Versions.Should().NotBeEmpty(
                "an empty version list and a list without the announced number produce the same green, "
                + $"and only one of them is evidence ({survey.Report})");
        }

        if (!survey.Versions.Contains(survey.Announced))
        {
            // State one. Legal, green, and deliberately not called "verified": nothing has been
            // delivered yet, so there is nothing to have provenance over.
            _output.WriteLine($"[Provenance] {survey.Announced} -> {Unpublished}");
            return;
        }

        var provenance = PackageProvenance.Take(survey.PackageId, survey.Announced);
        _output.WriteLine(provenance.Report);

        using (new AssertionScope())
        {
            provenance.Unreachable.Should().BeNull(
                "the index answered but the package itself did not, so this run measured NOTHING about "
                + $"provenance — that is unmeasured:package-unreachable, not a verdict ({provenance.Report})");

            provenance.TreeFileCount.Should().BeGreaterThanOrEqualTo(
                PackedContentFloor,
                "the denominator is every file under src/Tempo.Blazor/wwwroot, and it has never been "
                + "smaller than this. A sudden shrink means the sweep is reading the wrong directory, "
                + $"and a sweep over nothing is green ({provenance.Report})");

            provenance.Missing.Should().BeEmpty(
                "a file this tree builds and the published package does not carry is a provenance "
                + $"failure, not a rounding difference ({provenance.Report})");

            provenance.Differing.Should().BeEmpty(
                "WHAT LIES UNDER {0} ON THE FEED IS NOT WHAT THIS TREE BUILDS. Two mechanisms produce "
                + "this and the cure differs. (i) The repository moved after the publish: somebody "
                + "changed the library without bumping. Cure: bump the changelog and every packable "
                + "csproj, because the number is spent. (ii) The published artefact did not come from "
                + "the tagged tree. That is a SUPPLY-CHAIN finding and it must NOT be disposed of by "
                + "bumping — compare the tag, the commit recorded in the nuspec and the pushed "
                + "artefact before anything else. Decide WHICH before choosing the cure ({1})",
                survey.Announced, provenance.Report);
        }

        _output.WriteLine($"[Provenance] {survey.Announced} -> {Verified}");
    }

    /// <summary>
    /// The three outcomes this guard reports, as words rather than as a boolean, so a reader of a log
    /// can tell "nothing has been published yet" from "the published thing was checked".
    /// </summary>
    private const string Unpublished = "unpublished";

    private const string Verified = "verified";

    /// <summary>
    /// Floor for the content denominator. 168 files were measured under
    /// <c>src/Tempo.Blazor/wwwroot</c> on 2.8.23; the floor sits below that so ordinary deletions do
    /// not trip it, and far enough above zero that a sweep reading the wrong path cannot pass.
    /// </summary>
    private const int PackedContentFloor = 120;

    /// <summary>
    /// The pack script refuses a version the feed already serves, and refuses to guess when it cannot
    /// ask.
    /// <para>
    /// WHY THE SAME QUESTION LIVES IN TWO PLACES, said plainly so neither is read as redundant.
    /// <see cref="AnnouncedVersion_OnTheFeed_CarriesWhatThisTreeBuilds"/> asks it on every test run, which
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



    /// <summary>
    /// What nuget.org serves right now, surveyed once and read by both the skip decision and the
    /// assertions — the same one-survey-two-readers shape as <see cref="ReleaseStagingSurvey"/>, so the
    /// attribute and the test can never be measuring different worlds by different rules.
    /// </summary>
    /// <summary>
    /// Downloads the published package for one version and compares its CONTENT ITEMS against the
    /// working tree. Never compares the archive itself — see the guard's remark for why that is not a
    /// well-defined question.
    /// </summary>
    /// <param name="PackageId">The lead package id, taken from the same survey the index came from.</param>
    /// <param name="Version">The announced version this provenance is about.</param>
    /// <param name="TreeFileCount">The denominator: files found under <c>wwwroot</c>.</param>
    /// <param name="Matching">Items present in both and byte-identical.</param>
    /// <param name="Differing">Items present in both whose bytes differ — the finding.</param>
    /// <param name="Missing">Items the tree builds that the package does not carry.</param>
    /// <param name="ExtraInPackage">
    /// Items the package carries with no counterpart in <c>wwwroot</c>. NOT a finding: the SDK
    /// generates the scoped-CSS bundle and packs component-colocated <c>.razor.js</c> from outside
    /// that directory (5 of them on 2.8.23). Reported rather than dropped, because "the sweep ignored
    /// something" and "there was nothing to ignore" must not look alike.
    /// </param>
    /// <param name="Unreachable">Why the package could not be read, when it could not.</param>
    internal sealed record PackageProvenance(
        string PackageId,
        string Version,
        int TreeFileCount,
        IReadOnlyList<string> Matching,
        IReadOnlyList<string> Differing,
        IReadOnlyList<string> Missing,
        IReadOnlyList<string> ExtraInPackage,
        long ElapsedMilliseconds,
        string? Unreachable)
    {
        /// <summary>Where a Razor class library's <c>wwwroot</c> lands inside the package.</summary>
        private const string StaticWebAssetRoot = "staticwebassets/";

        internal string PackageUrl =>
            $"https://api.nuget.org/v3-flatcontainer/{PackageId.ToLowerInvariant()}/{Version}/"
            + $"{PackageId.ToLowerInvariant()}.{Version}.nupkg";

        internal string Report =>
            $"[Provenance] version={Version} tree-files={TreeFileCount} matching={Matching.Count} "
            + $"differing={Differing.Count} missing={Missing.Count} extra-in-package={ExtraInPackage.Count} "
            + $"elapsed-ms={ElapsedMilliseconds} url={PackageUrl}"
            + (Differing.Count == 0 ? string.Empty : $" :: differing={string.Join(",", Differing.Take(10))}")
            + (Missing.Count == 0 ? string.Empty : $" :: missing={string.Join(",", Missing.Take(10))}")
            + (Unreachable is null ? string.Empty : $" :: unmeasured:package-unreachable {Unreachable}");

        internal static PackageProvenance Take(string packageId, string version)
        {
            var stopwatch = Stopwatch.StartNew();
            var tree = TreeContent();

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                var empty = new PackageProvenance(packageId, version, tree.Count, [], [], [], [], 0, null);
                using var response = client.GetAsync(empty.PackageUrl).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    return empty with
                    {
                        ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                        Unreachable = $"HTTP {(int)response.StatusCode}",
                    };
                }

                using var stream = new MemoryStream(response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
                using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

                var packed = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith(StaticWebAssetRoot, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using var content = entry.Open();
                    using var buffer = new MemoryStream();
                    content.CopyTo(buffer);
                    packed[entry.FullName[StaticWebAssetRoot.Length..]] = Hash(buffer.ToArray());
                }

                return Compare(packageId, version, tree, packed, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                                              or InvalidDataException or IOException)
            {
                return new PackageProvenance(
                    packageId, version, tree.Count, [], [], [], [], stopwatch.ElapsedMilliseconds,
                    $"{error.GetType().Name}: {error.Message}");
            }
        }

        /// <summary>
        /// The comparison itself, over two dictionaries and nothing else.
        /// </summary>
        /// <remarks>
        /// SEPARATED FROM THE DOWNLOAD ON PURPOSE. The network path only runs when the announced number
        /// is already published, which for most of a release cycle it is not — so without this the
        /// whole comparison would sit untested behind a branch that a green run never enters, and the
        /// guard would report success having executed nothing. <c>ProvenanceComparisonTests</c> drives
        /// it directly through all four outcomes.
        /// </remarks>
        internal static PackageProvenance Compare(
            string packageId,
            string version,
            IReadOnlyDictionary<string, string> tree,
            IReadOnlyDictionary<string, string> packed,
            long elapsedMilliseconds)
        {
            List<string> matching = [], differing = [], missing = [];
            foreach (var (relative, hash) in tree.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!packed.TryGetValue(relative, out var packedHash))
                {
                    missing.Add(relative);
                }
                else if (string.Equals(packedHash, hash, StringComparison.Ordinal))
                {
                    matching.Add(relative);
                }
                else
                {
                    differing.Add(relative);
                }
            }

            var extra = packed.Keys.Where(key => !tree.ContainsKey(key)).Order(StringComparer.Ordinal).ToList();

            return new PackageProvenance(
                packageId, version, tree.Count, matching, differing, missing, extra, elapsedMilliseconds, null);
        }

        /// <summary>Exposes the source-derived denominator so its own guard can measure it.</summary>
        internal static IReadOnlyDictionary<string, string> TreeContentForTests() => TreeContent();

        /// <summary>
        /// The denominator, derived from the source tree: every file the SDK packs out of the lead
        /// package's <c>wwwroot</c>. Enumerated, never listed by hand.
        /// </summary>
        private static Dictionary<string, string> TreeContent()
        {
            var root = Path.Combine(FindRepoRoot(), "src", "Tempo.Blazor", "wwwroot");
            var content = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                content[relative] = Hash(File.ReadAllBytes(file));
            }

            return content;
        }

        private static string Hash(byte[] bytes) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

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
