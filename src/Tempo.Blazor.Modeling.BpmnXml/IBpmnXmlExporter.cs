using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Exports modeling models back to BPMN 2.0 XML. Implementations are deterministic (same input =
/// same XML: stable ordering, invariant-culture numbers, LF line endings, no BOM) and never throw
/// for model-content problems — unrepresentable constructs land in <see cref="BpmnExportResult.Losses"/>.
/// </summary>
public interface IBpmnXmlExporter
{
    /// <summary>
    /// Exports a single model to a BPMN 2.0 XML document. When the model carries pools or
    /// message flows, the document contains a collaboration; otherwise a plain process.
    /// </summary>
    /// <param name="model">The model to export.</param>
    /// <param name="options">Export options; <c>null</c> uses the defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The export result; inspect <see cref="BpmnExportResult.Success"/> before using <see cref="BpmnExportResult.Xml"/>.</returns>
    BpmnExportResult Export(ModelingModelDto model, BpmnExportOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Exports several models (e.g. the <see cref="BpmnImportResult.Models"/> of a
    /// <see cref="BpmnModelSplit.PerProcess"/> import) into ONE BPMN 2.0 XML document: every model
    /// becomes a process, the pools of all models become collaboration participants and
    /// cross-model message flows are re-wired between the participants.
    /// </summary>
    /// <param name="models">The models to export; order is preserved.</param>
    /// <param name="options">Export options; <c>null</c> uses the defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The export result; inspect <see cref="BpmnExportResult.Success"/> before using <see cref="BpmnExportResult.Xml"/>.</returns>
    BpmnExportResult Export(IReadOnlyList<ModelingModelDto> models, BpmnExportOptions? options = null, CancellationToken ct = default);
}

/// <summary>How much of the modeling model the exporter writes back into the BPMN document.</summary>
public enum BpmnExportProfile
{
    /// <summary>
    /// Only native BPMN 2.0 constructs are emitted (attributes, flow elements, DI, documentation
    /// from descriptions). Properties without a native representation are reported as export losses.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// In addition to the native constructs, every remaining element/relationship property is
    /// serialized into <c>extensionElements</c> of the host extension namespace (canonical JSON,
    /// see <see cref="BpmnExportOptions.HostExtensionNamespace"/>) so a clean import → export →
    /// import cycle loses nothing.
    /// </summary>
    WithHostExtensions = 1,
}

/// <summary>Options controlling the BPMN 2.0 XML export.</summary>
public sealed class BpmnExportOptions
{
    /// <summary>
    /// How model properties are written. Defaults to <see cref="BpmnExportProfile.Standard"/> —
    /// no extension payloads; unrepresentable properties become export losses.
    /// </summary>
    public BpmnExportProfile Profile { get; set; } = BpmnExportProfile.Standard;

    /// <summary>
    /// The namespace of the host extension payload written when
    /// <see cref="Profile"/> is <see cref="BpmnExportProfile.WithHostExtensions"/>. Must match the
    /// property schema namespace of the full BPMN authoring plan. Defaults to
    /// <c>https://tempo.blazor/schemas/bpmn-extensions</c>.
    /// </summary>
    public string HostExtensionNamespace { get; set; } = "https://tempo.blazor/schemas/bpmn-extensions";

    /// <summary>The namespace prefix of the host extension payload. Defaults to <c>tempo</c>.</summary>
    public string HostExtensionPrefix { get; set; } = "tempo";

    /// <summary>
    /// Whether view geometry is emitted as BPMNDI (<c>BPMNDiagram</c>/<c>BPMNPlane</c>/<c>BPMNShape</c>/
    /// <c>BPMNEdge</c>). Defaults to <c>true</c>; <c>false</c> produces a diagram-less document.
    /// </summary>
    public bool EmitBpmndi { get; set; } = true;

    /// <summary>
    /// The <c>targetNamespace</c> of the exported <c>definitions</c> element. When <c>null</c>,
    /// the exporter reuses the first model's <c>Metadata.SourceVersion</c> (the imported document's
    /// target namespace) and falls back to <c>http://tempo.blazor/bpmn</c>.
    /// </summary>
    public string? TargetNamespace { get; set; }

    /// <summary>
    /// Tool dialect plugins (Camunda/Signavio/Bizagi) consulted via
    /// <see cref="IBpmnToolExtension.OnExportElement"/> while <c>extensionElements</c> are built.
    /// </summary>
    public IList<IBpmnToolExtension> ToolExtensions { get; } = [];

    /// <summary>
    /// Prefix → namespace URI map for foreign attribute properties (e.g. <c>camunda:assignee</c>)
    /// written back to the document; the namespace is declared on <c>definitions</c>. Pre-seeded
    /// with the Camunda/Signavio/Bizagi dialect namespaces from the converter design; unknown
    /// prefixes are declared deterministically as <c>urn:tempo-blazor:bpmn:{prefix}</c> with an
    /// info loss record.
    /// </summary>
    public IDictionary<string, string> ExtensionNamespaces { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["camunda"] = "http://camunda.org/schema/1.0/bpmn",
        ["signavio"] = "http://www.signavio.com",
        ["Bizagi"] = "http://www.bizagi.com/schemas/2010/Bizagi",
    };
}

/// <summary>Severity of an export loss record.</summary>
public enum BpmnExportLossSeverity
{
    /// <summary>The model construct has no BPMN representation; its content survives only in this report.</summary>
    Loss = 0,

    /// <summary>The construct is exported in an approximated form; some semantics did not survive.</summary>
    Approximated = 1,

    /// <summary>Nothing was dropped, but the exporter wants the caller to know about a mapping decision.</summary>
    Info = 2,
}

/// <summary>
/// One entry of the export loss report: a model element, relationship or property that could NOT
/// be represented (exactly) in BPMN 2.0 XML under the active <see cref="BpmnExportProfile"/>.
/// </summary>
public sealed class BpmnExportLoss
{
    /// <summary>Id of the affected model element or relationship, when the construct carries one.</summary>
    public string? ElementId { get; init; }

    /// <summary>The model construct that was lost or approximated (e.g. <c>Property</c>, <c>UserTask</c>, <c>camunda:taskListener</c>).</summary>
    public required string Construct { get; init; }

    /// <summary>Human-readable explanation of what happened to the construct.</summary>
    public required string Reason { get; init; }

    /// <summary>How severe the loss is.</summary>
    public BpmnExportLossSeverity Severity { get; init; } = BpmnExportLossSeverity.Loss;
}

/// <summary>Result of a BPMN 2.0 XML export. Never throws for model-content problems — inspect <see cref="Success"/>.</summary>
public sealed class BpmnExportResult
{
    /// <summary>The exported BPMN 2.0 XML document (declaration + LF line endings, no BOM), or <c>null</c> when <see cref="Success"/> is false.</summary>
    public string? Xml { get; init; }

    /// <summary>Whether the export produced a document.</summary>
    public bool Success { get; init; }

    /// <summary>Why the export failed, when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Everything the exporter could not represent exactly, with reasons.</summary>
    public required IReadOnlyList<BpmnExportLoss> Losses { get; init; }
}
