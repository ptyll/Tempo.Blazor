using Tempo.Blazor.DocumentEditor.Models;

namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>
/// The content control under the caret as the field popover edits it: what the canvas engine reports (kind, title, flags, value)
/// without the document model. The editor maps the engine payload into it; a host or a test builds one directly.
/// </summary>
public sealed class DocumentFieldPopoverControl
{
    /// <summary>Stable content control identifier.</summary>
    public string ControlId { get; init; } = string.Empty;

    /// <summary>Control kind. Date, ComboBox, DropDown and Picture have an editor; every other kind shows its metadata only.</summary>
    public DocumentContentControlKind Kind { get; init; }

    /// <summary>Heading of the popover: the alias, falling back to the tag, then the control id.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Whether a value is required.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Whether the content is locked against editing (every editor is disabled).</summary>
    public bool LockContent { get; init; }

    /// <summary>Free-text value (combo box).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Selected choice value (drop-down, combo box).</summary>
    public string SelectedValue { get; init; } = string.Empty;

    /// <summary>ISO date value (date control).</summary>
    public string DateIso { get; init; } = string.Empty;

    /// <summary>Selected image asset id (picture control).</summary>
    public string AssetId { get; init; } = string.Empty;

    /// <summary>The choices of a drop-down or combo box.</summary>
    public IReadOnlyList<DocumentContentControlItem> Items { get; init; } = [];

    /// <summary>Whether the engine opens the popover for this kind (the four kinds that have an editor).</summary>
    public static bool IsPopoverKind(DocumentContentControlKind kind)
        => kind is DocumentContentControlKind.Date
            or DocumentContentControlKind.ComboBox
            or DocumentContentControlKind.DropDown
            or DocumentContentControlKind.Picture;

    /// <summary>
    /// Maps the engine-computed selection payload. Returns null for a kind that does not open the popover (or an unknown kind from a
    /// newer engine), mirroring the old document walk.
    /// </summary>
    public static DocumentFieldPopoverControl? From(TmDocumentCanvasEngineHost.CanvasEngineContentControlState? state)
    {
        if (state is null
            || !Enum.TryParse<DocumentContentControlKind>(state.Kind, ignoreCase: true, out var kind)
            || !IsPopoverKind(kind))
        {
            return null;
        }

        return new DocumentFieldPopoverControl
        {
            ControlId = state.ControlId,
            Kind = kind,
            Title = FirstNonEmpty(state.Title, state.ControlId),
            IsRequired = state.IsRequired,
            LockContent = state.LockContent,
            Text = state.Text,
            SelectedValue = state.SelectedValue,
            DateIso = FirstNonEmpty(state.DateIso, state.Text),
            AssetId = state.AssetId,
            Items = state.Items,
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
