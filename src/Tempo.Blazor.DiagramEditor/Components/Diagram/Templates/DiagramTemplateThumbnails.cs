using System.Text.Json;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Serialization;
using Tempo.Blazor.Components.Diagram.Services;

namespace Tempo.Blazor.Components.Diagram.Templates;

/// <summary>Shared thumbnail builder for the template providers (one code path).</summary>
internal static class DiagramTemplateThumbnails
{
    /// <summary>
    /// data: URI of a lightweight SVG preview of <paramref name="documentJson"/>; null when it
    /// cannot be built or the document has no nodes (the card then renders its category icon
    /// placeholder). The preview draws no labels (illegible at card size) on a transparent
    /// background (the card provides the surface), fitted to the node bounds in a 16:10 viewport.
    /// </summary>
    public static string? FromDocumentJson(string? documentJson)
    {
        DiagramDocument? doc = null;
        if (!string.IsNullOrWhiteSpace(documentJson))
        {
            try
            {
                DiagramSerializer.TryDeserialize(documentJson, out doc);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        doc ??= new DiagramDocument();
        doc.EnsurePages();
        if (doc.ActivePage.Nodes.Count == 0)
            return null;

        var svg = DiagramThumbnailSvgGenerator.Generate(doc, new DiagramThumbnailOptions
        {
            IncludeLabels = false,
            OpaqueBackground = false,
            Width = 320,
            Height = 200
        });
        return "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
    }
}
