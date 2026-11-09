using System.Text;
using System.Xml;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Default <see cref="IBpmnXmlImporter"/>: safe, bounded, deterministic BPMN 2.0 XML →
/// <see cref="ModelingModelDto"/> conversion. Never throws for bad input — structural problems
/// surface as <see cref="BpmnImportResult.HasErrors"/> with a message.
/// </summary>
public sealed class BpmnXmlImporter : IBpmnXmlImporter
{
    /// <inheritdoc />
    public BpmnImportResult Import(string xml, BpmnImportOptions? options = null)
    {
        options ??= new BpmnImportOptions();
        if (xml is null)
        {
            return Failure(options, "The BPMN XML input is null.");
        }

        // The byte cap is enforced on the UTF-8 encoded size; the parse itself reads from a
        // StringReader so an encoding declaration (e.g. utf-16) in the prolog cannot clash
        // with the actual decoding.
        if (Encoding.UTF8.GetByteCount(xml) > options.MaxInputBytes)
        {
            return Failure(options,
                $"The BPMN XML input exceeds the size limit of {options.MaxInputBytes} bytes (MaxInputBytes).");
        }

        return ImportCore(() =>
        {
            var settings = BpmnXmlSecurity.CreateSafeSettings();
            var reader = XmlReader.Create(new StringReader(xml), settings);
            return BpmnXmlSecurity.CreateLimitEnforcingReader(reader, options.MaxDepth, options.MaxElements);
        }, options, CancellationToken.None);
    }

    /// <inheritdoc />
    public BpmnImportResult Import(Stream xml, BpmnImportOptions? options = null, CancellationToken ct = default)
    {
        options ??= new BpmnImportOptions();
        if (xml is null)
        {
            return Failure(options, "The BPMN XML stream is null.");
        }

        return ImportCore(() =>
        {
            var settings = BpmnXmlSecurity.CreateSafeSettings();
            var bounded = BpmnXmlSecurity.CreateBoundedStream(xml, options.MaxInputBytes, ct);
            var reader = XmlReader.Create(bounded, settings);
            // The bounded stream is only wrapped, never disposed, so the caller's stream stays readable.
            return BpmnXmlSecurity.CreateLimitEnforcingReader(reader, options.MaxDepth, options.MaxElements);
        }, options, ct);
    }

    private static BpmnImportResult ImportCore(Func<XmlReader> createReader, BpmnImportOptions options, CancellationToken ct)
    {
        XDocument document;
        try
        {
            using (var enforcing = createReader())
            {
                ct.ThrowIfCancellationRequested();
                document = XDocument.Load(enforcing, LoadOptions.None);
            }

            ct.ThrowIfCancellationRequested();
        }
        catch (BpmnXmlLimitException limit)
        {
            return Failure(options, limit.Message);
        }
        catch (XmlException xml)
        {
            return Failure(options,
                $"The BPMN XML document is not well-formed (line {xml.LineNumber}, position {xml.LinePosition}): {xml.Message}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return Failure(options, $"The BPMN XML document could not be read: {ex.Message}");
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Unknown load failures (including hostile payloads that reach unexpected code paths)
            // fail closed with a generic message — never propagate as an unhandled exception.
            return Failure(options, "The BPMN XML document could not be imported.");
        }

        var root = document.Root;
        if (root is null)
        {
            return Failure(options, "The BPMN XML document is empty.");
        }

        if (!string.Equals(root.Name.LocalName, "definitions", StringComparison.Ordinal))
        {
            return Failure(options,
                $"The document root is '{root.Name.LocalName}', not a BPMN 'definitions' element.");
        }

        try
        {
            return BpmnModelBuilder.Build(document, options, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The semantic walk must never throw for hostile or malformed content either.
            return Failure(options, "The BPMN XML document could not be imported.");
        }
    }

    private static BpmnImportResult Failure(BpmnImportOptions options, string message) =>
        new()
        {
            Models = [],
            Losses = [],
            Warnings = [],
            HasErrors = true,
            ErrorMessage = message,
        };
}
