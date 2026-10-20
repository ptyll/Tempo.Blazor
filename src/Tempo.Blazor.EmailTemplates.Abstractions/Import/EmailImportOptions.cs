namespace Tempo.Blazor.EmailTemplates.Abstractions.Import;

/// <summary>
/// Limits applied when a template enters the application: the size of the MJML input, the number of
/// sections and blocks, and how deeply blocks may nest. They guard both the MJML importer and the JSON
/// deserialization against a hostile or broken document (a "billion laughs" style tree, a runaway
/// paste) and are reusable through
/// <see cref="Rendering.EmailDocumentLimitsValidator"/> before a document is saved.
/// </summary>
/// <remarks>
/// The defaults keep today's behaviour (<see cref="MaxInputBytes"/> is the 5 MB the import dialog has
/// always enforced — tightening it would be a breaking change, B14); a host may only raise or lower
/// them.
/// </remarks>
public sealed class EmailImportOptions
{
    /// <summary>
    /// Maximum size of the input in <b>UTF-8 bytes</b> (not in characters), default 5 MB. Exceeding it
    /// fails the import with <see cref="ImportKeys.InputTooLarge"/> instead of parsing the payload.
    /// </summary>
    public long MaxInputBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum number of sections in the imported document, including sections nested inside a wrapper
    /// (default 200). Exceeding it fails the import with
    /// <see cref="Rendering.DocumentValidationKeys.TooManySections"/>.
    /// </summary>
    public int MaxSections { get; set; } = 200;

    /// <summary>
    /// Maximum number of blocks in the imported document, including blocks nested in hero, group and
    /// wrapper containers (default 2000). Exceeding it fails the import with
    /// <see cref="Rendering.DocumentValidationKeys.TooManyBlocks"/>.
    /// </summary>
    public int MaxBlocks { get; set; } = 2000;

    /// <summary>
    /// Maximum nesting depth of blocks (default 8). A block directly in a section column is depth 1 and
    /// every hero/group/wrapper level adds one — the same convention
    /// <see cref="Layout.LayoutValidator"/> uses. Exceeding it fails the import with
    /// <see cref="Rendering.DocumentValidationKeys.TooDeeplyNested"/>.
    /// </summary>
    public int MaxNestingDepth { get; set; } = 8;
}
