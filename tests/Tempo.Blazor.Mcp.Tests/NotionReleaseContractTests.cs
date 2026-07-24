using System.Reflection;
using System.Xml.Linq;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Mcp.Tests;

public sealed class NotionReleaseContractTests
{
    [Fact]
    public void CanonicalTableRows_ExposeNoLegacyFlatCells()
    {
        typeof(ITableRowBlockContent).GetProperty("Cells")
            .Should().BeNull();
        typeof(TableRowBlockContent).GetProperty("Cells")
            .Should().BeNull();

        var sourceRoot = Path.Combine(
            RepoRoot(),
            "src",
            "Tempo.Blazor.NotionEditor");
        var offenders = Directory.EnumerateFiles(
                sourceRoot,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains(
                           "new TableRowBlockContent { Cells",
                           StringComparison.Ordinal) ||
                       source.Contains(
                           "ITableRowBlockContent row => row.Cells",
                           StringComparison.Ordinal) ||
                       source.Contains(
                           "row!.Cells",
                           StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(RepoRoot(), path))
            .ToList();

        offenders.Should().BeEmpty(
            "2.7.0 has one canonical RichCells table-row representation");
    }

    [Fact]
    public void DemoAggregateAdapter_HasNoLegacyCellFallback()
    {
        var path = Path.Combine(
            RepoRoot(),
            "src",
            "Tempo.Blazor.Demo.Api",
            "Data",
            "DemoNotionAggregateStore.cs");

        File.ReadAllText(path).Should().NotContain(
            "row.Cells",
            "the demo boundary must reject legacy rows instead of upgrading them at runtime");
    }

    [Fact]
    public void NotionAuthoring_HasNoGranularProviderOrBlockEndpoints()
    {
        typeof(INotionAggregateProvider).Assembly.GetType(
                "Tempo.Blazor.NotionEditor.Interfaces.INotionBlockProvider")
            .Should().BeNull();

        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var offenders = Directory.EnumerateFiles(
                sourceRoot,
                "*.*",
                SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) ||
                           path.EndsWith(".razor", StringComparison.Ordinal))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("INotionBlockProvider", StringComparison.Ordinal) ||
                       source.Contains("/api/notion/blocks", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(RepoRoot(), path))
            .ToList();

        offenders.Should().BeEmpty(
            "2.7 Notion authoring must persist exclusively through complete aggregate snapshots");
    }

    [Fact]
    public void PackableProjects_AreSynchronizedTo271()
    {
        var projectVersions = Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "src"),
                "*.csproj",
                SearchOption.AllDirectories)
            .Select(path => (
                Path: path,
                Version: XDocument.Load(path)
                    .Descendants("Version")
                    .Select(element => element.Value.Trim())
                    .SingleOrDefault()))
            .Where(item => item.Version is not null)
            .ToList();

        projectVersions.Should().NotBeEmpty();
        projectVersions.Should().OnlyContain(
            item => item.Version == "2.7.1",
            "all locally packable projects in the 2.7.1 patch release must agree on the version");
    }

    private static string RepoRoot()
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
