namespace Tempo.Blazor.Components.AI;

/// <summary>
/// One committed edit of a memory: the values the reader left in the editor plus the set of fields that actually
/// MOVED.
/// <para>
/// A host applies only what changed instead of diffing two snapshots of its own store, and it can refuse a field it
/// owns: <see cref="Fields"/> is a subset of the panel's <c>EditableFields</c>, never a superset. The record is reported
/// through <c>OnEditItem</c>; the legacy text-only <c>OnEdit</c> callback keeps working exactly as before for a host that
/// has not opted into the extra fields.
/// </para>
/// </summary>
/// <param name="Id">The id of the edited memory — the only identity the host needs to find its own row.</param>
/// <param name="Text">The fact as the reader left it in the editor.</param>
/// <param name="Category">The category as the reader left it, or the unchanged one when the field was not editable.</param>
/// <param name="Source">The source as the reader left it, or the unchanged one when the field was not editable.</param>
/// <param name="Fields">Which of the three fields the edit actually changed.</param>
public sealed record MemoryEdit(
    string Id,
    string Text,
    string? Category = null,
    string? Source = null,
    MemoryEditableFields Fields = MemoryEditableFields.Text);
