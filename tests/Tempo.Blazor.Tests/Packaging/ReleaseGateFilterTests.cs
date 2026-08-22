using FluentAssertions;
using FluentAssertions.Execution;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// The two publish workflows are the Tempo release gate. Their <c>--filter</c> is the list of
/// named exceptions, not a convenience. A clause added by hand is a silent hole; a clause
/// removed by hand is a red CI that nobody can satisfy. This guard reads both YAML files and
/// compares the filter to the documented exceptions.
/// <para>
/// IT ALSO HOLDS THE THREE RELEASE STEPS THAT ONLY EXIST IN CI, because a step that lives in one
/// workflow and not the other is the same class of hole as a filter clause that does: the version
/// agreement check, the per-package push accounting, and the second test lane under a non-English
/// ambient culture. None of those can be exercised by this suite — a unit test cannot run a
/// workflow — so what a green here proves is that the steps are PRESENT and identically shaped in
/// both files. That they WORK was measured by running the two scripts directly; see their headers.
/// The limit is stated rather than left to be inferred, for the same reason
/// <c>PackScript_RefusesAVersionTheFeedAlreadyServes</c> states its own.
/// </para>
/// </summary>
/// <remarks>
/// Fáze 12: Demo.Api including the two smtp4dev tests IS in the gate (CI starts smtp4dev).
/// <c>Tempo.Blazor.E2E</c> and <c>Tempo.ReportServer.Api.Tests.MsSql</c> stay out until their
/// cancellation conditions fire. The full solution suite is not the gate — see
/// <c>DEC-TEMPO-RELEASE-GATE</c>.
/// </remarks>
public sealed class ReleaseGateFilterTests
{
    private static readonly string[] WorkflowRelativePaths =
    [
        Path.Combine(".github", "workflows", "publish-nuget.yml"),
        Path.Combine(".github", "workflows", "publish-nuget-org.yml"),
    ];

    /// <summary>
    /// Named exceptions that stay out of the CI release gate. Exact set: a new exclusion without
    /// a register entry must fail here, and dropping one of these without meeting its cancellation
    /// condition must fail here too.
    /// </summary>
    internal static readonly string[] NamedExceptions =
    [
        "Tempo.Blazor.E2E",
        "Tempo.ReportServer.Api.Tests.MsSql",
    ];

    /// <summary>
    /// Every <c>--filter</c> in both files is the same filter.
    /// <para>
    /// WHY EVERY OCCURRENCE AND NOT THE FIRST ONE: until 2026-08-21 each workflow ran the gate once,
    /// so "the filter" and "the first filter" were the same string and this guard read
    /// <c>Regex.Match</c>. Each file now runs the gate twice — once under the runner's ambient
    /// culture and once under a Czech one — and a guard anchored on the first match would let the
    /// SECOND lane drift to any filter at all while staying green. The population is therefore every
    /// occurrence in both files, and the count per file is compared too, so a lane deleted from one
    /// workflow is a red rather than a shorter list nobody counted.
    /// </para>
    /// </summary>
    [Fact]
    public void BothPublishWorkflows_UseTheSameReleaseGateFilter()
    {
        IReadOnlyList<IReadOnlyList<string>> perFile = [.. WorkflowRelativePaths.Select(ReadFilters)];

        using (new AssertionScope())
        {
            perFile[0].Count.Should().Be(
                perFile[1].Count,
                "the two workflows must run the release gate the same number of times; a lane present "
                + $"in one and missing from the other is a hole in exactly one of them ({DescribeFilterCounts(perFile)})");

            perFile.SelectMany(filters => filters).Distinct(StringComparer.Ordinal)
                .Should().HaveCount(
                    1,
                    "publish-nuget.yml and publish-nuget-org.yml, and every lane inside them, must "
                    + "share one filter; two different filters make one CI permanently red and then "
                    + $"nobody reads either ({DescribeFilterCounts(perFile)})");
        }
    }

    [Fact]
    public void ReleaseGateFilter_IsExactlyTheNamedExceptions()
    {
        // Every lane in both files, not workflow[0] lane[0]: an exclusion smuggled into the second
        // lane excuses those tests from the gate just as effectively as one in the first.
        foreach (string relative in WorkflowRelativePaths)
        {
            foreach (string filter in ReadFilters(relative))
            {
                IReadOnlyList<string> exclusions = ParseExclusions(filter);

                using (new AssertionScope())
                {
                    exclusions.Except(NamedExceptions, StringComparer.Ordinal)
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .Should().BeEmpty(
                            $"a new FullyQualifiedName!~ clause in {relative} is a silent hole in the "
                            + "release gate; name it in NamedExceptions and in the exception register, "
                            + "or do not add it");

                    NamedExceptions.Except(exclusions, StringComparer.Ordinal)
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .Should().BeEmpty(
                            $"a named exception missing from a CI filter in {relative} is being run as "
                            + "a release condition; drop it from NamedExceptions only when its "
                            + "cancellation condition fired");

                    exclusions.Should().NotContain(
                        "Smtp4Dev",
                        $"the two Demo.Api smtp4dev tests are in the gate ({relative}); CI starts "
                        + "smtp4dev as a service");
                }
            }
        }
    }

    /// <summary>
    /// Both workflows carry the three release steps that only exist in CI.
    /// <para>
    /// EACH OF THE THREE CLOSES A GAP MEASURED ON 2026-08-21, and each is worth nothing in the
    /// workflow that lost it — a release goes out through whichever one is triggered:
    /// <c>eng/verify-announced-version.sh</c> is the only thing comparing the number being published
    /// against the number the changelog announces (on a tag push the first comes from the TAG);
    /// <c>eng/push-nuget-packages.sh</c> is the only thing that can tell a published package from a
    /// skipped one, because <c>--skip-duplicate</c> answers a 409 with exit 0; and the second test
    /// lane is the only one running under a culture that is not English.
    /// </para>
    /// <para>
    /// THE INLINE LOOP IS FORBIDDEN BY NAME. Reverting the push step to <c>dotnet nuget push</c> in
    /// the YAML restores exactly the silent state the script exists to end, and it would leave every
    /// other assertion here green — so its absence is asserted rather than assumed.
    /// </para>
    /// </summary>
    [Fact]
    public void BothPublishWorkflows_CarryTheReleaseStepsThatOnlyExistInCi()
    {
        foreach (string relative in WorkflowRelativePaths)
        {
            MissingReleaseSteps(ReadWorkflowCode(relative))
                .Should().BeEmpty(
                    $"{relative} is a complete release path on its own: whichever workflow a tag "
                    + "triggers is the one that decides what goes out, so a step that exists in only "
                    + "one of them protects only one of them");
        }
    }

    /// <summary>
    /// Mutation over the guard above: it is green on the healthy files, so feed it the shapes a
    /// careless revert produces and require each to be named. Without this the guard could be
    /// asserting over markers that are never absent, which is the same green either way.
    /// </summary>
    [Fact]
    public void TheGuard_DetectsEachReleaseStepBeingRemovedOrReverted()
    {
        string healthy = ReadRepoFile(WorkflowRelativePaths[0]);

        using (new AssertionScope())
        {
            MissingReleaseSteps(StripYamlComments(healthy)).Should().BeEmpty(
                "the positive control: the marker set must be absent from a broken file and present "
                + "in a healthy one, or its emptiness above says nothing");

            foreach (string marker in RequiredReleaseMarkers)
            {
                MissingReleaseSteps(
                        StripYamlComments(healthy.Replace(marker, "removed-by-mutation", StringComparison.Ordinal)))
                    .Should().Contain(
                        marker,
                        $"deleting '{marker}' from a publish workflow must be visible here; a marker "
                        + "this guard cannot miss is a marker it is not really checking");

                // THE ARM THAT WAS MISSING, AND IT WAS MEASURED RATHER THAN IMAGINED. Commenting the
                // whole step out — every line prefixed with '#' — leaves both markers in the file, so
                // over the RAW text every one of these guards was GREEN over a lane that no longer
                // runs. Deleting the marker (the arm above) cannot find that, because it removes the
                // marker from comments too. This one keeps the text and disables it, which is what a
                // careless revert actually looks like.
                MissingReleaseSteps(StripYamlComments(CommentOutLinesContaining(healthy, marker)))
                    .Should().Contain(
                        marker,
                        $"a step carrying '{marker}' that is COMMENTED OUT does not run, so it must "
                        + "read here exactly like a deleted one: YAML comments are prose, and a guard "
                        + "that accepts prose is the 'delete the code, keep the comment' hole its own "
                        + "sibling over the push script already names");
            }

            MissingReleaseSteps(
                    StripYamlComments(healthy + "\n          dotnet nuget push \"$pkg\" --skip-duplicate\n"))
                .Should().Contain(
                    InlinePushMarker,
                    "reverting to the inline push loop restores the state where a job is green "
                    + "whether 26 packages were published or none were");

            MissingReleaseSteps(
                    StripYamlComments(healthy + "\n          # dotnet nuget push \"$pkg\" --skip-duplicate\n"))
                .Should().NotContain(
                    InlinePushMarker,
                    "and the projection has to cut BOTH ways: a mention of the old loop inside a "
                    + "comment is prose about history, not a reverted step, so it must not be reported "
                    + "as one — otherwise the guard becomes unsatisfiable for anyone documenting why "
                    + "the loop went away");
        }
    }

    /// <summary>
    /// The push script still tells a published package from a skipped one, and still fails closed.
    /// <para>
    /// A TEXT ASSERTION BY NECESSITY, and the same admitted limit as
    /// <c>PackScript_RefusesAVersionTheFeedAlreadyServes</c>: a unit test cannot observe a push. What a
    /// green here proves is that the clauses are PRESENT. That they FIRE was measured on 2026-08-21 by
    /// running the script over all 26 staged packages against an endpoint answering 409 and 500 —
    /// 26/26 published, 26/26 skipped (job red, all 26 still attempted), 25 published + 1 skipped, and
    /// 24 published + 2 failed with the loop finishing in every case.
    /// </para>
    /// <para>
    /// THE NEEDLES RUN OVER THE SCRIPT'S CODE, NOT ITS FULL TEXT, because every one of these strings
    /// also appears in the header explaining why it is there — that block is the record of the defect —
    /// and asserting over the whole file would let "delete the code, keep the prose" stay green.
    /// </para>
    /// <para>
    /// <c>--skip-duplicate</c> IS ASSERTED PRESENT, WHICH READS BACKWARDS UNTIL YOU KNOW WHY. It is not
    /// the defect; it is what keeps the loop from aborting on the first already-published package and
    /// shipping half a release. Deleting it trades a silent "nothing went out" for a loud "half went
    /// out", and the second is unrecoverable because the numbers that did go out are immutable. The
    /// treatment was per-package accounting, not removing the flag.
    /// </para>
    /// </summary>
    [Fact]
    public void PushScript_TellsPublishedFromSkipped_AndFailsClosed()
    {
        string code = CodeLinesOf(ReadRepoFile(Path.Combine("eng", "push-nuget-packages.sh")));

        using (new AssertionScope())
        {
            code.Should().Contain(
                "--skip-duplicate",
                "the flag stays: without it the first already-published package aborts the remaining "
                + "25 and produces a partially published release, which no re-run can undo");

            code.Should().Contain(
                "|| rc=$?",
                "one package's failure must not end the loop — every package is attempted and every "
                + "one gets a verdict, because the decision is per package and only the summary is "
                + "per job");

            code.Should().Contain(
                "DOTNET_CLI_UI_LANGUAGE=en",
                "the verdict is read out of the tool's own words and those words are LOCALIZED: "
                + "measured on a cs_CZ machine the same 409 prints 'už v kanálu … existuje', which an "
                + "English-only reader would have classified as unreadable. Pinning the tool's language "
                + "makes the classifier's premise a property of this step rather than of the runner");

            code.Should().Contain(
                "already exists at feed",
                "the skip has to be recognised explicitly; with --skip-duplicate a 409 exits 0, so "
                + "nothing about the exit code separates 'published' from 'already there'");

            code.Should().Contain(
                "Your package was pushed.",
                "and so does the publish, because a verdict reached by elimination would call every "
                + "unrecognised output a success");

            // THE NEEDLE IS THE COUNTER, NOT THE WORD, and that distinction was measured rather than
            // reasoned about: an earlier version of this assertion looked for "UNRESOLVED", and a
            // mutation that collapsed the unresolved branch into the published one left it GREEN —
            // because the word also occurs in the failure message at the end of the script, which the
            // mutation did not touch. A needle that a second line can satisfy is a needle that stops
            // measuring the line it was written for.
            code.Should().Contain(
                "unresolved=$((unresolved + 1))",
                "an output matching neither marker must be a failure and not a shrug: if a future "
                + "NuGet version rewords these lines, this step has to refuse to certify what it "
                + "could not read rather than report a skip as a publication");

            code.Should().Contain(
                "\"$published\" -eq \"$total\"",
                "green means every staged package was accepted under this release's number; any other "
                + "arithmetic lets a partially published release pass");
        }
    }

    /// <summary>
    /// The locale of the second lane is set on that STEP and nowhere earlier — asked PER JOB.
    /// <para>
    /// WHY THIS IS ITS OWN ASSERTION rather than part of the presence check: a job-level or
    /// workflow-level <c>env:</c> would satisfy every "does the file contain LC_ALL" question and be
    /// a different, worse thing. A locale exported before <c>dotnet build</c> changes the order in
    /// which globbed sources are enumerated and therefore the emitted IL, which would make the two
    /// lanes differ in what was COMPILED instead of in what the tests READ — and then a red in the
    /// second lane would no longer be evidence about culture at all.
    /// </para>
    /// <para>
    /// AND WHY PER JOB RATHER THAN PER FILE, which is a SECOND hole and not a restatement of the
    /// first. This started as <c>IndexOf("- name: Build")</c> compared with <c>IndexOf("LC_ALL:")</c>
    /// over the whole file. Both workflows carry TWO jobs and each of them builds (measured
    /// 2026-08-21: <c>publish-nuget-org.yml</c> lines 71 and 220, <c>publish-nuget.yml</c> 74 and
    /// 221), so an <c>LC_ALL</c> anywhere in the PUBLISH job sits after the FIRST build and the
    /// comparison stayed green — while wrapping the build that produces the packages that ship. It is
    /// the same first-match trap this class fixed for <c>--filter</c> a few dozen lines above, which
    /// is why the treatment is the same shape: segment first, then ask inside each segment.
    /// </para>
    /// <para>
    /// THE QUESTION ASKED INSIDE A SEGMENT is "does any LC_ALL precede any build or pack in this
    /// job", not "is there an LC_ALL at all" — the second lane legitimately sets one, after the build
    /// it reuses. WHAT THAT DOES NOT COVER, stated rather than left to be inferred: an
    /// <c>LC_ALL</c> placed in the publish job AFTER its pack would pass. Nothing runs tests there,
    /// so it changes nothing that ships; if a step is ever added after the pack, this needs the
    /// stronger rule.
    /// </para>
    /// <para>
    /// THE POPULATION IS ASSERTED, because a segmentation that matched nothing would report "no job
    /// sets LC_ALL early" — the passing answer — out of an empty list.
    /// </para>
    /// </summary>
    [Fact]
    public void TheNonEnglishLaneSetsItsLocaleAfterTheBuild_NotAroundIt()
    {
        foreach (string relative in WorkflowRelativePaths)
        {
            IReadOnlyList<string> offenders = JobsWhereLocalePrecedesABuild(ReadWorkflowCode(relative), out int jobsWithABuild);

            using (new AssertionScope())
            {
                jobsWithABuild.Should().BeGreaterThanOrEqualTo(
                    2,
                    $"{relative} has two jobs and both of them build; a smaller number here means the "
                    + "segmentation missed a job, and an empty offender list out of a bad split is "
                    + "silence rather than evidence");

                offenders.Should().BeEmpty(
                    $"{relative} must not set LC_ALL before a build or a pack in the SAME job: a "
                    + "locale exported around `dotnet build` changes source enumeration order and "
                    + "therefore the emitted IL, which turns the second lane from 'same binaries, "
                    + "different culture' into two different builds and makes its red unattributable — "
                    + "and in the publish job it would wrap the build whose output is what ships");
            }
        }
    }

    /// <summary>
    /// Mutation over the ordering guard, in BOTH directions, because a check that refuses always
    /// would satisfy a one-armed criterion just as well as a working one.
    /// </summary>
    [Fact]
    public void TheOrderingGuard_DetectsALocaleThatWrapsTheReleaseBuild()
    {
        string healthy = ReadWorkflowCode(WorkflowRelativePaths[1]);

        using (new AssertionScope())
        {
            JobsWhereLocalePrecedesABuild(healthy, out _).Should().BeEmpty(
                "the positive control: with the locale where it belongs the guard has to be green, or "
                + "the red below says nothing about placement");

            // The shape the architect named: job-level env on the PUBLISH job. Its index is greater
            // than the FIRST `- name: Build` in the file, which is why the per-file comparison could
            // not see it.
            string jobLevelEnvOnPublish = healthy.Replace(
                "  publish:\n",
                "  publish:\n    env:\n      LC_ALL: cs_CZ.UTF-8\n",
                StringComparison.Ordinal);
            jobLevelEnvOnPublish.Should().NotBe(healthy, "the mutation must actually change the text");
            JobsWhereLocalePrecedesABuild(jobLevelEnvOnPublish, out _).Should().Contain(
                "publish",
                "a job-level locale on the publish job wraps the build AND the pack that produce the "
                + "packages that ship, and it is exactly the case the per-file comparison reported as "
                + "green");

            // And the same hole inside the job that legitimately owns a locale: moving it in front of
            // that job's own build.
            string localeBeforeTheGateBuild = healthy.Replace(
                "  build-and-test:\n",
                "  build-and-test:\n    env:\n      LC_ALL: cs_CZ.UTF-8\n",
                StringComparison.Ordinal);
            JobsWhereLocalePrecedesABuild(localeBeforeTheGateBuild, out _).Should().Contain(
                "build-and-test",
                "hoisting the second lane's locale to job level compiles the gate under it, which is "
                + "the IL-order change the two lanes exist to avoid");
        }
    }

    /// <summary>
    /// The version-agreement script still compares the two numbers, and still refuses what it cannot
    /// read.
    /// <para>
    /// WHY IT NEEDED ITS OWN GUARD: it was the newest gate on the release path and the only one with
    /// nothing but a call marker behind it — <c>eng/pack-nuget-packages.sh</c> has a guard,
    /// <c>eng/push-nuget-packages.sh</c> got seven needles, and gutting the comparison inside this one
    /// left every test in the suite green. The two weaknesses multiplied rather than added: the call
    /// marker was satisfiable by a COMMENT as well (measured — the step deleted, the commented line
    /// kept, 8/8 green), so the newest gate had neither a guard on its content nor one on its
    /// presence. The comment half is fixed by the projection the other guards now share; this is the
    /// content half.
    /// </para>
    /// <para>
    /// SAME ADMITTED LIMIT AS ITS SIBLINGS: a green here proves the clauses are PRESENT, not that they
    /// fire. That they fire was measured by running the script over seven arms — agreement, the
    /// tag/changelog mismatch, a prerelease suffix, a near-miss number, and the three states in which
    /// the question cannot be asked.
    /// </para>
    /// </summary>
    [Fact]
    public void VersionAgreementScript_ComparesTheTwoNumbers_AndRefusesWhatItCannotRead()
    {
        string code = CodeLinesOf(ReadRepoFile(Path.Combine("eng", "verify-announced-version.sh")));

        using (new AssertionScope())
        {
            code.Should().Contain(
                "\"$version\" == \"$announced\"",
                "the comparison itself is the whole gate; without it the step is a call that always "
                + "succeeds, which is indistinguishable from not having it at all");

            code.Should().Contain(
                "\"$announced-\"*",
                "a prerelease suffix is agreement, not drift: workflow_dispatch with version_suffix "
                + "publishes <announced>-beta1 on purpose, and a gate that refuses it gets switched off");

            code.Should().Contain(
                "-z \"$announced\"",
                "a changelog that opens with no version heading yields an empty announced version, and "
                + "an empty string compares unequal — reporting a mismatch nobody can fix instead of "
                + "reporting that the question could not be asked");

            code.Should().Contain(
                "! -f \"$changelog\"",
                "a missing changelog produces the same silence as an agreeing one, so it is refused "
                + "rather than read as agreement");

            code.Should().Contain(
                "-z \"$version\"",
                "an empty VERSION would compare unequal to everything and turn this into a gate that "
                + "refuses always, which is the failure mode a one-armed criterion cannot tell from a "
                + "working one");

            code.Should().Contain(
                "GITHUB_REF",
                "the message has to say WHERE the number came from: on a tag push it comes from the "
                + "tag, and '2.8.20 != 2.8.19' without provenance sends the reader to the wrong file");

            code.Should().Contain(
                "announced :",
                "both numbers are named in the refusal — one of them alone tells nobody which of the "
                + "two reads to fix");
        }
    }

    [Fact]
    public void BothPublishWorkflows_RunSmtp4DevAsAService()
    {
        foreach (string relative in WorkflowRelativePaths)
        {
            // Same projection as its siblings: a commented-out service block starts no container.
            string text = ReadWorkflowCode(relative);
            using (new AssertionScope())
            {
                text.Should().Contain(
                    "rnwood/smtp4dev",
                    $"{relative} must start smtp4dev as a service so EmailTemplateSmtp4DevTests "
                    + "are a release condition, not a comment about a missing container");
                text.Should().Contain("2525:25", $"{relative} must publish SMTP on 2525");
                text.Should().Contain("5000:80", $"{relative} must publish smtp4dev REST on 5000");
            }
        }
    }

    /// <summary>
    /// Mutation: the comparison above is green on the healthy files. Feed it the two shapes
    /// that used to be the gate (Smtp4Dev still excluded, MsSql dropped) and both must be named.
    /// </summary>
    [Fact]
    public void TheGuard_DetectsASilentExclusionAndADroppedException()
    {
        EvaluateDrift(
                "FullyQualifiedName!~Tempo.Blazor.E2E&FullyQualifiedName!~Smtp4Dev&FullyQualifiedName!~Tempo.ReportServer.Api.Tests.MsSql")
            .Should().Contain(
                "Smtp4Dev",
                "putting Smtp4Dev back into the filter must be visible; otherwise the 2/206 hole returns");

        EvaluateDrift("FullyQualifiedName!~Tempo.Blazor.E2E")
            .Should().Contain(
                "Tempo.ReportServer.Api.Tests.MsSql",
                "dropping the MsSql exclusion without SQL Server in CI is a permanently red gate");

        EvaluateDrift("")
            .Should().NotBeEmpty("an empty filter is not 'no exceptions', it is an unreadable gate");
    }

    /// <summary>
    /// The literals a publish workflow must contain, one per gap they close. Literals rather than a
    /// parsed step model because that is what a hasty revert deletes, and because this file has no
    /// YAML parser — the same trade the smtp4dev assertions above already make.
    /// </summary>
    internal static readonly string[] RequiredReleaseMarkers =
    [
        "bash eng/verify-announced-version.sh",
        "bash eng/push-nuget-packages.sh",
        "bash eng/verify-push-classifier.sh",
        "Test (non-English ambient culture)",
        "LC_ALL: cs_CZ.UTF-8",
    ];

    /// <summary>How the reverted inline push loop is SPELLED in the report below.</summary>
    internal const string InlinePushMarker = "inline `dotnet nuget push` loop";

    /// <summary>
    /// What a publish workflow is missing: every required marker it does not carry, plus the inline
    /// push loop if it carries that. Pure over the text so the mutation test can feed it broken
    /// shapes without editing a file on disk.
    /// </summary>
    internal static IReadOnlyList<string> MissingReleaseSteps(string workflowText)
    {
        List<string> missing =
            [.. RequiredReleaseMarkers.Where(marker => !workflowText.Contains(marker, StringComparison.Ordinal))];

        if (workflowText.Contains("dotnet nuget push", StringComparison.Ordinal))
        {
            missing.Add(InlinePushMarker);
        }

        return missing;
    }

    /// <summary>
    /// Markers that mean "this job compiles or packs what ships". Read as literals for the same
    /// reason the rest of this class does: it is what a YAML edit actually contains.
    /// </summary>
    private static readonly string[] BuildInvocationMarkers =
    [
        "dotnet build",
        "eng/pack-nuget-packages.sh",
    ];

    /// <summary>
    /// Names of the jobs in which an <c>LC_ALL:</c> appears BEFORE that job's last build or pack.
    /// <paramref name="jobsWithABuild"/> carries the population, so a segmentation that matched
    /// nothing is a red rather than an empty offender list.
    /// </summary>
    internal static IReadOnlyList<string> JobsWhereLocalePrecedesABuild(
        string workflowCode, out int jobsWithABuild)
    {
        List<string> offenders = [];
        jobsWithABuild = 0;

        foreach ((string name, string body) in JobSegments(workflowCode))
        {
            int lastBuild = BuildInvocationMarkers
                .Select(marker => body.LastIndexOf(marker, StringComparison.Ordinal))
                .Max();
            if (lastBuild < 0)
            {
                continue;
            }

            jobsWithABuild++;
            int firstLocale = body.IndexOf("LC_ALL:", StringComparison.Ordinal);
            if (firstLocale >= 0 && firstLocale < lastBuild)
            {
                offenders.Add(name);
            }
        }

        return offenders;
    }

    /// <summary>
    /// The workflow split into its jobs. Segmentation starts AFTER the <c>jobs:</c> key on purpose —
    /// <c>on:</c> carries <c>push:</c> and <c>pull_request:</c> at the same two-space indent, and a
    /// splitter that started at the top of the file would report them as jobs and dilute the
    /// population that the caller asserts on.
    /// </summary>
    private static IReadOnlyList<(string Name, string Body)> JobSegments(string workflowCode)
    {
        var jobsKey = System.Text.RegularExpressions.Regex.Match(workflowCode, @"^jobs:\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        if (!jobsKey.Success)
        {
            return [];
        }

        string tail = workflowCode[(jobsKey.Index + jobsKey.Length)..];
        var starts = System.Text.RegularExpressions.Regex
            .Matches(tail, @"^  (?<name>[A-Za-z0-9_.-]+):[ \t]*$",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .ToList();

        List<(string, string)> segments = [];
        for (int index = 0; index < starts.Count; index++)
        {
            int from = starts[index].Index;
            int to = index + 1 < starts.Count ? starts[index + 1].Index : tail.Length;
            segments.Add((starts[index].Groups["name"].Value, tail[from..to]));
        }

        return segments;
    }

    /// <summary>
    /// A workflow with its comment lines removed — the projection every reader in this class shares.
    /// <para>
    /// WHY IT EXISTS, measured rather than argued: with the whole second lane commented out (each of
    /// its lines prefixed with <c>#</c>) all four workflow guards were GREEN, because a comment keeps
    /// every marker in the file. It is the same hole
    /// <see cref="PushScript_TellsPublishedFromSkipped_AndFailsClosed"/> already defends against over
    /// the push script, and it names it there: "delete the code, keep the prose".
    /// </para>
    /// <para>
    /// WHAT IT DOES NOT REMOVE: a TRAILING comment on a live line (<c>run: foo  # note</c>). A marker
    /// hidden there would still satisfy these guards. No such marker exists today and the projection
    /// is deliberately the same one-line rule the push-script guard uses, so the two cannot drift into
    /// two different notions of "code".
    /// </para>
    /// </summary>
    private static string ReadWorkflowCode(string relativePath) =>
        StripYamlComments(ReadRepoFile(relativePath));

    /// <summary>Drops every line whose first non-blank character is <c>#</c>.</summary>
    internal static string StripYamlComments(string text) => CodeLinesOf(text);

    /// <summary>The comment-free projection of any <c>#</c>-commented text: YAML or shell.</summary>
    internal static string CodeLinesOf(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    /// <summary>
    /// Every line containing <paramref name="marker"/>, commented out where it stands. The mutation
    /// that a careless revert actually performs — the text stays, the step stops running — and the
    /// one that deleting the marker cannot imitate.
    /// </summary>
    internal static string CommentOutLinesContaining(string text, string marker) =>
        string.Join(
            '\n',
            text.Split('\n')
                .Select(line => line.Contains(marker, StringComparison.Ordinal) ? "# " + line : line));

    private static string DescribeFilterCounts(IReadOnlyList<IReadOnlyList<string>> perFile) =>
        string.Join(
            ", ",
            WorkflowRelativePaths.Select((path, index) => $"{path}: {perFile[index].Count} filter(s)"));

    internal static IReadOnlyList<string> EvaluateDrift(string filter)
    {
        IReadOnlyList<string> exclusions = ParseExclusions(filter);
        List<string> drift = [];
        drift.AddRange(exclusions.Except(NamedExceptions, StringComparer.Ordinal));
        drift.AddRange(NamedExceptions.Except(exclusions, StringComparer.Ordinal));
        if (string.IsNullOrWhiteSpace(filter))
        {
            drift.Add("empty-filter");
        }

        return drift;
    }

    internal static IReadOnlyList<string> ParseExclusions(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return [];
        }

        return [.. filter
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(clause => clause.Trim())
            .Where(clause => clause.StartsWith("FullyQualifiedName!~", StringComparison.Ordinal))
            .Select(clause => clause["FullyQualifiedName!~".Length..])];
    }

    /// <summary>
    /// EVERY <c>--filter "…"</c> in the file, in file order. Plural since the gate runs in more than
    /// one lane; an empty result is a failure rather than an empty population, because "no filter
    /// found" and "no exceptions" produce the same green everywhere this is read.
    /// </summary>
    private static IReadOnlyList<string> ReadFilters(string relativePath)
    {
        // CODE, NOT RAW TEXT: a commented-out lane leaves its `--filter` in the file, which made the
        // count 2 and `Distinct` 1 over a workflow that ran the gate once. Measured 2026-08-21.
        string text = ReadWorkflowCode(relativePath);
        IReadOnlyList<string> filters =
        [
            .. System.Text.RegularExpressions.Regex
                .Matches(text, @"--filter\s+""(?<filter>[^""]+)""")
                .Select(match => match.Groups["filter"].Value)
        ];

        filters.Should().NotBeEmpty(
            $"{relativePath} must contain a --filter \"…\" on every Test step; without it this "
            + "guard cannot see the exceptions and would treat a missing gate as no exceptions");

        return filters;
    }

    private static string ReadRepoFile(string relativePath)
    {
        string root = FindRepoRoot();
        string path = Path.Combine(root, relativePath);
        File.Exists(path).Should().BeTrue($"release-gate workflow must exist at {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException(
                "Could not locate the Tempo.Blazor repository root.");
    }
}
