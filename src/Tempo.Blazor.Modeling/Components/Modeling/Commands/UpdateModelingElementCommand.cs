using System.Text.Json;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// One inspector field commit applied to the model DTO and synchronized into the diagram
/// documents (Fáze 14, decision <c>MODELING-INSPECTOR-EDITING</c>): the command updates the
/// <see cref="ModelingElementDto"/> IN PLACE and mirrors the change onto the diagram nodes that
/// carry <c>modelElementId</c> — <c>Data["name"]</c> / <c>Data["label"]</c> — so the model tree,
/// the preview canvas and the open-editor dialog stay consistent. The editor pushes each commit
/// through a single <see cref="DiagramCommandTransaction"/> ("Upravit prvek") containing this
/// command, so one undo/redo step reverts the model AND the preview together.
/// <para>
/// The command is created by the editor with the LOCALIZED transaction name (the undo/redo
/// tooltips are user-facing text) and with a DOCUMENT RESOLVER — evaluated on every
/// Execute/Undo, so the sync targets the documents that exist at that moment: the editor may
/// regenerate the preview (or adopt a saved model) between the push and the undo, and a
/// snapshot list would then sync a dead document. The pre-change values are captured on the
/// FIRST execute — a redo then re-applies the captured delta instead of re-capturing the
/// already-applied state.
/// </para>
/// </summary>
public sealed class UpdateModelingElementCommand : IDiagramCommand
{
    private readonly ModelingElementDto _element;
    private readonly string? _name;
    private readonly string? _description;
    private readonly Dictionary<string, JsonElement>? _setProperties;
    private readonly List<string>? _removedProperties;
    private readonly Func<IEnumerable<DiagramDocument>>? _documents;

    private string? _originalName;
    private string? _originalDescription;
    private Dictionary<string, JsonElement>? _originalProperties;
    private bool _originalsCaptured;

    /// <summary>Creates the command for one field commit. Only non-null deltas are applied.</summary>
    /// <param name="transactionName">Localized undo/redo name of the owning transaction.</param>
    /// <param name="element">The model element to mutate in place.</param>
    /// <param name="name">New name, or null when the name did not change.</param>
    /// <param name="description">New description, or null when unchanged.</param>
    /// <param name="setProperties">Property values to set (JSON kind preserved), or null.</param>
    /// <param name="removedProperties">Property keys to remove, or null.</param>
    /// <param name="documents">Resolver of the diagram documents whose <c>modelElementId</c>
    /// nodes are synced — evaluated on every Execute/Undo.</param>
    public UpdateModelingElementCommand(
        string transactionName,
        ModelingElementDto element,
        string? name,
        string? description,
        Dictionary<string, JsonElement>? setProperties,
        List<string>? removedProperties,
        Func<IEnumerable<DiagramDocument>>? documents)
    {
        Name = transactionName;
        _element = element ?? throw new ArgumentNullException(nameof(element));
        _name = name;
        _description = description;
        _setProperties = setProperties;
        _removedProperties = removedProperties;
        _documents = documents;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Identifier of the mutated element (test/inspection seam).</summary>
    public string ElementId => _element.Id;

    /// <inheritdoc />
    public void Execute()
    {
        if (!_originalsCaptured)
        {
            _originalName = _element.Name;
            _originalDescription = _element.Description;
            _originalProperties = new Dictionary<string, JsonElement>(_element.Properties, StringComparer.Ordinal);
            _originalsCaptured = true;
        }

        if (_name is not null)
        {
            _element.Name = _name;
        }

        if (_description is not null)
        {
            _element.Description = _description;
        }

        if (_setProperties is not null)
        {
            foreach (var property in _setProperties)
            {
                _element.Properties[property.Key] = property.Value.Clone();
            }
        }

        if (_removedProperties is not null)
        {
            foreach (var key in _removedProperties)
            {
                _element.Properties.Remove(key);
            }
        }

        SynchronizeDocuments();
    }

    /// <inheritdoc />
    public void Undo()
    {
        if (!_originalsCaptured)
        {
            return;
        }

        if (_name is not null)
        {
            _element.Name = _originalName;
        }

        if (_description is not null)
        {
            _element.Description = _originalDescription;
        }

        if (_setProperties is not null || _removedProperties is not null)
        {
            _element.Properties = new Dictionary<string, JsonElement>(_originalProperties ?? [], StringComparer.Ordinal);
        }

        SynchronizeDocuments();
    }

    private void SynchronizeDocuments()
    {
        if (_documents is null)
        {
            return;
        }

        var label = _element.Name;
        foreach (var document in _documents.Invoke().Where(document => document is not null).Distinct())
        {
            foreach (var node in document.Nodes)
            {
                if (node.Data is not null
                    && node.Data.TryGetValue("modelElementId", out var value)
                    && value is string elementId
                    && string.Equals(elementId, _element.Id, StringComparison.Ordinal))
                {
                    node.Data["name"] = label;
                    node.Data["label"] = label;
                }
            }
        }
    }
}
