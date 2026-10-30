using System.Text.Json;
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
/// loaded it from (Fáze 14, decision <c>MODELING-INSPECTOR-EDITING</c>).
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

    /// <summary>Persists the supplied change set against the source system.
    /// <paramref name="changes.BaseVersion"/> carries the <see cref="ModelingMetadataDto.SourceVersion"/>
    /// the edited model was loaded with — the writer compares it against the current source
    /// version and returns <see cref="ModelingModelSaveResult.IsConflict"/> when the source moved on.</summary>
    /// <param name="changes">The aggregated changes to persist (only changed fields are set).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The save outcome; a failed save carries a message, a conflict also carries the
    /// <c>IsConflict</c> flag and may carry the freshly loaded model.</returns>
    Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken);
}

/// <summary>An aggregated change set passed to <see cref="IModelingModelWriter.SaveChangesAsync"/>.</summary>
public sealed class ModelingModelChangeSet
{
    private string _modelId = string.Empty;
    private string _providerKey = string.Empty;
    private string _baseVersion = string.Empty;
    private List<ModelingElementChange> _elements = [];
    private List<ModelingRelationshipChange> _relationships = [];

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
}

/// <summary>A single element's changed fields. A <c>null</c> property means "unchanged" —
/// the writer must be able to distinguish "not changed" from "changed to null/empty".</summary>
public sealed class ModelingElementChange
{
    private string _elementId = string.Empty;
    private List<string>? _removedProperties;

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
    public List<string>? RemovedProperties
    {
        get => _removedProperties;
        set => _removedProperties = value ?? [];
    }
}

/// <summary>A single relationship's changed fields. A <c>null</c> property means "unchanged".</summary>
public sealed class ModelingRelationshipChange
{
    private string _relationshipId = string.Empty;
    private List<string>? _removedProperties;

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
    public List<string>? RemovedProperties
    {
        get => _removedProperties;
        set => _removedProperties = value ?? [];
    }
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
