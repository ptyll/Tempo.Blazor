using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tempo.Blazor.Modeling;

/// <summary>
/// The ONE modeling operation engine (decision <c>MODELING-UNIFIED-OPERATION-ENGINE</c>): the
/// editor's structural/geometry commits and the <c>modeling_apply_operations</c> MCP tool apply a
/// batch through this type, so both surfaces share ONE semantics and the SAME error list.
/// <para>
/// <see cref="Apply"/> mutates the passed model IN PLACE (2.10.0 MCP semantics). A batch with one
/// invalid operation is a failed batch: nothing may be persisted, so an atomic caller applies to a
/// <see cref="Clone"/> and swaps on <see cref="ModelingOperationResult.Success"/>. The engine never
/// touches a provider or a writer, and it never cascades a removal — the editor expands the
/// cascade into the batch (decision Q3) and the engine keeps the MCP
/// <c>still_referenced</c> / <c>still_has_children</c> errors for every caller.
/// </para>
/// </summary>
public static partial class ModelingOperationEngine
{
    internal const string DefaultViewId = "default";

    /// <summary>Clone options: the shape the MCP tools serialize models with (camelCase, string
    /// enums, nulls omitted) so a cloned model is byte-identical to a round-tripped one.</summary>
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly string[] ViewOperations =
    [
        "set_view_node",
        "remove_view_node",
        "set_view_connection",
        "remove_view_connection"
    ];

    /// <summary>
    /// Applies the batch IN PLACE to <paramref name="model"/>. On failure the passed instance is in
    /// an undefined (partially applied) state, so atomic callers pass a <see cref="Clone"/> and swap
    /// on success. The CALLER persists: nothing here touches <c>IModelingModelDocumentProvider</c>
    /// or <c>IModelingModelWriter</c>.
    /// </summary>
    /// <param name="model">The model to mutate.</param>
    /// <param name="batch">The ordered operations.</param>
    /// <param name="context">Rules + policy; a null context means <see cref="ModelingOperationPolicy.Strict"/>.</param>
    /// <returns>The per-operation outcome (applied count, created ids, errors).</returns>
    public static ModelingOperationResult Apply(
        ModelingModelDto model,
        ModelingOperationBatch batch,
        ModelingOperationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(batch);

        var result = new ModelingOperationResult();
        var state = new ApplyState(context ?? new ModelingOperationContext());

        for (var index = 0; index < batch.Operations.Count; index++)
        {
            var operation = batch.Operations[index]
                ?? throw new ArgumentException($"operations[{index}] is null.", nameof(batch));
            ApplyOperation(model, operation.ToJson(), index, result, state);
        }

        ValidateRelationshipsAfterParentChanges(model, result, state);
        ValidateDeferredPropertySets(model, result, state);
        return result;
    }

    /// <summary>
    /// JSON front-end (the MCP contract, kept byte-compatible): parses the operation array and
    /// reports parse errors with the 2.10.0 wording.
    /// </summary>
    /// <param name="model">The model to mutate.</param>
    /// <param name="operationsJson">JSON array of operations.</param>
    /// <param name="context">Rules + policy; a null context means <see cref="ModelingOperationPolicy.Strict"/>.</param>
    /// <returns>The per-operation outcome (applied count, created ids, errors).</returns>
    public static ModelingOperationResult ApplyJson(
        ModelingModelDto model,
        string operationsJson,
        ModelingOperationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var result = new ModelingOperationResult();
        var state = new ApplyState(context ?? new ModelingOperationContext());

        JsonArray? operations;
        try
        {
            // No whitespace shortcut (review fix, phase 2): an EMPTY payload is just as malformed as
            // any other invalid JSON, so the parse throws and the caller gets the legacy
            // "invalid JSON (...)" error instead of a misleading "expected a JSON array" shape error.
            operations = JsonNode.Parse(operationsJson ?? string.Empty) as JsonArray;
            if (operations is null)
            {
                AddError(result, -1, string.Empty, "parse", "operations: expected a JSON array of operations.");
                return result;
            }
        }
        catch (JsonException ex)
        {
            AddError(result, -1, string.Empty, "parse", $"operations: invalid JSON ({ex.Message}).");
            return result;
        }

        for (var index = 0; index < operations.Count; index++)
        {
            if (operations[index] is not JsonObject operation)
            {
                AddError(result, index, string.Empty, "parse", $"operations[{index}]: expected a JSON object.");
                continue;
            }

            ApplyOperation(model, operation, index, result, state);
        }

        ValidateRelationshipsAfterParentChanges(model, result, state);
        ValidateDeferredPropertySets(model, result, state);
        return result;
    }

    /// <summary>Deep copy through System.Text.Json — the atomic-batch helper (the MCP tools moved
    /// their private clone here).</summary>
    /// <param name="model">The model to copy.</param>
    /// <returns>A deep, independent copy.</returns>
    public static ModelingModelDto Clone(ModelingModelDto model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var json = JsonSerializer.Serialize(model, CloneOptions);
        return JsonSerializer.Deserialize<ModelingModelDto>(json, CloneOptions)
            ?? throw new JsonException($"Could not clone {nameof(ModelingModelDto)}.");
    }

    // ── Dispatch ────────────────────────────────────────────────────────────────

    private sealed class ApplyState(ModelingOperationContext context)
    {
        public ModelingOperationContext Context { get; } = context;

        /// <summary>Element id → the (index, op) that changed its ParentId. The Authoring policy
        /// re-validates every relationship incident to such an element after the last operation
        /// (§3.2 rule 3): a lane move can invalidate a flow the batch never touched.</summary>
        public Dictionary<string, (int Index, string Op)> ParentChanges { get; } = new(StringComparer.Ordinal);

        /// <summary>Element id → the (index, op) that created or RE-TYPED it (review A4). The full
        /// property set of such an element is validated AFTER the batch, so a following
        /// <c>set_properties</c> in the same batch can still satisfy a required field while a
        /// genuinely missing one is still reported at the operation the user must fix.</summary>
        public Dictionary<string, (int Index, string Op)> FullPropertyElements { get; } = new(StringComparer.Ordinal);

        /// <summary>Relationship id → the (index, op) that created or re-typed it (review A4).</summary>
        public Dictionary<string, (int Index, string Op)> FullPropertyRelationships { get; } = new(StringComparer.Ordinal);
    }

    private static void ApplyOperation(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result,
        ApplyState state)
    {
        var kind = GetString(operation, "op");
        if (string.IsNullOrWhiteSpace(kind))
        {
            AddError(result, index, string.Empty, "parse", $"operations[{index}]: missing 'op'.");
            return;
        }

        if (Array.IndexOf(ViewOperations, kind) >= 0 && state.Context.Policy != ModelingOperationPolicy.Authoring)
        {
            AddError(
                result,
                index,
                kind,
                "unsupported_operation",
                $"operations[{index}] {kind}: view operations are not supported by this caller (policy 'Strict').");
            return;
        }

        var errorsBefore = result.Errors.Count;
        switch (kind)
        {
            case "add_element":
                ApplyAddElement(model, operation, index, result, state);
                break;
            case "update_element":
                ApplyUpdateElement(model, operation, index, result, state);
                break;
            case "delete_element":
                ApplyDeleteElement(model, operation, index, result);
                break;
            case "add_relationship":
                ApplyAddRelationship(model, operation, index, result, state);
                break;
            case "update_relationship":
                ApplyUpdateRelationship(model, operation, index, result, state);
                break;
            case "delete_relationship":
                ApplyDeleteRelationship(model, operation, index, result);
                break;
            case "set_properties":
                ApplySetProperties(model, operation, index, result, state);
                break;
            case "set_view_node":
                ModelingViewOperationEngine.ApplySetViewNode(model, operation, index, result, state.Context);
                break;
            case "remove_view_node":
                ModelingViewOperationEngine.ApplyRemoveViewNode(model, operation, index, result);
                break;
            case "set_view_connection":
                ModelingViewOperationEngine.ApplySetViewConnection(model, operation, index, result);
                break;
            case "remove_view_connection":
                ModelingViewOperationEngine.ApplyRemoveViewConnection(model, operation, index, result);
                break;
            default:
                AddError(result, index, kind, "unknown_op", $"operations[{index}]: unknown op '{kind}'.");
                break;
        }

        if (result.Errors.Count == errorsBefore)
        {
            result.Applied++;
        }
    }

    // ── Elements ────────────────────────────────────────────────────────────────

    private static void ApplyAddElement(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result,
        ApplyState state)
    {
        const string op = "add_element";
        var name = GetString(operation, "name");
        var semanticType = GetString(operation, "semanticType");
        if (string.IsNullOrWhiteSpace(name))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'name' is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(semanticType))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'semanticType' is required.");
            return;
        }

        var id = GetString(operation, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString();
            result.AddCreatedId(id);
        }
        else if (model.Elements.Any(element => string.Equals(element.Id, id, StringComparison.Ordinal)))
        {
            AddError(
                result,
                index,
                op,
                "duplicate_id",
                $"operations[{index}] {op}: an element with id '{id}' already exists.",
                elementId: id);
            return;
        }

        var parentId = GetString(operation, "parentId");
        if (!ValidateParentReference(model, id, parentId, index, op, result))
        {
            return;
        }

        if (!TryReadPropertyEdits(operation, index, op, result, out var setProperties, out var removedProperties))
        {
            return;
        }

        // Phase 5 (review A4): a NEW element's property set is validated IN FULL — but only AFTER the
        // whole batch, so a `set_properties` that follows this add in the SAME batch can still
        // satisfy a required field. The (index, op) is recorded so a genuinely missing field reports
        // `property_required` at THIS operation.
        state.FullPropertyElements[id] = (index, op);

        var element = new ModelingElementDto
        {
            Id = id,
            Name = name,
            SemanticType = semanticType,
            Notation = GetString(operation, "notation") ?? model.Notation,
            Description = GetString(operation, "description") ?? string.Empty,
            ParentId = parentId ?? string.Empty
        };
        ApplyPropertyValues(element.Properties, setProperties, removedProperties);
        model.Elements.Add(element);
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            state.ParentChanges[id] = (index, op);
        }
    }

    private static void ApplyUpdateElement(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result,
        ApplyState state)
    {
        const string op = "update_element";
        var id = GetString(operation, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'id' is required.");
            return;
        }

        var element = model.Elements.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (element is null)
        {
            AddError(result, index, op, "not_found", $"operations[{index}] {op}: element '{id}' not found.", elementId: id);
            return;
        }

        if (operation.ContainsKey("parentId"))
        {
            // An empty string clears the parent (the element becomes a root); any other value
            // must resolve to an existing element and must not close a cycle.
            var parentId = GetString(operation, "parentId") ?? string.Empty;
            if (!ValidateParentReference(model, id, parentId, index, op, result))
            {
                return;
            }
        }

        // The property payload is read WITHOUT mutating: a malformed 'properties' /
        // 'removedProperties' member fails the operation before any field changes (review fix,
        // phase 2 — all four collection operations accept the same optional payload).
        if (!TryReadPropertyEdits(operation, index, op, result, out var setProperties, out var removedProperties))
        {
            return;
        }

        // Phase 5: an UPDATE validates only the keys it touches. A re-typing/re-notating update also
        // schedules the DEFERRED full-set pass (review A4), which runs after the batch against the
        // NEW schema.
        if (!ModelingPropertyPayloadValidation.Validate(
                model,
                operation.ContainsKey("notation") ? GetString(operation, "notation") ?? string.Empty : element.Notation,
                operation.ContainsKey("semanticType") ? GetString(operation, "semanticType") ?? string.Empty : element.SemanticType,
                setProperties,
                removedProperties,
                fullSet: false,
                state.Context,
                index,
                op,
                result,
                existing: element.Properties,
                elementId: id))
        {
            return;
        }

        if (operation.ContainsKey("name"))
        {
            element.Name = GetString(operation, "name") ?? string.Empty;
        }

        if (operation.ContainsKey("semanticType"))
        {
            element.SemanticType = GetString(operation, "semanticType") ?? string.Empty;
        }

        if (operation.ContainsKey("description"))
        {
            element.Description = GetString(operation, "description") ?? string.Empty;
        }

        if (operation.ContainsKey("notation"))
        {
            element.Notation = GetString(operation, "notation") ?? string.Empty;
        }

        if (operation.ContainsKey("parentId"))
        {
            element.ParentId = GetString(operation, "parentId") ?? string.Empty;
            state.ParentChanges[id] = (index, op);
        }

        ApplyPropertyValues(element.Properties, setProperties, removedProperties);

        // Review A4: a re-typed/re-notated element is judged against the NEW schema in full — after
        // the batch, so the same batch can still fill the fields the new type requires.
        if (operation.ContainsKey("semanticType") || operation.ContainsKey("notation"))
        {
            state.FullPropertyElements[id] = (index, op);
        }
    }

    /// <summary>Validates a proposed ParentId edge: the parent must exist, must not be the element
    /// itself, and walking up its ancestor chain must never reach the element — a chain that does
    /// would close a parent cycle.</summary>
    private static bool ValidateParentReference(
        ModelingModelDto model,
        string elementId,
        string? parentId,
        int index,
        string opName,
        ModelingOperationResult result)
    {
        if (string.IsNullOrWhiteSpace(parentId))
        {
            return true;
        }

        if (string.Equals(parentId, elementId, StringComparison.Ordinal))
        {
            AddError(
                result,
                index,
                opName,
                "parent_cycle",
                $"operations[{index}] {opName}: element '{elementId}' cannot be its own parent (cycle).",
                elementId: elementId);
            return false;
        }

        if (!model.Elements.Any(element => string.Equals(element.Id, parentId, StringComparison.Ordinal)))
        {
            AddError(
                result,
                index,
                opName,
                "parent_not_found",
                $"operations[{index}] {opName}: parent element '{parentId}' not found.",
                elementId: parentId);
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = parentId;
        while (!string.IsNullOrEmpty(current) && seen.Add(current))
        {
            if (string.Equals(current, elementId, StringComparison.Ordinal))
            {
                AddError(
                    result,
                    index,
                    opName,
                    "parent_cycle",
                    $"operations[{index}] {opName}: setting '{parentId}' as the parent of '{elementId}' would create a parent cycle.",
                    elementId: elementId);
                return false;
            }

            current = model.Elements.FirstOrDefault(element => string.Equals(element.Id, current, StringComparison.Ordinal))?.ParentId;
        }

        return true;
    }

    private static void ApplyDeleteElement(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result)
    {
        const string op = "delete_element";
        var id = GetString(operation, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'id' is required.");
            return;
        }

        var element = model.Elements.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (element is null)
        {
            AddError(result, index, op, "not_found", $"operations[{index}] {op}: element '{id}' not found.", elementId: id);
            return;
        }

        // No silent cascade: the caller deletes the referencing relationships in the same batch.
        var referencing = model.Relationships
            .Where(relationship => string.Equals(relationship.SourceElementId, id, StringComparison.Ordinal)
                || string.Equals(relationship.TargetElementId, id, StringComparison.Ordinal))
            .Select(relationship => relationship.Id)
            .ToList();
        if (referencing.Count > 0)
        {
            AddError(
                result,
                index,
                op,
                "still_referenced",
                $"operations[{index}] {op}: element '{id}' is still referenced by relationship(s) [{string.Join(", ", referencing)}]. Delete them in the same batch first.",
                elementId: id);
            return;
        }

        // No silent cascade either way: an element that still parents other elements must be
        // reparented or deleted first — the caller orders the batch.
        var children = model.Elements
            .Where(child => string.Equals(child.ParentId, id, StringComparison.Ordinal))
            .Select(child => child.Id)
            .ToList();
        if (children.Count > 0)
        {
            AddError(
                result,
                index,
                op,
                "still_has_children",
                $"operations[{index}] {op}: element '{id}' still has child element(s) [{string.Join(", ", children)}]. Delete or reparent them in the same batch first.",
                elementId: id);
            return;
        }

        model.Elements.Remove(element);
    }

    // ── Relationships ───────────────────────────────────────────────────────────

    private static void ApplyAddRelationship(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result,
        ApplyState state)
    {
        const string op = "add_relationship";
        var relationshipType = GetString(operation, "relationshipType");
        var sourceId = GetString(operation, "sourceElementId");
        var targetId = GetString(operation, "targetElementId");
        if (string.IsNullOrWhiteSpace(relationshipType))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'relationshipType' is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'sourceElementId' and 'targetElementId' are required.");
            return;
        }

        var relationship = new ModelingRelationshipDto
        {
            Id = GetString(operation, "id") ?? string.Empty,
            RelationshipType = relationshipType,
            SourceElementId = sourceId,
            TargetElementId = targetId,
            Name = GetString(operation, "name") ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(relationship.Id)
            && model.Relationships.Any(item => string.Equals(item.Id, relationship.Id, StringComparison.Ordinal)))
        {
            AddError(
                result,
                index,
                op,
                "duplicate_id",
                $"operations[{index}] {op}: a relationship with id '{relationship.Id}' already exists.",
                relationshipId: relationship.Id);
            return;
        }

        if (!ValidateRelationship(model, relationship, state.Context, index, op, result))
        {
            return;
        }

        if (!TryReadPropertyEdits(operation, index, op, result, out var setProperties, out var removedProperties))
        {
            return;
        }

        // Phase 5 (review A4): a NEW relationship's full property set is validated AFTER the batch
        // (against the schema of its relationship type), so a following `set_properties` in the same
        // batch still satisfies a required field.
        if (string.IsNullOrWhiteSpace(relationship.Id))
        {
            relationship.Id = Guid.NewGuid().ToString();
            result.AddCreatedId(relationship.Id);
        }

        state.FullPropertyRelationships[relationship.Id] = (index, op);

        ApplyPropertyValues(relationship.Properties, setProperties, removedProperties);
        model.Relationships.Add(relationship);
    }

    private static void ApplyUpdateRelationship(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result,
        ApplyState state)
    {
        const string op = "update_relationship";
        var id = GetString(operation, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'id' is required.");
            return;
        }

        var relationship = model.Relationships.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (relationship is null)
        {
            AddError(result, index, op, "not_found", $"operations[{index}] {op}: relationship '{id}' not found.", relationshipId: id);
            return;
        }

        // Validate against the proposed post-update state without mutating on failure.
        var proposed = new ModelingRelationshipDto
        {
            Id = relationship.Id,
            RelationshipType = operation.ContainsKey("relationshipType") ? GetString(operation, "relationshipType") ?? string.Empty : relationship.RelationshipType,
            SourceElementId = operation.ContainsKey("sourceElementId") ? GetString(operation, "sourceElementId") ?? string.Empty : relationship.SourceElementId,
            TargetElementId = operation.ContainsKey("targetElementId") ? GetString(operation, "targetElementId") ?? string.Empty : relationship.TargetElementId,
            Name = operation.ContainsKey("name") ? GetString(operation, "name") ?? string.Empty : relationship.Name
        };

        if (!ValidateRelationship(model, proposed, state.Context, index, op, result))
        {
            return;
        }

        if (!TryReadPropertyEdits(operation, index, op, result, out var setProperties, out var removedProperties))
        {
            return;
        }

        if (!ModelingPropertyPayloadValidation.Validate(
                model,
                model.Notation,
                proposed.RelationshipType,
                setProperties,
                removedProperties,
                fullSet: false,
                state.Context,
                index,
                op,
                result,
                existing: relationship.Properties,
                relationshipId: id))
        {
            return;
        }

        relationship.RelationshipType = proposed.RelationshipType;
        relationship.SourceElementId = proposed.SourceElementId;
        relationship.TargetElementId = proposed.TargetElementId;
        relationship.Name = proposed.Name;
        ApplyPropertyValues(relationship.Properties, setProperties, removedProperties);

        // Review A4: a re-typed relationship is judged against the NEW schema in full, after the batch.
        if (operation.ContainsKey("relationshipType"))
        {
            state.FullPropertyRelationships[id] = (index, op);
        }
    }

    private static void ApplyDeleteRelationship(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result)
    {
        const string op = "delete_relationship";
        var id = GetString(operation, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'id' is required.");
            return;
        }

        var relationship = model.Relationships.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (relationship is null)
        {
            AddError(result, index, op, "not_found", $"operations[{index}] {op}: relationship '{id}' not found.", relationshipId: id);
            return;
        }

        model.Relationships.Remove(relationship);
    }

    /// <summary>
    /// Validates ONE relationship against the model — both endpoints exist AND the notation rules
    /// accept it — and returns the FIRST error, or <c>null</c> when the relationship is acceptable.
    /// Public (phase 3 review, A2) so the editor's type picker and its rejection text probe
    /// candidate types through the SAME code path the commit uses: the picker can never offer a
    /// type the batch would refuse, and a rejection always carries the rules' own message and
    /// suggested fix. The message is the rule text WITHOUT the <c>operations[i] op:</c> prefix the
    /// batch path adds (§6.4 "one semantics, same errors").
    /// <para>
    /// The viewpoint comes from <see cref="ModelingOperationContext.ViewpointKey"/> when the caller
    /// declares one (an editor whose host filters by viewpoint), else from the target view
    /// (<c>Views[0]</c> — the view authoring operations default to), else <c>""</c> — the 2.10.0
    /// behavior. A caller that passes nothing therefore keeps the MCP verdict byte-identical.
    /// </para>
    /// </summary>
    /// <param name="model">The model the relationship belongs to (endpoint lookup + rules context).</param>
    /// <param name="relationship">The proposed relationship (a candidate type may carry no id).</param>
    /// <param name="context">Rules + notation + viewpoint; null means "structure checks only".</param>
    public static ModelingOperationError? ValidateRelationship(
        ModelingModelDto model,
        ModelingRelationshipDto relationship,
        ModelingOperationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(relationship);

        context ??= new ModelingOperationContext();

        var source = model.Elements.FirstOrDefault(element => string.Equals(element.Id, relationship.SourceElementId, StringComparison.Ordinal));
        if (source is null)
        {
            return new ModelingOperationError
            {
                Index = -1,
                Op = "add_relationship",
                Code = "not_found",
                Message = $"source element '{relationship.SourceElementId}' not found.",
                ElementId = relationship.SourceElementId,
                RelationshipId = KnownRelationshipId(relationship)
            };
        }

        var target = model.Elements.FirstOrDefault(element => string.Equals(element.Id, relationship.TargetElementId, StringComparison.Ordinal));
        if (target is null)
        {
            return new ModelingOperationError
            {
                Index = -1,
                Op = "add_relationship",
                Code = "not_found",
                Message = $"target element '{relationship.TargetElementId}' not found.",
                ElementId = relationship.TargetElementId,
                RelationshipId = KnownRelationshipId(relationship)
            };
        }

        var rules = context.RelationshipRules;
        if (rules is null)
        {
            return null;
        }

        // The CALLER's notation wins when it declares one (an editor with a notation override);
        // otherwise the model's own notation, then the element's (the 2.10.0 resolution).
        var notationKey = !string.IsNullOrWhiteSpace(context.Notation)
            ? context.Notation!
            : !string.IsNullOrWhiteSpace(model.Notation) ? model.Notation : source.Notation;
        var viewpointKey = context.ViewpointKey ?? model.Views.FirstOrDefault()?.ViewpointKey ?? string.Empty;
        var ruleResult = rules.ValidateRelationship(new ModelingRelationshipRuleContext
        {
            NotationKey = notationKey,
            ViewpointKey = viewpointKey,
            Relationship = relationship,
            SourceElement = source,
            TargetElement = target,
            ElementsById = model.Elements
                .Where(element => !string.IsNullOrEmpty(element.Id))
                .GroupBy(element => element.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
        });

        if (ruleResult.IsValid)
        {
            return null;
        }

        var fix = string.IsNullOrWhiteSpace(ruleResult.SuggestedFix) ? string.Empty : $" {ruleResult.SuggestedFix}";
        return new ModelingOperationError
        {
            Index = -1,
            Op = "add_relationship",
            Code = "invalid_relationship",
            Message = $"'{relationship.RelationshipType}' from '{source.SemanticType}' to '{target.SemanticType}' is not valid for notation '{notationKey}'. {ruleResult.Message}{fix}".TrimEnd(),
            SuggestedFix = string.IsNullOrWhiteSpace(ruleResult.SuggestedFix) ? null : ruleResult.SuggestedFix,
            ElementId = source.Id,
            RelationshipId = KnownRelationshipId(relationship)
        };
    }

    /// <summary>Records the public validation's first error on the batch result with the
    /// <c>operations[i] op:</c> prefix the 2.10.0 message format carries; false = the caller must
    /// not write anything.</summary>
    private static bool ValidateRelationship(
        ModelingModelDto model,
        ModelingRelationshipDto relationship,
        ModelingOperationContext context,
        int index,
        string opName,
        ModelingOperationResult result)
    {
        var error = ValidateRelationship(model, relationship, context);
        if (error is null)
        {
            return true;
        }

        AddError(
            result,
            index,
            opName,
            error.Code,
            $"operations[{index}] {opName}: {error.Message}",
            suggestedFix: error.SuggestedFix,
            elementId: error.ElementId,
            relationshipId: error.RelationshipId);
        return false;
    }

    private static string? KnownRelationshipId(ModelingRelationshipDto relationship)
        => string.IsNullOrWhiteSpace(relationship.Id) ? null : relationship.Id;

    // ── Properties ──────────────────────────────────────────────────────────────

    private static void ApplySetProperties(
        ModelingModelDto model,
        JsonObject operation,
        int index,
        ModelingOperationResult result,
        ApplyState state)
    {
        const string op = "set_properties";
        var id = GetString(operation, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            AddError(result, index, op, "missing_field", $"operations[{index}] {op}: 'id' is required.");
            return;
        }

        var element = model.Elements.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        var relationship = element is null
            ? model.Relationships.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
            : null;
        if (element is null && relationship is null)
        {
            AddError(
                result,
                index,
                op,
                "not_found",
                $"operations[{index}] {op}: element or relationship '{id}' not found.",
                elementId: id);
            return;
        }

        var properties = RequireProperties(operation, index, op, result);
        if (properties is null)
        {
            return;
        }

        if (!TryReadRemovedProperties(operation, index, op, result, out var removedProperties))
        {
            return;
        }

        // Phase 5: only the touched keys are validated (an unrelated legacy value never blocks a
        // targeted fix). A RELATIONSHIP is judged with its relationship type as the semantic type.
        var notation = element is not null ? element.Notation : model.Notation;
        var semanticType = element is not null ? element.SemanticType : relationship!.RelationshipType;
        if (!ModelingPropertyPayloadValidation.Validate(
                model,
                notation,
                semanticType,
                properties,
                removedProperties,
                fullSet: false,
                state.Context,
                index,
                op,
                result,
                existing: element is not null ? element.Properties : relationship!.Properties,
                elementId: element?.Id,
                relationshipId: relationship?.Id))
        {
            return;
        }

        ApplyPropertyValues(element is not null ? element.Properties : relationship!.Properties, properties, removedProperties);
    }

    /// <summary>Reads the optional <c>properties</c> / <c>removedProperties</c> members of an
    /// operation WITHOUT mutating anything. Returns false when the members are malformed (the error
    /// is already recorded and the caller must not mutate the model).</summary>
    private static bool TryReadPropertyEdits(
        JsonObject operation,
        int index,
        string opName,
        ModelingOperationResult result,
        out JsonObject? setProperties,
        out List<string>? removedProperties)
    {
        setProperties = null;
        if (operation.TryGetPropertyValue("properties", out var propertiesNode) && propertiesNode is not null)
        {
            if (propertiesNode is not JsonObject propertiesObject)
            {
                removedProperties = null;
                AddError(result, index, opName, "property_type", $"operations[{index}] {opName}: 'properties' must be a JSON object.");
                return false;
            }

            setProperties = propertiesObject;
        }

        return TryReadRemovedProperties(operation, index, opName, result, out removedProperties);
    }

    private static JsonObject? RequireProperties(
        JsonObject operation,
        int index,
        string opName,
        ModelingOperationResult result)
    {
        if (!operation.TryGetPropertyValue("properties", out var propertiesNode) || propertiesNode is null)
        {
            AddError(result, index, opName, "missing_field", $"operations[{index}] {opName}: 'properties' is required.");
            return null;
        }

        if (propertiesNode is not JsonObject properties)
        {
            AddError(result, index, opName, "property_type", $"operations[{index}] {opName}: 'properties' must be a JSON object.");
            return null;
        }

        return properties;
    }

    private static bool TryReadRemovedProperties(
        JsonObject operation,
        int index,
        string opName,
        ModelingOperationResult result,
        out List<string>? removedProperties)
    {
        removedProperties = null;
        if (!operation.TryGetPropertyValue("removedProperties", out var removedNode) || removedNode is null)
        {
            return true;
        }

        if (removedNode is not JsonArray removedArray)
        {
            AddError(result, index, opName, "property_type", $"operations[{index}] {opName}: 'removedProperties' must be a JSON array of strings.");
            return false;
        }

        removedProperties = [];
        foreach (var item in removedArray)
        {
            if (item is null)
            {
                continue;
            }

            if (item is not JsonValue || item.GetValueKind() != JsonValueKind.String)
            {
                AddError(result, index, opName, "property_type", $"operations[{index}] {opName}: 'removedProperties' must be a JSON array of strings.");
                return false;
            }

            removedProperties.Add(item.GetValue<string>());
        }

        return true;
    }

    private static void ApplyPropertyValues(
        Dictionary<string, JsonElement> target,
        JsonObject? setProperties,
        List<string>? removedProperties)
    {
        if (setProperties is not null)
        {
            foreach (var property in setProperties)
            {
                target[property.Key] = JsonSerializer.SerializeToElement<JsonNode?>(property.Value);
            }
        }

        if (removedProperties is not null)
        {
            foreach (var key in removedProperties)
            {
                target.Remove(key);
            }
        }
    }

    // ── Post-batch validation (Authoring) ───────────────────────────────────────

    /// <summary>
    /// In the <see cref="ModelingOperationPolicy.Authoring"/> policy the engine re-validates, after
    /// the last operation, every relationship incident to an element whose <c>ParentId</c> changed
    /// in the batch (§3.2 rule 3): a lane move can invalidate a flow the batch never touched. The
    /// <see cref="ModelingOperationPolicy.Strict"/> (MCP) policy keeps the per-operation validation
    /// only, so 2.10.0 MCP behavior stays byte-identical.
    /// </summary>
    private static void ValidateRelationshipsAfterParentChanges(
        ModelingModelDto model,
        ModelingOperationResult result,
        ApplyState state)
    {
        if (state.Context.Policy != ModelingOperationPolicy.Authoring
            || state.Context.RelationshipRules is null
            || state.ParentChanges.Count == 0)
        {
            return;
        }

        foreach (var relationship in model.Relationships)
        {
            if (!state.ParentChanges.TryGetValue(relationship.SourceElementId, out var change)
                && !state.ParentChanges.TryGetValue(relationship.TargetElementId, out change))
            {
                continue;
            }

            // Already reported by the operation that touched this relationship — do not duplicate.
            if (result.Errors.Any(error => string.Equals(error.RelationshipId, relationship.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            ValidateRelationship(
                model,
                relationship,
                state.Context,
                change.Index,
                change.Op,
                result);
        }
    }

    // ── Deferred full property-set validation (phase 5, review A4) ─────────────

    /// <summary>
    /// Phase 5 review (A4): a NEW or RE-TYPED element/relationship is judged against its schema IN
    /// FULL — after the whole batch applied, at the operation index that created/re-typed it. The
    /// per-operation pass keeps touching only the keys each operation names; deferring the full set
    /// is what lets <c>[add_element, set_properties]</c> in ONE batch succeed (the add alone would
    /// have failed its required field before the follow-up ever ran) while a genuinely missing
    /// required field still reports <c>property_required</c> at the ADD's index.
    /// <para>
    /// An item the same batch removed again is skipped; an item that already carries a reported error
    /// is skipped too, so one failing batch does not stack a second, derived finding on it.
    /// </para>
    /// </summary>
    private static void ValidateDeferredPropertySets(
        ModelingModelDto model,
        ModelingOperationResult result,
        ApplyState state)
    {
        if (state.Context.ResolvePropertySchema is null
            || (state.FullPropertyElements.Count == 0 && state.FullPropertyRelationships.Count == 0))
        {
            return;
        }

        foreach (var (id, change) in state.FullPropertyElements)
        {
            var element = model.Elements.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (element is null || AlreadyReported(result, elementId: id))
            {
                continue;
            }

            ModelingPropertyPayloadValidation.ValidateFullSet(
                model,
                string.IsNullOrWhiteSpace(element.Notation) ? model.Notation : element.Notation,
                element.SemanticType,
                element.Properties,
                state.Context,
                change.Index,
                change.Op,
                result,
                elementId: id);
        }

        foreach (var (id, change) in state.FullPropertyRelationships)
        {
            var relationship = model.Relationships.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (relationship is null || AlreadyReported(result, relationshipId: id))
            {
                continue;
            }

            ModelingPropertyPayloadValidation.ValidateFullSet(
                model,
                model.Notation,
                relationship.RelationshipType,
                relationship.Properties,
                state.Context,
                change.Index,
                change.Op,
                result,
                relationshipId: id);
        }
    }

    private static bool AlreadyReported(ModelingOperationResult result, string? elementId = null, string? relationshipId = null)
        => result.Errors.Any(error =>
            (elementId is not null && string.Equals(error.ElementId, elementId, StringComparison.Ordinal))
            || (relationshipId is not null && string.Equals(error.RelationshipId, relationshipId, StringComparison.Ordinal)));

    // ── JSON helpers ────────────────────────────────────────────────────────────

    internal static string? GetString(JsonObject operation, string key)
    {
        if (!operation.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<string?>();
        }
        catch (InvalidOperationException)
        {
            return node.ToString();
        }
        catch (FormatException)
        {
            return node.ToString();
        }
    }

    internal static double? GetDouble(JsonObject operation, string key)
    {
        if (!operation.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<double>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal static void AddError(
        ModelingOperationResult result,
        int index,
        string op,
        string code,
        string message,
        string? suggestedFix = null,
        string? elementId = null,
        string? relationshipId = null,
        string? propertyKey = null)
        => result.AddError(new ModelingOperationError
        {
            Index = index,
            Op = op,
            Code = code,
            Message = message,
            SuggestedFix = suggestedFix,
            ElementId = elementId,
            RelationshipId = relationshipId,
            PropertyKey = propertyKey
        });
}
