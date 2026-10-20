using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Validates a document against <see cref="EmailImportOptions"/>: the number of sections, the number of
/// blocks and the block nesting depth. A host runs it on the server before saving (and the MJML importer
/// uses the very same validator, so an import can never accept a document the save-time check rejects).
/// Findings carry localization keys, never localized text.
/// </summary>
/// <remarks>
/// <see cref="EmailImportOptions.MaxInputBytes"/> has no meaning for an already materialized document: it
/// is enforced on the input, by <see cref="Import.MjmlImporter.Import(string, Import.IMjmlIncludeResolver, EmailImportOptions)"/>
/// and by <see cref="Serialization.EmailTemplateSerializer.Deserialize(string, EmailImportOptions?)"/>
/// (which rejects an oversized payload before parsing and runs this validator on the result).
/// The block walk is iterative, so a deeply nested or cyclic document is reported instead of
/// overflowing the stack.
/// </remarks>
public sealed class EmailDocumentLimitsValidator
{
    /// <summary>Initializes the validator with the limits to apply (defaults to <see cref="EmailImportOptions"/>).</summary>
    public EmailDocumentLimitsValidator(EmailImportOptions? options = null)
        => Options = options ?? new EmailImportOptions();

    /// <summary>Gets the limits this validator applies.</summary>
    public EmailImportOptions Options { get; }

    /// <summary>Validates the document and returns every violated limit (empty when it fits).</summary>
    public IReadOnlyList<DocumentValidationMessage> Validate(EmailTemplateDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var messages = new List<DocumentValidationMessage>();

        var sections = DocumentTree.AllSections(document).Count();
        if (sections > Options.MaxSections)
        {
            messages.Add(new DocumentValidationMessage(
                LayoutSeverity.Error, DocumentValidationKeys.TooManySections, nameof(EmailTemplateDocument.Sections)));
        }

        var blocks = DocumentTree.AllBlocks(document).Count();
        if (blocks > Options.MaxBlocks)
        {
            messages.Add(new DocumentValidationMessage(
                LayoutSeverity.Error, DocumentValidationKeys.TooManyBlocks, "blocks"));
        }

        // Iterative measurement first: a hostile document must not be turned into a deep recursion.
        if (DocumentNesting.FirstTooDeep(document, Options.MaxNestingDepth) is { } tooDeep)
        {
            messages.Add(new DocumentValidationMessage(
                LayoutSeverity.Error, DocumentValidationKeys.TooDeeplyNested, tooDeep.Block.Id.ToString()));
        }

        return messages;
    }
}
