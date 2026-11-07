using System.Text.Json;
using System.Text.Json.Serialization;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Modeling;

/// <summary>Request passed to a modeling model provider.</summary>
public sealed class ModelingModelRequest
{
    private string _providerKey = string.Empty;
    private string _sourceKind = string.Empty;
    private string _sourceId = string.Empty;
    private string _notation = string.Empty;
    private string _viewpointKey = string.Empty;
    private Dictionary<string, JsonElement> _filterOptions = [];
    private string _culture = string.Empty;

    /// <summary>Provider key used to select a modeling provider.</summary>
    public string ProviderKey
    {
        get => _providerKey;
        set => _providerKey = value ?? string.Empty;
    }

    /// <summary>Kind of source requested from the provider.</summary>
    public string SourceKind
    {
        get => _sourceKind;
        set => _sourceKind = value ?? string.Empty;
    }

    /// <summary>Source identifier requested from the provider.</summary>
    public string SourceId
    {
        get => _sourceId;
        set => _sourceId = value ?? string.Empty;
    }

    /// <summary>Requested modeling notation key.</summary>
    public string Notation
    {
        get => _notation;
        set => _notation = value ?? string.Empty;
    }

    /// <summary>Requested viewpoint key.</summary>
    public string ViewpointKey
    {
        get => _viewpointKey;
        set => _viewpointKey = value ?? string.Empty;
    }

    /// <summary>Provider-specific filter options preserved as JSON values.</summary>
    public Dictionary<string, JsonElement> FilterOptions
    {
        get => _filterOptions;
        set => _filterOptions = value ?? [];
    }

    /// <summary>Requested UI or data culture name.</summary>
    public string Culture
    {
        get => _culture;
        set => _culture = value ?? string.Empty;
    }
}

/// <summary>Provides source-backed modeling models.</summary>
public interface IModelingModelProvider
{
    /// <summary>Unique provider key used by the modeling editor.</summary>
    string ProviderKey { get; }

    /// <summary>Loads a modeling model for the supplied request.</summary>
    /// <param name="request">Model request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded modeling model.</returns>
    Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken);
}

/// <summary>Options used when generating a diagram document from a modeling model.</summary>
public sealed class ModelingDiagramGenerationOptionsDto
{
    private string _viewId = string.Empty;
    private string _viewpointKey = string.Empty;
    private string _layoutHint = string.Empty;

    /// <summary>Requested modeling view identifier.</summary>
    public string ViewId
    {
        get => _viewId;
        set => _viewId = value ?? string.Empty;
    }

    /// <summary>Requested viewpoint key for generated views.</summary>
    public string ViewpointKey
    {
        get => _viewpointKey;
        set => _viewpointKey = value ?? string.Empty;
    }

    /// <summary>Whether generated modeling issues should be included in the result.</summary>
    public bool IncludeIssues { get; set; } = true;

    /// <summary>Layout hint such as grid, tree, TB, or LR.</summary>
    public string LayoutHint
    {
        get => _layoutHint;
        set => _layoutHint = value ?? string.Empty;
    }

    /// <summary>
    /// Whether an element WITHOUT a stored view node is AUTO-PLACED instead of dropped when a
    /// curated view is selected. Default <c>false</c> — the 2.10.0 contract: with a view selected, a
    /// view node is a visibility gate, so every existing consumer (the MCP diagram tool included)
    /// keeps byte-identical output. An AUTHORING host (the modeling editor) opts in, because there a
    /// missing view node only means "no position yet" and dropping the element would hide a newly
    /// inserted one until it was positioned (phase 4, docs/modeling-authoring.md §1.4/§9.4).
    /// </summary>
    public bool AutoPlaceElementsWithoutViewNode { get; set; }
}

/// <summary>Result of generating a diagram document from a modeling model.</summary>
public sealed class ModelingDiagramGenerationResultDto
{
    private List<ModelingIssueDto> _issues = [];

    /// <summary>Generated diagram document. May be null when generation failed before document creation.</summary>
    public DiagramDocument? Document { get; set; }

    /// <summary>Issues discovered during generation.</summary>
    public List<ModelingIssueDto> Issues
    {
        get => _issues;
        set => _issues = value ?? [];
    }

    /// <summary>Timestamp when the result was generated.</summary>
    public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>
/// Writes edited modeling model data back to the source that <see cref="IModelingModelProvider"/>
/// loaded it from (Fáze 14, decision <c>MODELING-INSPECTOR-EDITING</c>; v2 structure + layout in
/// decision <c>MODELING-AUTHORING-CHANGESET</c>).
/// <para>
/// The provider contract stays READ-ONLY: a host that wants an editable inspector registers a
/// separate writer resolved by <see cref="ProviderKey"/>. The editing feature in
/// <c>TmModelingEditor</c> is active only when a writer is registered for the effective provider
/// key (or an <c>OnModelChanged</c> delegate is bound).
/// </para>
/// </summary>
public interface IModelingModelWriter
{
    /// <summary>Provider key this writer persists changes for. Must match the
    /// <see cref="IModelingModelProvider.ProviderKey"/> of the provider that loads the model.</summary>
    string ProviderKey { get; }

    /// <summary>
    /// Capabilities advertised to the editor (decision <c>MODELING-WRITER-CAPABILITIES</c>).
    /// <para>
    /// A <b>default interface member</b>, so a 2.10.0 writer — which knows only the content fields
    /// of an existing item — keeps compiling and behaves exactly as before without a source change.
    /// The editor hides every structurally impossible action instead of letting a host discover the
    /// limitation in its own writer.
    /// </para>
    /// </summary>
    ModelingModelWriterCapabilities Capabilities => ModelingModelWriterCapabilities.Content;

    /// <summary>Persists the supplied change set against the source system.
    /// <paramref name="changes"/> carries its <see cref="ModelingModelChangeSet.BaseVersion"/>
    /// — the <see cref="ModelingMetadataDto.SourceVersion"/> the edited model was loaded with —
    /// and the writer compares it against the current source version, returning
    /// <see cref="ModelingModelSaveResult.IsConflict"/> when the source moved on.</summary>
    /// <param name="changes">The aggregated changes to persist (only changed fields are set).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The save outcome; a failed save carries a message, a conflict also carries the
    /// <c>IsConflict</c> flag and may carry the freshly loaded model.</returns>
    Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken);
}

/// <summary>
/// What an <see cref="IModelingModelWriter"/> can persist (decision
/// <c>MODELING-WRITER-CAPABILITIES</c>). Additive: an existing writer that does not override
/// <see cref="IModelingModelWriter.Capabilities"/> is treated as <see cref="Content"/> only.
/// </summary>
[Flags]
public enum ModelingModelWriterCapabilities
{
    /// <summary>The writer cannot persist model edits through this contract.</summary>
    None = 0,

    /// <summary>Rename, description and property values of existing items (2.10.0 behavior).</summary>
    Content = 1,

    /// <summary>Added/removed elements and relationships, parent (pool/lane) changes.</summary>
    Structure = 2,

    /// <summary>Persisted layout — view node geometry, connection waypoints, view nodes.</summary>
    Layout = 4,

    /// <summary>Content | Structure | Layout.</summary>
    All = Content | Structure | Layout
}

/// <summary>An aggregated change set passed to <see cref="IModelingModelWriter.SaveChangesAsync"/>.</summary>
public sealed class ModelingModelChangeSet
{
    private string _modelId = string.Empty;
    private string _providerKey = string.Empty;
    private string _baseVersion = string.Empty;
    private List<ModelingElementChange> _elements = [];
    private List<ModelingRelationshipChange> _relationships = [];
    private List<ModelingElementAdd> _addedElements = [];
    private List<ModelingRelationshipAdd> _addedRelationships = [];
    private List<string> _removedElementIds = [];
    private List<string> _removedRelationshipIds = [];
    private List<ModelingViewChange> _viewChanges = [];

    /// <summary>Identifier of the edited <see cref="ModelingModelDto.Id"/>.</summary>
    public string ModelId
    {
        get => _modelId;
        set => _modelId = value ?? string.Empty;
    }

    /// <summary>Provider key of the writer the set is addressed to.</summary>
    public string ProviderKey
    {
        get => _providerKey;
        set => _providerKey = value ?? string.Empty;
    }

    /// <summary>The source version the edited model was loaded with
    /// (<see cref="ModelingMetadataDto.SourceVersion"/>).</summary>
    public string BaseVersion
    {
        get => _baseVersion;
        set => _baseVersion = value ?? string.Empty;
    }

    /// <summary>Changed elements. Only fields the user actually changed are set.</summary>
    public List<ModelingElementChange> Elements
    {
        get => _elements;
        set => _elements = value ?? [];
    }

    /// <summary>Changed relationships. Only fields the user actually changed are set.</summary>
    public List<ModelingRelationshipChange> Relationships
    {
        get => _relationships;
        set => _relationships = value ?? [];
    }

    /// <summary>
    /// NEW semantic elements, in list order (a parent must precede its children). Additive in the
    /// authoring contract; empty for a 2.10.0 content-only edit.
    /// </summary>
    public List<ModelingElementAdd> AddedElements
    {
        get => _addedElements;
        set => _addedElements = value ?? [];
    }

    /// <summary>NEW semantic relationships. Endpoints may be existing or added elements.</summary>
    public List<ModelingRelationshipAdd> AddedRelationships
    {
        get => _addedRelationships;
        set => _addedRelationships = value ?? [];
    }

    /// <summary>
    /// Elements the host must delete. The editor expands a cascade and lists the descendants FIRST
    /// (decision Q3) — writers never cascade on their own.
    /// </summary>
    public List<string> RemovedElementIds
    {
        get => _removedElementIds;
        set => _removedElementIds = value ?? [];
    }

    /// <summary>Relationships the host must delete (the editor lists them before the elements).</summary>
    public List<string> RemovedRelationshipIds
    {
        get => _removedRelationshipIds;
        set => _removedRelationshipIds = value ?? [];
    }

    /// <summary>Layout changes per view (<see cref="ModelingViewDto"/>). Never semantic content.</summary>
    public List<ModelingViewChange> ViewChanges
    {
        get => _viewChanges;
        set => _viewChanges = value ?? [];
    }

    /// <summary>
    /// Whether the batch changes the model STRUCTURE (an added/removed element or relationship, a
    /// <c>ParentId</c> move or a relationship re-target). Computed, never serialized state —
    /// System.Text.Json serializes get-only properties, so the flag must be ignored explicitly.
    /// </summary>
    [JsonIgnore]
    public bool HasStructuralChanges => AddedElements.Count > 0 || AddedRelationships.Count > 0
        || RemovedElementIds.Count > 0 || RemovedRelationshipIds.Count > 0
        || Elements.Any(change => change.ParentId is not null)
        || Relationships.Any(change => change.SourceElementId is not null || change.TargetElementId is not null);

    /// <summary>Whether the batch carries persisted-layout changes. Computed, never serialized.</summary>
    [JsonIgnore]
    public bool HasViewChanges => ViewChanges.Count > 0;
}

/// <summary>
/// A NEW semantic element (decision <c>MODELING-AUTHORING-CHANGESET</c>). The editor proposes a
/// stable client id — the same id its commands and its pending diff use — so undo/redo and the
/// diagram commands already reference it; a writer may keep it or rewrite it and return the saved
/// model.
/// </summary>
public sealed class ModelingElementAdd
{
    private string _id = string.Empty;
    private string _semanticType = string.Empty;
    private string _name = string.Empty;
    private string _notation = string.Empty;
    private string _description = string.Empty;
    private string _parentId = string.Empty;
    private Dictionary<string, JsonElement> _properties = [];

    /// <summary>Client-proposed unique identifier (required; unique inside the model).</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Semantic element type within the notation (required).</summary>
    public string SemanticType
    {
        get => _semanticType;
        set => _semanticType = value ?? string.Empty;
    }

    /// <summary>Display name (required).</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>Notation key; empty means the model's notation.</summary>
    public string Notation
    {
        get => _notation;
        set => _notation = value ?? string.Empty;
    }

    /// <summary>Optional description.</summary>
    public string Description
    {
        get => _description;
        set => _description = value ?? string.Empty;
    }

    /// <summary>Parent element id for pool/lane/subprocess containment; empty means a root element.</summary>
    public string ParentId
    {
        get => _parentId;
        set => _parentId = value ?? string.Empty;
    }

    /// <summary>Initial property values, preserving the JSON value kind of each entry.</summary>
    public Dictionary<string, JsonElement> Properties
    {
        get => _properties;
        set => _properties = value ?? [];
    }
}

/// <summary>A NEW semantic relationship between two elements that exist after this batch.</summary>
public sealed class ModelingRelationshipAdd
{
    private string _id = string.Empty;
    private string _relationshipType = string.Empty;
    private string _sourceElementId = string.Empty;
    private string _targetElementId = string.Empty;
    private string _name = string.Empty;
    private Dictionary<string, JsonElement> _properties = [];

    /// <summary>Client-proposed unique identifier (required; unique inside the model).</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Semantic relationship type within the notation (required).</summary>
    public string RelationshipType
    {
        get => _relationshipType;
        set => _relationshipType = value ?? string.Empty;
    }

    /// <summary>Source element id (required).</summary>
    public string SourceElementId
    {
        get => _sourceElementId;
        set => _sourceElementId = value ?? string.Empty;
    }

    /// <summary>Target element id (required).</summary>
    public string TargetElementId
    {
        get => _targetElementId;
        set => _targetElementId = value ?? string.Empty;
    }

    /// <summary>Optional display name.</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>Initial property values, preserving the JSON value kind of each entry.</summary>
    public Dictionary<string, JsonElement> Properties
    {
        get => _properties;
        set => _properties = value ?? [];
    }
}

/// <summary>
/// A layout change of ONE view (decision <c>MODELING-AUTHORING-CHANGESET</c>); upsert semantics per
/// node and connection. An empty <see cref="ViewId"/> means the model's default view — the writer
/// creates one (<c>Id = "default"</c>) when the model has none.
/// </summary>
public sealed class ModelingViewChange
{
    private string _viewId = string.Empty;
    private List<ModelingViewNodeChange> _nodes = [];
    private List<string> _removedNodeElementIds = [];
    private List<ModelingViewConnectionChange> _connections = [];
    private List<string> _removedConnectionRelationshipIds = [];

    /// <summary>Target view id; empty means the model's default view.</summary>
    public string ViewId
    {
        get => _viewId;
        set => _viewId = value ?? string.Empty;
    }

    /// <summary>Node placements to upsert (a new node requires <c>X</c> and <c>Y</c>).</summary>
    public List<ModelingViewNodeChange> Nodes
    {
        get => _nodes;
        set => _nodes = value ?? [];
    }

    /// <summary>View nodes to remove, addressed by <see cref="ModelingViewNodeDto.ElementId"/>.</summary>
    public List<string> RemovedNodeElementIds
    {
        get => _removedNodeElementIds;
        set => _removedNodeElementIds = value ?? [];
    }

    /// <summary>Connection placements to upsert.</summary>
    public List<ModelingViewConnectionChange> Connections
    {
        get => _connections;
        set => _connections = value ?? [];
    }

    /// <summary>View connections to remove, addressed by the relationship id.</summary>
    public List<string> RemovedConnectionRelationshipIds
    {
        get => _removedConnectionRelationshipIds;
        set => _removedConnectionRelationshipIds = value ?? [];
    }
}

/// <summary>A geometry change of ONE view node. Every member is nullable: <c>null</c> = unchanged.</summary>
public sealed class ModelingViewNodeChange
{
    private string _elementId = string.Empty;

    /// <summary>Model element rendered by the view node.</summary>
    public string ElementId
    {
        get => _elementId;
        set => _elementId = value ?? string.Empty;
    }

    /// <summary>X position; null = unchanged, and required for a node that is not in the view yet.</summary>
    public double? X { get; set; }

    /// <summary>Y position; null = unchanged, and required for a node that is not in the view yet.</summary>
    public double? Y { get; set; }

    /// <summary>Width; null = unchanged (or the stencil default for a new node).</summary>
    public double? Width { get; set; }

    /// <summary>Height; null = unchanged (or the stencil default for a new node).</summary>
    public double? Height { get; set; }

    /// <summary>
    /// Parent VIEW node identifier for nested elements (it holds the PARENT ELEMENT id — view nodes
    /// have no id of their own). <c>null</c> = unchanged, <c>""</c> = clear (the node becomes a root).
    /// </summary>
    public string? ParentNodeId { get; set; }
}

/// <summary>A routing change of ONE view connection. <c>null</c> = unchanged, empty = clear.</summary>
public sealed class ModelingViewConnectionChange
{
    private string _relationshipId = string.Empty;

    /// <summary>Model relationship rendered by the view connection.</summary>
    public string RelationshipId
    {
        get => _relationshipId;
        set => _relationshipId = value ?? string.Empty;
    }

    /// <summary>Bend points; null = unchanged, an EMPTY list clears the manual routing.</summary>
    public List<ModelingViewWaypointDto>? Waypoints { get; set; }
}

/// <summary>A single element's changed fields. A <c>null</c> property means "unchanged" —
/// the writer must be able to distinguish "not changed" from "changed to null/empty".</summary>
public sealed class ModelingElementChange
{
    private string _elementId = string.Empty;

    /// <summary>Identifier of the edited <see cref="ModelingElementDto.Id"/>.</summary>
    public string ElementId
    {
        get => _elementId;
        set => _elementId = value ?? string.Empty;
    }

    /// <summary>The new <see cref="ModelingElementDto.Name"/>, or null when unchanged.</summary>
    public string? Name { get; set; }

    /// <summary>The new <see cref="ModelingElementDto.Description"/>, or null when unchanged.</summary>
    public string? Description { get; set; }

    /// <summary>Property values to set, preserving the JSON value kind of each entry; null when
    /// no property changed.</summary>
    public Dictionary<string, JsonElement>? SetProperties { get; set; }

    /// <summary>Property keys to remove; null when no property was removed.</summary>
    public List<string>? RemovedProperties { get; set; }

    /// <summary>
    /// The new <see cref="ModelingElementDto.ParentId"/> (pool/lane/subprocess containment).
    /// <c>null</c> = unchanged, <c>""</c> = make the element a root.
    /// </summary>
    public string? ParentId { get; set; }
}

/// <summary>A single relationship's changed fields. A <c>null</c> property means "unchanged".</summary>
public sealed class ModelingRelationshipChange
{
    private string _relationshipId = string.Empty;

    /// <summary>Identifier of the edited <see cref="ModelingRelationshipDto.Id"/>.</summary>
    public string RelationshipId
    {
        get => _relationshipId;
        set => _relationshipId = value ?? string.Empty;
    }

    /// <summary>The new <see cref="ModelingRelationshipDto.Name"/>, or null when unchanged.</summary>
    public string? Name { get; set; }

    /// <summary>Property values to set, preserving the JSON value kind of each entry; null when
    /// no property changed.</summary>
    public Dictionary<string, JsonElement>? SetProperties { get; set; }

    /// <summary>Property keys to remove; null when no property was removed.</summary>
    public List<string>? RemovedProperties { get; set; }

    /// <summary>
    /// The new <see cref="ModelingRelationshipDto.SourceElementId"/>, or null when unchanged
    /// (re-targeting, needed when a lane move rewrites a connection).
    /// </summary>
    public string? SourceElementId { get; set; }

    /// <summary>
    /// The new <see cref="ModelingRelationshipDto.TargetElementId"/>, or null when unchanged.
    /// </summary>
    public string? TargetElementId { get; set; }
}

/// <summary>The outcome of <see cref="IModelingModelWriter.SaveChangesAsync"/>.</summary>
public sealed class ModelingModelSaveResult
{
    /// <summary>Whether the change set was persisted.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Whether the save failed because the source moved on from
    /// <see cref="ModelingModelChangeSet.BaseVersion"/> — the editor shows a reload prompt.</summary>
    public bool IsConflict { get; set; }

    /// <summary>User-facing message for a failed save (a conflict reason, a validation error…).</summary>
    public string? Message { get; set; }

    /// <summary>The freshly loaded model after a successful (or conflict-resolved) save; the
    /// editor adopts it as the new baseline when supplied.</summary>
    public ModelingModelDto? Model { get; set; }

    private List<ModelingIssueDto> _issues = [];

    /// <summary>Issues the writer wants surfaced after the save (validation findings…).</summary>
    public List<ModelingIssueDto> Issues
    {
        get => _issues;
        set => _issues = value ?? [];
    }
}
