using System.Reflection;
using ModelContextProtocol.Server;
using Tempo.Blazor.Mcp;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>
/// Drift guard for docs/modeling-mcp-tools.md (phase 7): every registered modeling MCP tool must have
/// a section in the catalog and the catalog must not document tools that no longer exist — the same
/// contract <c>DocumentMcpToolsDocumentationDriftTests</c> enforces for the document suite.
/// </summary>
public class ModelingMcpToolsDocumentationDriftTests
{
    private const string DocRelativePath = "docs/modeling-mcp-tools.md";

    /// <summary>
    /// The catalog also documents <c>diagram_render_svg</c> (the diagram suite's addition this plan
    /// introduced and the modeling view bridges into), so the "known" set of the stale-section guard
    /// is modeling + diagram.
    /// </summary>
    private static IReadOnlyList<string> KnownToolNames()
        => RegisteredToolNames()
            .Concat(ToolNames(TempoDiagramMcp.ToolTypes))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> RegisteredToolNames() => ToolNames(TempoModelingMcp.ToolTypes);

    private static IReadOnlyList<string> ToolNames(IReadOnlyList<Type> toolTypes)
        => toolTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void EveryRegisteredModelingTool_HasACatalogSection()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), DocRelativePath));
        var missing = RegisteredToolNames()
            .Where(name => !doc.Contains($"### `{name}`", StringComparison.Ordinal))
            .ToList();

        missing.Should().BeEmpty(
            $"every modeling MCP tool needs a '### `<name>`' section in {DocRelativePath}");
    }

    [Fact]
    public void CatalogDoesNotDocumentUnknownModelingTools()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), DocRelativePath));
        var known = KnownToolNames().ToHashSet(StringComparer.Ordinal);
        var documented = System.Text.RegularExpressions.Regex
            .Matches(doc, @"^### `([a-z0-9_]+)`", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToList();

        documented.Should().NotBeEmpty("the catalog must use '### `<tool_name>`' sections");
        documented.Where(name => !known.Contains(name)).Should().BeEmpty(
            "the catalog documents tools that are no longer registered — remove or rename the stale sections");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return directory!.FullName;
    }
}
