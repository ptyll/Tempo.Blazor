using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// In-memory demo writer (Fáze 14): persists inspector edits against an in-memory copy of the
/// demo model so the demo page can exercise the full edit → save → reload loop without a
/// backend. Version checks are simulated with a configurable <see cref="SourceVersion"/>, which
/// the <c>?scenario=save-conflict</c> demo route bumps so the writer reports the conflict path.
/// Registered automatically by <c>AddTempoBlazorModeling</c> for the
/// <see cref="DemoModelingModelProvider"/> key.
/// </summary>
public sealed class DemoModelingModelWriter : IModelingModelWriter
{
    private readonly object _gate = new();
    private ModelingModelDto? _persisted;

    /// <inheritdoc />
    public string ProviderKey => DemoModelingModelProvider.ProviderKeyValue;

    /// <summary>The source version the writer pretends the backend is on. The demo save-conflict
    /// scenario raises it so the next save reports <see cref="ModelingModelSaveResult.IsConflict"/>.</summary>
    public string SourceVersion { get; set; } = "2026.06";

    /// <summary>Armed by the demo <c>?scenario=save-conflict</c> route: the NEXT save reports
    /// the conflict path (the source moved on from the version the editor based its edit on)
    /// and the flag disarms itself, so a later save succeeds.</summary>
    public static bool ConflictArmed { get; set; }

    /// <summary>The persisted model (the last successful save or the initial demo model).</summary>
    public ModelingModelDto PersistedModel
    {
        get
        {
            lock (_gate)
            {
                return _persisted ??= Clone(DemoModelingModelProvider.CreateModel());
            }
        }
    }

    /// <summary>How many change sets were persisted (demo diagnostics).</summary>
    public int SaveCount { get; private set; }

    /// <inheritdoc />
    public Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(changes.ModelId, DemoModelingModelProvider.ModelIdValue, StringComparison.Ordinal))
        {
            return Task.FromResult(new ModelingModelSaveResult
            {
                Succeeded = false,
                Message = $"Unknown model '{changes.ModelId}'."
            });
        }

        lock (_gate)
        {
            var expectedVersion = ConflictArmed ? $"{SourceVersion}-next" : SourceVersion;
            ConflictArmed = false;
            if (!string.Equals(changes.BaseVersion, expectedVersion, StringComparison.Ordinal))
            {
                // Simulated concurrent write: the source moved on from the version the model was
                // loaded with. The editor shows the conflict alert with a reload action.
                return Task.FromResult(new ModelingModelSaveResult
                {
                    Succeeded = false,
                    IsConflict = true,
                    Message = $"The model changed in the meantime (source version {SourceVersion}, edit was based on {changes.BaseVersion}).",
                    Model = Clone(PersistedModel)
                });
            }

            var persisted = Clone(PersistedModel);
            foreach (var elementChange in changes.Elements)
            {
                var element = persisted.Elements.FirstOrDefault(item => string.Equals(item.Id, elementChange.ElementId, StringComparison.Ordinal));
                if (element is null)
                {
                    continue;
                }

                if (elementChange.Name is not null)
                {
                    element.Name = elementChange.Name;
                }

                if (elementChange.Description is not null)
                {
                    element.Description = elementChange.Description;
                }

                if (elementChange.SetProperties is not null)
                {
                    foreach (var property in elementChange.SetProperties)
                    {
                        element.Properties[property.Key] = property.Value.Clone();
                    }
                }

                if (elementChange.RemovedProperties is not null)
                {
                    foreach (var key in elementChange.RemovedProperties)
                    {
                        element.Properties.Remove(key);
                    }
                }
            }

            foreach (var relationshipChange in changes.Relationships)
            {
                var relationship = persisted.Relationships.FirstOrDefault(item => string.Equals(item.Id, relationshipChange.RelationshipId, StringComparison.Ordinal));
                if (relationship is null)
                {
                    continue;
                }

                if (relationshipChange.Name is not null)
                {
                    relationship.Name = relationshipChange.Name;
                }

                if (relationshipChange.SetProperties is not null)
                {
                    foreach (var property in relationshipChange.SetProperties)
                    {
                        relationship.Properties[property.Key] = property.Value.Clone();
                    }
                }

                if (relationshipChange.RemovedProperties is not null)
                {
                    foreach (var key in relationshipChange.RemovedProperties)
                    {
                        relationship.Properties.Remove(key);
                    }
                }
            }

            SourceVersion = $"2026.06-{++SaveCount}";
            _persisted = persisted;

            return Task.FromResult(new ModelingModelSaveResult
            {
                Succeeded = true,
                Model = Clone(persisted)
            });
        }
    }

    private static ModelingModelDto Clone(ModelingModelDto model)
    {
        var copy = new ModelingModelDto
        {
            Id = model.Id,
            Title = model.Title,
            Notation = model.Notation,
            SupportedNotations = [.. model.SupportedNotations],
            Metadata = new ModelingMetadataDto
            {
                SourceSystem = model.Metadata.SourceSystem,
                SourceVersion = model.Metadata.SourceVersion,
                LoadedAt = model.Metadata.LoadedAt,
                IsFresh = model.Metadata.IsFresh
            }
        };

        foreach (var element in model.Elements)
        {
            copy.Elements.Add(new ModelingElementDto
            {
                Id = element.Id,
                SourceId = element.SourceId,
                SourceType = element.SourceType,
                SourcePath = element.SourcePath,
                Notation = element.Notation,
                SemanticType = element.SemanticType,
                Name = element.Name,
                Description = element.Description,
                ParentId = element.ParentId,
                Properties = element.Properties.ToDictionary(property => property.Key, property => property.Value.Clone(), StringComparer.Ordinal),
                Tags = [.. element.Tags],
                Governance = new ModelingGovernanceDto
                {
                    TrustLevel = element.Governance.TrustLevel,
                    ReviewState = element.Governance.ReviewState,
                    SyncState = element.Governance.SyncState,
                    DataSource = element.Governance.DataSource
                }
            });
        }

        foreach (var relationship in model.Relationships)
        {
            copy.Relationships.Add(new ModelingRelationshipDto
            {
                Id = relationship.Id,
                SourceId = relationship.SourceId,
                SourceType = relationship.SourceType,
                SourceElementId = relationship.SourceElementId,
                TargetElementId = relationship.TargetElementId,
                RelationshipType = relationship.RelationshipType,
                Name = relationship.Name,
                Properties = relationship.Properties.ToDictionary(property => property.Key, property => property.Value.Clone(), StringComparer.Ordinal),
                Tags = [.. relationship.Tags]
            });
        }

        copy.Views.AddRange(model.Views.Select(view => new ModelingViewDto
        {
            Id = view.Id,
            Name = view.Name,
            Notation = view.Notation,
            ViewpointKey = view.ViewpointKey,
            Nodes = view.Nodes.Select(node => new ModelingViewNodeDto
            {
                ElementId = node.ElementId,
                X = node.X,
                Y = node.Y,
                Width = node.Width,
                Height = node.Height,
                ParentNodeId = node.ParentNodeId
            }).ToList(),
            Connections = view.Connections.Select(connection => new ModelingViewConnectionDto
            {
                RelationshipId = connection.RelationshipId,
                SourceNodeId = connection.SourceNodeId,
                TargetNodeId = connection.TargetNodeId,
                Waypoints = connection.Waypoints.Select(waypoint => new ModelingViewWaypointDto { X = waypoint.X, Y = waypoint.Y }).ToList()
            }).ToList()
        }));

        copy.Issues.AddRange(model.Issues);
        return copy;
    }
}
