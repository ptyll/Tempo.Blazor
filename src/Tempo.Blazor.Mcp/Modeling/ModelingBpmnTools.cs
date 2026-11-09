using System.ComponentModel;
using ModelContextProtocol.Server;
using Tempo.Blazor.DocumentLibrary;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Mcp.Modeling;

/// <summary>
/// MCP tools for the headless BPMN 2.0 XML converter (plan _"BPMN 2.0 XML converter"_, phase 5):
/// import a BPMN document into modeling models (with the loss report) and export a stored model
/// back to BPMN 2.0 XML. Both directions run the built-in Camunda/Signavio/Bizagi tool dialect
/// plugins, so tool metadata survives the round trip. English literals follow the established
/// <c>Tempo.Blazor.Mcp</c> convention (audience is the LLM).
/// </summary>
[McpServerToolType]
public static class ModelingBpmnTools
{
    /// <summary>
    /// Maximum accepted <c>bpmnXml</c> text length per call (roughly 2 MB of XML). The importer's
    /// byte cap is aligned to the UTF-8 worst case of this text limit.
    /// </summary>
    public const int MaxBpmnXmlChars = 2_000_000;

    /// <summary>
    /// Maximum accepted <c>name</c> length per call (saved document title override). Titles are
    /// persisted by host document providers, so the cap keeps a hostile agent input out of the
    /// host's storage.
    /// </summary>
    public const int MaxNameChars = 200;

    [McpServerTool(Name = "modeling_import_bpmn")]
    [Description("Import a BPMN 2.0 XML document (text) into Tempo modeling models using the headless BPMN converter, with the built-in Camunda/Signavio/Bizagi dialect plugins. Returns every imported model (id, title, notation, elements, relationships, views, counts) plus the full loss report (elementId, xmlPath, construct, reason, severity) and warnings — nothing is silently dropped. Set save=true to persist the imported models as modeling documents (one document per model; pass name to override the title, capped at 200 characters). Inputs are size-limited (bpmnXml 2M characters, name 200 characters). Effect: write when save=true, otherwise read-only conversion.")]
    public static async Task<string> ImportBpmn(
        IModelingModelDocumentProvider models,
        [Description("The BPMN 2.0 XML document text (definitions root).")] string bpmnXml,
        [Description("PerProcess (default: one model per process, pools as elements) or SingleWithPools (one model, pools as root elements).")] string modelSplit = "PerProcess",
        [Description("Import BPMNDI diagram geometry into model views; false runs the deterministic headless auto-layout. Default true.")] bool importBpmndi = true,
        [Description("Persist the imported models as modeling documents. Default false (conversion only).")] bool save = false,
        [Description("Optional title for the saved document(s); defaults to each imported model's title.")] string? name = null,
        [Description("Optional app id (GUID) scoping document creation; required when the host serves more than one app.")] string? scopeAppId = null)
    {
        if (bpmnXml is null)
        {
            return McpToolResults.Failure(McpToolResults.ValidationFailed, "The bpmnXml input is required.");
        }

        if (bpmnXml.Length > MaxBpmnXmlChars)
        {
            return McpToolResults.Failure(
                McpToolResults.ValidationFailed,
                $"The bpmnXml input exceeds the size limit of {MaxBpmnXmlChars:N0} characters per call.");
        }

        if (!Enum.TryParse<BpmnModelSplit>(modelSplit, ignoreCase: true, out var split))
        {
            return McpToolResults.Failure(
                McpToolResults.ValidationFailed,
                $"The modelSplit value '{modelSplit}' is not supported.",
                ["Use 'PerProcess' (default) or 'SingleWithPools'."]);
        }

        if (name is not null && name.Length > MaxNameChars)
        {
            return McpToolResults.Failure(
                McpToolResults.ValidationFailed,
                $"The name input exceeds the size limit of {MaxNameChars:N0} characters per call.");
        }

        var importer = new BpmnXmlImporter(BpmnBuiltInToolExtensions.All);
        var result = importer.Import(bpmnXml, new BpmnImportOptions
        {
            ModelSplit = split,
            ImportBpmndi = importBpmndi,
            MaxInputBytes = MaxBpmnXmlChars * 4L,
        });

        if (result.HasErrors)
        {
            // Malformed XML, a wrong root element, over-limit documents — all are input problems,
            // so the agent gets validation_failed (not the generic error code) with the parser
            // message (finding 5).
            return McpToolResults.Failure(McpToolResults.ValidationFailed, result.ErrorMessage ?? "The BPMN XML document could not be imported.");
        }

        var saved = new List<object>();
        if (save)
        {
            List<object>? failed = null;
            var firstFailedIndex = -1;
            for (var index = 0; index < result.Models.Count; index++)
            {
                var model = result.Models[index];
                var title = name is not null
                    ? result.Models.Count > 1 ? $"{name} ({index + 1})" : name
                    : model.Title.Length > 0 ? model.Title : $"Imported model {index + 1}";

                try
                {
                    var (id, _) = await models.CreateModelingModelDocumentAsync(title, scopeAppId);
                    await models.SaveModelingModelDocumentAsync(id, model);
                    saved.Add(new { id, title });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Finding 4: one failing document must not abort the remaining saves. The
                    // provider contract has no delete, so the caller gets an exact partial-
                    // progress report — everything saved so far plus where it stopped.
                    failed ??= [];
                    if (firstFailedIndex < 0)
                    {
                        firstFailedIndex = index;
                    }
                    failed.Add(new { index, title, error = ex.Message });
                }
            }

            if (failed is not null)
            {
                return McpToolResults.Failure(
                    McpToolResults.Error,
                    $"Saving {failed.Count} of {result.Models.Count} imported model(s) failed; {saved.Count} document(s) were saved.",
                    data: new { saved, failedAt = firstFailedIndex, failed });
            }
        }

        return McpToolResults.Success(new
        {
            modelCount = result.Models.Count,
            models = result.Models.Select(model => new
            {
                id = model.Id,
                title = model.Title,
                notation = model.Notation,
                counts = new
                {
                    elements = model.Elements.Count,
                    relationships = model.Relationships.Count,
                    views = model.Views.Count
                },
                elements = model.Elements.Select(element => new
                {
                    id = element.Id,
                    semanticType = element.SemanticType,
                    name = element.Name,
                    parentId = element.ParentId
                }).ToList(),
                relationships = model.Relationships.Select(relationship => new
                {
                    id = relationship.Id,
                    relationshipType = relationship.RelationshipType,
                    sourceElementId = relationship.SourceElementId,
                    targetElementId = relationship.TargetElementId
                }).ToList(),
                views = model.Views.Select(view => new
                {
                    id = view.Id,
                    name = view.Name,
                    viewpointKey = view.ViewpointKey,
                    nodes = view.Nodes.Count,
                    connections = view.Connections.Count
                }).ToList()
            }).ToList(),
            losses = result.Losses.Select(MapLoss).ToList(),
            warnings = result.Warnings.Select(warning => new
            {
                message = warning.Message,
                xmlPath = warning.XmlPath
            }).ToList(),
            saved
        });
    }

    [McpServerTool(Name = "modeling_export_bpmn")]
    [Description("Export a stored modeling model to BPMN 2.0 XML using the headless BPMN converter, with the built-in Camunda/Signavio/Bizagi dialect plugins. Returns the XML document plus the export loss report (elementId, construct, reason, severity) for everything the model carries that BPMN cannot represent exactly. profile Standard (default) writes only native BPMN; WithHostExtensions additionally writes Tempo host extension payloads so a clean re-import loses nothing. Set emitBpmndi=false to omit the BPMNDI diagram. Effect: read-only.")]
    public static async Task<string> ExportBpmn(
        ITempoDocumentLibraryProvider library,
        IModelingModelDocumentProvider models,
        [Description("Modeling model id (GUID).")] Guid modelId,
        [Description("Standard (default: native BPMN only) or WithHostExtensions (adds Tempo host extension payloads for loss-free round trips).")] string profile = "Standard",
        [Description("Emit BPMNDI diagram geometry. Default true.")] bool emitBpmndi = true,
        [Description("Optional targetNamespace of the exported definitions element.")] string? targetNamespace = null)
    {
        var entry = await library.GetEntryAsync(TempoDocumentKind.Modeling, modelId);
        if (entry is null)
        {
            return McpToolResults.Failure(McpToolResults.NotFound, $"Model {modelId} not found.");
        }

        var model = await models.GetModelingModelDocumentAsync(modelId);
        if (model is null)
        {
            return McpToolResults.Failure(McpToolResults.NotFound, $"Model {modelId} not found.");
        }

        if (!Enum.TryParse<BpmnExportProfile>(profile, ignoreCase: true, out var exportProfile))
        {
            return McpToolResults.Failure(
                McpToolResults.ValidationFailed,
                $"The profile value '{profile}' is not supported.",
                ["Use 'Standard' (default) or 'WithHostExtensions'."]);
        }

        var exporter = new BpmnXmlExporter(BpmnBuiltInToolExtensions.All);
        var result = exporter.Export(model, new BpmnExportOptions
        {
            Profile = exportProfile,
            EmitBpmndi = emitBpmndi,
            TargetNamespace = targetNamespace,
        });

        if (!result.Success)
        {
            return McpToolResults.Failure(McpToolResults.Error, result.ErrorMessage ?? "The model could not be exported to BPMN XML.");
        }

        return McpToolResults.Success(new
        {
            id = modelId,
            xml = result.Xml,
            losses = result.Losses.Select(MapLoss).ToList()
        });
    }

    private static object MapLoss(BpmnImportLoss loss) => new
    {
        elementId = loss.ElementId,
        xmlPath = loss.XmlPath,
        construct = loss.Construct,
        reason = loss.Reason,
        severity = loss.Severity.ToString()
    };

    private static object MapLoss(BpmnExportLoss loss) => new
    {
        elementId = loss.ElementId,
        construct = loss.Construct,
        reason = loss.Reason,
        severity = loss.Severity.ToString()
    };
}
