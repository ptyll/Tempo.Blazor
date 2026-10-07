namespace Tempo.Blazor.Components.Feedback;

/// <summary>Size variants for the modal dialog.</summary>
public enum ModalSize
{
    /// <summary>Small modal width (e.g., 400px).</summary>
    Small,

    /// <summary>Medium modal width (default, e.g., 600px).</summary>
    Medium,

    /// <summary>Large modal width (e.g., 800px).</summary>
    Large,

    /// <summary>Extra large modal width (e.g., 1000px).</summary>
    XLarge,

    /// <summary>Fullscreen modal covering the entire viewport.</summary>
    Fullscreen
}

/// <summary>Position of the modal on the screen.</summary>
public enum ModalPosition
{
    /// <summary>Centered vertically and horizontally (default).</summary>
    Center,

    /// <summary>Positioned at the top of the screen.</summary>
    Top,

    /// <summary>Positioned at the bottom of the screen.</summary>
    Bottom
}

/// <summary>Type of dialog to display.</summary>
public enum DialogType
{
    /// <summary>Simple alert with a message and OK button.</summary>
    Alert,

    /// <summary>Confirmation dialog with OK and Cancel buttons.</summary>
    Confirm,

    /// <summary>Prompt dialog with input field and OK/Cancel buttons.</summary>
    Prompt,

    /// <summary>Custom dialog with fully customizable content.</summary>
    Custom
}

/// <summary>
/// How a viewport-positioned overlay presents itself on a narrow viewport. A forced value renders
/// that presentation regardless of the viewport; <see cref="Auto"/> follows the viewport scope.
/// </summary>
public enum MobilePresentation
{
    /// <summary>The centered dialog, at every viewport width.</summary>
    Dialog,

    /// <summary>A bottom sheet anchored to the viewport's bottom edge.</summary>
    Sheet,

    /// <summary>A panel that fills the viewport.</summary>
    Fullscreen,

    /// <summary>
    /// Follows the viewport scope: a sheet on mobile, a dialog otherwise. With no viewport scope it
    /// renders the component's initial mode.
    /// </summary>
    Auto
}

/// <summary>How the footer buttons of a modal or dialog are arranged.</summary>
public enum FooterLayout
{
    /// <summary>Buttons sit side by side, at every width. The default.</summary>
    Inline,

    /// <summary>Buttons stack vertically, full width, with the confirm action at the bottom.</summary>
    Stacked
}

/// <summary>Where a dialog places its icon relative to its content.</summary>
public enum DialogLayout
{
    /// <summary>The icon sits above the title, centered. The default.</summary>
    Centered,

    /// <summary>The icon sits beside the content, in a row.</summary>
    Inline
}

/// <summary>Visual variant for the dialog indicating severity or purpose.</summary>
public enum DialogVariant
{
    /// <summary>Informational dialog (blue).</summary>
    Info,

    /// <summary>Success dialog (green).</summary>
    Success,

    /// <summary>Warning dialog (yellow/amber).</summary>
    Warning,

    /// <summary>Error dialog (red).</summary>
    Error
}
