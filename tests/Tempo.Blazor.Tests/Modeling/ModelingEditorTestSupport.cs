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

/// <summary>
/// A TimeProvider that keeps the editor's SHORT loading delays off the real-timer path:
/// every delay at or below <see cref="ImmediateThreshold"/> fires its callback on the spot
/// (so <c>Task.Delay(minimum-loading, this)</c> completes synchronously and the load pipeline
/// settles inside the test's own dispatcher turns), while LONGER delays — the 5 s
/// long-loading threshold — stay pending like an unadvanced fake clock, so a fast load never
/// falsely raises the "taking longer than expected" message. Flaky-test fix: the 150 ms
/// minimum-loading delay no longer races a real-timer sleep under parallel test execution.
/// </summary>
internal sealed class ModelingEditorTestClock : TimeProvider
{
    /// <summary>Delays at or below this span complete immediately (the editor's minimum-loading
    /// delay is 150 ms; anything longer is a long-loading threshold and must stay pending).</summary>
    private static readonly TimeSpan ImmediateThreshold = TimeSpan.FromMilliseconds(999);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime <= ImmediateThreshold)
        {
            return new ImmediateTimer(callback, state);
        }

        return new PendingTimer();
    }

    /// <summary>A timer that fires its callback during construction — the delay completes
    /// before <c>Task.Delay</c> even returns (verified: the delay promise tolerates the
    /// callback running inside <c>CreateTimer</c>).</summary>
    private sealed class ImmediateTimer : ITimer
    {
        public ImmediateTimer(TimerCallback callback, object? state) => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>A timer that never fires — the delay stays pending until disposed.</summary>
    private sealed class PendingTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
