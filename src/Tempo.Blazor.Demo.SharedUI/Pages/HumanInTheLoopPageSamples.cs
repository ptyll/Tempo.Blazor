using Tempo.Blazor.Abstractions.Models.Approvals;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// Sample data of the <c>/human-in-the-loop</c> demo page: the document paragraph the inline diff compares,
/// and the twelve invoices the review queue puts in front of the reader.
/// </summary>
/// <remarks>
/// These are demo DATA, not user-interface text — invoice numbers, supplier names and the paragraph an "AI
/// rewrite" proposes are content, the same way a chat demo ships sample messages. Every label, heading,
/// button, status and announcement on the page itself resolves through <c>ITmLocalizer</c>.
/// </remarks>
internal static class HumanInTheLoopPageSamples
{
    /// <summary>
    /// The paragraph before the rewrite. It differs from <see cref="ProposedParagraph"/> in exactly three
    /// places (decided→agreed, postpone→delay, complete→finished), which is what makes "accept two of the
    /// three changes" a meaningful demonstration: the result is a mixture, never one of the two inputs.
    /// </summary>
    internal const string OriginalParagraph =
        "The team reviewed the draft and decided to postpone the launch until the documentation is complete.";

    /// <summary>The paragraph the rewrite proposes — the same text with all three changes applied.</summary>
    internal const string ProposedParagraph =
        "The team reviewed the draft and agreed to delay the launch until the documentation is finished.";

    /// <summary>
    /// The text a reader produces by accepting the first two changes and leaving the third one pending: the
    /// accepted wording replaces the first two, the last one keeps the original. It is what the demo's
    /// result line must show, and what the E2E lane asserts after two decisions.
    /// </summary>
    internal const string TwoOfThreeAcceptedResult =
        "The team reviewed the draft and agreed to delay the launch until the documentation is complete.";

    /// <summary>
    /// The twelve invoices the queue reviews. Risk and priority deliberately disagree, so switching
    /// <c>SortMode</c> is visible and a test cannot pass on an accidentally ordered list.
    /// </summary>
    internal static IReadOnlyList<ReviewDocument> ReviewItems { get; } =
    [
        new("inv-0042", "INV-2026-0042", "Northwind Systems", ApprovalRisk.High, 2),
        new("inv-0043", "INV-2026-0043", "Contoso Ltd", ApprovalRisk.Medium, 0),
        new("inv-0044", "INV-2026-0044", "Fabrikam", ApprovalRisk.Critical, 1),
        new("inv-0045", "INV-2026-0045", "Tailspin Toys", ApprovalRisk.Low, 0),
        new("inv-0046", "INV-2026-0046", "Adatum", ApprovalRisk.High, 0),
        new("inv-0047", "INV-2026-0047", "Proseware", ApprovalRisk.Medium, 3),
        new("inv-0048", "INV-2026-0048", "Litware", ApprovalRisk.Critical, 0),
        new("inv-0049", "INV-2026-0049", "Wingtip", ApprovalRisk.Low, 1),
        new("inv-0050", "INV-2026-0050", "Woodgrove", ApprovalRisk.Medium, 1),
        new("inv-0051", "INV-2026-0051", "Northwind Systems", ApprovalRisk.High, 1),
        new("inv-0052", "INV-2026-0052", "Contoso Ltd", ApprovalRisk.Low, 2),
        new("inv-0053", "INV-2026-0053", "Fabrikam", ApprovalRisk.Medium, 0),
    ];

    /// <summary>One reviewable invoice: identity, number, supplier, risk and review priority.</summary>
    /// <param name="Id">Stable id the queue selects on.</param>
    /// <param name="Number">Invoice number, the row's title.</param>
    /// <param name="Supplier">Supplier name, the row's subtitle.</param>
    /// <param name="Risk">Risk the queue sorts by.</param>
    /// <param name="Priority">Review priority; a higher value ranks first.</param>
    internal sealed record ReviewDocument(
        string Id,
        string Number,
        string Supplier,
        ApprovalRisk Risk,
        int Priority);
}
