using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using Tempo.Blazor.Mcp;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>Guards the modeling MCP tool contract: a stable, snapshotted set of tool names.</summary>
public class TempoModelingMcpRegistrationTests
{
    private static IEnumerable<string> ToolNames(IReadOnlyList<Type> toolTypes)
        => toolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name!)
            .OrderBy(n => n);

    [Fact]
    public void ModelingToolNames_MatchExpectedContract()
    {
        ToolNames(TempoModelingMcp.ToolTypes).Should().BeEquivalentTo(new[]
        {
            "modeling_apply_operations",
            "modeling_export_bpmn",
            "modeling_get_model_tree",
            "modeling_get_property_schema",
            "modeling_get_view",
            "modeling_import_bpmn",
            "modeling_list_models",
            "modeling_list_notations",
            "modeling_validate"
        });
    }

    [Fact]
    public void EveryModelingTool_HasNameAndDescription()
    {
        foreach (var method in TempoModelingMcp.ToolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            var tool = method.GetCustomAttribute<McpServerToolAttribute>()!;
            var description = method.GetCustomAttribute<DescriptionAttribute>();

            tool.Name.Should().NotBeNullOrWhiteSpace();
            description.Should().NotBeNull($"{method.Name} must have a description for the LLM");
            description!.Description.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Modeling_IsPartOfTheAggregateToolset()
    {
        TempoBlazorMcp.ToolTypes.Should().Contain(TempoModelingMcp.ToolTypes);
    }
}
