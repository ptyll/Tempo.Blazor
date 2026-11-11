using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// The built-in tool dialect plugins of the headless BPMN 2.0 XML converter, in their fixed
/// registration order (Camunda, Signavio, Bizagi). State-free — fresh instances are cheap, so
/// callers and DI can share this list or create their own; the mapping is deterministic either way.
/// </summary>
public static class BpmnBuiltInToolExtensions
{
    /// <summary>Every built-in tool extension, in registration order.</summary>
    public static IReadOnlyList<IBpmnToolExtension> All { get; } =
    [
        new BpmnCamundaToolExtension(),
        new BpmnSignavioToolExtension(),
        new BpmnBizagiToolExtension(),
    ];
}

/// <summary>
/// Camunda Modeler dialect plugin (<c>http://camunda.org/schema/1.0/bpmn</c>). Imports
/// <c>camunda:properties</c> into <c>camunda:*</c> property keys, <c>camunda:documentation</c>
/// into the element description and preserves every other payload (task listeners, connectors,
/// input/output mappings, …) verbatim under the <c>camundaExtensions</c> property for a loss-free
/// re-export. Element-level <c>camunda:*</c> attributes (assignee, candidateGroups, candidateUsers,
/// formKey, dueDate, versionTag, delegateExpression, …) are the attribute catch-all's job and are
/// written back as attributes by the exporter — this plugin owns only the element payloads.
/// </summary>
public sealed class BpmnCamundaToolExtension : IBpmnToolExtension
{
    /// <summary>The Camunda extension namespace URI.</summary>
    public const string NamespaceUriConstant = "http://camunda.org/schema/1.0/bpmn";

    private static readonly XNamespace CamundaNs = NamespaceUriConstant;

    /// <inheritdoc />
    public string NamespaceUri => NamespaceUriConstant;

    /// <inheritdoc />
    /// <remarks>
    /// Camunda's native shape for scalar values IS attributes (<c>camunda:assignee</c>), so the
    /// exporter's generic attribute writer keeps writing <c>camunda:*</c> property keys; this
    /// plugin restores only the preserved <c>camundaExtensions</c> payloads.
    /// </remarks>
    public bool OwnsAttributeExport => false;

    /// <inheritdoc />
    public void OnImportElement(BpmnToolImportContext context)
    {
        var preserved = new StringBuilder();
        foreach (var payload in context.ExtensionElements.Elements()
                     .Where(element => string.Equals(element.Name.NamespaceName, NamespaceUriConstant, StringComparison.Ordinal)))
        {
            if (payload.Name.LocalName == "properties")
            {
                foreach (var property in payload.Elements(CamundaNs + "property"))
                {
                    var name = property.Attribute("name")?.Value;
                    var value = property.Attribute("value")?.Value ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    TargetProperties(context)["camunda:" + name] = BpmnImportInternals.StringElement(value);
                }

                continue;
            }

            if (payload.Name.LocalName == "documentation")
            {
                var text = payload.Value;
                if (context.Element is { } element)
                {
                    element.Description = element.Description.Length == 0
                        ? text
                        : element.Description + "\n" + text;
                }
                else
                {
                    context.ReportLoss(new BpmnImportLoss
                    {
                        ElementId = null,
                        XmlPath = BpmnImportInternals.BuildPath(payload),
                        Construct = "camunda:documentation",
                        Reason = $"Camunda documentation at the process level has no model element to attach to; content: {text}",
                        Severity = BpmnImportLossSeverity.Loss,
                    });
                }

                continue;
            }

            preserved.Append(payload.ToString(SaveOptions.DisableFormatting));
        }

        if (preserved.Length == 0)
        {
            return;
        }

        if (context.Element is not null)
        {
            context.Element.Properties["camundaExtensions"] = BpmnImportInternals.StringElement(preserved.ToString());
        }
        else
        {
            context.ReportLoss(new BpmnImportLoss
            {
                ElementId = null,
                XmlPath = BpmnImportInternals.BuildPath(context.ExtensionElements),
                Construct = "extensionElements",
                Reason = $"Camunda extension payloads at the process level have no model element to attach to; content: {preserved}",
                Severity = BpmnImportLossSeverity.Loss,
            });
        }
    }

    /// <inheritdoc />
    public void OnExportElement(BpmnToolExportContext context)
    {
        if (!context.Element.Properties.TryGetValue("camundaExtensions", out var raw)
            || raw.ValueKind != JsonValueKind.String
            || raw.GetString() is not { Length: > 0 } preserved)
        {
            return;
        }

        try
        {
            // The importer accumulates every preserved payload of the namespace into a single
            // concatenated string, so parse behind a throwaway wrapper to allow siblings.
            var wrapper = XElement.Parse("<wrapper>" + preserved + "</wrapper>");
            context.ExtensionElements.Add(wrapper.Elements());
        }
        catch (Exception)
        {
            context.ReportLoss(
                "The preserved 'camundaExtensions' payload is not well-formed XML and cannot be written back.");
        }
    }

    private static Dictionary<string, JsonElement> TargetProperties(BpmnToolImportContext context) =>
        context.Element is not null ? context.Element.Properties : context.Model.Properties;
}

/// <summary>
/// Signavio Process Editor dialect plugin (<c>http://www.signavio.com</c>). Imports
/// <c>signavio:signavioMetaData</c> into <c>signavio:*</c> element property keys and diagram-level
/// <c>signavio:signavioDiagramMetaData</c> into <c>signavio:*</c> model properties; exports the
/// keys back as <c>signavioMetaData</c> payloads (the dialect-native shape Signavio reads).
/// </summary>
public sealed class BpmnSignavioToolExtension : IBpmnToolExtension
{
    /// <summary>The Signavio extension namespace URI.</summary>
    public const string NamespaceUriConstant = "http://www.signavio.com";

    private static readonly XNamespace SignavioNs = NamespaceUriConstant;

    /// <inheritdoc />
    public string NamespaceUri => NamespaceUriConstant;

    /// <inheritdoc />
    /// <remarks>
    /// Signavio scalars live in <c>signavioMetaData</c> payloads, not attributes — the plugin
    /// writes them via <see cref="OnExportElement"/> and the exporter's generic attribute writer
    /// skips <c>signavio:*</c> property keys while this plugin is registered.
    /// </remarks>
    public bool OwnsAttributeExport => true;

    /// <inheritdoc />
    public void OnImportElement(BpmnToolImportContext context)
    {
        foreach (var payload in context.ExtensionElements.Elements()
                     .Where(element => string.Equals(element.Name.NamespaceName, NamespaceUriConstant, StringComparison.Ordinal)))
        {
            if (payload.Name.LocalName is not ("signavioMetaData" or "signavioDiagramMetaData"))
            {
                context.ReportLoss(new BpmnImportLoss
                {
                    ElementId = context.Element?.Id,
                    XmlPath = BpmnImportInternals.BuildPath(payload),
                    Construct = payload.Name.LocalName,
                    Reason = $"The Signavio extension element '{payload.Name.LocalName}' has no dedicated mapping; content: {payload.ToString(SaveOptions.DisableFormatting)}",
                    Severity = BpmnImportLossSeverity.Loss,
                });
                continue;
            }

            var key = payload.Attribute("metaKey")?.Value;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = payload.Attribute("metaValue")?.Value ?? string.Empty;
            var target = payload.Name.LocalName == "signavioDiagramMetaData" || context.Element is null
                ? context.Model.Properties
                : context.Element.Properties;
            target["signavio:" + key] = BpmnImportInternals.StringElement(value);
        }
    }

    /// <inheritdoc />
    public void OnExportElement(BpmnToolExportContext context)
    {
        foreach (var (key, value) in context.Element.Properties)
        {
            if (!key.StartsWith("signavio:", StringComparison.Ordinal))
            {
                continue;
            }

            context.ExtensionElements.Add(new XElement(SignavioNs + "signavioMetaData",
                new XAttribute("metaKey", key["signavio:".Length..]),
                new XAttribute("metaValue", PropertyText(value))));
        }
    }

    private static string PropertyText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
}

/// <summary>
/// Bizagi Modeler dialect plugin (<c>http://www.bizagi.com/schemas/2010/Bizagi</c>). Imports
/// <c>Bizagi:BizagiExtensions/Bizagi:BizagiProperty</c> name/value pairs into <c>bizagi:*</c>
/// element property keys and exports them back as a <c>BizagiExtensions</c> payload. The
/// <c>Bizagi:property</c> element ATTRIBUTE is normalized to the <c>bizagi:property</c> key by the
/// importer's attribute catch-all and written back by this plugin's payload (round-trip stable).
/// </summary>
public sealed class BpmnBizagiToolExtension : IBpmnToolExtension
{
    /// <summary>The Bizagi extension namespace URI.</summary>
    public const string NamespaceUriConstant = "http://www.bizagi.com/schemas/2010/Bizagi";

    private static readonly XNamespace BizagiNs = NamespaceUriConstant;

    /// <inheritdoc />
    public string NamespaceUri => NamespaceUriConstant;

    /// <inheritdoc />
    /// <remarks>
    /// Bizagi scalars live in <c>BizagiProperty</c> payloads — the plugin writes them via
    /// <see cref="OnExportElement"/> and the exporter's generic attribute writer skips
    /// <c>bizagi:*</c> property keys while this plugin is registered.
    /// </remarks>
    public bool OwnsAttributeExport => true;

    /// <inheritdoc />
    public void OnImportElement(BpmnToolImportContext context)
    {
        foreach (var payload in context.ExtensionElements.Elements()
                     .Where(element => string.Equals(element.Name.NamespaceName, NamespaceUriConstant, StringComparison.Ordinal)))
        {
            if (payload.Name.LocalName != "BizagiExtensions")
            {
                context.ReportLoss(new BpmnImportLoss
                {
                    ElementId = context.Element?.Id,
                    XmlPath = BpmnImportInternals.BuildPath(payload),
                    Construct = payload.Name.LocalName,
                    Reason = $"The Bizagi extension element '{payload.Name.LocalName}' has no dedicated mapping; content: {payload.ToString(SaveOptions.DisableFormatting)}",
                    Severity = BpmnImportLossSeverity.Loss,
                });
                continue;
            }

            foreach (var property in payload.Elements(BizagiNs + "BizagiProperty"))
            {
                var name = property.Attribute("name")?.Value;
                var value = property.Attribute("value")?.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var target = context.Element is not null ? context.Element.Properties : context.Model.Properties;
                target["bizagi:" + name] = BpmnImportInternals.StringElement(value);
            }
        }
    }

    /// <inheritdoc />
    public void OnExportElement(BpmnToolExportContext context)
    {
        XElement? wrapper = null;
        foreach (var (key, value) in context.Element.Properties)
        {
            if (!key.StartsWith("bizagi:", StringComparison.Ordinal))
            {
                continue;
            }

            wrapper ??= new XElement(BizagiNs + "BizagiExtensions");
            wrapper.Add(new XElement(BizagiNs + "BizagiProperty",
                new XAttribute("name", key["bizagi:".Length..]),
                new XAttribute("value", PropertyText(value))));
        }

        if (wrapper is not null)
        {
            context.ExtensionElements.Add(wrapper);
        }
    }

    private static string PropertyText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
}
