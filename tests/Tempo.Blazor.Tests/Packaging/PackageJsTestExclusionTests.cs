using FluentAssertions;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// Guards the paired <c>Content Remove</c> + <c>None Pack="false"</c> exclusion that keeps node
/// test suites out of a packable RCL's static web assets. A per-package check (AI only) used to
/// be the whole net; a new package with <c>wwwroot/js/__tests__</c> and no pair shipped the
/// suites to every consumer as <c>_content/…/js/__tests__/</c>.
/// </summary>
public sealed class PackageJsTestExclusionTests
{
    /// <summary>
    /// Every packable <c>src/*</c> project that actually has node tests under <c>wwwroot</c>
    /// covers each of those files with both halves of the exclusion pair. Matching is by compiled
    /// glob against the real file, so EmailTemplates / Signing (wider <c>wwwroot\**\__tests__\**</c>)
    /// pass and a dead glob that matches nothing does not.
    /// </summary>
    [Fact]
    public void Every_package_with_js_tests_excludes_them_from_static_web_assets()
    {
        var root = ReleaseScriptInputReadTests.FindRepoRoot();
        var coverage = PackageJsTestExclusion.InspectRepository(root);

        coverage.Should().NotBeEmpty(
            "the node suites under wwwroot are the reason the pair exists — if they all move, "
            + "the guard must go red instead of matching nothing");
        coverage.Select(item => item.ProjectName).Should().Contain(
            "Tempo.Blazor.AI",
            "Tempo.Blazor.AI is the known-good pair this guard replaced; losing it is a vacuous pass");

        var packableNames = PackageJsTestExclusion.EnumeratePackableProjectRoots(root)
            .Select(projectRoot => Path.GetFileName(
                projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        packableNames.Should().Contain(
            ["Tempo.Blazor.Modeling", "Tempo.Blazor.NotionEditor", "Tempo.Blazor.Reporting"],
            "IsPackable is read only from the csproj; those three RCLs must stay in the sweep");

        var violations = coverage.Where(item => item.IsViolation).Select(item => item.Format()).ToList();
        violations.Should().BeEmpty(
            "node test sources must not become static web assets — a consumer would serve them "
            + "from _content/<package>/… . Named violations: {0}",
            string.Join("; ", violations));
    }

    /// <summary>
    /// The packed-file denominator (<see cref="ReleaseContractTests.PackageProvenance.TreeContent"/>)
    /// is what would land in <c>staticwebassets/</c>. It subtracts only <c>Pack="false"</c>, not
    /// <c>Content Remove</c> — dropping the None half therefore turns this test red, while dropping
    /// only Content Remove leaves it green (the csproj guard above is what catches that half).
    /// </summary>
    [Fact]
    public void No_package_ships_js_tests_as_static_web_assets()
    {
        var root = ReleaseScriptInputReadTests.FindRepoRoot();
        var shipped = PackageJsTestExclusion.CollectShippedJsTests(root);

        shipped.Should().BeEmpty(
            "TreeContent keys are the staticwebassets payload; a __tests__ segment or *.test.mjs "
            + "file still in the denominator is a file the package would publish. Named: {0}",
            string.Join("; ", shipped));
    }

    /// <summary>
    /// A synthetic csproj with a test file and no exclusion pair is a violation naming both halves.
    /// </summary>
    [Fact]
    public void A_project_without_the_exclusion_pair_is_a_named_violation()
    {
        using var tree = SyntheticProject.Create(csproj: """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <PropertyGroup><IsPackable>true</IsPackable></PropertyGroup>
            </Project>
            """, testFile: "wwwroot/js/__tests__/sample.test.mjs");

        var coverage = PackageJsTestExclusion.InspectProject(tree.Root);

        coverage.Should().ContainSingle()
            .Which.Format().Should().Be(
                $"{tree.Name}: js/__tests__/sample.test.mjs (missing Content Remove, missing None Pack=false)");
    }

    /// <summary>
    /// <c>Content Remove</c> alone is not the pair — the provenance sweep reads Pack="false".
    /// </summary>
    [Fact]
    public void Content_Remove_without_None_Pack_false_is_missing_None()
    {
        using var tree = SyntheticProject.Create(csproj: """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <Content Remove="wwwroot\js\**\*.test.mjs" />
                <Content Remove="wwwroot\js\**\__tests__\**" />
              </ItemGroup>
            </Project>
            """, testFile: "wwwroot/js/__tests__/sample.test.mjs");

        var coverage = PackageJsTestExclusion.InspectProject(tree.Root);

        coverage.Should().ContainSingle();
        coverage[0].CoveredByContentRemove.Should().BeTrue();
        coverage[0].CoveredByNonePackFalse.Should().BeFalse();
        coverage[0].Format().Should().Be(
            $"{tree.Name}: js/__tests__/sample.test.mjs (missing None Pack=false)");
    }

    /// <summary>
    /// Signing / EmailTemplates declare the wider <c>wwwroot\**\__tests__\**</c> pair. That pair
    /// must count as coverage — requiring the narrower <c>wwwroot\js\**\…</c> string would false-red
    /// those packages.
    /// </summary>
    [Fact]
    public void A_wide_wwwroot_tests_glob_covers_the_file()
    {
        using var tree = SyntheticProject.Create(csproj: """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <Content Remove="wwwroot\**\__tests__\**" />
                <None Include="wwwroot\**\__tests__\**" Pack="false" Visible="false" />
              </ItemGroup>
            </Project>
            """, testFile: "wwwroot/__tests__/sample.test.mjs");

        var coverage = PackageJsTestExclusion.InspectProject(tree.Root);

        coverage.Should().ContainSingle();
        coverage[0].IsViolation.Should().BeFalse();
        coverage[0].CoveredByContentRemove.Should().BeTrue();
        coverage[0].CoveredByNonePackFalse.Should().BeTrue();
    }

    /// <summary>A disposable temp project with one csproj and one wwwroot test file.</summary>
    private sealed class SyntheticProject : IDisposable
    {
        private SyntheticProject(string root, string name)
        {
            Root = root;
            Name = name;
        }

        internal string Root { get; }

        internal string Name { get; }

        internal static SyntheticProject Create(string csproj, string testFile)
        {
            var name = "pkg-" + Guid.NewGuid().ToString("N");
            var root = Path.Combine(Path.GetTempPath(), name);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, name + ".csproj"), csproj);
            var path = Path.Combine(root, testFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "// synthetic");
            return new SyntheticProject(root, name);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
