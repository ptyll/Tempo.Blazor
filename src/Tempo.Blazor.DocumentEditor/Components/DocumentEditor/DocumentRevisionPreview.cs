using System.Text.Json;
using Tempo.Blazor.DocumentEditor.Models;

namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>
/// What a revision card shows of a tracked change (redesign 146c7e1b, phase 6, DE-2.4): the affected text and, for a change that is no
/// text, a one-line human description ("Bold: on"). A host or the editor supplies it through the <c>PreviewResolver</c> of
/// <see cref="TmDocumentRevisionPanel"/>; it never carries the raw payload of the engine. Lives in the DocumentEditor package, not in
/// Abstractions - it is a presentation record, not part of the document model.
/// </summary>
public sealed class DocumentRevisionPreview
{
    /// <summary>A preview with nothing to show; the card then falls back to its localized "details in the document" / "no preview" line.</summary>
    public static DocumentRevisionPreview Empty { get; } = new();

    /// <summary>The text the revision inserted, deleted or touches. Rendered in an <c>ins</c> / <c>del</c> by the revision type.</summary>
    public string? Text { get; init; }

    /// <summary>A human description of a change that is not text (a formatting change reads "Bold: on"). Plain text, already localized.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the preview carries anything to show.</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Text) || !string.IsNullOrWhiteSpace(Description);

    /// <summary>
    /// Reads the mark and the new state of a formatting revision out of its payload. The engine writes the payload camel-cased with the mark
    /// as a name, the C# side Pascal-cased - both are accepted, as is a numeric mark; a payload that is none of these reads as <c>null</c>.
    /// </summary>
    public static (InlineMarkType MarkType, bool NewActive)? ReadFormatting(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            InlineMarkType? mark = null;
            var active = true;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "markType", StringComparison.OrdinalIgnoreCase))
                {
                    mark = property.Value.ValueKind switch
                    {
                        JsonValueKind.String when Enum.TryParse<InlineMarkType>(property.Value.GetString(), ignoreCase: true, out var named) => named,
                        JsonValueKind.Number when property.Value.TryGetInt32(out var number) && Enum.IsDefined(typeof(InlineMarkType), number) => (InlineMarkType)number,
                        _ => null
                    };
                }
                else if (string.Equals(property.Name, "newActive", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    active = property.Value.GetBoolean();
                }
            }

            return mark is { } known ? (known, active) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
