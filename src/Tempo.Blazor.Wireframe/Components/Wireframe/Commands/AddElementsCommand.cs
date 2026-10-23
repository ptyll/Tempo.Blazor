using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe.Commands;

/// <summary>
/// Adds several elements to the document as ONE undoable step (duplicate selection). <see cref="AddElementCommand"/>
/// covers the single-element case.
/// </summary>
public sealed class AddElementsCommand : IWireframeCommand
{
    private readonly WireframeDocument _doc;
    private readonly IReadOnlyList<WireframeElement> _elements;

    // Ids this command actually inserted in Execute(): an element that was already in the document (same id) is not ours, so Undo must
    // not remove it.
    private readonly HashSet<string> _added = [];

    /// <param name="doc">Target document.</param>
    /// <param name="elements">The elements to add (already carrying their final ids, z-index and layer).</param>
    public AddElementsCommand(WireframeDocument doc, IEnumerable<WireframeElement> elements)
    {
        _doc = doc;
        _elements = elements.ToList();
    }

    /// <inheritdoc />
    public string Name => _elements.Count == 1 ? $"Add {_elements[0].Type}" : $"Add {_elements.Count} elements";

    /// <inheritdoc />
    public void Execute()
    {
        _added.Clear();
        foreach (var element in _elements)
        {
            if (_doc.Elements.Any(e => e.Id == element.Id)) continue;
            _doc.Elements.Add(element);
            _added.Add(element.Id);
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        _doc.Elements.RemoveAll(e => _added.Contains(e.Id));
    }
}
