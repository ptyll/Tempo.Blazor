using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The Gantt filter editor: one row per rule (<c>[Field] [operator] [value] [x]</c>), "+ Add filter" and a
/// transactional footer (Clear all | Cancel | Apply). The value editor follows the field: an enum select for
/// Status and Priority, a date input for Start / End / Deadline, text for everything else; the operators follow
/// it too. Nothing reaches the Gantt until Apply.
/// </summary>
public partial class TmGanttFilterPanel
{
    private enum FieldType { Text, Enum, Date }

    private sealed class EditableFilter
    {
        public string Field { get; set; } = "Title";
        public GanttFilterOperator Operator { get; set; } = GanttFilterOperator.Contains;
        public string? Value { get; set; }
    }

    private static readonly string[] KnownFields = ["Title", "Status", "Priority", "Start", "End", "Deadline"];

    private static readonly GanttFilterOperator[] TextOperators =
        [GanttFilterOperator.Contains, GanttFilterOperator.Equals, GanttFilterOperator.NotEquals, GanttFilterOperator.IsEmpty, GanttFilterOperator.IsNotEmpty];

    private static readonly GanttFilterOperator[] EnumOperators = [GanttFilterOperator.Equals, GanttFilterOperator.NotEquals];

    private static readonly GanttFilterOperator[] DateOperators = [GanttFilterOperator.Equals, GanttFilterOperator.Before, GanttFilterOperator.After];

    private static readonly GanttFilterOperator[] NullableDateOperators =
        [GanttFilterOperator.Equals, GanttFilterOperator.Before, GanttFilterOperator.After, GanttFilterOperator.IsEmpty, GanttFilterOperator.IsNotEmpty];

    private List<EditableFilter> _editFilters = [];
    private List<GanttFilter> _appliedSnapshot = [];
    private bool _wasShown;

    /// <summary>The applied filters. The editor starts from them and goes back to them on Cancel.</summary>
    [Parameter] public List<GanttFilter> Filters { get; set; } = [];

    /// <summary>Fires with the complete rule set when the user presses Apply.</summary>
    [Parameter] public EventCallback<IReadOnlyList<GanttFilter>> OnFiltersChanged { get; set; }

    /// <summary>Fires when the panel should close: its X button, Cancel, and (in the dialog) Apply and Escape.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// Presents the editor in a <c>TmModal</c> (a bottom sheet on a mobile viewport) instead of an inline strip:
    /// the modal owns the header and the footer, Apply closes it. <c>TmGantt</c> sets it for
    /// <see cref="GanttFilterPanelMode.Dialog"/>.
    /// </summary>
    [Parameter] public bool AsDialog { get; set; }

    /// <summary>
    /// Whether the dialog presentation (<see cref="AsDialog"/>) is shown. The host keeps the panel mounted and toggles
    /// this, so the modal can return the focus to its trigger on close. Ignored by the inline presentation.
    /// </summary>
    [Parameter] public bool Show { get; set; } = true;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // Reopening the dialog starts from the applied filters again, whatever the last close left behind.
        var opened = Show && !_wasShown;
        _wasShown = Show;
        if (opened && AsDialog)
        {
            ResetEdit();
            return;
        }

        // TmGantt hands over a fresh list on every render: rebuild the edit state only when the APPLIED filters
        // changed, so an unrelated parent render does not throw an in-progress edit away.
        if (_appliedSnapshot.SequenceEqual(Filters)) return;
        ResetEdit();
    }

    private void ResetEdit()
    {
        _appliedSnapshot = Filters.ToList();
        _editFilters = Filters.Select(Load).ToList();
    }

    private static EditableFilter Load(GanttFilter filter)
    {
        var editable = new EditableFilter { Field = filter.Field, Operator = filter.Operator, Value = filter.Value };
        if (TypeOf(editable.Field) == FieldType.Enum)
        {
            var domain = EnumValues(editable.Field);
            var canonical = domain.FirstOrDefault(v => string.Equals(v, editable.Value, StringComparison.OrdinalIgnoreCase));
            editable.Value = canonical ?? (string.IsNullOrWhiteSpace(editable.Value) ? domain[0] : editable.Value);
        }

        return editable;
    }

    private static FieldType TypeOf(string field) => field switch
    {
        "Status" or "Priority" => FieldType.Enum,
        "Start" or "End" or "Deadline" => FieldType.Date,
        _ => FieldType.Text,
    };

    private static IReadOnlyList<string> EnumValues(string field) => field == "Priority"
        ? Enum.GetNames<TmWorkItemPriority>()
        : Enum.GetNames<TmWorkItemStatus>();

    private static IReadOnlyList<GanttFilterOperator> AllowedOperators(string field) => field switch
    {
        "Status" or "Priority" => EnumOperators,
        "Start" or "End" => DateOperators,
        "Deadline" => NullableDateOperators,
        _ => TextOperators,
    };

    private static IReadOnlyList<GanttFilterOperator> OperatorsFor(EditableFilter filter)
    {
        var allowed = AllowedOperators(filter.Field);

        // A rule the host loaded with an operator the field type would not offer keeps it (it is not rewritten
        // just by opening the panel); picking another operator is then the user's choice.
        return allowed.Contains(filter.Operator) ? allowed : allowed.Append(filter.Operator).ToArray();
    }

    /// <summary>The fields the field select lists: the known ones plus a host-supplied one (e.g. a custom field) already in use.</summary>
    private IEnumerable<string> FieldsFor(EditableFilter filter) =>
        KnownFields.Contains(filter.Field) ? KnownFields : KnownFields.Append(filter.Field);

    private static bool NeedsValue(EditableFilter filter) =>
        filter.Operator is not GanttFilterOperator.IsEmpty and not GanttFilterOperator.IsNotEmpty;

    private string FieldLabel(string field) => field switch
    {
        "Title" => Loc["TmGantt_FilterFieldTitle"],
        "Status" => Loc["TmGantt_FilterFieldStatus"],
        "Priority" => Loc["TmGantt_FilterFieldPriority"],
        "Start" => Loc["TmGantt_Start"],
        "End" => Loc["TmGantt_End"],
        "Deadline" => Loc["TmGanttTaskPanel_Deadline"],
        _ => field.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) ? field["custom:".Length..] : field,
    };

    private string EnumLabel(string field, string value) => field == "Priority"
        ? Loc[$"GanttPriority_{value}"]
        : Loc[$"GanttStatus_{value}"];

    private bool IsKnownEnumValue(EditableFilter filter) =>
        EnumValues(filter.Field).Contains(filter.Value ?? string.Empty);

    private void AddFilter() =>
        _editFilters.Add(new EditableFilter());

    private void RemoveFilter(EditableFilter filter) =>
        _editFilters.Remove(filter);

    private void UpdateFilterField(EditableFilter filter, string field)
    {
        var previous = TypeOf(filter.Field);
        filter.Field = field;
        var type = TypeOf(field);

        var allowed = AllowedOperators(field);
        if (!allowed.Contains(filter.Operator))
        {
            filter.Operator = allowed[0];
        }

        // The value is only meaningful inside its own domain: an enum starts on its first member (an enum rule
        // never applies "nothing"), a date starts empty, and free text survives a text -> text change.
        filter.Value = type switch
        {
            FieldType.Enum => EnumValues(field)[0],
            FieldType.Date => null,
            _ => previous == FieldType.Text ? filter.Value : null,
        };
    }

    private void UpdateFilterOp(EditableFilter filter, GanttFilterOperator op) =>
        filter.Operator = op;

    private void UpdateFilterValue(EditableFilter filter, string? value) =>
        filter.Value = string.IsNullOrEmpty(value) ? null : value;

    private async Task ApplyAsync()
    {
        var filters = _editFilters
            // A rule that needs a value but has none (a freshly added row) filters nothing meaningful: drop it.
            .Where(f => !NeedsValue(f) || !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => new GanttFilter(f.Field, f.Operator, NeedsValue(f) ? f.Value : null))
            .ToList();
        await OnFiltersChanged.InvokeAsync(filters);

        if (AsDialog)
        {
            await OnClose.InvokeAsync();
        }
    }

    /// <summary>Clears the rows being edited; nothing is applied until Apply.</summary>
    private void ClearAll() =>
        _editFilters.Clear();

    private async Task CancelAsync()
    {
        ResetEdit();
        await OnClose.InvokeAsync();
    }
}
