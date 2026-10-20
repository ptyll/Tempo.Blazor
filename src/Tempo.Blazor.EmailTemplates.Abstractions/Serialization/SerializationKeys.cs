namespace Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

/// <summary>Localization keys for template serialization diagnostics (never raw text).</summary>
public static class SerializationKeys
{
    /// <summary>The document uses a schema version newer than <see cref="Model.EmailTemplateDocument.CurrentSchemaVersion"/>.</summary>
    public const string UnsupportedSchemaVersion = "serialization.schema_version.unsupported";

    /// <summary>The schema version header is missing a number, is not positive or appears twice.</summary>
    public const string InvalidSchemaVersion = "serialization.schema_version.invalid";

    /// <summary>The JSON itself is malformed (not parseable, or an unsupported type).</summary>
    public const string Malformed = "serialization.malformed";

    /// <summary>The document nests blocks too deeply (or contains a block cycle) to be serialized.</summary>
    public const string TooDeeplyNested = "serialization.too_deeply_nested";

    /// <summary>The JSON is larger than <see cref="Import.EmailImportOptions.MaxInputBytes"/> (UTF-8 bytes).</summary>
    public const string InputTooLarge = "serialization.input_too_large";
}
