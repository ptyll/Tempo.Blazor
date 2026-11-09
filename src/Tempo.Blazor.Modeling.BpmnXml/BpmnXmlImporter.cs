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
    private readonly IReadOnlyList<IBpmnToolExtension> _toolExtensions;

    /// <summary>
    /// Creates the importer. <paramref name="toolExtensions"/> supplies the fallback tool dialect
    /// plugins (see <see cref="AddTempoBpmnXml"/>) used when the caller's
    /// <see cref="BpmnImportOptions.ToolExtensions"/> list is empty; an explicitly populated
    /// options list always wins. Omit the argument (or pass an empty sequence) for the plain
    /// converter without tool plugins.
    /// </summary>
    public BpmnXmlImporter(IEnumerable<IBpmnToolExtension>? toolExtensions = null) =>
        _toolExtensions = toolExtensions?.ToArray() ?? [];

    /// <inheritdoc />
    public BpmnImportResult Import(string xml, BpmnImportOptions? options = null)
    {
        options = ResolveOptions(options);
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
        options = ResolveOptions(options);
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

    private BpmnImportOptions ResolveOptions(BpmnImportOptions? options)
    {
        if (options is null)
        {
            return _toolExtensions.Count == 0 ? new BpmnImportOptions() : MergeOptions(new BpmnImportOptions(), []);
        }

        return options.ToolExtensions.Count > 0 || _toolExtensions.Count == 0
            ? options
            : MergeOptions(options, options.ToolExtensions);
    }

    private BpmnImportOptions MergeOptions(BpmnImportOptions source, IList<IBpmnToolExtension> explicitExtensions)
    {
        var merged = new BpmnImportOptions
        {
            ModelSplit = source.ModelSplit,
            ImportBpmndi = source.ImportBpmndi,
            MaxInputBytes = source.MaxInputBytes,
            MaxDepth = source.MaxDepth,
            MaxElements = source.MaxElements,
        };

        foreach (var extension in explicitExtensions)
        {
            merged.ToolExtensions.Add(extension);
        }

        foreach (var extension in _toolExtensions)
        {
            merged.ToolExtensions.Add(extension);
        }

        return merged;
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
