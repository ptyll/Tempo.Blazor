using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Finds the blocked URL schemes of the model links the <see cref="MjmlGenerator"/> rewrites to <c>#</c>
/// <b>before</b> the generated markup is checked.
/// <para>
/// Every other model link (button, image, navbar, social, carousel, hero/wrapper background) reaches the
/// markup as an attribute, so <see cref="MarkupGuard"/> sees it after substitution and reports it itself.
/// A table cell's <see cref="EmailTableCell.Href"/> is different: the generator already replaced the value
/// in the markup (fail-closed for the export path that runs without the guard), so the guard can no longer
/// recognise the scheme. This scanner is what lets the render still warn and audit it (§3.7a).
/// </para>
/// </summary>
internal static class ModelLinkScanner
{
    /// <summary>The distinct blocked schemes of all table-cell links, in a stable order.</summary>
    public static IReadOnlyList<string> BlockedTableCellSchemes(EmailTemplateDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var schemes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var table in DocumentTree.AllBlocks(document).OfType<EmailTableBlock>())
            foreach (var cell in table.Rows.SelectMany(row => row.Cells))
            {
                if (UrlSchemePolicy.FindBlockedScheme(cell.Href) is { } scheme) schemes.Add(scheme);
            }

        return schemes.ToList();
    }
}
