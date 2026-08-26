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
        var result = ReleaseContractTests.PackageProvenance.Compare(Id, Version, Tree(), Tree(), 0);

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

        var result = ReleaseContractTests.PackageProvenance.Compare(Id, Version, Tree(), packed, 0);

        result.Differing.Should().Equal("css/tempo-blazor.bundled.css");
        result.Matching.Should().HaveCount(2);
        result.Report.Should().Contain("differing=1").And.Contain("css/tempo-blazor.bundled.css");
    }

    [Fact]
    public void AnItemThePackageDoesNotCarry_IsMissing_NotSilentlyMatching()
    {
        var packed = Tree();
        packed.Remove("js/data-table.js");

        var result = ReleaseContractTests.PackageProvenance.Compare(Id, Version, Tree(), packed, 0);

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

        var result = ReleaseContractTests.PackageProvenance.Compare(Id, Version, Tree(), packed, 0);

        result.ExtraInPackage.Should().Equal("Tempo.Blazor.abc123.bundle.scp.css");
        result.Differing.Should().BeEmpty();
        result.Missing.Should().BeEmpty();
        result.Report.Should().Contain("extra-in-package=1");
    }

    /// <summary>
    /// The denominator is enumerated from the source tree, so it has to be big and it has to contain
    /// the artefact the release is actually about. A hand-picked list would pass the tests above and
    /// still measure one file.
    /// </summary>
    [Fact]
    public void TheDenominatorIsTheWholeWwwroot()
    {
        var tree = ReleaseContractTests.PackageProvenance.TreeContentForTests();

        tree.Should().HaveCountGreaterThan(
            120,
            "src/Tempo.Blazor/wwwroot held 168 files on 2.8.23; a handful means the sweep reads the "
            + "wrong directory and a sweep over nothing is green");
        tree.Should().ContainKey("css/tempo-blazor.bundled.css");
        tree.Should().ContainKey("css/tokens.css");
        tree.Keys.Should().AllSatisfy(key => key.Should().NotContain("\\", "paths are compared in the "
            + "package's separator, so a Windows run must not produce a different denominator"));
    }
}
