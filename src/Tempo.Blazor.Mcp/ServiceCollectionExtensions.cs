using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Mcp.DocumentEditor;
using Tempo.Blazor.Mcp.Diagram;
using Tempo.Blazor.Mcp.Modeling;
using Tempo.Blazor.Mcp.Notion;
using Tempo.Blazor.Mcp.Reporting;
using Tempo.Blazor.Mcp.Wireframe;
using Tempo.Reporting.Abstractions.Data;
using Tempo.Reporting.Engine.Fonts;
using Tempo.Reporting.Engine.Pdf;

namespace Tempo.Blazor.Mcp;

/// <summary>
/// Registration helpers for the wireframe MCP tools.
/// </summary>
/// <remarks>
/// The host application maps the tools onto its own MCP server, e.g.:
/// <code>
/// builder.Services.AddTempoWireframeMcpTools();
/// builder.Services.AddMcpServer()
///     .WithHttpTransport()
///     .WithToolsFromAssembly(typeof(TempoWireframeMcp).Assembly);
/// // plus the host's ITempoDocumentLibraryProvider and IWireframeDocumentProvider.
/// </code>
/// Prefer <c>WithToolsFromAssembly</c> over <c>WithTools(ToolTypes)</c>: the assembly scan
/// advertises the <c>tools</c> capability in the MCP handshake, which the type list does not.
/// </remarks>
public static class TempoWireframeMcp
{
    /// <summary>
    /// The wireframe tool types, exposed for hosts that register tools by type. Prefer
    /// <c>WithToolsFromAssembly(typeof(TempoWireframeMcp).Assembly)</c>, which also advertises
    /// the <c>tools</c> capability in the handshake.
    /// </summary>
    public static IReadOnlyList<Type> ToolTypes { get; } =
    [
        typeof(WireframeComponentCatalogTools),
        typeof(WireframeDocumentTools),
        typeof(WireframeValidationTools),
        typeof(WireframeOperationTools),
        typeof(WireframeAuthoringGuideTools),
        typeof(WireframeBriefTools)
    ];

    /// <summary>
    /// Registers the dependencies the wireframe MCP tools resolve from DI (the component schema
    /// registry). The host must additionally supply an <c>ITempoDocumentLibraryProvider</c> and an
    /// <c>IWireframeDocumentProvider</c>, and register the tools with its MCP server via
    /// <see cref="ToolTypes"/>.
    /// </summary>
    public static IServiceCollection AddTempoWireframeMcpTools(this IServiceCollection services)
    {
        services.AddWireframeSchemas();
        return services;
    }
}

/// <summary>
/// Registration helpers for the diagram/draw MCP tools.
/// </summary>
public static class TempoDiagramMcp
{
    /// <summary>The diagram/draw tool types, exposed for hosts that register tools by type.</summary>
    public static IReadOnlyList<Type> ToolTypes { get; } =
    [
        typeof(DiagramDocumentTools),
        typeof(DiagramOperationTools),
        typeof(DiagramValidationTools),
        typeof(DiagramStencilCatalogTools),
        typeof(DiagramBriefTools),
        typeof(DiagramRenderTools)
    ];

    /// <summary>
    /// Registers dependencies required by the diagram/draw MCP tools. The host must supply
    /// <c>ITempoDocumentLibraryProvider</c> and <c>IDiagramDocumentProvider</c>; stencil providers
    /// are optional and enable the catalog tools plus stricter validation.
    /// </summary>
    public static IServiceCollection AddTempoDiagramMcpTools(this IServiceCollection services)
        => services;
}

/// <summary>
/// Registration helpers for the architecture/modeling MCP tools.
/// </summary>
public static class TempoModelingMcp
{
    /// <summary>The modeling tool types, exposed for hosts that register tools by type.</summary>
    public static IReadOnlyList<Type> ToolTypes { get; } =
    [
        typeof(ModelingModelTools),
        typeof(ModelingOperationTools),
        typeof(ModelingValidationTools)
    ];

    /// <summary>
    /// Registers dependencies required by the modeling MCP tools. The host must supply
    /// <c>ITempoDocumentLibraryProvider</c> and <c>IModelingModelDocumentProvider</c>; the notation
    /// rule providers, <c>IModelingDiagramProjector</c> (for <c>modeling_get_view</c>) and the
    /// diagram SVG renderer (for <c>diagram_render_svg</c>) come from <c>AddTempoBlazorModeling()</c>
    /// / <c>AddTempoBlazorDiagramEditor()</c> and degrade gracefully when absent.
    /// </summary>
    public static IServiceCollection AddTempoModelingMcpTools(this IServiceCollection services)
        => services;
}

/// <summary>
/// Registration helpers for the DocumentEditor MCP tools.
/// </summary>
public static class TempoDocumentEditorMcp
{
    /// <summary>The DocumentEditor tool types, exposed for hosts that register tools by type.</summary>
    public static IReadOnlyList<Type> ToolTypes { get; } =
    [
        typeof(DocumentEditorDocumentTools),
        typeof(DocumentEditorOperationTools),
        typeof(DocumentEditorAnalysisTools),
        typeof(DocumentEditorDescribeTools),
        typeof(DocumentEditorSemanticTextTools),
        typeof(DocumentEditorBlockTools),
        typeof(DocumentEditorAuthoringTools),
        typeof(DocumentEditorRenderTools),
        typeof(DocumentEditorTemplateTools),
        typeof(DocumentEditorDiffTools)
    ];

    /// <summary>
    /// Registers dependencies required by the DocumentEditor MCP tools. The host must supply an
    /// <c>IDocumentEditorProvider</c>.
    /// </summary>
    public static IServiceCollection AddTempoDocumentEditorMcpTools(this IServiceCollection services)
        => services;
}

/// <summary>
/// Registration helpers for the NotionEditor MCP tools.
/// </summary>
public static class TempoNotionMcp
{
    /// <summary>The NotionEditor tool types, exposed for hosts that register tools by type.</summary>
    public static IReadOnlyList<Type> ToolTypes { get; } =
    [
        typeof(NotionPageTools),
        typeof(NotionBlockTools),
        typeof(NotionSchemaAndValidationTools)
    ];

    /// <summary>
    /// Registers dependencies required by the NotionEditor MCP tools. The host must supply
    /// <c>INotionDataProvider</c> for page metadata/lifecycle tools and
    /// <c>INotionAggregateProvider</c> for canonical recursive reads and atomic block authoring.
    /// Hosts may implement <c>INotionIdempotentAggregateProvider</c> to replace the registered
    /// process-local receipt fallback with a durable transactional boundary.
    /// </summary>
    public static IServiceCollection AddTempoNotionMcpTools(this IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryNotionIdempotencyReceiptStore>();
        return services;
    }
}

/// <summary>
/// Registration helpers for the Tempo Reporting MCP tools.
/// </summary>
public static class TempoReportingMcp
{
    /// <summary>The reporting tool types, exposed for hosts that register tools by type.</summary>
    public static IReadOnlyList<Type> ToolTypes { get; } =
    [
        typeof(ReportCatalogTools),
        typeof(ReportDefinitionTools),
        typeof(ReportValidationTools),
        typeof(ReportPreviewTools)
    ];

    /// <summary>
    /// Registers dependencies required by the reporting MCP tools. The host should supply an
    /// <see cref="IReportDefinitionStore"/> and may supply its own <see cref="IReportDataProvider"/>
    /// and renderer services. Fallback services support static/empty preview flows.
    /// </summary>
    public static IServiceCollection AddTempoReportingMcpTools(this IServiceCollection services)
    {
        services.TryAddSingleton<IReportDataProvider, ReportingMcpFallbackDataProvider>();
        services.TryAddSingleton<ITextMeasurer, ReportingMcpTextMeasurer>();
        services.TryAddSingleton<ReportPdfRenderer>();
        return services;
    }
}

/// <summary>
/// Registration helpers for the complete Tempo.Blazor MCP toolset.
/// </summary>
public static class TempoBlazorMcp
{
    /// <summary>All tool types shipped by the MCP package.</summary>
    public static IReadOnlyList<Type> ToolTypes { get; } =
        TempoWireframeMcp.ToolTypes
            .Concat(TempoDiagramMcp.ToolTypes)
            .Concat(TempoModelingMcp.ToolTypes)
            .Concat(TempoDocumentEditorMcp.ToolTypes)
            .Concat(TempoNotionMcp.ToolTypes)
            .Concat(TempoReportingMcp.ToolTypes)
            .ToList();

    /// <summary>
    /// Registers shared dependencies for all Tempo.Blazor MCP tools. The host still supplies the
    /// persistence providers for the domains it enables.
    /// </summary>
    public static IServiceCollection AddTempoBlazorMcpTools(this IServiceCollection services)
    {
        services.AddTempoWireframeMcpTools();
        services.AddTempoDiagramMcpTools();
        services.AddTempoModelingMcpTools();
        services.AddTempoDocumentEditorMcpTools();
        services.AddTempoNotionMcpTools();
        services.AddTempoReportingMcpTools();
        return services;
    }
}
