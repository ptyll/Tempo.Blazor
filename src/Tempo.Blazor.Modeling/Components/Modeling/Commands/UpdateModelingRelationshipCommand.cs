using System.Text.Json;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// One inspector field commit for a RELATIONSHIP: updates the <see cref="ModelingRelationshipDto"/>
/// in place and mirrors the name onto the diagram edge whose Id matches the relationship id
/// (<c>Label</c>). Pairs with <see cref="UpdateModelingElementCommand"/> under the same one
/// transaction per commit rule.
/// </summary>
public sealed class UpdateModelingRelationshipCommand : IDiagramCommand
{
    private readonly ModelingRelationshipDto _relationship;
    private readonly string? _name;
    private readonly Dictionary<string, JsonElement>? _setProperties;
    private readonly List<string>? _removedProperties;
    private readonly IReadOnlyList<DiagramDocument> _documents;

    private string? _originalName;
    private Dictionary<string, JsonElement>? _originalProperties;
    private bool _originalsCaptured;

    /// <summary>Creates the command for one relationship field commit.</summary>
    /// <param name="transactionName">Localized undo/redo name of the owning transaction.</param>
    /// <param name="relationship">The model relationship to mutate in place.</param>
    /// <param name="name">New name, or null when the name did not change.</param>
    /// <param name="setProperties">Property values to set (JSON kind preserved), or null.</param>
    /// <param name="removedProperties">Property keys to remove, or null.</param>
    /// <param name="documents">Diagram documents whose edges are synced by relationship id.</param>
    public UpdateModelingRelationshipCommand(
        string transactionName,
        ModelingRelationshipDto relationship,
        string? name,
        Dictionary<string, JsonElement>? setProperties,
        List<string>? removedProperties,
        IEnumerable<DiagramDocument> documents)
    {
        Name = transactionName;
        _relationship = relationship ?? throw new ArgumentNullException(nameof(relationship));
        _name = name;
        _setProperties = setProperties;
        _removedProperties = removedProperties;
        _documents = documents?.Where(document => document is not null).Distinct().ToList() ?? [];
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Identifier of the mutated relationship (test/inspection seam).</summary>
    public string RelationshipId => _relationship.Id;

    /// <inheritdoc />
    public void Execute()
    {
        if (!_originalsCaptured)
        {
            _originalName = _relationship.Name;
            _originalProperties = new Dictionary<string, JsonElement>(_relationship.Properties, StringComparer.Ordinal);
            _originalsCaptured = true;
        }

        if (_name is not null)
        {
            _relationship.Name = _name;
        }

        if (_setProperties is not null)
        {
            foreach (var property in _setProperties)
            {
                _relationship.Properties[property.Key] = property.Value.Clone();
            }
        }

        if (_removedProperties is not null)
        {
            foreach (var key in _removedProperties)
            {
                _relationship.Properties.Remove(key);
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
            _relationship.Name = _originalName;
        }

        if (_setProperties is not null || _removedProperties is not null)
        {
            _relationship.Properties = new Dictionary<string, JsonElement>(_originalProperties ?? [], StringComparer.Ordinal);
        }

        SynchronizeDocuments();
    }

    private void SynchronizeDocuments()
    {
        foreach (var document in _documents)
        {
            foreach (var edge in document.Edges)
            {
                if (string.Equals(edge.Id, _relationship.Id, StringComparison.Ordinal))
                {
                    edge.Label = string.IsNullOrWhiteSpace(_relationship.Name) ? null : _relationship.Name;
                }
            }
        }
    }
}
