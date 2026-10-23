using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe.Commands;

/// <summary>
/// Renames a single element (sets <see cref="WireframeElement.Name"/>). A null or whitespace new name clears the name.
/// The element is looked up on the page that was active when the command was created, so Undo / Redo still reach it
/// after the user switched to another page.
/// </summary>
public sealed class RenameElementCommand : IWireframeCommand
{
    private readonly WireframeDocument _doc;
    private readonly string? _pageId;
    private readonly string _elementId;
    private readonly string? _oldName;
    private readonly string? _newName;

    /// <summary>Creates a rename command.</summary>
    /// <param name="doc">Target document (the element is looked up on the page that is active now).</param>
    /// <param name="elementId">Id of the element to rename.</param>
    /// <param name="oldName">Name before the change (restored by Undo).</param>
    /// <param name="newName">Name after the change; null/whitespace clears the name.</param>
    public RenameElementCommand(WireframeDocument doc, string elementId, string? oldName, string? newName)
    {
        _doc = doc;
        _pageId = doc.ActivePage?.Id;
        _elementId = elementId;
        _oldName = oldName;
        _newName = string.IsNullOrWhiteSpace(newName) ? null : newName;
    }

    /// <inheritdoc />
    public string Name => "Rename element";

    /// <inheritdoc />
    public void Execute() => Apply(_newName);

    /// <inheritdoc />
    public void Undo() => Apply(_oldName);

    private void Apply(string? name)
    {
        var page = _pageId is null ? _doc.ActivePage : _doc.Pages.FirstOrDefault(p => p.Id == _pageId) ?? _doc.ActivePage;
        var el = page?.Elements.FirstOrDefault(e => e.Id == _elementId);
        if (el is not null)
            el.Name = name;
    }
}
