namespace Tempo.Blazor.EmailTemplates.Abstractions.Model;

/// <summary>
/// The root of an email template: subject/preheader metadata, global <see cref="Styles"/>
/// and the ordered list of <see cref="Sections"/>. This is the canonical editable model that the
/// generator turns into MJML and the importer produces from MJML.
/// </summary>
public sealed class EmailTemplateDocument
{
    /// <summary>
    /// The schema version the serializer writes and the version a migration upgrades to. It is a
    /// property (not a <c>const</c>) so a future version can change it without recompiling consumers.
    /// </summary>
    public static int CurrentSchemaVersion => 1;

    /// <summary>
    /// Gets or sets the schema version of this document. The serializer always writes
    /// <see cref="CurrentSchemaVersion"/>; a document read without the property is version 1 and
    /// <see cref="Serialization.EmailTemplateSerializer.Deserialize(string)"/> migrates it upward.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Gets or sets the unique identifier of this document.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the human-readable template name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the email subject line (<c>mj-title</c> / mail subject).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Gets or sets the preview text shown in inboxes (<c>mj-preview</c>).</summary>
    public string? Preheader { get; set; }

    /// <summary>Gets or sets the language code (defaults to Czech, <c>"cs"</c>).</summary>
    public string Language { get; set; } = "cs";

    /// <summary>Gets or sets the global styles and head settings.</summary>
    public TemplateStyles Styles { get; set; } = new();

    /// <summary>Gets the ordered sections that make up the body.</summary>
    public IList<EmailSection> Sections { get; set; } = new List<EmailSection>();

    /// <summary>Gets or sets the creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the last update timestamp (UTC), when known.</summary>
    public DateTime? UpdatedAt { get; set; }
}
