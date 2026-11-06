using System.Globalization;

namespace Tempo.Blazor.Modeling;

/// <summary>
/// Translates a v2 <see cref="ModelingModelChangeSet"/> into the ONE ordered
/// <see cref="ModelingOperationBatch"/> the shared <see cref="ModelingOperationEngine"/> applies
/// (decision <c>MODELING-AUTHORING-CHANGESET</c>).
/// <para>
/// The translation lives in Abstractions — next to the change-set DTOs and the engine — so every
/// writer (the demo writer, a host writer, a future server-side writer) executes the SAME
/// application order instead of re-deriving it: added elements (parents first) → content/parent
/// updates of existing elements → removed relationships → added relationships → relationship
/// updates → removed elements (descendants first) → view (layout) changes. The engine never
/// cascades a removal, so the change set must carry the expansion the editor produced.
/// </para>
/// <para>
/// The translator also rejects a self-contradictory change set BEFORE anything is applied: when the
/// same id is named in more than one of the added/updated/removed sections of ONE kind (element or
/// relationship) the change set has no defined outcome, so it is reported as a single
/// <c>duplicate_id</c> error per offending id and the returned batch is empty.
/// </para>
/// </summary>
public static class ModelingChangeSetTranslator
{
    /// <summary>
    /// Translates <paramref name="changes"/> into an ordered operation batch, without touching any
    /// model. Check <see cref="ModelingChangeSetTranslation.Success"/> first: a change set that
    /// names one id in several sections produces <see cref="ModelingChangeSetTranslation.Errors"/>
    /// and an EMPTY batch, so a caller that ignores the flag applies nothing instead of a
    /// half-defined batch.
    /// </summary>
    /// <param name="changes">The aggregated change set.</param>
    /// <returns>The translated batch plus the section-level errors (none on success).</returns>
    public static ModelingChangeSetTranslation ToOperationBatch(ModelingModelChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var translation = new ModelingChangeSetTranslation();
        ValidateSections(changes, translation);
        if (!translation.Success)
        {
            return translation;
        }

        var operations = translation.Batch.Operations;

        // 1. Added elements, in list order (a parent must precede its children).
        foreach (var element in changes.AddedElements)
        {
            operations.Add(new ModelingOperation
            {
                Op = "add_element",
                Id = element.Id,
                Name = element.Name,
                SemanticType = element.SemanticType,
                Notation = string.IsNullOrEmpty(element.Notation) ? null : element.Notation,
                Description = string.IsNullOrEmpty(element.Description) ? null : element.Description,
                ParentId = element.ParentId,
                Properties = element.Properties.Count > 0 ? element.Properties : null
            });
        }

        // 2. Content / parent updates of EXISTING elements. The properties ride on the SAME
        //    update_element op (the engine reads an optional payload there too), so a writer never
        //    needs a follow-up set_properties op.
        foreach (var change in changes.Elements)
        {
            if (change.Name is null && change.Description is null && change.ParentId is null
                && change.SetProperties is null && change.RemovedProperties is null)
            {
                continue;
            }

            operations.Add(new ModelingOperation
            {
                Op = "update_element",
                Id = change.ElementId,
                Name = change.Name,
                Description = change.Description,
                ParentId = change.ParentId,
                Properties = change.SetProperties,
                RemovedProperties = change.RemovedProperties
            });
        }

        // 3. Removed relationships (before the elements they refer to).
        foreach (var relationshipId in changes.RemovedRelationshipIds)
        {
            operations.Add(new ModelingOperation { Op = "delete_relationship", Id = relationshipId });
        }

        // 4. Added relationships (endpoints may be existing or added elements).
        foreach (var relationship in changes.AddedRelationships)
        {
            operations.Add(new ModelingOperation
            {
                Op = "add_relationship",
                Id = relationship.Id,
                RelationshipType = relationship.RelationshipType,
                SourceElementId = relationship.SourceElementId,
                TargetElementId = relationship.TargetElementId,
                Name = string.IsNullOrEmpty(relationship.Name) ? null : relationship.Name,
                Properties = relationship.Properties.Count > 0 ? relationship.Properties : null
            });
        }

        // 5. Content / re-target updates of EXISTING relationships.
        foreach (var change in changes.Relationships)
        {
            if (change.Name is null && change.SourceElementId is null && change.TargetElementId is null
                && change.SetProperties is null && change.RemovedProperties is null)
            {
                continue;
            }

            operations.Add(new ModelingOperation
            {
                Op = "update_relationship",
                Id = change.RelationshipId,
                Name = change.Name,
                SourceElementId = change.SourceElementId,
                TargetElementId = change.TargetElementId,
                Properties = change.SetProperties,
                RemovedProperties = change.RemovedProperties
            });
        }

        // 6. Removed elements (the editor lists them descendants first).
        foreach (var elementId in changes.RemovedElementIds)
        {
            operations.Add(new ModelingOperation { Op = "delete_element", Id = elementId });
        }

        // 7. Layout — the only section the engine gates behind the Authoring policy.
        foreach (var viewChange in changes.ViewChanges)
        {
            var viewId = string.IsNullOrEmpty(viewChange.ViewId) ? null : viewChange.ViewId;

            foreach (var node in viewChange.Nodes)
            {
                operations.Add(new ModelingOperation
                {
                    Op = "set_view_node",
                    ViewId = viewId,
                    ElementId = node.ElementId,
                    X = node.X,
                    Y = node.Y,
                    Width = node.Width,
                    Height = node.Height,
                    ParentNodeId = node.ParentNodeId
                });
            }

            foreach (var elementId in viewChange.RemovedNodeElementIds)
            {
                operations.Add(new ModelingOperation { Op = "remove_view_node", ViewId = viewId, ElementId = elementId });
            }

            foreach (var connection in viewChange.Connections)
            {
                operations.Add(new ModelingOperation
                {
                    Op = "set_view_connection",
                    ViewId = viewId,
                    RelationshipId = connection.RelationshipId,
                    Waypoints = connection.Waypoints
                });
            }

            foreach (var relationshipId in viewChange.RemovedConnectionRelationshipIds)
            {
                operations.Add(new ModelingOperation
                {
                    Op = "remove_view_connection",
                    ViewId = viewId,
                    RelationshipId = relationshipId
                });
            }
        }

        return translation;
    }

    /// <summary>
    /// Reports every id that appears in more than one of the added/updated/removed sections of its
    /// kind. The check is ORDINAL (model ids are opaque strings) and runs before any operation is
    /// built, so a contradictory change set applies nothing at all.
    /// </summary>
    private static void ValidateSections(ModelingModelChangeSet changes, ModelingChangeSetTranslation translation)
    {
        var duplicateElementIds = FindDuplicateIds(
        [
            ("added", changes.AddedElements.Select(element => element.Id)),
            ("updated", changes.Elements.Select(change => change.ElementId)),
            ("removed", changes.RemovedElementIds)
        ]);

        var duplicateRelationshipIds = FindDuplicateIds(
        [
            ("added", changes.AddedRelationships.Select(relationship => relationship.Id)),
            ("updated", changes.Relationships.Select(change => change.RelationshipId)),
            ("removed", changes.RemovedRelationshipIds)
        ]);

        foreach (var id in duplicateElementIds.Concat(duplicateRelationshipIds))
        {
            translation.AddError(new ModelingOperationError
            {
                Index = -1,
                Code = "duplicate_id",
                Message = string.Format(
                    CultureInfo.InvariantCulture,
                    "changes: id '{0}' appears in more than one of added/updated/removed.",
                    id)
            });
        }
    }

    /// <summary>Ids present in at least two of the supplied sections, in first-seen order.</summary>
    private static List<string> FindDuplicateIds((string Section, IEnumerable<string> Ids)[] sections)
    {
        var sectionsById = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var firstSeen = new List<string>();
        foreach (var (section, ids) in sections)
        {
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!sectionsById.TryGetValue(id, out var owners))
                {
                    sectionsById[id] = owners = new HashSet<string>(StringComparer.Ordinal);
                    firstSeen.Add(id);
                }

                owners.Add(section);
            }
        }

        return [.. firstSeen.Where(id => sectionsById[id].Count > 1)];
    }
}

/// <summary>
/// The outcome of <see cref="ModelingChangeSetTranslator.ToOperationBatch"/>: the ordered batch a
/// writer feeds to <see cref="ModelingOperationEngine.Apply"/> plus the section-level errors that
/// make the change set self-contradictory.
/// </summary>
public sealed class ModelingChangeSetTranslation
{
    /// <summary>The translated batch; EMPTY when <see cref="Errors"/> is non-empty, so a caller that
    /// ignores <see cref="Success"/> applies nothing.</summary>
    public ModelingOperationBatch Batch { get; } = new();

    /// <summary>The section-level errors (index <c>-1</c>, code <c>duplicate_id</c>); empty on
    /// success.</summary>
    public List<ModelingOperationError> Errors { get; } = [];

    /// <summary>Whether the change set translated into a batch that may be applied.</summary>
    public bool Success => Errors.Count == 0;

    internal void AddError(ModelingOperationError error) => Errors.Add(error);
}
