using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Phase 5 (decision <c>MODELING-PROPERTY-SCHEMA</c>, docs/modeling-authoring.md §5): the
/// SCHEMA-DRIVEN half of the inspector. A host registers an
/// <see cref="IModelingPropertySchemaProvider"/> (and optionally an
/// <see cref="IModelingLookupProvider"/>) for its model provider key; the editor resolves them and
/// hands the schema, the per-field validation issues and the lookup provider down to this component.
/// <para>
/// With no schema the component renders exactly the 2.10.0 surface (scalars editable, complex values
/// read-only) — that backward-compatibility contract is test-guarded. WITH a schema the sections and
/// their typed fields replace the generic table for the keys the schema covers (every remaining key
/// still renders in the "Other properties" table, and unknown keys are allowed by the validator).
/// </para>
/// <para>
/// Values are stored per §5.2/§5.4: text as JSON strings, numbers as JSON numbers, booleans as
/// true/false, dates as <c>yyyy-MM-dd</c>, date-times as ISO 8601 round-trip strings, durations as
/// INTEGER MINUTES, entity references as the typed <see cref="ModelingEntityReference"/>
/// (<c>{type, id, display, snapshotAt}</c>) object — array for the list types.
/// </para>
/// </summary>
public partial class TmModelingInspector : IAsyncDisposable
{
    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>The property schema of the SELECTED element/relationship, or <c>null</c> when the
    /// host registered no schema provider for the provider key (2.10.0 behavior).</summary>
    [Parameter] public ModelingPropertySchemaDto? PropertySchema { get; set; }

    /// <summary>Validation issues of the selected element/relationship (the editor's shared-validator
    /// result plus the writer's save-time rejections). They render inline on their field
    /// (<c>role="alert"</c> + <c>aria-invalid</c> on the input) and as the issue dock rows.</summary>
    [Parameter] public IReadOnlyList<ModelingIssueDto>? PropertyIssues { get; set; }

    /// <summary>The host lookup provider for EntityReference / EntityReferenceList fields
    /// (<c>null</c> keeps those fields read-only — a reference cannot be picked without a catalogue).
    /// Its <see cref="IModelingLookupProvider.ResolveAsync"/> re-resolves the stored labels (§5.4).</summary>
    [Parameter] public IModelingLookupProvider? LookupProvider { get; set; }

    /// <summary>Model id passed to the schema/lookup requests.</summary>
    [Parameter] public string? ModelId { get; set; }

    /// <summary>UI culture name for the lookup requests (null = the current UI culture).</summary>
    [Parameter] public string? Culture { get; set; }

    // ── Schema draft state ───────────────────────────────────────────────────

    /// <summary>The JSON value a schema field currently drafts (null = absent/removed).</summary>
    private readonly Dictionary<string, JsonElement?> _schemaDrafts = new(StringComparer.Ordinal);

    /// <summary>Raw text while the user types (wins over the parsed draft, so an in-progress "1." is
    /// never rewritten mid-keystroke). Keys are the field key, or
    /// <c>key[index]</c> / <c>key[index].itemKey</c> for list items.</summary>
    private readonly Dictionary<string, string> _schemaTextDrafts = new(StringComparer.Ordinal);

    /// <summary>Per-PATH local errors (an unparsable number/date the commit refused). A field error
    /// uses its key; a structured-list item uses <c>key[index].itemKey</c>.</summary>
    private readonly Dictionary<string, string> _schemaFieldErrors = new(StringComparer.Ordinal);

    /// <summary>The paths the user actually COMMITTED this session (review U7): only a committed
    /// field announces its error through <c>role="alert"</c>; an error that was already there at seed
    /// time renders as a quiet message until the user touches the field.</summary>
    private readonly HashSet<string> _committedPaths = new(StringComparer.Ordinal);

    /// <summary>The duration field's display unit ("minutes" / "hours" / "days"); the STORED value is
    /// always integer minutes.</summary>
    private readonly Dictionary<string, string> _durationUnits = new(StringComparer.Ordinal);

    /// <summary>Raw JSON of the committed value per schema key at seed time — the reseed comparator.</summary>
    private readonly Dictionary<string, string> _schemaSeeded = new(StringComparer.Ordinal);

    /// <summary>The options a reference field's combobox currently offers: the selected references
    /// (seeded from their display snapshots) plus the host lookup's latest page.</summary>
    private readonly Dictionary<string, List<ModelingLookupItemDto>> _schemaLookupItems = new(StringComparer.Ordinal);

    /// <summary>Fresh labels from <see cref="IModelingLookupProvider.ResolveAsync"/>, by reference id
    /// (review A1). The stored value is never rewritten — only the rendered label changes.</summary>
    private readonly Dictionary<string, string> _referenceLabels = new(StringComparer.Ordinal);

    /// <summary>The reference ids the host no longer knows (review A1): the field renders its stored
    /// snapshot plus the localized stale hint, and the save is never blocked by it.</summary>
    private readonly HashSet<string> _staleReferenceIds = new(StringComparer.Ordinal);

    /// <summary>Per-field cancellation of the label re-resolution (cancelled on reseed/dispose).</summary>
    private readonly Dictionary<string, CancellationTokenSource> _referenceCts = new(StringComparer.Ordinal);

    private int _referenceGeneration;
    private Task _referenceResolution = Task.CompletedTask;

    /// <summary>Collapsed schema sections, by section key (review A11). A section with an error is
    /// always rendered open.</summary>
    private readonly HashSet<string> _collapsedSections = new(StringComparer.Ordinal);

    /// <summary>The lookup provider the last label re-resolution ran against (review A1).</summary>
    private IModelingLookupProvider? _referenceProvider;

    /// <summary>The editing SESSION the schema drafts were seeded for: the selected
    /// element/relationship id plus the schema identity. A session change starts fresh (collapsed
    /// sections and the "user committed this path" markers reset); a new DTO instance for the SAME
    /// session only reseeds the values — the parent applying a change must not erase what the user
    /// did (review U7).</summary>
    private string _schemaDraftSessionKey = string.Empty;

    /// <summary>Test seam (InternalsVisibleTo): the label re-resolution task of the last seed, so a
    /// test can await it deterministically.</summary>
    internal Task TestReferenceResolution => _referenceResolution;

    /// <summary>Whether a schema with at least one section is present (drives the schema surface).</summary>
    private bool HasPropertySchema => PropertySchema is not null && PropertySchema.Sections.Count > 0;

    /// <summary>The sections in display order (a section with no usable field is dropped).</summary>
    private IReadOnlyList<ModelingPropertySectionDto> SchemaSections => PropertySchema is null
        ? []
        : PropertySchema.Sections
            .Where(section => section.Fields.Any(candidate => !string.IsNullOrWhiteSpace(candidate.Key)))
            .OrderBy(section => section.Order)
            .ToArray();

    /// <summary>The property keys the schema covers — everything else lands in "Other properties".</summary>
    private HashSet<string> SchemaKeys
    {
        get
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (PropertySchema is null)
            {
                return keys;
            }

            foreach (var schemaField in PropertySchema.Sections.SelectMany(section => section.Fields))
            {
                if (!string.IsNullOrWhiteSpace(schemaField.Key))
                {
                    keys.Add(schemaField.Key);
                }
            }

            return keys;
        }
    }

    /// <summary>Whether a schema field edits (editing is on, the schema allows it, no save is in
    /// flight and a reference field has a catalogue). A read-only field still shows label + value.</summary>
    private bool CanEditSchemaField(ModelingPropertyFieldDto field)
        => IsEditing && !field.ReadOnly && !Saving
           && (field.Type is not (ModelingPropertyFieldType.EntityReference or ModelingPropertyFieldType.EntityReferenceList)
               || LookupProvider is not null);

    /// <summary>The fields of one section in order.</summary>
    private static IReadOnlyList<ModelingPropertyFieldDto> SectionFields(ModelingPropertySectionDto section)
        => OrderedFields(section.Fields);

    /// <summary>One item-field list (a <see cref="ModelingPropertyFieldType.StructuredList"/>'s
    /// <see cref="ModelingPropertyFieldDto.ItemFields"/>) in order.</summary>
    private static IReadOnlyList<ModelingPropertyFieldDto> OrderedFields(IEnumerable<ModelingPropertyFieldDto> fields)
        => fields
            .Select((field, index) => (field, index))
            .OrderBy(entry => entry.field.Order)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.field)
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .ToArray();

    // ── Seeding ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-seeds the schema drafts from the committed values. Like the scalar drafts this only happens
    /// when the selection, the schema IDENTITY or a committed value actually changed — an unrelated
    /// parent render must never patch a keystroke away (the controlled-input pitfall).
    /// </summary>
    private void SeedSchemaDrafts()
    {
        if (!HasPropertySchema)
        {
            ClearSchemaDraftState();
            return;
        }

        if (Saving)
        {
            return;
        }

        var source = Element is not null
            ? Element.Properties
            : Relationship is not null
                ? Relationship.Properties
                : null;
        if (source is null)
        {
            return;
        }

        var sessionKey = SchemaSessionKey(PropertySchema!);

        // A DIFFERENT selection / schema starts a fresh editing session. A reseed caused only by a
        // COMMITTED VALUE (the parent applied the change the user just made) keeps the session
        // state that belongs to the user — above all the paths the user committed, which is what
        // makes their own error announce assertively (review U7) instead of going quiet again the
        // moment the model is updated.
        var sessionChanged = !string.Equals(sessionKey, _schemaDraftSessionKey, StringComparison.Ordinal);
        var reseed = sessionChanged || !SchemaSeededValuesMatch(source);

        // Review A1: a PROVIDER switch restarts the label re-resolution even when every draft would
        // reseed to the same values — one place decides, so a plain parent render never re-asks.
        var providerChanged = !ReferenceEquals(LookupProvider, _referenceProvider);
        if (providerChanged)
        {
            _referenceProvider = LookupProvider;
        }

        if (!reseed && !providerChanged)
        {
            return;
        }

        if (!reseed)
        {
            StartReferenceResolution();
            return;
        }

        _schemaDraftSessionKey = sessionKey;
        _schemaDrafts.Clear();
        _schemaTextDrafts.Clear();
        _schemaFieldErrors.Clear();
        _schemaSeeded.Clear();
        _collapsedSections.Clear();
        if (sessionChanged)
        {
            _committedPaths.Clear();
        }

        foreach (var field in PropertySchema.Sections.SelectMany(section => section.Fields))
        {
            if (string.IsNullOrWhiteSpace(field.Key) || _schemaSeeded.ContainsKey(field.Key))
            {
                continue;
            }

            var present = source.TryGetValue(field.Key, out var committed);
            _schemaSeeded[field.Key] = present ? committed.GetRawText() : string.Empty;
            _schemaDrafts[field.Key] = present ? committed : DefaultSchemaValue(field);
        }

        foreach (var section in PropertySchema.Sections)
        {
            if (section.CollapsedByDefault && !string.IsNullOrWhiteSpace(section.Key))
            {
                _collapsedSections.Add(section.Key);
            }
        }

        foreach (var field in PropertySchema.Sections.SelectMany(section => section.Fields))
        {
            if (field.Type is ModelingPropertyFieldType.EntityReference or ModelingPropertyFieldType.EntityReferenceList)
            {
                _schemaLookupItems[field.Key] = SelectedReferenceItems(field);
            }
        }

        StartReferenceResolution();
    }

    /// <summary>The identity of the editing session: which element/relationship is inspected and
    /// which schema version/notation/type answers for it.</summary>
    private string SchemaSessionKey(ModelingPropertySchemaDto schema)
        => string.Join(
            '|',
            Element?.Id ?? string.Empty,
            Relationship?.Id ?? string.Empty,
            schema.SchemaId,
            schema.Version,
            schema.Notation,
            schema.SemanticType);

    private bool SchemaSeededValuesMatch(IReadOnlyDictionary<string, JsonElement> source)
    {
        foreach (var seeded in _schemaSeeded)
        {
            var raw = source.TryGetValue(seeded.Key, out var value) ? value.GetRawText() : string.Empty;
            if (!string.Equals(raw, seeded.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void ClearSchemaDraftState()
    {
        _schemaDrafts.Clear();
        _schemaTextDrafts.Clear();
        _schemaFieldErrors.Clear();
        _schemaSeeded.Clear();
        _schemaLookupItems.Clear();
        _committedPaths.Clear();
        _collapsedSections.Clear();
        CancelReferenceResolution();
        _referenceLabels.Clear();
        _staleReferenceIds.Clear();
        _schemaDraftSessionKey = string.Empty;
    }

    /// <summary>A field's default value when the property is absent: the schema default when the host
    /// supplies one, else nothing (the input shows empty and the validator judges the absence).</summary>
    private static JsonElement? DefaultSchemaValue(ModelingPropertyFieldDto field)
        => field.DefaultValue is { } value && value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
            ? value
            : null;

    // ── Label re-resolution (review A1, §5.4) ────────────────────────────────

    /// <summary>The reference fields of the current schema.</summary>
    private IEnumerable<ModelingPropertyFieldDto> ReferenceFields
        => PropertySchema?.Sections
               .SelectMany(section => section.Fields)
               .Where(candidate => candidate.Type is ModelingPropertyFieldType.EntityReference or ModelingPropertyFieldType.EntityReferenceList)
           ?? [];

    /// <summary>Starts ONE resolve call per reference field, cancellable on reseed/dispose. A provider
    /// without <see cref="IModelingLookupProvider.ResolveAsync"/> support (the default member returns
    /// <c>null</c>) resolves nothing and renders no hint.</summary>
    private void StartReferenceResolution()
    {
        CancelReferenceResolution();

        if (LookupProvider is null)
        {
            return;
        }

        _referenceResolution = ResolveReferencesAsync(_referenceGeneration);
    }

    private void CancelReferenceResolution()
    {
        unchecked { _referenceGeneration++; }

        foreach (var cts in _referenceCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _referenceCts.Clear();
    }

    private async Task ResolveReferencesAsync(int generation)
    {
        var provider = LookupProvider;
        if (provider is null)
        {
            return;
        }

        foreach (var field in ReferenceFields.ToArray())
        {
            var references = SelectedReferenceItems(field);
            if (references.Count == 0)
            {
                continue;
            }

            var cts = new CancellationTokenSource();
            _referenceCts[field.Key] = cts;

            ModelingLookupResultDto? result;
            try
            {
                result = await provider.ResolveAsync(
                    new ModelingLookupResolveRequest
                    {
                        ModelId = ModelId ?? string.Empty,
                        Culture = EffectiveSchemaCulture,
                        References = [.. references]
                    },
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // A failing re-resolution is UNRESOLVED, not stale: no hint, stored snapshots stay.
                result = null;
            }

            if (generation != _referenceGeneration)
            {
                return;
            }

            ApplyResolution(references, result);
        }

        if (generation == _referenceGeneration)
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ApplyResolution(IReadOnlyList<ModelingLookupItemDto> references, ModelingLookupResultDto? result)
    {
        if (result is null)
        {
            return;
        }

        foreach (var item in result.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.RefId) && !string.IsNullOrWhiteSpace(item.Display))
            {
                _referenceLabels[item.RefId] = item.Display;
            }
        }

        foreach (var reference in references)
        {
            if (!result.Items.Any(item => string.Equals(item.RefId, reference.RefId, StringComparison.Ordinal)))
            {
                _staleReferenceIds.Add(reference.RefId);
            }
            else
            {
                _staleReferenceIds.Remove(reference.RefId);
            }
        }

        // The combobox labels follow the fresh labels too (the STORED snapshot stays untouched).
        foreach (var items in _schemaLookupItems.Values)
        {
            foreach (var item in items)
            {
                if (_referenceLabels.TryGetValue(item.RefId, out var fresh))
                {
                    item.Display = fresh;
                }
            }
        }
    }

    // ── Draft access (the markup's read side) ────────────────────────────────

    private JsonElement? SchemaDraft(ModelingPropertyFieldDto field)
        => _schemaDrafts.TryGetValue(field.Key, out var value) ? value : null;

    private string SchemaCommittedRaw(ModelingPropertyFieldDto field)
    {
        var source = Element?.Properties ?? (IReadOnlyDictionary<string, JsonElement>?)Relationship?.Properties;
        return source is not null && source.TryGetValue(field.Key, out var value) ? value.GetRawText() : string.Empty;
    }

    /// <summary>The text a text-like input shows: the in-progress typing, else the formatted value.</summary>
    private string SchemaText(ModelingPropertyFieldDto field)
    {
        if (_schemaTextDrafts.TryGetValue(field.Key, out var typed))
        {
            return typed;
        }

        var value = SchemaDraft(field);
        if (value is not { } element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return string.Empty;
        }

        return field.Type switch
        {
            ModelingPropertyFieldType.Duration => FormatDurationForDisplay(field, element),
            ModelingPropertyFieldType.Boolean => element.ValueKind == JsonValueKind.True ? "true" : "false",
            ModelingPropertyFieldType.Integer or ModelingPropertyFieldType.Number => element.ToString(),
            _ => element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString()
        };
    }

    /// <summary>The stored text value of a string/enum field (empty when absent or not a string).</summary>
    private string SchemaString(ModelingPropertyFieldDto field)
        => SchemaDraft(field) is { } value && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private bool SchemaBoolean(ModelingPropertyFieldDto field)
        => SchemaDraft(field) is { } value && value.ValueKind == JsonValueKind.True;

    private DateOnly? SchemaDate(ModelingPropertyFieldDto field)
        => SchemaDraft(field) is { } value
           && value.ValueKind == JsonValueKind.String
           && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private DateTime? SchemaDateTime(ModelingPropertyFieldDto field)
        => SchemaDraft(field) is { } value
           && value.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment)
            ? moment.DateTime
            : null;

    // ── Labels / ids ─────────────────────────────────────────────────────────

    private static string FieldLabel(ModelingPropertyFieldDto field)
        => string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label;

    private static string SectionLabel(ModelingPropertySectionDto section)
        => string.IsNullOrWhiteSpace(section.Label) ? section.Key : section.Label;

    /// <summary>The stable DOM id of a field's input (E2E and <c>&lt;label for&gt;</c> target).</summary>
    private static string FieldDomId(ModelingPropertyFieldDto field)
        => $"modeling-inspector-field-{SanitizeKey(field.Key)}";

    private static string FieldErrorId(ModelingPropertyFieldDto field) => $"{FieldDomId(field)}-error";

    private static string FieldHelpId(ModelingPropertyFieldDto field) => $"{FieldDomId(field)}-help";

    /// <summary>The id of the "reference may be stale" hint (review A1) — part of the field's
    /// <c>aria-describedby</c> while any of its references is unknown to the host.</summary>
    private static string FieldStaleId(ModelingPropertyFieldDto field) => $"{FieldDomId(field)}-stale";

    private static string ItemDomId(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField)
        => $"{FieldDomId(field)}-{index}-{SanitizeKey(itemField.Key)}";

    private static string ItemErrorId(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField)
        => $"{ItemDomId(field, index, itemField)}-error";

    private static string SectionBodyId(ModelingPropertySectionDto section)
        => $"modeling-inspector-section-{SanitizeKey(section.Key)}-body";

    private static string SanitizeKey(string key)
    {
        var builder = new System.Text.StringBuilder(key.Length);
        foreach (var character in key)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        }

        return builder.ToString();
    }

    private static string FieldTypeAttribute(ModelingPropertyFieldDto field)
        => field.Type.ToString().ToLowerInvariant();

    /// <summary>The <c>TmSelect</c> options of an Enum field (label falls back to the value).</summary>
    private static IReadOnlyList<Tempo.Blazor.Models.SelectOption<string>> FieldOptions(ModelingPropertyFieldDto field)
        => field.Options
            .Where(option => !string.IsNullOrWhiteSpace(option.Value))
            .Select(option => new Tempo.Blazor.Models.SelectOption<string>(
                option.Value,
                string.IsNullOrWhiteSpace(option.Label) ? option.Value : option.Label,
                option.Disabled))
            .ToArray();

    // ── Sections (review A11) ────────────────────────────────────────────────

    /// <summary>Whether a section renders collapsed: it opted into
    /// <see cref="ModelingPropertySectionDto.CollapsedByDefault"/>, the user has not expanded it and
    /// none of its fields carries an error (a field error FORCES the section open).</summary>
    private bool IsSectionCollapsed(ModelingPropertySectionDto section)
        => section.CollapsedByDefault
           && _collapsedSections.Contains(section.Key)
           && !SectionHasError(section);

    private void ToggleSection(ModelingPropertySectionDto section)
    {
        if (!section.CollapsedByDefault)
        {
            return;
        }

        if (_collapsedSections.Contains(section.Key))
        {
            _collapsedSections.Remove(section.Key);
        }
        else
        {
            _collapsedSections.Add(section.Key);
        }
    }

    private bool SectionHasError(ModelingPropertySectionDto section)
        => SectionFields(section).Any(field => SchemaFieldHasError(field));

    // ── Errors ───────────────────────────────────────────────────────────────

    /// <summary>The inline error of a field: a local parse failure first, then the shared validator's
    /// issue for the field's OWN path (review U6: an item error belongs to its item, not to the list).
    /// </summary>
    private string? SchemaFieldError(ModelingPropertyFieldDto field)
    {
        if (_schemaFieldErrors.TryGetValue(field.Key, out var local))
        {
            return local;
        }

        var issue = PropertyIssues?.FirstOrDefault(candidate =>
            candidate.Severity == ModelingIssueSeverity.Error
            && IssueBelongsToSelection(candidate)
            && (IsExactIssuePath(candidate, field.Key) || HostIssueTargetsField(candidate, field)));

        return issue is null ? null : LocalizedIssueMessage(issue, field);
    }

    /// <summary>Whether the issue names EXACTLY this path (review U6: a structured item's error stays
    /// on its item, never on the list).</summary>
    private static bool IsExactIssuePath(ModelingIssueDto issue, string path)
        => string.Equals(ModelingPropertyIssueText.PathOf(issue.Id), path, StringComparison.Ordinal);

    /// <summary>A host-authored (writer) issue carries no validator code, so it is matched on its
    /// <see cref="ModelingIssueDto.SuggestedFix"/> — the property key the host rejected. The inspector
    /// still matches coded issues ONLY by their id path (review U10).</summary>
    private static bool HostIssueTargetsField(ModelingIssueDto issue, ModelingPropertyFieldDto field)
        => ModelingPropertyIssueText.PathOf(issue.Id).Length == 0
           && (string.Equals(issue.SuggestedFix, field.Key, StringComparison.Ordinal)
               || string.Equals(issue.SuggestedFix, FieldLabel(field), StringComparison.Ordinal));

    /// <summary>The per-item error of a <see cref="ModelingPropertyFieldType.StructuredList"/> field
    /// (review U6): matched on the EXACT <c>{key}[{index}].{itemKey}</c> path.</summary>
    private string? SchemaItemError(ModelingPropertyFieldDto field, int index, ModelingPropertyFieldDto itemField)
    {
        var path = StructuredItemDraftKey(field, index, itemField);
        if (_schemaFieldErrors.TryGetValue(path, out var local))
        {
            return local;
        }

        var issue = PropertyIssues?.FirstOrDefault(candidate =>
            candidate.Severity == ModelingIssueSeverity.Error
            && IssueBelongsToSelection(candidate)
            && string.Equals(ModelingPropertyIssueText.PathOf(candidate.Id), path, StringComparison.Ordinal));

        return issue is null ? null : LocalizedIssueMessage(issue, itemField);
    }

    /// <summary>Whether the field or any of its items carries an error (the invalid styling and the
    /// section auto-open).</summary>
    private bool SchemaFieldHasError(ModelingPropertyFieldDto field)
    {
        if (_schemaFieldErrors.Keys.Any(path => PathIsWithin(path, field.Key)))
        {
            return true;
        }

        return PropertyIssues?.Any(issue =>
            issue.Severity == ModelingIssueSeverity.Error
            && IssueBelongsToSelection(issue)
            && (PathIsWithin(ModelingPropertyIssueText.PathOf(issue.Id), field.Key) || HostIssueTargetsField(issue, field))) ?? false;
    }

    private static bool PathIsWithin(string path, string key)
        => string.Equals(path, key, StringComparison.Ordinal)
           || path.StartsWith(key + "[", StringComparison.Ordinal)
           || path.StartsWith(key + ".", StringComparison.Ordinal);

    /// <summary>Whether the issue belongs to the inspected element/relationship. An issue with NEITHER
    /// source id is treated as already scoped by the caller (the editor filters the list).</summary>
    private bool IssueBelongsToSelection(ModelingIssueDto issue)
    {
        if (!string.IsNullOrWhiteSpace(issue.SourceElementId))
        {
            return Element is not null && string.Equals(issue.SourceElementId, Element.Id, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(issue.SourceRelationshipId))
        {
            return Relationship is not null && string.Equals(issue.SourceRelationshipId, Relationship.Id, StringComparison.Ordinal);
        }

        return true;
    }

    /// <summary>The localized error text for a shared-validator issue (each language owns the wording;
    /// the English <see cref="ModelingIssueDto.Message"/> stays the MCP-facing text).</summary>
    private string LocalizedIssueMessage(ModelingIssueDto issue, ModelingPropertyFieldDto field)
        => ModelingPropertyIssueText.Localize(Loc, issue, field);

    /// <summary>Whether an error of this path announces itself assertively (review U7): a local parse
    /// failure the user just caused and a field the user already committed do; an error that was
    /// already on the value when the inspector seeded stays a quiet message with
    /// <c>aria-describedby</c> only — a panel that shouts on load is noise.
    /// </summary>
    private bool ShouldAnnounce(string path)
        => _schemaFieldErrors.ContainsKey(path) || _committedPaths.Contains(path);

    private void MarkCommitted(ModelingPropertyFieldDto field) => _committedPaths.Add(field.Key);

    private void SetSchemaFieldError(string path, string? error)
    {
        if (error is null)
        {
            _schemaFieldErrors.Remove(path);
        }
        else
        {
            _schemaFieldErrors[path] = error;
        }
    }

    // ── Text / number / duration inputs ──────────────────────────────────────

    private void HandleSchemaTextInput(ModelingPropertyFieldDto field, string value)
    {
        _schemaTextDrafts[field.Key] = value;
        _schemaFieldErrors.Remove(field.Key);
    }

    private Task HandleSchemaTextKeyDownAsync(ModelingPropertyFieldDto field, KeyboardEventArgs args)
    {
        if (string.Equals(args.Key, "Escape", StringComparison.Ordinal))
        {
            // Escape reverts the DISPLAYED value to the committed one: drop the in-progress text and
            // let the re-render read the parsed draft again.
            _schemaTextDrafts.Remove(field.Key);
            _schemaFieldErrors.Remove(field.Key);
            StateHasChanged();
            return Task.CompletedTask;
        }

        return string.Equals(args.Key, "Enter", StringComparison.Ordinal)
            ? CommitSchemaTextFieldAsync(field)
            : Task.CompletedTask;
    }

    /// <summary>Parses the typed text into the field's JSON shape and commits it (a removal when the
    /// text is emptied). A parse failure stays local: nothing is committed and the field shows the
    /// localized type error.</summary>
    private Task CommitSchemaTextFieldAsync(ModelingPropertyFieldDto field)
    {
        var text = _schemaTextDrafts.TryGetValue(field.Key, out var typed) ? typed : SchemaText(field);

        switch (field.Type)
        {
            case ModelingPropertyFieldType.Integer:
            case ModelingPropertyFieldType.Number:
                if (string.IsNullOrWhiteSpace(text))
                {
                    MarkCommitted(field);
                    return CommitSchemaValueAsync(field, null);
                }

                if (!TryParseNumber(text, out var number))
                {
                    MarkCommitted(field);
                    SetSchemaFieldError(field.Key, TypeErrorMessage(field));
                    return Task.CompletedTask;
                }

                if (field.Type == ModelingPropertyFieldType.Integer)
                {
                    // Review A14: 1.5 is NOT silently rounded into 2 — an integer field refuses a
                    // fractional value with the field-level type error and commits nothing.
                    if (number != Math.Floor(number))
                    {
                        MarkCommitted(field);
                        SetSchemaFieldError(field.Key, IntegerErrorMessage(field));
                        return Task.CompletedTask;
                    }

                    MarkCommitted(field);
                    return CommitSchemaValueAsync(field, JsonSerializer.SerializeToElement((long)number));
                }

                MarkCommitted(field);
                return CommitSchemaValueAsync(field, JsonSerializer.SerializeToElement(number));

            case ModelingPropertyFieldType.Duration:
                if (string.IsNullOrWhiteSpace(text))
                {
                    MarkCommitted(field);
                    return CommitSchemaValueAsync(field, null);
                }

                if (!TryParseNumber(text, out var amount))
                {
                    MarkCommitted(field);
                    SetSchemaFieldError(field.Key, TypeErrorMessage(field));
                    return Task.CompletedTask;
                }

                MarkCommitted(field);
                return CommitSchemaValueAsync(
                    field,
                    JsonSerializer.SerializeToElement((long)Math.Round(amount * DurationFactor(field), MidpointRounding.AwayFromZero)));

            default:
                MarkCommitted(field);
                return CommitSchemaValueAsync(
                    field,
                    text.Length == 0 ? null : JsonSerializer.SerializeToElement(text));
        }
    }

    private string TypeErrorMessage(ModelingPropertyFieldDto field)
        => string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingInspector_FieldType"], FieldLabel(field));

    private string IntegerErrorMessage(ModelingPropertyFieldDto field)
        => string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingInspector_FieldWholeNumber"], FieldLabel(field));

    private static bool TryParseNumber(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
           || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // ── Boolean / enum / date inputs ─────────────────────────────────────────

    private Task HandleSchemaBooleanAsync(ModelingPropertyFieldDto field, bool value)
    {
        MarkCommitted(field);
        return CommitSchemaValueAsync(field, JsonSerializer.SerializeToElement(value));
    }

    private Task HandleSchemaEnumAsync(ModelingPropertyFieldDto field, string? value)
    {
        MarkCommitted(field);
        return CommitSchemaValueAsync(field, string.IsNullOrEmpty(value) ? null : JsonSerializer.SerializeToElement(value));
    }

    private Task HandleSchemaDateAsync(ModelingPropertyFieldDto field, DateOnly? value)
    {
        MarkCommitted(field);
        return CommitSchemaValueAsync(
            field,
            value is null
                ? null
                : JsonSerializer.SerializeToElement(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    private Task HandleSchemaDateTimeAsync(ModelingPropertyFieldDto field, DateTime? value)
    {
        MarkCommitted(field);
        return CommitSchemaValueAsync(
            field,
            value is null
                ? null
                : JsonSerializer.SerializeToElement(new DateTimeOffset(value.Value).ToString("o", CultureInfo.InvariantCulture)));
    }

    /// <summary>Switches the display unit of a duration field (the stored value stays MINUTES).</summary>
    private void HandleDurationUnitChanged(ModelingPropertyFieldDto field, ChangeEventArgs args)
    {
        var unit = args.Value?.ToString() ?? "minutes";
        if (unit is not ("minutes" or "hours" or "days"))
        {
            unit = "minutes";
        }

        _durationUnits[field.Key] = unit;
        _schemaTextDrafts.Remove(field.Key);
        StateHasChanged();
    }

    private string DurationUnit(ModelingPropertyFieldDto field)
        => _durationUnits.TryGetValue(field.Key, out var unit) ? unit : "minutes";

    private string DurationUnitLabel(ModelingPropertyFieldDto field) => DurationUnit(field) switch
    {
        "hours" => Loc["TmModelingInspector_DurationUnitHours"],
        "days" => Loc["TmModelingInspector_DurationUnitDays"],
        _ => Loc["TmModelingInspector_DurationUnitMinutes"]
    };

    private double DurationFactor(ModelingPropertyFieldDto field)
        => DurationUnit(field) switch
        {
            "hours" => 60d,
            "days" => 1440d,
            _ => 1d
        };

    private string FormatDurationForDisplay(ModelingPropertyFieldDto field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var minutes))
        {
            return string.Empty;
        }

        return (minutes / DurationFactor(field)).ToString("0.###", CultureInfo.CurrentUICulture);
    }

    /// <summary>A human readable duration for the read-only presentation: days when it divides
    /// evenly, else hours, else minutes (localized unit words).</summary>
    private string FormatDurationReadable(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var minutes))
        {
            return value.ToString();
        }

        if (minutes >= 1440 && minutes % 1440 == 0)
        {
            return string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingInspector_DurationDays"], minutes / 1440);
        }

        if (minutes >= 60 && minutes % 60 == 0)
        {
            return string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingInspector_DurationHours"], minutes / 60);
        }

        return string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingInspector_DurationMinutes"], minutes);
    }

    // ── Commit ───────────────────────────────────────────────────────────────

    /// <summary>Commits one schema field value (null removes the property). A value equal to the
    /// committed one is a no-op, so a blur without typing never enters the undo stack.</summary>
    private Task CommitSchemaValueAsync(ModelingPropertyFieldDto field, JsonElement? value)
    {
        _schemaTextDrafts.Remove(field.Key);
        _schemaFieldErrors.Remove(field.Key);

        var previous = SchemaCommittedRaw(field);
        var next = value is { } element && element.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
            ? element.GetRawText()
            : string.Empty;

        _schemaDrafts[field.Key] = value;
        if (string.Equals(previous, next, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return value is null
            ? CommitSchemaRemovalAsync(field.Key)
            : CommitSchemaSetAsync(field.Key, value.Value);
    }

    /// <summary>Commits a whole list value (the item actions work on a copy and commit the WHOLE list,
    /// one undo step per action).</summary>
    private Task CommitSchemaArrayAsync(ModelingPropertyFieldDto field, List<JsonElement> items)
        => CommitSchemaValueAsync(field, JsonSerializer.SerializeToElement(items));

    private IReadOnlyList<JsonElement> SchemaArray(ModelingPropertyFieldDto field)
    {
        var value = SchemaDraft(field);
        if (value is not { } element || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private Task CommitSchemaSetAsync(string key, JsonElement value)
    {
        if (Element is not null)
        {
            return OnElementEdited.InvokeAsync(new ModelingElementChange
            {
                ElementId = Element.Id,
                SetProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [key] = value }
            });
        }

        if (Relationship is not null)
        {
            return OnRelationshipEdited.InvokeAsync(new ModelingRelationshipChange
            {
                RelationshipId = Relationship.Id,
                SetProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [key] = value }
            });
        }

        return Task.CompletedTask;
    }

    private Task CommitSchemaRemovalAsync(string key)
    {
        if (Element is not null)
        {
            return OnElementEdited.InvokeAsync(new ModelingElementChange
            {
                ElementId = Element.Id,
                RemovedProperties = [key]
            });
        }

        if (Relationship is not null)
        {
            return OnRelationshipEdited.InvokeAsync(new ModelingRelationshipChange
            {
                RelationshipId = Relationship.Id,
                RemovedProperties = [key]
            });
        }

        return Task.CompletedTask;
    }

    // ── Entity references (§5.4) ─────────────────────────────────────────────

    /// <summary>The current single reference of an EntityReference field.</summary>
    private ModelingEntityReference? SchemaReference(ModelingPropertyFieldDto field)
        => SchemaDraft(field) is { } value ? ModelingEntityReference.TryParse(value) : null;

    /// <summary>The current references of an EntityReferenceList field.</summary>
    private IReadOnlyList<ModelingEntityReference> SchemaReferences(ModelingPropertyFieldDto field)
        => SchemaArray(field)
            .Select(ModelingEntityReference.TryParse)
            .Where(reference => reference is not null)
            .Select(reference => reference!)
            .ToArray();

    /// <summary>The label a reference renders: the freshly resolved display when the host answered
    /// (review A1 — labels only, the stored value never changes), else its stored snapshot.</summary>
    private string ReferenceLabel(ModelingEntityReference reference)
        => _referenceLabels.TryGetValue(reference.Id, out var fresh) && !string.IsNullOrWhiteSpace(fresh)
            ? fresh
            : reference.Label;

    /// <summary>Whether any reference the field stores is unknown to the host (review A1).</summary>
    private bool HasStaleReference(ModelingPropertyFieldDto field)
        => field.Type == ModelingPropertyFieldType.EntityReferenceList
            ? SchemaReferences(field).Any(reference => _staleReferenceIds.Contains(reference.Id))
            : SchemaReference(field) is { } single && _staleReferenceIds.Contains(single.Id);

    /// <summary>The localized "reference may be stale" hint, or <c>null</c>.</summary>
    private string? StaleReferenceHint(ModelingPropertyFieldDto field)
        => HasStaleReference(field) ? Loc["TmModelingInspector_ReferenceStale"] : null;

    /// <summary>The references the field currently stores, as lookup items (so the combobox can show
    /// their DISPLAY SNAPSHOT — or the freshly resolved label — without a network call).</summary>
    private List<ModelingLookupItemDto> SelectedReferenceItems(ModelingPropertyFieldDto field)
    {
        if (field.Type == ModelingPropertyFieldType.EntityReferenceList)
        {
            return SchemaReferences(field)
                .Select(reference => new ModelingLookupItemDto
                {
                    RefType = reference.Type,
                    RefId = reference.Id,
                    Display = ReferenceLabel(reference)
                })
                .ToList();
        }

        return SchemaReference(field) is { } single
            ? [new ModelingLookupItemDto { RefType = single.Type, RefId = single.Id, Display = ReferenceLabel(single) }]
            : [];
    }

    /// <summary>Builds the typed reference value written into the property (§5.4) for a NEWLY picked
    /// lookup item — its display becomes the snapshot and the timestamp is now.</summary>
    private static JsonElement BuildReference(ModelingLookupItemDto item)
        => ModelingEntityReference.FromLookupItem(item, DateTimeOffset.UtcNow).ToJsonElement();
}
