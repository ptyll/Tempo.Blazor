using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Builds conditional visibility rules for signing fields.</summary>
public partial class TmConditionBuilder : IAsyncDisposable
{
    private static readonly SigningConditionOperation[] Operations = Enum.GetValues<SigningConditionOperation>();

    /// <summary>A condition plus its stable row identity: the Guid keys the rendered row
    /// (<c>@key</c>), so a move travels the DOM row (and its focus) with the condition. The id is
    /// settable so a rebuild can reuse the id of the row that already occupies that position.</summary>
    private sealed class ConditionRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public SigningFieldCondition Condition { get; set; } = new();
    }

    private readonly List<ConditionRow> _rows = [];
    private readonly Dictionary<Guid, string> _valueDrafts = new();
    private IReadOnlyList<SigningFieldCondition>? _lastConditions;
    private SigningEditorInterop? _interop;
    private string? _pendingFocusSelector;
    private string? _moveAnnouncement;

    /// <summary>Per-instance scope attribute for the remove-action focus selector (never a DOM id —
    /// two builders on one page must not collide).</summary>
    private readonly string _builderId = $"tm-condition-builder-{Guid.NewGuid():N}";

    /// <summary>Injects the JS runtime for the remove/move focus hand-off (best-effort).</summary>
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>All fields in the current signing template.</summary>
    [Parameter] public IReadOnlyList<SigningField> Fields { get; set; } = [];

    /// <summary>Field identifier whose conditions are currently edited.</summary>
    [Parameter] public string? CurrentFieldUuid { get; set; }

    /// <summary>Condition rules currently assigned to the edited field.</summary>
    [Parameter] public IReadOnlyList<SigningFieldCondition> Conditions { get; set; } = [];

    /// <summary>Callback invoked when the condition rules change.</summary>
    [Parameter] public EventCallback<IReadOnlyList<SigningFieldCondition>> ConditionsChanged { get; set; }

    /// <summary>Whether the builder controls are disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Culture used to resolve localized field and option labels.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Fallback culture used when localized labels are missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private IEnumerable<SigningField> ConditionSourceFields => Fields.Where(IsConditionSourceField);

    private bool HasValidationErrors => _rows.Any(row => !IsBlank(row.Condition)
        && (IsValueMissing(row.Condition) || CreatesCycle(row.Condition)));

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-condition-builder" };
            AddClass(classes, Disabled, "tm-condition-builder--disabled");
            AddClass(classes, HasValidationErrors, "tm-condition-builder--invalid");

            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_lastConditions, Conditions))
        {
            return;
        }

        var incoming = (Conditions ?? []).Select(CloneAndNormalize).ToArray();

        // Hosts (the demo page, TmSigningFieldEditorPanel) echo a NEW list after every change.
        // Rebuilding for an equal echo minted fresh Guids, so @key recreated every row and the
        // focus queued for the surviving row landed on <body>. An equal list keeps the rows —
        // but only when there are rows to keep. An empty list against empty rows is the first
        // render (or a cleared builder), which still needs its trailing blank draft.
        if (_rows.Count > 0 && MatchesCurrentNonBlankRows(incoming))
        {
            _lastConditions = Conditions;
            return;
        }

        var count = incoming.Length;
        var reused = new List<ConditionRow>(count);
        for (var i = 0; i < count; i++)
        {
            reused.Add(new ConditionRow
            {
                Id = i < _rows.Count ? _rows[i].Id : Guid.NewGuid(),
                Condition = incoming[i]
            });
        }

        _rows.Clear();
        _rows.AddRange(reused);
        _valueDrafts.Clear();

        if (_rows.Count == 0)
        {
            _rows.Add(new ConditionRow());
        }

        _lastConditions = Conditions;
    }

    /// <summary>
    /// Whether <paramref name="incoming"/> is the same non-blank condition list the rows already
    /// show, in order, by value (FieldUuid / Action / Value / Operation). A trailing blank draft
    /// the builder itself added is not part of the echoed list and does not count as a difference.
    /// </summary>
    private bool MatchesCurrentNonBlankRows(SigningFieldCondition[] incoming)
    {
        var current = _rows
            .Select(row => row.Condition)
            .Where(condition => !IsBlank(condition))
            .ToArray();

        if (current.Length != incoming.Length)
        {
            return false;
        }

        for (var i = 0; i < current.Length; i++)
        {
            if (!SameCondition(current[i], incoming[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameCondition(SigningFieldCondition left, SigningFieldCondition right)
    {
        return string.Equals(left.FieldUuid, right.FieldUuid, StringComparison.Ordinal)
            && left.Action == right.Action
            && string.Equals(left.Value, right.Value, StringComparison.Ordinal)
            && left.Operation == right.Operation;
    }

    /// <summary>
    /// Applies the focus hand-off AFTER the render that moved the rows (phase 7 review): a selector
    /// captured before the render would hit the DOM before Blazor rebuilt it. The dashed Add action
    /// is the fallback when the primary target no longer exists.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocusSelector is null)
        {
            return;
        }

        var selector = _pendingFocusSelector;
        _pendingFocusSelector = null;

        _interop ??= new SigningEditorInterop(JS);
        // Stay on the renderer sync context: the focus hand-off runs after the render that
        // rebuilt the rows, and a captured context lets a later render see the cleared selector.
        if (!await _interop.FocusSelectorAsync(selector))
        {
            await _interop.FocusSelectorAsync(AddActionSelector);
        }
    }

    private string AddActionSelector => $"[data-condition-builder='{_builderId}'] .tm-condition-builder__add";

    /// <summary>Queues the focus move onto <paramref name="selector"/> for the next render; the
    /// builder's Add action is the fallback target.</summary>
    private void RequestFocus(string selector)
    {
        _pendingFocusSelector = selector;
    }

    private static void AddClass(List<string> classes, bool condition, string cssClass)
    {
        if (condition)
        {
            classes.Add(cssClass);
        }
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    /// <summary>
    /// Moves a row one slot up (<paramref name="delta"/> = -1) or down (+1; Q-S7 — the keyboard
    /// alternative to drag &amp; drop). The conditions swap wholesale; the join operation stays
    /// POSITIONAL — the leading row never carries one, so after a move its hidden operation
    /// normalizes back to <see cref="SigningConditionOperation.And"/>. The focus stays INSIDE the
    /// moved row (phase 7 review): the same-direction button at the new position, or the opposite
    /// one when that direction hit the edge, and the result is announced.
    /// </summary>
    private async Task MoveConditionAsync(int index, int delta)
    {
        var target = index + delta;
        if (Disabled || target < 0 || target >= _rows.Count)
        {
            return;
        }

        (_rows[index], _rows[target]) = (_rows[target], _rows[index]);
        _rows[0].Condition.Operation = SigningConditionOperation.And;

        var label = GetConditionLabel(_rows[target].Condition);
        _moveAnnouncement = string.Format(Loc["TmConditionBuilder_ConditionMoved"], label, target + 1);
        var sameDirectionEnabled = delta < 0 ? target > 0 : target < _rows.Count - 1;
        var buttonClass = sameDirectionEnabled
            ? (delta < 0 ? "tm-condition-builder__move-up" : "tm-condition-builder__move-down")
            : (delta < 0 ? "tm-condition-builder__move-down" : "tm-condition-builder__move-up");
        RequestFocus($"[data-condition-builder='{_builderId}'] .tm-condition-builder__row[data-row-id='{_rows[target].Id}'] .{buttonClass}");

        await NotifyChangedAsync();
    }

    private async Task HandleFieldChangedAsync(int index, string? value)
    {
        if (!TryGetCondition(index, out var condition))
        {
            return;
        }

        condition.FieldUuid = value ?? string.Empty;
        condition.Action = GetActionsFor(GetField(condition.FieldUuid)?.Type).First();
        condition.Value = null;
        _valueDrafts.Remove(_rows[index].Id);
        await NotifyChangedAsync();
    }

    private async Task HandleActionChangedAsync(int index, string? value)
    {
        if (!TryGetCondition(index, out var condition)
            || !Enum.TryParse<SigningConditionAction>(value, out var action))
        {
            return;
        }

        condition.Action = action;
        if (!ActionRequiresValue(action))
        {
            condition.Value = null;
            _valueDrafts.Remove(_rows[index].Id);
        }

        await NotifyChangedAsync();
    }

    /// <summary>The COMMIT path of a select-backed value (choice fields commit on change).</summary>
    private async Task HandleValueChangedAsync(int index, string? value)
    {
        if (!TryGetCondition(index, out var condition))
        {
            return;
        }

        condition.Value = string.IsNullOrWhiteSpace(value)
            ? null
            : value;

        _valueDrafts.Remove(_rows[index].Id);
        await NotifyChangedAsync();
    }

    private async Task HandleOperationChangedAsync(int index, string? value)
    {
        if (!TryGetCondition(index, out var condition)
            || !Enum.TryParse<SigningConditionOperation>(value, out var operation))
        {
            return;
        }

        condition.Operation = operation;
        if (!IsBlank(condition))
        {
            await NotifyChangedAsync();
        }
    }

    private async Task AddConditionAsync()
    {
        _rows.Add(new ConditionRow());
        await Task.CompletedTask;
    }

    private async Task RemoveConditionAsync(int index)
    {
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        _valueDrafts.Remove(_rows[index].Id);
        _rows.RemoveAt(index);

        if (_rows.Count == 0)
        {
            _rows.Add(new ConditionRow());
        }

        // The remove action deletes the row the user is acting on — the focus lands on the remove
        // button of the row that now occupies the removed position, or on the dashed Add action
        // when the last row collapsed into the empty draft. The move happens AFTER the render that
        // rebuilt the list (phase 7 review).
        RequestFocus(index < _rows.Count
            ? $"[data-condition-builder='{_builderId}'] .tm-condition-builder__row[data-row-id='{_rows[index].Id}'] .tm-condition-builder__remove"
            : AddActionSelector);

        await NotifyChangedAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_interop is not null)
        {
            await _interop.DisposeAsync();
            _interop = null;
        }
    }

    // ── Value draft buffers (phase 7 review: keep the raw text while typing) ─────────

    /// <summary>The displayed value: the live DRAFT while the user types, the committed value
    /// otherwise. The draft is dropped on blur / Enter, where the commit normalizes.</summary>
    private string GetDraftValue(ConditionRow row, string committed)
    {
        return _valueDrafts.TryGetValue(row.Id, out var draft) ? draft : committed;
    }

    private Task HandleDraftChanged(ConditionRow row, string? value)
    {
        if (Disabled)
        {
            return Task.CompletedTask;
        }

        // No normalization, no notification: the keystroke must not rebuild the rows (a blanking
        // keystroke would wipe the value, and every keystroke would fire ConditionsChanged).
        _valueDrafts[row.Id] = value ?? string.Empty;
        return Task.CompletedTask;
    }

    private Task CommitDraftAsync(ConditionRow row)
    {
        if (Disabled || !_valueDrafts.Remove(row.Id, out var draft))
        {
            return Task.CompletedTask;
        }

        var index = _rows.FindIndex(candidate => candidate.Id == row.Id);
        return index < 0 ? Task.CompletedTask : HandleValueChangedAsync(index, draft);
    }

    private Task CommitDraftOnEnterAsync(KeyboardEventArgs args, ConditionRow row)
    {
        return string.Equals(args.Key, "Enter", StringComparison.Ordinal)
            ? CommitDraftAsync(row)
            : Task.CompletedTask;
    }

    private bool TryGetCondition(int index, out SigningFieldCondition condition)
    {
        if (index < 0 || index >= _rows.Count)
        {
            condition = default!;
            return false;
        }

        condition = _rows[index].Condition;
        return true;
    }

    /// <summary>The name the move announcement carries: the field label, or the localized
    /// "Condition" placeholder for a draft row without a field yet.</summary>
    private string GetConditionLabel(SigningFieldCondition condition)
    {
        var field = GetField(condition.FieldUuid);
        return field is null
            ? Loc["TmConditionBuilder_Condition"]
            : GetFieldLabel(field);
    }

    private Task NotifyChangedAsync()
    {
        var materialized = _rows
            .Select(row => row.Condition)
            .Where(condition => !IsBlank(condition))
            .Select(Clone)
            .ToArray();

        // The list we just emitted is already what the rows show. Recording it means a host that
        // writes the callback argument straight back (same instance) is a no-op, and a host that
        // clones it falls through to the value comparison above instead of a Guid rebuild.
        _lastConditions = materialized;
        return ConditionsChanged.InvokeAsync(materialized);
    }

    private SigningFieldCondition CloneAndNormalize(SigningFieldCondition condition)
    {
        var clone = Clone(condition);
        var actions = GetActionsFor(GetField(clone.FieldUuid)?.Type);

        if (!actions.Contains(clone.Action))
        {
            clone.Action = actions.First();
            clone.Value = null;
        }

        if (!ActionRequiresValue(clone.Action))
        {
            clone.Value = null;
        }

        return clone;
    }

    private static SigningFieldCondition Clone(SigningFieldCondition condition)
    {
        return new SigningFieldCondition
        {
            FieldUuid = condition.FieldUuid,
            Action = condition.Action,
            Value = condition.Value,
            Operation = condition.Operation
        };
    }

    private bool IsConditionSourceField(SigningField field)
    {
        return !string.Equals(field.Uuid, CurrentFieldUuid, StringComparison.Ordinal)
            && field.Type is not SigningFieldType.Heading
            && field.Type is not SigningFieldType.Strikethrough;
    }

    private SigningField? GetField(string? fieldUuid)
    {
        return string.IsNullOrWhiteSpace(fieldUuid)
            ? null
            : Fields.FirstOrDefault(field => string.Equals(field.Uuid, fieldUuid, StringComparison.Ordinal));
    }

    private string GetFieldLabel(SigningField field)
    {
        return SigningTextResolver.FieldLabel(field, Culture, FallbackCulture, Loc);
    }

    private string GetFieldTypeLabel(SigningFieldType type)
    {
        return SigningTextResolver.FieldTypeLabel(type, Loc);
    }

    private static IReadOnlyList<SigningConditionAction> GetActionsFor(SigningFieldType? type)
    {
        return type switch
        {
            SigningFieldType.Checkbox =>
            [
                SigningConditionAction.Checked,
                SigningConditionAction.Unchecked
            ],
            SigningFieldType.Radio or SigningFieldType.Select =>
            [
                SigningConditionAction.Equal,
                SigningConditionAction.NotEqual
            ],
            SigningFieldType.Multiple =>
            [
                SigningConditionAction.Contains,
                SigningConditionAction.DoesNotContain
            ],
            SigningFieldType.Number =>
            [
                SigningConditionAction.Empty,
                SigningConditionAction.NotEmpty,
                SigningConditionAction.Equal,
                SigningConditionAction.NotEqual,
                SigningConditionAction.GreaterThan,
                SigningConditionAction.LessThan
            ],
            _ =>
            [
                SigningConditionAction.Empty,
                SigningConditionAction.NotEmpty
            ]
        };
    }

    private string GetActionLabel(SigningConditionAction action)
    {
        return action switch
        {
            SigningConditionAction.Checked => Loc["TmConditionBuilder_Checked"],
            SigningConditionAction.Unchecked => Loc["TmConditionBuilder_Unchecked"],
            SigningConditionAction.Equal => Loc["TmConditionBuilder_Equal"],
            SigningConditionAction.NotEqual => Loc["TmConditionBuilder_NotEqual"],
            SigningConditionAction.Contains => Loc["TmConditionBuilder_Contains"],
            SigningConditionAction.DoesNotContain => Loc["TmConditionBuilder_DoesNotContain"],
            SigningConditionAction.Empty => Loc["TmConditionBuilder_Empty"],
            SigningConditionAction.NotEmpty => Loc["TmConditionBuilder_NotEmpty"],
            SigningConditionAction.GreaterThan => Loc["TmConditionBuilder_GreaterThan"],
            SigningConditionAction.LessThan => Loc["TmConditionBuilder_LessThan"],
            _ => action.ToString()
        };
    }

    private string GetOptionLabel(SigningFieldOption option)
    {
        return SigningTextResolver.OptionLabel(option, Culture, FallbackCulture);
    }

    private string GetOperationLabel(SigningConditionOperation operation)
    {
        return operation switch
        {
            SigningConditionOperation.And => Loc["TmConditionBuilder_And"],
            SigningConditionOperation.Or => Loc["TmConditionBuilder_Or"],
            _ => operation.ToString()
        };
    }

    private static bool IsChoiceField(SigningField? field)
    {
        return field?.Type is SigningFieldType.Radio or SigningFieldType.Select or SigningFieldType.Multiple;
    }

    private static bool ActionRequiresValue(SigningConditionAction action)
    {
        return action is SigningConditionAction.Equal
            or SigningConditionAction.NotEqual
            or SigningConditionAction.Contains
            or SigningConditionAction.DoesNotContain
            or SigningConditionAction.GreaterThan
            or SigningConditionAction.LessThan;
    }

    private static bool IsBlank(SigningFieldCondition condition)
    {
        return string.IsNullOrWhiteSpace(condition.FieldUuid);
    }

    private static bool IsValueMissing(SigningFieldCondition condition)
    {
        return ActionRequiresValue(condition.Action)
            && string.IsNullOrWhiteSpace(condition.Value);
    }

    private IEnumerable<string> GetValidationMessages(SigningFieldCondition condition)
    {
        if (IsBlank(condition))
        {
            yield break;
        }

        if (IsValueMissing(condition))
        {
            yield return Loc["TmConditionBuilder_MissingValue"];
        }

        if (CreatesCycle(condition))
        {
            yield return Loc["TmConditionBuilder_CycleDetected"];
        }
    }

    private bool CreatesCycle(SigningFieldCondition condition)
    {
        if (string.IsNullOrWhiteSpace(CurrentFieldUuid) || string.IsNullOrWhiteSpace(condition.FieldUuid))
        {
            return false;
        }

        return DependsOnCurrentField(condition.FieldUuid, []);
    }

    private bool DependsOnCurrentField(string sourceFieldUuid, HashSet<string> visited)
    {
        if (string.Equals(sourceFieldUuid, CurrentFieldUuid, StringComparison.Ordinal))
        {
            return true;
        }

        if (!visited.Add(sourceFieldUuid))
        {
            return false;
        }

        var sourceField = GetField(sourceFieldUuid);
        if (sourceField is null)
        {
            return false;
        }

        foreach (var nestedCondition in sourceField.Conditions)
        {
            if (DependsOnCurrentField(nestedCondition.FieldUuid, visited))
            {
                return true;
            }
        }

        return false;
    }
}
