using System.Text.Json;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Documentation;

/// <summary>
/// The documentation generator keys overlays by file name (the item base name) inside ONE package's
/// documentation roots and used to keep the first one silently when two overlays shared a name. A
/// stub dropped next to a hand-written overlay then shipped instead of the manual descriptions. The
/// generator now throws; this guard fails in the test project before a generate run, naming both
/// paths. Names are compared per package, because each package loads its own dictionary — the same
/// file name in two packages (AddLayerCommand in DiagramEditor and Wireframe) is not a collision.
/// </summary>
public class DocumentationOverlayUniquenessTests
{
    [Fact]
    public void JsonDocumentation_HasNoDuplicateOverlayFileNames()
    {
        var repo = ThemeCss.RepositoryRoot().FullName;
        var configPath = Path.Combine(repo, "JsonDocumentation", "packages.json");
        File.Exists(configPath).Should().BeTrue($"{configPath} lists the documentation roots the generator loads");

        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        var duplicates = new List<string>();

        foreach (var package in config.RootElement.GetProperty("packages").EnumerateArray())
        {
            var packageId = package.GetProperty("packageId").GetString();
            var files = package.GetProperty("documentationRoots").EnumerateArray()
                .Select(root => Path.Combine(repo, "JsonDocumentation", root.GetString()!.Replace('/', Path.DirectorySeparatorChar)))
                .Where(Directory.Exists)
                .SelectMany(root => Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                .ToList();

            duplicates.AddRange(files
                .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => $"{packageId}/{group.Key}: {string.Join(" and ", group.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))}"));
        }

        duplicates.Should().BeEmpty(
            "two overlays with the same file name inside one package collide in the generator (PackageDocumentationGenerator.LoadExistingItems)");
    }
}
