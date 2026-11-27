using Tempo.Blazor.DocumentEditor.Models;

namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>What <see cref="TmDocumentCompareDialog.OnOpenComparison"/> reports: the comparison to show in the document region.</summary>
/// <param name="Result">The finished comparison.</param>
/// <param name="BlockId">The block whose change the reader picked in the list, or <c>null</c> to start at the top.</param>
/// <param name="BaseTitle">The heading of the old side.</param>
/// <param name="CompareTitle">The heading of the new side.</param>
public sealed record DocumentCompareOpenRequest(DocumentCompareResult Result, string? BlockId, string? BaseTitle, string? CompareTitle);

/// <summary>What <see cref="TmDocumentVersionPanel.OnShowDiff"/> reports: the two versions the reader picked.</summary>
/// <param name="Base">The version picked as "Base" (the old side).</param>
/// <param name="Compare">The version picked as "Compare" (the new side).</param>
public sealed record DocumentVersionDiffRequest(DocumentVersion Base, DocumentVersion Compare);
