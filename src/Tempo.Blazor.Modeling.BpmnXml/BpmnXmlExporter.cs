using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Default <see cref="IBpmnXmlExporter"/>: deterministic <see cref="ModelingModelDto"/> → BPMN 2.0
/// XML conversion (stable ordering, invariant-culture numbers, LF line endings, no BOM).
/// Model-content problems surface as <see cref="BpmnExportResult.Losses"/> instead of exceptions.
/// </summary>
public sealed class BpmnXmlExporter : IBpmnXmlExporter
{
    /// <inheritdoc />
    public BpmnExportResult Export(ModelingModelDto model, BpmnExportOptions? options = null, CancellationToken ct = default)
    {
        if (model is null)
        {
            return Failure(options, "The model to export is null.");
        }

        return Export([model], options, ct);
    }

    /// <inheritdoc />
    public BpmnExportResult Export(IReadOnlyList<ModelingModelDto> models, BpmnExportOptions? options = null, CancellationToken ct = default)
    {
        options ??= new BpmnExportOptions();
        if (models is null || models.Count == 0)
        {
            return Failure(options, "At least one model is required for the BPMN export.");
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            return BpmnExportBuilder.Build(models, options, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The export walk must never throw for hostile or inconsistent model content either.
            return Failure(options, "The modeling model could not be exported to BPMN XML.");
        }
    }

    private static BpmnExportResult Failure(BpmnExportOptions options, string message) =>
        new()
        {
            Xml = null,
            Success = false,
            ErrorMessage = message,
            Losses = [],
        };
}
