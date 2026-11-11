using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Default <see cref="IBpmnXmlExporter"/>: deterministic <see cref="ModelingModelDto"/> → BPMN 2.0
/// XML conversion (stable ordering, invariant-culture numbers, LF line endings, no BOM).
/// Model-content problems surface as <see cref="BpmnExportResult.Losses"/> instead of exceptions.
/// </summary>
public sealed class BpmnXmlExporter : IBpmnXmlExporter
{
    private readonly IReadOnlyList<IBpmnToolExtension> _toolExtensions;

    /// <summary>
    /// Creates the exporter. <paramref name="toolExtensions"/> supplies the fallback tool dialect
    /// plugins (see <see cref="AddTempoBpmnXml"/>) merged with the caller's
    /// <see cref="BpmnExportOptions.ToolExtensions"/> list: explicit options plugins are consulted
    /// first, the DI fallback list fills the remaining namespaces, and the merged list is
    /// de-duplicated by <see cref="IBpmnToolExtension.NamespaceUri"/> first-wins (the same
    /// semantics the importer's per-namespace lookup uses). Omit the argument (or pass an empty
    /// sequence) for the plain converter without tool plugins.
    /// </summary>
    public BpmnXmlExporter(IEnumerable<IBpmnToolExtension>? toolExtensions = null) =>
        _toolExtensions = toolExtensions?.ToArray() ?? [];

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
        options = ResolveOptions(options);
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

    private BpmnExportOptions ResolveOptions(BpmnExportOptions? options)
    {
        options ??= new BpmnExportOptions();
        if (options.ToolExtensions.Count == 0 && _toolExtensions.Count == 0)
        {
            return options;
        }

        return MergeOptions(options, options.ToolExtensions);
    }

    private BpmnExportOptions MergeOptions(BpmnExportOptions source, IList<IBpmnToolExtension> explicitExtensions)
    {
        var merged = new BpmnExportOptions
        {
            Profile = source.Profile,
            HostExtensionNamespace = source.HostExtensionNamespace,
            HostExtensionPrefix = source.HostExtensionPrefix,
            EmitBpmndi = source.EmitBpmndi,
            TargetNamespace = source.TargetNamespace,
        };

        foreach (var (prefix, uri) in source.ExtensionNamespaces)
        {
            merged.ExtensionNamespaces[prefix] = uri;
        }

        // Finding 3 (phase 5 review): de-duplicate by NamespaceUri, first wins, with the caller's
        // explicit plugins consulted before the DI fallback list. A namespace is therefore handled
        // by exactly one plugin — extension payloads are written once and IsToolOwnedAttributeKey
        // sees a single owner (same first-wins semantics as the importer's per-namespace lookup).
        var seenNamespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in explicitExtensions.Concat(_toolExtensions))
        {
            if (seenNamespaces.Add(extension.NamespaceUri))
            {
                merged.ToolExtensions.Add(extension);
            }
        }

        return merged;
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
