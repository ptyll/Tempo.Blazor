namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// State the property inspector remembers for the lifetime of one editor: which collapsible groups the
/// user folded away (keyed per block type, so a Text block keeps its own Spacing state) and which field
/// was edited last (the target of the caret-less variable insertion fallback).
/// </summary>
public sealed class EmailInspectorState
{
    /// <summary>The group id of the collapsible "Advanced" section.</summary>
    public const string AdvancedGroup = "advanced";

    /// <summary>The group id of the collapsible "Spacing" section.</summary>
    public const string PaddingGroup = "padding";

    private readonly Dictionary<string, bool> _collapsedGroups = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the field the user last put the caret into. A variable inserted while the caret-aware
    /// JS interop is unavailable lands behind this field's content, matching what the JS path would do.
    /// </summary>
    public PropertyField? LastFocusedField { get; set; }

    /// <summary>
    /// The field whose variable picker is open (a property name, or <see cref="RichContentPicker"/> for the
    /// text block's rich editor), or <see langword="null"/>. It lives here - on the editor's inspector state -
    /// and not in the inspector components, because a desktop ↔ tablet layout flip re-creates those
    /// components while the user is still choosing a variable.
    /// </summary>
    public string? OpenVariablesField { get; private set; }

    /// <summary>The key of the text block's rich-editor picker in <see cref="OpenVariablesField"/>.</summary>
    public const string RichContentPicker = "(rich-content)";

    /// <summary>
    /// The text typed into the open picker's search field. Survives a layout flip with the picker, and is
    /// reset when the picker closes (decision EMAIL-VARIABLE-PICKER-FILTER).
    /// </summary>
    public string VariableFilter { get; set; } = string.Empty;

    /// <summary>The inspected node the picker state belongs to; another node discards the picker state.</summary>
    public Guid? InspectedId { get; set; }

    /// <summary>Opens the picker of <paramref name="field"/> (and closes any other).</summary>
    /// <param name="field">The property name, or <see cref="RichContentPicker"/>.</param>
    public void OpenVariables(string field)
    {
        if (!string.Equals(OpenVariablesField, field, StringComparison.Ordinal)) VariableFilter = string.Empty;
        OpenVariablesField = field;
    }

    /// <summary>Closes the picker of <paramref name="field"/> and forgets its filter; another field's picker is left alone.</summary>
    /// <param name="field">The property name, or <see cref="RichContentPicker"/>.</param>
    public void CloseVariables(string field)
    {
        if (!string.Equals(OpenVariablesField, field, StringComparison.Ordinal)) return;
        CloseVariables();
    }

    /// <summary>Closes whichever picker is open and forgets its filter.</summary>
    public void CloseVariables()
    {
        OpenVariablesField = null;
        VariableFilter = string.Empty;
    }

    /// <summary>Whether the group is collapsed for the given owner (block type); groups start expanded.</summary>
    /// <param name="owner">The label owner token (e.g. <c>Text</c>, <c>Section</c>).</param>
    /// <param name="group">The group id — <see cref="PaddingGroup"/> or <see cref="AdvancedGroup"/>.</param>
    public bool IsGroupCollapsed(string owner, string group)
        => _collapsedGroups.TryGetValue(Key(owner, group), out var collapsed) && collapsed;

    /// <summary>Stores the collapsed state of a group for one owner.</summary>
    /// <param name="owner">The label owner token (e.g. <c>Text</c>, <c>Section</c>).</param>
    /// <param name="group">The group id.</param>
    /// <param name="collapsed">Whether the group is collapsed.</param>
    public void SetGroupCollapsed(string owner, string group, bool collapsed)
        => _collapsedGroups[Key(owner, group)] = collapsed;

    /// <summary>Flips the collapsed state of a group and returns the new state.</summary>
    /// <param name="owner">The label owner token (e.g. <c>Text</c>, <c>Section</c>).</param>
    /// <param name="group">The group id.</param>
    /// <returns>The collapsed state after the flip.</returns>
    public bool ToggleGroup(string owner, string group)
    {
        var collapsed = !IsGroupCollapsed(owner, group);
        SetGroupCollapsed(owner, group, collapsed);
        return collapsed;
    }

    /// <summary>Forgets the remembered field (e.g. when the inspected target changes).</summary>
    public void ClearFocus() => LastFocusedField = null;

    private static string Key(string owner, string group) => owner + ":" + group;
}
