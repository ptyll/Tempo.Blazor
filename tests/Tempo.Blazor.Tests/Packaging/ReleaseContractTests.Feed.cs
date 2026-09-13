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
    /// What lies under the announced number on nuget.org is what THIS TREE builds — for every id the
    /// manifest publishes, not for the lead package alone.
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
    /// FOUR OUTCOMES PER ID, NOT TWO (<c>DEC-VACUOUS-CONSISTENCY</c> point 1). "Absent" and "present
    /// and matching" are both green and they are NOT the same evidence, so they never report the same
    /// word: absent is <c>unpublished</c> — a legal pre-publication state that proves nothing about
    /// delivery — while present-and-matching is <c>verified</c>, which is strictly stronger than
    /// anything the old guard could say, because it positively establishes that the release arrived.
    /// Present-and-different is red. An id the feed did not answer for is a fourth outcome,
    /// <c>unmeasured:&lt;id&gt;</c> — REPORTED and tolerated for the 25 non-lead ids (the same limit
    /// the pack script's manifest sweep states), while the LEAD id not answering skips the whole
    /// guard at discovery through <see cref="FeedReachableFact"/> — a network outage can never dress
    /// itself as a version check.
    /// </para>
    /// <para>
    /// THE PACKAGE IS NOT COMPARED AS A FILE. A <c>.nupkg</c> is a zip carrying timestamps, entry
    /// order and a signature block, so byte equality of the ARCHIVE is not achievable in general and a
    /// gate resting on it would go red for reasons that have nothing to do with content. What is
    /// compared is the CONTENT ITEMS, and the denominator is derived from the source rather than
    /// chosen: every file under the package's own <c>src/&lt;project&gt;/wwwroot</c>, which the SDK
    /// packs to <c>staticwebassets/&lt;relative path&gt;</c>. Measured on 2.8.23 for the lead: 168
    /// files in the tree, 168 present in the package, 168 byte-identical. A hand-picked list would
    /// shrink to whichever file somebody once cared about — the <c>MeasuredSites</c> mistake — so the
    /// count carries a floor and a package entry with no counterpart in the tree is REPORTED rather
    /// than ignored.
    /// </para>
    /// <para>
    /// WHAT THE COMPARISON CANNOT SEE IS REGISTERED, NOT IGNORED: everything under <c>lib/</c> — the
    /// compiled assemblies — is counted and reported as <c>nonreproducible</c>, because assemblies
    /// are not byte-reproducible from a clean build here and comparing them would report noise as
    /// provenance. For the 25 non-lead ids <c>lib/**</c> is usually the WHOLE payload; the registry
    /// row is what keeps "this id was measured" apart from "this id had nothing to measure".
    /// </para>
    /// <para>
    /// AND THE ARTEFACT IS ASKED FOR BY ITS EXACT URL BEFORE ITS BYTES ARE. Membership in the index
    /// and existence of the <c>{id}/{version}/{id}.{version}.nupkg</c> object are different questions
    /// — a CDN that has propagated the index but not the package, or the reverse, is a real state of
    /// the feed — so the guard sends a HEAD to the exact URL first. A non-200 there is
    /// <c>unmeasured:package-unreachable</c>, not a verdict: the run then says it could not read the
    /// artefact rather than reporting anything about its content.
    /// </para>
    /// <para>
    /// WHAT A DIFFERENCE MEANS IS NOT ONE THING, and the message says so, because the two mechanisms
    /// have different cures. (i) The repository moved after the publish — somebody changed the library
    /// without bumping. The cure is a bump and it is the ordinary case. (ii) The published artefact
    /// did not come from the tagged tree at all. That is a supply-chain finding, it is the graver of
    /// the two, and it must NOT be quietly disposed of by bumping: the push is a named manual step and
    /// this is the only instrument that can say anything about it.
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

            survey.Answers.Should().HaveCount(
                survey.ManifestCount,
                "every manifest id gets its own index answer — a sweep that reads fewer than the "
                + $"manifest holds is the one-id question wearing a wider name ({survey.Report})");
        }

        var verified = new List<string>();
        var unpublished = new List<string>();
        var unmeasured = new List<string>();
        var differing = new List<string>();
        var outcomes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var answer in survey.Answers)
        {
            var outcome = Evaluate(answer, survey.Announced,
                isLead: string.Equals(answer.Id, survey.PackageId, StringComparison.Ordinal));
            outcomes[answer.Id] = outcome;
            _output.WriteLine($"[Provenance] {answer.Id} -> {outcome}");
            switch (outcome)
            {
                case var o when o.StartsWith(Verified, StringComparison.Ordinal):
                    verified.Add(answer.Id);
                    break;
                case var o when o.StartsWith("unmeasured", StringComparison.Ordinal):
                    unmeasured.Add($"{answer.Id}: {outcome}");
                    break;
                case var o when o.StartsWith("different", StringComparison.Ordinal):
                    differing.Add($"{answer.Id}: {outcome}");
                    break;
                default:
                    unpublished.Add(answer.Id);
                    break;
            }
        }

        using (new AssertionScope())
        {
            outcomes.TryGetValue(survey.PackageId, out var leadOutcome).Should().BeTrue(
                "the lead id is a manifest row — a survey that can answer for 25 satellites and not "
                + $"for the reach control is reading a different manifest ({survey.Report})");

            leadOutcome.Should().NotStartWith(
                "unmeasured",
                "the LEAD id is the reach control: a feed that answers the satellites but not the "
                + $"lead, or a lead index that serves a non-200, cannot produce any verdict about "
                + $"the announced number — lead outcome: {leadOutcome} ({survey.Report})");

            differing.Should().BeEmpty(
                "WHAT LIES UNDER {0} ON THE FEED IS NOT WHAT THIS TREE BUILDS. Two mechanisms produce "
                + "this and the cure differs. (i) The repository moved after the publish: somebody "
                + "changed the library without bumping. Cure: bump the changelog and every packable "
                + "csproj, because the number is spent. (ii) The published artefact did not come from "
                + "the tagged tree. That is a SUPPLY-CHAIN finding and it must NOT be disposed of by "
                + "bumping — compare the tag, the commit recorded in the nuspec and the pushed "
                + "artefact before anything else. Decide WHICH before choosing the cure: {1}",
                survey.Announced, string.Join(" | ", differing));
        }

        _output.WriteLine(
            $"[Provenance] {survey.Announced} :: verified={verified.Count} unpublished={unpublished.Count} "
            + $"unmeasured={unmeasured.Count} differing={differing.Count}"
            + (unmeasured.Count == 0 ? string.Empty : $" :: not-asked: {string.Join(", ", unmeasured)}"));
    }

    /// <summary>
    /// One manifest id's verdict: <c>unpublished</c>, <c>verified</c>, <c>unmeasured:…</c> or
    /// <c>different:…</c>. Written as a word plus the detail, because "the feed did not answer for
    /// this id" and "the package does not match" must never share a colour.
    /// </summary>
    private string Evaluate(
        ManifestAnswer answer, string announced, bool isLead)
    {
        var verdict = IndexVerdict(answer, announced, isLead);
        if (verdict is not CompareContent)
        {
            return verdict;
        }

        var provenance = PackageProvenance.Take(answer.Id, announced, answer.ProjectDirectory);
        _output.WriteLine(provenance.Report);

        if (provenance.Unreachable is not null)
        {
            return $"unmeasured:package-unreachable {provenance.Unreachable}";
        }

        if (isLead && provenance.TreeFileCount < PackedContentFloor)
        {
            return $"different: lead denominator {provenance.TreeFileCount} below floor "
                + $"{PackedContentFloor} — the sweep is reading the wrong directory ({provenance.Report})";
        }

        if (provenance.Differing.Count > 0 || provenance.Missing.Count > 0)
        {
            return $"different: {provenance.Report}";
        }

        return Verified;
    }

    /// <summary>The sentinel <see cref="IndexVerdict"/> returns when the index says the announced
    /// number is live for this id and only the package bytes can say more.</summary>
    internal const string CompareContent = "compare-content";

    /// <summary>
    /// What an index answer alone decides: <c>unpublished</c>, <c>unmeasured:…</c>, or
    /// <see cref="CompareContent"/> when the announced version IS listed and the package itself must
    /// be fetched. Pure over the answer record so every arm — including the ones a green run never
    /// reaches — is driven by <c>ProvenanceComparisonTests</c> without a network.
    /// </summary>
    internal static string IndexVerdict(ManifestAnswer answer, string announced, bool isLead)
    {
        if (answer.Unreachable is not null)
        {
            return $"unmeasured:{answer.Unreachable}";
        }

        if (isLead && answer.Status != 200)
        {
            // A lead that serves anything but a version list — a 404 means the id is unknown and
            // EVERY number reads as free — cannot produce a verdict, only a broken instrument.
            return $"unmeasured:lead-index-http-{answer.Status}";
        }

        if (answer.Status == 404)
        {
            return $"{Unpublished} (id never published)";
        }

        if (answer.Status != 200 || answer.Versions.Count == 0)
        {
            // An empty 200 list and a non-200 answer are the same amount of information: none.
            return $"unmeasured:index-http-{answer.Status}";
        }

        return answer.Versions.Contains(announced) ? CompareContent : Unpublished;
    }

    /// <summary>
    /// The three outcomes this guard reports, as words rather than as a boolean, so a reader of a log
    /// can tell "nothing has been published yet" from "the published thing was checked".
    /// </summary>
    private const string Unpublished = "unpublished";

    private const string Verified = "verified";

    /// <summary>
    /// Floor for the LEAD package's content denominator. 168 files were measured under
    /// <c>src/Tempo.Blazor/wwwroot</c> on 2.8.23; the floor sits below that so ordinary deletions do
    /// not trip it, and far enough above zero that a sweep reading the wrong path cannot pass. The
    /// satellite ids carry no floor — several legitimately ship <c>lib/**</c> and nothing else.
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
    /// <param name="PackageId">The package id this provenance is about — one row per manifest entry.</param>
    /// <param name="Version">The announced version this provenance is about.</param>
    /// <param name="TreeFileCount">The denominator: files found under the package's <c>wwwroot</c>.</param>
    /// <param name="Matching">Items present in both and byte-identical.</param>
    /// <param name="Differing">Items present in both whose bytes differ — the finding.</param>
    /// <param name="Missing">Items the tree builds that the package does not carry.</param>
    /// <param name="ExtraInPackage">
    /// Items the package carries with no counterpart in <c>wwwroot</c>. NOT a finding: the SDK
    /// generates the scoped-CSS bundle and packs component-colocated <c>.razor.js</c> from outside
    /// that directory (5 of them on 2.8.23). Reported rather than dropped, because "the sweep ignored
    /// something" and "there was nothing to ignore" must not look alike.
    /// </param>
    /// <param name="NonReproducible">
    /// The registry row for what the comparison CANNOT see: every entry under <c>lib/</c> — the
    /// compiled assemblies. They are counted and named because "no comparable content" and "content
    /// was skipped" must not produce the same report. For most satellite ids this is the entire
    /// payload.
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
        IReadOnlyList<string> NonReproducible,
        long ElapsedMilliseconds,
        string? Unreachable)
    {
        /// <summary>Where a Razor class library's <c>wwwroot</c> lands inside the package.</summary>
        private const string StaticWebAssetRoot = "staticwebassets/";

        /// <summary>Where the compiled assemblies sit — counted, never compared.</summary>
        private const string LibraryRoot = "lib/";

        internal string PackageUrl =>
            $"https://api.nuget.org/v3-flatcontainer/{PackageId.ToLowerInvariant()}/{Version}/"
            + $"{PackageId.ToLowerInvariant()}.{Version}.nupkg";

        internal string Report =>
            $"[Provenance] id={PackageId} version={Version} tree-files={TreeFileCount} matching={Matching.Count} "
            + $"differing={Differing.Count} missing={Missing.Count} extra-in-package={ExtraInPackage.Count} "
            + $"nonreproducible={NonReproducible.Count} "
            + $"elapsed-ms={ElapsedMilliseconds} url={PackageUrl}"
            + (Differing.Count == 0 ? string.Empty : $" :: differing={string.Join(",", Differing.Take(10))}")
            + (Missing.Count == 0 ? string.Empty : $" :: missing={string.Join(",", Missing.Take(10))}")
            + (Unreachable is null ? string.Empty : $" :: unmeasured:package-unreachable {Unreachable}");

        internal static PackageProvenance Take(string packageId, string version, string projectDirectory)
        {
            var stopwatch = Stopwatch.StartNew();
            var tree = TreeContent(projectDirectory);

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                var empty = new PackageProvenance(
                    packageId, version, tree.Count, [], [], [], [], [], 0, null);

                // THE ARTEFACT IS CONFIRMED BY ITS EXACT URL BEFORE IT IS DOWNLOADED. The index
                // listing the version and the package object existing are two different states of
                // the feed; a HEAD on the exact .nupkg URL is the cheap question that tells them
                // apart, and a non-200 there is unmeasured — never a verdict about content.
                using var head = client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, empty.PackageUrl)).GetAwaiter().GetResult();
                if (!head.IsSuccessStatusCode)
                {
                    return empty with
                    {
                        ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                        Unreachable = $"HEAD {(int)head.StatusCode} on {empty.PackageUrl}",
                    };
                }

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
                var nonReproducible = new List<string>();
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.StartsWith(LibraryRoot, StringComparison.Ordinal))
                    {
                        nonReproducible.Add(entry.FullName);
                        continue;
                    }

                    if (!entry.FullName.StartsWith(StaticWebAssetRoot, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using var content = entry.Open();
                    using var buffer = new MemoryStream();
                    content.CopyTo(buffer);
                    packed[entry.FullName[StaticWebAssetRoot.Length..]] = Hash(buffer.ToArray());
                }

                return Compare(packageId, version, tree, packed, nonReproducible,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                                              or InvalidDataException or IOException)
            {
                return new PackageProvenance(
                    packageId, version, tree.Count, [], [], [], [], [], stopwatch.ElapsedMilliseconds,
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
            IReadOnlyList<string> nonReproducible,
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
                packageId, version, tree.Count, matching, differing, missing, extra,
                nonReproducible, elapsedMilliseconds, null);
        }

        /// <summary>Exposes the source-derived denominator so its own guard can measure it.</summary>
        internal static IReadOnlyDictionary<string, string> TreeContentForTests() =>
            TreeContent("Tempo.Blazor");

        /// <summary>
        /// The denominator, derived from the source tree: every file the SDK packs out of the
        /// package's own <c>src/&lt;projectDirectory&gt;/wwwroot</c>. Enumerated, never listed by
        /// hand. A project with no <c>wwwroot</c> yields an empty denominator — a package that is
        /// all <c>lib/**</c> then reports exactly that, rather than pretending to have been compared.
        /// </summary>
        private static Dictionary<string, string> TreeContent(string projectDirectory)
        {
            var root = Path.Combine(FindRepoRoot(), "src", projectDirectory, "wwwroot");
            var content = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(root))
            {
                return content;
            }

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

    /// <summary>One manifest id's index answer: its status, its version list, and the project
    /// directory its <c>wwwroot</c> denominator is read from.</summary>
    /// <param name="Id">The package id, lower-cased — the flat container's paths are.</param>
    /// <param name="ProjectDirectory">The manifest row's directory under <c>src/</c>.</param>
    /// <param name="Status">The index's HTTP status; -1 when the question was never asked.</param>
    /// <param name="Versions">The version list a 200 served; empty otherwise.</param>
    /// <param name="Unreachable">Why this id could not be asked, when it could not — package-level.</param>
    internal sealed record ManifestAnswer(
        string Id,
        string ProjectDirectory,
        int Status,
        IReadOnlyList<string> Versions,
        string? Unreachable)
    {
        /// <summary>The flat container index: one request, every version the feed serves. Chosen over a
        /// per-version HEAD because a 404 there cannot tell "this number is free" apart from "this
        /// package id is unknown", and over the registration endpoint because this is the resource
        /// <c>dotnet restore</c> itself resolves against.</summary>
        internal string IndexUrl =>
            $"https://api.nuget.org/v3-flatcontainer/{Id}/index.json";
    }

    /// <summary>
    /// What nuget.org serves right now for EVERY id the manifest publishes, surveyed once and read by
    /// both the skip decision and the assertions — the same one-survey-two-readers shape as
    /// <see cref="ReleaseStagingSurvey"/>, so the attribute and the test can never be measuring
    /// different worlds by different rules.
    /// </summary>
    /// <param name="Announced">The version the changelog announces.</param>
    /// <param name="PackageId">The lead package id — the reach control.</param>
    /// <param name="ManifestCount">How many entries the manifest holds — the denominator the sweep must cover.</param>
    /// <param name="Answers">One index answer per manifest id, in manifest order.</param>
    /// <param name="Unreachable">Why the LEAD id could not be asked, when it could not — the discovery-time skip reason.</param>
    internal sealed record PublishedVersionSurvey(
        string Announced,
        string PackageId,
        int ManifestCount,
        IReadOnlyList<ManifestAnswer> Answers,
        long ElapsedMilliseconds,
        string? Unreachable)
    {
        private static readonly TimeSpan FeedTimeout = TimeSpan.FromSeconds(15);

        /// <summary>The population line: printed on every run and quoted into every failure.</summary>
        internal string Report =>
            $"[ReleaseContract] announced={Announced} lead={PackageId} manifest-ids={ManifestCount} "
            + $"answered={Answers.Count(a => a.Status == 200)} "
            + $"unpublished={Answers.Count(a => a.Status == 404)} "
            + $"not-answered={Answers.Count(a => a.Status != 200 && a.Status != 404)} "
            + $"announced-on-feed={Answers.Count(a => a.Versions.Contains(Announced))} "
            + $"elapsed-ms={ElapsedMilliseconds}"
            + (Unreachable is null ? string.Empty : $" :: {Unreachable}");

        internal static PublishedVersionSurvey Take()
        {
            var repositoryRoot = FindRepoRoot();
            var announced = ReadAnnouncedVersion(repositoryRoot);
            var stopwatch = Stopwatch.StartNew();

            var manifestProjects = ReadManifestProjects(repositoryRoot);
            var answers = new List<ManifestAnswer>();

            using var client = new HttpClient { Timeout = FeedTimeout };
            string? leadUnreachable = null;

            foreach (var (projectPath, projectDirectory) in manifestProjects)
            {
                var id = ReadPackageId(repositoryRoot, projectPath);
                if (id is null)
                {
                    answers.Add(new ManifestAnswer(
                        $"({Path.GetFileNameWithoutExtension(projectPath)})",
                        projectDirectory, Status: -1, Versions: [],
                        Unreachable: "the manifest row's csproj carries no <PackageId> — an id that "
                            + "cannot be read cannot be asked, and is never read as free"));
                    continue;
                }

                var probe = new ManifestAnswer(id, projectDirectory, Status: -1, Versions: [],
                    Unreachable: "the probe did not run");
                try
                {
                    using var response = client.GetAsync(probe.IndexUrl).GetAwaiter().GetResult();
                    var status = (int)response.StatusCode;
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    var versions = status == 200
                        ? JsonDocument.Parse(body).RootElement.GetProperty("versions").EnumerateArray()
                            .Select(element => element.GetString() ?? string.Empty).ToList()
                        : (IReadOnlyList<string>)[];

                    answers.Add(probe with { Status = status, Versions = versions, Unreachable = null });
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or TaskCanceledException or JsonException)
                {
                    answers.Add(probe with
                    {
                        Unreachable = $"{exception.GetType().Name}: {exception.Message}",
                    });
                }
            }

            var lead = answers.FirstOrDefault(
                a => string.Equals(a.Id, ReadLeadPackageId(repositoryRoot), StringComparison.Ordinal));
            leadUnreachable = lead?.Unreachable
                ?? (lead is null
                    ? "the lead id is not among the manifest answers — the reach control never ran"
                    : null);

            return new PublishedVersionSurvey(
                announced,
                ReadLeadPackageId(repositoryRoot),
                manifestProjects.Count,
                answers,
                stopwatch.ElapsedMilliseconds,
                leadUnreachable);
        }

        /// <summary>
        /// Every manifest row as (project path, project directory under <c>src/</c>) — the same read
        /// the pack script's loop performs, so the two populations can never drift.
        /// </summary>
        private static IReadOnlyList<(string ProjectPath, string ProjectDirectory)> ReadManifestProjects(
            string repositoryRoot) =>
            File.ReadAllLines(Path.Combine([repositoryRoot, .. ManifestPath]))
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(line => line.Replace('\\', '/'))
                .Select(relative => (relative,
                    Path.GetDirectoryName(relative)?.Replace('\\', '/')
                        .Split('/', StringSplitOptions.RemoveEmptyEntries).Last() ?? relative))
                .ToList();

        /// <summary>
        /// The package id a manifest row ships under, read from its own csproj. Lower-cased because
        /// the flat container's paths are — a property of the URL space, not of the id. A missing
        /// id is null: the caller records it as an unasked row rather than guessing one.
        /// </summary>
        private static string? ReadPackageId(string repositoryRoot, string relativeProjectPath)
        {
            var path = Path.Combine(repositoryRoot, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                return null;
            }

            var id = Regex.Match(File.ReadAllText(path), @"<PackageId>(?<id>[^<]+)</PackageId>")
                .Groups["id"].Value;
            return id.Length == 0 ? null : id.Trim().ToLowerInvariant();
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
