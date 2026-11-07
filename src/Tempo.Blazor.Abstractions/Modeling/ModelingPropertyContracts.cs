using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling;

// Decision MODELING-PROPERTY-SCHEMA (docs/modeling-authoring.md §5). Everything here is OPTIONAL:
// with no IModelingPropertySchemaProvider registered for a provider key the inspector behaves
// exactly like 2.10.0 and the operation engine validates no property values at all.

/// <summary>Request passed to an <see cref="IModelingPropertySchemaProvider"/>.</summary>
public sealed class ModelingPropertySchemaRequest
{
    /// <summary>Model the selection belongs to.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Notation key the selection is rendered under (the model's or the host's override).</summary>
    public string Notation { get; set; } = string.Empty;

    /// <summary>Semantic element type, or the relationship type for a relationship selection.</summary>
    public string SemanticType { get; set; } = string.Empty;

    /// <summary>Active viewpoint key (empty when the host has none).</summary>
    public string ViewpointKey { get; set; } = string.Empty;

    /// <summary>UI culture name the labels are expected in.</summary>
    public string Culture { get; set; } = string.Empty;

    /// <summary>Version the host previously served (null = latest). Lets the host pin a schema so a
    /// mid-session deploy cannot silently re-interpret a value.</summary>
    public string? SchemaVersion { get; set; }
}

/// <summary>Provides the property schema (sections + typed fields) for a notation + semantic type.
/// Resolved by provider key, exactly like <see cref="IModelingModelProvider"/> /
/// <see cref="IModelingModelWriter"/>. Returning <c>null</c> means "no schema" and restores the
/// 2.10.0 inspector (scalars editable, complex values read-only).</summary>
public interface IModelingPropertySchemaProvider
{
    /// <summary>Unique provider key; the editor resolves the provider per model provider key.</summary>
    string ProviderKey { get; }

    /// <summary>Loads the schema for the requested notation/type, or <c>null</c> when the host has none.</summary>
    Task<ModelingPropertySchemaDto?> GetSchemaAsync(ModelingPropertySchemaRequest request, CancellationToken cancellationToken);
}

/// <summary>Host lookups for entity-reference fields. One provider per provider key.</summary>
public interface IModelingLookupProvider
{
    /// <summary>Unique provider key; the editor resolves the provider per model provider key.</summary>
    string ProviderKey { get; }

    /// <summary>Searches the host's entity catalogue.</summary>
    Task<ModelingLookupResultDto> LookupAsync(ModelingLookupRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Re-resolves the DISPLAY LABELS of references a value already stores (§5.4 "the editor
    /// re-resolves the label on demand"). The stored value is never rewritten: a fresh
    /// <see cref="ModelingLookupItemDto.Display"/> only replaces the label the user sees, and an id
    /// the host no longer knows (or an unsupported provider) degrades to the stored snapshot plus a
    /// localized "reference may be stale" hint — it never blocks the save.
    /// <para>
    /// A <b>default interface member</b> returning <c>null</c>, so every existing lookup provider —
    /// which can search but cannot resolve by id — keeps compiling and behaves exactly as before:
    /// <c>null</c> means "re-resolution unsupported" and the inspector shows no hint at all. A
    /// returned <see cref="ModelingLookupResultDto"/> is matched by
    /// <see cref="ModelingLookupItemDto.RefId"/>; an id missing from <c>Items</c> is STALE.
    /// </para>
    /// </summary>
    /// <param name="request">The model, the culture and the references to resolve.</param>
    /// <param name="cancellationToken">Cancelled when the selection/schema re-seeds or the inspector is disposed.</param>
    Task<ModelingLookupResultDto?> ResolveAsync(ModelingLookupResolveRequest request, CancellationToken cancellationToken)
        => Task.FromResult<ModelingLookupResultDto?>(null);
}

/// <summary>The property schema of one notation + semantic type: ordered sections of typed fields.</summary>
public sealed class ModelingPropertySchemaDto
{
    private string _schemaId = string.Empty;
    private string _version = string.Empty;
    private string _notation = string.Empty;
    private string _semanticType = string.Empty;
    private List<ModelingPropertySectionDto> _sections = [];

    /// <summary>Stable identity of this schema.</summary>
    public string SchemaId
    {
        get => _schemaId;
        set => _schemaId = value ?? string.Empty;
    }

    /// <summary>Opaque, host-owned version (e.g. <c>"4"</c>).</summary>
    public string Version
    {
        get => _version;
        set => _version = value ?? string.Empty;
    }

    /// <summary>Notation the schema belongs to.</summary>
    public string Notation
    {
        get => _notation;
        set => _notation = value ?? string.Empty;
    }

    /// <summary>Semantic type the schema belongs to; empty = every type of the notation.</summary>
    public string SemanticType
    {
        get => _semanticType;
        set => _semanticType = value ?? string.Empty;
    }

    /// <summary>Ordered sections.</summary>
    public List<ModelingPropertySectionDto> Sections
    {
        get => _sections;
        set => _sections = value ?? [];
    }
}

/// <summary>One collapsible group of schema fields.</summary>
public sealed class ModelingPropertySectionDto
{
    private string _key = string.Empty;
    private string _label = string.Empty;
    private List<ModelingPropertyFieldDto> _fields = [];

    /// <summary>Stable section key (test id + collapse state identity).</summary>
    public string Key
    {
        get => _key;
        set => _key = value ?? string.Empty;
    }

    /// <summary>Visible section label.</summary>
    public string Label
    {
        get => _label;
        set => _label = value ?? string.Empty;
    }

    /// <summary>Optional section help text.</summary>
    public string? Help { get; set; }

    /// <summary>Sort order (null sections keep declaration order).</summary>
    public int Order { get; set; }

    /// <summary>Whether the section starts collapsed.</summary>
    public bool CollapsedByDefault { get; set; }

    /// <summary>Fields of the section, rendered in list order.</summary>
    public List<ModelingPropertyFieldDto> Fields
    {
        get => _fields;
        set => _fields = value ?? [];
    }
}

/// <summary>The value type of a schema field (and of the JSON shape it stores, see §5.2).</summary>
public enum ModelingPropertyFieldType
{
    /// <summary>Single-line text — JSON string.</summary>
    String,

    /// <summary>Markdown text — JSON string, edited through the Tempo markdown editor.</summary>
    Markdown,

    /// <summary>Multi-line plain text — JSON string.</summary>
    MultilineText,

    /// <summary>Integer number — JSON integer.</summary>
    Integer,

    /// <summary>Any number — JSON number.</summary>
    Number,

    /// <summary>Boolean — JSON true/false.</summary>
    Boolean,

    /// <summary>One of the offered options — JSON string = the option value.</summary>
    Enum,

    /// <summary>Date without time — JSON string <c>yyyy-MM-dd</c>.</summary>
    Date,

    /// <summary>Date and time — ISO 8601 round-trip string with offset.</summary>
    DateTime,

    /// <summary>Duration — JSON integer MINUTES.</summary>
    Duration,

    /// <summary>Absolute http(s) URL — JSON string.</summary>
    Url,

    /// <summary>One typed entity reference — JSON object <c>{type, id, display, snapshotAt}</c>.</summary>
    EntityReference,

    /// <summary>A list of typed entity references — JSON array of the same object.</summary>
    EntityReferenceList,

    /// <summary>A list of strings — JSON array of strings.</summary>
    StringList,

    /// <summary>A repeatable list of structured items — JSON array of objects keyed by
    /// <see cref="ModelingPropertyFieldDto.ItemFields"/>.</summary>
    StructuredList
}

/// <summary>One typed field of a schema section.</summary>
public sealed class ModelingPropertyFieldDto
{
    private string _key = string.Empty;
    private string _label = string.Empty;
    private List<ModelingPropertyOptionDto> _options = [];
    private List<ModelingPropertyFieldDto> _itemFields = [];

    /// <summary>The <c>ModelingElementDto.Properties</c> key this field stores.</summary>
    public string Key
    {
        get => _key;
        set => _key = value ?? string.Empty;
    }

    /// <summary>Visible field label (also the suggested fix of a validation issue).</summary>
    public string Label
    {
        get => _label;
        set => _label = value ?? string.Empty;
    }

    /// <summary>Value type.</summary>
    public ModelingPropertyFieldType Type { get; set; }

    /// <summary>Whether a value is required (missing/empty/null fails validation).</summary>
    public bool Required { get; set; }

    /// <summary>Whether the field renders read-only for a user who may otherwise edit.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Optional field help text.</summary>
    public string? Help { get; set; }

    /// <summary>Optional input placeholder.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Display-only unit label ("h", "days", "CZK").</summary>
    public string? Unit { get; set; }

    /// <summary>Optional default value (the editor offers it, the validator does not fill it in).</summary>
    public JsonElement? DefaultValue { get; set; }

    /// <summary>Minimum for numeric values (Integer, Number, Duration).</summary>
    public double? Min { get; set; }

    /// <summary>Maximum for numeric values (Integer, Number, Duration).</summary>
    public double? Max { get; set; }

    /// <summary>Maximum text length for the text types.</summary>
    public int? MaxLength { get; set; }

    /// <summary>Host-authored regular expression a text value must match.</summary>
    public string? Pattern { get; set; }

    /// <summary>Offered options (Enum).</summary>
    public List<ModelingPropertyOptionDto> Options
    {
        get => _options;
        set => _options = value ?? [];
    }

    /// <summary>Lookup descriptor (EntityReference / EntityReferenceList).</summary>
    public ModelingLookupDescriptor? Lookup { get; set; }

    /// <summary>Item fields of a StructuredList; ignored by every other type.</summary>
    public List<ModelingPropertyFieldDto> ItemFields
    {
        get => _itemFields;
        set => _itemFields = value ?? [];
    }

    /// <summary>Sort order inside the section (declaration order breaks ties).</summary>
    public int Order { get; set; }
}

/// <summary>One offered value of an Enum field.</summary>
public sealed class ModelingPropertyOptionDto
{
    private string _value = string.Empty;
    private string _label = string.Empty;

    /// <summary>The stored value (JSON string written into the property).</summary>
    public string Value
    {
        get => _value;
        set => _value = value ?? string.Empty;
    }

    /// <summary>The visible label.</summary>
    public string Label
    {
        get => _label;
        set => _label = value ?? string.Empty;
    }

    /// <summary>Optional option help text.</summary>
    public string? Help { get; set; }

    /// <summary>Whether the option cannot be picked (an already stored value still validates).</summary>
    public bool Disabled { get; set; }
}

/// <summary>How an EntityReference / EntityReferenceList field searches the host's catalogue.</summary>
public sealed class ModelingLookupDescriptor
{
    private string _lookupType = string.Empty;

    /// <summary>Host vocabulary: "role", "system", "document".</summary>
    public string LookupType
    {
        get => _lookupType;
        set => _lookupType = value ?? string.Empty;
    }

    /// <summary>Optional narrowing inside <see cref="LookupType"/>.</summary>
    public string? EntityType { get; set; }

    /// <summary>Minimum typed characters before a query is issued.</summary>
    public int MinQueryLength { get; set; } = 2;

    /// <summary>Page size requested from the provider.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>Whether a query is required (false = the first page is offered immediately).</summary>
    public bool SearchRequired { get; set; } = true;
}

/// <summary>One lookup query.</summary>
public sealed class ModelingLookupRequest
{
    private string _modelId = string.Empty;
    private string _lookupType = string.Empty;
    private string _searchText = string.Empty;
    private string _culture = string.Empty;

    /// <summary>Model the field belongs to.</summary>
    public string ModelId
    {
        get => _modelId;
        set => _modelId = value ?? string.Empty;
    }

    /// <summary>Host vocabulary of the lookup ("role", "system", …).</summary>
    public string LookupType
    {
        get => _lookupType;
        set => _lookupType = value ?? string.Empty;
    }

    /// <summary>Optional entity-type narrowing.</summary>
    public string? EntityType { get; set; }

    /// <summary>The typed search term.</summary>
    public string SearchText
    {
        get => _searchText;
        set => _searchText = value ?? string.Empty;
    }

    /// <summary>Items to skip (paging).</summary>
    public int Skip { get; set; }

    /// <summary>Items to take (paging).</summary>
    public int Take { get; set; } = 20;

    /// <summary>UI culture name.</summary>
    public string Culture
    {
        get => _culture;
        set => _culture = value ?? string.Empty;
    }

    /// <summary>Host-owned paging cursor (optional).</summary>
    public string? ContinuationToken { get; set; }
}

/// <summary>
/// One label re-resolution batch (§5.4): every reference value the inspector currently renders, so
/// the host answers with ONE call per field instead of one call per reference.
/// </summary>
public sealed class ModelingLookupResolveRequest
{
    private string _modelId = string.Empty;
    private string _culture = string.Empty;
    private List<ModelingLookupItemDto> _references = [];

    /// <summary>Model the references belong to.</summary>
    public string ModelId
    {
        get => _modelId;
        set => _modelId = value ?? string.Empty;
    }

    /// <summary>UI culture name of the labels.</summary>
    public string Culture
    {
        get => _culture;
        set => _culture = value ?? string.Empty;
    }

    /// <summary>The stored references to re-resolve (their <c>RefId</c> is the key).</summary>
    public List<ModelingLookupItemDto> References
    {
        get => _references;
        set => _references = value ?? [];
    }
}

/// <summary>One page of lookup results.</summary>
public sealed class ModelingLookupResultDto
{
    private List<ModelingLookupItemDto> _items = [];

    /// <summary>The items of this page.</summary>
    public List<ModelingLookupItemDto> Items
    {
        get => _items;
        set => _items = value ?? [];
    }

    /// <summary>Total number of matches the host knows about.</summary>
    public int TotalCount { get; set; }

    /// <summary>Whether another page exists.</summary>
    public bool HasMore { get; set; }

    /// <summary>Host-owned paging cursor (optional).</summary>
    public string? ContinuationToken { get; set; }
}

/// <summary>One lookup hit; its <see cref="RefType"/>/<see cref="RefId"/>/<see cref="Display"/> become
/// the stored entity reference snapshot.</summary>
public sealed class ModelingLookupItemDto
{
    private string _refType = string.Empty;
    private string _refId = string.Empty;
    private string _display = string.Empty;

    /// <summary>Reference type (usually the descriptor's EntityType/LookupType).</summary>
    public string RefType
    {
        get => _refType;
        set => _refType = value ?? string.Empty;
    }

    /// <summary>Reference identifier.</summary>
    public string RefId
    {
        get => _refId;
        set => _refId = value ?? string.Empty;
    }

    /// <summary>The label the user sees (and the stored display snapshot).</summary>
    public string Display
    {
        get => _display;
        set => _display = value ?? string.Empty;
    }

    /// <summary>Optional longer description.</summary>
    public string? Description { get; set; }

    /// <summary>Optional secondary line ("Sales • CZ").</summary>
    public string? SecondaryText { get; set; }

    /// <summary>Whether the item cannot be picked.</summary>
    public bool Disabled { get; set; }
}

/// <summary>
/// The SHARED property-value validator (decision <c>MODELING-PROPERTY-SCHEMA</c> §5.5). One rule set
/// for the inspector's field errors, the editor's save gate and
/// <see cref="ModelingOperationEngine"/> — "one semantics, same errors".
/// <para>
/// Every returned <see cref="ModelingIssueDto"/> has <c>Category = "property"</c>,
/// <c>SuggestedFix</c> = the top-level field label and a machine-readable
/// <c>Id = "&lt;code&gt;:&lt;path&gt;"</c> where the code is <c>required</c>, <c>type</c>,
/// <c>range</c>, <c>length</c>, <c>pattern</c>, <c>enum</c> or <c>reference</c> for an Error and
/// <c>unknown</c> for the Info-level host-added key. The path is the property key or
/// <c>key[index].itemKey</c> inside a <see cref="ModelingPropertyFieldType.StructuredList"/>.
/// <c>SourceElementId</c>/<c>SourceRelationshipId</c> are filled by the CALLER (the validator has no
/// element in hand — the engine and the editor stamp their own owner id).
/// </para>
/// </summary>
public sealed class ModelingPropertySchemaValidator
{
    /// <summary>Validates <paramref name="properties"/> against <paramref name="schema"/>.</summary>
    public IReadOnlyList<ModelingIssueDto> Validate(
        ModelingPropertySchemaDto schema,
        IReadOnlyDictionary<string, JsonElement> properties)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(properties);

        var issues = new List<ModelingIssueDto>();
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in OrderedFields(schema))
        {
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                continue;
            }

            // A field declared twice (two sections) is validated once: the schema author's
            // duplicate must not double the required error.
            if (!known.Add(field.Key))
            {
                continue;
            }

            var path = field.Key;
            var present = properties.TryGetValue(field.Key, out var value)
                && !IsMissing(value);

            if (!present && !field.Required)
            {
                continue;
            }

            ValidateField(field, present ? value : default, path, FieldLabel(field), field, issues);
        }

        foreach (var property in properties)
        {
            if (known.Contains(property.Key))
            {
                continue;
            }

            issues.Add(new ModelingIssueDto
            {
                Id = $"unknown:{property.Key}",
                Severity = ModelingIssueSeverity.Info,
                Category = "property",
                Message = $"Property '{property.Key}' is not part of the schema '{schema.SchemaId}'.",
                SuggestedFix = property.Key
            });
        }

        return issues;
    }

    /// <summary>The sections' fields ordered by <c>Order</c> then declaration order.</summary>
    private static IEnumerable<ModelingPropertyFieldDto> OrderedFields(ModelingPropertySchemaDto schema)
        => schema.Sections
            .OrderBy(section => section.Order)
            .SelectMany(section => section.Fields
                .Select((field, index) => (field, index))
                .OrderBy(item => item.field.Order)
                .ThenBy(item => item.index)
                .Select(item => item.field));

    private static string FieldLabel(ModelingPropertyFieldDto field)
        => string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label;

    private static bool IsMissing(JsonElement value)
        => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

    private static void ValidateField(
        ModelingPropertyFieldDto field,
        JsonElement value,
        string path,
        string label,
        ModelingPropertyFieldDto topLevelField,
        List<ModelingIssueDto> issues)
    {
        var suggestedFix = FieldLabel(topLevelField);

        if (IsMissing(value)
            || (field.Required && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0))
        {
            if (field.Required)
            {
                Add(issues, "required", path, suggestedFix, $"Property '{label}' is required.");
            }

            return;
        }

        switch (field.Type)
        {
            case ModelingPropertyFieldType.Integer:
                ValidateNumeric(value, path, label, suggestedFix, integer: true, field, issues);
                break;

            case ModelingPropertyFieldType.Number:
            case ModelingPropertyFieldType.Duration:
                ValidateNumeric(value, path, label, suggestedFix, integer: field.Type == ModelingPropertyFieldType.Duration, field, issues);
                break;

            case ModelingPropertyFieldType.Boolean:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    Add(issues, "type", path, suggestedFix, $"Property '{label}' must be true or false.");
                }

                break;

            case ModelingPropertyFieldType.Enum:
                ValidateEnum(value, path, label, suggestedFix, field, issues);
                break;

            case ModelingPropertyFieldType.Date:
                if (value.ValueKind != JsonValueKind.String
                    || !DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    Add(issues, "type", path, suggestedFix, $"Property '{label}' must be a date in the yyyy-MM-dd format.");
                }

                break;

            case ModelingPropertyFieldType.DateTime:
                if (value.ValueKind != JsonValueKind.String
                    || !IsIsoDateTimeWithOffset(value.GetString()))
                {
                    Add(issues, "type", path, suggestedFix, $"Property '{label}' must be an ISO 8601 date and time with a UTC offset (for example 2026-11-06T09:24:33Z or 2026-11-06T09:24:33+01:00).");
                }

                break;

            case ModelingPropertyFieldType.Url:
                if (value.ValueKind != JsonValueKind.String)
                {
                    Add(issues, "type", path, suggestedFix, $"Property '{label}' must be text.");
                }
                else if (!Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri)
                         || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    Add(issues, "pattern", path, suggestedFix, $"Property '{label}' must be an absolute http(s) URL.");
                }

                break;

            case ModelingPropertyFieldType.EntityReference:
                ValidateReference(value, path, label, suggestedFix, issues);
                break;

            case ModelingPropertyFieldType.EntityReferenceList:
                ValidateList(value, path, label, suggestedFix, issues, (item, itemPath) =>
                {
                    if (IsMissing(item) && !field.Required)
                    {
                        return;
                    }

                    ValidateReference(item, itemPath, label, suggestedFix, issues);
                });
                break;

            case ModelingPropertyFieldType.StringList:
                ValidateList(value, path, label, suggestedFix, issues, (item, itemPath) =>
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        Add(issues, "type", itemPath, suggestedFix, $"Property '{label}' must contain only text values.");
                    }
                });
                break;

            case ModelingPropertyFieldType.StructuredList:
                ValidateList(value, path, label, suggestedFix, issues, (item, itemPath) =>
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        Add(issues, "type", itemPath, suggestedFix, $"Property '{label}' must contain objects.");
                        return;
                    }

                    foreach (var itemField in field.ItemFields
                                 .Select((itemField, index) => (itemField, index))
                                 .OrderBy(entry => entry.itemField.Order)
                                 .ThenBy(entry => entry.index)
                                 .Select(entry => entry.itemField))
                    {
                        if (string.IsNullOrWhiteSpace(itemField.Key))
                        {
                            continue;
                        }

                        var itemPathWithKey = $"{itemPath}.{itemField.Key}";
                        var itemPresent = item.TryGetProperty(itemField.Key, out var itemValue) && !IsMissing(itemValue);
                        if (!itemPresent && !itemField.Required)
                        {
                            continue;
                        }

                        ValidateField(
                            itemField,
                            itemPresent ? itemValue : default,
                            itemPathWithKey,
                            $"{label} / {FieldLabel(itemField)}",
                            topLevelField,
                            issues);
                    }
                });
                break;

            default:
                ValidateText(value, path, label, suggestedFix, field, issues);
                break;
        }
    }

    private static void ValidateText(
        JsonElement value,
        string path,
        string label,
        string suggestedFix,
        ModelingPropertyFieldDto field,
        List<ModelingIssueDto> issues)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            Add(issues, "type", path, suggestedFix, $"Property '{label}' must be text.");
            return;
        }

        var text = value.GetString() ?? string.Empty;
        if (field.MaxLength is { } maxLength && text.Length > maxLength)
        {
            Add(issues, "length", path, suggestedFix, $"Property '{label}' must be at most {maxLength} characters long.");
        }

        if (!string.IsNullOrWhiteSpace(field.Pattern))
        {
            if (!MatchesPattern(text, field.Pattern))
            {
                Add(issues, "pattern", path, suggestedFix, $"Property '{label}' does not match the required format.");
            }
        }
    }

    /// <summary>A host-authored pattern that does not compile is ignored (never a crash, never a
    /// silently always-failing field).</summary>
    private static bool MatchesPattern(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    private static void ValidateNumeric(
        JsonElement value,
        string path,
        string label,
        string suggestedFix,
        bool integer,
        ModelingPropertyFieldDto field,
        List<ModelingIssueDto> issues)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
        {
            Add(issues, "type", path, suggestedFix, $"Property '{label}' must be a number.");
            return;
        }

        if (integer && number != Math.Floor(number))
        {
            Add(issues, "type", path, suggestedFix, $"Property '{label}' must be a whole number.");
            return;
        }

        if (field.Min is { } min && number < min)
        {
            Add(issues, "range", path, suggestedFix, RangeMessage(label, field.Min, field.Max));
            return;
        }

        if (field.Max is { } max && number > max)
        {
            Add(issues, "range", path, suggestedFix, RangeMessage(label, field.Min, field.Max));
        }
    }

    private static string RangeMessage(string label, double? min, double? max)
        => min is not null && max is not null
            ? $"Property '{label}' must be between {Format(min)} and {Format(max)}."
            : min is not null
                ? $"Property '{label}' must be at least {Format(min)}."
                : $"Property '{label}' must be at most {Format(max)}.";

    private static string Format(double? value)
        => value is null ? "?" : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void ValidateEnum(
        JsonElement value,
        string path,
        string label,
        string suggestedFix,
        ModelingPropertyFieldDto field,
        List<ModelingIssueDto> issues)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            Add(issues, "enum", path, suggestedFix, EnumMessage(label, field));
            return;
        }

        var text = value.GetString();
        if (!field.Options.Any(option => string.Equals(option.Value, text, StringComparison.Ordinal)))
        {
            Add(issues, "enum", path, suggestedFix, EnumMessage(label, field));
        }
    }

    private static string EnumMessage(string label, ModelingPropertyFieldDto field)
    {
        var offered = string.Join(", ", field.Options
            .Where(option => !string.IsNullOrWhiteSpace(option.Value))
            .Select(option => string.IsNullOrWhiteSpace(option.Label) ? option.Value : option.Label));

        return offered.Length == 0
            ? $"Property '{label}' must be one of the schema's options."
            : $"Property '{label}' must be one of: {offered}.";
    }

    /// <summary>§5.4: a reference is the typed snapshot object <c>{type, id, display, snapshotAt}</c> —
    /// never a bare id, never HTML. Only <c>type</c> and <c>id</c> are REQUIRED; the display snapshot
    /// and its timestamp are optional so a portal can still render a value it cannot re-resolve.</summary>
    private static void ValidateReference(
        JsonElement value,
        string path,
        string label,
        string suggestedFix,
        List<ModelingIssueDto> issues)
    {
        if (!ModelingEntityReference.TryParse(value, out _))
        {
            Add(
                issues,
                "reference",
                path,
                suggestedFix,
                $"Property '{label}' must be an entity reference with a non-empty 'type' and 'id'.");
        }
    }

    /// <summary>
    /// The accepted <see cref="ModelingPropertyFieldType.DateTime"/> literals (§5.2: "ISO 8601
    /// round-trip string with offset"). Anchored exactly — a bare date, a culture-specific
    /// <c>11/06/2026 9:00</c> or an ISO literal WITHOUT an offset is rejected, so the stored value
    /// always names its zone and a portal can order/label it without guessing the author's machine.
    /// </summary>
    private static readonly string[] IsoDateTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    ];

    /// <summary>Whether the text is an ISO 8601 date-time that carries <c>Z</c> or <c>±hh:mm</c>.</summary>
    private static bool IsIsoDateTimeWithOffset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !DateTimeOffset.TryParseExact(
                text,
                IsoDateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            return false;
        }

        // The 'K' specifier also matches "no offset at all", so the suffix decides: Z or ±hh:mm.
        if (text.EndsWith('Z'))
        {
            return true;
        }

        return text.Length >= 6
               && text[^6] is '+' or '-'
               && text[^5..^3].All(char.IsAsciiDigit)
               && text[^3] == ':'
               && text[^2..].All(char.IsAsciiDigit);
    }

    private static void ValidateList(
        JsonElement value,
        string path,
        string label,
        string suggestedFix,
        List<ModelingIssueDto> issues,
        Action<JsonElement, string> validateItem)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            Add(issues, "type", path, suggestedFix, $"Property '{label}' must be a list.");
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            validateItem(item, $"{path}[{index}]");
            index++;
        }
    }

    private static void Add(
        List<ModelingIssueDto> issues,
        string code,
        string path,
        string suggestedFix,
        string message)
        => issues.Add(new ModelingIssueDto
        {
            Id = $"{code}:{path}",
            Severity = ModelingIssueSeverity.Error,
            Category = "property",
            Message = message,
            SuggestedFix = suggestedFix
        });
}

/// <summary>
/// The typed entity-reference value of §5.4 — the ONE reader/writer of the stored JSON shape
/// <c>{ "type", "id", "display", "snapshotAt" }</c>. Shared by <see cref="ModelingPropertySchemaValidator"/>,
/// the inspector's read/build paths and a host writer (the shipped demo writer rejects a reference
/// the catalogue no longer knows), so the shape can never drift between them.
/// <para>
/// <see cref="Type"/> and <see cref="Id"/> are required; <see cref="Display"/> is the host-owned
/// label snapshot (the value renders without a lookup call) and <see cref="SnapshotAt"/> says when
/// that label was captured (ISO 8601). <see cref="ToJsonElement"/> writes <c>snapshotAt</c> only
/// when it is set, so a value that never had one round-trips unchanged.
/// </para>
/// </summary>
/// <param name="Type">Reference type ("role", "system", …).</param>
/// <param name="Id">Reference identifier inside <paramref name="Type"/>.</param>
/// <param name="Display">Label snapshot the value renders without a lookup call.</param>
/// <param name="SnapshotAt">When the label snapshot was captured (ISO 8601), if known.</param>
public sealed record ModelingEntityReference(string Type, string Id, string Display, string? SnapshotAt)
{
    /// <summary>Reads the stored shape. Returns <c>false</c> (with an empty reference) unless
    /// <c>type</c> and <c>id</c> are non-empty JSON strings; <c>display</c> and <c>snapshotAt</c>
    /// are optional.</summary>
    public static bool TryParse(JsonElement value, out ModelingEntityReference reference)
    {
        reference = new ModelingEntityReference(string.Empty, string.Empty, string.Empty, null);

        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var type = ReadString(value, "type");
        var id = ReadString(value, "id");
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var snapshotAt = ReadString(value, "snapshotAt");
        reference = new ModelingEntityReference(
            type,
            id,
            ReadString(value, "display"),
            string.IsNullOrWhiteSpace(snapshotAt) ? null : snapshotAt);
        return true;
    }

    /// <summary>Convenience overload: the stored reference, or <c>null</c> when the value is not
    /// one.</summary>
    public static ModelingEntityReference? TryParse(JsonElement value)
        => TryParse(value, out var reference) ? reference : null;

    /// <summary>The reference a lookup item stores (its display becomes the label snapshot).</summary>
    /// <param name="item">The picked lookup item.</param>
    /// <param name="snapshotAt">The capture timestamp; null keeps the value timestamp-free.</param>
    public static ModelingEntityReference FromLookupItem(ModelingLookupItemDto item, DateTimeOffset snapshotAt)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new ModelingEntityReference(
            item.RefType,
            item.RefId,
            item.Display,
            snapshotAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }

    /// <summary>The label this reference renders: its display snapshot, else its id, else its type.</summary>
    public string Label
        => !string.IsNullOrWhiteSpace(Display)
            ? Display
            : !string.IsNullOrWhiteSpace(Id) ? Id : Type;

    /// <summary>Writes the stored shape <c>{type, id, display, snapshotAt}</c>.</summary>
    public JsonElement ToJsonElement()
    {
        var value = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["type"] = JsonSerializer.SerializeToElement(Type),
            ["id"] = JsonSerializer.SerializeToElement(Id),
            ["display"] = JsonSerializer.SerializeToElement(Display)
        };

        if (!string.IsNullOrWhiteSpace(SnapshotAt))
        {
            value["snapshotAt"] = JsonSerializer.SerializeToElement(SnapshotAt);
        }

        return JsonSerializer.SerializeToElement(value);
    }

    /// <summary>This reference as the lookup item the combobox renders (its stored snapshot).</summary>
    public ModelingLookupItemDto ToLookupItem()
        => new() { RefType = Type, RefId = Id, Display = Label };

    /// <summary>Whether the reference names this id (the host resolution's match key).</summary>
    public bool HasId(string id) => string.Equals(Id, id, StringComparison.Ordinal);

    private static string ReadString(JsonElement value, string property)
        => value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
}
