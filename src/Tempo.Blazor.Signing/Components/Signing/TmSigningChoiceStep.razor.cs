using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Renders select, radio, multiple-choice, and checkbox signing steps on the core input components
/// (Signing redesign phase 4, SI1): <see cref="TmSelect"/>, <see cref="TmRadioGroup"/> /
/// <see cref="TmRadio"/> and <see cref="TmCheckbox"/> option rows. The phase-2 group contract is
/// kept: the radio and checkbox variants are real <c>fieldset</c>s named by a visually hidden
/// <c>legend</c>, with a per-instance radio <c>name</c>.
/// <para>
/// Signing redesign phase 5 (SI1): on a MOBILE layout, choice fields with at most five options
/// present as radio CARDS (the whole card is the clickable row) — a select-typed field swaps the
/// system picker for the card set, a radio-typed field keeps its group with the card modifier.
/// Six or more options keep the system <see cref="TmSelect"/> at every width.
/// </para>
/// </summary>
public partial class TmSigningChoiceStep
{
    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";
    private readonly string _hintId = $"tm-signing-choice-hint-{Guid.NewGuid():N}";
    private readonly string _radioName = $"tm-signing-choice-{Guid.NewGuid():N}";
    private string? _validationMessage;

    /// <summary>Primary signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new() { Type = SigningFieldType.Select };

    /// <summary>Optional checkbox fields rendered as one grouped checkbox step.</summary>
    [Parameter] public IReadOnlyList<SigningField> Fields { get; set; } = [];

    /// <summary>Current value. Select/radio use string, multiple and grouped checkboxes use string arrays, checkbox uses bool.</summary>
    [Parameter] public object? Value { get; set; }

    /// <summary>Callback invoked when the value changes.</summary>
    [Parameter] public EventCallback<object?> ValueChanged { get; set; }

    /// <summary>Short text describing where the field appears in the document.</summary>
    [Parameter] public string? AppearsOn { get; set; }

    /// <summary>Whether the input controls are disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// The resolved layout this step renders in (Signing redesign phase 5). <see cref="TmLayoutMode.Auto"/>
    /// does NOT measure anything — it renders today's desktop markup; the runner passes its own
    /// resolved classification down (never Auto), and a standalone host that wants the narrow
    /// radio-card presentation without a runner passes <see cref="TmLayoutMode.Mobile"/> explicitly.
    /// A forced <see cref="TmLayoutMode.Mobile"/> presents up to five select/radio options as radio
    /// cards (SI1), so a test can render that presentation without a DOM.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>Whether a single checkbox should show a generic instruction instead of the field label.</summary>
    [Parameter] public bool AnonymousCheckbox { get; set; }

    /// <summary>Culture used to resolve localized field and option text.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Fallback culture used when localized text is missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Additional CSS classes for the shell element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the shell element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The runner's step validation, cascaded by <see cref="TmSigningFormRunner"/> (absent for standalone steps).</summary>
    [CascadingParameter(Name = "TmSigningRunnerValidation")]
    private string? RunnerValidationMessage { get; set; }

    private IReadOnlyList<SigningField> CheckboxFields => Fields.Count > 0 ? Fields : [Field];

    private bool IsCheckboxGroup => Field.Type == SigningFieldType.Checkbox && CheckboxFields.Count > 1;

    private const int MaxRadioCardOptions = 5;

    /// <summary>
    /// SI1 (phase 5): up to five select/radio options present as radio CARDS on a mobile layout —
    /// the whole card is the clickable row and every option fits without scrolling. More options
    /// keep the system picker (<see cref="TmSelect"/>), whose native affordance scales.
    /// </summary>
    private bool UseRadioCards => LayoutMode == TmLayoutMode.Mobile
        && Field.Type is SigningFieldType.Select or SigningFieldType.Radio
        && Field.Options.Count > 0
        && Field.Options.Count <= MaxRadioCardOptions;

    /// <summary>
    /// An OPTIONAL select rendered as cards keeps a way to CLEAR the choice (phase 5 review):
    /// the system picker had a selectable "empty" placeholder, so the card set gains a localized
    /// "None" card first, which sends the same empty value. Required fields keep only their real
    /// options — the required validation explains the missing choice instead.
    /// </summary>
    private bool ShowNoneCard => UseRadioCards
        && Field.Type == SigningFieldType.Select
        && !Field.Required;

    /// <summary>The TmRadioGroup class: the option-row skin plus the phase-5 card modifier.</summary>
    private string RadioGroupClass => UseRadioCards
        ? "tm-signing-choice-step__options tm-signing-choice-step__options--cards"
        : "tm-signing-choice-step__options";

    /// <summary>The TmRadioGroup options: the localized "None" card (empty value) first when the
    /// select is optional, then the field's own options (stable uuid values, localized labels).</summary>
    private List<RadioOption<string>> RadioOptions
    {
        get
        {
            var options = Field.Options
                .Select(option => new RadioOption<string>(GetOptionLabel(option), option.Uuid))
                .ToList();
            if (ShowNoneCard)
            {
                options.Insert(0, new RadioOption<string>(Loc["TmSigningChoiceStep_None"], string.Empty));
            }

            return options;
        }
    }

    private string SingleValue => Value?.ToString() ?? string.Empty;

    private bool BoolValue => Value is bool boolValue && boolValue;

    private HashSet<string> SelectedValues => Value switch
    {
        IEnumerable<string> strings => strings.ToHashSet(StringComparer.Ordinal),
        string text when !string.IsNullOrWhiteSpace(text) => [text],
        _ => []
    };

    /// <summary>The field label the TmSelect carries as its accessible name (it has no splattable
    /// name on the select element itself).</summary>
    private string DisplayFieldLabel => GetFieldLabel(Field);

    /// <summary>The TmSelect options: stable option uuids as values, localized labels as text.</summary>
    private List<SelectOption<string>> SelectOptions => Field.Options
        .Select(option => new SelectOption<string>(option.Uuid, GetOptionLabel(option)))
        .ToList();

    private string ShellClass => string.Join(" ", new[] { "tm-signing-choice-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private bool HasDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    /// <summary>The message the shell renders as the step's single alert (own message first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage)
        ? _validationMessage
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    private string InvalidText => BoolText(HasValidationMessage);

    /// <summary>
    /// The ids a control of this step references — per-instance, and the SAME instances the shell
    /// renders (they are passed to the shell as parameters), so every reference resolves inside
    /// the rendered DOM (Signing redesign phase 2).
    /// </summary>
    private string? DescribedBy => JoinIds(HasDescription ? _descriptionId : null, HasValidationMessage ? _validationId : null);

    /// <summary>The describedby list for the checkbox-group fieldset: hint + description + validation.</summary>
    private string? GroupDescribedBy => JoinIds(
        IsCheckboxGroup ? _hintId : null,
        HasDescription ? _descriptionId : null,
        HasValidationMessage ? _validationId : null);

    private static string? JoinIds(params string?[] ids)
    {
        var parts = ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        return parts.Length == 0 ? null : string.Join(" ", parts);
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    /// <summary>
    /// The attributes the select variant splats onto the NATIVE <c>&lt;select&gt;</c> through the
    /// core <c>TmSelect.SelectAttributes</c> (phase 4 review): the step title as the labelledby,
    /// the description / validation ids as the description, and the invalid state while the step
    /// shows a message.
    /// </summary>
    private Dictionary<string, object> BuildSelectAttributes()
    {
        var attributes = new Dictionary<string, object>
        {
            ["aria-labelledby"] = _titleId,
            ["aria-invalid"] = InvalidText
        };

        if (DescribedBy is { } describedBy)
        {
            attributes["aria-describedby"] = describedBy;
        }

        return attributes;
    }

    /// <summary>The attributes the SINGLE checkbox splats onto its native input (invalid state + description).</summary>
    private Dictionary<string, object> BuildSingleCheckboxAttributes()
    {
        var attributes = new Dictionary<string, object>
        {
            ["aria-invalid"] = InvalidText
        };

        if (DescribedBy is { } describedBy)
        {
            attributes["aria-describedby"] = describedBy;
        }

        return attributes;
    }

    /// <summary>The visually hidden legend naming the fieldset group.</summary>
    private string GroupLegendText => GetFieldLabel(Field);

    /// <summary>The TmSelect change (nullable — the selectable placeholder maps to null).</summary>
    private Task HandleSingleValueChangedAsync(string? value)
    {
        var effective = value ?? string.Empty;
        _validationMessage = Field.Required && string.IsNullOrWhiteSpace(effective)
            ? RequiredChoiceMessage
            : null;
        return ValueChanged.InvokeAsync(effective);
    }

    /// <summary>The TmRadioGroup change (the selected option's stable uuid; the group's options are never null).</summary>
    private Task HandleRadioValueChangedAsync(string value) => HandleSingleValueChangedAsync(value);

    private async Task HandleMultipleValueChangedAsync(string optionUuid, bool isChecked)
    {
        var values = SelectedValues;
        if (isChecked)
        {
            values.Add(optionUuid);
        }
        else
        {
            values.Remove(optionUuid);
        }

        _validationMessage = Field.Required && values.Count == 0 ? RequiredChoiceMessage : null;
        await ValueChanged.InvokeAsync(values.ToArray());
    }

    private async Task HandleCheckboxChangedAsync(bool value)
    {
        _validationMessage = Field.Required && !value ? RequiredChoiceMessage : null;
        await ValueChanged.InvokeAsync(value);
    }

    private async Task HandleGroupCheckboxChangedAsync(SigningField checkboxField, bool isChecked)
    {
        var values = SelectedValues;
        if (isChecked)
        {
            values.Add(checkboxField.Uuid);
        }
        else
        {
            values.Remove(checkboxField.Uuid);
        }

        _validationMessage = CheckboxFields.Any(item => item.Required) && values.Count == 0 ? RequiredChoiceMessage : null;
        await ValueChanged.InvokeAsync(values.ToArray());
    }

    private bool IsGroupCheckboxChecked(SigningField field) => SelectedValues.Contains(field.Uuid);

    private string RequiredChoiceMessage => SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_RequiredChoice"]);

    private string GetOptionLabel(SigningFieldOption option)
    {
        return SigningTextResolver.OptionLabel(option, Culture, FallbackCulture);
    }

    private string GetFieldLabel(SigningField field)
    {
        return SigningTextResolver.FieldLabel(field, Culture, FallbackCulture, Loc);
    }

    /// <summary>The item field's description for a grouped checkbox row's help text (empty when the field carries none).</summary>
    private string GetFieldDescription(SigningField field)
    {
        return SigningTextResolver.FieldDescription(field, Culture, FallbackCulture);
    }
}
