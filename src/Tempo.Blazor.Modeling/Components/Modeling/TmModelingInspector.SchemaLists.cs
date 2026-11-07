using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Phase 5 (decision <c>MODELING-PROPERTY-SCHEMA</c>) — the LIST, PRESENTATION, ATTRIBUTE and LOOKUP
/// half of the schema-driven inspector. Split from <c>TmModelingInspector.Schema.cs</c> (state,
/// seeding, field errors and the commit paths) to keep both files readable; it is the same partial
/// class.
/// </summary>
public partial class TmModelingInspector
{
    // ── String lists and structured lists ────────────────────────────────────

    private IReadOnlyList<string> SchemaStringList(ModelingPropertyFieldDto field)
        => SchemaArray(field)
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();

    private static string StringListItemDraftKey(ModelingPropertyFieldDto field, int index)
        => $"{field.Key}[{index}]";

    private static string StructuredItemDraftKey(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField)
        => $"{field.Key}[{index}].{itemField.Key}";

    private string StringListItemText(ModelingPropertyFieldDto field, int index)
    {
        var key = StringListItemDraftKey(field, index);
        if (_schemaTextDrafts.TryGetValue(key, out var typed))
        {
            return typed;
        }

        var items = SchemaStringList(field);
        return index >= 0 && index < items.Count ? items[index] : string.Empty;
    }

    private void HandleStringListItemInput(ModelingPropertyFieldDto field, int index, string value)
        => _schemaTextDrafts[StringListItemDraftKey(field, index)] = value;

    private Task CommitStringListItemAsync(ModelingPropertyFieldDto field, int index)
    {
        var key = StringListItemDraftKey(field, index);
        var text = _schemaTextDrafts.TryGetValue(key, out var typed) ? typed : StringListItemText(field, index);
        _schemaTextDrafts.Remove(key);
        _committedPaths.Add(key);
        return SetStringListItemAsync(field, index, text);
    }

    private Task AddStringListItemAsync(ModelingPropertyFieldDto field)
    {
        var items = SchemaArray(field).ToList();
        items.Add(JsonSerializer.SerializeToElement(string.Empty));
        return AddListItemAsync(field, items);
    }

    private Task SetStringListItemAsync(ModelingPropertyFieldDto field, int index, string value)
    {
        var items = SchemaArray(field).ToList();
        if (index < 0 || index >= items.Count)
        {
            return Task.CompletedTask;
        }

        items[index] = JsonSerializer.SerializeToElement(value);
        return CommitSchemaArrayAsync(field, items);
    }

    private Task RemoveStringListItemAsync(ModelingPropertyFieldDto field, int index)
    {
        var items = SchemaArray(field).ToList();
        if (index < 0 || index >= items.Count)
        {
            return Task.CompletedTask;
        }

        items.RemoveAt(index);
        return RemoveListItemAsync(field, items, index);
    }

    /// <summary>Appends one structured-list item carrying the item fields' defaults (empty strings for
    /// the text fields) and commits the WHOLE list — the value stays validated by the same rules.</summary>
    private Task AddStructuredItemAsync(ModelingPropertyFieldDto field)
    {
        var item = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var itemField in OrderedFields(field.ItemFields))
        {
            if (itemField.DefaultValue is { } defaultValue && defaultValue.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                item[itemField.Key] = defaultValue;
                continue;
            }

            switch (itemField.Type)
            {
                case ModelingPropertyFieldType.String:
                case ModelingPropertyFieldType.Markdown:
                case ModelingPropertyFieldType.MultilineText:
                case ModelingPropertyFieldType.Url:
                    item[itemField.Key] = JsonSerializer.SerializeToElement(string.Empty);
                    break;

                case ModelingPropertyFieldType.Boolean:
                    item[itemField.Key] = JsonSerializer.SerializeToElement(false);
                    break;
            }
        }

        var items = SchemaArray(field).ToList();
        items.Add(JsonSerializer.SerializeToElement(item));
        return AddListItemAsync(field, items);
    }

    private Task RemoveStructuredItemAsync(ModelingPropertyFieldDto field, int index)
    {
        var items = SchemaArray(field).ToList();
        if (index < 0 || index >= items.Count)
        {
            return Task.CompletedTask;
        }

        items.RemoveAt(index);
        return RemoveListItemAsync(field, items, index);
    }

    /// <summary>Replaces one item field of a structured-list item and commits the whole list.</summary>
    private Task SetStructuredItemFieldAsync(
        ModelingPropertyFieldDto field,
        int index,
        ModelingPropertyFieldDto itemField,
        JsonElement? value)
    {
        var items = SchemaArray(field).ToList();
        if (index < 0 || index >= items.Count)
        {
            return Task.CompletedTask;
        }

        var item = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (items[index].ValueKind == JsonValueKind.Object)
        {
            foreach (var property in items[index].EnumerateObject())
            {
                item[property.Name] = property.Value.Clone();
            }
        }

        if (value is { } element && element.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            item[itemField.Key] = element;
        }
        else
        {
            item.Remove(itemField.Key);
        }

        items[index] = JsonSerializer.SerializeToElement(item);
        return CommitSchemaArrayAsync(field, items);
    }

    /// <summary>The raw text one structured-list item field shows (its own text draft first).</summary>
    private string StructuredItemText(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField)
    {
        var key = StructuredItemDraftKey(field, index, itemField);
        if (_schemaTextDrafts.TryGetValue(key, out var typed))
        {
            return typed;
        }

        var value = StructuredItemValue(field, index, itemField);
        return value is not { } element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? string.Empty
            : element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString();
    }

    private void HandleStructuredItemInput(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField, string value)
        => _schemaTextDrafts[StructuredItemDraftKey(field, index, itemField)] = value;

    /// <summary>Parses one structured-list item field. A number that does not parse keeps the TYPED
    /// TEXT (nothing is committed) and shows the item-level type error — review A14: a structured
    /// item never silently discards an unparsable value.</summary>
    private Task CommitStructuredItemFieldAsync(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField)
    {
        var key = StructuredItemDraftKey(field, index, itemField);
        var text = _schemaTextDrafts.TryGetValue(key, out var typed) ? typed : StructuredItemText(field, index, itemField);
        _schemaTextDrafts.Remove(key);
        _committedPaths.Add(key);

        JsonElement? value;
        switch (itemField.Type)
        {
            case ModelingPropertyFieldType.Integer:
            case ModelingPropertyFieldType.Number:
                if (string.IsNullOrWhiteSpace(text))
                {
                    value = null;
                    break;
                }

                if (!TryParseNumber(text, out var number)
                    || (itemField.Type == ModelingPropertyFieldType.Integer && number != Math.Floor(number)))
                {
                    _schemaTextDrafts[key] = text;
                    SetSchemaFieldError(
                        key,
                        itemField.Type == ModelingPropertyFieldType.Integer
                            ? IntegerErrorMessage(itemField)
                            : TypeErrorMessage(itemField));
                    StateHasChanged();
                    return Task.CompletedTask;
                }

                value = itemField.Type == ModelingPropertyFieldType.Integer
                    ? JsonSerializer.SerializeToElement((long)number)
                    : JsonSerializer.SerializeToElement(number);
                break;

            case ModelingPropertyFieldType.Duration:
                if (string.IsNullOrWhiteSpace(text))
                {
                    value = null;
                    break;
                }

                if (!TryParseNumber(text, out var minutes))
                {
                    _schemaTextDrafts[key] = text;
                    SetSchemaFieldError(key, TypeErrorMessage(itemField));
                    StateHasChanged();
                    return Task.CompletedTask;
                }

                value = JsonSerializer.SerializeToElement((long)Math.Round(minutes, MidpointRounding.AwayFromZero));
                break;

            default:
                value = text.Length == 0 ? null : JsonSerializer.SerializeToElement(text);
                break;
        }

        return SetStructuredItemFieldAsync(field, index, itemField, value);
    }

    private Task HandleStructuredItemBooleanAsync(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField, bool value)
        => SetStructuredItemFieldAsync(field, index, itemField, JsonSerializer.SerializeToElement(value));

    private Task HandleStructuredItemEnumAsync(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField, string? value)
        => SetStructuredItemFieldAsync(
            field,
            index,
            itemField,
            string.IsNullOrEmpty(value) ? null : JsonSerializer.SerializeToElement(value));

    private bool StructuredItemBoolean(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField)
        => StructuredItemValue(field, index, itemField) is { } value && value.ValueKind == JsonValueKind.True;

    private string StructuredItemString(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField)
        => StructuredItemValue(field, index, itemField) is { } value && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private JsonElement? StructuredItemValue(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField)
    {
        var items = SchemaArray(field);
        return index >= 0 && index < items.Count
               && items[index].ValueKind == JsonValueKind.Object
               && items[index].TryGetProperty(itemField.Key, out var value)
            ? value
            : null;
    }

    // ── List item actions (review U8: focus + polite announcement) ───────────

    /// <summary>Commits an ADDED item and then moves focus into the new row's first input, announcing
    /// the change politely (a keyboard user must land where the new item is).</summary>
    private async Task AddListItemAsync(ModelingPropertyFieldDto field, List<JsonElement> items)
    {
        await CommitSchemaArrayAsync(field, items);

        var firstItemField = OrderedFields(field.ItemFields).FirstOrDefault();
        _pendingItemAddFocus = firstItemField is null
            ? $"{FieldDomId(field)}-{items.Count - 1}"
            : ItemDomId(field, items.Count - 1, firstItemField);
        _itemAnnouncement = string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingInspector_ItemAdded"], FieldLabel(field));
        StateHasChanged();
    }

    /// <summary>Commits a REMOVED item and then moves focus to the NEXT row's remove button (else the
    /// add button), announcing the change politely.</summary>
    private async Task RemoveListItemAsync(ModelingPropertyFieldDto field, List<JsonElement> items, int removedIndex)
    {
        await CommitSchemaArrayAsync(field, items);

        var next = removedIndex < items.Count ? removedIndex : -1;
        _pendingItemRemoveFocus = next >= 0
            ? $"modeling-inspector-field-{SanitizeKey(field.Key)}-remove-{next}"
            : $"modeling-inspector-field-{SanitizeKey(field.Key)}-add";
        _itemAnnouncement = string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingInspector_ItemRemoved"], FieldLabel(field));
        StateHasChanged();
    }

    // ── Read-only presentation ───────────────────────────────────────────────

    /// <summary>The formatted value a read-only field renders; an empty/absent value shows the
    /// localized "Not set" instead of a blank cell (review U9).</summary>
    private string SchemaDisplayValue(ModelingPropertyFieldDto field)
    {
        var formatted = SchemaDraft(field) is { } value ? FormatSchemaValue(field, value) : string.Empty;
        return formatted.Length == 0 ? Loc["TmModelingInspector_NoValue"] : formatted;
    }

    private string FormatSchemaValue(ModelingPropertyFieldDto field, JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return string.Empty;
        }

        switch (field.Type)
        {
            case ModelingPropertyFieldType.Boolean:
                return Loc[element.ValueKind == JsonValueKind.True ? "TmModelingInspector_Yes" : "TmModelingInspector_No"];

            case ModelingPropertyFieldType.Enum:
                var stored = element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty;
                var option = field.Options.FirstOrDefault(candidate => string.Equals(candidate.Value, stored, StringComparison.Ordinal));
                return option is null || string.IsNullOrWhiteSpace(option.Label) ? stored : option.Label;

            case ModelingPropertyFieldType.Duration:
                return FormatDurationReadable(element);

            case ModelingPropertyFieldType.Date:
                return element.ValueKind == JsonValueKind.String
                       && DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date.ToString("d", CultureInfo.CurrentUICulture)
                    : element.ToString();

            case ModelingPropertyFieldType.DateTime:
                return element.ValueKind == JsonValueKind.String
                       && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment)
                    ? moment.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture)
                    : element.ToString();

            case ModelingPropertyFieldType.EntityReference:
                return ModelingEntityReference.TryParse(element) is { } reference ? ReferenceLabel(reference) : string.Empty;

            case ModelingPropertyFieldType.EntityReferenceList:
                return string.Join(", ", SchemaReferences(field).Select(ReferenceLabel));

            case ModelingPropertyFieldType.StringList:
                return string.Join(", ", SchemaStringList(field));

            case ModelingPropertyFieldType.StructuredList:
                return string.Join(
                    " | ",
                    SchemaArray(field).Select(item => FormatStructuredItem(field, item)));

            default:
                return element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString();
        }
    }

    /// <summary>One structured-list item ("Code: E-1 · Severity: Warning").</summary>
    private string FormatStructuredItem(ModelingPropertyFieldDto field, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return item.ToString() ?? string.Empty;
        }

        var parts = new List<string>();
        foreach (var itemField in OrderedFields(field.ItemFields))
        {
            if (!item.TryGetProperty(itemField.Key, out var itemValue) || itemValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            var formatted = FormatSchemaValue(itemField, itemValue);
            if (formatted.Length == 0)
            {
                continue;
            }

            parts.Add($"{FieldLabel(itemField)}: {formatted}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Whether the "Other properties" table still has rows (keys the schema does not cover).</summary>
    private IReadOnlyList<KeyValuePair<string, JsonElement>> OtherProperties(IReadOnlyDictionary<string, JsonElement> properties)
    {
        var keys = SchemaKeys;
        return properties
            .Where(property => !keys.Contains(property.Key))
            .OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Whether a schema field renders its editor (editing on + the schema allows it).</summary>
    private bool ShowSchemaEditor(ModelingPropertyFieldDto field) => CanEditSchemaField(field);

    // ── Attribute helpers (labels, max-length, described-by) ─────────────────

    /// <summary>The visible label of a field. A display-only unit is appended ("Cost (CZK)") — EXCEPT
    /// for a duration, whose unit lives in the unit select next to the number (review U5), and the
    /// label must not repeat it.</summary>
    private static string FieldDisplayLabel(ModelingPropertyFieldDto field)
        => string.IsNullOrWhiteSpace(field.Unit) || field.Type == ModelingPropertyFieldType.Duration
            ? FieldLabel(field)
            : $"{FieldLabel(field)} ({field.Unit})";

    /// <summary>The ids a field's control describes: its error, else its help, plus the "reference may
    /// be stale" hint (review A1).</summary>
    private string? DescribedBy(ModelingPropertyFieldDto field, string? error)
    {
        var ids = new List<string>();
        if (error is not null)
        {
            ids.Add(FieldErrorId(field));
        }
        else if (!string.IsNullOrWhiteSpace(field.Help))
        {
            ids.Add(FieldHelpId(field));
        }

        if (HasStaleReference(field))
        {
            ids.Add(FieldStaleId(field));
        }

        return ids.Count == 0 ? null : string.Join(' ', ids);
    }

    /// <summary>The aria attributes a control that has no <c>Error</c> parameter receives through its
    /// additional attributes. Review U1: the accessible NAME comes from the control's own
    /// <c>Label</c> parameter (the pickers name their trigger from it), so no <c>aria-label</c> is
    /// duplicated here — only the state and the description ids.</summary>
    private Dictionary<string, object> FieldAriaAttributes(ModelingPropertyFieldDto field, string? error)
    {
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal);

        if (error is not null)
        {
            attributes["aria-invalid"] = "true";
        }

        if (DescribedBy(field, error) is { } describedBy)
        {
            attributes["aria-describedby"] = describedBy;
        }

        return attributes;
    }

    /// <summary>Extra attributes of a text input/area: the schema's length cap plus the described-by
    /// hint when the core component's own <c>Error</c> is not carrying it.</summary>
    private Dictionary<string, object> TextFieldAttributes(ModelingPropertyFieldDto field)
    {
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal);
        if (field.MaxLength is { } maxLength && maxLength > 0)
        {
            attributes["maxlength"] = maxLength.ToString(CultureInfo.InvariantCulture);
        }

        if (field.Required)
        {
            attributes["aria-required"] = "true";
        }

        return attributes;
    }

    /// <summary>Attributes of one structured-list item field's text input: its length cap, its
    /// item-level error id (review U6) and the invalid state.</summary>
    private Dictionary<string, object> ItemFieldAttributes(
        ModelingPropertyFieldDto field,
        int index,
        ModelingPropertyFieldDto itemField,
        string? error)
    {
        var attributes = TextFieldAttributes(itemField);
        if (error is not null)
        {
            attributes["aria-invalid"] = "true";
            attributes["aria-describedby"] = ItemErrorId(field, index, itemField);
        }

        return attributes;
    }

    /// <summary>The markdown editor has no blur/key callback of its own: the commit rides the root
    /// element's bubbling <c>focusout</c> (leaving the editor commits, exactly like the text fields).</summary>
    private Dictionary<string, object> MarkdownAttributes(ModelingPropertyFieldDto field)
        => new(StringComparer.Ordinal)
        {
            ["onfocusout"] = EventCallback.Factory.Create<FocusEventArgs>(this, () => CommitSchemaTextFieldAsync(field)),
            ["data-field-key"] = field.Key
        };

    /// <summary>The ids a reference field's combobox selects (the stored references, in order).</summary>
    private IReadOnlyList<string> CurrentReferenceIds(ModelingPropertyFieldDto field)
        => SelectedReferenceItems(field).Select(item => item.RefId).ToArray();

    /// <summary>The full list value labels for the assistant-facing render (used by the copy helpers).</summary>
    private string AddItemAriaLabel(ModelingPropertyFieldDto field)
        => string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingInspector_AddItemTo"], FieldLabel(field));

    private string RemoveItemAriaLabel(ModelingPropertyFieldDto field, int index)
        => string.Format(
            CultureInfo.CurrentUICulture,
            Loc["TmModelingInspector_RemoveItem"],
            index + 1,
            FieldLabel(field));

    // ── Lookup plumbing (review A7) ──────────────────────────────────────────

    private string EffectiveSchemaCulture => string.IsNullOrWhiteSpace(Culture)
        ? CultureInfo.CurrentUICulture.Name
        : Culture;

    /// <summary>Per-field async state of a reference lookup: the page it currently offers, whether the
    /// host has more, its own cancellation and a monotonically increasing request generation — a
    /// response whose generation is stale is DROPPED, so fast typing can never show an older answer
    /// (review A7 "out-of-order").</summary>
    private sealed class ReferenceLookupState
    {
        public List<ModelingLookupItemDto> Items { get; } = [];

        public string SearchText { get; set; } = string.Empty;

        public string? ContinuationToken { get; set; }

        /// <summary>How many raw items the host already returned for this search — the SKIP of the
        /// next page when the host pages by offset instead of by a cursor (review A7).</summary>
        public int ReceivedCount { get; set; }

        public bool HasMore { get; set; }

        public bool Failed { get; set; }

        public bool BelowMinimum { get; set; }

        public int Generation { get; set; }

        public CancellationTokenSource? Cancellation { get; set; }
    }

    private readonly Dictionary<string, ReferenceLookupState> _referenceLookup = new(StringComparer.Ordinal);

    /// <summary>Debounce before a lookup request leaves the inspector (review A7: one request after a
    /// fast burst of typing, not one per keystroke).</summary>
    internal const int LookupDebounceMilliseconds = 300;

    private ReferenceLookupState LookupStateFor(ModelingPropertyFieldDto field)
    {
        if (!_referenceLookup.TryGetValue(field.Key, out var state))
        {
            state = new ReferenceLookupState();
            _referenceLookup[field.Key] = state;
        }

        return state;
    }

    /// <summary>The options a reference field's combobox offers: the STORED references (always — a
    /// value must render even offline) followed by the host lookup's current page.</summary>
    private IReadOnlyList<ModelingLookupItemDto> ReferenceItems(ModelingPropertyFieldDto field)
    {
        var state = LookupStateFor(field);
        var merged = SelectedReferenceItems(field);
        foreach (var item in state.Items)
        {
            if (!merged.Any(existing => string.Equals(existing.RefId, item.RefId, StringComparison.Ordinal)))
            {
                merged.Add(item);
            }
        }

        return merged;
    }

    /// <summary>Whether the lookup offers a next page ("Load more").</summary>
    private bool HasMoreReferences(ModelingPropertyFieldDto field)
        => LookupStateFor(field).HasMore;

    /// <summary>The message the combobox shows instead of options: below the descriptor's minimum query
    /// length "type at least N characters", a failed host call the localized failure text.</summary>
    private string? ReferenceLookupMessage(ModelingPropertyFieldDto field)
    {
        var state = LookupStateFor(field);
        if (state.Failed)
        {
            return Loc["TmModelingInspector_LookupFailed"];
        }

        return state.BelowMinimum
            ? string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingInspector_LookupMinChars"], LookupMinLength(field))
            : null;
    }

    /// <summary>Called when a reference combobox opens: with <c>SearchRequired = false</c> the first
    /// page is offered immediately (review A7), else nothing is requested until the user types.</summary>
    private Task HandleReferenceOpenAsync(ModelingPropertyFieldDto field)
    {
        var state = LookupStateFor(field);
        if (state.Items.Count > 0 || state.SearchText.Length > 0)
        {
            return Task.CompletedTask;
        }

        if (field.Lookup is { SearchRequired: false })
        {
            return RunLookupAsync(field, string.Empty, append: false);
        }

        return Task.CompletedTask;
    }

    /// <summary>The combobox filtering: debounced per field, generation-guarded, and never below the
    /// descriptor's minimum query length.</summary>
    private Task HandleReferenceFilterAsync(ModelingPropertyFieldDto field, string searchText)
    {
        var text = searchText?.Trim() ?? string.Empty;
        var state = LookupStateFor(field);
        state.SearchText = text;

        // A new keystroke invalidates every in-flight answer (review A7).
        unchecked { state.Generation++; }
        state.Cancellation?.Cancel();
        state.Cancellation?.Dispose();
        state.Cancellation = null;

        if (text.Length < LookupMinLength(field))
        {
            state.Items.Clear();
            state.HasMore = false;
            state.ContinuationToken = null;
            state.BelowMinimum = true;
            state.Failed = false;
            StateHasChanged();
            return Task.CompletedTask;
        }

        state.BelowMinimum = false;
        var generation = state.Generation;
        var cancellation = new CancellationTokenSource();
        state.Cancellation = cancellation;
        _ = DebouncedLookupAsync(field, text, generation, cancellation.Token);
        return Task.CompletedTask;
    }

    private async Task DebouncedLookupAsync(ModelingPropertyFieldDto field, string text, int generation, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(LookupDebounceMilliseconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (generation != LookupStateFor(field).Generation || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await RunLookupAsync(field, text, append: false, generation: generation, cancellationToken: cancellationToken);
    }

    /// <summary>The "Load more" action: appends the next page through the host's own paging cursor
    /// (<see cref="ModelingLookupRequest.ContinuationToken"/>, else <c>Skip</c>) — review A7.</summary>
    private Task LoadMoreReferencesAsync(ModelingPropertyFieldDto field)
    {
        var state = LookupStateFor(field);
        unchecked { state.Generation++; }
        state.Cancellation?.Cancel();
        state.Cancellation = new CancellationTokenSource();
        return RunLookupAsync(field, state.SearchText, append: true, generation: state.Generation, cancellationToken: state.Cancellation.Token);
    }

    private async Task RunLookupAsync(
        ModelingPropertyFieldDto field,
        string searchText,
        bool append,
        int? generation = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = field.Lookup;
        if (LookupProvider is null || descriptor is null)
        {
            return;
        }

        var state = LookupStateFor(field);
        var expected = generation ?? state.Generation;

        ModelingLookupResultDto? result;
        try
        {
            // ONE request page goes through the private adapter — the same mapping a host could hand
            // to a core combobox through IDropdownDataProvider.DataProvider.
            var adapter = new ModelingLookupDropdownProvider(
                LookupProvider,
                descriptor,
                ModelId ?? string.Empty,
                EffectiveSchemaCulture,
                append ? state.ReceivedCount : 0,
                append ? state.ContinuationToken : null);
            await adapter.GetItemsAsync(
                new DropdownSearchRequest
                {
                    SearchText = searchText,
                    Page = 1,
                    PageSize = descriptor.PageSize <= 0 ? 20 : descriptor.PageSize
                },
                cancellationToken);

            // The adapter keeps the host's OWN answer (review A7): its continuation cursor and its
            // HasMore flag cannot be reconstructed from an IDropdownDataResult, and losing the cursor
            // would make "Load more" re-ask the first page.
            result = adapter.LastResult;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            result = null;
        }

        // Drop a stale answer: another keystroke/field state has moved on (review A7).
        if (expected != LookupStateFor(field).Generation)
        {
            return;
        }

        if (result is null)
        {
            state.Failed = true;
            state.Items.Clear();
            state.HasMore = false;
            await InvokeAsync(StateHasChanged);
            return;
        }

        state.Failed = false;
        if (!append)
        {
            state.Items.Clear();
            state.ReceivedCount = 0;
            state.ContinuationToken = null;
        }

        state.ReceivedCount += result.Items.Count;
        foreach (var item in result.Items.Where(item => !item.Disabled))
        {
            if (!state.Items.Any(existing => string.Equals(existing.RefId, item.RefId, StringComparison.Ordinal)))
            {
                state.Items.Add(item);
            }
        }

        state.HasMore = result.HasMore;
        // The host's cursor is authoritative; a host that pages by OFFSET leaves it null and the next
        // append sends Skip = the number of items already returned (review A7).
        state.ContinuationToken = result.ContinuationToken;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>A single reference field stores ONE object; a list field stores an array of them.
    /// Every change commits through the regular field commit (one undo step). A re-selected id keeps
    /// its ORIGINAL snapshot (§5.4) — only newly picked items get a fresh <c>snapshotAt</c>
    /// (review A15).</summary>
    private Task HandleSchemaReferencesChangedAsync(ModelingPropertyFieldDto field, IReadOnlyList<string> values)
    {
        var stored = field.Type == ModelingPropertyFieldType.EntityReference
            ? (SchemaReference(field) is { } single ? [single] : Array.Empty<ModelingEntityReference>())
            : SchemaReferences(field);
        var known = ReferenceItems(field);

        var selected = new List<JsonElement>();
        foreach (var id in values)
        {
            if (stored.FirstOrDefault(reference => reference.HasId(id)) is { } existing)
            {
                selected.Add(existing.ToJsonElement());
                continue;
            }

            var item = known.FirstOrDefault(candidate => string.Equals(candidate.RefId, id, StringComparison.Ordinal));
            if (item is not null)
            {
                selected.Add(BuildReference(item));
            }
        }

        MarkCommitted(field);
        if (field.Type == ModelingPropertyFieldType.EntityReference)
        {
            return CommitSchemaValueAsync(field, selected.Count == 0 ? null : selected[^1]);
        }

        return CommitSchemaValueAsync(field, JsonSerializer.SerializeToElement(selected));
    }

    /// <summary>Test seam (InternalsVisibleTo): loads the first lookup page exactly like opening the
    /// combobox does, so a test can then pick an option that is not stored yet.</summary>
    internal Task TestLoadReferenceOptionsAsync(string fieldKey)
    {
        var field = PropertySchema?.Sections
            .SelectMany(section => section.Fields)
            .FirstOrDefault(candidate => string.Equals(candidate.Key, fieldKey, StringComparison.Ordinal));
        return field is null ? Task.CompletedTask : RunLookupAsync(field, string.Empty, append: false);
    }

    /// <summary>Test seam (InternalsVisibleTo): drives the reference commit exactly like the core
    /// combobox does, so the snapshot-preservation rule (review A15) is testable without opening a
    /// JS-backed popup.</summary>
    internal Task TestSelectReferencesAsync(string fieldKey, IReadOnlyList<string> ids)
    {
        var field = PropertySchema?.Sections
            .SelectMany(section => section.Fields)
            .FirstOrDefault(candidate => string.Equals(candidate.Key, fieldKey, StringComparison.Ordinal));
        return field is null ? Task.CompletedTask : HandleSchemaReferencesChangedAsync(field, ids);
    }

    /// <summary>The minimum search length of a reference field's lookup.</summary>
    private static int LookupMinLength(ModelingPropertyFieldDto field)
        => field.Lookup is { MinQueryLength: > 0 } descriptor ? descriptor.MinQueryLength : 2;

    /// <summary>The reference picker's placeholder (localized; the field's own placeholder wins).</summary>
    private string LookupPlaceholder(ModelingPropertyFieldDto field)
        => string.IsNullOrWhiteSpace(field.Placeholder)
            ? Loc["TmModelingInspector_ReferenceSearch"]
            : field.Placeholder;

    /// <summary>
    /// The private adapter review A7 asks for: maps a
    /// <see cref="IDropdownDataProvider{TItem}"/> request (search text, 1-based page, page size) onto
    /// one <see cref="IModelingLookupProvider.LookupAsync"/> call. The inspector drives it directly
    /// because continuation-token paging and "Load more" APPENDING cannot be expressed through
    /// <c>IDropdownDataProvider</c> + a core combobox; the mapping itself lives in exactly one place,
    /// so a host that only needs search can still hand an instance to a core control as
    /// <c>DataProvider</c>.
    /// </summary>
    private sealed class ModelingLookupDropdownProvider(
        IModelingLookupProvider provider,
        ModelingLookupDescriptor descriptor,
        string modelId,
        string culture,
        int skip = 0,
        string? continuationToken = null) : IDropdownDataProvider<ModelingLookupItemDto>
    {
        /// <summary>The host's OWN answer to the last call (its <c>HasMore</c> and its continuation
        /// cursor), which an <see cref="DropdownDataResult{TItem}"/> cannot carry.</summary>
        public ModelingLookupResultDto? LastResult { get; private set; }

        public async Task<DropdownDataResult<ModelingLookupItemDto>> GetItemsAsync(DropdownSearchRequest request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var pageSize = request.PageSize <= 0 ? (descriptor.PageSize <= 0 ? 20 : descriptor.PageSize) : request.PageSize;
            // A cursor page starts at the host's own offset; an offset page adds the pages already
            // taken plus this request's own page offset.
            var pageOffset = skip + (Math.Max(1, request.Page) - 1) * pageSize;
            var result = await provider.LookupAsync(
                new ModelingLookupRequest
                {
                    ModelId = modelId,
                    LookupType = descriptor.LookupType,
                    EntityType = descriptor.EntityType,
                    SearchText = request.SearchText?.Trim() ?? string.Empty,
                    Skip = pageOffset,
                    Take = pageSize,
                    Culture = culture,
                    ContinuationToken = continuationToken
                },
                ct);

            LastResult = result;
            return DropdownDataResult<ModelingLookupItemDto>.WithItems(result.Items, result.TotalCount);
        }
    }
}
