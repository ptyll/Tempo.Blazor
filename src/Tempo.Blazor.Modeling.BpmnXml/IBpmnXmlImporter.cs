using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>Imports BPMN 2.0 XML into modeling models. Implementations must be deterministic and never throw for bad input.</summary>
public interface IBpmnXmlImporter
{
    /// <summary>
    /// Imports a BPMN 2.0 XML document from a stream. The stream is read with a bounded buffer
    /// (at most <see cref="BpmnImportOptions.MaxInputBytes"/> bytes); it does not need to be seekable.
    /// </summary>
    /// <param name="xml">The BPMN 2.0 XML document stream.</param>
    /// <param name="options">Import options; <c>null</c> uses the defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The import result; inspect <see cref="BpmnImportResult.HasErrors"/> before using the models.</returns>
    BpmnImportResult Import(Stream xml, BpmnImportOptions? options = null, CancellationToken ct = default);

    /// <summary>Imports a BPMN 2.0 XML document from a string.</summary>
    /// <param name="xml">The BPMN 2.0 XML document.</param>
    /// <param name="options">Import options; <c>null</c> uses the defaults.</param>
    /// <returns>The import result; inspect <see cref="BpmnImportResult.HasErrors"/> before using the models.</returns>
    BpmnImportResult Import(string xml, BpmnImportOptions? options = null);
}

/// <summary>
/// Context handed to <see cref="IBpmnToolExtension.OnImportElement"/> for one <c>extensionElements</c>
/// payload of the extension's namespace.
/// </summary>
public sealed class BpmnToolImportContext
{
    /// <summary>The <c>extensionElements</c> element whose children live in the extension's namespace.</summary>
    public required XElement ExtensionElements { get; init; }

    /// <summary>The model element (or model root element) the extensionElements belong to; <c>null</c> for the definitions root.</summary>
    public ModelingElementDto? Element { get; init; }

    /// <summary>The model being built.</summary>
    public required ModelingModelDto Model { get; init; }

    /// <summary>Reports a loss the extension could not avoid.</summary>
    public required Action<BpmnImportLoss> ReportLoss { get; init; }
}

/// <summary>
/// Context handed to <see cref="IBpmnToolExtension.OnExportElement"/> while the exporter writes
/// <c>extensionElements</c> (export lands in a later phase of the converter plan).
/// </summary>
public sealed class BpmnToolExportContext
{
    /// <summary>The model element being exported.</summary>
    public required ModelingElementDto Element { get; init; }

    /// <summary>The <c>extensionElements</c> element to append namespaced children to.</summary>
    public required XElement ExtensionElements { get; init; }

    /// <summary>Reports a loss the extension could not avoid.</summary>
    public required Action<string> ReportLoss { get; init; }
}

/// <summary>
/// A tool dialect plugin (Camunda, Signavio, Bizagi, …). The importer routes every
/// <c>extensionElements</c> child in <see cref="NamespaceUri"/> through
/// <see cref="OnImportElement"/>; unrecognized namespaces are preserved verbatim under
/// <c>foreignExtensions</c> with a loss record instead of being dropped.
/// </summary>
public interface IBpmnToolExtension
{
    /// <summary>The extension namespace this plugin owns (e.g. <c>http://camunda.org/schema/1.0/bpmn</c>).</summary>
    string NamespaceUri { get; }

    /// <summary>
    /// When <c>true</c>, the exporter does NOT write this plugin's <c>{prefix}:*</c> property keys
    /// as XML attributes; the plugin serializes them itself from <see cref="OnExportElement"/>
    /// (the dialect-native payload shape). When <c>false</c> (the default), scalar values keep the
    /// attribute shape the generic writer produces (e.g. Camunda's <c>camunda:assignee</c>).
    /// </summary>
    bool OwnsAttributeExport => false;

    /// <summary>Maps the extension payload into element properties during import.</summary>
    void OnImportElement(BpmnToolImportContext context);

    /// <summary>Writes the element's extension properties back during export.</summary>
    void OnExportElement(BpmnToolExportContext context);
}
