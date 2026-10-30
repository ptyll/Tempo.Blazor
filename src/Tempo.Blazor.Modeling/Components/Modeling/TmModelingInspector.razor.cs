using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Displays details for a selected modeling element or relationship.
/// <para>
/// Fáze 14 (decision <c>MODELING-INSPECTOR-EDITING</c>): with <see cref="ReadOnly"/> (the
/// default, MO2) the inspector is the historical read-only panel. When the editor turns editing
/// on (<c>ReadOnly=false</c>), the name, the description and the SCALAR properties become inputs
/// that commit on blur/Enter (the toggle commits on click) and raise
/// <see cref="OnElementEdited"/> / <see cref="OnRelationshipEdited"/> with ONLY the changed
/// fields. Escape reverts the field. Semantic type, notation, source metadata, governance and
/// complex (object/array) JSON values are NEVER editable.
/// </para>
/// </summary>
public partial class TmModelingInspector
{
    /// <summary>Whether the inspector is read-only (MO2 default). The editor passes
    /// <c>false</c> when model editing is enabled for the current provider.</summary>
    [Parameter] public bool ReadOnly { get; set; } = true;

    /// <summary>Raised when an element field commit lands. The change carries only the edited
    /// fields (a null member means "unchanged").</summary>
    [Parameter] public EventCallback<ModelingElementChange> OnElementEdited { get; set; }

    /// <summary>Raised when a relationship field commit lands.</summary>
    [Parameter] public EventCallback<ModelingRelationshipChange> OnRelationshipEdited { get; set; }

    /// <summary>Raised for the inspector-scope Ctrl+Z (the editor's own undo stack — the diagram
    /// canvas keyboard scope is never involved).</summary>
    [Parameter] public EventCallback OnUndoRequested { get; set; }

    /// <summary>Raised for the inspector-scope Ctrl+Shift+Z / Ctrl+Y.</summary>
    [Parameter] public EventCallback OnRedoRequested { get; set; }

    /// <summary>Editor-owned actions rendered inside the header (the undo/redo pair and the
    /// save/discard buttons when the editor shows them). Null keeps the plain header.</summary>
    [Parameter] public RenderFragment? HeaderActions { get; set; }

    /// <summary>Whether a model save is currently in flight (review fix, phase 14): every
    /// editing input disables and the drafts are NOT re-seeded from the re-renders the saving
    /// state causes — a keystroke mid-save cannot be silently wiped.</summary>
    [Parameter] public bool Saving { get; set; }

    // ── Editing drafts ───────────────────────────────────────────────────────
    // The drafts belong to the SELECTED entity and are re-seeded only when the committed values
    // actually change (a commit, an undo, a reload) — never on an unrelated parent render, so a
    // keystroke cannot be patched back mid-typing (the controlled-input pitfall).

    private ModelingElementDto? _draftElement;
    private ModelingRelationshipDto? _draftRelationship;
    private string _nameDraft = string.Empty;
    private string _descriptionDraft = string.Empty;
    private Dictionary<string, string> _scalarDrafts = new(StringComparer.Ordinal);
    /// <summary>Localized name-required message; null while the name is valid.</summary>
    private string? _nameErrorText;

    private string? NameError => _nameErrorText;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        SeedDrafts();
    }

    private void SeedDrafts()
    {
        if (!IsEditing)
        {
            _draftElement = null;
            _draftRelationship = null;
            _nameErrorText = null;
            return;
        }

        if (Saving)
        {
            // A save re-renders the editor several times; the drafts are the user's in-flight
            // typing and the inputs are disabled — leave them alone (review fix, phase 14).
            return;
        }

        if (Element is not null)
        {
            var reseed = !ReferenceEquals(Element, _draftElement)
                || !string.Equals(Element.Name, _nameDraft, StringComparison.Ordinal)
                || !string.Equals(Element.Description, _descriptionDraft, StringComparison.Ordinal)
                || !ScalarDraftsMatch(Element.Properties);

            if (reseed)
            {
                _draftElement = Element;
                _draftRelationship = null;
                _nameDraft = Element.Name;
                _descriptionDraft = Element.Description;
                _scalarDrafts = Element.Properties
                    .Where(property => IsScalarEditable(property.Value))
                    .ToDictionary(property => property.Key, property => FormatPropertyValue(property.Value), StringComparer.Ordinal);
                _nameErrorText = null;
            }

            return;
        }

        if (Relationship is not null)
        {
            var reseed = !ReferenceEquals(Relationship, _draftRelationship)
                || !string.Equals(Relationship.Name, _nameDraft, StringComparison.Ordinal)
                || !ScalarDraftsMatch(Relationship.Properties);

            if (reseed)
            {
                _draftRelationship = Relationship;
                _draftElement = null;
                _nameDraft = Relationship.Name;
                _descriptionDraft = string.Empty;
                _scalarDrafts = Relationship.Properties
                    .Where(property => IsScalarEditable(property.Value))
                    .ToDictionary(property => property.Key, property => FormatPropertyValue(property.Value), StringComparer.Ordinal);
                _nameErrorText = null;
            }
        }
    }

    private bool ScalarDraftsMatch(IReadOnlyDictionary<string, JsonElement> properties)
    {
        foreach (var property in properties)
        {
            if (!IsScalarEditable(property.Value))
            {
                continue;
            }

            if (!_scalarDrafts.TryGetValue(property.Key, out var draft)
                || draft != FormatPropertyValue(property.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether editing inputs render (the editor opted in and something is selected).</summary>
    private bool IsEditing => !ReadOnly && (Element is not null || Relationship is not null);

    /// <summary>Whether the JSON value is an editable scalar: string, number or boolean.</summary>
    private static bool IsScalarEditable(JsonElement value)
        => value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    /// <summary>Whether the number is representable by the integer TmNumberInput (otherwise the
    /// scalar edits through a text field that parses invariantly).</summary>
    private static bool IsIntegerJsonNumber(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _);

    // ── Commit paths ─────────────────────────────────────────────────────────

    private void HandleNameInput(string value)
    {
        _nameDraft = value;
        if (_nameErrorText is not null && !string.IsNullOrWhiteSpace(_nameDraft))
        {
            _nameErrorText = null;
        }
    }

    private Task HandleNameKeyDownAsync(KeyboardEventArgs args)
    {
        if (string.Equals(args.Key, "Escape", StringComparison.Ordinal))
        {
            RevertNameDraft();
            return Task.CompletedTask;
        }

        if (string.Equals(args.Key, "Enter", StringComparison.Ordinal))
        {
            return CommitNameAsync();
        }

        return Task.CompletedTask;
    }

    private Task CommitNameAsync()
    {
        if (Element is not null)
        {
            if (string.IsNullOrWhiteSpace(_nameDraft))
            {
                if (!string.Equals(_nameDraft, Element.Name, StringComparison.Ordinal))
                {
                    _nameErrorText = Loc["TmModelingInspector_NameRequired"];
                }

                return Task.CompletedTask;
            }

            if (string.Equals(_nameDraft, Element.Name, StringComparison.Ordinal))
            {
                _nameErrorText = null;
                return Task.CompletedTask;
            }

            return OnElementEdited.InvokeAsync(new ModelingElementChange
            {
                ElementId = Element.Id,
                Name = _nameDraft
            });
        }

        if (Relationship is not null)
        {
            if (string.IsNullOrWhiteSpace(_nameDraft))
            {
                // An empty relationship name is refused the same way an element's is (review
                // fix, phase 14) — the field shows the localized NameRequired error.
                if (!string.Equals(_nameDraft, Relationship.Name, StringComparison.Ordinal))
                {
                    _nameErrorText = Loc["TmModelingInspector_NameRequired"];
                }

                return Task.CompletedTask;
            }

            if (string.Equals(_nameDraft, Relationship.Name, StringComparison.Ordinal))
            {
                _nameErrorText = null;
                return Task.CompletedTask;
            }

            return OnRelationshipEdited.InvokeAsync(new ModelingRelationshipChange
            {
                RelationshipId = Relationship.Id,
                Name = _nameDraft
            });
        }

        return Task.CompletedTask;
    }

    private void RevertNameDraft()
    {
        _nameDraft = Element?.Name ?? Relationship?.Name ?? string.Empty;
        _nameErrorText = null;
    }

    private Task HandleDescriptionInput(string value)
    {
        _descriptionDraft = value;
        return Task.CompletedTask;
    }

    /// <summary>Escape on the description textarea reverts the DISPLAYED value to the committed
    /// one (review fix, phase 14): the draft goes back and the re-render pushes the textarea's
    /// bound value — unlike the name input this is not a native-revert control.</summary>
    private Task HandleDescriptionKeyDownAsync(KeyboardEventArgs args)
    {
        if (!string.Equals(args.Key, "Escape", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var committed = Element?.Description ?? string.Empty;
        if (!string.Equals(_descriptionDraft, committed, StringComparison.Ordinal))
        {
            _descriptionDraft = committed;
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    private Task CommitDescriptionAsync()
    {
        if (Element is null || string.Equals(_descriptionDraft, Element.Description, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return OnElementEdited.InvokeAsync(new ModelingElementChange
        {
            ElementId = Element.Id,
            Description = _descriptionDraft
        });
    }

    private Task HandleScalarInputAsync(string key, string value)
    {
        _scalarDrafts[key] = value;
        return Task.CompletedTask;
    }

    private Task HandleScalarKeyDownAsync(string key, KeyboardEventArgs args)
    {
        if (string.Equals(args.Key, "Escape", StringComparison.Ordinal))
        {
            RevertScalarDraft(key);
            return Task.CompletedTask;
        }

        if (string.Equals(args.Key, "Enter", StringComparison.Ordinal))
        {
            return CommitScalarAsync(key);
        }

        return Task.CompletedTask;
    }

    private void RevertScalarDraft(string key)
    {
        var source = Element?.Properties ?? (IReadOnlyDictionary<string, JsonElement>?)Relationship?.Properties;
        if (source is not null && source.TryGetValue(key, out var value))
        {
            _scalarDrafts[key] = FormatPropertyValue(value);
            // The TmNumberInput is a controlled component whose bound Value may equal the
            // committed one — bump the render key so the input REMOUNTS and the DOM shows the
            // committed value again (review fix, phase 14).
            _scalarRenderKeys[key] = _scalarRenderKeys.GetValueOrDefault(key) + 1;
        }
    }

    /// <summary>Per-key render keys of the scalar inputs — bumped on an Escape revert so the
    /// remount resets the displayed value even when the bound value did not change.</summary>
    private readonly Dictionary<string, int> _scalarRenderKeys = new(StringComparer.Ordinal);

    private string ScalarRenderKey(string key) => $"{key}-{_scalarRenderKeys.GetValueOrDefault(key)}";

    private async Task CommitScalarAsync(string key)
    {
        var source = Element?.Properties ?? (IReadOnlyDictionary<string, JsonElement>?)Relationship?.Properties;
        if (source is null || !source.TryGetValue(key, out var original))
        {
            return;
        }

        var draft = _scalarDrafts.TryGetValue(key, out var stored) ? stored : string.Empty;
        if (string.Equals(draft, FormatPropertyValue(original), StringComparison.Ordinal))
        {
            return;
        }

        if (original.ValueKind == JsonValueKind.String)
        {
            await CommitScalarChangeAsync(key, draft.Length == 0 ? null : JsonSerializer.SerializeToElement(draft));
            return;
        }

        // Numbers: the JSON kind is preserved — an integer stays an integer, any other number
        // parses invariantly. An empty draft removes the property.
        if (draft.Length == 0)
        {
            await CommitScalarChangeAsync(key, null);
            return;
        }

        if (IsIntegerJsonNumber(original))
        {
            if (int.TryParse(draft, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                await CommitScalarChangeAsync(key, JsonSerializer.SerializeToElement(intValue));
            }

            return;
        }

        if (double.TryParse(draft, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            await CommitScalarChangeAsync(key, JsonSerializer.SerializeToElement(doubleValue));
        }
    }

    private Task CommitScalarChangeAsync(string key, JsonElement? value)
    {
        if (Element is not null)
        {
            var change = new ModelingElementChange { ElementId = Element.Id };
            if (value is { } elementValue)
            {
                change.SetProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [key] = elementValue };
            }
            else
            {
                change.RemovedProperties = [key];
            }

            return OnElementEdited.InvokeAsync(change);
        }

        if (Relationship is not null)
        {
            var change = new ModelingRelationshipChange { RelationshipId = Relationship.Id };
            if (value is { } relationshipValue)
            {
                change.SetProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [key] = relationshipValue };
            }
            else
            {
                change.RemovedProperties = [key];
            }

            return OnRelationshipEdited.InvokeAsync(change);
        }

        return Task.CompletedTask;
    }

    private Task HandleToggleChangeAsync(string key, bool value)
    {
        _scalarDrafts[key] = value ? "true" : "false";
        return CommitScalarChangeAsync(key, JsonSerializer.SerializeToElement(value));
    }

    /// <summary>The inspector-scope keyboard handler: Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y drive the
    /// editor's own stack (raised as callbacks) — the diagram canvas keyboard scope is separate.
    /// While ANY draft differs from its committed value the handler returns early and the
    /// browser's NATIVE text undo owns the keystroke (review fix, phase 14): the editor stack
    /// must not swallow Ctrl+Z inside a field with uncommitted typing.</summary>
    private Task HandleInspectorKeyDownAsync(KeyboardEventArgs args)
    {
        if ((!args.CtrlKey && !args.MetaKey) || HasActiveDraft)
        {
            return Task.CompletedTask;
        }

        if (string.Equals(args.Key, "z", StringComparison.OrdinalIgnoreCase) && !args.ShiftKey)
        {
            return OnUndoRequested.InvokeAsync();
        }

        if ((string.Equals(args.Key, "z", StringComparison.OrdinalIgnoreCase) && args.ShiftKey)
            || string.Equals(args.Key, "y", StringComparison.OrdinalIgnoreCase))
        {
            return OnRedoRequested.InvokeAsync();
        }

        return Task.CompletedTask;
    }

    /// <summary>Whether any editing draft (name, description, scalar) differs from its committed
    /// value — the native-text-undo window.</summary>
    private bool HasActiveDraft
    {
        get
        {
            if (Element is not null)
            {
                return !string.Equals(_nameDraft, Element.Name, StringComparison.Ordinal)
                    || !string.Equals(_descriptionDraft, Element.Description, StringComparison.Ordinal)
                    || !ScalarDraftsMatch(Element.Properties);
            }

            if (Relationship is not null)
            {
                return !string.Equals(_nameDraft, Relationship.Name, StringComparison.Ordinal)
                    || !ScalarDraftsMatch(Relationship.Properties);
            }

            return false;
        }
    }

    /// <summary>Attributes for the description textarea (the core TmTextArea has no blur/key
    /// passthrough parameters — the splat carries the commit handler and the Escape revert).</summary>
    private Dictionary<string, object> DescriptionAttributes => new()
    {
        ["onblur"] = EventCallback.Factory.Create<FocusEventArgs>(this, CommitDescriptionAsync),
        ["onkeydown"] = EventCallback.Factory.Create<KeyboardEventArgs>(this, HandleDescriptionKeyDownAsync)
    };

    /// <summary>The scalar input's current draft value.</summary>
    private string ScalarDraft(string key)
        => _scalarDrafts.TryGetValue(key, out var draft) ? draft : string.Empty;

    /// <summary>Whether a scalar property edits through the integer TmNumberInput.</summary>
    private bool IsIntegerProperty(JsonElement value) => IsIntegerJsonNumber(value);

    // ── Read-only presentation (unchanged) ───────────────────────────────────

    /// <summary>Selected modeling element. Takes precedence when both element and relationship are supplied.</summary>
    [Parameter] public ModelingElementDto? Element { get; set; }

    /// <summary>Selected modeling relationship.</summary>
    [Parameter] public ModelingRelationshipDto? Relationship { get; set; }

    /// <summary>Elements used to resolve relationship source and target display names.</summary>
    [Parameter] public IReadOnlyList<ModelingElementDto> Elements { get; set; } = [];

    /// <summary>Additional CSS class applied to the inspector root.</summary>
    [Parameter] public string? Class { get; set; }

    private string RootClass => string.Join(" ", new[]
    {
        "tm-modeling-inspector",
        IsEditing ? "tm-modeling-inspector--editing" : null,
        Class
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private string Kind => Element is not null ? "element" : Relationship is not null ? "relationship" : "empty";

    private string KindLabel => Element is not null
        ? Loc["TmModelingInspector_Element"]
        : Relationship is not null
            ? Loc["TmModelingInspector_Relationship"]
            : Loc["TmModelingInspector_Title"];

    /// <summary>Header icon: the element's semantic-type icon, a link for relationships, the
    /// fallback shape otherwise (shared with the model tree so the same type always maps to
    /// the same icon).</summary>
    private string HeaderIcon => Element is not null
        ? ModelingSemanticTypeIcons.ForSemanticType(Element.SemanticType)
        : Relationship is not null
            ? IconNames.Link2
            : ModelingSemanticTypeIcons.Fallback;

    private string DisplayName => Element is not null
        ? Fallback(Element.Name, Loc["TmModelingModelTree_Unnamed"])
        : Relationship is not null
            ? Fallback(Relationship.Name, Relationship.Id)
            : string.Empty;

    private string DisplayType => Element is not null
        ? Fallback(Element.SemanticType, Loc["TmModelingModelTree_UnknownType"])
        : Relationship is not null
            ? Fallback(Relationship.RelationshipType, Loc["TmModelingModelTree_UnknownType"])
            : string.Empty;

    private string DisplayNotation => Element is not null
        ? Fallback(Element.Notation, Loc["TmModelingEditor_Unknown"])
        : Relationship is not null
            ? Fallback(SourceElement?.Notation, TargetElement?.Notation, Loc["TmModelingEditor_Unknown"])
            : string.Empty;

    private string DisplaySourceId => Element is not null
        ? Element.SourceId
        : Relationship?.SourceId ?? string.Empty;

    private string DisplaySourceType => Element is not null
        ? Element.SourceType
        : Relationship?.SourceType ?? string.Empty;

    private string DisplaySourcePath => Element?.SourcePath ?? string.Empty;

    private ModelingElementDto? SourceElement => Relationship is null
        ? null
        : Elements.FirstOrDefault(element => string.Equals(element.Id, Relationship.SourceElementId, StringComparison.Ordinal));

    private ModelingElementDto? TargetElement => Relationship is null
        ? null
        : Elements.FirstOrDefault(element => string.Equals(element.Id, Relationship.TargetElementId, StringComparison.Ordinal));

    private string DisplaySourceName => Fallback(SourceElement?.Name, Relationship?.SourceElementId, Loc["TmModelingEditor_Unknown"]);

    private string DisplayTargetName => Fallback(TargetElement?.Name, Relationship?.TargetElementId, Loc["TmModelingEditor_Unknown"]);

    private static string Fallback(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string FormatPropertyValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };

    private static string GetTrustClass(string trustLevel)
        => string.Equals(trustLevel, "low", StringComparison.OrdinalIgnoreCase)
            ? "tm-modeling-inspector__value tm-modeling-inspector__value--trust-low"
            : "tm-modeling-inspector__value";
}
