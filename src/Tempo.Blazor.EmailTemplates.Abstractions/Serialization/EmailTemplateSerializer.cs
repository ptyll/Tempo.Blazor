using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

/// <summary>
/// Serializes <see cref="EmailTemplateDocument"/> to and from JSON. Block polymorphism uses the
/// <c>type</c> discriminator declared on <see cref="EmailBlockBase"/>. Unknown properties are
/// ignored (forward compatibility); unknown block types and malformed JSON surface as
/// <see cref="EmailTemplateSerializationException"/>.
/// </summary>
/// <remarks>
/// Schema versioning (§3.8, D18): <see cref="Serialize"/> always writes
/// <see cref="EmailTemplateDocument.CurrentSchemaVersion"/>, a document read without the property is
/// version 1 and <see cref="Deserialize(string)"/> migrates it to the current version, and a document
/// from a newer schema is refused with <see cref="SerializationKeys.UnsupportedSchemaVersion"/> instead
/// of being silently mis-read. <see cref="Deserialize(string, EmailImportOptions?)"/> additionally
/// bounds the JSON nesting depth.
/// </remarks>
public static partial class EmailTemplateSerializer
{
    /// <summary>
    /// JSON levels every document needs before block nesting starts: a flat document measures 8 levels
    /// (root → sections → section → columns → column → blocks → block), a table adds four more
    /// (rows → row → cells → cell), and 14 leaves room for attribute dictionaries and class lists on top.
    /// </summary>
    private const int JsonEnvelopeDepth = 14;

    /// <summary>
    /// JSON levels the most expensive block nesting level costs, measured on a nested
    /// <c>mj-wrapper</c> (wrapper → sections → section → columns → column → blocks → block).
    /// <see cref="EmailImportOptions.MaxNestingDepth"/> counts <b>model</b> nesting, so the JSON parser
    /// bound is derived from it — a document within the model limit must always parse (invariant pinned
    /// by <c>ImportLimitsTests</c>).
    /// </summary>
    private const int JsonLevelsPerNestingLevel = 6;

    /// <summary>
    /// The depth a caller that passes no limits always gets: never stricter than
    /// <see cref="JsonSerializerOptions.MaxDepth"/>'s own default of 64, so adding import limits does not
    /// silently reject documents that used to deserialize.
    /// </summary>
    private const int DefaultJsonDepth = 64;

    /// <summary>
    /// The largest JSON depth the parser is ever allowed to accept. A host may raise
    /// <see cref="EmailImportOptions.MaxNestingDepth"/>; the derived bound stays finite so a hostile
    /// payload cannot exhaust the stack.
    /// </summary>
    private const int MaxJsonDepth = 10_000;

    /// <summary>
    /// The deepest block nesting the serializer will write. It is well above any sane template (the
    /// default import limit is 8) and it keeps the writer's own recursion bounded, so a cyclic or
    /// pathological object graph fails with a clear error instead of exhausting the stack.
    /// </summary>
    private const int MaxSerializableNesting = 128;

    /// <summary>Gets the canonical serialization options used for documents, blocks and cloning.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>
    /// Serializes a document to a compact JSON string, always stamping the current schema version
    /// (<see cref="EmailTemplateDocument.SchemaVersion"/> on the instance is never mutated).
    /// </summary>
    public static string Serialize(EmailTemplateDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // The writer has the same depth budget as the reader: a deeply nested document (a host may allow
        // more nesting than the default 8) needs a bound derived from its own shape, otherwise the
        // serializer would reject a document the importer/validator accept.
        var nesting = DocumentNesting.MaxDepth(document, MaxSerializableNesting);
        if (nesting > MaxSerializableNesting)
        {
            throw new EmailTemplateSerializationException(
                $"The template document nests blocks deeper than {MaxSerializableNesting} levels " +
                "(or contains a block cycle), so it cannot be serialized.",
                SerializationKeys.TooDeeplyNested);
        }

        var options = OptionsFor(JsonEnvelopeDepth + JsonLevelsPerNestingLevel * (nesting + 1));
        var node = JsonSerializer.SerializeToNode(document, options) as JsonObject
            ?? throw new EmailTemplateSerializationException("The template JSON serialized to null.");

        node[SchemaVersionPropertyName] = EmailTemplateDocument.CurrentSchemaVersion;
        return node.ToJsonString(options);
    }

    /// <summary>
    /// Deserializes a document from JSON, migrating it to
    /// <see cref="EmailTemplateDocument.CurrentSchemaVersion"/>.
    /// </summary>
    /// <exception cref="EmailTemplateSerializationException">
    /// The JSON is malformed, references an unknown block type, or was written by a newer schema.
    /// </exception>
    public static EmailTemplateDocument Deserialize(string json)
        => Deserialize(json, null);

    /// <summary>
    /// Deserializes a document from JSON with explicit import limits: the JSON nesting depth is bounded
    /// by <see cref="EmailImportOptions.MaxNestingDepth"/>, so a hostile payload is refused instead of
    /// being parsed. <see cref="Options"/> itself stays untouched (the bound is a per-call copy).
    /// </summary>
    /// <exception cref="EmailTemplateSerializationException">
    /// The JSON is malformed, deeper than the limit, references an unknown block type, or was written by
    /// a newer schema.
    /// </exception>
    public static EmailTemplateDocument Deserialize(string json, EmailImportOptions? options)
    {
        ArgumentNullException.ThrowIfNull(json);

        // The input limits are checked before the payload is parsed: an oversized document must not
        // reach the JSON reader at all.
        if (options is not null)
        {
            var inputBytes = Encoding.UTF8.GetByteCount(json);
            if (inputBytes > options.MaxInputBytes)
            {
                throw new EmailTemplateSerializationException(
                    $"The template JSON is {inputBytes} bytes, over the configured limit of " +
                    $"{options.MaxInputBytes} bytes.",
                    SerializationKeys.InputTooLarge);
            }
        }

        EmailTemplateDocument document;
        try
        {
            // One parse for both the schema-version header and the document (the header must be known
            // before the body is interpreted, but a second parse of the payload would be wasted work on
            // every API request).
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = JsonMaxDepth(options) });
            var version = ReadSchemaVersion(parsed.RootElement);
            if (version > EmailTemplateDocument.CurrentSchemaVersion)
            {
                throw new EmailTemplateSerializationException(
                    $"The template JSON uses schema version {version}, but this version of " +
                    $"{nameof(EmailTemplateDocument)} supports at most " +
                    $"{EmailTemplateDocument.CurrentSchemaVersion}. Upgrade Tempo.Blazor.EmailTemplates.Abstractions.",
                    SerializationKeys.UnsupportedSchemaVersion);
            }

            document = parsed.RootElement.Deserialize<EmailTemplateDocument>(ForImport(options))
                ?? throw new EmailTemplateSerializationException("The template JSON deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw Malformed(ex, json);
        }

        // Migration v → Current. Version 1 is the only schema so far, so this is the whole migration
        // step today; a later version adds its upgrade here.
        document.SchemaVersion = EmailTemplateDocument.CurrentSchemaVersion;

        // Same document limits as the MJML importer, so an import can never accept a document than a
        // save-time check or the JSON path rejects. The first violation names the localization key.
        if (options is not null)
        {
            var limits = new EmailDocumentLimitsValidator(options).Validate(document);
            if (limits.Count > 0)
            {
                throw new EmailTemplateSerializationException(
                    $"The template JSON violates the import limit '{limits[0].Key}'.",
                    limits[0].Key);
            }
        }

        return document;
    }

    /// <summary>
    /// Reads the schema version header of the JSON without deserializing the document (useful to decide
    /// about a migration or to build a friendly error). A missing (or non-positive) version means
    /// version 1.
    /// </summary>
    /// <exception cref="EmailTemplateSerializationException">The JSON is malformed or the version is not a number.</exception>
    public static int ReadSchemaVersion(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = JsonMaxDepth(null) });
            return ReadSchemaVersion(parsed.RootElement);
        }
        catch (JsonException ex)
        {
            throw Malformed(ex, json);
        }
    }

    /// <summary>Creates an independent deep copy of a single block (same identifiers).</summary>
    public static EmailBlockBase CloneBlock(EmailBlockBase block)
        => Clone(block);

    /// <summary>Creates an independent deep copy of any model value via a JSON round-trip.</summary>
    public static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options)!;
    }

    /// <summary>The JSON property name the schema version is written under (the camelCase of the member).</summary>
    private static string SchemaVersionPropertyName =>
        Options.PropertyNamingPolicy?.ConvertName(nameof(EmailTemplateDocument.SchemaVersion))
        ?? nameof(EmailTemplateDocument.SchemaVersion);

    /// <summary>
    /// Reads the schema version header of an already parsed JSON root. A version that is not a positive
    /// integer, or that appears more than once (in any casing), is a corrupt header — never a guess.
    /// </summary>
    private static int ReadSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return EmailTemplateDocument.CurrentSchemaVersion;
        }

        int? version = null;
        foreach (var property in root.EnumerateObject())
        {
            if (!IsSchemaVersionProperty(property.Name))
            {
                continue;
            }

            if (version is not null)
            {
                throw new EmailTemplateSerializationException(
                    "The template JSON declares schemaVersion more than once.",
                    SerializationKeys.InvalidSchemaVersion);
            }

            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetInt32(out var declared) || declared <= 0)
            {
                throw new EmailTemplateSerializationException(
                    "The template JSON schemaVersion is not a positive number.",
                    SerializationKeys.InvalidSchemaVersion);
            }

            version = declared;
        }

        return version ?? EmailTemplateDocument.CurrentSchemaVersion;
    }

    private static bool IsSchemaVersionProperty(string name)
        => string.Equals(name, SchemaVersionPropertyName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The per-call serialization options: the canonical <see cref="Options"/> plus the JSON depth bound
    /// derived from the model nesting limit.
    /// </summary>
    private static JsonSerializerOptions ForImport(EmailImportOptions? options)
        => OptionsFor(JsonMaxDepth(options));

    /// <summary>A mutable copy of the canonical <see cref="Options"/> with the given JSON depth bound.</summary>
    private static JsonSerializerOptions OptionsFor(int maxDepth)
        => new(Options) { MaxDepth = Math.Clamp(maxDepth, 1, MaxJsonDepth) };

    /// <summary>
    /// Derives the JSON parser bound from the model nesting limit. It is deliberately not
    /// <see cref="EmailImportOptions.MaxNestingDepth"/> itself: one block nesting level costs several
    /// JSON levels, so the literal value would reject documents that are well within the model limit.
    /// </summary>
    private static int JsonMaxDepth(EmailImportOptions? options)
    {
        if (options is null)
        {
            // A caller that passes no limits must never get a *stricter* bound than before import limits
            // existed (System.Text.Json's 64), and must still read any document the serializer can write
            // — hence the floor of 64 and the serializer's own nesting cap as the ceiling.
            return Math.Max(
                DefaultJsonDepth,
                JsonEnvelopeDepth + JsonLevelsPerNestingLevel * (MaxSerializableNesting + 1));
        }

        var derived = (long)JsonEnvelopeDepth + (long)JsonLevelsPerNestingLevel * options.MaxNestingDepth;
        return (int)Math.Clamp(derived, JsonEnvelopeDepth, MaxJsonDepth);
    }

    /// <summary>
    /// Wraps a parser failure. STJ's unknown-discriminator message does not always include the offending
    /// token; it is recovered so the surfaced error names the unknown block type (forward compatibility),
    /// and a generic parse failure carries the <see cref="SerializationKeys.Malformed"/> key.
    /// </summary>
    private static EmailTemplateSerializationException Malformed(Exception ex, string json)
    {
        var token = TryExtractDiscriminator(json, ex);
        return token is not null
            ? new EmailTemplateSerializationException(
                $"Unknown email block type '{token}' in the template JSON.", ex)
            : new EmailTemplateSerializationException(
                $"The template JSON could not be parsed: {ex.Message}", SerializationKeys.Malformed, ex);
    }

    /// <summary>
    /// Names the unknown block type a parse failure is about. STJ does not always put the offending
    /// token in the message, so the token is looked up in the JSON itself and only reported when the
    /// message really mentions it — otherwise the failure is a generic malformed JSON (the first
    /// single-quoted token of a syntax error is not a block type).
    /// </summary>
    private static string? TryExtractDiscriminator(string json, Exception ex)
    {
        foreach (Match match in TypeDiscriminatorPattern().Matches(json))
        {
            var token = match.Groups["token"].Value;
            if (token.Length > 0 && ex.Message.Contains(token, StringComparison.Ordinal))
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>A <c>$type</c> discriminator value of a serialized block.</summary>
    [GeneratedRegex("\"\\$type\"\\s*:\\s*\"(?<token>[^\"]+)\"", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TypeDiscriminatorPattern();
}
