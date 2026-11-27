namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>The tabs of the editor's image panel: one panel replaces the ribbon's image menu and the old URL dialog.</summary>
public enum DocumentImagePanelTab
{
    /// <summary>Insert an image from a URL (the default tab).</summary>
    Url,

    /// <summary>Upload a file through the host's image provider (offered only with a provider).</summary>
    Upload,

    /// <summary>Pick one of the host's image assets (offered only when the host supplies assets).</summary>
    Library,
}
