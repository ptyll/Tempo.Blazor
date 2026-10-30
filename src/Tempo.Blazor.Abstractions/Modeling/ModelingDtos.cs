using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tempo.Blazor.Modeling;

/// <summary>Governance state attached to a source-backed modeling element.</summary>
public sealed class ModelingGovernanceDto
{
    private string _trustLevel = string.Empty;
    private string _reviewState = string.Empty;
    private string _syncState = string.Empty;
    private string _dataSource = string.Empty;

    /// <summary>Trust classification assigned by the source system or reviewer.</summary>
    public string TrustLevel
    {
        get => _trustLevel;
        set => _trustLevel = value ?? string.Empty;
    }

    /// <summary>Human or automated review state.</summary>
    public string ReviewState
    {
        get => _reviewState;
        set => _reviewState = value ?? string.Empty;
    }

    /// <summary>Synchronization state between Tempo and the source system.</summary>
    public string SyncState
    {
        get => _syncState;
        set => _syncState = value ?? string.Empty;
    }

    /// <summary>Name or key of the data source that owns this governance state.</summary>
    public string DataSource
    {
        get => _dataSource;
        set => _dataSource = value ?? string.Empty;
    }
}

/// <summary>A semantic model element before it is projected into a diagram node.</summary>
public sealed class ModelingElementDto
{
    private string _id = string.Empty;
    private string _sourceId = string.Empty;
    private string _sourceType = string.Empty;
    private string _sourcePath = string.Empty;
    private string _notation = string.Empty;
    private string _semanticType = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private Dictionary<string, JsonElement> _properties = [];
    private List<string> _tags = [];
    private ModelingGovernanceDto _governance = new();

    /// <summary>Unique identifier inside the loaded model.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Identifier of the corresponding object in the source system.</summary>
    public string SourceId
    {
        get => _sourceId;
        set => _sourceId = value ?? string.Empty;
    }

    /// <summary>Type of the corresponding object in the source system.</summary>
    public string SourceType
    {
        get => _sourceType;
        set => _sourceType = value ?? string.Empty;
    }

    /// <summary>Human-readable or machine-readable path to the source object.</summary>
    public string SourcePath
    {
        get => _sourcePath;
        set => _sourcePath = value ?? string.Empty;
    }

    /// <summary>Modeling notation key, for example BPMN or ArchiMate.</summary>
    public string Notation
    {
        get => _notation;
        set => _notation = value ?? string.Empty;
    }

    /// <summary>Semantic element type within the notation.</summary>
    public string SemanticType
    {
        get => _semanticType;
        set => _semanticType = value ?? string.Empty;
    }

    /// <summary>Display name used by the modeling UI.</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    private string _parentId = string.Empty;

    /// <summary>
    /// Identifier of the parent element inside the model, when the element is hierarchically
    /// contained (for example a BPMN subprocess or a UML package). Empty when the element is a
    /// root. Additive in 2.10.0: drives the model tree's hierarchy view only — the diagram
    /// generator never maps it onto <see cref="ModelingViewNodeDto.ParentNodeId"/> (diagram
    /// nesting stays owned by the view). A value that does not resolve to another element of the
    /// model is reported as a validation issue and the element renders at the tree root.
    /// </summary>
    public string ParentId
    {
        get => _parentId;
        set => _parentId = value ?? string.Empty;
    }

    /// <summary>Optional element description.</summary>
    public string Description
    {
        get => _description;
        set => _description = value ?? string.Empty;
    }

    /// <summary>Arbitrary source-specific properties preserved as JSON values.</summary>
    public Dictionary<string, JsonElement> Properties
    {
        get => _properties;
        set => _properties = value ?? [];
    }

    /// <summary>Searchable or filterable element tags.</summary>
    public List<string> Tags
    {
        get => _tags;
        set => _tags = value ?? [];
    }

    /// <summary>Governance metadata for this element.</summary>
    public ModelingGovernanceDto Governance
    {
        get => _governance;
        set => _governance = value ?? new();
    }
}

/// <summary>A semantic relationship between two modeling elements.</summary>
public sealed class ModelingRelationshipDto
{
    private string _id = string.Empty;
    private string _sourceId = string.Empty;
    private string _sourceType = string.Empty;
    private string _sourceElementId = string.Empty;
    private string _targetElementId = string.Empty;
    private string _relationshipType = string.Empty;
    private string _name = string.Empty;
    private Dictionary<string, JsonElement> _properties = [];
    private List<string> _tags = [];

    /// <summary>Unique identifier inside the loaded model.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Identifier of the corresponding relationship in the source system.</summary>
    public string SourceId
    {
        get => _sourceId;
        set => _sourceId = value ?? string.Empty;
    }

    /// <summary>Type of the corresponding relationship in the source system.</summary>
    public string SourceType
    {
        get => _sourceType;
        set => _sourceType = value ?? string.Empty;
    }

    /// <summary>Source element identifier.</summary>
    public string SourceElementId
    {
        get => _sourceElementId;
        set => _sourceElementId = value ?? string.Empty;
    }

    /// <summary>Target element identifier.</summary>
    public string TargetElementId
    {
        get => _targetElementId;
        set => _targetElementId = value ?? string.Empty;
    }

    /// <summary>Semantic relationship type within the notation.</summary>
    public string RelationshipType
    {
        get => _relationshipType;
        set => _relationshipType = value ?? string.Empty;
    }

    /// <summary>Optional display name used by the modeling UI.</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>Arbitrary source-specific properties preserved as JSON values.</summary>
    public Dictionary<string, JsonElement> Properties
    {
        get => _properties;
        set => _properties = value ?? [];
    }

    /// <summary>Searchable or filterable relationship tags.</summary>
    public List<string> Tags
    {
        get => _tags;
        set => _tags = value ?? [];
    }
}

/// <summary>A node placement in a modeling view.</summary>
public sealed class ModelingViewNodeDto
{
    private string _elementId = string.Empty;

    /// <summary>Identifier of the model element rendered by this view node.</summary>
    public string ElementId
    {
        get => _elementId;
        set => _elementId = value ?? string.Empty;
    }

    /// <summary>X position in view coordinates.</summary>
    public double X { get; set; }

    /// <summary>Y position in view coordinates.</summary>
    public double Y { get; set; }

    /// <summary>Node width in view coordinates.</summary>
    public double Width { get; set; }

    /// <summary>Node height in view coordinates.</summary>
    public double Height { get; set; }

    /// <summary>Optional parent view node identifier for nested elements.</summary>
    public string? ParentNodeId { get; set; }
}

/// <summary>A point used as a relationship waypoint in a modeling view.</summary>
public sealed class ModelingViewWaypointDto
{
    /// <summary>X position in view coordinates.</summary>
    public double X { get; set; }

    /// <summary>Y position in view coordinates.</summary>
    public double Y { get; set; }
}

/// <summary>A relationship connection placement in a modeling view.</summary>
public sealed class ModelingViewConnectionDto
{
    private string _relationshipId = string.Empty;
    private string _sourceNodeId = string.Empty;
    private string _targetNodeId = string.Empty;
    private List<ModelingViewWaypointDto> _waypoints = [];

    /// <summary>Identifier of the model relationship rendered by this connection.</summary>
    public string RelationshipId
    {
        get => _relationshipId;
        set => _relationshipId = value ?? string.Empty;
    }

    /// <summary>Identifier of the source view node.</summary>
    public string SourceNodeId
    {
        get => _sourceNodeId;
        set => _sourceNodeId = value ?? string.Empty;
    }

    /// <summary>Identifier of the target view node.</summary>
    public string TargetNodeId
    {
        get => _targetNodeId;
        set => _targetNodeId = value ?? string.Empty;
    }

    /// <summary>Optional bend points between the source and target nodes.</summary>
    public List<ModelingViewWaypointDto> Waypoints
    {
        get => _waypoints;
        set => _waypoints = value ?? [];
    }
}

/// <summary>A named projection of model elements and relationships.</summary>
public sealed class ModelingViewDto
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _notation = string.Empty;
    private string _viewpointKey = string.Empty;
    private List<ModelingViewNodeDto> _nodes = [];
    private List<ModelingViewConnectionDto> _connections = [];

    /// <summary>Unique view identifier.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Display name shown in the modeling editor.</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>Modeling notation key used by this view.</summary>
    public string Notation
    {
        get => _notation;
        set => _notation = value ?? string.Empty;
    }

    /// <summary>Viewpoint key used to filter and arrange this view.</summary>
    public string ViewpointKey
    {
        get => _viewpointKey;
        set => _viewpointKey = value ?? string.Empty;
    }

    /// <summary>Nodes included in this view.</summary>
    public List<ModelingViewNodeDto> Nodes
    {
        get => _nodes;
        set => _nodes = value ?? [];
    }

    /// <summary>Connections included in this view.</summary>
    public List<ModelingViewConnectionDto> Connections
    {
        get => _connections;
        set => _connections = value ?? [];
    }
}

/// <summary>Metadata describing where a modeling model came from and whether it is fresh.</summary>
public sealed class ModelingMetadataDto
{
    private string _sourceSystem = string.Empty;
    private string _sourceVersion = string.Empty;

    /// <summary>Name or key of the source system.</summary>
    public string SourceSystem
    {
        get => _sourceSystem;
        set => _sourceSystem = value ?? string.Empty;
    }

    /// <summary>Source system version or source snapshot version.</summary>
    public string SourceVersion
    {
        get => _sourceVersion;
        set => _sourceVersion = value ?? string.Empty;
    }

    /// <summary>Timestamp when the model was loaded.</summary>
    public DateTimeOffset LoadedAt { get; set; }

    /// <summary>Whether the loaded model is considered fresh by the provider.</summary>
    public bool IsFresh { get; set; }
}

/// <summary>Severity of an issue discovered while loading or generating a modeling model.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ModelingIssueSeverity>))]
public enum ModelingIssueSeverity
{
    /// <summary>Informational issue that does not require action.</summary>
    Info,

    /// <summary>Warning issue that may need attention.</summary>
    Warning,

    /// <summary>Error issue that blocks a complete result.</summary>
    Error
}

/// <summary>An issue discovered in source data, validation, or diagram generation.</summary>
public sealed class ModelingIssueDto
{
    private string _id = string.Empty;
    private string _category = string.Empty;
    private string _sourceElementId = string.Empty;
    private string _sourceRelationshipId = string.Empty;
    private string _message = string.Empty;
    private string _suggestedFix = string.Empty;

    /// <summary>Unique issue identifier.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Issue severity.</summary>
    public ModelingIssueSeverity Severity { get; set; }

    /// <summary>Issue category such as validation, mapping, sync, or layout.</summary>
    public string Category
    {
        get => _category;
        set => _category = value ?? string.Empty;
    }

    /// <summary>Optional source element identifier related to this issue.</summary>
    public string SourceElementId
    {
        get => _sourceElementId;
        set => _sourceElementId = value ?? string.Empty;
    }

    /// <summary>Optional source relationship identifier related to this issue.</summary>
    public string SourceRelationshipId
    {
        get => _sourceRelationshipId;
        set => _sourceRelationshipId = value ?? string.Empty;
    }

    /// <summary>User-facing issue message.</summary>
    public string Message
    {
        get => _message;
        set => _message = value ?? string.Empty;
    }

    /// <summary>Optional suggested fix for the issue.</summary>
    public string SuggestedFix
    {
        get => _suggestedFix;
        set => _suggestedFix = value ?? string.Empty;
    }
}

/// <summary>Root modeling model exchanged between providers and the modeling editor.</summary>
public sealed class ModelingModelDto
{
    private string _id = string.Empty;
    private string _title = string.Empty;
    private string _notation = string.Empty;
    private List<string> _supportedNotations = [];
    private List<ModelingElementDto> _elements = [];
    private List<ModelingRelationshipDto> _relationships = [];
    private List<ModelingViewDto> _views = [];
    private List<ModelingIssueDto> _issues = [];
    private ModelingMetadataDto _metadata = new();

    /// <summary>Unique model identifier.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Human-readable model title.</summary>
    public string Title
    {
        get => _title;
        set => _title = value ?? string.Empty;
    }

    /// <summary>Primary modeling notation key for this model.</summary>
    public string Notation
    {
        get => _notation;
        set => _notation = value ?? string.Empty;
    }

    /// <summary>Notation keys supported by this model.</summary>
    public List<string> SupportedNotations
    {
        get => _supportedNotations;
        set => _supportedNotations = value ?? [];
    }

    /// <summary>Elements contained in this model.</summary>
    public List<ModelingElementDto> Elements
    {
        get => _elements;
        set => _elements = value ?? [];
    }

    /// <summary>Relationships contained in this model.</summary>
    public List<ModelingRelationshipDto> Relationships
    {
        get => _relationships;
        set => _relationships = value ?? [];
    }

    /// <summary>Views defined by this model.</summary>
    public List<ModelingViewDto> Views
    {
        get => _views;
        set => _views = value ?? [];
    }

    /// <summary>Issues discovered while loading or preparing this model.</summary>
    public List<ModelingIssueDto> Issues
    {
        get => _issues;
        set => _issues = value ?? [];
    }

    /// <summary>Source metadata for this model.</summary>
    public ModelingMetadataDto Metadata
    {
        get => _metadata;
        set => _metadata = value ?? new();
    }
}
