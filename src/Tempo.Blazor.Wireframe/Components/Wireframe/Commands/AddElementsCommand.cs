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
        foreach (var element in _elements)
        {
            if (!_doc.Elements.Any(e => e.Id == element.Id))
                _doc.Elements.Add(element);
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        var ids = _elements.Select(e => e.Id).ToHashSet();
        _doc.Elements.RemoveAll(e => ids.Contains(e.Id));
    }
}
