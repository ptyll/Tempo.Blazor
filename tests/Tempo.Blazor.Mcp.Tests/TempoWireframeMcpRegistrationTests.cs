using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Mcp;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>
/// Guards the MCP tool contract: registration wires the schema registry, and every tool exposes a
/// stable snake_case name and a non-empty description (snapshot of the LLM-facing surface).
/// </summary>
public class TempoWireframeMcpRegistrationTests
{
    private static IEnumerable<MethodInfo> ToolMethods(IReadOnlyList<Type> toolTypes)
        => toolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

    [Fact]
    public void AddTempoWireframeMcpTools_RegistersSchemaRegistry()
    {
        var services = new ServiceCollection();
        services.AddTempoWireframeMcpTools();

        using var provider = services.BuildServiceProvider();
        provider.GetService<WireframeSchemaRegistry>().Should().NotBeNull();
    }

    [Fact]
    public void EveryTool_HasNameAndDescription()
    {
        foreach (var method in ToolMethods(TempoBlazorMcp.ToolTypes))
        {
            var tool = method.GetCustomAttribute<McpServerToolAttribute>()!;
            var description = method.GetCustomAttribute<DescriptionAttribute>();

            tool.Name.Should().NotBeNullOrWhiteSpace($"{method.Name} must have a stable tool name");
            description.Should().NotBeNull($"{method.Name} must have a description for the LLM");
            description!.Description.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void ToolNames_MatchExpectedContract()
    {
        var names = ToolMethods(TempoWireframeMcp.ToolTypes)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name)
            .OrderBy(n => n)
            .ToList();

        names.Should().BeEquivalentTo(new[]
        {
            "wireframe_apply_operations",
            "wireframe_author_document",
            "wireframe_create_document",
            "wireframe_get_authoring_guide",
            "wireframe_get_component_schema",
            "wireframe_get_document",
            "wireframe_get_implementation_brief",
            "wireframe_list_components",
            "wireframe_list_documents",
            "wireframe_replace_document",
            "wireframe_scaffold",
            "wireframe_validate_document"
        });
    }

    [Fact]
    public void AddTempoBlazorMcpTools_RegistersSharedDependencies()
    {
        var services = new ServiceCollection();
        services.AddTempoBlazorMcpTools();

        using var provider = services.BuildServiceProvider();
        provider.GetService<WireframeSchemaRegistry>().Should().NotBeNull();
    }

    [Fact]
    public void AllToolNames_ContainWireframeAndDiagramContracts()
    {
        var names = ToolMethods(TempoBlazorMcp.ToolTypes)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name)
            .OrderBy(n => n)
            .ToList();

        names.Should().BeEquivalentTo(new[]
        {
            "create_report",
            "diagram_apply_operations",
            "diagram_create_document",
            "diagram_get_document",
            "diagram_get_implementation_brief",
            "diagram_get_stencil",
            "diagram_list_documents",
            "diagram_list_stencils",
            "diagram_render_svg",
            "diagram_replace_document",
            "diagram_validate_document",
            "document_editor_apply_operations",
            "document_editor_create",
            "document_editor_delete_block",
            "document_editor_delete_text",
            "document_editor_describe_document",
            "document_editor_diff_versions",
            "document_editor_export",
            "document_editor_export_redline",
            "document_editor_format_range",
            "document_editor_import",
            "document_editor_insert_block",
            "document_editor_get_document",
            "document_editor_get_json",
            "document_editor_get_outline",
            "document_editor_get_versions",
            "document_editor_insert_repeating_section",
            "document_editor_insert_text",
            "document_editor_insert_token",
            "document_editor_move_block",
            "document_editor_replace_document",
            "document_editor_replace_text",
            "document_editor_restore_version",
            "document_editor_save_document",
            "document_editor_search_text",
            "document_editor_set_heading",
            "document_editor_set_paragraph_properties",
            "document_editor_set_table_cell_text",
            "document_editor_update_block",
            "document_editor_wrap_conditional",
            "document_assemble_render",
            "document_render_pdf",
            "document_render_preview",
            "document_template_describe",
            "document_editor_validate_document",
            "get_report_definition",
            "list_reports",
            "modeling_apply_operations",
            "modeling_export_bpmn",
            "modeling_get_model_tree",
            "modeling_get_property_schema",
            "modeling_get_view",
            "modeling_import_bpmn",
            "modeling_list_models",
            "modeling_list_notations",
            "modeling_validate",
            "notion_apply_block_operations",
            "notion_create_page",
            "notion_delete_page",
            "notion_duplicate_page",
            "notion_get_authoring_guide",
            "notion_get_block_schema",
            "notion_get_block_tree",
            "notion_get_operation_catalog",
            "notion_get_page",
            "notion_list_block_types",
            "notion_list_pages",
            "notion_move_page",
            "notion_restore_page",
            "notion_update_page",
            "render_report_preview",
            "update_report_elements",
            "validate_report",
            "wireframe_apply_operations",
            "wireframe_author_document",
            "wireframe_create_document",
            "wireframe_get_authoring_guide",
            "wireframe_get_component_schema",
            "wireframe_get_document",
            "wireframe_get_implementation_brief",
            "wireframe_list_components",
            "wireframe_list_documents",
            "wireframe_replace_document",
            "wireframe_scaffold",
            "wireframe_validate_document"
        });
    }
}
