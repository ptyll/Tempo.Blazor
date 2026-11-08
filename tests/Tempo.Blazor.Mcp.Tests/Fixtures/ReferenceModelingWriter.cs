using System.Globalization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Tests.Fixtures;

/// <summary>
/// The hosting pattern docs/modeling-hosting-guide.md documents, reduced to one file: a writer that
/// advertises <see cref="ModelingModelWriterCapabilities.All"/>, checks the optimistic-concurrency
/// token, translates the v2 change set with the shared <see cref="ModelingChangeSetTranslator"/> and
/// applies it through the shared <see cref="ModelingOperationEngine"/> under the
/// <see cref="ModelingOperationPolicy.Authoring"/> policy with the shared
/// <see cref="ModelingPropertySchemaResolver"/> preloaded.
/// <para>
/// The phase-7 parity suite drives THIS writer as the editor's SAVE path: the MCP tool applies one
/// batch, the harness saves the equivalent <see cref="ModelingModelChangeSet"/>, and the two
/// resulting models and error lists must be identical ("one semantics, same errors").
/// </para>
/// </summary>
public sealed class ReferenceModelingWriter : IModelingModelWriter
{
    private readonly IModelingRelationshipRulesProvider? _relationshipRules;
    private readonly IModelingViewpointRulesProvider? _viewpointRules;
    private readonly IModelingPropertySchemaProvider? _schemas;
    private string _version;
    private int _saves;

    /// <summary>Creates a writer over the given initial model (it is cloned, never mutated).</summary>
    public ReferenceModelingWriter(
        ModelingModelDto initialModel,
        IModelingRelationshipRulesProvider? relationshipRules = null,
        IModelingViewpointRulesProvider? viewpointRules = null,
        IModelingPropertySchemaProvider? propertySchemas = null,
        string providerKey = "tests.reference-writer",
        string initialVersion = "v1")
    {
        PersistedModel = ModelingOperationEngine.Clone(initialModel);
        PersistedModel.Metadata.SourceVersion = initialVersion;
        _relationshipRules = relationshipRules;
        _viewpointRules = viewpointRules;
        _schemas = propertySchemas;
        ProviderKey = providerKey;
        _version = initialVersion;
    }

    /// <inheritdoc />
    public string ProviderKey { get; }

    /// <inheritdoc />
    public ModelingModelWriterCapabilities Capabilities => ModelingModelWriterCapabilities.All;

    /// <summary>The persisted model after the last successful save.</summary>
    public ModelingModelDto PersistedModel { get; private set; }

    /// <summary>The version the writer currently reports (it advances on every successful save).</summary>
    public string Version => _version;

    /// <summary>How many change sets were persisted.</summary>
    public int SaveCount => _saves;

    /// <inheritdoc />
    public async Task<ModelingModelSaveResult> SaveChangesAsync(
        ModelingModelChangeSet changes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(changes.BaseVersion, _version, StringComparison.Ordinal))
        {
            return new ModelingModelSaveResult
            {
                Succeeded = false,
                IsConflict = true,
                Message = $"The model moved from '{changes.BaseVersion}' to '{_version}'.",
                Model = ModelingOperationEngine.Clone(PersistedModel)
            };
        }

        var translation = ModelingChangeSetTranslator.ToOperationBatch(changes);
        if (!translation.Success)
        {
            return Failure(translation.Errors);
        }

        var working = ModelingOperationEngine.Clone(PersistedModel);
        var resolver = await ModelingPropertySchemaResolver.PreloadAsync(
            _schemas,
            working,
            ModelingPropertySchemaResolver.PropertySchemaPairs(working, translation.Batch),
            working.Views.FirstOrDefault()?.ViewpointKey,
            CultureInfo.CurrentUICulture.Name,
            cancellationToken);

        var outcome = ModelingOperationEngine.Apply(
            working,
            translation.Batch,
            new ModelingOperationContext
            {
                RelationshipRules = _relationshipRules,
                ViewpointRules = _viewpointRules,
                ResolvePropertySchema = resolver,
                Policy = ModelingOperationPolicy.Authoring
            });
        if (!outcome.Success)
        {
            return Failure(outcome.Errors);
        }

        _version = $"v{++_saves + 1}";
        working.Metadata.SourceVersion = _version;
        PersistedModel = working;

        return new ModelingModelSaveResult
        {
            Succeeded = true,
            Message = string.Empty,
            Model = ModelingOperationEngine.Clone(working)
        };
    }

    private static ModelingModelSaveResult Failure(IReadOnlyList<ModelingOperationError> errors)
        => new()
        {
            Succeeded = false,
            Message = errors.Count > 0 ? errors[0].Message : string.Empty,
            Issues = [.. errors.Select((error, position) => new ModelingIssueDto
            {
                Id = $"{position}-{error.Index}-{error.Code}",
                Severity = ModelingIssueSeverity.Error,
                Category = "validation",
                SourceElementId = error.ElementId ?? string.Empty,
                SourceRelationshipId = error.RelationshipId ?? string.Empty,
                Message = error.Message,
                SuggestedFix = error.SuggestedFix ?? string.Empty
            })]
        };
}
