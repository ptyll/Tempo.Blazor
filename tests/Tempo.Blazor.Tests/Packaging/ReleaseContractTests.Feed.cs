using FluentAssertions.Execution;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
    /// chosen: every file under the package's own <c>src/&lt;project&gt;/wwwroot</c> which the SDK
    /// packs to <c>staticwebassets/&lt;relative path&gt;</c> — that is, every file there MINUS the
    /// ones the project's own csproj declares <c>Pack="false"</c>. The subtraction is not cosmetic:
    /// on 2.8.26 the DocumentEditor denominator counted 140 files its csproj intentionally never
    /// packs (<c>*.test.mjs</c>, <c>__tests__</c>, <c>*.md</c>, <c>.gitkeep</c> under
    /// <c>wwwroot/js</c>) and reported every one of them <c>missing</c> against a package that was
    /// byte-correct — the artefact was right and the denominator was wrong. Measured on 2.8.23 for
    /// the lead: 168 files in the tree, 168 present in the package, 168 byte-identical. A
    /// hand-picked list would shrink to whichever file somebody once cared about — the
    /// <c>MeasuredSites</c> mistake — so the count carries a floor and a package entry with no
    /// counterpart in the tree is REPORTED rather than ignored.
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
    /// <param name="TreeFileCount">The denominator: files found under the package's <c>wwwroot</c>
    /// after the csproj's <c>Pack="false"</c> globs are subtracted — what the SDK actually packs.</param>
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

        /// <summary>
        /// How many <c>Pack="false"</c> globs the project's csproj declared — counted whether or not
        /// any of them reaches a file under <c>wwwroot</c>, because a population nobody counts is a
        /// population whose drift nobody sees. Not positional: the record predates the model and the
        /// sweep is the only place that knows it, so it is set with <c>with</c> where it is measured.
        /// </summary>
        internal int PackExcludedPatterns { get; init; }

        /// <summary>How many <c>wwwroot</c> files the globs subtracted from the denominator.</summary>
        internal int PackExcludedFiles { get; init; }

        internal string PackageUrl =>
            $"https://api.nuget.org/v3-flatcontainer/{PackageId.ToLowerInvariant()}/{Version}/"
            + $"{PackageId.ToLowerInvariant()}.{Version}.nupkg";

        internal string Report =>
            $"[Provenance] id={PackageId} version={Version} tree-files={TreeFileCount} "
            + $"pack-excluded-patterns={PackExcludedPatterns} pack-excluded-files={PackExcludedFiles} "
            + $"matching={Matching.Count} "
            + $"differing={Differing.Count} missing={Missing.Count} extra-in-package={ExtraInPackage.Count} "
            + $"nonreproducible={NonReproducible.Count} "
            + $"elapsed-ms={ElapsedMilliseconds} url={PackageUrl}"
            + (Differing.Count == 0 ? string.Empty : $" :: differing={string.Join(",", Differing.Take(10))}")
            + (Missing.Count == 0 ? string.Empty : $" :: missing={string.Join(",", Missing.Take(10))}")
            + (Unreachable is null ? string.Empty : $" :: unmeasured:package-unreachable {Unreachable}");

        internal static PackageProvenance Take(string packageId, string version, string projectDirectory)
        {
            var stopwatch = Stopwatch.StartNew();
            var denominator = TreeContent(projectDirectory);
            var tree = denominator.Files;

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                var empty = new PackageProvenance(
                    packageId, version, tree.Count, [], [], [], [], [], 0, null)
                {
                    PackExcludedPatterns = denominator.PackExcludedPatterns,
                    PackExcludedFiles = denominator.PackExcludedFiles,
                };

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
                    stopwatch.ElapsedMilliseconds) with
                {
                    PackExcludedPatterns = denominator.PackExcludedPatterns,
                    PackExcludedFiles = denominator.PackExcludedFiles,
                };
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                                              or InvalidDataException or IOException)
            {
                return new PackageProvenance(
                    packageId, version, tree.Count, [], [], [], [], [], stopwatch.ElapsedMilliseconds,
                    $"{error.GetType().Name}: {error.Message}")
                {
                    PackExcludedPatterns = denominator.PackExcludedPatterns,
                    PackExcludedFiles = denominator.PackExcludedFiles,
                };
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

        /// <summary>
        /// What the sweep subtracted and what it left. The denominator is a population, so the count
        /// of exclusion patterns and of files they removed is part of the record — a model that
        /// changed nothing and a model that read nothing must not produce the same report line.
        /// </summary>
        /// <param name="Files"><c>wwwroot</c>-relative path → content hash, <c>Pack="false"</c>
        /// matches already removed.</param>
        /// <param name="PackExcludedPatterns"><c>Pack="false"</c> globs the project's csproj
        /// declared — counted even when none of them reaches a file under <c>wwwroot</c>, because
        /// "the pattern matched nothing" and "the pattern was never read" must not look alike.</param>
        /// <param name="PackExcludedFiles"><c>wwwroot</c> files the globs subtracted.</param>
        internal sealed record TreeDenominator(
            IReadOnlyDictionary<string, string> Files,
            int PackExcludedPatterns,
            int PackExcludedFiles);

        /// <summary>
        /// One <c>Pack="false"</c> Include glob lifted out of the project's csproj.
        /// <see cref="UnderWwwroot"/> is null when the pattern's first segment is not
        /// <c>wwwroot</c> — such a glob can never reach a swept file, yet it is still counted in
        /// <see cref="TreeDenominator.PackExcludedPatterns"/>: the population of the model stays
        /// visible even where it bites nothing.
        /// </summary>
        private sealed record PackExclusion(string RawPattern, Regex? UnderWwwroot)
        {
            internal bool AppliesUnderWwwroot(string wwwrootRelativePath) =>
                UnderWwwroot?.IsMatch(wwwrootRelativePath) == true;
        }

        /// <summary>
        /// The denominator, derived from the source tree: every file under the package's own
        /// <c>src/&lt;projectDirectory&gt;/wwwroot</c> that the SDK actually packs — which is every
        /// file there MINUS the ones matching a <c>Pack="false"</c> glob in the project's csproj.
        /// Enumerated, never listed by hand. A project with no <c>wwwroot</c> yields an empty
        /// denominator — a package that is all <c>lib/**</c> then reports exactly that, rather than
        /// pretending to have been compared.
        /// </summary>
        /// <remarks>
        /// WHY THE SUBTRACTION EXISTS, measured not imagined: without it the 2.8.26 run reported
        /// <c>tempo.blazor.documenteditor</c> <c>missing=140</c> — every one a
        /// <c>*.test.mjs</c>/<c>__tests__</c>/<c>*.md</c>/<c>.gitkeep</c> file the csproj declares
        /// <c>Pack="false"</c> precisely so it never reaches the package. The artefact was correct
        /// and the denominator was wrong; a gate that goes red over a correct release teaches its
        /// readers to bump past it.
        /// </remarks>
        internal static TreeDenominator TreeContent(string projectDirectory) =>
            TreeContentUnder(Path.Combine(FindRepoRoot(), "src", projectDirectory));

        /// <summary>
        /// The sweep over an arbitrary project directory — the seam
        /// <c>ProvenanceComparisonTests</c> drives with a synthetic csproj and a synthetic
        /// <c>wwwroot</c>, so the parse, the glob translation and the subtraction are all exercised
        /// without touching the real tree.
        /// </summary>
        internal static TreeDenominator TreeContentUnder(string projectRoot)
        {
            var exclusions = ReadPackExclusions(projectRoot);

            var root = Path.Combine(projectRoot, "wwwroot");
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var excluded = 0;
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(root, file)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    if (exclusions.Any(exclusion => exclusion.AppliesUnderWwwroot(relative)))
                    {
                        excluded++;
                        continue;
                    }

                    files[relative] = Hash(File.ReadAllBytes(file));
                }
            }

            return new TreeDenominator(files, exclusions.Count, excluded);
        }

        /// <summary>
        /// The <c>Pack="false"</c> model, read out of the project's csproj as XML: every item
        /// element carrying <c>Pack</c> is classified — <c>true</c> is not an exclusion,
        /// <c>false</c> contributes its <c>Include</c> glob(s), and anything else is a value this
        /// model cannot measure.
        /// </summary>
        /// <remarks>
        /// <para>
        /// FAIL-CLOSED on every shape the model cannot evaluate. A glob the parser cannot read —
        /// <c>$(…)</c>/<c>@(…)</c>/<c>%(…)</c>/<c>${…}</c> substitutions, a <c>Condition</c> on the
        /// item or an ancestor, an <c>Update</c>/<c>Remove</c>/<c>Exclude</c> spec, a partial
        /// <c>**</c> segment, a rooted or parent-escaping path, an item sitting under something
        /// other than <c>ItemGroup</c>/<c>Project</c> — throws instead of guessing: an unmeasurable
        /// exclusion that is silently ignored widens the denominator and reports intentionally
        /// unpacked files as <c>missing</c>, which is the exact false red this model exists to
        /// remove. Unmeasurable beats a wrong pass.
        /// </para>
        /// <para>
        /// NAMED GAPS, stated so they are read as limits rather than oversights. (i) The sibling
        /// <c>&lt;Content Remove="wwwroot\…"&gt;</c> lines that actually de-pack the files are not
        /// modelled — the <c>Pack="false"</c> items are the declared, readable half of the same set,
        /// and in every csproj here the two are textually paired. (ii) <c>Update</c>/<c>Remove</c>
        /// item specs are not expanded — out of the model's scope, and any of them carrying
        /// <c>Pack="false"</c> throws rather than being skipped. (iii) <c>Pack</c> metadata arriving
        /// through an imported <c>Directory.Build.props</c>/<c>.targets</c> resolves relative to the
        /// IMPORTING file's directory — a base this model cannot reproduce — so any
        /// <c>Pack</c>-bearing element on the import chain between the project and the repository
        /// root is unmeasurable and throws. Today that chain carries none.
        /// </para>
        /// </remarks>
        private static IReadOnlyList<PackExclusion> ReadPackExclusions(string projectRoot)
        {
            var csprojFiles = Directory.Exists(projectRoot)
                ? Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly)
                    .ToList()
                : [];
            if (csprojFiles.Count > 1)
            {
                throw new InvalidOperationException(
                    $"{projectRoot} holds {csprojFiles.Count} csproj files — which one packs is "
                    + "ambiguous, and picking one would let a Pack=\"false\" glob in the unread one "
                    + "widen the denominator");
            }

            var exclusions = new List<PackExclusion>();
            if (csprojFiles.Count == 0)
            {
                // A wwwroot without a csproj is the one case where 'no exclusions' is a guess —
                // the exclusion model cannot be read at all, so the sweep refuses rather than
                // reports an unmeasured denominator. No wwwroot means nothing to measure and the
                // empty answer is honest.
                if (Directory.Exists(Path.Combine(projectRoot, "wwwroot")))
                {
                    throw new InvalidOperationException(
                        $"{projectRoot} has a wwwroot but no csproj — the Pack=\"false\" model "
                        + "cannot be read, and a denominator swept without it reports intentional "
                        + "exclusions as missing");
                }

                return exclusions;
            }

            var csproj = csprojFiles[0];
            CollectPackExclusions(XDocument.Load(csproj), csproj, exclusions);

            var repositoryRoot = FindRepoRoot();
            for (var directory = new DirectoryInfo(projectRoot);
                 directory is not null
                 && (string.Equals(directory.FullName, repositoryRoot, StringComparison.Ordinal)
                     || directory.FullName.StartsWith(
                         repositoryRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal));
                 directory = directory.Parent)
            {
                foreach (var importedName in new[] { "Directory.Build.props", "Directory.Build.targets" })
                {
                    var imported = Path.Combine(directory.FullName, importedName);
                    if (!File.Exists(imported))
                    {
                        continue;
                    }

                    var bearer = XDocument.Load(imported).Descendants().FirstOrDefault(
                        element => element.Attributes().Any(
                            attribute => string.Equals(
                                attribute.Name.LocalName, "Pack", StringComparison.OrdinalIgnoreCase)));
                    if (bearer is not null)
                    {
                        throw new InvalidOperationException(
                            $"{imported}: <{bearer.Name.LocalName}> carries Pack metadata — items "
                            + "from an imported props/targets resolve relative to the importing "
                            + "file's directory, a base this model cannot reproduce, so the "
                            + "exclusion set is unmeasurable and the run fails closed");
                    }
                }
            }

            return exclusions;
        }

        /// <summary>
        /// Classifies every <c>Pack</c>-bearing element in one project file. Only a plain,
        /// unconditional <c>Include</c> is measurable; the rest throws. Element and attribute NAMES
        /// are matched case-insensitively because MSBuild's own are.
        /// </summary>
        private static void CollectPackExclusions(
            XDocument document, string sourcePath, List<PackExclusion> into)
        {
            foreach (var element in document.Descendants())
            {
                var pack = AttributeNamed(element, "Pack")?.Value;
                if (pack is null
                    || string.Equals(pack.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = element.Name.LocalName;
                if (!string.Equals(pack.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{sourcePath}: <{name}> Pack=\"{pack}\" is not a literal true/false — "
                        + "evaluated Pack metadata is a shape this model cannot measure, and an "
                        + "unmeasurable exclusion must never pass as a wider denominator");
                }

                if (element.Ancestors().Any(
                        ancestor => ancestor.Name.LocalName is not ("ItemGroup" or "Project"))
                    || element.AncestorsAndSelf().Any(
                        ancestor => AttributeNamed(ancestor, "Condition") is not null))
                {
                    throw new InvalidOperationException(
                        $"{sourcePath}: <{name}> Pack=\"false\" sits under a condition or outside a "
                        + "plain ItemGroup — whether it fires is build-time state this model does "
                        + "not run, so the exclusion set is unmeasurable");
                }

                if (AttributeNamed(element, "Update") is not null
                    || AttributeNamed(element, "Remove") is not null
                    || AttributeNamed(element, "Exclude") is not null)
                {
                    throw new InvalidOperationException(
                        $"{sourcePath}: <{name}> Pack=\"false\" carries Update/Remove/Exclude — "
                        + "the resulting item set is an evaluated one, and evaluating it wrongly "
                        + "would report intentionally unpacked files as missing (or the reverse)");
                }

                var include = AttributeNamed(element, "Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    throw new InvalidOperationException(
                        $"{sourcePath}: <{name}> Pack=\"false\" without Include declares no file "
                        + "set — a shape the denominator cannot subtract");
                }

                // MSBuild list syntax: one Include attribute can carry several globs.
                foreach (var piece in include.Split(';'))
                {
                    var pattern = piece.Trim();
                    if (pattern.Length == 0)
                    {
                        continue;
                    }

                    into.Add(CompilePackExclusion(pattern, sourcePath));
                }
            }
        }

        /// <summary>
        /// One Include glob → one matcher over <c>wwwroot</c>-relative paths. The glob language is
        /// MSBuild's own: separators <c>\</c> and <c>/</c> normalize to <c>/</c>, <c>**</c> as a
        /// WHOLE segment matches zero or more directories, <c>*</c>/<c>?</c> inside a segment never
        /// cross a separator, and matching is case-insensitive because MSBuild's file matching is.
        /// Every other shape throws — the caller cannot tell "excluded nothing" from "was not
        /// understood", so an unreadable pattern is never allowed to pass as one.
        /// </summary>
        private static PackExclusion CompilePackExclusion(string rawPattern, string sourcePath)
        {
            var pattern = rawPattern.Replace('\\', '/');

            if (pattern.Contains("$(") || pattern.Contains("${")
                || pattern.Contains("@(") || pattern.Contains("%("))
            {
                throw new InvalidOperationException(
                    $"{sourcePath}: Pack=\"false\" glob '{rawPattern}' carries an MSBuild "
                    + "substitution — an evaluated pattern is a shape this model cannot measure, "
                    + "and treating it as literal would both exclude nothing and say nothing");
            }

            var segments = pattern.Split('/');
            if (pattern.Contains(':')
                || segments.Any(segment => segment.Length == 0))
            {
                throw new InvalidOperationException(
                    $"{sourcePath}: Pack=\"false\" glob '{rawPattern}' is rooted, an ADS name or "
                    + "carries an empty segment — none of them is a project-relative glob this "
                    + "model can apply");
            }

            if (segments.Any(segment => segment is "." or ".."))
            {
                throw new InvalidOperationException(
                    $"{sourcePath}: Pack=\"false\" glob '{rawPattern}' walks off the project "
                    + "directory — the sweep's keys never contain '.' or '..', so a pattern that "
                    + "does is a shape the matcher cannot express honestly");
            }

            if (segments.Any(segment => segment != "**" && segment.Contains("**")))
            {
                throw new InvalidOperationException(
                    $"{sourcePath}: Pack=\"false\" glob '{rawPattern}' embeds '**' inside a "
                    + "segment — MSBuild's own matcher gives that a meaning this translation does "
                    + "not reproduce, so it fails closed rather than widening silently");
            }

            // The sweep's keys are relative to wwwroot; a glob whose first segment is not
            // 'wwwroot' can never reach one. It is still COUNTED — see TreeDenominator.
            if (segments.Length == 1
                || !string.Equals(segments[0], "wwwroot", StringComparison.OrdinalIgnoreCase))
            {
                return new PackExclusion(rawPattern, null);
            }

            return new PackExclusion(rawPattern, new Regex(
                "^" + GlobToRegex(segments[1..]) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        /// <summary>
        /// Glob segments → regex body (no anchors). A mid-pattern <c>**</c> owns the separator that
        /// follows it — <c>a/**/b</c> compiles to <c>a/(?:[^/]+/)*b</c>, so zero directory levels
        /// still match — and a trailing <c>**</c> is "anything below".
        /// </summary>
        private static string GlobToRegex(IReadOnlyList<string> segments)
        {
            var builder = new StringBuilder();
            var needsSeparator = false;
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                if (segment == "**")
                {
                    if (needsSeparator)
                    {
                        builder.Append('/');
                    }

                    builder.Append(index == segments.Count - 1 ? ".*" : "(?:[^/]+/)*");
                    needsSeparator = false;
                    continue;
                }

                if (needsSeparator)
                {
                    builder.Append('/');
                }

                foreach (var c in segment)
                {
                    builder.Append(c switch
                    {
                        '*' => "[^/]*",
                        '?' => "[^/]",
                        _ when ".\\+()[]{}^$|".IndexOf(c) >= 0 => "\\" + c,
                        _ => c.ToString(),
                    });
                }

                needsSeparator = true;
            }

            return builder.ToString();
        }

        /// <summary>An XML attribute by name, case-insensitively — MSBuild's attribute names are.</summary>
        private static XAttribute? AttributeNamed(XElement element, string name) =>
            element.Attributes().FirstOrDefault(
                attribute => string.Equals(
                    attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

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
