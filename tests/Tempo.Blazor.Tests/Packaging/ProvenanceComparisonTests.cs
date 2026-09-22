using FluentAssertions;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// Drives <see cref="ReleaseContractTests.PackageProvenance.Compare"/> through every outcome it can
/// report, without a network.
/// <para>
/// IT EXISTS BECAUSE THE GUARD THAT USES IT SPENDS MOST OF ITS LIFE NOT USING IT. The release gate
/// only downloads and compares when the announced number is ALREADY on the feed; for the whole of a
/// normal release cycle it takes the <c>unpublished</c> branch and returns. A green there says
/// nothing about whether the comparison works — and a comparison nobody has ever seen produce a red
/// is the shape of every measurement this plan has had to redo.
/// </para>
/// </summary>
public class ProvenanceComparisonTests
{
    private const string Id = "tempo.blazor";
    private const string Version = "9.9.9";

    private static Dictionary<string, string> Tree() => new(StringComparer.Ordinal)
    {
        ["css/tempo-blazor.bundled.css"] = "AAAA",
        ["css/components/_button.css"] = "BBBB",
        ["js/data-table.js"] = "CCCC",
    };

    [Fact]
    public void APackageThatCarriesTheTree_IsAllMatching()
    {
        var result = ReleaseContractTests.PackageProvenance.Compare(Id, Version, Tree(), Tree(), [], 0);

        result.Matching.Should().HaveCount(3);
        result.Differing.Should().BeEmpty();
        result.Missing.Should().BeEmpty();
        result.ExtraInPackage.Should().BeEmpty();
        result.TreeFileCount.Should().Be(3);
    }

    [Fact]
    public void OneChangedItem_IsReportedAsDiffering_AndNamed()
    {
        var packed = Tree();
        packed["css/tempo-blazor.bundled.css"] = "CHANGED";

        var result = ReleaseContractTests.PackageProvenance.Compare(Id, Version, Tree(), packed, [], 0);

        result.Differing.Should().Equal("css/tempo-blazor.bundled.css");
        result.Matching.Should().HaveCount(2);
        result.Report.Should().Contain("differing=1").And.Contain("css/tempo-blazor.bundled.css");
    }

    [Fact]
    public void AnItemThePackageDoesNotCarry_IsMissing_NotSilentlyMatching()
    {
        var packed = Tree();
        packed.Remove("js/data-table.js");

        var result = ReleaseContractTests.PackageProvenance.Compare(Id, Version, Tree(), packed, [], 0);

        result.Missing.Should().Equal("js/data-table.js");
        result.Differing.Should().BeEmpty("a file that is absent is absent, not different");
        result.Report.Should().Contain("missing=1");
    }

    /// <summary>
    /// The generated scoped-CSS bundle and the colocated <c>.razor.js</c> live here — five of them on
    /// 2.8.23. They are not a finding, and they are not dropped either: "the sweep ignored something"
    /// and "there was nothing to ignore" must not produce the same report.
    /// </summary>
    [Fact]
    public void PackageEntriesWithNoCounterpartInTheTree_AreReportedNotIgnored()
    {
        var packed = Tree();
        packed["Tempo.Blazor.abc123.bundle.scp.css"] = "GENERATED";

        var result = ReleaseContractTests.PackageProvenance.Compare(Id, Version, Tree(), packed, [], 0);

        result.ExtraInPackage.Should().Equal("Tempo.Blazor.abc123.bundle.scp.css");
        result.Differing.Should().BeEmpty();
        result.Missing.Should().BeEmpty();
        result.Report.Should().Contain("extra-in-package=1");
    }

    /// <summary>
    /// The compiled assemblies under <c>lib/</c> are the content the comparison CANNOT see — not
    /// byte-reproducible from a clean build. They are a registry row, not silence: for most of the 25
    /// satellite ids they are the whole payload, and "this id was measured" must read differently from
    /// "this id had nothing to measure".
    /// </summary>
    [Fact]
    public void LibEntries_AreRegisteredAsNonReproducible_NotCompared()
    {
        var result = ReleaseContractTests.PackageProvenance.Compare(
            Id, Version, Tree(), Tree(), ["lib/net8.0/Tempo.Blazor.dll"], 0);

        result.NonReproducible.Should().Equal("lib/net8.0/Tempo.Blazor.dll");
        result.Differing.Should().BeEmpty("an assembly is registered, not compared — a hash mismatch "
            + "there is noise, not provenance");
        result.ExtraInPackage.Should().BeEmpty("lib/** never reaches the staticwebassets comparison");
        result.Report.Should().Contain("nonreproducible=1");
    }

    /// <summary>
    /// The artefact is asked for by its exact URL — the same <c>{id}/{version}/{id}.{version}.nupkg</c>
    /// object a consumer's restore resolves. A URL built any other way would have the HEAD and the
    /// download confirming different things.
    /// </summary>
    [Fact]
    public void ThePackageUrl_IsTheExactNupkgObject_TheFeedServes()
    {
        var result = ReleaseContractTests.PackageProvenance.Compare(Id, Version, Tree(), Tree(), [], 0);

        result.PackageUrl.Should().Be(
            "https://api.nuget.org/v3-flatcontainer/tempo.blazor/9.9.9/tempo.blazor.9.9.9.nupkg");
        result.Report.Should().Contain(result.PackageUrl);
    }

    /// <summary>
    /// Every arm of <see cref="ReleaseContractTests.IndexVerdict"/>, without a network — the same
    /// reason the comparison itself is tested here: the feed guard takes most of these branches only
    /// in broken-instrument states a green run never reaches, and a verdict nobody has seen produce
    /// its word is not a verdict.
    /// </summary>
    [Fact]
    public void AnIndexThatListsTheAnnouncedVersion_AsksForThePackageBytes()
    {
        var answer = new ReleaseContractTests.ManifestAnswer(
            Id, "Tempo.Blazor", 200, ["2.8.24", Version], null);

        ReleaseContractTests.IndexVerdict(answer, Version, isLead: true)
            .Should().Be(ReleaseContractTests.CompareContent);
    }

    [Fact]
    public void AnIndexWithoutTheAnnouncedVersion_IsUnpublished_NotVerified()
    {
        var answer = new ReleaseContractTests.ManifestAnswer(
            Id, "Tempo.Blazor", 200, ["2.8.24", "2.8.25"], null);

        ReleaseContractTests.IndexVerdict(answer, Version, isLead: true)
            .Should().Be("unpublished");
    }

    [Fact]
    public void A404ForASatelliteId_IsUnpublished_BecauseTheIdWasNeverPublished()
    {
        var answer = new ReleaseContractTests.ManifestAnswer(
            "tempo.blazor.mcp", "Tempo.Blazor.Mcp", 404, [], null);

        ReleaseContractTests.IndexVerdict(answer, Version, isLead: false)
            .Should().StartWith("unpublished");
    }

    [Fact]
    public void A404ForTheLead_IsUnmeasured_BecauseEveryNumberWouldReadAsFree()
    {
        var answer = new ReleaseContractTests.ManifestAnswer(
            Id, "Tempo.Blazor", 404, [], null);

        ReleaseContractTests.IndexVerdict(answer, Version, isLead: true)
            .Should().Be("unmeasured:lead-index-http-404",
                "a lead the feed does not know cannot answer the membership question at all");
    }

    [Fact]
    public void ANonTwoHundredIndex_IsUnmeasured_NotUnpublished()
    {
        var answer = new ReleaseContractTests.ManifestAnswer(
            "tempo.blazor.mcp", "Tempo.Blazor.Mcp", 500, [], null);

        ReleaseContractTests.IndexVerdict(answer, Version, isLead: false)
            .Should().Be("unmeasured:index-http-500",
                "a network fault must never dress itself as a version check");
    }

    [Fact]
    public void AnEmptyVersionList_IsUnmeasured_AnEmptyListCannotAnswerMembership()
    {
        var answer = new ReleaseContractTests.ManifestAnswer(
            "tempo.blazor.mcp", "Tempo.Blazor.Mcp", 200, [], null);

        ReleaseContractTests.IndexVerdict(answer, Version, isLead: false)
            .Should().Be("unmeasured:index-http-200",
                "an empty list and a list without the announced number must not produce the same green");
    }

    [Fact]
    public void AnUnansweredId_IsUnmeasured_WithItsOwnReason()
    {
        var answer = new ReleaseContractTests.ManifestAnswer(
            "tempo.blazor.mcp", "Tempo.Blazor.Mcp", -1, [], "HttpRequestException: timed out");

        ReleaseContractTests.IndexVerdict(answer, Version, isLead: false)
            .Should().Be("unmeasured:HttpRequestException: timed out");
    }

    /// <summary>
    /// The denominator is enumerated from the source tree, so it has to be big and it has to contain
    /// the artefact the release is actually about. A hand-picked list would pass the tests above and
    /// still measure one file.
    /// </summary>
    [Fact]
    public void TheDenominatorIsTheWholeWwwroot()
    {
        var denominator = ReleaseContractTests.PackageProvenance.TreeContent("Tempo.Blazor");
        var tree = denominator.Files;

        tree.Should().HaveCountGreaterThan(
            120,
            "src/Tempo.Blazor/wwwroot held 168 files on 2.8.23; a handful means the sweep reads the "
            + "wrong directory and a sweep over nothing is green");
        tree.Should().ContainKey("css/tempo-blazor.bundled.css");
        tree.Should().ContainKey("css/tokens.css");
        tree.Keys.Should().AllSatisfy(key => key.Should().NotContain("\\", "paths are compared in the "
            + "package's separator, so a Windows run must not produce a different denominator"));

        denominator.PackExcludedPatterns.Should().Be(
            3,
            "src/Tempo.Blazor/Tempo.Blazor.csproj declares three Pack=false globs — *.test.mjs, "
            + "__tests__ and *.md under wwwroot/js. 'Read and empty' must not look like "
            + "'never read'");
        // js/__tests__/overlay.test.mjs + overlay-lifecycle.test.mjs — the overlay.js Node tests
        // are exactly what the __tests__ Pack=false glob was declared for: they ship in the repo,
        // never in the nupkg.
        denominator.PackExcludedFiles.Should().Be(2);
    }

    /// <summary>
    /// The denominator is what the SDK packs, not what the directory holds: Pack="false" globs in
    /// the project's own csproj are subtracted before the comparison. Driven end-to-end over a
    /// synthetic project so the csproj parse, the glob translation and the subtraction are all
    /// exercised — without it the 2.8.26 run reported tempo.blazor.documenteditor missing=140,
    /// every one a file the csproj intentionally never packs.
    /// </summary>
    [Fact]
    public void PackFalseGlobs_AreSubtractedFromTheDenominator_AndCounted()
    {
        var root = Path.Combine(Path.GetTempPath(), "provenance-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteTreeFile(root, "wwwroot/js/x/y.test.mjs");            // excluded: **\*.test.mjs
            WriteTreeFile(root, "wwwroot/js/top.test.mjs");            // excluded: '**' covers ZERO segments
            WriteTreeFile(root, "wwwroot/js/x/y.mjs");                 // kept: *.test.mjs must not reach y.mjs
            WriteTreeFile(root, "wwwroot/js/x/__tests__/deep/harness.mjs"); // excluded: __tests__ below depth
            WriteTreeFile(root, "wwwroot/js/__tests__/top.mjs");       // excluded: __tests__ at depth zero
            WriteTreeFile(root, "wwwroot/js/x/notes.md");              // excluded: **\*.md
            WriteTreeFile(root, "wwwroot/js/x/.gitkeep");              // excluded: **\.gitkeep
            WriteTreeFile(root, "wwwroot/css/app.css");                // kept: outside js/ entirely
            File.WriteAllText(Path.Combine(root, "Synthetic.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk.Razor">
                  <ItemGroup>
                    <None Include="wwwroot\js\**\*.test.mjs" Pack="false" Visible="false" />
                    <None Include="wwwroot\js\**\__tests__\**" Pack="false" Visible="false" />
                    <None Include="wwwroot\js\**\*.md" Pack="false" Visible="false" />
                    <None Include="wwwroot\js\**\.gitkeep" Pack="false" Visible="false" />
                    <None Include="obj\generated\**\*.tmp" Pack="false" />
                  </ItemGroup>
                </Project>
                """);

            var denominator = ReleaseContractTests.PackageProvenance.TreeContentUnder(root);

            denominator.PackExcludedPatterns.Should().Be(
                5,
                "the obj\\ pattern never reaches wwwroot and is STILL counted — the model's "
                + "population must be visible, or 'matched nothing' and 'was never read' look alike");
            denominator.PackExcludedFiles.Should().Be(6);
            denominator.Files.Keys.OrderBy(key => key, StringComparer.Ordinal).Should().Equal(
                "css/app.css", "js/x/y.mjs");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A Pack="false" shape the model cannot evaluate must fail closed: an unmeasurable exclusion
    /// that is silently ignored widens the denominator and reports intentionally unpacked files as
    /// missing — the exact false red this model exists to remove. Each case throws rather than
    /// guesses, and the thrown message names the shape it could not read.
    /// </summary>
    [Fact]
    public void APackFalseShapeTheModelCannotEvaluate_Throws_InsteadOfGuessingTheDenominator()
    {
        var cases = new (string Shape, string Item)[]
        {
            ("a substitution", """<None Include="wwwroot\js\$(Kind)\**\*.mjs" Pack="false" />"""),
            ("a condition", """<None Include="wwwroot\js\**\*.mjs" Pack="false" Condition="'$(ShipJs)'=='true'" />"""),
            ("an update", """<None Update="wwwroot\js\**\*.mjs" Pack="false" />"""),
            ("an exclude", """<None Include="wwwroot\js\**\*.mjs" Exclude="wwwroot\js\keep.mjs" Pack="false" />"""),
            ("a partial **", """<None Include="wwwroot\js\a**b\*.mjs" Pack="false" />"""),
            ("an evaluated pack", """<None Include="wwwroot\js\**\*.mjs" Pack="$(ShipJs)" />"""),
        };

        foreach (var (shape, item) in cases)
        {
            var root = Path.Combine(Path.GetTempPath(), "provenance-" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteTreeFile(root, "wwwroot/js/x.mjs");
                File.WriteAllText(Path.Combine(root, "Synthetic.csproj"),
                    $"<Project><ItemGroup>{item}</ItemGroup></Project>");

                Action act = () => ReleaseContractTests.PackageProvenance.TreeContentUnder(root);

                act.Should().Throw<InvalidOperationException>(
                    $"{shape} makes the exclusion set depend on evaluation this model does not "
                    + "run — unmeasurable must fail the run, never pass as a wrong denominator");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// The fix measured on the tree that motivated it: DocumentEditor's csproj subtracts four globs
    /// — 140 files on 2.8.26 — and the denominator keeps only what the package can carry. The exact
    /// file count is deliberately not pinned (js sources come and go); what is pinned is that no
    /// test or placeholder artefact survives the subtraction, because surviving is what 'missing'
    /// was reporting.
    /// </summary>
    [Fact]
    public void TheDocumentEditorDenominator_DropsItsPackFalseFiles_NotItsPackedOnes()
    {
        var denominator = ReleaseContractTests.PackageProvenance.TreeContent(
            "Tempo.Blazor.DocumentEditor");

        denominator.PackExcludedPatterns.Should().Be(
            4,
            "src/Tempo.Blazor.DocumentEditor/Tempo.Blazor.DocumentEditor.csproj declares four "
            + "Pack=false globs; a fifth has to reach this number, not hide inside it");
        denominator.PackExcludedFiles.Should().BeGreaterThan(
            0,
            "the 2.8.26 package lacked exactly these files and the gate read them as missing=140");
        denominator.Files.Keys.Should().NotContain(
            key => key.EndsWith(".test.mjs", StringComparison.Ordinal),
            "*.test.mjs under wwwroot/js is Pack=false — every one left in the denominator reports "
            + "missing against a package that correctly never carried it");
        denominator.Files.Keys.Should().NotContain(key => key.Contains("__tests__/"));
        denominator.Files.Should().ContainKey(
            "css/tempo-blazor-document-editor.css",
            "packed assets must survive the subtraction — an exclusion model that eats the real "
            + "payload is the same wrong denominator in the other direction");
    }

    /// <summary>Writes a file into a synthetic project tree, creating its directories.</summary>
    private static void WriteTreeFile(string root, string relative)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative);
    }

    /// <summary>
    /// The sweep's denominator is every row in <c>eng/nuget-packages.txt</c> — 26 of them — and every
    /// row must yield a package id the feed can be asked about. A csproj without <c>&lt;PackageId&gt;</c>
    /// records itself as unasked rather than guessed, so this asserts none of them has to.
    /// </summary>
    [Fact]
    public void EveryManifestRow_YieldsAnAskablePackageId()
    {
        var root = FindRepoRoot();
        var rows = File.ReadAllLines(Path.Combine(root, "eng", "nuget-packages.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        rows.Should().HaveCount(26, "the manifest is the sweep's denominator — a count drift is a "
            + "population drift, and this number is the record of what 'all' means");

        var unreadable = rows.Where(row =>
        {
            var csproj = Path.Combine(root, row.Replace('/', Path.DirectorySeparatorChar));
            return !File.Exists(csproj)
                || !System.Text.RegularExpressions.Regex.IsMatch(
                    File.ReadAllText(csproj), @"<PackageId>[^<]+</PackageId>");
        }).ToList();

        unreadable.Should().BeEmpty(
            "a manifest row whose csproj carries no <PackageId> enters the survey as unasked — the "
            + "feed is never given the chance to answer for it, and 'the sweep asked every id' stops "
            + "being true");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("repository root not found from " + AppContext.BaseDirectory);
    }
}
