using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// N178 — the SQL Server Testcontainers image is pinned by digest in BOTH fixture files, and the
/// two constants must stay byte-identical so the API-catalog lane and the Web-cache lane always
/// run the same engine. The test lives here (not in either SQL project) so it runs without Docker.
/// </summary>
public class SqlServerImagePinTests
{
    private static readonly Regex ImageConstant = new(
        @"ContainerImage\s*=\s*""(?<image>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void PinnedImageDigestMatchesInBothFixtures()
    {
        string apiSource = ReleaseGateFilterTests.ReadRepoFile(
            Path.Combine("tests", "Tempo.ReportServer.Api.Tests", "MsSql", "MsSqlTestDatabase.cs"));
        string webSource = ReleaseGateFilterTests.ReadRepoFile(
            Path.Combine("tests", "Tempo.ReportServer.Web.Tests", "SqlServerCacheFixture.cs"));

        string apiImage = ImageConstant.Match(apiSource).Groups["image"].Value;
        string webImage = ImageConstant.Match(webSource).Groups["image"].Value;

        apiImage.Should().NotBeNullOrEmpty("MsSqlTestDatabase.ContainerImage must be a string literal");
        webImage.Should().NotBeNullOrEmpty("SqlServerCacheFixture.ContainerImage must be a string literal");

        apiImage.Should().Contain("@sha256:",
            "N178: the API fixture image must be pinned by digest, not a floating tag like :2022-latest");
        webImage.Should().Contain("@sha256:",
            "N178: the Web fixture image must be pinned by digest, not a floating tag like :2022-latest");
        apiImage.Should().Be(webImage,
            "N178: both SQL fixture lanes must run the byte-identical engine image");
    }
}
