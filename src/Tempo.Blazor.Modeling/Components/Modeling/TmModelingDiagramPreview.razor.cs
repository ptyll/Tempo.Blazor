using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Shows a generated modeling diagram through the diagram editor canvas.</summary>
public partial class TmModelingDiagramPreview
{
    private const double DefaultDroppedNodeWidth = 160;
    private const double DefaultDroppedNodeHeight = 88;
    private const double DropCollisionOffset = 24;
    private const string DefaultLayerId = "default";

    /// <summary>Generated diagram document to show in the preview. Null renders the pre-generation empty state.</summary>
    [Parameter] public DiagramDocument? Document { get; set; }

    /// <summary>Raised when the embedded diagram editor reports a document mutation.</summary>
    [Parameter] public EventCallback<DiagramDocument> DocumentChanged { get; set; }

    /// <summary>Raised when the user requests opening the generated document in the full diagram editor.</summary>
    [Parameter] public EventCallback<DiagramDocument> OnOpenInEditor { get; set; }

    /// <summary>
    /// DOM id for the "Open in editor" trigger, owned by the hosting editor so its dialog can
    /// name the trigger as the focus-scope restore target. Null renders no id (the default).
    /// </summary>
    [Parameter] public string? OpenInEditorTriggerId { get; set; }

    /// <summary>Raised when the user requests a fresh diagram generation from the currently loaded model.</summary>
    [Parameter] public EventCallback OnGenerateRequested { get; set; }

    /// <summary>Raised when a dragged modeling tree item is dropped onto the preview canvas.</summary>
    [Parameter] public EventCallback<DragEventArgs> OnDrop { get; set; }

    /// <summary>Modeling element currently being dragged from the model tree.</summary>
    [Parameter] public ModelingElementDto? ActiveDraggedElement { get; set; }

    /// <summary>When false, model tree drops are ignored without mutating the preview document.</summary>
    [Parameter] public bool AllowNodeDrop { get; set; } = true;

    /// <summary>Raised after a tree element is reused as a new diagram node.</summary>
    [Parameter] public EventCallback<ModelingNodeDroppedEventArgs> OnNodeDropped { get; set; }

    /// <summary>Raised when a relationship edge is selected in the generated preview.</summary>
    [Parameter] public EventCallback<string> OnRelationshipSelected { get; set; }

    /// <summary>
    /// Whether the preview header renders its edit actions (the "Generate diagram" and
    /// "Open in editor" buttons). Default <c>true</c>. The mobile layout (Fáze 7, G2) passes
    /// <c>false</c>: those entries live in the editor's action bar and header overflow there,
    /// so the preview shows only viewer controls (the embedded editor's own toolbar).
    /// </summary>
    [Parameter] public bool ShowEditActions { get; set; } = true;

    /// <summary>
    /// Whether the embedded diagram editor renders its own chrome. Default <c>true</c>. The
    /// mobile band (Fáze 7 review) passes <c>false</c>: there the DE header bar would hold ONLY
    /// the "⋯" trigger (the title is hidden and the mobile chrome moves history/search/panels
    /// into it) and the floating zoom pill duplicates the canvas pinch, so the preview shows
    /// just the page tabs and the canvas. The reduction goes through the DE's public
    /// <c>ShowToolbar</c> parameter only — never CSS on its internals.
    /// </summary>
    [Parameter] public bool ShowEmbeddedEditorChrome { get; set; } = true;

    /// <summary>Additional CSS class applied to the preview root.</summary>
    [Parameter] public string? Class { get; set; }

    private string RootClass => string.Join(" ", new[] { "tm-modeling-diagram-preview", Class }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private int NodeCount => Document?.Nodes.Count ?? 0;

    private int EdgeCount => Document?.Edges.Count ?? 0;

    private bool IsEmptyDiagram => Document is not null && NodeCount == 0;

    private string StateName => Document is null
        ? "empty"
        : IsEmptyDiagram ? "empty-diagram" : "populated";

    private string SummaryText => Document is null
        ? Loc["TmModelingDiagramPreview_EmptySummary"]
        : string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Loc["TmModelingDiagramPreview_Summary"],
            NodeCount,
            EdgeCount);

    private Task HandleGenerateRequestedAsync() => OnGenerateRequested.InvokeAsync();

    private Task HandleOpenInEditorAsync()
        => Document is null ? Task.CompletedTask : OnOpenInEditor.InvokeAsync(Document);

    private Task HandleDocumentChangedAsync(DiagramDocument document)
        => DocumentChanged.InvokeAsync(document);

    private Task HandleSelectedIdsChangedAsync(string[] selectedIds)
    {
        if (!OnRelationshipSelected.HasDelegate || Document is null || selectedIds.Length != 1)
        {
            return Task.CompletedTask;
        }

        var selectedId = selectedIds[0];
        return Document.Edges.Any(edge => string.Equals(edge.Id, selectedId, StringComparison.Ordinal))
            ? OnRelationshipSelected.InvokeAsync(selectedId)
            : Task.CompletedTask;
    }

    private static Task HandleDragOver(DragEventArgs _) => Task.CompletedTask;

    private async Task HandleDropAsync(DragEventArgs args)
    {
        if (OnDrop.HasDelegate)
        {
            await OnDrop.InvokeAsync(args);
        }

        if (!AllowNodeDrop || Document is null || ActiveDraggedElement is null)
        {
            return;
        }

        if (args.OffsetX < 0 || args.OffsetY < 0)
        {
            return;
        }

        await InsertElementAsync(ActiveDraggedElement, args.OffsetX, args.OffsetY);
    }

    /// <summary>The embedded viewer — the <see cref="GetViewport"/> source for the viewport-center insert.</summary>
    private Tempo.Blazor.Components.Diagram.TmDiagramEditor? _editor;

    /// <summary>
    /// The non-drag alternative (Fáze 6, WCAG 2.5.7): reuses <paramref name="element"/> as a new
    /// preview node at the CENTER OF THE VIEWPORT through the same path the drop uses (Fáze 6
    /// review fix: the embedded editor's <c>GetViewport()</c> reports the visible window in
    /// document space, so the insert lands where the user is looking — a fit view may have
    /// shrunk the visible rect away from the page center, and a node dropped at the page center
    /// could sit outside it). Falls back to the page center while no viewport is known (SSR,
    /// prerender, bUnit). The preview owns the node creation, the document mutation and the
    /// <see cref="OnNodeDropped"/> contract; the same clamp/collision logic places the node
    /// exactly as a real drop.
    /// </summary>
    public async Task InsertAtViewportCenterAsync(ModelingElementDto element)
    {
        if (!AllowNodeDrop || Document is null || element is null)
        {
            return;
        }

        Document.EnsurePages();
        var page = Document.ActivePage;
        var offsetX = page.Width / 2d;
        var offsetY = page.Height / 2d;
        var viewport = _editor?.GetViewport() ?? default;
        if (viewport.W > 0)
        {
            offsetX = viewport.X + viewport.W / 2;
            offsetY = viewport.Y + viewport.H / 2;
        }

        await InsertElementAsync(element, offsetX, offsetY);
    }

    /// <summary>The shared drop/insert core: create the node at the given point, mutate the document and raise the contract.</summary>
    private async Task InsertElementAsync(ModelingElementDto element, double offsetX, double offsetY)
    {
        Document.EnsurePages();
        var page = Document.ActivePage;
        EnsureDefaultLayer(page);

        var position = ResolveDropPosition(page, element, offsetX, offsetY);
        var node = CreateDroppedNode(page, element, position);
        page.Nodes.Add(node);
        Document.ModifiedAt = DateTime.UtcNow;

        await DocumentChanged.InvokeAsync(Document);
        await OnNodeDropped.InvokeAsync(new ModelingNodeDroppedEventArgs
        {
            Element = element,
            Point = new DiagramPoint(position.X, position.Y),
            NodeId = node.Id
        });
    }

    private static void EnsureDefaultLayer(DiagramPage page)
    {
        if (page.Layers.Count == 0)
        {
            page.Layers.Add(new DiagramLayer { Id = DefaultLayerId, Name = "Default" });
        }
    }

    private static (double X, double Y) ResolveDropPosition(DiagramPage page, ModelingElementDto element, double offsetX, double offsetY)
    {
        var template = FindExistingNodeForElement(page, element);
        var width = template?.W > 0 ? template.W : DefaultDroppedNodeWidth;
        var height = template?.H > 0 ? template.H : DefaultDroppedNodeHeight;
        var x = Math.Clamp(offsetX - width / 2, 0, Math.Max(0, page.Width - width));
        var y = Math.Clamp(offsetY - height / 2, 0, Math.Max(0, page.Height - height));

        for (var attempt = 0; attempt < 20 && IntersectsAnyNode(page, x, y, width, height); attempt++)
        {
            x += width + DropCollisionOffset;
            if (x + width > page.Width)
            {
                x = Math.Max(0, offsetX - width / 2);
                y += height + DropCollisionOffset;
            }
        }

        return (
            Math.Clamp(x, 0, Math.Max(0, page.Width - width)),
            Math.Clamp(y, 0, Math.Max(0, page.Height - height)));
    }

    private static DiagramNode CreateDroppedNode(DiagramPage page, ModelingElementDto element, (double X, double Y) position)
    {
        var template = FindExistingNodeForElement(page, element);
        var width = template?.W > 0 ? template.W : DefaultDroppedNodeWidth;
        var height = template?.H > 0 ? template.H : DefaultDroppedNodeHeight;
        var stencilId = !string.IsNullOrWhiteSpace(template?.StencilId)
            ? template.StencilId
            : TryGetStringProperty(element, "stencilId") ?? "general.rectangle";
        var name = string.IsNullOrWhiteSpace(element.Name) ? element.Id : element.Name;

        return new DiagramNode
        {
            Id = CreateDroppedNodeId(page, element.Id),
            StencilId = stencilId,
            X = position.X,
            Y = position.Y,
            W = width,
            H = height,
            ZIndex = page.Nodes.Count == 0 ? 0 : page.Nodes.Max(node => node.ZIndex) + 1,
            LayerId = page.Layers.FirstOrDefault()?.Id ?? DefaultLayerId,
            Data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["label"] = name,
                ["modelElementId"] = element.Id,
                ["sourceId"] = element.SourceId,
                ["sourceType"] = element.SourceType,
                ["sourcePath"] = element.SourcePath,
                ["notation"] = element.Notation,
                ["semanticType"] = element.SemanticType,
                ["droppedReuse"] = true
            }
        };
    }

    private static DiagramNode? FindExistingNodeForElement(DiagramPage page, ModelingElementDto element)
        => page.Nodes.FirstOrDefault(node =>
            node.Data.TryGetValue("modelElementId", out var modelElementId)
            && string.Equals(modelElementId?.ToString(), element.Id, StringComparison.Ordinal));

    private static bool IntersectsAnyNode(DiagramPage page, double x, double y, double width, double height)
        => page.Nodes.Any(node =>
            x < node.X + node.W
            && x + width > node.X
            && y < node.Y + node.H
            && y + height > node.Y);

    private static string CreateDroppedNodeId(DiagramPage page, string elementId)
    {
        var slug = new string((elementId.Length == 0 ? "element" : elementId)
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');

        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "element";
        }

        for (var index = 1; index < 10_000; index++)
        {
            var id = $"drop-{slug}-{index}";
            if (page.Nodes.All(node => !string.Equals(node.Id, id, StringComparison.Ordinal)))
            {
                return id;
            }
        }

        return $"drop-{Guid.NewGuid():N}";
    }

    private static string? TryGetStringProperty(ModelingElementDto element, string key)
    {
        if (!element.Properties.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }
}
