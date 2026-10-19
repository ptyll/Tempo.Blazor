using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

internal static class TempoNativeRendererProvider
{
    private static readonly string[] NativeTypes =
    [
        "TmChart",
        "TmGauge",
        "TmStockChart",
        "TmKanbanBoard",
        "TmPivotTable",
        "TmGantt",
        "TmWorkflowDesignerCanvas",
        "TmDiagramEditor",
        "TmDashboard",
        "TmSpreadsheet",
        "TmDocumentEditor",
        "TmNotionEditor",
        "TmChat"
    ];

    public static IReadOnlyDictionary<string, Action<WireframeElement, RenderTreeBuilder>> GetRenderers()
    {
        var nativeTypes = NativeTypes.ToHashSet(StringComparer.Ordinal);
        var renderers = TempoNativeRendererDefinitions.GetDefinitions()
            .Where(def => nativeTypes.Contains(def.Type))
            .ToDictionary(def => def.Type, def => def.RenderSvg, StringComparer.Ordinal);

        var missing = NativeTypes.Where(type => !renderers.ContainsKey(type)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Missing built-in native wireframe renderers: {string.Join(", ", missing)}.");
        }

        return renderers;
    }
}
