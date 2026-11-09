namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// How the importer maps a BPMN document with several processes onto <see cref="Modeling.ModelingModelDto"/> models.
/// </summary>
public enum BpmnModelSplit
{
    /// <summary>
    /// Every <c>process</c> referenced by a collaboration participant becomes its own model and the
    /// participant imports as a <c>Pool</c> element of that model. Cross-pool message flows stay in the
    /// source element's model with an <c>externalModelRef</c> property pointing at the other model.
    /// </summary>
    PerProcess = 0,

    /// <summary>
    /// One model for the whole document; every participant imports as a root <c>Pool</c> element and all
    /// message flows are in-model relationships.
    /// </summary>
    SingleWithPools = 1,
}

/// <summary>Options controlling the BPMN 2.0 XML import.</summary>
public sealed class BpmnImportOptions
{
    /// <summary>
    /// How a multi-process document is split into models. Defaults to <see cref="BpmnModelSplit.PerProcess"/>.
    /// </summary>
    public BpmnModelSplit ModelSplit { get; set; } = BpmnModelSplit.PerProcess;

    /// <summary>
    /// Whether BPMNDI diagram data is imported into views. Honored by the BPMNDI phase of the converter;
    /// semantic import (the current phase) ignores diagram data either way.
    /// </summary>
    public bool ImportBpmndi { get; set; } = true;

    /// <summary>Maximum accepted input size in bytes; larger inputs fail with <c>HasErrors</c>. Defaults to 10 MiB.</summary>
    public long MaxInputBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Maximum XML element depth; deeper documents fail with <c>HasErrors</c>. Defaults to 256.</summary>
    public int MaxDepth { get; set; } = 256;

    /// <summary>Maximum number of XML elements; larger documents fail with <c>HasErrors</c>. Defaults to 500 000.</summary>
    public int MaxElements { get; set; } = 500_000;

    /// <summary>
    /// Tool dialect plugins (Camunda/Signavio/Bizagi) consulted for <c>extensionElements</c> in their namespace.
    /// Unknown namespaces are preserved verbatim under <c>foreignExtensions</c> with a loss record instead.
    /// </summary>
    public IList<IBpmnToolExtension> ToolExtensions { get; } = [];
}

/// <summary>Severity of an import loss record.</summary>
public enum BpmnImportLossSeverity
{
    /// <summary>The construct has no representation in the modeling model; its content is preserved only in this report.</summary>
    Loss = 0,

    /// <summary>The construct is imported in an approximated form; some semantics did not survive.</summary>
    Approximated = 1,

    /// <summary>Nothing was dropped, but the importer wants the caller to know about a mapping decision.</summary>
    Info = 2,
}

/// <summary>
/// One entry of the import loss report: an unsupported or approximated BPMN construct that was NOT
/// silently dropped. Also surfaced as a <c>bpmn-import</c> issue on the owning model.
/// </summary>
public sealed class BpmnImportLoss
{
    /// <summary>BPMN id of the affected element, when the construct carries one.</summary>
    public string? ElementId { get; init; }

    /// <summary>Location of the construct in the XML document (a <c>/definitions/…</c> path).</summary>
    public required string XmlPath { get; init; }

    /// <summary>The BPMN construct that was lost or approximated (XML local name, e.g. <c>multiInstanceLoopCharacteristics</c>).</summary>
    public required string Construct { get; init; }

    /// <summary>Human-readable explanation of what happened to the construct.</summary>
    public required string Reason { get; init; }

    /// <summary>How severe the loss is.</summary>
    public BpmnImportLossSeverity Severity { get; init; } = BpmnImportLossSeverity.Loss;
}

/// <summary>A non-fatal observation about the imported document that is not a content loss.</summary>
public sealed class BpmnImportWarning
{
    /// <summary>Human-readable warning message.</summary>
    public required string Message { get; init; }

    /// <summary>Location of the observation in the XML document, when available.</summary>
    public string? XmlPath { get; init; }
}

/// <summary>Result of a BPMN 2.0 XML import. Never throws for bad input — inspect <see cref="HasErrors"/>.</summary>
public sealed class BpmnImportResult
{
    /// <summary>The imported models (one per process for <see cref="BpmnModelSplit.PerProcess"/>).</summary>
    public required IReadOnlyList<Modeling.ModelingModelDto> Models { get; init; }

    /// <summary>Everything the importer could not represent exactly, with locations and reasons.</summary>
    public required IReadOnlyList<BpmnImportLoss> Losses { get; init; }

    /// <summary>Non-fatal observations about the imported document.</summary>
    public required IReadOnlyList<BpmnImportWarning> Warnings { get; init; }

    /// <summary>Whether the document could not be imported at all (malformed/unsafe XML, limits exceeded, no process).</summary>
    public bool HasErrors { get; init; }

    /// <summary>Why the import failed, when <see cref="HasErrors"/> is true.</summary>
    public string? ErrorMessage { get; init; }
}
