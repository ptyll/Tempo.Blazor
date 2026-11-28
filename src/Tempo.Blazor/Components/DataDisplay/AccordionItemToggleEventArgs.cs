namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// Describes one user-driven expansion change of a <see cref="TmAccordionItem"/> inside a
/// <see cref="TmAccordion"/> — raised through <see cref="TmAccordion.OnItemToggled"/> for the
/// clicked item and for every sibling the single mode closes implicitly.
/// </summary>
/// <param name="Key">The item's <see cref="TmAccordionItem.Key"/>, or <c>null</c> for a keyless item.</param>
/// <param name="Expanded">The requested expansion state (<c>false</c> for an implicit sibling close).</param>
/// <param name="Index">The item's index within the accordion (registration order — DOM order for a static item list; an item inserted conditionally later is appended).</param>
public sealed record AccordionItemToggleEventArgs(string? Key, bool Expanded, int Index);
