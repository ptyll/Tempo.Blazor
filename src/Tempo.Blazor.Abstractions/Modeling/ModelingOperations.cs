using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tempo.Blazor.Modeling;

/// <summary>
/// How strictly a <see cref="ModelingOperationBatch"/> is applied (decision
/// <c>MODELING-UNIFIED-OPERATION-ENGINE</c>).
/// </summary>
public enum ModelingOperationPolicy
{
    /// <summary>MCP semantics kept byte-compatible: no silent cascade and no view operations
    /// (until a later phase enables them for MCP).</summary>
    Strict,

    /// <summary>Editor semantics: view (layout) operations are allowed. Removals are still never
    /// cascaded by the engine (decision Q3) — the caller expands them.</summary>
    Authoring
}

/// <summary>An ordered batch of modeling operations applied by <see cref="ModelingOperationEngine"/>.</summary>
public sealed class ModelingOperationBatch
{
    private List<ModelingOperation> _operations = [];

    /// <summary>The operations, applied in list order (a parent must precede its children).</summary>
    public List<ModelingOperation> Operations
    {
        get => _operations;
        set => _operations = value ?? [];
    }
}

/// <summary>
/// ONE typed modeling operation. It is the typed front-end of the same JSON shape
/// <c>modeling_apply_operations</c> accepts: a <c>null</c> member is NOT emitted, so it means
/// "unchanged", while an empty string / empty list is a real value (clear the parent, clear the
/// waypoints). The op kind decides which members are read (see the operation catalog in
/// <a href="https://github.com/ptyll/Tempo.Blazor/blob/main/docs/modeling-authoring.md">docs/modeling-authoring.md</a> §6.2).
/// </summary>
public sealed class ModelingOperation
{
    private string _op = string.Empty;
    private string? _id;
    private string? _name;
    private string? _description;
    private string? _notation;
    private string? _semanticType;
    private string? _parentId;
    private string? _relationshipType;
    private string? _sourceElementId;
    private string? _targetElementId;
    private string? _elementId;
    private string? _relationshipId;
    private string? _viewId;
    private string? _parentNodeId;
    private Dictionary<string, JsonElement>? _properties;
    private List<string>? _removedProperties;
    private List<ModelingViewWaypointDto>? _waypoints;

    /// <summary>Operation kind: <c>add_element</c>, <c>update_element</c>, <c>delete_element</c>,
    /// <c>add_relationship</c>, <c>update_relationship</c>, <c>delete_relationship</c>,
    /// <c>set_properties</c>, <c>set_view_node</c>, <c>remove_view_node</c>,
    /// <c>set_view_connection</c> or <c>remove_view_connection</c>.</summary>
    public string Op
    {
        get => _op;
        set => _op = value ?? string.Empty;
    }

    /// <summary>Element or relationship id (per operation). Null = not supplied.</summary>
    public string? Id
    {
        get => _id;
        set => _id = value;
    }

    /// <summary>Name (<c>add_element</c> / <c>update_element</c> / relationship operations).</summary>
    public string? Name
    {
        get => _name;
        set => _name = value;
    }

    /// <summary>Description (<c>add_element</c> / <c>update_element</c>).</summary>
    public string? Description
    {
        get => _description;
        set => _description = value;
    }

    /// <summary>Notation key (<c>add_element</c> / <c>update_element</c>).</summary>
    public string? Notation
    {
        get => _notation;
        set => _notation = value;
    }

    /// <summary>Semantic element type (<c>add_element</c> / <c>update_element</c>).</summary>
    public string? SemanticType
    {
        get => _semanticType;
        set => _semanticType = value;
    }

    /// <summary>Parent element id (pool/lane/subprocess containment); empty clears it.</summary>
    public string? ParentId
    {
        get => _parentId;
        set => _parentId = value;
    }

    /// <summary>Semantic relationship type (<c>add_relationship</c> / <c>update_relationship</c>).</summary>
    public string? RelationshipType
    {
        get => _relationshipType;
        set => _relationshipType = value;
    }

    /// <summary>Source element id (relationship operations).</summary>
    public string? SourceElementId
    {
        get => _sourceElementId;
        set => _sourceElementId = value;
    }

    /// <summary>Target element id (relationship operations).</summary>
    public string? TargetElementId
    {
        get => _targetElementId;
        set => _targetElementId = value;
    }

    /// <summary>Element id addressed by a view-node operation.</summary>
    public string? ElementId
    {
        get => _elementId;
        set => _elementId = value;
    }

    /// <summary>Relationship id addressed by a view-connection operation.</summary>
    public string? RelationshipId
    {
        get => _relationshipId;
        set => _relationshipId = value;
    }

    /// <summary>Target view id; null / empty means the model's default view.</summary>
    public string? ViewId
    {
        get => _viewId;
        set => _viewId = value;
    }

    /// <summary>Parent VIEW node id (the parent ELEMENT id); empty clears it, null = unchanged.</summary>
    public string? ParentNodeId
    {
        get => _parentNodeId;
        set => _parentNodeId = value;
    }

    /// <summary>X geometry of a view node.</summary>
    public double? X { get; set; }

    /// <summary>Y geometry of a view node.</summary>
    public double? Y { get; set; }

    /// <summary>Width of a view node.</summary>
    public double? Width { get; set; }

    /// <summary>Height of a view node.</summary>
    public double? Height { get; set; }

    /// <summary>Property values to set — accepted by <c>set_properties</c>, the <c>Added*</c> ops AND
    /// <c>update_element</c> / <c>update_relationship</c> (the same optional payload on all four
    /// collection operations).</summary>
    public Dictionary<string, JsonElement>? Properties
    {
        get => _properties;
        set => _properties = value;
    }

    /// <summary>Property keys to remove — accepted by <c>set_properties</c> and the two update
    /// operations.</summary>
    public List<string>? RemovedProperties
    {
        get => _removedProperties;
        set => _removedProperties = value;
    }

    /// <summary>Connection bend points; an empty list clears them, null = unchanged.</summary>
    public List<ModelingViewWaypointDto>? Waypoints
    {
        get => _waypoints;
        set => _waypoints = value;
    }

    /// <summary>Projects the typed operation onto the JSON shape the engine and MCP share.</summary>
    internal JsonObject ToJson()
    {
        var json = new JsonObject();
        AddString(json, "op", _op, omitWhenEmpty: true);
        AddString(json, "id", _id);
        AddString(json, "name", _name);
        AddString(json, "description", _description);
        AddString(json, "notation", _notation);
        AddString(json, "semanticType", _semanticType);
        AddString(json, "parentId", _parentId);
        AddString(json, "relationshipType", _relationshipType);
        AddString(json, "sourceElementId", _sourceElementId);
        AddString(json, "targetElementId", _targetElementId);
        AddString(json, "elementId", _elementId);
        AddString(json, "relationshipId", _relationshipId);
        AddString(json, "viewId", _viewId);
        AddString(json, "parentNodeId", _parentNodeId);

        if (X is { } x)
        {
            json["x"] = x;
        }

        if (Y is { } y)
        {
            json["y"] = y;
        }

        if (Width is { } width)
        {
            json["width"] = width;
        }

        if (Height is { } height)
        {
            json["height"] = height;
        }

        if (_properties is not null)
        {
            json["properties"] = JsonSerializer.SerializeToNode(_properties);
        }

        if (_removedProperties is not null)
        {
            json["removedProperties"] = new JsonArray([.. _removedProperties.Select(key => (JsonNode?)JsonValue.Create(key))]);
        }

        if (_waypoints is not null)
        {
            var waypoints = new JsonArray();
            foreach (var waypoint in _waypoints)
            {
                waypoints.Add(new JsonObject { ["x"] = waypoint.X, ["y"] = waypoint.Y });
            }

            json["waypoints"] = waypoints;
        }

        return json;
    }

    private static void AddString(JsonObject json, string key, string? value, bool omitWhenEmpty = false)
    {
        if (value is null || (omitWhenEmpty && value.Length == 0))
        {
            return;
        }

        json[key] = value;
    }
}

/// <summary>
/// Everything an operation batch needs from the host: the notation rules it validates against and
/// the policy it applies under.
/// </summary>
public sealed class ModelingOperationContext
{
    /// <summary>Notation relationship rules; when null only reference/structure checks run.</summary>
    public IModelingRelationshipRulesProvider? RelationshipRules { get; set; }

    /// <summary>Optional viewpoint rules (viewpoint-scoped element checks).</summary>
    public IModelingViewpointRulesProvider? ViewpointRules { get; set; }

    /// <summary>Optional notation profile provider (supported vocabulary discovery).</summary>
    public IModelingNotationProfileProvider? NotationProfiles { get; set; }

    /// <summary>
    /// The notation the caller authors under. Null (the default) validates against the model's own
    /// <see cref="ModelingModelDto.Notation"/> — the MCP behavior. An EDITOR that lets the host
    /// override the notation (a model supporting several notations, e.g. "mixed", rendered as
    /// "bpmn2") passes the effective key here so its own rule checks and the engine's validation
    /// judge the SAME notation (mandatory rule "one semantics, same errors").
    /// </summary>
    public string? Notation { get; set; }

    /// <summary>
    /// The viewpoint the caller authors under (phase 3 review, A2). Null (the default) keeps the
    /// 2.10.0 behavior: the engine resolves the viewpoint from the target view (or reaches the
    /// rules with an empty key). An EDITOR whose host filters the model by a viewpoint passes its
    /// effective key here, so the type picker's rule probe and the engine's own verdict judge the
    /// SAME viewpoint — a UML "Include" offered under <c>UseCaseDiagram</c> then also COMMITS.
    /// </summary>
    public string? ViewpointKey { get; set; }

    /// <summary>Editor batch (expanded cascades, geometry allowed) vs MCP batch (no cascade, no
    /// geometry unless asked). Defaults to <see cref="ModelingOperationPolicy.Strict"/>.</summary>
    public ModelingOperationPolicy Policy { get; set; } = ModelingOperationPolicy.Strict;

    /// <summary>
    /// Phase 5 (decision <c>MODELING-PROPERTY-SCHEMA</c> §5.5/§6.4): resolves the property schema for
    /// a <see cref="ModelingPropertySchemaRequest"/>. The engine is synchronous while
    /// <see cref="IModelingPropertySchemaProvider"/> is async, so the CALLER resolves the schemas up
    /// front (a dictionary lookup here — see <see cref="ModelingPropertySchemaResolver"/>) and the
    /// engine validates every property payload with the SHARED
    /// <see cref="ModelingPropertySchemaValidator"/> — the same rules, messages and codes the
    /// inspector uses ("one semantics, same errors").
    /// <para>
    /// The engine BUILDS the request (§5.1): <c>ModelId = model.Id</c>, the notation and semantic
    /// type of the judged value, <c>ViewpointKey</c> = this context's
    /// <see cref="ViewpointKey"/> else the target view's (<c>Views[0]</c>) else <c>""</c>,
    /// <c>Culture</c> = <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> and
    /// <c>SchemaVersion = null</c> (the engine never pins — a caller that pinned answers from its
    /// preloaded dictionary). A body that only needs the pair ignores the rest, exactly like the
    /// phase-5 <c>(notation, semanticType)</c> delegate did.
    /// </para>
    /// <para>
    /// A RELATIONSHIP is looked up with its relationship type as the semantic type. A NEW
    /// element/relationship (<c>add_element</c> / <c>add_relationship</c>) is validated in FULL —
    /// AFTER the whole batch, so a following <c>set_properties</c> in the same batch can still
    /// satisfy a required field — while an UPDATE (<c>update_element</c>, <c>update_relationship</c>,
    /// <c>set_properties</c>) is validated only for the keys it touches (or in full when the
    /// operation changes the semantic type / notation), so a client can still fix one field of a
    /// model that carries an unrelated legacy value. Reference EXISTENCE is never checked here —
    /// that is the writer's job (§5.4).
    /// </para>
    /// <para>Null (the default) validates no property values at all — the 2.10.0 behavior.</para>
    /// </summary>
    public Func<ModelingPropertySchemaRequest, ModelingPropertySchemaDto?>? ResolvePropertySchema { get; set; }
}

/// <summary>The outcome of <see cref="ModelingOperationEngine.Apply"/> / <c>ApplyJson</c>.</summary>
public sealed class ModelingOperationResult
{
    private readonly List<string> _createdIds = [];
    private readonly List<ModelingOperationError> _errors = [];

    /// <summary>Whether every operation succeeded (a batch with one error is a failed batch).</summary>
    public bool Success => _errors.Count == 0;

    /// <summary>How many operations were applied. A failed batch is still partially applied to the
    /// passed model, so atomic callers apply to a <see cref="ModelingOperationEngine.Clone"/> and
    /// swap only on success.</summary>
    public int Applied { get; internal set; }

    /// <summary>Ids generated for operations that did not carry one (element / relationship ids).</summary>
    public List<string> CreatedIds => _createdIds;

    /// <summary>The per-operation errors, in operation order.</summary>
    public List<ModelingOperationError> Errors => _errors;

    internal void AddError(ModelingOperationError error) => _errors.Add(error);

    internal void AddCreatedId(string id) => _createdIds.Add(id);
}

/// <summary>One failed operation, with the legacy message and a machine-readable code.</summary>
public sealed class ModelingOperationError
{
    /// <summary>Index of the offending operation in the batch (<c>-1</c> for a document-level
    /// parse error).</summary>
    public int Index { get; set; }

    /// <summary>The operation kind (empty for a parse error that has no usable <c>op</c>).</summary>
    public string Op { get; set; } = string.Empty;

    /// <summary>Machine-readable code: <c>parse</c>, <c>unknown_op</c>, <c>missing_field</c>,
    /// <c>duplicate_id</c>, <c>not_found</c>, <c>parent_cycle</c>, <c>parent_not_found</c>,
    /// <c>still_referenced</c>, <c>still_has_children</c>, <c>invalid_relationship</c>,
    /// <c>unsupported_relationship</c>, <c>viewpoint_disallowed</c>, <c>property_required</c>,
    /// <c>property_type</c>, <c>property_range</c>, <c>property_pattern</c>, <c>property_enum</c>,
    /// <c>property_reference</c> or <c>unsupported_operation</c>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The 2.10.0-formatted user/agent-facing message (MCP parity).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional suggested fix from the notation rule that rejected the operation.</summary>
    public string? SuggestedFix { get; set; }

    /// <summary>Offending element id, when the operation addressed one.</summary>
    public string? ElementId { get; set; }

    /// <summary>Offending relationship id, when the operation addressed one.</summary>
    public string? RelationshipId { get; set; }

    /// <summary>Offending property key, when the operation addressed one.</summary>
    public string? PropertyKey { get; set; }
}
