using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>The words of a document version, shared by the version panel and the preview strip (redesign 146c7e1b, phase 6).</summary>
internal static class DocumentVersionText
{
    /// <summary>The localized kind: "Minor version", "Major version", "Autosave", "Restore point".</summary>
    public static string KindLabel(ITmLocalizer loc, DocumentVersionKind kind)
        => kind switch
        {
            DocumentVersionKind.Major => loc["TmDocumentEditor_VersionKindMajor"],
            DocumentVersionKind.Autosave => loc["TmDocumentEditor_VersionKindAutosave"],
            DocumentVersionKind.Restore => loc["TmDocumentEditor_VersionKindRestore"],
            _ => loc["TmDocumentEditor_VersionKindMinor"]
        };

    /// <summary>The label of the version, or its localized kind when it has none.</summary>
    public static string Title(ITmLocalizer loc, DocumentVersion version)
        => !string.IsNullOrWhiteSpace(version.Label)
            ? version.Label
            : KindLabel(loc, version.Kind);
}
