using System.Text.Json.Serialization;
using Tempo.Blazor.DocumentEditor.Models;

namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>
/// Source-generated serializer metadata for the canvas engine interop boundary (perf plan N10.3):
/// the <see cref="CanvasDocumentModel"/> mounted into the JS engine and the CanvasEngine*State
/// payloads read back from it. Wired into <c>TmDocumentCanvasEngineHost.CanvasJsonOptions</c> as the
/// first link of the resolver chain; reflection stays chained behind it for anonymous option
/// payloads and the private clipboard debug state. Enum values keep their camelCase string wire
/// format because the runtime <see cref="JsonStringEnumConverter"/> registered on the options takes
/// precedence over generated metadata in metadata-based mode.
/// </summary>
// GenerationMode = Metadata (no fast-path): keeps nested type lookups on the runtime options so the
// runtime enum converter and the chained reflection fallback (object-typed members, JsonElement
// payloads) always apply. See DocumentEditorJsonContext for the failure mode fast-path causes.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CanvasDocumentModel))]
[JsonSerializable(typeof(DocumentOperationBatch))]
[JsonSerializable(typeof(List<DocumentOperationBatch>))]
[JsonSerializable(typeof(DocumentEditorAuthor))]
[JsonSerializable(typeof(DocumentSigningFieldDescriptor))]
[JsonSerializable(typeof(List<DocumentSigningFieldDescriptor>))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineFormattingState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEnginePrintPreviewState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEnginePrintDialogState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineImageState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineContextMenuRequest))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineMisspelling))]
[JsonSerializable(typeof(List<TmDocumentCanvasEngineHost.CanvasEngineMisspelling>))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineCommandResult))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineClipboardResult))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineCommandState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineAnnotationSelection))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineUndoState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineSearchState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineSearchMatch))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineNavigationState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineBookmarkState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineSelectionState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineContentControlState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineSigningFieldSelection))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineViewportRect))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineDiagnosticsState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineRemoteApplyResult))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineRemoteConflict))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEnginePresenceState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEnginePresenceCursor))]
[JsonSerializable(typeof(List<TmDocumentCanvasEngineHost.CanvasEnginePresenceCursor>))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineChangedState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineUiState))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineUiStateEnvelope))]
[JsonSerializable(typeof(TmDocumentCanvasEngineHost.CanvasEngineAnnotations))]
internal sealed partial class CanvasEngineJsonContext : JsonSerializerContext
{
}
