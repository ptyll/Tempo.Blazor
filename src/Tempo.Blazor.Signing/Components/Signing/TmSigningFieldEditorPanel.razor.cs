using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Edits signing field settings, options, validation, preferences, conditions, and formulas.</summary>
public partial class TmSigningFieldEditorPanel
{
    private SigningField? _field;
    private SigningField? _lastField;
    private string? _lastFieldUuid;
    private bool _showConditions;
    private bool _showFormula;
    private string? _localizationCulture;

    /// <summary>Selected signing field edited by the panel.</summary>
    [Parameter] public SigningField? Field { get; set; }

    /// <summary>Callback invoked whenever the edited field changes.</summary>
    [Parameter] public EventCallback<SigningField> FieldChanged { get; set; }

    /// <summary>All fields in the current signing template, used by condition and formula builders.</summary>
    [Parameter] public IReadOnlyList<SigningField> Fields { get; set; } = [];

    /// <summary>Submitter roles available for assigning the edited field.</summary>
    [Parameter] public IReadOnlyList<SigningSubmitterRole> SubmitterRoles { get; set; } = [];

    /// <summary>Whether the panel should prevent edits.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Culture used to preview localized field labels in nested editors.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Fallback culture used when localized labels are missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Cultures available for editing localized signer-facing text.</summary>
    [Parameter] public IReadOnlyList<string> SupportedCultures { get; set; } = [];

    /// <summary>Whether to show authoring controls for localized signer-facing text.</summary>
    [Parameter] public bool ShowLocalizationEditor { get; set; }

    /// <summary>Callback invoked when a radio or multiple option should be mapped to a document area.</summary>
    [Parameter] public EventCallback<TmSigningFieldOptionAreaMappingEventArgs> OptionAreaMappingRequested { get; set; }

    /// <summary>Callback invoked when the current field should be copied to all pages.</summary>
    [Parameter] public EventCallback<SigningField> CopyToAllPagesRequested { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private bool HasField => _field is not null;

    private bool IsChoiceField => _field?.Type is SigningFieldType.Select or SigningFieldType.Radio or SigningFieldType.Multiple;

    private bool IsTextLikeField => _field?.Type is SigningFieldType.Text or SigningFieldType.Cells;

    private bool IsNumberField => _field?.Type is SigningFieldType.Number or SigningFieldType.Payment;

    private bool IsDateField => _field?.Type is SigningFieldType.Date or SigningFieldType.DateNow;

    private bool IsSignatureField => _field?.Type is SigningFieldType.Signature or SigningFieldType.Initials;

    private bool IsStampField => _field?.Type is SigningFieldType.Stamp;

    private bool CanEditFormula => _field?.Type is SigningFieldType.Number or SigningFieldType.Payment;

    private bool ShouldShowLocalizationEditor => ShowLocalizationEditor && _field is not null && LocalizationCultures.Count > 0;

    private IReadOnlyList<string> LocalizationCultures => GetLocalizationCultures();

    private string ActiveLocalizationCulture => _localizationCulture ?? LocalizationCultures.FirstOrDefault() ?? string.Empty;

    private int MissingLocalizationCount => CountMissingLocalizations();

    /// <summary>
    /// The value shown by the colour input: the field's saved preference, else the concrete colour
    /// of the field's submitter role (an <c>&lt;input type="color"&gt</c> needs a concrete colour —
    /// it cannot render a <c>var()</c> reference).
    /// </summary>
    private string ColorInputValue => _field is null
        ? SigningRolePalette.ColorForOrder(0)
        : _field.Preferences.Color ?? SigningRolePalette.ResolveRoleHexColor(SubmitterRoles, _field.SubmitterUuid) ?? SigningRolePalette.ColorForOrder(0);

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-signing-field-editor-panel" };
            AddClass(classes, ReadOnly, "tm-signing-field-editor-panel--readonly");
            AddClass(classes, !HasField, "tm-signing-field-editor-panel--empty");

            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_lastField, Field))
        {
            var fieldChanged = !string.Equals(_lastFieldUuid, Field?.Uuid, StringComparison.Ordinal);
            _field = Field is null ? null : Clone(Field);
            _lastField = Field;
            _lastFieldUuid = Field?.Uuid;

            if (fieldChanged)
            {
                _showConditions = false;
                _showFormula = false;
            }
        }

        EnsureLocalizationCulture();
    }

    private static void AddClass(List<string> classes, bool condition, string cssClass)
    {
        if (condition)
        {
            classes.Add(cssClass);
        }
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    private Task UpdateFieldAsync(Action<SigningField> update)
    {
        if (ReadOnly || _field is null)
        {
            return Task.CompletedTask;
        }

        update(_field);
        return FieldChanged.InvokeAsync(Clone(_field));
    }

    private Task UpdatePreferencesAsync(Action<SigningFieldPreferences> update)
    {
        return UpdateFieldAsync(field =>
        {
            field.Preferences ??= new SigningFieldPreferences();
            update(field.Preferences);
        });
    }

    private Task UpdateValidationAsync(Action<SigningFieldValidation> update)
    {
        return UpdateFieldAsync(field =>
        {
            field.Validation ??= new SigningFieldValidation();
            update(field.Validation);
        });
    }

    private Task HandleTypeChangedAsync(ChangeEventArgs args)
    {
        return Enum.TryParse<SigningFieldType>(args.Value?.ToString(), out var type)
            ? UpdateFieldAsync(field => ApplyFieldType(field, type))
            : Task.CompletedTask;
    }

    private Task HandleTextPropertyChangedAsync(ChangeEventArgs args, Action<SigningField, string?> update)
    {
        return UpdateFieldAsync(field => update(field, NormalizeOptional(args.Value?.ToString())));
    }

    private Task HandleNameChangedAsync(ChangeEventArgs args)
    {
        var nextName = NormalizeOptional(args.Value?.ToString());
        return UpdateFieldAsync(field =>
        {
            var previousName = field.Name;
            field.Name = nextName;
            SyncLabelWithNameChange(field, previousName, nextName);
        });
    }

    private Task HandleBoolPropertyChangedAsync(ChangeEventArgs args, Action<SigningField, bool> update)
    {
        return UpdateFieldAsync(field => update(field, ToBool(args.Value)));
    }

    private Task HandleSubmitterChangedAsync(ChangeEventArgs args)
    {
        return UpdateFieldAsync(field => field.SubmitterUuid = NormalizeOptional(args.Value?.ToString()));
    }

    private Task AddOptionAsync()
    {
        return UpdateFieldAsync(field =>
        {
            field.Options.Add(new SigningFieldOption
            {
                Value = Loc["TmSigningFieldEditorPanel_NewOption", field.Options.Count + 1]
            });
        });
    }

    private Task UpdateOptionValueAsync(string optionUuid, ChangeEventArgs args)
    {
        return UpdateFieldAsync(field =>
        {
            var option = field.Options.FirstOrDefault(item => string.Equals(item.Uuid, optionUuid, StringComparison.Ordinal));
            if (option is not null)
            {
                option.Value = args.Value?.ToString() ?? string.Empty;
            }
        });
    }

    private Task RemoveOptionAsync(string optionUuid)
    {
        return UpdateFieldAsync(field =>
        {
            field.Options.RemoveAll(option => string.Equals(option.Uuid, optionUuid, StringComparison.Ordinal));
            if (string.Equals(field.DefaultValue?.ToString(), optionUuid, StringComparison.Ordinal))
            {
                field.DefaultValue = null;
            }
        });
    }

    private Task MoveOptionAsync(string optionUuid, int delta)
    {
        return UpdateFieldAsync(field =>
        {
            var index = field.Options.FindIndex(option => string.Equals(option.Uuid, optionUuid, StringComparison.Ordinal));
            var target = index + delta;
            if (index < 0 || target < 0 || target >= field.Options.Count)
            {
                return;
            }

            (field.Options[index], field.Options[target]) = (field.Options[target], field.Options[index]);
        });
    }

    private Task HandleDefaultValueChangedAsync(ChangeEventArgs args)
    {
        return UpdateFieldAsync(field => field.DefaultValue = NormalizeOptional(args.Value?.ToString()));
    }

    private Task HandleLocalizationCultureChangedAsync(ChangeEventArgs args)
    {
        _localizationCulture = NormalizeOptional(args.Value?.ToString());
        return Task.CompletedTask;
    }

    private Task HandleLocalizedTextChangedAsync(ChangeEventArgs args, Func<SigningField, SigningLocalizedText> selectText)
    {
        var culture = ActiveLocalizationCulture;
        if (string.IsNullOrWhiteSpace(culture))
        {
            return Task.CompletedTask;
        }

        return UpdateFieldAsync(field => SetLocalizedText(selectText(field), culture, args.Value?.ToString()));
    }

    private Task HandleLocalizedValidationMessageChangedAsync(ChangeEventArgs args)
    {
        var culture = ActiveLocalizationCulture;
        if (string.IsNullOrWhiteSpace(culture))
        {
            return Task.CompletedTask;
        }

        return UpdateValidationAsync(validation => SetLocalizedText(validation.Messages, culture, args.Value?.ToString()));
    }

    private Task HandleLocalizedOptionLabelChangedAsync(string optionUuid, ChangeEventArgs args)
    {
        var culture = ActiveLocalizationCulture;
        if (string.IsNullOrWhiteSpace(culture))
        {
            return Task.CompletedTask;
        }

        return UpdateFieldAsync(field =>
        {
            var option = field.Options.FirstOrDefault(item => string.Equals(item.Uuid, optionUuid, StringComparison.Ordinal));
            if (option is not null)
            {
                SetLocalizedText(option.Labels, culture, args.Value?.ToString());
            }
        });
    }

    private Task RequestOptionAreaMappingAsync(SigningFieldOption option)
    {
        return _field is null
            ? Task.CompletedTask
            : OptionAreaMappingRequested.InvokeAsync(new TmSigningFieldOptionAreaMappingEventArgs
            {
                Field = Clone(_field),
                Option = Clone(option)
            });
    }

    private Task HandleValidationModeChangedAsync(ChangeEventArgs args)
    {
        var mode = args.Value?.ToString();
        if (string.Equals(mode, "None", StringComparison.Ordinal))
        {
            return UpdateFieldAsync(field => field.Validation = null);
        }

        return UpdateValidationAsync(_ => { });
    }

    private Task HandleValidationValueChangedAsync(ChangeEventArgs args, Action<SigningFieldValidation, string?> update)
    {
        return UpdateValidationAsync(validation => update(validation, NormalizeOptional(args.Value?.ToString())));
    }

    private Task HandleFontSizeChangedAsync(ChangeEventArgs args)
    {
        return UpdatePreferencesAsync(preferences =>
        {
            preferences.FontSize = double.TryParse(args.Value?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        });
    }

    private Task HandlePreferenceTextChangedAsync(ChangeEventArgs args, Action<SigningFieldPreferences, string?> update)
    {
        return UpdatePreferencesAsync(preferences => update(preferences, NormalizeOptional(args.Value?.ToString())));
    }

    private Task HandlePreferenceBoolChangedAsync(ChangeEventArgs args, Action<SigningFieldPreferences, bool?> update)
    {
        return UpdatePreferencesAsync(preferences => update(preferences, ToBool(args.Value)));
    }

    private Task HandleConditionsChangedAsync(IReadOnlyList<SigningFieldCondition> conditions)
    {
        return UpdateFieldAsync(field =>
        {
            field.Conditions = conditions.Select(Clone).ToList();
        });
    }

    private Task HandleFormulaSavedAsync(string formula)
    {
        return UpdateFieldAsync(field =>
        {
            field.Preferences.Formula = formula;
            field.ReadOnly = true;
        });
    }

    private Task CopyToAllPagesAsync()
    {
        return _field is null || ReadOnly
            ? Task.CompletedTask
            : CopyToAllPagesRequested.InvokeAsync(Clone(_field));
    }

    private void ToggleConditions()
    {
        if (!ReadOnly)
        {
            _showConditions = !_showConditions;
        }
    }

    private void ToggleFormula()
    {
        if (!ReadOnly)
        {
            _showFormula = !_showFormula;
        }
    }

    private string GetPanelTitle()
    {
        return string.IsNullOrWhiteSpace(_field?.Name)
            ? Loc["TmSigningFieldEditorPanel_Title"]
            : _field!.Name!;
    }

    private string GetValidationMode()
    {
        return string.IsNullOrWhiteSpace(_field?.Validation?.Pattern) ? "None" : "Regex";
    }

    private IReadOnlyList<string> GetLocalizationCultures()
    {
        var cultures = new List<string>();
        AddCultures(cultures, SupportedCultures);
        AddCulture(cultures, Culture);
        AddCulture(cultures, FallbackCulture);
        return cultures;
    }

    private void EnsureLocalizationCulture()
    {
        var cultures = LocalizationCultures;
        if (cultures.Count == 0)
        {
            _localizationCulture = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(_localizationCulture)
            || !cultures.Any(culture => string.Equals(culture, _localizationCulture, StringComparison.OrdinalIgnoreCase)))
        {
            _localizationCulture = cultures[0];
        }
    }

    private static void AddCultures(List<string> target, IEnumerable<string>? cultures)
    {
        if (cultures is null)
        {
            return;
        }

        foreach (var culture in cultures)
        {
            AddCulture(target, culture);
        }
    }

    private static void AddCulture(List<string> target, string? culture)
    {
        var normalized = NormalizeCulture(culture);
        if (!string.IsNullOrWhiteSpace(normalized)
            && !target.Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            target.Add(normalized);
        }
    }

    private static string? NormalizeCulture(string? culture)
    {
        var trimmed = culture?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(trimmed).Name;
        }
        catch (CultureNotFoundException)
        {
            return trimmed;
        }
    }

    private string? GetLocalizedText(SigningLocalizedText? text)
    {
        if (text is null || string.IsNullOrWhiteSpace(ActiveLocalizationCulture))
        {
            return null;
        }

        return text.Translations.TryGetValue(ActiveLocalizationCulture, out var value)
            ? value
            : null;
    }

    private int CountMissingLocalizations()
    {
        if (_field is null || string.IsNullOrWhiteSpace(ActiveLocalizationCulture))
        {
            return 0;
        }

        var count = 0;
        count += IsMissingLocalization(_field.Labels, _field.Title ?? _field.Name) ? 1 : 0;
        count += IsMissingLocalization(_field.Titles, _field.Title ?? _field.Name) ? 1 : 0;
        count += IsMissingLocalization(_field.Descriptions, _field.Description) ? 1 : 0;
        count += IsMissingLocalization(_field.Placeholders, null) ? 1 : 0;

        if (_field.Validation is not null)
        {
            count += IsMissingLocalization(_field.Validation.Messages, _field.Validation.Message) ? 1 : 0;
        }

        if (IsChoiceField)
        {
            count += _field.Options.Count(option => IsMissingLocalization(option.Labels, option.Value));
        }

        return count;
    }

    private bool IsMissingLocalization(SigningLocalizedText? text, string? legacyFallback)
    {
        if (!HasFallbackText(text, legacyFallback))
        {
            return false;
        }

        return !HasTranslationForActiveCulture(text);
    }

    private static bool HasFallbackText(SigningLocalizedText? text, string? legacyFallback)
    {
        return !string.IsNullOrWhiteSpace(text?.Default)
            || !string.IsNullOrWhiteSpace(legacyFallback);
    }

    private bool HasTranslationForActiveCulture(SigningLocalizedText? text)
    {
        if (text is null || text.Translations.Count == 0)
        {
            return false;
        }

        foreach (var candidate in ExpandCultureCandidates(ActiveLocalizationCulture))
        {
            if (text.Translations.TryGetValue(candidate, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ExpandCultureCandidates(string? culture)
    {
        var normalized = NormalizeCulture(culture);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        yield return normalized;

        var neutral = GetNeutralCultureName(normalized);
        if (!string.IsNullOrWhiteSpace(neutral) && !string.Equals(neutral, normalized, StringComparison.OrdinalIgnoreCase))
        {
            yield return neutral;
        }
    }

    private static string? GetNeutralCultureName(string culture)
    {
        try
        {
            var cultureInfo = CultureInfo.GetCultureInfo(culture);
            return cultureInfo.IsNeutralCulture ? cultureInfo.Name : cultureInfo.Parent.Name;
        }
        catch (CultureNotFoundException)
        {
            var separator = culture.IndexOf('-', StringComparison.Ordinal);
            return separator > 0 ? culture[..separator] : culture;
        }
    }

    private static void SetLocalizedText(SigningLocalizedText text, string culture, string? value)
    {
        var normalized = NormalizeCulture(culture) ?? culture;
        var normalizedValue = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            text.Translations.Remove(normalized);
        }
        else
        {
            text.Translations[normalized] = normalizedValue;
        }
    }

    private void SyncLabelWithNameChange(SigningField field, string? previousName, string? nextName)
    {
        if (string.IsNullOrWhiteSpace(nextName))
        {
            return;
        }

        if (ShouldReplaceNameBackedLabel(field.Labels.Default, previousName))
        {
            field.Labels.Default = nextName;
        }

        var culture = NormalizeCulture(Culture);
        if (!string.IsNullOrWhiteSpace(culture)
            && ShouldReplaceCultureLabel(field.Labels, culture, previousName))
        {
            field.Labels.Translations[culture] = nextName;
        }
    }

    private static bool ShouldReplaceCultureLabel(SigningLocalizedText text, string culture, string? previousName)
    {
        foreach (var candidate in ExpandCultureCandidates(culture))
        {
            if (text.Translations.TryGetValue(candidate, out var value))
            {
                return ShouldReplaceNameBackedLabel(value, previousName);
            }
        }

        return false;
    }

    private static bool ShouldReplaceNameBackedLabel(string? label, string? previousName)
    {
        return string.IsNullOrWhiteSpace(label)
            || (!string.IsNullOrWhiteSpace(previousName)
                && string.Equals(label.Trim(), previousName.Trim(), StringComparison.Ordinal));
    }

    private static string GetCultureLabel(string culture)
    {
        try
        {
            var cultureInfo = CultureInfo.GetCultureInfo(culture);
            return string.Create(CultureInfo.InvariantCulture, $"{cultureInfo.NativeName} ({cultureInfo.Name})");
        }
        catch (CultureNotFoundException)
        {
            return culture;
        }
    }

    private static bool ToBool(object? value)
    {
        return value is bool boolValue && boolValue;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private string GetFieldTypeLabel(SigningFieldType type)
    {
        return type switch
        {
            SigningFieldType.Heading => Loc["TmSigning_Field_Heading"],
            SigningFieldType.Strikethrough => Loc["TmSigning_Field_Strikethrough"],
            SigningFieldType.Text => Loc["TmSigning_Field_Text"],
            SigningFieldType.Signature => Loc["TmSigning_Field_Signature"],
            SigningFieldType.Initials => Loc["TmSigning_Field_Initials"],
            SigningFieldType.Date or SigningFieldType.DateNow => Loc["TmSigning_Field_Date"],
            SigningFieldType.Number => Loc["TmSigning_Field_Number"],
            SigningFieldType.Image => Loc["TmSigning_Field_Image"],
            SigningFieldType.File => Loc["TmSigning_Field_File"],
            SigningFieldType.Select => Loc["TmSigning_Field_Select"],
            SigningFieldType.Checkbox => Loc["TmSigning_Field_Checkbox"],
            SigningFieldType.Multiple => Loc["TmSigning_Field_Multiple"],
            SigningFieldType.Radio => Loc["TmSigning_Field_Radio"],
            SigningFieldType.Cells => Loc["TmSigning_Field_Cells"],
            SigningFieldType.Stamp => Loc["TmSigning_Field_Stamp"],
            SigningFieldType.Phone => Loc["TmSigning_Field_Phone"],
            SigningFieldType.Verification => Loc["TmSigning_Field_Verification"],
            SigningFieldType.Kba => Loc["TmSigning_Field_Kba"],
            SigningFieldType.Payment => Loc["TmSigning_Field_Payment"],
            _ => type.ToString()
        };
    }

    private void ApplyFieldType(SigningField field, SigningFieldType type)
    {
        var previousType = field.Type;
        field.Type = type;

        if (!IsChoiceType(type))
        {
            field.Options.Clear();
        }
        else if (field.Options.Count == 0)
        {
            field.Options.Add(new SigningFieldOption { Value = Loc["TmSigningFieldEditorPanel_NewOption", 1] });
            field.Options.Add(new SigningFieldOption { Value = Loc["TmSigningFieldEditorPanel_NewOption", 2] });
        }

        if ((IsChoiceType(previousType) && !IsChoiceType(type))
            || (IsImageLikeType(type) && !IsImageSource(field.DefaultValue?.ToString())))
        {
            field.DefaultValue = null;
        }
    }

    private static bool IsChoiceType(SigningFieldType type)
    {
        return type is SigningFieldType.Select or SigningFieldType.Radio or SigningFieldType.Multiple;
    }

    private static bool IsImageLikeType(SigningFieldType type)
    {
        return type is SigningFieldType.Signature or SigningFieldType.Initials or SigningFieldType.Image;
    }

    private static bool IsImageSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith("./", StringComparison.Ordinal)
            || value.StartsWith("../", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";
    }

    private static SigningField Clone(SigningField field)
    {
        return new SigningField
        {
            Uuid = field.Uuid,
            SubmitterUuid = field.SubmitterUuid,
            Name = field.Name,
            Labels = Clone(field.Labels),
            Title = field.Title,
            Titles = Clone(field.Titles),
            Description = field.Description,
            Descriptions = Clone(field.Descriptions),
            Placeholders = Clone(field.Placeholders),
            Type = field.Type,
            Required = field.Required,
            ReadOnly = field.ReadOnly,
            Prefillable = field.Prefillable,
            DefaultValue = field.DefaultValue,
            Preferences = Clone(field.Preferences),
            Validation = field.Validation is null ? null : Clone(field.Validation),
            Conditions = field.Conditions.Select(Clone).ToList(),
            Options = field.Options.Select(Clone).ToList(),
            Areas = field.Areas.Select(Clone).ToList()
        };
    }

    private static SigningFieldPreferences Clone(SigningFieldPreferences preferences)
    {
        return new SigningFieldPreferences
        {
            Color = preferences.Color,
            Align = preferences.Align,
            Format = preferences.Format,
            FontFamily = preferences.FontFamily,
            FontSize = preferences.FontSize,
            WithSignatureId = preferences.WithSignatureId,
            WithLogo = preferences.WithLogo,
            ReasonFieldUuid = preferences.ReasonFieldUuid,
            Formula = preferences.Formula,
            Currency = preferences.Currency,
            Price = preferences.Price,
            PriceId = preferences.PriceId,
            PaymentLinkId = preferences.PaymentLinkId,
            AdditionalSettings = new Dictionary<string, object?>(preferences.AdditionalSettings)
        };
    }

    private static SigningFieldValidation Clone(SigningFieldValidation validation)
    {
        return new SigningFieldValidation
        {
            Pattern = validation.Pattern,
            Message = validation.Message,
            Messages = Clone(validation.Messages),
            Min = validation.Min,
            Max = validation.Max,
            Step = validation.Step
        };
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

    private static SigningFieldOption Clone(SigningFieldOption option)
    {
        return new SigningFieldOption
        {
            Uuid = option.Uuid,
            Labels = Clone(option.Labels),
            Value = option.Value
        };
    }

    private static SigningLocalizedText Clone(SigningLocalizedText text)
    {
        return new SigningLocalizedText
        {
            Default = text.Default,
            Translations = new Dictionary<string, string>(text.Translations, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static SigningFieldArea Clone(SigningFieldArea area)
    {
        return new SigningFieldArea
        {
            Uuid = area.Uuid,
            AttachmentUuid = area.AttachmentUuid,
            Page = area.Page,
            X = area.X,
            Y = area.Y,
            Width = area.Width,
            Height = area.Height,
            OptionUuid = area.OptionUuid
        };
    }
}
