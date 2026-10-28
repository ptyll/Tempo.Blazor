using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// The shared modeling test model (BPMN task "task-a" with one main view) plus the two providers
/// several editor test classes render through. Internal so the editor/dialog/phase-5 test files
/// stay under the 1200-line ceiling without duplicating the model.
/// </summary>
internal static class ModelingEditorTestModels
{
internal static ModelingModelDto CreateModel()
{
    var model = new ModelingModelDto
    {
        Id = "modeling-editor-test",
        Title = "Modeling editor test",
        Metadata = new ModelingMetadataDto { SourceSystem = "Unit tests" },
        Elements =
        [
            new()
            {
                Id = "task-a",
                SourceId = "source-task-a",
                Notation = "bpmn",
                SemanticType = "userTask",
                Name = "Approve request"
            }
        ]
    };

    model.Views.Add(new ModelingViewDto
    {
        Id = "main-view",
        Name = "Main view",
        Notation = "bpmn",
        Nodes =
        [
            new() { ElementId = "task-a", X = 120, Y = 160, Width = 180, Height = 90 }
        ]
    });

    return model;
}
}

/// <summary>Provider that resolves <see cref="ModelingEditorTestModels.CreateModel"/> synchronously.</summary>
internal sealed class SuccessfulModelingModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.success";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ModelingEditorTestModels.CreateModel());
    }
}

/// <summary>Provider that counts every load; the default key serves the shared model.</summary>
internal sealed class CountingModelingModelProvider : IModelingModelProvider
{
    private readonly string _key;

    public CountingModelingModelProvider(string key = Key)
    {
        _key = key;
    }

    public const string Key = "tempo.tests.modeling.counting";

    public int LoadCount { get; private set; }

    public string ProviderKey => _key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadCount++;
        return Task.FromResult(ModelingEditorTestModels.CreateModel());
    }
}
