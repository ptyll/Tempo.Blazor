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

    /// <summary>
    /// An overlay whose <c>sourcePath</c> does not exist is still merged into the shipped bundle
    /// (the generator treats it as an orphan and keeps it). That is how the removed
    /// <c>ICommentEntry</c> interface kept shipping after 2.0.0. Every overlay under
    /// <c>JsonDocumentation</c> is checked; known orphans are an explicit allow-list that may only
    /// shrink — an allow-listed path that now resolves fails the test so the entry is removed.
    /// </summary>
    [Fact]
    public void OverlaySourcePaths_Exist_ExceptKnownOrphans_AndICommentEntryIsGone()
    {
        var repo = ThemeCss.RepositoryRoot().FullName;
        var docs = Path.Combine(repo, "JsonDocumentation");
        var missing = new List<string>();
        var resolvedAllowList = new List<string>();

        foreach (var file in Directory.GetFiles(docs, "*.json", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}JsonDocumentationGenerator{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (!document.RootElement.TryGetProperty("sourcePath", out var sourcePathElement))
            {
                continue;
            }

            var sourcePath = sourcePathElement.GetString();
            var relativeOverlay = Path.GetRelativePath(repo, file).Replace('\\', '/');
            var full = Path.Combine(repo, (sourcePath ?? "").Replace('/', Path.DirectorySeparatorChar));
            var exists = !string.IsNullOrWhiteSpace(sourcePath) && File.Exists(full);
            if (KnownOrphanSourcePaths.Contains(relativeOverlay))
            {
                if (exists)
                {
                    resolvedAllowList.Add(relativeOverlay);
                }

                continue;
            }

            if (!exists)
            {
                missing.Add($"{relativeOverlay} -> {sourcePath}");
            }
        }

        missing.Should().BeEmpty(
            "an overlay whose sourcePath does not exist ships as a phantom type; add it to KnownOrphanSourcePaths only when the orphan is already known, and never grow that list");
        resolvedAllowList.Should().BeEmpty(
            "an allow-listed overlay whose sourcePath now resolves must be removed from KnownOrphanSourcePaths — the list may only shrink");
        File.Exists(Path.Combine(repo, "JsonDocumentation", "Abstractions", "Interfaces", "ICommentEntry.json"))
            .Should().BeFalse("ICommentEntry was removed in 2.0.0 (4a27f28c); its overlay must not ship");
    }

    /// <summary>
    /// Overlay files whose <c>sourcePath</c> does not resolve, measured when the repo-wide guard
    /// replaced the comment-only pin. The four Backplane overlays were corrected onto
    /// <c>DocumentCollaborationBackplane.cs</c> and are not listed. Remove an entry once its file
    /// exists — never add one.
    /// </summary>
    private static readonly HashSet<string> KnownOrphanSourcePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "JsonDocumentation/Abstractions/Models/GanttComment.json",
        "JsonDocumentation/Abstractions/Notion-Editor/BlockComment.json",
        "JsonDocumentation/Abstractions/Notion-Editor/CommentReaction.json",
        "JsonDocumentation/Abstractions/Notion-Editor/IBlockComment.json",
        "JsonDocumentation/Abstractions/Notion-Editor/ICommentReaction.json",
        "JsonDocumentation/Abstractions/Notion-Editor/INotionCommentEntry.json",
        "JsonDocumentation/Abstractions/Notion-Editor/IPageComment.json",
        "JsonDocumentation/Abstractions/Notion-Editor/ITextAnchorComment.json",
        "JsonDocumentation/Abstractions/Notion-Editor/NotionCommentEntry.json",
        "JsonDocumentation/Abstractions/Notion-Editor/PageComment.json",
        "JsonDocumentation/Abstractions/Notion-Editor/TextAnchorComment.json",
        "JsonDocumentation/Components/Components/BarcodeFormat.json",
        "JsonDocumentation/Components/Components/QRErrorCorrectionLevel.json",
        "JsonDocumentation/Components/Services/IndexedDbDocumentOfflineStore.json",
        "JsonDocumentation/Packages/Tempo.Blazor.DocumentEditor/items/Components/CoreEngineModelConverter.json",
        "JsonDocumentation/Packages/Tempo.Blazor.EmailTemplates.Engine/items/Rendering/AngleSharpHtmlContentSanitizer.json",
        "JsonDocumentation/Packages/Tempo.Blazor.EmailTemplates.Engine/items/Templating/ScribanTemplateVariableExtractor.json",
        "JsonDocumentation/Packages/Tempo.Blazor.GanttXlsx/items/Services/GanttExcelImporter.json",
        "JsonDocumentation/Packages/Tempo.Blazor.GanttXlsx/items/Services/GanttXlsxExporter.json",
        "JsonDocumentation/Packages/Tempo.Blazor.Modeling/items/Modeling/ModelingModelChangedEventArgs.json",
        "JsonDocumentation/Packages/Tempo.Blazor.Modeling/items/Modeling/ModelingSaveMode.json",
        "JsonDocumentation/Packages/Tempo.Blazor.Modeling/items/Modeling/UpdateModelingElementCommand.json",
        "JsonDocumentation/Packages/Tempo.Blazor.Modeling/items/Modeling/UpdateModelingRelationshipCommand.json",
        "JsonDocumentation/Packages/Tempo.Blazor.PdfViewer/items/Components/PdfViewMode.json",
        "JsonDocumentation/Packages/Tempo.Blazor.Wireframe/items/Components/BuiltInWireframeComponentProvider.json",
    };
}
